using FontWeave.Core;

namespace FontWeave.Tables;

/// <summary>Which of the two CFF1 forms a `CFF ` table is.
///
/// The distinction is structural, not cosmetic: a CID-keyed font keeps its Private DICT per
/// Font DICT and has no Encoding, so the two forms cannot share one result model.</summary>
public enum Cff1Flavor : byte
{
    /// <summary>Name-keyed: glyphs have names, one top-level Private DICT, optional Encoding.</summary>
    NameKeyed = 0,

    /// <summary>CID-keyed: ROS plus FDArray/FDSelect, Private DICT per FD.</summary>
    CidKeyed = 1
}

/// <summary>How a CID-keyed font maps glyphs to Font DICTs.
///
/// TN5176 defines exactly formats 0 and 3 for CFF1. Format 4 exists only in CFF2 and is
/// never accepted here, so this enum cannot represent it.</summary>
public enum CidFdSelectFormat : byte
{
    /// <summary>One FD index per glyph.</summary>
    PerGlyph = 0,

    /// <summary>Runs of glyphs, closed by a sentinel equal to the glyph count.</summary>
    Ranges = 3
}

/// <summary>One FDSelect run in format 3: glyphs from <see cref="FirstGlyph"/> up to the next
/// run's first glyph (or the sentinel) belong to <see cref="FdIndex"/>.</summary>
public readonly record struct CidFdRange(int Ordinal, int FirstGlyph, int FdIndex);

/// <summary>The FDArray plus the glyph-to-FD mapping.
///
/// The mapping is kept in its wire form: format 0 stays a per-glyph array and format 3 stays
/// a run list, so re-encoding preserves the original form rather than normalizing one into
/// the other. Format conversion is deliberately not offered.</summary>
public sealed class CidFdSelect
{
    private readonly int[]? _perGlyph;
    private readonly CidFdRange[]? _ranges;

    private CidFdSelect(CidFdSelectFormat format, int numGlyphs, int fdCount,
        int[]? perGlyph, CidFdRange[]? ranges, DataLocation location, ulong offset)
    {
        Format = format;
        NumGlyphs = numGlyphs;
        FdCount = fdCount;
        _perGlyph = perGlyph;
        _ranges = ranges;
        Location = location;
        Offset = offset;
    }

    internal static CidFdSelect FromPerGlyph(int numGlyphs, int fdCount, int[] perGlyph,
        DataLocation location, ulong offset) =>
        new(CidFdSelectFormat.PerGlyph, numGlyphs, fdCount, perGlyph, null, location, offset);

    internal static CidFdSelect FromRanges(int numGlyphs, int fdCount, CidFdRange[] ranges,
        DataLocation location, ulong offset) =>
        new(CidFdSelectFormat.Ranges, numGlyphs, fdCount, null, ranges, location, offset);

    public CidFdSelectFormat Format { get; }

    public int NumGlyphs { get; }

    /// <summary>Number of Font DICTs the mapping actually reaches, as the greatest referenced
    /// index plus one. Cross-checked against the FDArray count by the strict validator.</summary>
    public int FdCount { get; }

    public DataLocation Location { get; }

    /// <summary>Byte offset of the FDSelect structure within the CFF table.</summary>
    public ulong Offset { get; }

    public int RangeCount => _ranges?.Length ?? 0;

    /// <summary>The per-glyph FD array, only for <see cref="CidFdSelectFormat.PerGlyph"/>.</summary>
    public ReadOnlySpan<int> PerGlyph =>
        _perGlyph is null ? default : _perGlyph;

    /// <summary>The run list, only for <see cref="CidFdSelectFormat.Ranges"/>.</summary>
    public ReadOnlySpan<CidFdRange> Ranges =>
        _ranges is null ? default : _ranges;

    /// <summary>Encoded length: one format byte plus the payload for the current format.</summary>
    public int EncodedLength => Format == CidFdSelectFormat.PerGlyph
        ? 1 + NumGlyphs
        : 1 + 2 + RangeCount * 3 + 2;

    /// <summary>Resolves the FD index for a glyph.</summary>
    public ReadStatus TryGetFdIndex(int glyphId, out int fdIndex, out FontDiagnostic diagnostic)
    {
        fdIndex = 0;
        if (glyphId < 0 || glyphId >= NumGlyphs)
            return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                Location.At(DataField.GlyphId, Offset, glyphId), out diagnostic,
                unchecked((ulong)(long)glyphId), (ulong)NumGlyphs);
        if (_perGlyph is not null)
        {
            fdIndex = _perGlyph[glyphId];
            diagnostic = default;
            return ReadStatus.Done;
        }
        var ranges = _ranges!;
        // Runs are strictly increasing, so the containing run is the last one whose first
        // glyph does not exceed the query.
        int low = 0, high = ranges.Length - 1, found = 0;
        while (low <= high)
        {
            int mid = low + ((high - low) >> 1);
            if (ranges[mid].FirstGlyph <= glyphId) { found = mid; low = mid + 1; }
            else high = mid - 1;
        }
        fdIndex = ranges[found].FdIndex;        diagnostic = default;
        return ReadStatus.Done;
    }
}

/// <summary>One Font DICT: its DICT entries plus the Private DICT and Local Subrs it owns.
///
/// Either may be absent, which is legal: a real witness has FDs with no Private DICT at all
/// and FDs with a Private DICT but no Local Subrs.</summary>
public sealed class CidFontDict
{
    private readonly byte[] _rawBytes;
    private readonly Cff1DictEntry[] _entries;
    private readonly Cff1DictEntry[] _privateEntries;
    private readonly byte[][] _localSubrs;

    internal CidFontDict(int ordinal, byte[] rawBytes, Cff1DictEntry[] entries,
        int privateOffset, int privateSize, Cff1DictEntry[] privateEntries, bool hasLocalSubrs,
        byte[][] localSubrs, DataLocation location)
    {
        Ordinal = ordinal;
        _rawBytes = rawBytes;
        _entries = entries;
        PrivateOffset = privateOffset;
        PrivateSize = privateSize;
        _privateEntries = privateEntries;
        HasLocalSubrs = hasLocalSubrs;
        _localSubrs = localSubrs;
        Location = location;
    }

    public int Ordinal { get; }

    public DataLocation Location { get; }

    /// <summary>The Font DICT's own bytes, preserved verbatim.</summary>
    public ReadOnlySpan<byte> RawBytes => _rawBytes;

    public ReadOnlySpan<Cff1DictEntry> Entries => _entries;

    /// <summary>Offset of the Private DICT, or -1 when this FD declares none.</summary>
    public int PrivateOffset { get; }

    public int PrivateSize { get; }

    public bool HasPrivate => PrivateSize >= 0;

    public ReadOnlySpan<Cff1DictEntry> PrivateEntries => _privateEntries;

    public bool HasLocalSubrs { get; }

    public int LocalSubrCount => _localSubrs.Length;

    /// <summary>Local Subrs as verbatim byte programs. Never interpreted.</summary>
    public ReadOnlySpan<byte[]> LocalSubrs => _localSubrs;

    /// <summary>Byte span the Private DICT occupies, or null when absent.</summary>
    public (long First, long Last)? PrivateSpan => HasPrivate
        ? (PrivateOffset, (long)PrivateOffset + PrivateSize) : null;
}

/// <summary>One entry of the ROS operator: registry SID, ordering SID and supplement.</summary>
public readonly record struct CidRos(int RegistrySid, int OrderingSid, int Supplement);

/// <summary>The CID-keyed view of a `CFF ` table.
///
/// This is not a second parser: the shared <see cref="Cff1View"/> reads the Header, the four
/// INDEXes, the top DICT and the CharStrings, and the CID part supplies only what is
/// genuinely different — ROS/CIDCount, the FDArray, FDSelect and the per-FD Private DICTs.
/// </summary>
public sealed class CidCff1Table
{
    private readonly CidFontDict[] _fontDicts;
    private readonly int[] _charsetCids;
    private readonly byte[][] _charStrings;
    private readonly Cff1DictEntry[] _topDict;
    private readonly byte[][] _strings;
    private readonly byte[][] _globalSubrs;
    private readonly byte[] _nameBytes;

    internal CidCff1Table(byte headerMajor, byte headerMinor, byte headerHdrSize,
        byte headerOffSize, byte[] nameBytes, Cff1DictEntry[] topDict, byte[][] strings,
        byte[][] globalSubrs,
        CidRos ros, int cidCount, CidFdSelect fdSelect,
        CidFontDict[] fontDicts, int[] charsetCids, byte[][] charStrings,
        Cff1CharsetForm charsetForm, DataLocation location, ulong fdArrayOffset,
        ulong charsetOffset, ulong fdSelectOffset, ulong charStringsOffset)
    {
        HeaderMajor = headerMajor;
        HeaderMinor = headerMinor;
        HeaderHdrSize = headerHdrSize;
        HeaderOffSize = headerOffSize;
        _nameBytes = nameBytes;
        _topDict = topDict;
        _strings = strings;
        _globalSubrs = globalSubrs;
        Ros = ros;
        CidCount = cidCount;
        FdSelect = fdSelect;
        _fontDicts = fontDicts;
        _charsetCids = charsetCids;
        _charStrings = charStrings;
        CharsetForm = charsetForm;
        Location = location;
        FdArrayOffset = fdArrayOffset;
        CharsetOffset = charsetOffset;
        FdSelectOffset = fdSelectOffset;
        CharStringsOffset = charStringsOffset;
    }

    public byte HeaderMajor { get; }

    public byte HeaderMinor { get; }

    public byte HeaderHdrSize { get; }

    /// <summary>The header's offSize field, preserved from the source so recompilation does
    /// not needlessly change a byte it was not asked to edit.</summary>
    public byte HeaderOffSize { get; }

    /// <summary>The private font name from the Name INDEX.</summary>
    public ReadOnlySpan<byte> NameBytes => _nameBytes;

    public string FontName => System.Text.Encoding.Latin1.GetString(_nameBytes);

    /// <summary>The Top DICT entries, preserved verbatim so recompilation can re-emit the
    /// keys it does not edit.</summary>
    public ReadOnlySpan<Cff1DictEntry> TopDict => _topDict;

    public ReadOnlySpan<byte[]> Strings => _strings;

    public ReadOnlySpan<byte[]> GlobalSubrs => _globalSubrs;

    public ulong CharsetOffset { get; }

    public ulong FdSelectOffset { get; }

    public ulong CharStringsOffset { get; }

    public DataLocation Location { get; }

    /// <summary>The ROS triple identifying the character collection.</summary>
    public CidRos Ros { get; }

    /// <summary>CIDCount from the top DICT, or -1 when the key is absent (the spec default is
    /// 8720, and the field is optional, so absence is recorded rather than invented).</summary>
    public int CidCount { get; }

    public CidFdSelect FdSelect { get; }

    public int FdCount => _fontDicts.Length;

    public ReadOnlySpan<CidFontDict> FontDicts => _fontDicts;

    /// <summary>CID for each glyph, from the charset. Length equals the glyph count.</summary>
    public ReadOnlySpan<int> CharsetCids => _charsetCids;

    public Cff1CharsetForm CharsetForm { get; }

    public int NumGlyphs => _charStrings.Length;

    /// <summary>CharStrings as verbatim programs. Never interpreted, never validated.</summary>
    public ReadOnlySpan<byte[]> CharStrings => _charStrings;

    public ulong FdArrayOffset { get; }

    public CidFontDict this[int fd] => _fontDicts[fd];

    public ReadStatus TryGetFd(int ordinal, out CidFontDict? fontDict,
        out FontDiagnostic diagnostic)
    {
        fontDict = null;
        if (ordinal < 0 || ordinal >= _fontDicts.Length)
            return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                Location.At(DataField.Count, FdArrayOffset, ordinal), out diagnostic,
                unchecked((ulong)(long)ordinal), (ulong)_fontDicts.Length);
        fontDict = _fontDicts[ordinal];
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>The FD index serving a glyph.</summary>
    public ReadStatus TryGetFdIndex(int glyphId, out int fdIndex,
        out FontDiagnostic diagnostic) =>
        FdSelect.TryGetFdIndex(glyphId, out fdIndex, out diagnostic);

    /// <summary>The CID of a glyph, as the charset declares it.</summary>
    public ReadStatus TryGetCid(int glyphId, out int cid, out FontDiagnostic diagnostic)
    {
        cid = 0;
        if (glyphId < 0 || glyphId >= CharsetCids.Length)
            return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                Location.At(DataField.GlyphId, 0, glyphId), out diagnostic,
                unchecked((ulong)(long)glyphId), (ulong)CharsetCids.Length);
        cid = CharsetCids[glyphId];
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>The Local Subrs serving a glyph, via its FD. Empty when the FD declares none.</summary>
    public ReadStatus TryGetLocalSubrs(int glyphId, out ReadOnlySpan<byte[]> subrs,
        out FontDiagnostic diagnostic)
    {
        subrs = default;
        ReadStatus status = TryGetFdIndex(glyphId, out int fd, out diagnostic);
        if (status != ReadStatus.Done) return status;
        subrs = _fontDicts[fd].LocalSubrs;
        diagnostic = default;
        return ReadStatus.Done;
    }
}
