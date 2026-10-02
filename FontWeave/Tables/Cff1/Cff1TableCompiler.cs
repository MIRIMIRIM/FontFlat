using System.Buffers.Binary;
using FontWeave.Core;

namespace FontWeave.Tables;

/// <summary>One Top DICT SID rewrite: the target operator must already exist (key
/// creation is not an edit) and either point at an existing SID or append fresh string
/// bytes to the String INDEX.</summary>
public sealed record Cff1TopDictSidEdit(int Operator, int? Sid, byte[]? NewString);

/// <summary>One charset SID rewrite for a single glyph (same form; glyph 0/.notdef is
/// immutable and the SID must stay unique inside the charset).</summary>
public sealed record Cff1CharsetEdit(int GlyphId, int Sid);

/// <summary>One encoding rewrite: the new code carried by one glyph (same form; 0
/// unmaps the glyph where the form allows it).</summary>
public sealed record Cff1EncodingEdit(int GlyphId, int Code);

/// <summary>One Private DICT numeric rewrite of an already present single-integer key
/// (the key set itself is preserved; Subrs and array/real values are out of scope).</summary>
public sealed record Cff1PrivateValueEdit(int Operator, int Value);

/// <summary>Supported CFF1 edit plan; every list member is one edit and an empty plan
/// is rejected as a no-op.</summary>
public sealed record Cff1EditPlan
{
    public IReadOnlyList<Cff1TopDictSidEdit> TopDictSids { get; init; } = [];
    public byte[]? NewFontName { get; init; }
    public IReadOnlyList<Cff1CharsetEdit> CharsetItems { get; init; } = [];
    public IReadOnlyList<Cff1EncodingEdit> EncodingItems { get; init; } = [];
    public IReadOnlyList<Cff1PrivateValueEdit> PrivateValues { get; init; } = [];
}

/// <summary>Canonical CFF1 compiler. Header 1.0 with hdrSize 4 and minimal offSize,
/// every INDEX with minimized offSize, DICT operands minimally re-encoded (real
/// operands verbatim), blocks laid out in the frozen section 1 order, charset/encoding
/// kept in their input form (predefined markers preserved, custom content re-encoded
/// minimally in the same form) and untouched charstrings/subroutines/string items
/// byte-for-byte. Supported edits are the four T3-01 classes; anything that changes
/// structure, key sets or charstring bytes is rejected, as is any no-op.</summary>
public static class Cff1TableCompiler
{
    public static ReadStatus TryCompile(Cff1Table model, out byte[]? bytes,
        out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        bytes = null;
        ArgumentNullException.ThrowIfNull(model);
        var budget = new BuildBudget(limits);
        return Compile(model, out bytes, out diagnostic, budget);
    }

    public static ReadStatus TryWrite(Cff1Table model, Span<byte> destination, out FontDiagnostic diagnostic)
    {
        ArgumentNullException.ThrowIfNull(model);
        ReadStatus status = TryCompile(model, out byte[]? encoded, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (destination.Length < encoded!.Length)
            return ReadWire.Fail(ReadStatus.DestinationTooSmall, DiagnosticCode.DestinationTooSmall,
                model.Location.At(DataField.OutputBytes, 0), out diagnostic,
                (ulong)destination.Length, (uint)encoded.Length);
        encoded.CopyTo(destination);
        diagnostic = default;
        return ReadStatus.Done;
    }

    public static ReadStatus TryApply(Cff1Table model, Cff1EditPlan plan, out Cff1Table? updated,
        out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        updated = null;
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(plan.TopDictSids);
        ArgumentNullException.ThrowIfNull(plan.CharsetItems);
        ArgumentNullException.ThrowIfNull(plan.EncodingItems);
        ArgumentNullException.ThrowIfNull(plan.PrivateValues);
        var budget = new BuildBudget(limits);
        return Apply(model, plan, out updated, out diagnostic, budget);
    }

    private static ReadStatus Apply(Cff1Table model, Cff1EditPlan plan, out Cff1Table? updated,
        out FontDiagnostic diagnostic, BuildBudget budget)
    {
        updated = null;
        if (plan.TopDictSids.Count == 0 && plan.CharsetItems.Count == 0
            && plan.EncodingItems.Count == 0 && plan.PrivateValues.Count == 0
            && plan.NewFontName is null)
            return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
                model.Location, out diagnostic);
        long payload = model.NameBytes.Length + 8L * model.NumGlyphs;
        foreach (var item in model.Strings) payload += item.Length + 8;
        foreach (var item in model.CharStrings) payload += item.Length + 8;
        foreach (var item in model.GlobalSubrs) payload += item.Length + 8;
        foreach (var item in model.LocalSubrs) payload += item.Length + 8;
        payload += 24L * (model.TopDict.Length + model.PrivateDict.Length);
        if (!budget.Work(payload, model.Location, out diagnostic) ||
            !budget.Buffer(payload, model.Location, out diagnostic))
            return ReadStatus.ResourceLimit;

        int sidBound = Cff1Layout.StandardStringCount + model.StringCount;
        var strings = new List<byte[]>(model.Strings.ToArray());
        var topDict = model.TopDict.ToArray();
        var privateDict = model.PrivateDict.ToArray();
        var charsetSids = model.CharsetSids.ToArray();
        var encodingCodes = model.EncodingCodes.ToArray();
        var nameBytes = model.NameBytes.ToArray();

        foreach (var edit in plan.TopDictSids)
        {
            if (!Cff1Layout.IsSidOperator(edit.Operator))
                return Unsupported(model.Location, out diagnostic, (uint)edit.Operator);
            int index = FindEntry(topDict, edit.Operator);
            if (index < 0)
                return Unsupported(model.Location, out diagnostic, (uint)edit.Operator);
            int newSid;
            if (edit.NewString is not null && edit.Sid is not null)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    model.Location.At(DataField.Payload, 0), out diagnostic, (uint)edit.Operator);
            if (edit.NewString is not null)
            {
                newSid = Cff1Layout.StandardStringCount + strings.Count;
                if (newSid > 0xFFFF)
                    return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                        model.Location.At(DataField.Cid, 0), out diagnostic,
                        (uint)newSid, 0x10000u);
                strings.Add(edit.NewString);
            }
            else if (edit.Sid is int sid)
            {
                newSid = sid;
            }
            else
            {
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    model.Location.At(DataField.Payload, 0), out diagnostic, (uint)edit.Operator);
            }
            if (newSid < 0 || newSid >= Cff1Layout.StandardStringCount + strings.Count)
                return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                    model.Location.At(DataField.Cid, 0), out diagnostic,
                    (uint)Math.Max(0, newSid), (uint)(Cff1Layout.StandardStringCount + strings.Count));
            var current = topDict[index].Operands;
            if (current.Count == 1 && !current[0].IsReal && current[0].Value == newSid)
                return Unsupported(model.Location, out diagnostic, (uint)edit.Operator);
            var operand = new Cff1Operand(newSid, null);
            topDict[index] = new Cff1DictEntry(edit.Operator, [operand], EncodeEntryRaw(edit.Operator, [operand]));
        }

        if (plan.NewFontName is not null)
        {
            if (nameBytes.AsSpan().SequenceEqual(plan.NewFontName))
                return Unsupported(model.Location, out diagnostic);
            nameBytes = plan.NewFontName;
        }

        if (plan.CharsetItems.Count > 0 && model.CharsetForm.Predefined)
            return Unsupported(model.Location, out diagnostic);
        foreach (var edit in plan.CharsetItems)
        {
            if (edit.GlyphId <= 0 || edit.GlyphId >= model.NumGlyphs)
                return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                    model.Location.At(DataField.GlyphId, 0, edit.GlyphId), out diagnostic,
                    unchecked((ulong)(long)edit.GlyphId), (uint)model.NumGlyphs);
            if (edit.Sid < 0 || edit.Sid >= Cff1Layout.StandardStringCount + strings.Count
                || edit.Sid > 0xFFFF)
                return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                    model.Location.At(DataField.Cid, 0, edit.GlyphId), out diagnostic,
                    (uint)Math.Max(0, edit.Sid), (uint)(Cff1Layout.StandardStringCount + strings.Count));
            if (charsetSids[edit.GlyphId] == edit.Sid)
                return Unsupported(model.Location, out diagnostic);
            if (Array.IndexOf(charsetSids, edit.Sid) >= 0)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.OverlappingRange,
                    model.Location.At(DataField.Cid, 0, edit.GlyphId), out diagnostic,
                    (uint)edit.Sid, (uint)edit.Sid);
            charsetSids[edit.GlyphId] = edit.Sid;
        }

        if (plan.EncodingItems.Count > 0 && model.EncodingForm.Predefined)
            return Unsupported(model.Location, out diagnostic);
        foreach (var edit in plan.EncodingItems)
        {
            if (edit.GlyphId <= 0 || edit.GlyphId >= model.NumGlyphs)
                return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                    model.Location.At(DataField.GlyphId, 0, edit.GlyphId), out diagnostic,
                    unchecked((ulong)(long)edit.GlyphId), (uint)model.NumGlyphs);
            if (edit.Code < 0 || edit.Code > 0xFF)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    model.Location.At(DataField.CharCode, 0, edit.GlyphId), out diagnostic,
                    (uint)edit.Code, 0xFF);
            if (!model.EncodingForm.Predefined && model.EncodingForm.Format == 0
                && edit.Code != 0 && edit.GlyphId > 0xFF)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    model.Location.At(DataField.CharCode, 0, edit.GlyphId), out diagnostic,
                    (uint)edit.GlyphId, 0xFF);
            if (encodingCodes[edit.GlyphId] == edit.Code)
                return Unsupported(model.Location, out diagnostic);
            bool supplementOccupies = false;
            foreach (var supplement in model.EncodingSupplements)
                if (supplement.Code == edit.Code) { supplementOccupies = true; break; }
            if (edit.Code != 0 && (Array.IndexOf(encodingCodes, edit.Code) >= 0 || supplementOccupies))
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.OverlappingRange,
                    model.Location.At(DataField.CharCode, 0, edit.GlyphId), out diagnostic,
                    (uint)edit.Code, (uint)edit.Code);
            encodingCodes[edit.GlyphId] = edit.Code;
        }
        if (plan.EncodingItems.Count > 0 && !model.EncodingForm.Predefined
            && model.EncodingForm.Format == 1)
        {
            int last = encodingCodes.Length - 1;
            while (last >= 1 && encodingCodes[last] == 0) last--;
            for (int gid = 1; gid <= last; gid++)
            {
                if (encodingCodes[gid] == 0)
                    return Unsupported(model.Location, out diagnostic);
            }
            int ranges = 0;
            for (int gid = 1; gid <= last;)
            {
                ranges++;
                gid++;
                while (gid <= last && encodingCodes[gid] == encodingCodes[gid - 1] + 1)
                    gid++;
            }
            if (ranges > 0xFF)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    model.Location.At(DataField.CharCode, 0), out diagnostic,
                    (uint)ranges, 0xFF);
        }

        foreach (var edit in plan.PrivateValues)
        {
            if (!Cff1Layout.IsEditablePrivateOperator(edit.Operator))
                return Unsupported(model.Location, out diagnostic, (uint)edit.Operator);
            int index = FindEntry(privateDict, edit.Operator);
            if (index < 0)
                return Unsupported(model.Location, out diagnostic, (uint)edit.Operator);
            var current = privateDict[index].Operands;
            if (current.Count != 1 || current[0].IsReal)
                return Unsupported(model.Location, out diagnostic, (uint)edit.Operator);
            if (current[0].Value == edit.Value)
                return Unsupported(model.Location, out diagnostic, (uint)edit.Operator);
            var operand = new Cff1Operand(edit.Value, null);
            privateDict[index] = new Cff1DictEntry(edit.Operator, [operand], EncodeEntryRaw(edit.Operator, [operand]));
        }

        var table = new Cff1Table(model.HeaderMajor, model.HeaderMinor, model.HeaderHdrSize,
            nameBytes, topDict, strings.ToArray(), model.GlobalSubrs.ToArray(),
            model.LocalSubrs.ToArray(), model.HasLocalSubrs, privateDict, model.CharsetForm,
            charsetSids, model.EncodingForm, encodingCodes,
            model.EncodingSupplements.ToArray(), model.CharStrings.ToArray(), model.Location);
        updated = table;
        diagnostic = default;
        return ReadStatus.Done;
    }

    private static ReadStatus Unsupported(DataLocation location, out FontDiagnostic diagnostic,
        ulong detail = 0) =>
        ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
            location.At(DataField.Payload, detail), out diagnostic, detail);

    private static int FindEntry(Cff1DictEntry[] entries, int op)
    {
        for (int i = 0; i < entries.Length; i++)
        {
            if (entries[i].Operator == op)
                return i;
        }
        return -1;
    }

    private static ReadStatus Compile(Cff1Table model, out byte[]? bytes,
        out FontDiagnostic diagnostic, BuildBudget budget)
    {
        bytes = null;
        long charsetLength = CharsetLength(model);
        bool charsetCustom = !model.CharsetForm.Predefined;
        bool encodingCustom = !model.EncodingForm.Predefined;
        long encodingLength = EncodingLength(model);
        bool hasLocalSubrs = model.HasLocalSubrs;

        // Encoding expressibility invariants for the preserved form; unreachable for
        // models produced by TryRead/TryApply but checked defensively before sizing.
        if (encodingCustom)
        {
            var codes = model.EncodingCodes;
            int last = codes.Length - 1;
            while (last >= 1 && codes[last] == 0)
                last--;
            if (model.EncodingForm.Format == 1)
            {
                for (int gid = 1; gid <= last; gid++)
                {
                    if (codes[gid] == 0)
                        return ReadWire.Fail(ReadStatus.UnsupportedFormat,
                            DiagnosticCode.UnsupportedOperation,
                            model.Location.At(DataField.CharCode, 0, gid), out diagnostic,
                            (uint)gid);
                }
            }
            else if (last > 0xFF)
            {
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    model.Location.At(DataField.CharCode, 0), out diagnostic,
                    (uint)last, 0xFF);
            }
        }

        // Private DICT length fixpoint: op 19 points right behind the DICT, so its
        // operand encodes the DICT's own length.
        int privateLength = 0;
        Dictionary<int, int[]> privateReplacements = new();
        for (int i = 0; i < 5; i++)
        {
            privateReplacements = hasLocalSubrs
                ? new Dictionary<int, int[]> { [Cff1Layout.OpSubrs] = [privateLength] }
                : [];
            int next = DictLength(model.PrivateDict, privateReplacements);
            if (next == privateLength) break;
            privateLength = next;
        }

        long nameIndexLength = SingleItemIndexLength(model.NameBytes.Length);
        long stringIndexLength = IndexLength(model.Strings);
        long globalIndexLength = IndexLength(model.GlobalSubrs);
        long charstringsIndexLength = IndexLength(model.CharStrings);
        long localIndexLength = hasLocalSubrs ? IndexLength(model.LocalSubrs) : 0;

        // Top DICT offsets depend on every preceding block length, which depends on
        // the DICT operand widths the offsets are encoded with; iterate the monotone
        // fixpoint from the minimal widths upward.
        long charsetOffset = 0, encodingOffset = 0, charstringsOffset = 0, privateOffset = 0;
        long total = 0;
        for (int iteration = 0; iteration < 24; iteration++)
        {
            var topReplacements = new Dictionary<int, int[]>
            {
                [Cff1Layout.OpCharStrings] = [(int)charstringsOffset],
                [Cff1Layout.OpPrivate] = [privateLength, (int)privateOffset]
            };
            if (charsetCustom)
                topReplacements[Cff1Layout.OpCharset] = [(int)charsetOffset];
            if (encodingCustom)
                topReplacements[Cff1Layout.OpEncoding] = [(int)encodingOffset];
            int topDictLength = DictLength(model.TopDict, topReplacements);
            long topIndexLength = SingleItemIndexLength(topDictLength);
            long cursor = Cff1Layout.MinimumHeaderLength + nameIndexLength + topIndexLength
                + stringIndexLength + globalIndexLength;
            long nextCharsetOffset = cursor;
            if (charsetCustom) cursor += charsetLength;
            long nextEncodingOffset = cursor;
            if (encodingCustom) cursor += encodingLength;
            long nextCharstringsOffset = cursor;
            cursor += charstringsIndexLength;
            long nextPrivateOffset = cursor;
            cursor += privateLength + localIndexLength;
            if (nextCharsetOffset == charsetOffset && nextEncodingOffset == encodingOffset
                && nextCharstringsOffset == charstringsOffset && nextPrivateOffset == privateOffset
                && cursor == total)
                break;
            if (iteration == 23)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    model.Location.At(DataField.Length, 0), out diagnostic,
                    (ulong)cursor, (ulong)total);
            charsetOffset = nextCharsetOffset;
            encodingOffset = nextEncodingOffset;
            charstringsOffset = nextCharstringsOffset;
            privateOffset = nextPrivateOffset;
            total = cursor;
        }

        if (total > int.MaxValue)
            return ReadWire.Fail(ReadStatus.ResourceLimit, DiagnosticCode.BudgetExceeded,
                model.Location.At(DataField.Length, 0), out diagnostic,
                (ulong)total, int.MaxValue);
        long indexItems = model.StringCount + model.GlobalSubrCount + model.NumGlyphs
            + model.LocalSubrCount;
        if (!budget.Work(2L * total + 16L * indexItems, model.Location, out diagnostic) ||
            !budget.Buffer(total + 4L * indexItems, model.Location, out diagnostic) ||
            !budget.Output(total, model.Location, out diagnostic))
            return ReadStatus.ResourceLimit;

        var output = new byte[(int)total];
        int at = 0;
        output[at++] = 1;
        output[at++] = 0;
        output[at++] = 4;
        output[at++] = 1;
        EmitSingleItemIndex(output, ref at, model.NameBytes);
        var topReplacementsFinal = new Dictionary<int, int[]>
        {
            [Cff1Layout.OpCharStrings] = [(int)charstringsOffset],
            [Cff1Layout.OpPrivate] = [privateLength, (int)privateOffset]
        };
        if (charsetCustom)
            topReplacementsFinal[Cff1Layout.OpCharset] = [(int)charsetOffset];
        if (encodingCustom)
            topReplacementsFinal[Cff1Layout.OpEncoding] = [(int)encodingOffset];
        int finalTopDictLength = DictLength(model.TopDict, topReplacementsFinal);
        int topOffSize = OffSizeFor((long)finalTopDictLength + 1);
        EmitCountedIndexHeader(output, ref at, 1, topOffSize);
        WriteIndexOffset(output, ref at, 1, topOffSize);
        WriteIndexOffset(output, ref at, (long)finalTopDictLength + 1, topOffSize);
        EmitDict(output, ref at, model.TopDict, topReplacementsFinal);
        EmitIndex(output, ref at, model.Strings);
        EmitIndex(output, ref at, model.GlobalSubrs);
        if (charsetCustom)
            EmitCharset(output, ref at, model);
        if (encodingCustom)
        {
            ReadStatus status = EmitEncoding(model, output, ref at, out diagnostic);
            if (status != ReadStatus.Done) return status;
        }
        EmitIndex(output, ref at, model.CharStrings);
        EmitDict(output, ref at, model.PrivateDict, privateReplacements);
        if (hasLocalSubrs)
            EmitIndex(output, ref at, model.LocalSubrs);
        if (at != output.Length)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                model.Location.At(DataField.Length, (uint)at), out diagnostic,
                (uint)at, (uint)output.Length);
        budget.RecordCompiledTable(output.Length);
        bytes = output;
        diagnostic = default;
        return ReadStatus.Done;
    }

    private static long SingleItemIndexLength(int itemLength) =>
        3 + 2L * OffSizeFor((long)itemLength + 1) + itemLength;

    private static void EmitSingleItemIndex(byte[] output, ref int at, ReadOnlySpan<byte> item)
    {
        int offSize = OffSizeFor((long)item.Length + 1);
        EmitCountedIndexHeader(output, ref at, 1, offSize);
        WriteIndexOffset(output, ref at, 1, offSize);
        WriteIndexOffset(output, ref at, (long)item.Length + 1, offSize);
        item.CopyTo(output.AsSpan(at));
        at += item.Length;
    }

    private static void EmitCountedIndexHeader(byte[] output, ref int at, int count, int offSize)
    {
        BinaryPrimitives.WriteUInt16BigEndian(output.AsSpan(at), (ushort)count);
        at += 2;
        output[at++] = unchecked((byte)(offSize));
    }

    private static int OffSizeFor(long maxOffset) => maxOffset switch
    {
        <= 0xFF => 1,
        <= 0xFFFF => 2,
        <= 0xFFFFFF => 3,
        _ => 4
    };

    private static int IntOperandLength(int value) => value switch
    {
        >= -107 and <= 107 => 1,
        >= 108 and <= 1131 or >= -1131 and <= -108 => 2,
        >= short.MinValue and <= short.MaxValue => 3,
        _ => 5
    };

    private static void WriteIntOperand(byte[] output, ref int at, int value)
    {
        if (value is >= -107 and <= 107)
        {
            output[at++] = unchecked((byte)((value + 139)));
        }
        else if (value is >= 108 and <= 1131)
        {
            int v = value - 108;
            output[at++] = unchecked((byte)((247 + (v >> 8))));
            output[at++] = unchecked((byte)((v & 0xFF)));
        }
        else if (value is >= -1131 and <= -108)
        {
            int v = -value - 108;
            output[at++] = unchecked((byte)((251 + (v >> 8))));
            output[at++] = unchecked((byte)((v & 0xFF)));
        }
        else if (value is >= short.MinValue and <= short.MaxValue)
        {
            output[at++] = 28;
            BinaryPrimitives.WriteInt16BigEndian(output.AsSpan(at), (short)value);
            at += 2;
        }
        else
        {
            output[at++] = 29;
            BinaryPrimitives.WriteInt32BigEndian(output.AsSpan(at), value);
            at += 4;
        }
    }

    private static int DictLength(ReadOnlySpan<Cff1DictEntry> entries, Dictionary<int, int[]> replacements)
    {
        long length = 0;
        foreach (var entry in entries)
        {
            if (replacements.TryGetValue(entry.Operator, out var values))
            {
                foreach (int value in values)
                    length += IntOperandLength(value);
            }
            else
            {
                foreach (var operand in entry.Operands)
                    length += operand.IsReal
                        ? operand.RealBytes!.Length + 1     // + the 0x1E real-number marker
                        : IntOperandLength(operand.Value);
            }
            length += entry.Operator > 0xFF ? 2 : 1;
        }
        return (int)length;
    }

    private static void EmitDict(byte[] output, ref int at, ReadOnlySpan<Cff1DictEntry> entries,
        Dictionary<int, int[]> replacements)
    {
        foreach (var entry in entries)
        {
            if (replacements.TryGetValue(entry.Operator, out var values))
            {
                foreach (int value in values)
                    WriteIntOperand(output, ref at, value);
            }
            else
            {
                foreach (var operand in entry.Operands)
                {
                    if (operand.IsReal)
                    {
                        // RealNumber bytes are held without their 0x1E marker.
                        output[at++] = 30;
                        operand.RealBytes!.CopyTo(output.AsSpan(at));
                        at += operand.RealBytes!.Length;
                    }
                    else
                    {
                        WriteIntOperand(output, ref at, operand.Value);
                    }
                }
            }
            if (entry.Operator > 0xFF)
                output[at++] = 12;
            output[at++] = unchecked((byte)((entry.Operator & 0xFF)));
        }
    }

    private static byte[] EncodeEntryRaw(int op, IReadOnlyList<Cff1Operand> operands)
    {
        int length = op > 0xFF ? 2 : 1;
        foreach (var operand in operands)
            length += operand.IsReal
                ? operand.RealBytes!.Length + 1     // + the 0x1E real-number marker
                : IntOperandLength(operand.Value);
        var raw = new byte[length];
        int at = 0;
        foreach (var operand in operands)
        {
            if (operand.IsReal)
            {
                // RealNumber operands are stored without their 0x1E marker, so it has to be
                // written back here; omitting it would corrupt the DICT.
                raw[at++] = 30;
                operand.RealBytes!.CopyTo(raw.AsSpan(at));
                at += operand.RealBytes!.Length;
            }
            else
            {
                WriteIntOperand(raw, ref at, operand.Value);
            }
        }
        if (op > 0xFF) raw[at++] = 12;
        raw[at] = (byte)(op & 0xFF);
        return raw;
    }

    private static long IndexLength(ReadOnlySpan<byte[]> items)
    {
        if (items.Length == 0)
            return 2;
        long data = 0;
        foreach (var item in items)
            data += item.Length;
        return 3 + (long)(items.Length + 1) * OffSizeFor(data + 1) + data;
    }

    private static void EmitIndex(byte[] output, ref int at, ReadOnlySpan<byte[]> items)
    {
        BinaryPrimitives.WriteUInt16BigEndian(output.AsSpan(at), (ushort)items.Length);
        at += 2;
        if (items.Length == 0)
            return;
        long data = 0;
        foreach (var item in items)
            data += item.Length;
        int offSize = OffSizeFor(data + 1);
        output[at++] = unchecked((byte)(offSize));
        long offset = 1;
        WriteIndexOffset(output, ref at, offset, offSize);
        foreach (var item in items)
        {
            offset += item.Length;
            WriteIndexOffset(output, ref at, offset, offSize);
        }
        foreach (var item in items)
        {
            item.CopyTo(output.AsSpan(at));
            at += item.Length;
        }
    }

    private static void WriteIndexOffset(byte[] output, ref int at, long value, int offSize)
    {
        // Big-endian offSize-byte offset; shifts land inside 8*offSize <= 32 bits,
        // but the long shift expression is checked, so narrow through int bits.
        for (int i = 0; i < offSize; i++)
            output[at + i] = unchecked((byte)((ulong)value >> (8 * (offSize - 1 - i))));
        at += offSize;
    }

    private static long CharsetLength(Cff1Table model)
    {
        if (model.CharsetForm.Predefined)
            return 0;
        if (model.CharsetForm.Format == 0)
            return 1 + 2L * (model.NumGlyphs - 1);
        return 1 + CharsetRangeBytes(model.CharsetSids, model.CharsetForm.Format);
    }

    private static long CharsetRangeBytes(ReadOnlySpan<int> sids, int format)
    {
        long bytes = 0;
        int maxRun = format == 1 ? 256 : 65536;
        int i = 1;
        while (i < sids.Length)
        {
            int j = i;
            while (j + 1 < sids.Length && j + 1 - i < maxRun && sids[j + 1] == sids[j] + 1)
                j++;
            bytes += format == 1 ? 3 : 4;
            i = j + 1;
        }
        return bytes;
    }

    private static void EmitCharset(byte[] output, ref int at, Cff1Table model)
    {
        output[at++] = unchecked((byte)(model.CharsetForm.Format));
        var sids = model.CharsetSids;
        if (model.CharsetForm.Format == 0)
        {
            for (int gid = 1; gid < sids.Length; gid++)
            {
                output[at++] = unchecked((byte)((sids[gid] >> 8)));
                output[at++] = unchecked((byte)(sids[gid]));
            }
            return;
        }
        int maxRun = model.CharsetForm.Format == 1 ? 256 : 65536;
        int i = 1;
        while (i < sids.Length)
        {
            int j = i;
            while (j + 1 < sids.Length && j + 1 - i < maxRun && sids[j + 1] == sids[j] + 1)
                j++;
            output[at++] = unchecked((byte)((sids[i] >> 8)));
            output[at++] = unchecked((byte)(sids[i]));
            int nLeft = j - i;
            if (model.CharsetForm.Format == 1)
            {
                output[at++] = unchecked((byte)(nLeft));
            }
            else
            {
                output[at++] = unchecked((byte)((nLeft >> 8)));
                output[at++] = unchecked((byte)(nLeft));
            }
            i = j + 1;
        }
    }

    private static long EncodingLength(Cff1Table model)
    {
        if (model.EncodingForm.Predefined)
            return 0;
        var codes = model.EncodingCodes;
        int last = codes.Length - 1;
        while (last >= 1 && codes[last] == 0)
            last--;
        long length;
        if (model.EncodingForm.Format == 0)
        {
            length = 2 + last;
        }
        else
        {
            int ranges = 0;
            for (int gid = 1; gid <= last;)
            {
                ranges++;
                gid++;
                while (gid <= last && codes[gid] == codes[gid - 1] + 1)
                    gid++;
            }
            length = 2 + 2L * ranges;
        }
        if (model.EncodingForm.HasSupplement)
            length += 1 + 3L * model.EncodingSupplements.Length;
        return length;
    }

    private static ReadStatus EmitEncoding(Cff1Table model, byte[] output, ref int at,
        out FontDiagnostic diagnostic)
    {
        diagnostic = default;
        output[at++] = unchecked((byte)((model.EncodingForm.Format | (model.EncodingForm.HasSupplement ? 0x80 : 0))));
        var codes = model.EncodingCodes;
        int last = codes.Length - 1;
        while (last >= 1 && codes[last] == 0)
            last--;
        if (model.EncodingForm.Format == 0)
        {
            output[at++] = unchecked((byte)(last));
            for (int gid = 1; gid <= last; gid++)
                output[at++] = unchecked((byte)(codes[gid]));
        }
        else
        {
            int rangesStart = at;
            at++;
            int ranges = 0;
            for (int gid = 1; gid <= last;)
            {
                ranges++;
                output[at++] = unchecked((byte)(codes[gid]));
                int runStart = gid;
                gid++;
                while (gid <= last && codes[gid] == codes[gid - 1] + 1)
                    gid++;
                output[at++] = unchecked((byte)((gid - 1 - runStart)));
            }
            if (ranges > 0xFF)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    model.Location.At(DataField.CharCode, 0), out diagnostic, (uint)ranges, 0xFF);
            output[rangesStart] = (byte)ranges;
        }
        if (model.EncodingForm.HasSupplement)
        {
            var supplements = model.EncodingSupplements;
            output[at++] = unchecked((byte)(supplements.Length));
            foreach (var supplement in supplements)
            {
                output[at++] = unchecked((byte)(supplement.Code));
                output[at++] = unchecked((byte)((supplement.Sid >> 8)));
                output[at++] = unchecked((byte)(supplement.Sid));
            }
        }
        return ReadStatus.Done;
    }
}
