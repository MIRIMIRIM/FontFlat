using System.Buffers.Binary;
using FontWeave.Core;

namespace FontWeave.Tables;

/// <summary>Mechanical CFF1 wire constants (Adobe Technical Note #5176).</summary>
internal static class Cff1Layout
{
    internal const uint Tag = 0x43464620u; // 'CFF '
    internal const int MinimumHeaderLength = 4;
    internal const int StandardStringCount = 391;
    internal const byte MajorVersion = 1;
    internal const int MaxOperandStack = 48;
    internal const int MaxRealNibbleDigits = 128;

    // Top DICT operators.
    internal const int OpVersion = 0, OpNotice = 1, OpFullName = 2, OpFamilyName = 3, OpWeight = 4;
    internal const int OpFontBBox = 5, OpSyntheticBase = 13, OpXuid = 14, OpCharset = 15,
        OpEncoding = 16, OpCharStrings = 17, OpPrivate = 18;
    internal const int OpCopyright = 0x0c00, OpIsFixedPitch = 0x0c01, OpItalicAngle = 0x0c02,
        OpUnderlinePosition = 0x0c03, OpUnderlineThickness = 0x0c04, OpPaintType = 0x0c05,
        OpCharstringType = 0x0c06,
        OpFontMatrix = 0x0c07, OpStrokeWidth = 0x0c08;

    // CID-keyed markers: their presence means the table is not name-keyed.
    internal const int OpRos = 0x0c1e, OpFdArray = 0x0c24, OpFdSelect = 0x0c25;

    // Private DICT operators.
    internal const int OpBlueValues = 6, OpOtherBlues = 7, OpFamilyBlues = 8, OpFamilyOtherBlues = 9;
    internal const int OpStdHW = 10, OpStdVW = 11, OpBlueScale = 0x0c09, OpBlueShift = 0x0c0a,
        OpBlueFuzz = 0x0c0b, OpStemSnapH = 0x0c0c, OpStemSnapV = 0x0c0d;
    internal const int OpSubrs = 19, OpDefaultWidthX = 20, OpNominalWidthX = 21;

    /// <summary>Operators whose numeric value is editable inside the Private DICT; every
    /// other operator is structural (Subrs) or an array/real value outside edit scope.</summary>
    internal static bool IsEditablePrivateOperator(int op) =>
        op is OpStdHW or OpStdVW or OpBlueShift or OpBlueFuzz or OpDefaultWidthX or OpNominalWidthX;

    /// <summary>Top DICT operators whose single operand is a SID.</summary>
    internal static bool IsSidOperator(int op) =>
        op is OpVersion or OpNotice or OpFullName or OpFamilyName or OpWeight or OpCopyright;

    internal static int DictArity(bool topScope, int op)
    {
        if (topScope)
        {
            return op switch
            {
                OpVersion or OpNotice or OpFullName or OpFamilyName or OpWeight => 1,
                OpFontBBox => 4,
                OpSyntheticBase => 1,
                OpXuid => -1,
                OpCharset or OpEncoding or OpCharStrings => 1,
                OpPrivate => 2,
                OpCopyright or OpIsFixedPitch or OpItalicAngle or OpUnderlinePosition
                    or OpUnderlineThickness or OpCharstringType or OpPaintType
                    or OpStrokeWidth => 1,
                OpFontMatrix => 6,
                _ => -2
            };
        }
        return op switch
        {
            OpBlueValues or OpOtherBlues or OpFamilyBlues or OpFamilyOtherBlues
                or OpStemSnapH or OpStemSnapV => -1,
            OpStdHW or OpStdVW or OpSubrs or OpDefaultWidthX or OpNominalWidthX
                or OpBlueScale or OpBlueShift or OpBlueFuzz or 0x0c0e or 0x0c11
                or 0x0c12 or 0x0c13 => 1,
            _ => -2
        };
    }
}

/// <summary>Form of one charset or encoding reference in the Top DICT: a predefined
/// marker value (0/1/2 for charset, 0/1 for encoding) or a custom table with the
/// wire format byte recorded for form-preserving recompilation.</summary>
public readonly record struct Cff1CharsetForm(bool Predefined, int Format);

/// <summary>Form of one Encoding reference; HasSupplement mirrors the high bit of a
/// custom encoding's format byte (predefined encodings never carry supplements).</summary>
public readonly record struct Cff1EncodingForm(bool Predefined, int Format, bool HasSupplement);

/// <summary>One glyph's position in the charset: GlyphId in charset order and the
/// string SID that names it (0 for .notdef).</summary>
public readonly record struct Cff1SidGlyph(int GlyphId, int Sid);

/// <summary>Single handwritten View over a whole name-keyed CFF1 table. CFF is a
/// mesh of relative offsets (INDEX/DICT/charset/Encoding/Private all cross-reference
/// table and Private positions), which is beyond the flat Schema table expressiveness
/// declared for the generator; per the T3-01 card this is the single official View
/// type for the layout and no Generated*View exists beside it. TryOpen only performs the
/// mechanical header admission (major version, hdrSize); all strict wire rules live
/// in Validate.</summary>
public readonly ref partial struct Cff1View
{
    private readonly ReadOnlySpan<byte> _data;
    private readonly DataLocation _location;

    private Cff1View(ReadOnlySpan<byte> data, DataLocation location)
    {
        _data = data;
        _location = location;
    }

    internal DataLocation Location => _location == default ? DataLocation.Anonymous(Cff1Layout.Tag) : _location;

    public bool IsBound => _data.Length >= Cff1Layout.MinimumHeaderLength;

    public ReadOnlySpan<byte> RawBytes => _data;

    /// <summary>Binding admission only: at least a 4-byte header, major version 1 (CFF2
    /// tables are rejected here), hdrSize at least 4 and within the bytes. No INDEX,
    /// DICT or offset structure is touched.</summary>
    public static ReadStatus TryOpen(ReadOnlySpan<byte> data, out Cff1View view,
        out FontDiagnostic diagnostic, DataLocation location = default)
    {
        view = default;
        if (location == default)
            location = DataLocation.Anonymous(Cff1Layout.Tag);
        if (data.Length < Cff1Layout.MinimumHeaderLength)
            return ReadWire.Fail(ReadStatus.Truncated, DiagnosticCode.TruncatedData,
                location.At(DataField.Version, 0), out diagnostic,
                (ulong)data.Length, (ulong)Cff1Layout.MinimumHeaderLength);
        byte major = data[0];
        if (major != Cff1Layout.MajorVersion)
            return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedVersion,
                location.At(DataField.Version, 0), out diagnostic, major, Cff1Layout.MajorVersion);
        byte hdrSize = data[2];
        if (hdrSize < Cff1Layout.MinimumHeaderLength)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                location.At(DataField.Length, 2), out diagnostic,
                hdrSize, (ulong)Cff1Layout.MinimumHeaderLength);
        if (data.Length < hdrSize)
            return ReadWire.Fail(ReadStatus.Truncated, DiagnosticCode.TruncatedData,
                location.At(DataField.Length, 2), out diagnostic, (ulong)data.Length, hdrSize);
        view = new Cff1View(data, location);
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>Mechanical header read; TryOpen has already bounded these four bytes.</summary>
    public ReadStatus TryGetHeader(out byte major, out byte minor, out byte hdrSize,
        out byte offSize, out FontDiagnostic diagnostic)
    {
        major = 0; minor = 0; hdrSize = 0; offSize = 0;
        if (!IsBound)
            return Unbound(out diagnostic);
        major = _data[0];
        minor = _data[1];
        hdrSize = _data[2];
        offSize = _data[3];
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>Number of entries in the Name INDEX; a valid name-keyed table has exactly one.</summary>
    public ReadStatus TryGetNameIndexCount(out int count, out FontDiagnostic diagnostic)
    {
        count = 0;
        ReadStatus status = LocateIndexes(out var names, out _, out _, out _, out diagnostic);
        if (status != ReadStatus.Done) return status;
        count = names.Count;
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>Bounded read of one Name INDEX item as raw bytes (an approximate
    /// PostScript name; contents are preserved, not interpreted).</summary>
    public ReadStatus TryGetNameIndex(int ordinal, out ReadOnlySpan<byte> name,
        out FontDiagnostic diagnostic)
    {
        name = default;
        ReadStatus status = LocateIndexes(out var names, out _, out _, out _, out diagnostic);
        if (status != ReadStatus.Done) return status;
        return ReadIndexItem(names, ordinal, out name, out diagnostic);
    }

    /// <summary>Number of entries in the String INDEX (custom strings start at SID 391).</summary>
    public ReadStatus TryGetStringIndexCount(out int count, out FontDiagnostic diagnostic)
    {
        count = 0;
        ReadStatus status = LocateIndexes(out _, out _, out var strings, out _, out diagnostic);
        if (status != ReadStatus.Done) return status;
        count = strings.Count;
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>Bounded read of one String INDEX item as raw bytes.</summary>
    public ReadStatus TryGetStringIndex(int ordinal, out ReadOnlySpan<byte> value,
        out FontDiagnostic diagnostic)
    {
        value = default;
        ReadStatus status = LocateIndexes(out _, out _, out var strings, out _, out diagnostic);
        if (status != ReadStatus.Done) return status;
        return ReadIndexItem(strings, ordinal, out value, out diagnostic);
    }

    /// <summary>Count of one subroutine INDEX: global (independent) or local (offset
    /// relative to the Private DICT start). A Private DICT without Subrs reports 0.</summary>
    public ReadStatus TryGetSubrs(bool local, out int count, out FontDiagnostic diagnostic)
    {
        count = 0;
        if (!IsBound)
            return Unbound(out diagnostic);
        ReadStatus status;
        if (!local)
        {
            status = LocateIndexes(out _, out _, out _, out var globals, out diagnostic);
            if (status != ReadStatus.Done) return status;
            count = globals.Count;
            diagnostic = default;
            return ReadStatus.Done;
        }
        status = LocatePrivate(out int privateStart, out int privateLength, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = ParseDict(privateStart, privateLength, topScope: false, out var entries, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (!TryFindEntry(entries, Cff1Layout.OpSubrs, out var entry))
        {
            diagnostic = default;
            return ReadStatus.Done;
        }
        status = EntryIntegers(entry!, out var operands, out diagnostic);
        if (status != ReadStatus.Done) return status;
        long subrsAt = (long)privateStart + operands[0];
        if (operands[0] < 0 || subrsAt > _data.Length - 2)
            return ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.RangeOutsideSource,
                Location.At(DataField.Offset, (ulong)(uint)Math.Max(0, (int)Math.Min(int.MaxValue, subrsAt))),
                out diagnostic, (ulong)Math.Max(0, subrsAt), (ulong)_data.Length);
        status = LocateIndex((int)subrsAt, out var index, out diagnostic);
        if (status != ReadStatus.Done) return status;
        count = index.Count;
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>Bounded read of one subroutine item as raw bytes.</summary>
    public ReadStatus TryGetSubrItem(bool local, int ordinal, out ReadOnlySpan<byte> item,
        out FontDiagnostic diagnostic)
    {
        item = default;
        if (!IsBound)
            return Unbound(out diagnostic);
        IndexSpan index;
        ReadStatus status;
        if (!local)
        {
            status = LocateIndexes(out _, out _, out _, out var globals, out diagnostic);
            if (status != ReadStatus.Done) return status;
            index = globals;
        }
        else
        {
            status = LocateLocalSubrs(out index, out diagnostic);
            if (status != ReadStatus.Done) return status;
        }
        return ReadIndexItem(index, ordinal, out item, out diagnostic);
    }

    /// <summary>Number of glyphs: the CharStrings INDEX count (glyph 0 is .notdef).</summary>
    public ReadStatus TryGetCharStringsCount(out int count, out FontDiagnostic diagnostic)
    {
        count = 0;
        ReadStatus status = LocateCharStrings(out var index, out diagnostic);
        if (status != ReadStatus.Done) return status;
        count = index.Count;
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>Bounded read of one charstring as raw bytes; the bytes are never interpreted.</summary>
    public ReadStatus TryGetCharString(int ordinal, out ReadOnlySpan<byte> bytes,
        out FontDiagnostic diagnostic)
    {
        bytes = default;
        ReadStatus status = LocateCharStrings(out var index, out diagnostic);
        if (status != ReadStatus.Done) return status;
        return ReadIndexItem(index, ordinal, out bytes, out diagnostic);
    }

    /// <summary>Raw Private DICT bytes at its Top DICT declared position.</summary>
    public ReadStatus TryGetPrivateDict(out ReadOnlySpan<byte> privateBytes, out FontDiagnostic diagnostic)
    {
        privateBytes = default;
        ReadStatus status = LocatePrivate(out int start, out int length, out diagnostic);
        if (status != ReadStatus.Done) return status;
        privateBytes = _data.Slice(start, length);
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>Integer operands of one Private DICT operator; NotFound reports a key
    /// the Private DICT does not carry, InvalidValue an operand that is a real token.</summary>
    public ReadStatus TryGetPrivateDictIntegers(int operatorKey, out int[]? operands,
        out FontDiagnostic diagnostic)
    {
        operands = null;
        ReadStatus status = LocatePrivate(out int start, out int length, out diagnostic);
        if (status != ReadStatus.Done) return status;
        return ReadDictIntegers(start, length, topScope: false, operatorKey, out operands, out diagnostic);
    }

    /// <summary>Integer operands of one Top DICT operator; NotFound reports a key the
    /// Top DICT does not carry, InvalidValue an operand that is a real token.</summary>
    public ReadStatus TryGetTopDictIntegers(int operatorKey, out int[]? operands,
        out FontDiagnostic diagnostic)
    {
        operands = null;
        ReadStatus status = LocateTopDict(out int start, out int length, out diagnostic);
        if (status != ReadStatus.Done) return status;
        return ReadDictIntegers(start, length, topScope: true, operatorKey, out operands, out diagnostic);
    }

    /// <summary>Faithful raw operand bytes of one Top DICT entry. Real operands are
    /// passed through verbatim (never decoded to a number); the span covers the entry's
    /// whole operand region so integer operands inside it appear as well.</summary>
    public ReadStatus TryGetTopDictRealBytes(int operatorKey, out ReadOnlySpan<byte> realBytes,
        out FontDiagnostic diagnostic)
    {
        realBytes = default;
        ReadStatus status = LocateTopDict(out int start, out int length, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = ParseDict(start, length, topScope: true, out var entries, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (!TryFindEntry(entries, operatorKey, out var entry))
            return ReadWire.Fail(ReadStatus.NotFound, DiagnosticCode.TableNotFound,
                Location.At(DataField.Payload, (uint)start), out diagnostic, (uint)operatorKey);
        realBytes = _data.Slice(entry!.OperandStart, entry.OperandEnd - entry.OperandStart);
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>Charset reference form: predefined marker (0 ISOAdobe, 1 Expert,
    /// 2 ExpertSubset) or custom table with its wire format byte.</summary>
    public ReadStatus TryGetCharsetForm(out Cff1CharsetForm form, out FontDiagnostic diagnostic)
    {
        form = default;
        if (!IsBound)
            return Unbound(out diagnostic);
        ReadStatus status = TryGetTopDictIntegers(Cff1Layout.OpCharset, out var operands, out diagnostic);
        if (status == ReadStatus.NotFound)
        {
            form = new Cff1CharsetForm(Predefined: true, Format: 0);
            diagnostic = default;
            return ReadStatus.Done;
        }
        if (status != ReadStatus.Done) return status;
        return CharsetFormFor(operands![0], out form, out diagnostic);
    }

    /// <summary>SID of one glyph in the charset, expanding predefined charsets from the
    /// embedded standard tables (glyph 0 is .notdef with SID 0).</summary>
    public ReadStatus TryGetSidForGlyph(int gid, out int sid, out FontDiagnostic diagnostic)
    {
        sid = 0;
        if (!IsBound)
            return Unbound(out diagnostic);
        ReadStatus status = TryGetCharStringsCount(out int numGlyphs, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (gid < 0 || gid >= numGlyphs)
            return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                Location.At(DataField.GlyphId, 0, gid), out diagnostic,
                unchecked((ulong)(long)gid), (uint)numGlyphs);
        status = TryGetTopDictIntegers(Cff1Layout.OpCharset, out var operands, out diagnostic);
        if (status == ReadStatus.NotFound)
        {
            status = PredefinedCharsetSid(0, gid, out sid, out diagnostic);
            return status;
        }
        if (status != ReadStatus.Done) return status;
        int value = operands![0];
        if (value is >= 0 and <= 2)
            return PredefinedCharsetSid(value, gid, out sid, out diagnostic);
        return CustomCharsetSid(value, gid, out sid, out diagnostic);
    }

    /// <summary>Encoding reference form: predefined marker (0 Standard, 1 Expert) or
    /// custom table with wire format and supplement flag.</summary>
    public ReadStatus TryGetEncodingForm(out Cff1EncodingForm form, out FontDiagnostic diagnostic)
    {
        form = default;
        if (!IsBound)
            return Unbound(out diagnostic);
        ReadStatus status = TryGetTopDictIntegers(Cff1Layout.OpEncoding, out var operands, out diagnostic);
        if (status == ReadStatus.NotFound)
        {
            form = new Cff1EncodingForm(Predefined: true, Format: 0, HasSupplement: false);
            diagnostic = default;
            return ReadStatus.Done;
        }
        if (status != ReadStatus.Done) return status;
        return EncodingFormFor(operands![0], out form, out diagnostic);
    }

    /// <summary>Encoding lookup of one code (0..255): custom encodings resolve through
    /// their wire tables (format 0 positional codes with 0 as the unmapped marker,
    /// format 1 ranges assigning glyphs consecutively, supplements overriding by SID);
    /// predefined Standard/Expert encodings resolve code to SID and then match the
    /// glyph whose charset name carries that SID.</summary>
    public ReadStatus TryMapCode(int code, out bool mapped, out int glyph, out FontDiagnostic diagnostic)
    {
        mapped = false;
        glyph = 0;
        if (!IsBound)
            return Unbound(out diagnostic);
        ReadStatus status = TryGetTopDictIntegers(Cff1Layout.OpEncoding, out var operands, out diagnostic);
        int encodingValue = 0;
        if (status == ReadStatus.Done)
            encodingValue = operands![0];
        else if (status != ReadStatus.NotFound)
            return status;
        if (code < 0 || code > 0xFF)
        {
            diagnostic = default;
            return ReadStatus.Done;
        }
        int sid;
        if (encodingValue is 0 or 1)
        {
            sid = Cff1Predefined.EncodingSid(encodingValue, code);
            if (sid == 0)
            {
                diagnostic = default;
                return ReadStatus.Done;
            }
            return GlyphForSidName(sid, out mapped, out glyph, out diagnostic);
        }
        status = CustomEncodingGlyph(encodingValue, code, out int directGlyph, out sid, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (directGlyph >= 0)
        {
            mapped = true;
            glyph = directGlyph;
            diagnostic = default;
            return ReadStatus.Done;
        }
        if (sid >= 0)
            return GlyphForSidName(sid, out mapped, out glyph, out diagnostic);
        diagnostic = default;
        return ReadStatus.Done;
    }

    // ---- structural location helpers ----

    private ReadStatus Unbound(out FontDiagnostic diagnostic) =>
        ReadWire.Fail(ReadStatus.Uninitialized, DiagnosticCode.UnboundView, Location, out diagnostic);

    private readonly record struct IndexSpan(int Start, int Count, int OffSize, int OffsetsAt,
        int DataAt, int End)
    {
        internal int Length => End - Start;
    }

    private ReadStatus LocateIndexes(out IndexSpan names, out IndexSpan tops,
        out IndexSpan strings, out IndexSpan globals, out FontDiagnostic diagnostic)
    {
        names = default; tops = default; strings = default; globals = default;
        if (!IsBound)
            return Unbound(out diagnostic);
        byte hdrSize = _data[2];
        ReadStatus status = LocateIndex(hdrSize, out names, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = LocateIndex(names.End, out tops, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = LocateIndex(tops.End, out strings, out diagnostic);
        if (status != ReadStatus.Done) return status;
        return LocateIndex(strings.End, out globals, out diagnostic);
    }

    /// <summary>Mechanical INDEX delimitation shared by reads and strict validation:
    /// count/offSize/1-based monotone offsets with every byte inside the table.</summary>
    private ReadStatus LocateIndex(int at, out IndexSpan index, out FontDiagnostic diagnostic)
    {
        index = default;
        if (!IsBound)
            return Unbound(out diagnostic);
        // The addition must be done in 64-bit: a corrupt offset can be near int.MaxValue, and
        // `at + 2` would overflow (the project builds with checked arithmetic) instead of
        // reporting the range violation the caller is owed.
        if (at < 0 || (long)at + 2 > _data.Length)
            return ReadWire.Fail(ReadStatus.Truncated, DiagnosticCode.TruncatedData,
                Location.At(DataField.Count, (ulong)(uint)Math.Max(0, at)), out diagnostic,
                (ulong)_data.Length, (ulong)((long)Math.Max(0, at) + 2));
        int start = at;
        int count = ReadWire.U16(_data, at);
        if (count == 0)
        {
            index = new IndexSpan(start, 0, 0, 0, 0, start + 2);
            diagnostic = default;
            return ReadStatus.Done;
        }
        // Same 64-bit care as above: `at` can be a corrupt offset, so neither `at + 2` nor
        // `at + 3` may be computed in 32-bit arithmetic.
        if ((long)at + 3 > _data.Length)
            return ReadWire.Fail(ReadStatus.Truncated, DiagnosticCode.TruncatedData,
                Location.At(DataField.Length, (ulong)(uint)((long)at + 2)), out diagnostic,
                (ulong)_data.Length, (ulong)((long)at + 3));
        int offSize = _data[at + 2];
        if (offSize is < 1 or > 4)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.At(DataField.Length, (ulong)(uint)(at + 2)), out diagnostic,
                unchecked((uint)offSize), 4);
        long offsetsAt = at + 3;
        long offsetBytes = (long)(count + 1) * offSize;
        if (offsetsAt + offsetBytes > _data.Length)
            return ReadWire.Fail(ReadStatus.Truncated, DiagnosticCode.TruncatedData,
                Location.At(DataField.Count, (uint)at), out diagnostic,
                (ulong)_data.Length, (ulong)(offsetsAt + offsetBytes));
        long dataAt = offsetsAt + offsetBytes;
        long previous = ReadOffsetAt(offsetsAt, offSize);
        if (previous != 1)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.At(DataField.Offset, (ulong)offsetsAt), out diagnostic,
                (ulong)previous, 1);
        for (int i = 1; i <= count; i++)
        {
            long next = ReadOffsetAt(offsetsAt + (long)i * offSize, offSize);
            if (next < previous)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    Location.At(DataField.Offset, (ulong)(offsetsAt + (long)i * offSize)),
                    out diagnostic, (ulong)next, (ulong)previous);
            previous = next;
        }
        long end = dataAt + previous - 1;
        if (end > _data.Length)
            return ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.RangeOutsideSource,
                Location.At(DataField.Offset, (ulong)dataAt), out diagnostic,
                (ulong)end, (ulong)_data.Length);
        index = new IndexSpan(start, count, offSize, (int)offsetsAt, (int)dataAt, (int)end);
        diagnostic = default;
        return ReadStatus.Done;
    }

    private long ReadOffsetAt(long at, int offSize)
    {
        long value = 0;
        for (int i = 0; i < offSize; i++)
            value = (value << 8) | _data[(int)at + i];
        return value;
    }

    private ReadStatus ReadIndexItem(IndexSpan index, int ordinal,
        out ReadOnlySpan<byte> item, out FontDiagnostic diagnostic)
    {
        item = default;
        ReadStatus status = IndexItemBounds(index, ordinal, out int itemStart, out int itemLength,
            out diagnostic);
        if (status != ReadStatus.Done) return status;
        item = _data.Slice(itemStart, itemLength);
        diagnostic = default;
        return ReadStatus.Done;
    }

    private ReadStatus IndexItemBounds(IndexSpan index, int ordinal, out int itemStart,
        out int itemLength, out FontDiagnostic diagnostic)
    {
        itemStart = 0;
        itemLength = 0;
        if (!IsBound)
            return Unbound(out diagnostic);
        if (ordinal < 0 || (uint)ordinal >= (uint)index.Count)
            return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                Location.At(DataField.Count, (uint)index.OffsetsAt, ordinal), out diagnostic,
                unchecked((ulong)(long)ordinal), (uint)index.Count);
        long startOffset = ReadOffsetAt(index.OffsetsAt + (long)ordinal * index.OffSize, index.OffSize);
        long endOffset = ReadOffsetAt(index.OffsetsAt + (long)(ordinal + 1) * index.OffSize, index.OffSize);
        itemStart = (int)(index.DataAt + startOffset - 1);
        itemLength = (int)(endOffset - startOffset);
        diagnostic = default;
        return ReadStatus.Done;
    }

    private ReadStatus LocateTopDict(out int start, out int length, out FontDiagnostic diagnostic)
    {
        start = 0;
        length = 0;
        ReadStatus status = LocateIndexes(out var names, out var tops, out _, out _, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (tops.Count != 1)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.At(DataField.Count, (uint)tops.Start), out diagnostic,
                (uint)tops.Count, 1);
        return IndexItemBounds(tops, 0, out start, out length, out diagnostic);
    }

    private ReadStatus LocateCharStrings(out IndexSpan index, out FontDiagnostic diagnostic)
    {
        index = default;
        ReadStatus status = TryGetTopDictIntegers(Cff1Layout.OpCharStrings, out var operands, out diagnostic);
        if (status != ReadStatus.Done) return status;
        int at = operands![0];
        if (at < 0 || at > _data.Length)
            return ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.RangeOutsideSource,
                Location.At(DataField.Offset, (ulong)(uint)Math.Max(0, at)), out diagnostic,
                (uint)Math.Max(0, at), (ulong)_data.Length);
        return LocateIndex(at, out index, out diagnostic);
    }

    private ReadStatus LocatePrivate(out int start, out int length, out FontDiagnostic diagnostic)
    {
        start = 0;
        length = 0;
        ReadStatus status = TryGetTopDictIntegers(Cff1Layout.OpPrivate, out var operands, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (operands!.Length != 2)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.At(DataField.Count, (uint)operands.Length), out diagnostic,
                (uint)operands.Length, 2);
        length = operands[0];
        start = operands[1];
        if (length < 0 || start < 0 || (long)start + length > _data.Length)
            return ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.RangeOutsideSource,
                Location.At(DataField.Offset, (ulong)(uint)Math.Max(0, start)), out diagnostic,
                (ulong)Math.Max(0, (long)start + length), (ulong)_data.Length);
        diagnostic = default;
        return ReadStatus.Done;
    }

    private ReadStatus LocateLocalSubrs(out IndexSpan index, out FontDiagnostic diagnostic)
    {
        index = default;
        ReadStatus status = LocatePrivate(out int privateStart, out int privateLength, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = ParseDict(privateStart, privateLength, topScope: false, out var entries, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (!TryFindEntry(entries, Cff1Layout.OpSubrs, out var entry))
            return ReadWire.Fail(ReadStatus.NotFound, DiagnosticCode.TableNotFound,
                Location.At(DataField.Payload, (uint)privateStart), out diagnostic,
                (uint)Cff1Layout.OpSubrs);
        status = EntryIntegers(entry!, out var operands, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (operands[0] < 0 || operands[0] > _data.Length - privateStart)
            return ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.RangeOutsideSource,
                Location.At(DataField.Offset, (uint)privateStart), out diagnostic,
                (uint)Math.Max(0, operands[0]), (ulong)(_data.Length - privateStart));
        return LocateIndex(privateStart + operands[0], out index, out diagnostic);
    }

    // ---- DICT parsing ----

    private sealed class DictEntryData
    {
        internal int Operator;
        internal int EntryStart;
        internal int EntryEnd;
        internal int OperandStart;
        internal int OperandEnd;
        internal int?[] Operands = [];
        internal int[] RealOffsets = [];
        internal int[] RealLengths = [];

        internal int RealCount => RealOffsets.Length;
    }

    private static bool TryFindEntry(List<DictEntryData> entries, int op, out DictEntryData? entry)
    {
        foreach (var candidate in entries)
        {
            if (candidate.Operator == op)
            {
                entry = candidate;
                return true;
            }
        }
        entry = null;
        return false;
    }

    /// <summary>Strict DICT entry decode (Top or Private scope): arity whitelist with
    /// reversed name-keyed rules, operand stack cap, duplicate operator rejection,
    /// real tokens scanned structurally and preserved as raw spans.</summary>
    private ReadStatus ParseDict(int start, int length, bool topScope,
        out List<DictEntryData> entries, out FontDiagnostic diagnostic)
    {
        entries = [];
        int end = start + length;
        int at = start;
        int entryStart = start;
        int operandStart = start;
        var operands = new List<int?>(8);
        var realOffsets = new List<int>(4);
        var realLengths = new List<int>(4);
        while (at < end)
        {
            byte b = _data[at++];
            int? number;
            if (operands.Count == 0)
                operandStart = at - 1;
            if (b is >= 32 and <= 246)
            {
                number = b - 139;
            }
            else if (b is >= 247 and <= 250)
            {
                if (at >= end)
                    return FailDictPayload(at, out diagnostic);
                number = (b - 247) * 256 + _data[at++] + 108;
            }
            else if (b is >= 251 and <= 254)
            {
                if (at >= end)
                    return FailDictPayload(at, out diagnostic);
                number = -(b - 251) * 256 - _data[at++] - 108;
            }
            else if (b is 28 or 29)
            {
                int size = b == 28 ? 2 : 4;
                if (at + size > end)
                    return FailDictPayload(at, out diagnostic);
                number = size == 2
                    ? BinaryPrimitives.ReadInt16BigEndian(_data.Slice(at, 2))
                    : BinaryPrimitives.ReadInt32BigEndian(_data.Slice(at, 4));
                at += size;
            }
            else if (b == 30)
            {
                if (at >= end)
                    return FailDictPayload(at, out diagnostic);
                ReadStatus status = ScanReal(at, end, out int tokenEnd, out diagnostic);
                if (status != ReadStatus.Done) return status;
                realOffsets.Add(at);
                realLengths.Add(tokenEnd - at);
                number = null;
                at = tokenEnd;
            }
            else
            {
                int op = b;
                int operatorStart = at - 1;
                if (b == 12)
                {
                    if (at >= end)
                        return FailDictPayload(at, out diagnostic);
                    op = 0x0c00 | _data[at++];
                }
                if (topScope && op is Cff1Layout.OpRos or Cff1Layout.OpFdArray or Cff1Layout.OpFdSelect)
                    return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedVersion,
                        Location.At(DataField.Payload, (uint)operatorStart), out diagnostic, (uint)op);
                int arity = Cff1Layout.DictArity(topScope, op);
                if (arity == -2)
                    return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.InvalidValue,
                        Location.At(DataField.Payload, (uint)operatorStart), out diagnostic, (uint)op);
                if (operands.Count == 0 || (arity >= 0 && operands.Count != arity))
                    return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                        Location.At(DataField.Count, (uint)entryStart), out diagnostic,
                        (uint)operands.Count, (uint)Math.Max(0, arity));
                foreach (var existing in entries)
                {
                    if (existing.Operator == op)
                        return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                            Location.At(DataField.Count, (uint)entryStart), out diagnostic, (uint)op);
                }
                entries.Add(new DictEntryData
                {
                    Operator = op,
                    EntryStart = entryStart,
                    EntryEnd = at,
                    OperandStart = operandStart,
                    OperandEnd = operatorStart,
                    Operands = [.. operands],
                    RealOffsets = [.. realOffsets],
                    RealLengths = [.. realLengths]
                });
                operands.Clear();
                realOffsets.Clear();
                realLengths.Clear();
                entryStart = at;
                continue;
            }
            if (operands.Count >= Cff1Layout.MaxOperandStack)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    Location.At(DataField.Count, (uint)at), out diagnostic,
                    (uint)(operands.Count + 1), (uint)Cff1Layout.MaxOperandStack);
            operands.Add(number);
        }
        if (operands.Count != 0)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.At(DataField.Payload, (uint)entryStart), out diagnostic, (uint)operands.Count);
        diagnostic = default;
        return ReadStatus.Done;
    }

    private ReadStatus FailDictPayload(int at, out FontDiagnostic diagnostic) =>
        ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
            Location.At(DataField.Payload, (uint)at), out diagnostic);

    /// <summary>Structural real scan: nibble stream with a 0xF terminator, no reserved
    /// 0xD nibble, bounded digit count. The value itself is never decoded.</summary>
    private ReadStatus ScanReal(int at, int end, out int tokenEnd, out FontDiagnostic diagnostic)
    {
        tokenEnd = 0;
        int digits = 0;
        int p = at;
        while (true)
        {
            if (p >= end)
                return FailDictPayload(at, out diagnostic);
            byte b = _data[p++];
            for (int shift = 4; shift >= 0; shift -= 4)
            {
                int nibble = (b >> shift) & 15;
                if (nibble == 15)
                {
                    if (digits == 0)
                        return FailDictPayload(at, out diagnostic);
                    tokenEnd = p;
                    diagnostic = default;
                    return ReadStatus.Done;
                }
                if (nibble == 13)
                    return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                        Location.At(DataField.Payload, (uint)(p - 1)), out diagnostic, (uint)nibble);
                digits++;
                if (digits > Cff1Layout.MaxRealNibbleDigits)
                    return ReadWire.Fail(ReadStatus.ResourceLimit, DiagnosticCode.BudgetExceeded,
                        Location.At(DataField.Length, (uint)at), out diagnostic,
                        (uint)digits, (uint)Cff1Layout.MaxRealNibbleDigits);
            }
        }
    }

    private ReadStatus ReadDictIntegers(int start, int length, bool topScope, int operatorKey,
        out int[]? operands, out FontDiagnostic diagnostic)
    {
        operands = null;
        ReadStatus status = ParseDict(start, length, topScope, out var entries, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (!TryFindEntry(entries, operatorKey, out var entry))
            return ReadWire.Fail(ReadStatus.NotFound, DiagnosticCode.TableNotFound,
                Location.At(DataField.Payload, (uint)start), out diagnostic, (uint)operatorKey);
        return EntryIntegers(entry!, out operands, out diagnostic);
    }

    private ReadStatus EntryIntegers(DictEntryData entry, out int[] operands, out FontDiagnostic diagnostic)
    {
        operands = [];
        var result = new int[entry.Operands.Length];
        for (int i = 0; i < entry.Operands.Length; i++)
        {
            int? operand = entry.Operands[i];
            if (operand is null)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    Location.At(DataField.Payload, (uint)entry.OperandStart), out diagnostic,
                    (uint)entry.Operator);
            result[i] = operand.Value;
        }
        operands = result;
        diagnostic = default;
        return ReadStatus.Done;
    }

    // ---- charset / encoding ----

    private ReadStatus CharsetFormFor(int value, out Cff1CharsetForm form, out FontDiagnostic diagnostic)
    {
        form = default;
        if (value < 0)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.At(DataField.Offset, unchecked((ulong)(long)value)), out diagnostic,
                unchecked((ulong)(long)value), 0);
        if (value is 0 or 1 or 2)
        {
            form = new Cff1CharsetForm(Predefined: true, Format: value);
            diagnostic = default;
            return ReadStatus.Done;
        }
        if (value > _data.Length - 1)
            return ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.RangeOutsideSource,
                Location.At(DataField.Offset, (ulong)value), out diagnostic,
                (ulong)value, (ulong)_data.Length);
        int format = _data[value];
        if (format > 2)
            return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedVersion,
                Location.At(DataField.Version, (uint)value), out diagnostic, (uint)format, 2);
        form = new Cff1CharsetForm(Predefined: false, Format: format);
        diagnostic = default;
        return ReadStatus.Done;
    }

    private ReadStatus EncodingFormFor(int value, out Cff1EncodingForm form, out FontDiagnostic diagnostic)
    {
        form = default;
        if (value < 0)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.At(DataField.Offset, unchecked((ulong)(long)value)), out diagnostic,
                unchecked((ulong)(long)value), 0);
        if (value is 0 or 1)
        {
            form = new Cff1EncodingForm(Predefined: true, Format: value, HasSupplement: false);
            diagnostic = default;
            return ReadStatus.Done;
        }
        if (value > _data.Length - 1)
            return ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.RangeOutsideSource,
                Location.At(DataField.Offset, (ulong)value), out diagnostic,
                (ulong)value, (ulong)_data.Length);
        int format = _data[value];
        int low = format & 0x7f;
        if (low > 1)
            return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedVersion,
                Location.At(DataField.Version, (uint)value), out diagnostic, (uint)format, 1);
        form = new Cff1EncodingForm(Predefined: false, Format: low, HasSupplement: (format & 0x80) != 0);
        diagnostic = default;
        return ReadStatus.Done;
    }

    private ReadStatus PredefinedCharsetSid(int predefined, int gid, out int sid, out FontDiagnostic diagnostic)
    {
        sid = 0;
        var table = Cff1Predefined.Charset(predefined);
        if (gid >= table.Length)
            return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                Location.At(DataField.Cid, 0, gid), out diagnostic,
                unchecked((ulong)(long)gid), (uint)table.Length);
        sid = table[gid];
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>SID of one glyph in a custom charset at the given offset; ranges are
    /// walked in wire order (they tile glyph ids; SID order is not required).</summary>
    private ReadStatus CustomCharsetSid(int charsetOffset, int gid, out int sid, out FontDiagnostic diagnostic)
    {
        sid = 0;
        if (gid == 0)
        {
            diagnostic = default;
            return ReadStatus.Done;
        }
        if (charsetOffset < 0 || charsetOffset >= _data.Length)
            return ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.RangeOutsideSource,
                Location.At(DataField.Offset, (ulong)(uint)Math.Max(0, charsetOffset)), out diagnostic,
                (uint)Math.Max(0, charsetOffset), (ulong)_data.Length);
        int format = _data[charsetOffset];
        long pos = charsetOffset + 1;
        if (format == 0)
        {
            long at = pos + 2L * (gid - 1);
            if (gid < 1 || at + 2 > _data.Length)
                return ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.RangeOutsideSource,
                    Location.At(DataField.Offset, (ulong)(uint)Math.Max(0, (int)Math.Min(int.MaxValue, at))),
                    out diagnostic, (ulong)Math.Max(0, at), (ulong)_data.Length);
            sid = ReadWire.U16(_data, (int)at);
            diagnostic = default;
            return ReadStatus.Done;
        }
        long covered = 1;
        while (covered <= gid)
        {
            int length = format == 1 ? 3 : 4;
            if (pos + length > _data.Length)
                return ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.RangeOutsideSource,
                    Location.At(DataField.Offset, (ulong)pos), out diagnostic,
                    (ulong)(pos + length), (ulong)_data.Length);
            int first = ReadWire.U16(_data, (int)pos);
            int left = format == 1 ? _data[(int)pos + 2] : ReadWire.U16(_data, (int)pos + 2);
            if (gid <= covered + left)
            {
                sid = first + (int)(gid - covered);
                diagnostic = default;
                return ReadStatus.Done;
            }
            covered += left + 1;
            pos += length;
        }
        return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
            Location.At(DataField.Count, (ulong)pos), out diagnostic, (uint)gid);
    }

    /// <summary>Custom encoding lookup of one code. Returns a direct glyph for the base
    /// tables (format 0 positional codes with 0 as unmapped marker; format 1 consecutive
    /// ranges) or the supplement SID to resolve by name; -1/-1 when the code is unmapped.</summary>
    private ReadStatus CustomEncodingGlyph(int encodingOffset, int code, out int glyph,
        out int supplementSid, out FontDiagnostic diagnostic)
    {
        glyph = -1;
        supplementSid = -1;
        if (encodingOffset < 0 || encodingOffset >= _data.Length)
            return ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.RangeOutsideSource,
                Location.At(DataField.Offset, (ulong)(uint)Math.Max(0, encodingOffset)), out diagnostic,
                (uint)Math.Max(0, encodingOffset), (ulong)_data.Length);
        int format = _data[encodingOffset];
        int low = format & 0x7f;
        long pos = encodingOffset + 1;
        if (low == 0)
        {
            if (pos >= _data.Length)
                return ReadWire.Fail(ReadStatus.Truncated, DiagnosticCode.TruncatedData,
                    Location.At(DataField.Count, (ulong)pos), out diagnostic,
                    (ulong)_data.Length, (ulong)(pos + 1));
            int nCodes = _data[(int)pos++];
            for (int i = 0; i < nCodes; i++)
            {
                if (pos + i >= _data.Length)
                    return ReadWire.Fail(ReadStatus.Truncated, DiagnosticCode.TruncatedData,
                        Location.At(DataField.CharCode, (ulong)(pos + i)), out diagnostic,
                        (ulong)_data.Length, (ulong)(pos + i + 1));
                int tableCode = _data[(int)(pos + i)];
                if (tableCode != 0 && tableCode == code)
                {
                    glyph = i + 1;
                    diagnostic = default;
                    return ReadStatus.Done;
                }
            }
            pos += nCodes;
        }
        else
        {
            if (pos >= _data.Length)
                return ReadWire.Fail(ReadStatus.Truncated, DiagnosticCode.TruncatedData,
                    Location.At(DataField.Count, (ulong)pos), out diagnostic,
                    (ulong)_data.Length, (ulong)(pos + 1));
            int nRanges = _data[(int)pos++];
            long glyphId = 1;
            for (int r = 0; r < nRanges; r++)
            {
                if (pos + 2 > _data.Length)
                    return ReadWire.Fail(ReadStatus.Truncated, DiagnosticCode.TruncatedData,
                        Location.At(DataField.CharCode, (ulong)pos), out diagnostic,
                        (ulong)_data.Length, (ulong)(pos + 2));
                int first = _data[(int)pos];
                int nLeft = _data[(int)pos + 1];
                if (code >= first && code <= first + nLeft)
                {
                    glyph = (int)(glyphId + (code - first));
                    diagnostic = default;
                    return ReadStatus.Done;
                }
                glyphId += nLeft + 1;
                pos += 2;
            }
        }
        if ((format & 0x80) != 0)
        {
            if (pos >= _data.Length)
                return ReadWire.Fail(ReadStatus.Truncated, DiagnosticCode.TruncatedData,
                    Location.At(DataField.Count, (ulong)pos), out diagnostic,
                    (ulong)_data.Length, (ulong)(pos + 1));
            int nSups = _data[(int)pos++];
            for (int s = 0; s < nSups; s++)
            {
                if (pos + 3 > _data.Length)
                    return ReadWire.Fail(ReadStatus.Truncated, DiagnosticCode.TruncatedData,
                        Location.At(DataField.CharCode, (ulong)pos), out diagnostic,
                        (ulong)_data.Length, (ulong)(pos + 3));
                if (_data[(int)pos] == code)
                {
                    supplementSid = ReadWire.U16(_data, (int)pos + 1);
                    diagnostic = default;
                    return ReadStatus.Done;
                }
                pos += 3;
            }
        }
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>First glyph whose charset SID resolves to the given SID's string;
    /// SID 0 names .notdef (glyph 0) directly.</summary>
    private ReadStatus GlyphForSidName(int sid, out bool mapped, out int glyph, out FontDiagnostic diagnostic)
    {
        mapped = false;
        glyph = 0;
        if (sid == 0)
        {
            mapped = true;
            glyph = 0;
            diagnostic = default;
            return ReadStatus.Done;
        }
        ReadStatus status = TryGetStringIndexCount(out int stringCount, out diagnostic);
        if (status != ReadStatus.Done) return status;
        int bound = Cff1Layout.StandardStringCount + stringCount;
        if (sid >= bound)
            return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                Location.At(DataField.Cid, 0), out diagnostic, (uint)sid, (uint)bound);
        status = TryGetSidNameBytes(sid, out var wanted, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = TryGetCharStringsCount(out int numGlyphs, out diagnostic);
        if (status != ReadStatus.Done) return status;
        for (int gid = 1; gid < numGlyphs; gid++)
        {
            status = TryGetSidForGlyph(gid, out int candidate, out diagnostic);
            if (status != ReadStatus.Done) return status;
            if (candidate == 0)
                continue;
            status = TryGetSidNameBytes(candidate, out var name, out diagnostic);
            if (status != ReadStatus.Done) return status;
            if (name.SequenceEqual(wanted))
            {
                mapped = true;
                glyph = gid;
                diagnostic = default;
                return ReadStatus.Done;
            }
        }
        diagnostic = default;
        return ReadStatus.Done;
    }

    private ReadStatus TryGetSidNameBytes(int sid, out ReadOnlySpan<byte> name, out FontDiagnostic diagnostic)
    {
        name = default;
        if (sid < Cff1Layout.StandardStringCount)
        {
            name = Cff1Predefined.StandardNameBytes(sid);
            diagnostic = default;
            return ReadStatus.Done;
        }
        ReadStatus status = TryGetStringIndex(sid - Cff1Layout.StandardStringCount, out name, out diagnostic);
        return status;
    }
}
