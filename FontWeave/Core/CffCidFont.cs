using FontWeave.Containers;
using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace FontWeave.Core;

/// <summary>Finite static CID-CFF1 structural model. Programs stay borrowed from the
/// source face; this is not a Type 2 interpreter, sanitizer or glyph closure engine.</summary>
public sealed class CffCidFont
{
    private const uint Tag = 0x43464620;
    private const int Ros = 0x0c1e, FdArray = 0x0c24, FdSelect = 0x0c25;
    private const int MaxBytes = 64 * 1024 * 1024;
    private readonly int _ordinal;
    private readonly Index _names, _strings, _global, _chars;
    private readonly Dict _top;
    private readonly Dict[] _fds, _private;
    private readonly Index[] _local;
    private readonly ushort[] _cids;
    private readonly byte[] _fdBindings;

    private CffCidFont(FontFaceRef face, int ordinal, Index names, Index strings,
        Index global, Index chars, Dict top, Dict[] fds, Dict[] privateDicts,
        Index[] local, ushort[] cids, byte[] bindings)
    {
        SourceFace = face; _ordinal = ordinal; _names = names; _strings = strings;
        _global = global; _chars = chars; _top = top; _fds = fds;
        _private = privateDicts; _local = local; _cids = cids; _fdBindings = bindings;
    }

    public FontFaceRef SourceFace { get; }
    public int GlyphCount => _chars.Count;
    public int FontDictionaryCount => _fds.Length;

    public static ReadStatus TryRead(FontFaceRef face, out CffCidFont? font,
        out FontDiagnostic diagnostic) => TryRead(face, out font, out diagnostic, new BuildBudget());

    internal static ReadStatus TryRead(FontFaceRef face, out CffCidFont? font,
        out FontDiagnostic diagnostic, BuildBudget budget)
    {
        font = null;
        ReadStatus status = face.TryOpenDirectory(out var directory, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (!budget.Input(directory.SourceByteLength, face.Location, out diagnostic)
            || !budget.Work((long)directory.DeclaredCount * directory.DeclaredCount + directory.DeclaredCount,
                face.Location, out diagnostic)) return ReadStatus.ResourceLimit;
        status = directory.Validate(out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (directory.Version != 0x4f54544f)
            return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedVersion,
                face.Location, out diagnostic, directory.Version, 0x4f54544f);
        status = directory.TryFindUniqueTable(Tag, out int ordinal, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = directory.TryGetTableBytes(ordinal, out var bytes, out diagnostic);
        if (status != ReadStatus.Done) return status;
        var reader = new Parser(bytes, directory.TableLocation(ordinal), budget);
        bool ok = reader.Read(face, ordinal, out font);
        diagnostic = reader.Diagnostic;
        return ok ? ReadStatus.Done : reader.Status;
    }

    /// <summary>Compile one frozen output-to-input order. Keeps CID values, all FD
    /// numbers and every local/global subroutine byte-for-byte. No fallback to raw copy.</summary>
    public ReadStatus TryCompile(GlyphRemap map, out byte[]? bytes, out FontDiagnostic diagnostic) =>
        TryCompile(map, out bytes, out diagnostic, new BuildBudget());

    internal ReadStatus TryCompile(GlyphRemap map, out byte[]? bytes, out FontDiagnostic diagnostic,
        BuildBudget budget)
    {
        ArgumentNullException.ThrowIfNull(map);
        bytes = null;
        diagnostic = default;
        if (map.SourceFace != SourceFace)
            return ReadWire.Fail(ReadStatus.InvalidSource, DiagnosticCode.WrongSourceFace,
                SourceFace.Location with { Basis = OffsetBasis.None, Offset = 0, Field = DataField.GlyphId },
                out diagnostic, (ulong)SourceFace.SourceId, (ulong)map.SourceFace.SourceId);
        if (map.InputGlyphCount != GlyphCount)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                CffLocation(DataField.Count) with { Basis = OffsetBasis.None },
                out diagnostic, (ulong)map.InputGlyphCount, (ulong)GlyphCount);
        ReadStatus status = SourceFace.TryOpenDirectory(out var directory, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = directory.TryGetTableBytes(_ordinal, out var source, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (!budget.Input(directory.SourceByteLength, SourceFace.Location, out diagnostic))
            return ReadStatus.ResourceLimit;

        int count = map.OutputGlyphCount;
        // Charge before allocation/iteration. Work is deterministic admission units,
        // not CPU instructions; program copying is charged after exact sizing below.
        long indexItems = _names.Count + _strings.Count + _global.Count + (long)count
            + _local.Sum(index => (long)index.Count);
        if (!budget.Work(4L * count + 8L * indexItems + 16L * _fds.Length, CffLocation(DataField.Payload), out diagnostic)
            || !budget.Buffer(11L * count + 28L * _fds.Length, CffLocation(DataField.Length), out diagnostic))
            return ReadStatus.ResourceLimit;
        var selected = new Range[count];
        byte[] charset = new byte[1 + 2 * (count - 1)]; // format 0; .notdef is implicit
        byte[] bindings = new byte[1 + count]; // format 0, no range sentinel ambiguity
        for (int i = 0; i < count; i++)
        {
            status = map.TryGetInputGlyph(new((ushort)i), out var input, out diagnostic);
            if (status != ReadStatus.Done) return status;
            int gid = input.Value;
            selected[i] = _chars.Items[gid];
            if (i > 0) BinaryPrimitives.WriteUInt16BigEndian(charset.AsSpan(1 + 2 * (i - 1)), _cids[gid]);
            bindings[i + 1] = _fdBindings[gid];
        }

        // Five-byte DICT offset operands and offSize=4 INDEXes make every block's
        // size independent of its address. Exactly one sizing pass + one emission pass.
        byte[][] privateBytes = new byte[_private.Length][];
        for (int fd = 0; fd < _private.Length; fd++)
        {
            Dict pd = _private[fd];
            Dictionary<int, int[]> fixes = pd.Has(19)
                ? new() { [19] = [pd.LengthWith(new() { [19] = [0] })] } : new();
            int privateLength = pd.LengthWith(fixes);
            if (!budget.Buffer(privateLength, CffLocation(DataField.Length), out diagnostic)
                || !budget.Work(privateLength, CffLocation(DataField.Payload), out diagnostic))
                return ReadStatus.ResourceLimit;
            privateBytes[fd] = pd.Emit(source, fixes);
        }
        var topFixups = new Dictionary<int, int[]> { [15] = [0], [17] = [0], [FdArray] = [0], [FdSelect] = [0] };
        int topLength = _top.LengthWith(topFixups);
        var fdFixups = new Dictionary<int, int[]>[_fds.Length];
        int[] fdLengths = new int[_fds.Length];
        for (int fd = 0; fd < _fds.Length; fd++)
        {
            fdFixups[fd] = new() { [18] = [privateBytes[fd].Length, 0] };
            fdLengths[fd] = _fds[fd].LengthWith(fdFixups[fd]);
        }
        long cursor = 4L + IndexLength(_names.Items) + IndexLength([new(0, topLength)])
            + IndexLength(_strings.Items) + IndexLength(_global.Items);
        topFixups[15][0] = (int)cursor;
        cursor += charset.Length;
        topFixups[FdSelect][0] = (int)cursor;
        cursor += bindings.Length;
        topFixups[FdArray][0] = (int)cursor;
        cursor += IndexLength(fdLengths.Select(n => new Range(0, n)).ToArray());
        for (int fd = 0; fd < _fds.Length; fd++)
        {
            if (cursor > MaxBytes) return TooLarge(cursor, out diagnostic);
            fdFixups[fd][18][1] = (int)cursor;
            cursor += privateBytes[fd].Length;
            if (_private[fd].Has(19)) cursor += IndexLength(_local[fd].Items);
        }
        if (cursor > MaxBytes) return TooLarge(cursor, out diagnostic);
        topFixups[17][0] = (int)cursor;
        cursor += IndexLength(selected);
        if (cursor > MaxBytes) return TooLarge(cursor, out diagnostic);
        if (!budget.Output(cursor, CffLocation(DataField.Length), out diagnostic)
            || !budget.Buffer(cursor + topLength + fdLengths.Sum(n => (long)n) + 4L * indexItems,
                CffLocation(DataField.Length), out diagnostic)
            || !budget.Work(2L * cursor, CffLocation(DataField.Payload), out diagnostic))
            return ReadStatus.ResourceLimit;

        byte[] output = new byte[(int)cursor];
        output[0] = 1; output[2] = 4; output[3] = 4;
        int at = 4;
        EmitIndex(output, ref at, source, _names.Items);
        EmitOwnedIndex(output, ref at, [_top.Emit(source, topFixups)]);
        EmitIndex(output, ref at, source, _strings.Items);
        EmitIndex(output, ref at, source, _global.Items);
        Copy(output, ref at, charset);
        Copy(output, ref at, bindings);
        byte[][] fdBytes = new byte[_fds.Length][];
        for (int fd = 0; fd < _fds.Length; fd++) fdBytes[fd] = _fds[fd].Emit(source, fdFixups[fd]);
        EmitOwnedIndex(output, ref at, fdBytes);
        for (int fd = 0; fd < _fds.Length; fd++)
        {
            Copy(output, ref at, privateBytes[fd]);
            if (_private[fd].Has(19)) EmitIndex(output, ref at, source, _local[fd].Items);
        }
        EmitIndex(output, ref at, source, selected);
        if (at != output.Length) throw new InvalidOperationException("CFF sizing/emission disagreement.");
        budget.RecordCompiledTable(output.Length);
        bytes = output;
        return ReadStatus.Done;
    }

    private ReadStatus TooLarge(long length, out FontDiagnostic diagnostic) =>
        ReadWire.Fail(ReadStatus.ResourceLimit, DiagnosticCode.BudgetExceeded,
            CffLocation(DataField.Length),
            out diagnostic, (ulong)length, MaxBytes);

    private DataLocation CffLocation(DataField field) => new(SourceFace.SourceId,
        SourceFace.FaceIndex, Tag, -1, field, OffsetBasis.Table, 0, _ordinal);

    private readonly record struct Range(int Start, int Length);
    private sealed record Index(Range[] Items, int End)
    {
        internal int Count => Items.Length;
        internal static Index Empty { get; } = new([], 0);
    }
    private sealed record Entry(int Op, Range Raw, int?[] Operands);
    private sealed record Dict(Entry[] Entries)
    {
        internal bool Has(int op) => Entries.Any(e => e.Op == op);
        internal int?[]? Args(int op) => Entries.FirstOrDefault(e => e.Op == op)?.Operands;
        internal int LengthWith(Dictionary<int, int[]> changes) => Entries.Sum(e =>
            changes.TryGetValue(e.Op, out var values) ? 5 * values.Length + (e.Op > 255 ? 2 : 1) : e.Raw.Length);
        internal byte[] Emit(ReadOnlySpan<byte> source, Dictionary<int, int[]> changes)
        {
            byte[] output = new byte[LengthWith(changes)];
            int at = 0;
            foreach (Entry entry in Entries)
            {
                if (!changes.TryGetValue(entry.Op, out var values))
                {
                    Copy(output, ref at, source.Slice(entry.Raw.Start, entry.Raw.Length));
                    continue;
                }
                foreach (int value in values)
                {
                    output[at++] = 29;
                    BinaryPrimitives.WriteInt32BigEndian(output.AsSpan(at), value);
                    at += 4;
                }
                if (entry.Op > 255) output[at++] = 12;
                output[at++] = (byte)(entry.Op & 255);
            }
            return output;
        }
    }

    private static long IndexLength(Range[] items) => items.Length == 0 ? 2 :
        3L + 4L * (items.Length + 1) + items.Sum(r => (long)r.Length);
    private static void Copy(byte[] output, ref int at, ReadOnlySpan<byte> data)
    {
        data.CopyTo(output.AsSpan(at)); at += data.Length;
    }
    private static void EmitHeader(byte[] output, ref int at, int[] lengths)
    {
        BinaryPrimitives.WriteUInt16BigEndian(output.AsSpan(at), (ushort)lengths.Length); at += 2;
        if (lengths.Length == 0) return;
        output[at++] = 4;
        uint offset = 1;
        BinaryPrimitives.WriteUInt32BigEndian(output.AsSpan(at), offset); at += 4;
        foreach (int length in lengths)
        {
            offset += (uint)length;
            BinaryPrimitives.WriteUInt32BigEndian(output.AsSpan(at), offset); at += 4;
        }
    }
    private static void EmitIndex(byte[] output, ref int at, ReadOnlySpan<byte> source, Range[] items)
    {
        EmitHeader(output, ref at, items.Select(r => r.Length).ToArray());
        foreach (var item in items) Copy(output, ref at, source.Slice(item.Start, item.Length));
    }
    private static void EmitOwnedIndex(byte[] output, ref int at, byte[][] items)
    {
        EmitHeader(output, ref at, items.Select(b => b.Length).ToArray());
        foreach (var item in items) Copy(output, ref at, item);
    }

    private ref struct Parser
    {
        private readonly ReadOnlySpan<byte> _bytes;
        private readonly DataLocation _location;
        private readonly BuildBudget _budget;
        private long _work;
        internal ReadStatus Status { get; private set; }
        internal FontDiagnostic Diagnostic { get; private set; }
        internal Parser(ReadOnlySpan<byte> bytes, DataLocation location, BuildBudget budget)
        {
            _bytes = bytes; _location = location; _budget = budget; _work = 0;
            Status = ReadStatus.Done; Diagnostic = default;
        }
        private bool Fail(int at, DataField field, ulong detail = 0, ulong expected = 0,
            ReadStatus status = ReadStatus.InvalidFormat, DiagnosticCode code = DiagnosticCode.InvalidValue)
        {
            Status = status;
            Diagnostic = new(code, _location.At(field, (ulong)Math.Max(0, at)), detail, expected);
            return false;
        }
        private bool Bounds(long at, long length)
        {
            if (at >= 0 && at <= _bytes.Length && length >= 0 && length <= _bytes.Length - at) return true;
            return Fail((int)Math.Clamp(at, 0, int.MaxValue), DataField.Offset,
                (ulong)Math.Max(0, at + length), (ulong)_bytes.Length,
                ReadStatus.InvalidOffset, DiagnosticCode.RangeOutsideSource);
        }
        private bool Work(int amount)
        {
            _work += amount;
            if (_work > MaxBytes) return Fail(0, DataField.Length, (ulong)_work, MaxBytes,
                ReadStatus.ResourceLimit, DiagnosticCode.BudgetExceeded);
            if (_budget.Work(amount, _location, out var diagnostic)) return true;
            Status = ReadStatus.ResourceLimit; Diagnostic = diagnostic; return false;
        }
        private bool Buffer(long amount)
        {
            if (_budget.Buffer(amount, _location, out var diagnostic)) return true;
            Status = ReadStatus.ResourceLimit; Diagnostic = diagnostic; return false;
        }
        internal bool Read(FontFaceRef face, int ordinal, out CffCidFont? font)
        {
            font = null;
            if (_bytes.Length > MaxBytes) return Fail(0, DataField.Length, (ulong)_bytes.Length, MaxBytes,
                ReadStatus.ResourceLimit, DiagnosticCode.BudgetExceeded);
            if (!Bounds(0, 4)) return false;
            if (_bytes[0] != 1 || _bytes[1] != 0)
                return Fail(0, DataField.Version, _bytes[0], 1, ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedVersion);
            if (_bytes[2] < 4 || _bytes[3] is < 1 or > 4) return Fail(2, DataField.Length, _bytes[2], 4);
            if (!ReadIndex(_bytes[2], out var names) || !ReadIndex(names.End, out var tops)
                || !ReadIndex(tops.End, out var strings) || !ReadIndex(strings.End, out var global)) return false;
            if (names.Count != 1 || tops.Count != 1) return Fail(_bytes[2], DataField.Count, (ulong)tops.Count, 1);
            if (!ReadDict(tops.Items[0], 0, out var top)) return false;
            if (!Integers(top, Ros, 3, out var ros)
                || !Integer(top, 15, out int charsetAt) || !Integer(top, 17, out int charsAt)
                || !Integer(top, FdArray, out int fdsAt) || !Integer(top, FdSelect, out int selectAt)) return false;
            if (ros.Any(n => n < 0) || ros[0] >= 391 + strings.Count || ros[1] >= 391 + strings.Count)
                return Fail(tops.Items[0].Start, DataField.Count, (ulong)strings.Count);
            if (top.Has(16) || top.Has(18) || !DefaultInteger(top, 0x0c06, 2, 2)
                || !DefaultInteger(top, 0x0c21, 0, 0))
            {
                // Integer extraction can fail its budget before a value is available.
                // Preserve that failure rather than relabeling it as an unsupported value.
                if (Status != ReadStatus.Done) return false;
                return Fail(tops.Items[0].Start, DataField.Payload, 0, 0, ReadStatus.UnsupportedFormat);
            }
            if (!ReadIndex(charsAt, out var chars) || !ReadIndex(fdsAt, out var fdsIndex)) return false;
            if (chars.Count == 0 || fdsIndex.Count is < 1 or > 256)
                return Fail(charsAt, DataField.Count, (ulong)chars.Count);
            if (!ReadCharset(charsetAt, chars.Count, out var cids)
                || !ReadBindings(selectAt, chars.Count, fdsIndex.Count, out var bindings)) return false;
            int cidCount = 8720;
            if (top.Has(0x0c22) && !Integer(top, 0x0c22, out cidCount)) return false;
            if (cidCount < 1 || cidCount > 65536 || cids.Any(cid => cid >= cidCount))
                return Fail(charsetAt, DataField.Count, (ulong)Math.Max(0, cidCount));
            if (!Buffer(3L * IntPtr.Size * fdsIndex.Count)) return false;
            Dict[] fds = new Dict[fdsIndex.Count], privateDicts = new Dict[fdsIndex.Count];
            Index[] local = new Index[fdsIndex.Count];
            for (int fd = 0; fd < fds.Length; fd++)
            {
                if (!ReadDict(fdsIndex.Items[fd], 1, out fds[fd])
                    || !Integers(fds[fd], 18, 2, out var priv)) return false;
                if (!Bounds(priv[1], priv[0])) return false;
                if (!ReadDict(new(priv[1], priv[0]), 2, out privateDicts[fd])) return false;
                local[fd] = Index.Empty;
                if (privateDicts[fd].Has(19))
                {
                    if (!Integer(privateDicts[fd], 19, out int subrs)) return false;
                    if (subrs < priv[0]) return Fail(priv[1], DataField.Offset, (ulong)Math.Max(0, subrs), (ulong)priv[0]);
                    long localAt = (long)priv[1] + subrs;
                    if (!Bounds(localAt, 2) || !ReadIndex((int)localAt, out local[fd])) return false;
                }
            }
            font = new(face, ordinal, names, strings, global, chars, top, fds, privateDicts, local, cids, bindings);
            return true;
        }

        private bool ReadIndex(int at, out Index index)
        {
            index = Index.Empty;
            if (!Bounds(at, 2) || !Work(2)) return false;
            int count = ReadWire.U16(_bytes, at);
            if (count == 0) { index = new([], at + 2); return true; }
            if (!Bounds(at + 2L, 1)) return false;
            int size = _bytes[at + 2];
            if (size is < 1 or > 4) return Fail(at + 2, DataField.Length, (ulong)size, 4);
            int offsetBytes = (count + 1) * size;
            if (!Bounds(at + 3L, offsetBytes) || !Work(offsetBytes)) return false;
            long dataAt = at + 3L + offsetBytes;
            uint previous = ReadOffset(at + 3, size);
            if (previous != 1) return Fail(at + 3, DataField.Offset, previous, 1);
            if (!Buffer(8L * count)) return false;
            Range[] items = new Range[count];
            for (int i = 0; i < count; i++)
            {
                uint next = ReadOffset(at + 3 + (i + 1) * size, size);
                if (next < previous) return Fail(at + 3 + (i + 1) * size, DataField.Offset, next, previous);
                if (!Bounds(dataAt + previous - 1, (long)next - previous)) return false;
                items[i] = new((int)(dataAt + previous - 1), (int)(next - previous));
                previous = next;
            }
            index = new(items, (int)(dataAt + previous - 1));
            return true;
        }
        private uint ReadOffset(int at, int size)
        {
            uint value = 0;
            for (int i = 0; i < size; i++) value = (value << 8) | _bytes[at + i];
            return value;
        }

        // Known non-address fields are retained byte-for-byte; unsupported operators
        // are rejected, not assumed position-independent. kind: top / FD / Private.
        private bool ReadDict(Range range, int kind, out Dict dict)
        {
            dict = new([]);
            if (!Bounds(range.Start, range.Length) || !Work(range.Length)) return false;
            var entries = new List<Entry>();
            var args = new List<int?>();
            var seen = new HashSet<int>();
            int start = range.Start, end = start + range.Length, at = start;
            while (at < end)
            {
                byte b = _bytes[at++];
                int? number;
                if (b >= 32 && b <= 246) number = b - 139;
                else if (b is >= 247 and <= 254)
                {
                    if (at >= end) return Fail(at, DataField.Payload);
                    number = b <= 250 ? (b - 247) * 256 + _bytes[at++] + 108
                        : -(b - 251) * 256 - _bytes[at++] - 108;
                }
                else if (b is 28 or 29)
                {
                    int size = b == 28 ? 2 : 4;
                    if (size > end - at) return Fail(at, DataField.Payload);
                    number = size == 2 ? BinaryPrimitives.ReadInt16BigEndian(_bytes.Slice(at, 2))
                        : BinaryPrimitives.ReadInt32BigEndian(_bytes.Slice(at, 4));
                    at += size;
                }
                else if (b == 30)
                {
                    if (!ReadReal(ref at, end)) return false;
                    number = null; // raw real token retained; never accepted as an address
                }
                else
                {
                    int op = b;
                    if (b == 12)
                    {
                        if (at >= end) return Fail(at, DataField.Payload);
                        op = 0x0c00 | _bytes[at++];
                    }
                    int arity = Arity(kind, op);
                    if (arity == -2) return Fail(at - 1, DataField.Payload, (ulong)op, 0, ReadStatus.UnsupportedFormat);
                    if (!seen.Add(op) || args.Count == 0 || (arity >= 0 && args.Count != arity))
                        return Fail(start, DataField.Count, (ulong)args.Count, (ulong)Math.Max(0, arity));
                    if (!Buffer(8L * args.Count)) return false;
                    entries.Add(new(op, new(start, at - start), args.ToArray()));
                    args.Clear(); start = at;
                    continue;
                }
                args.Add(number);
                if (args.Count > 48) return Fail(at, DataField.Count, (ulong)args.Count, 48);
            }
            if (args.Count != 0) return Fail(start, DataField.Payload);
            if (!Buffer((long)IntPtr.Size * entries.Count)) return false;
            dict = new(entries.ToArray());
            return true;
        }
        private static int Arity(int kind, int op) => kind switch
        {
            0 => op switch
            {
                Ros => 3, 5 => 4, 0x0c07 => 6, 14 => -1,
                18 => 2,
                0 or 1 or 2 or 3 or 4 or 13 or 15 or 16 or 17 or
                0x0c00 or 0x0c01 or 0x0c02 or 0x0c03 or 0x0c04 or 0x0c05 or 0x0c06 or 0x0c08 or
                0x0c1f or 0x0c20 or 0x0c21 or 0x0c22 or 0x0c23 or FdArray or FdSelect => 1,
                _ => -2
            },
            1 => op switch { 18 => 2, 0x0c07 => 6, 0x0c26 => 1, _ => -2 },
            _ => op switch
            {
                6 or 7 or 8 or 9 or 0x0c0c or 0x0c0d => -1,
                10 or 11 or 19 or 20 or 21 or 0x0c09 or 0x0c0a or 0x0c0b or 0x0c0e or
                0x0c11 or 0x0c12 or 0x0c13 => 1,
                _ => -2
            }
        };
        private bool ReadReal(ref int at, int end)
        {
            var text = new StringBuilder();
            while (at < end)
            {
                byte b = _bytes[at++];
                for (int shift = 4; shift >= 0; shift -= 4)
                {
                    int nibble = (b >> shift) & 15;
                    if (nibble == 15)
                        return double.TryParse(text.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture,
                            out double value) && double.IsFinite(value) || Fail(at - 1, DataField.Payload);
                    if (nibble == 13) return Fail(at - 1, DataField.Payload);
                    text.Append(nibble switch { 10 => ".", 11 => "E", 12 => "E-", 14 => "-", _ => ((char)('0' + nibble)).ToString() });
                    if (text.Length > 128) return Fail(at - 1, DataField.Length, (ulong)text.Length, 128,
                        ReadStatus.ResourceLimit, DiagnosticCode.BudgetExceeded);
                }
            }
            return Fail(at, DataField.Payload);
        }
        private bool Integers(Dict dict, int op, int count, out int[] values)
        {
            values = [];
            var args = dict.Args(op);
            if (args is null || args.Length != count || args.Any(n => n is null))
                return Fail(0, DataField.Payload, (ulong)op);
            if (!Buffer(4L * count)) return false;
            values = args.Select(n => n!.Value).ToArray();
            return true;
        }
        private bool Integer(Dict dict, int op, out int value)
        {
            value = 0;
            if (!Integers(dict, op, 1, out var values)) return false;
            value = values[0]; return true;
        }
        private bool DefaultInteger(Dict dict, int op, int defaultValue, int expected)
        {
            int value = defaultValue;
            return (!dict.Has(op) || Integer(dict, op, out value)) && value == expected;
        }
        private bool ReadCharset(int at, int count, out ushort[] cids)
        {
            cids = [];
            if (at <= 2) return Fail(at, DataField.Offset, (ulong)Math.Max(0, at), 3, ReadStatus.UnsupportedFormat);
            if (!Bounds(at, 1) || !Work(count * 2) || !Buffer(2L * count)) return false;
            int format = _bytes[at++];
            if (format > 2) return Fail(at - 1, DataField.Version, (ulong)format, 2, ReadStatus.UnsupportedFormat);
            var result = new ushort[count];
            var seen = new HashSet<ushort> { 0 };
            int gid = 1;
            while (gid < count)
            {
                int length = format == 0 ? 2 : format == 1 ? 3 : 4;
                if (!Bounds(at, length)) return false;
                int first = ReadWire.U16(_bytes, at);
                int left = format == 0 ? 0 : format == 1 ? _bytes[at + 2] : ReadWire.U16(_bytes, at + 2);
                if (left >= count - gid || first + left > ushort.MaxValue)
                    return Fail(at, DataField.Count, (ulong)left, (ulong)(count - gid));
                for (int j = 0; j <= left; j++)
                {
                    ushort cid = (ushort)(first + j);
                    if (!seen.Add(cid)) return Fail(at, DataField.Cid, cid);
                    result[gid++] = cid;
                }
                at += length;
            }
            cids = result;
            return true;
        }
        private bool ReadBindings(int at, int count, int fdCount, out byte[] bindings)
        {
            bindings = [];
            if (!Bounds(at, 1) || !Work(count) || !Buffer(count)) return false;
            int format = _bytes[at++];
            byte[] result = new byte[count];
            if (format == 0)
            {
                if (!Bounds(at, count)) return false;
                for (int i = 0; i < count; i++)
                {
                    byte fd = _bytes[at + i];
                    if (fd >= fdCount) return Fail(at + i, DataField.FdIndex, fd, (ulong)fdCount);
                    result[i] = fd;
                }
            }
            else if (format == 3)
            {
                if (!Bounds(at, 2)) return false;
                int ranges = ReadWire.U16(_bytes, at); at += 2;
                if (ranges == 0 || ranges > count) return Fail(at - 2, DataField.Count, (ulong)ranges, (ulong)count);
                if (!Bounds(at, 3L * ranges + 2)) return false;
                for (int i = 0; i < ranges; i++)
                {
                    int first = ReadWire.U16(_bytes, at + 3 * i);
                    byte fd = _bytes[at + 3 * i + 2];
                    int next = ReadWire.U16(_bytes, at + 3 * (i + 1));
                    if (fd >= fdCount) return Fail(at + 3 * i + 2, DataField.FdIndex, fd, (ulong)fdCount);
                    if ((i == 0 && first != 0) || first >= next || next > count
                        || (i == ranges - 1 && next != count))
                        return Fail(at + 3 * i, DataField.GlyphId, (ulong)first, (ulong)count);
                    result.AsSpan(first, next - first).Fill(fd);
                }
            }
            else return Fail(at - 1, DataField.Version, (ulong)format, 3, ReadStatus.UnsupportedFormat);
            bindings = result;
            return true;
        }
    }
}
