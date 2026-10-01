using FontWeave.Core;

namespace FontWeave.Tables;

public readonly record struct CmapMapping(uint CharacterCode, ushort GlyphId);

public readonly record struct CmapRecord(ushort PlatformId, ushort EncodingId,
    uint SourceOffset, int SubtableIndex);

/// <summary>Base for one materialized subtable: format identity and source provenance.
/// SourceOffset is where the subtable started in its source table, not a live binding.</summary>
public abstract class CmapSubtable
{
    private protected CmapSubtable(ushort format, uint sourceOffset, DataLocation location)
    {
        Format = format; SourceOffset = sourceOffset; Location = location;
    }

    public ushort Format { get; }
    public uint SourceOffset { get; }
    public DataLocation Location { get; }

    /// <summary>Looks up one code point. The result is Done with mapped=false for
    /// "not mapped" (including GID 0, the format 4 sentinel and out-of-range codes);
    /// a non-Done status reports a structural defect, never a plain miss.</summary>
    public abstract ReadStatus TryMapCharacter(uint codePoint, out bool mapped, out uint glyphId,
        out FontDiagnostic diagnostic);
}

public readonly record struct CmapFormat4Segment(ushort StartCode, ushort EndCode,
    short IdDelta, ushort IdRangeOffset);

public sealed class CmapFormat4Subtable : CmapSubtable
{
    private readonly CmapFormat4Segment[] _segments;
    private readonly ushort[] _glyphIdArray;

    internal CmapFormat4Subtable(ushort language, CmapFormat4Segment[] segments,
        ushort[] glyphIdArray, uint sourceOffset, DataLocation location)
        : base(4, sourceOffset, location)
    {
        Language = language; _segments = segments; _glyphIdArray = glyphIdArray;
    }

    public ushort Language { get; }
    public int SegmentCount => _segments.Length;
    public ReadOnlySpan<CmapFormat4Segment> Segments => _segments;
    public ReadOnlySpan<ushort> GlyphIdArray => _glyphIdArray;

    public override ReadStatus TryMapCharacter(uint codePoint, out bool mapped, out uint glyphId,
        out FontDiagnostic diagnostic)
    {
        mapped = false; glyphId = 0;
        if (codePoint > 0xFFFF) { diagnostic = default; return ReadStatus.Done; }
        int low = 0, high = _segments.Length - 1, found = -1;
        while (low <= high)
        {
            int mid = low + (high - low) / 2;
            if (_segments[mid].EndCode >= codePoint) { found = mid; high = mid - 1; }
            else low = mid + 1;
        }
        if (found < 0 || _segments[found].StartCode > codePoint)
        {
            diagnostic = default; return ReadStatus.Done;
        }
        var segment = _segments[found];
        ushort resolved;
        if (segment.IdRangeOffset == 0)
        {
            resolved = unchecked((ushort)(codePoint + segment.IdDelta));
        }
        else
        {
            // Self-relative IdRangeOffset normalized into glyph-array uint16 units.
            long index = segment.IdRangeOffset / 2 + found - _segments.Length
                + (codePoint - segment.StartCode);
            if (index < 0 || index >= _glyphIdArray.Length)
                return ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.RangeOutsideSource,
                    Location.At(DataField.GlyphId,
                        (ulong)(16 + 6L * _segments.Length) + 2UL * (uint)found, found),
                    out diagnostic, (ulong)index, (ulong)_glyphIdArray.Length);
            ushort raw = _glyphIdArray[index];
            if (raw == 0) { diagnostic = default; return ReadStatus.Done; }
            resolved = unchecked((ushort)(raw + segment.IdDelta));
        }
        if (resolved == 0) { diagnostic = default; return ReadStatus.Done; }
        mapped = true; glyphId = resolved;
        diagnostic = default;
        return ReadStatus.Done;
    }
}

public readonly record struct CmapFormat12Group(uint StartCharCode, uint EndCharCode,
    uint StartGlyphID);

public sealed class CmapFormat12Subtable : CmapSubtable
{
    private readonly CmapFormat12Group[] _groups;

    internal CmapFormat12Subtable(CmapFormat12Group[] groups, uint sourceOffset,
        DataLocation location) : base(12, sourceOffset, location)
    {
        _groups = groups;
    }

    public int GroupCount => _groups.Length;
    public ReadOnlySpan<CmapFormat12Group> Groups => _groups;

    public override ReadStatus TryMapCharacter(uint codePoint, out bool mapped, out uint glyphId,
        out FontDiagnostic diagnostic)
    {
        mapped = false; glyphId = 0;
        int low = 0, high = _groups.Length - 1, found = -1;
        while (low <= high)
        {
            int mid = low + (high - low) / 2;
            if (_groups[mid].StartCharCode <= codePoint) { found = mid; low = mid + 1; }
            else high = mid - 1;
        }
        if (found < 0 || codePoint > _groups[found].EndCharCode)
        {
            diagnostic = default; return ReadStatus.Done;
        }
        uint resolved = _groups[found].StartGlyphID + (codePoint - _groups[found].StartCharCode);
        if (resolved == 0 || resolved > ushort.MaxValue)
        {
            diagnostic = default; return ReadStatus.Done;
        }
        mapped = true; glyphId = resolved;
        diagnostic = default;
        return ReadStatus.Done;
    }
}

public readonly record struct CmapFormat13Group(uint StartCharCode, uint EndCharCode,
    uint StartGlyphID);

/// <summary>Constant-gid many-to-one subtable: every code in a group resolves to the group's
/// StartGlyphID, with GID 0 meaning unmapped.</summary>
public sealed class CmapFormat13Subtable : CmapSubtable
{
    private readonly CmapFormat13Group[] _groups;

    internal CmapFormat13Subtable(CmapFormat13Group[] groups, uint sourceOffset,
        DataLocation location) : base(13, sourceOffset, location)
    {
        _groups = groups;
    }

    public int GroupCount => _groups.Length;
    public ReadOnlySpan<CmapFormat13Group> Groups => _groups;

    public override ReadStatus TryMapCharacter(uint codePoint, out bool mapped, out uint glyphId,
        out FontDiagnostic diagnostic)
    {
        mapped = false; glyphId = 0;
        int low = 0, high = _groups.Length - 1, found = -1;
        while (low <= high)
        {
            int mid = low + (high - low) / 2;
            if (_groups[mid].StartCharCode <= codePoint) { found = mid; low = mid + 1; }
            else high = mid - 1;
        }
        if (found < 0 || codePoint > _groups[found].EndCharCode)
        {
            diagnostic = default; return ReadStatus.Done;
        }
        uint resolved = _groups[found].StartGlyphID;
        if (resolved == 0 || resolved > ushort.MaxValue)
        {
            diagnostic = default; return ReadStatus.Done;
        }
        mapped = true; glyphId = resolved;
        diagnostic = default;
        return ReadStatus.Done;
    }
}

public readonly record struct CmapFormat8Group(uint StartCharCode, uint EndCharCode,
    uint StartGlyphID);

/// <summary>Mixed 16/32-bit subtable: linear groups as in format 12 plus the is32 first-half
/// bitmap. A 16-bit lookup whose is32 bit is set is not itself mapped (the value only occurs
/// as the first half of a 32-bit code); enumeration skips such code points as well.</summary>
public sealed class CmapFormat8Subtable : CmapSubtable
{
    private readonly CmapFormat8Group[] _groups;
    private readonly byte[] _is32;

    internal CmapFormat8Subtable(uint language, CmapFormat8Group[] groups, byte[] is32,
        uint sourceOffset, DataLocation location) : base(8, sourceOffset, location)
    {
        Language = language; _groups = groups; _is32 = is32;
    }

    public uint Language { get; }
    public int GroupCount => _groups.Length;
    public ReadOnlySpan<CmapFormat8Group> Groups => _groups;
    public ReadOnlySpan<byte> Is32 => _is32;

    public override ReadStatus TryMapCharacter(uint codePoint, out bool mapped, out uint glyphId,
        out FontDiagnostic diagnostic)
    {
        mapped = false; glyphId = 0;
        if (codePoint <= 0xFFFF &&
            CmapFormat8Layout.GetIs32Bit(_is32, codePoint))
        {
            // The 16-bit value only occurs as the first half of a 32-bit code.
            diagnostic = default; return ReadStatus.Done;
        }
        int low = 0, high = _groups.Length - 1, found = -1;
        while (low <= high)
        {
            int mid = low + (high - low) / 2;
            if (_groups[mid].StartCharCode <= codePoint) { found = mid; low = mid + 1; }
            else high = mid - 1;
        }
        if (found < 0 || codePoint > _groups[found].EndCharCode)
        {
            diagnostic = default; return ReadStatus.Done;
        }
        uint resolved = _groups[found].StartGlyphID + (codePoint - _groups[found].StartCharCode);
        if (resolved == 0 || resolved > ushort.MaxValue)
        {
            diagnostic = default; return ReadStatus.Done;
        }
        mapped = true; glyphId = resolved;
        diagnostic = default;
        return ReadStatus.Done;
    }
}

/// <summary>Trimmed 32-bit window subtable: one window [StartCharCode, StartCharCode +
/// GlyphArray.Length) of explicit glyph ids, with GID 0 meaning unmapped.</summary>
public sealed class CmapFormat10Subtable : CmapSubtable
{
    private readonly ushort[] _glyphArray;

    internal CmapFormat10Subtable(uint language, uint startCharCode, ushort[] glyphArray,
        uint sourceOffset, DataLocation location) : base(10, sourceOffset, location)
    {
        Language = language; StartCharCode = startCharCode; _glyphArray = glyphArray;
    }

    public uint Language { get; }
    public uint StartCharCode { get; }
    public ReadOnlySpan<ushort> GlyphArray => _glyphArray;

    public override ReadStatus TryMapCharacter(uint codePoint, out bool mapped, out uint glyphId,
        out FontDiagnostic diagnostic)
    {
        mapped = false; glyphId = 0;
        if (codePoint < StartCharCode || codePoint - StartCharCode >= (uint)_glyphArray.Length)
        {
            diagnostic = default; return ReadStatus.Done;
        }
        ushort resolved = _glyphArray[(int)(codePoint - StartCharCode)];
        if (resolved == 0) { diagnostic = default; return ReadStatus.Done; }
        mapped = true; glyphId = resolved;
        diagnostic = default;
        return ReadStatus.Done;
    }
}

/// <summary>Single-byte format 0 subtable: one fixed 256-entry glyph array indexed by the
/// code point itself, with GID 0 meaning unmapped.</summary>
public sealed class CmapFormat0Subtable : CmapSubtable
{
    private readonly byte[] _glyphArray;

    internal CmapFormat0Subtable(ushort language, byte[] glyphArray, uint sourceOffset,
        DataLocation location) : base(0, sourceOffset, location)
    {
        Language = language; _glyphArray = glyphArray;
    }

    public ushort Language { get; }
    public ReadOnlySpan<byte> GlyphArray => _glyphArray;

    public override ReadStatus TryMapCharacter(uint codePoint, out bool mapped, out uint glyphId,
        out FontDiagnostic diagnostic)
    {
        mapped = false; glyphId = 0;
        if (codePoint > 0xFF) { diagnostic = default; return ReadStatus.Done; }
        if (codePoint >= (uint)_glyphArray.Length)
            return ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.RangeOutsideSource,
                Location.At(DataField.GlyphId, 6 + codePoint), out diagnostic,
                codePoint, (uint)_glyphArray.Length);
        byte resolved = _glyphArray[(int)codePoint];
        if (resolved == 0) { diagnostic = default; return ReadStatus.Done; }
        mapped = true; glyphId = resolved;
        diagnostic = default;
        return ReadStatus.Done;
    }
}

/// <summary>Trimmed format 6 subtable: one window [FirstCode, FirstCode + GlyphArray.Length)
/// of explicit glyph ids, with GID 0 meaning unmapped.</summary>
public sealed class CmapFormat6Subtable : CmapSubtable
{
    private readonly ushort[] _glyphArray;

    internal CmapFormat6Subtable(ushort language, ushort firstCode, ushort[] glyphArray,
        uint sourceOffset, DataLocation location) : base(6, sourceOffset, location)
    {
        Language = language; FirstCode = firstCode; _glyphArray = glyphArray;
    }

    public ushort Language { get; }
    public ushort FirstCode { get; }
    public ReadOnlySpan<ushort> GlyphArray => _glyphArray;

    public override ReadStatus TryMapCharacter(uint codePoint, out bool mapped, out uint glyphId,
        out FontDiagnostic diagnostic)
    {
        mapped = false; glyphId = 0;
        if (codePoint > 0xFFFF || codePoint < FirstCode)
        {
            diagnostic = default; return ReadStatus.Done;
        }
        uint index = codePoint - FirstCode;
        if (index >= (uint)_glyphArray.Length)
        {
            diagnostic = default; return ReadStatus.Done;
        }
        ushort resolved = _glyphArray[(int)index];
        if (resolved == 0) { diagnostic = default; return ReadStatus.Done; }
        mapped = true; glyphId = resolved;
        diagnostic = default;
        return ReadStatus.Done;
    }
}

public readonly record struct CmapFormat2SubHeader(ushort FirstCode, ushort EntryCount,
    short IdDelta, ushort IdRangeOffset);

/// <summary>High-byte format 2 subtable: 256 lead-byte keys into shared subHeaders whose
/// self-relative windows index the glyph array. Key 0 on the zero lead byte is the
/// single-byte path; a nonzero key resolves byte2 through subHeader key/8. Raw 0 and a
/// (raw + idDelta) result of 0 both mean unmapped.</summary>
public sealed class CmapFormat2Subtable : CmapSubtable
{
    private readonly ushort[] _subHeaderKeys;
    private readonly CmapFormat2SubHeader[] _subHeaders;
    private readonly ushort[] _glyphArray;

    internal CmapFormat2Subtable(ushort language, ushort[] subHeaderKeys,
        CmapFormat2SubHeader[] subHeaders, ushort[] glyphArray, uint sourceOffset,
        DataLocation location) : base(2, sourceOffset, location)
    {
        Language = language; _subHeaderKeys = subHeaderKeys;
        _subHeaders = subHeaders; _glyphArray = glyphArray;
    }

    public ushort Language { get; }
    public int SubHeaderCount => _subHeaders.Length;
    public ReadOnlySpan<ushort> SubHeaderKeys => _subHeaderKeys;
    public ReadOnlySpan<CmapFormat2SubHeader> SubHeaders => _subHeaders;
    public ReadOnlySpan<ushort> GlyphArray => _glyphArray;

    public override ReadStatus TryMapCharacter(uint codePoint, out bool mapped, out uint glyphId,
        out FontDiagnostic diagnostic)
    {
        mapped = false; glyphId = 0;
        if (codePoint > 0xFFFF) { diagnostic = default; return ReadStatus.Done; }
        uint byte1 = codePoint >> 8, byte2 = codePoint & 0xFF;
        ushort key = _subHeaderKeys[(int)byte1];
        if (key == 0 && byte1 != 0)
        {
            // A zero key for a nonzero lead byte starts no two-byte sequence.
            diagnostic = default; return ReadStatus.Done;
        }
        int header = (int)((uint)key / 8);
        if (header >= _subHeaders.Length)
            return ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.RangeOutsideSource,
                Location.At(DataField.Offset, SourceOffset), out diagnostic,
                key, (uint)_subHeaders.Length * 8);
        var subHeader = _subHeaders[header];
        if (byte2 < subHeader.FirstCode || byte2 >= subHeader.FirstCode + subHeader.EntryCount)
        {
            diagnostic = default; return ReadStatus.Done;
        }
        // Self-relative IdRangeOffset normalized into glyph-array uint16 units.
        long index = (long)subHeader.IdRangeOffset / 2 + 3
            + 4L * (header - _subHeaders.Length) + (byte2 - subHeader.FirstCode);
        if (index < 0 || index >= _glyphArray.Length)
            return ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.RangeOutsideSource,
                Location.At(DataField.Offset,
                    (ulong)(518 + 8L * header + 6L), header), out diagnostic,
                (ulong)index, (ulong)_glyphArray.Length);
        ushort raw = _glyphArray[(int)index];
        if (raw == 0) { diagnostic = default; return ReadStatus.Done; }
        ushort resolved = unchecked((ushort)(raw + subHeader.IdDelta));
        if (resolved == 0) { diagnostic = default; return ReadStatus.Done; }
        mapped = true; glyphId = resolved;
        diagnostic = default;
        return ReadStatus.Done;
    }
}

/// <summary>Outcome of a variation-selector lookup against one format 14 subtable:
/// Default reports a default-range hit whose glyph comes from the base cmap,
/// NonDefault reports the resolved non-default mapping (GID 0 stays visible),
/// None reports no coverage for the requested (selector, character) pair.</summary>
public enum CmapVariationResultKind : byte
{
    None = 0, Default = 1, NonDefault = 2
}

public readonly record struct CmapDefaultUvsRange(uint StartUnicodeValue, byte AdditionalCount);

public readonly record struct CmapUvsMapping(uint UnicodeValue, ushort GlyphId);

/// <summary>One format 14 variation selector with its owned default ranges and non-default
/// mappings; arrays are model-owned and treated as immutable by consumers.</summary>
public sealed record CmapVarSelectorRecord(uint VarSelector,
    CmapDefaultUvsRange[] DefaultRanges, CmapUvsMapping[] NonDefaultMappings);

/// <summary>Format 14 Unicode variation sequences. Character mapping is intentionally not
/// offered through TryMapCharacter; use TryMapVariation and the base cmap combination.</summary>
public sealed class CmapFormat14Subtable : CmapSubtable
{
    private readonly CmapVarSelectorRecord[] _selectors;

    internal CmapFormat14Subtable(CmapVarSelectorRecord[] selectors, uint sourceOffset,
        DataLocation location) : base(14, sourceOffset, location)
    {
        _selectors = selectors;
    }

    public int SelectorCount => _selectors.Length;
    public ReadOnlySpan<CmapVarSelectorRecord> Selectors => _selectors;

    /// <summary>Looks up one (selector, character) pair: non-default mappings win over default
    /// ranges; a non-default GID 0 is reported as-is for the caller to interpret as unmapped.</summary>
    public ReadStatus TryMapVariation(uint varSelector, uint baseCharacter,
        out CmapVariationResultKind kind, out uint glyphId, out FontDiagnostic diagnostic)
    {
        kind = CmapVariationResultKind.None; glyphId = 0;
        int low = 0, high = _selectors.Length - 1, found = -1;
        while (low <= high)
        {
            int mid = low + (high - low) / 2;
            if (_selectors[mid].VarSelector < varSelector) low = mid + 1;
            else if (_selectors[mid].VarSelector > varSelector) high = mid - 1;
            else { found = mid; break; }
        }
        if (found < 0)
        {
            diagnostic = default; return ReadStatus.Done;
        }
        var selector = _selectors[found];
        var mappings = selector.NonDefaultMappings;
        int low2 = 0, high2 = mappings.Length - 1;
        while (low2 <= high2)
        {
            int mid = low2 + (high2 - low2) / 2;
            if (mappings[mid].UnicodeValue < baseCharacter) low2 = mid + 1;
            else if (mappings[mid].UnicodeValue > baseCharacter) high2 = mid - 1;
            else
            {
                kind = CmapVariationResultKind.NonDefault;
                glyphId = mappings[mid].GlyphId;
                diagnostic = default;
                return ReadStatus.Done;
            }
        }
        var ranges = selector.DefaultRanges;
        int low3 = 0, high3 = ranges.Length - 1, rangeIndex = -1;
        while (low3 <= high3)
        {
            int mid = low3 + (high3 - low3) / 2;
            if (ranges[mid].StartUnicodeValue <= baseCharacter) { rangeIndex = mid; low3 = mid + 1; }
            else high3 = mid - 1;
        }
        if (rangeIndex >= 0 &&
            baseCharacter <= ranges[rangeIndex].StartUnicodeValue + ranges[rangeIndex].AdditionalCount)
        {
            kind = CmapVariationResultKind.Default;
            glyphId = 0;
        }
        diagnostic = default;
        return ReadStatus.Done;
    }

    public override ReadStatus TryMapCharacter(uint codePoint, out bool mapped, out uint glyphId,
        out FontDiagnostic diagnostic)
    {
        mapped = false; glyphId = 0;
        return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
            Location.At(DataField.Version, SourceOffset), out diagnostic, Format);
    }
}

/// <summary>A subtable whose format is outside the supported typed set: extent-checked
/// bytes preserved verbatim, with no semantic claims.</summary>
public sealed class CmapRawSubtable : CmapSubtable
{
    private readonly byte[] _bytes;

    internal CmapRawSubtable(ushort format, uint sourceOffset, byte[] bytes,
        DataLocation location) : base(format, sourceOffset, location)
    {
        _bytes = bytes;
    }

    public ReadOnlySpan<byte> RawBytes => _bytes;

    public override ReadStatus TryMapCharacter(uint codePoint, out bool mapped, out uint glyphId,
        out FontDiagnostic diagnostic)
    {
        mapped = false; glyphId = 0;
        return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
            Location.At(DataField.Version, SourceOffset), out diagnostic, Format);
    }
}

/// <summary>Owned cmap model: encoding records and deduplicated subtables with per-subtable
/// lookup and enumeration. It deliberately offers no platform-preference selection; choosing
/// which record to consult is a consumer/Operations policy.</summary>
public sealed class CmapTable
{
    private readonly CmapRecord[] _records;
    private readonly CmapSubtable[] _subtables;

    internal CmapTable(CmapRecord[] records, CmapSubtable[] subtables, int numGlyphs,
        DataLocation location, DataLocation maxpLocation)
    {
        _records = records; _subtables = subtables; NumGlyphs = numGlyphs;
        Location = location; MaxpLocation = maxpLocation;
    }

    public ushort Version => 0;
    public int NumGlyphs { get; }
    public DataLocation Location { get; }
    public DataLocation MaxpLocation { get; }
    public ReadOnlySpan<CmapRecord> Records => _records;
    public int SubtableCount => _subtables.Length;

    public ReadStatus TryGetSubtable(int recordOrdinal, out CmapSubtable? subtable,
        out FontDiagnostic diagnostic)
    {
        subtable = null;
        if (recordOrdinal < 0 || recordOrdinal >= _records.Length)
            return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                Location.At(DataField.Count, 2, recordOrdinal), out diagnostic,
                unchecked((ulong)(long)recordOrdinal), (uint)_records.Length);
        subtable = _subtables[_records[recordOrdinal].SubtableIndex];
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>Full forward mapping of one subtable: Unicode scalar values with nonzero GIDs.
    /// Surrogate code points are excluded; the format 4 structural U+FFFF sentinel is excluded
    /// as well. Format 2 expands its single-byte and two-byte paths without duplicates.</summary>
    public ReadStatus TryGetMappings(CmapSubtable subtable, out CmapMapping[]? mappings,
        out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        mappings = null;
        ArgumentNullException.ThrowIfNull(subtable);
        var budget = new BuildBudget(limits);
        switch (subtable)
        {
            case CmapFormat0Subtable format0:
                return EnumerateFormat0(format0, out mappings, out diagnostic, budget);
            case CmapFormat2Subtable format2:
                return EnumerateFormat2(format2, out mappings, out diagnostic, budget);
            case CmapFormat4Subtable format4:
                return EnumerateFormat4(format4, out mappings, out diagnostic, budget);
            case CmapFormat6Subtable format6:
                return EnumerateFormat6(format6, out mappings, out diagnostic, budget);
            case CmapFormat12Subtable format12:
                return EnumerateFormat12(format12, out mappings, out diagnostic, budget);
            case CmapFormat13Subtable format13:
                return EnumerateFormat13(format13, out mappings, out diagnostic, budget);
            case CmapFormat8Subtable format8:
                return EnumerateFormat8(format8, out mappings, out diagnostic, budget);
            case CmapFormat10Subtable format10:
                return EnumerateFormat10(format10, out mappings, out diagnostic, budget);
            default:
                return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
                    subtable.Location.At(DataField.Version, subtable.SourceOffset), out diagnostic,
                    subtable.Format);
        }
    }

    /// <summary>Full forward enumeration of one format 14 subtable's selectors with their owned
    /// default ranges and non-default mappings; the returned records are the model's own.</summary>
    public ReadStatus TryGetVariations(CmapSubtable subtable, out CmapVarSelectorRecord[]? variations,
        out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        variations = null;
        ArgumentNullException.ThrowIfNull(subtable);
        var budget = new BuildBudget(limits);
        if (subtable is CmapFormat14Subtable format14)
        {
            long bytes = 16L * format14.SelectorCount;
            foreach (var selector in format14.Selectors)
                bytes += 12L * selector.DefaultRanges.Length + 6L * selector.NonDefaultMappings.Length;
            if (!budget.Work(bytes, subtable.Location, out diagnostic) ||
                !budget.Buffer(bytes, subtable.Location, out diagnostic))
                return ReadStatus.ResourceLimit;
            variations = format14.Selectors.ToArray();
            diagnostic = default;
            return ReadStatus.Done;
        }
        return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
            subtable.Location.At(DataField.Version, subtable.SourceOffset), out diagnostic,
            subtable.Format);
    }

    /// <summary>Combined variation-sequence lookup: non-default mappings win, default-range hits
    /// delegate to the base subtable's mapping of the base character, and a resolved GID 0 means
    /// unmapped. The variation subtable must be format 14 and the base one any typed
    /// scalar-mapping format (0/2/4/6/8/10/12/13); choosing
    /// which records to consult is a caller policy.</summary>
    public ReadStatus TryMapVariationSequence(CmapSubtable variationSubtable,
        CmapSubtable baseSubtable, uint baseCharacter, uint varSelector, out bool mapped,
        out uint glyphId, out FontDiagnostic diagnostic)
    {
        mapped = false; glyphId = 0;
        ArgumentNullException.ThrowIfNull(variationSubtable);
        ArgumentNullException.ThrowIfNull(baseSubtable);
        if (variationSubtable is not CmapFormat14Subtable variation)
            return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
                variationSubtable.Location.At(DataField.Version, variationSubtable.SourceOffset),
                out diagnostic, variationSubtable.Format);
        if (baseSubtable is not (CmapFormat0Subtable or CmapFormat2Subtable or CmapFormat4Subtable
            or CmapFormat6Subtable or CmapFormat8Subtable or CmapFormat10Subtable
            or CmapFormat12Subtable or CmapFormat13Subtable))
            return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
                baseSubtable.Location.At(DataField.Version, baseSubtable.SourceOffset),
                out diagnostic, baseSubtable.Format);
        ReadStatus status = variation.TryMapVariation(varSelector, baseCharacter,
            out CmapVariationResultKind kind, out uint variationGlyphId, out diagnostic);
        if (status != ReadStatus.Done) return status;
        switch (kind)
        {
            case CmapVariationResultKind.NonDefault:
                mapped = variationGlyphId != 0;
                glyphId = variationGlyphId;
                diagnostic = default;
                return ReadStatus.Done;
            case CmapVariationResultKind.Default:
                return baseSubtable.TryMapCharacter(baseCharacter, out mapped, out glyphId, out diagnostic);
            default:
                mapped = false; glyphId = 0;
                diagnostic = default;
                return ReadStatus.Done;
        }
    }

    internal static ReadStatus EnumerateFormat4(CmapFormat4Subtable subtable,
        out CmapMapping[]? mappings, out FontDiagnostic diagnostic, BuildBudget budget)
    {
        mappings = null;
        var segments = subtable.Segments;
        long span = 0;
        foreach (var segment in segments)
            span += segment.EndCode - segment.StartCode + 1;
        if (!budget.Work(span + 16L * segments.Length, subtable.Location, out diagnostic) ||
            !budget.Buffer(12L * span, subtable.Location, out diagnostic))
            return ReadStatus.ResourceLimit;
        var result = new List<CmapMapping>(capacity: (int)Math.Min(span, 1 << 20));
        var glyphs = subtable.GlyphIdArray;
        for (int i = 0; i < segments.Length; i++)
        {
            var segment = segments[i];
            for (uint c = segment.StartCode; ; c++)
            {
                if (c is not (>= 0xD800 and <= 0xDFFF) and not 0xFFFF)
                {
                    ushort gid;
                    if (segment.IdRangeOffset == 0)
                        gid = unchecked((ushort)(c + segment.IdDelta));
                    else
                    {
                        long index = segment.IdRangeOffset / 2 + i - segments.Length
                            + (c - segment.StartCode);
                        gid = glyphs[(int)index] == 0
                            ? ushort.MinValue
                            : unchecked((ushort)(glyphs[(int)index] + segment.IdDelta));
                    }
                    if (gid != 0)
                        result.Add(new(c, gid));
                }
                if (c == segment.EndCode) break;
            }
        }
        mappings = [.. result];
        diagnostic = default;
        return ReadStatus.Done;
    }

    internal static ReadStatus EnumerateFormat0(CmapFormat0Subtable subtable,
        out CmapMapping[]? mappings, out FontDiagnostic diagnostic, BuildBudget budget)
    {
        mappings = null;
        var glyphs = subtable.GlyphArray;
        long span = glyphs.Length;
        if (!budget.Work(span + 16, subtable.Location, out diagnostic) ||
            !budget.Buffer(12L * span, subtable.Location, out diagnostic))
            return ReadStatus.ResourceLimit;
        var result = new List<CmapMapping>(capacity: (int)Math.Min(span, 1 << 20));
        for (int i = 0; i < glyphs.Length; i++)
        {
            // Codes are at most 0xFF, so every mapped code is a scalar value.
            if (glyphs[i] != 0)
                result.Add(new((uint)i, glyphs[i]));
        }
        mappings = [.. result];
        diagnostic = default;
        return ReadStatus.Done;
    }

    internal static ReadStatus EnumerateFormat6(CmapFormat6Subtable subtable,
        out CmapMapping[]? mappings, out FontDiagnostic diagnostic, BuildBudget budget)
    {
        mappings = null;
        var glyphs = subtable.GlyphArray;
        long span = glyphs.Length;
        if (!budget.Work(span + 16, subtable.Location, out diagnostic) ||
            !budget.Buffer(12L * span, subtable.Location, out diagnostic))
            return ReadStatus.ResourceLimit;
        var result = new List<CmapMapping>(capacity: (int)Math.Min(span, 1 << 20));
        for (int i = 0; i < glyphs.Length; i++)
        {
            uint code = subtable.FirstCode + (uint)i;
            if (glyphs[i] != 0 && code is not (>= 0xD800 and <= 0xDFFF))
                result.Add(new(code, glyphs[i]));
        }
        mappings = [.. result];
        diagnostic = default;
        return ReadStatus.Done;
    }

    internal static ReadStatus EnumerateFormat2(CmapFormat2Subtable subtable,
        out CmapMapping[]? mappings, out FontDiagnostic diagnostic, BuildBudget budget)
    {
        mappings = null;
        var keys = subtable.SubHeaderKeys;
        var subHeaders = subtable.SubHeaders;
        var glyphs = subtable.GlyphArray;
        var referenced = new bool[subHeaders.Length];
        if (!budget.Buffer(referenced.Length, subtable.Location, out diagnostic))
            return ReadStatus.ResourceLimit;
        long span = 0;
        for (int b = 0; b < 256; b++)
        {
            ushort key = keys[b];
            if (key == 0)
            {
                if (b == 0 && subHeaders.Length > 0 && !referenced[0])
                {
                    referenced[0] = true;
                    span += subHeaders[0].EntryCount;
                }
            }
            else
            {
                int index = key / 8;
                if (index < subHeaders.Length && !referenced[index])
                {
                    referenced[index] = true;
                    span += subHeaders[index].EntryCount;
                }
            }
        }
        if (!budget.Work(span + 16L * subHeaders.Length, subtable.Location, out diagnostic) ||
            !budget.Buffer(12L * span, subtable.Location, out diagnostic))
            return ReadStatus.ResourceLimit;
        var result = new List<CmapMapping>(capacity: (int)Math.Min(span, 1 << 20));
        for (int b = 0; b < 256; b++)
        {
            ushort key = keys[b];
            // A zero key on a nonzero lead byte starts no two-byte sequence; the zero
            // lead byte takes the single-byte path through subHeader 0.
            if (key == 0 && b != 0) continue;
            int header = key / 8;
            if (header >= subHeaders.Length)
                return ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.RangeOutsideSource,
                    subtable.Location.At(DataField.Offset, subtable.SourceOffset), out diagnostic,
                    key, (uint)subHeaders.Length * 8);
            var subHeader = subHeaders[header];
            for (int i = 0; i < subHeader.EntryCount; i++)
            {
                uint code = ((uint)b << 8) | (uint)(subHeader.FirstCode + i);
                long index = (long)subHeader.IdRangeOffset / 2 + 3
                    + 4L * (header - subHeaders.Length) + i;
                if (index < 0 || index >= glyphs.Length)
                    return ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.RangeOutsideSource,
                        subtable.Location.At(DataField.Offset, (ulong)(518 + 8L * header + 6L), header),
                        out diagnostic, (ulong)index, (ulong)glyphs.Length);
                ushort raw = glyphs[(int)index];
                if (raw == 0) continue;
                ushort gid = unchecked((ushort)(raw + subHeader.IdDelta));
                if (gid == 0 || code is (>= 0xD800 and <= 0xDFFF)) continue;
                result.Add(new(code, gid));
            }
        }
        mappings = [.. result];
        diagnostic = default;
        return ReadStatus.Done;
    }

    internal static ReadStatus EnumerateFormat12(CmapFormat12Subtable subtable,
        out CmapMapping[]? mappings, out FontDiagnostic diagnostic, BuildBudget budget)
    {
        mappings = null;
        var groups = subtable.Groups;
        long span = 0;
        foreach (var group in groups)
            span += group.EndCharCode - group.StartCharCode + 1;
        if (!budget.Work(span + 24L * groups.Length, subtable.Location, out diagnostic) ||
            !budget.Buffer(12L * span, subtable.Location, out diagnostic))
            return ReadStatus.ResourceLimit;
        var result = new List<CmapMapping>(capacity: (int)Math.Min(span, 1 << 20));
        for (int i = 0; i < groups.Length; i++)
        {
            var group = groups[i];
            for (uint c = group.StartCharCode; ; c++)
            {
                if (c is not (>= 0xD800 and <= 0xDFFF))
                {
                    uint gid = group.StartGlyphID + (c - group.StartCharCode);
                    if (gid != 0 && gid <= ushort.MaxValue)
                        result.Add(new(c, (ushort)gid));
                }
                if (c == group.EndCharCode) break;
            }
        }
        mappings = [.. result];
        diagnostic = default;
        return ReadStatus.Done;
    }

    internal static ReadStatus EnumerateFormat13(CmapFormat13Subtable subtable,
        out CmapMapping[]? mappings, out FontDiagnostic diagnostic, BuildBudget budget)
    {
        mappings = null;
        var groups = subtable.Groups;
        long span = 0;
        foreach (var group in groups)
            span += group.EndCharCode - group.StartCharCode + 1;
        if (!budget.Work(span + 24L * groups.Length, subtable.Location, out diagnostic) ||
            !budget.Buffer(12L * span, subtable.Location, out diagnostic))
            return ReadStatus.ResourceLimit;
        var result = new List<CmapMapping>(capacity: (int)Math.Min(span, 1 << 20));
        for (int i = 0; i < groups.Length; i++)
        {
            var group = groups[i];
            for (uint c = group.StartCharCode; ; c++)
            {
                // Constant gid: the group start GID applies to every covered code point.
                if (c is not (>= 0xD800 and <= 0xDFFF) &&
                    group.StartGlyphID != 0 && group.StartGlyphID <= ushort.MaxValue)
                    result.Add(new(c, (ushort)group.StartGlyphID));
                if (c == group.EndCharCode) break;
            }
        }
        mappings = [.. result];
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>Full forward enumeration of one format 8 subtable: scalar code points with
    /// nonzero GIDs. Surrogates are excluded; 16-bit code points whose is32 bit is set are
    /// skipped because they only occur as first halves of 32-bit codes.</summary>
    internal static ReadStatus EnumerateFormat8(CmapFormat8Subtable subtable,
        out CmapMapping[]? mappings, out FontDiagnostic diagnostic, BuildBudget budget)
    {
        mappings = null;
        var groups = subtable.Groups;
        long span = 0;
        foreach (var group in groups)
            span += group.EndCharCode - group.StartCharCode + 1;
        if (!budget.Work(span + 24L * groups.Length, subtable.Location, out diagnostic) ||
            !budget.Buffer(12L * span, subtable.Location, out diagnostic))
            return ReadStatus.ResourceLimit;
        var result = new List<CmapMapping>(capacity: (int)Math.Min(span, 1 << 20));
        var is32 = subtable.Is32;
        for (int i = 0; i < groups.Length; i++)
        {
            var group = groups[i];
            for (uint c = group.StartCharCode; ; c++)
            {
                if (c is not (>= 0xD800 and <= 0xDFFF) &&
                    !(c <= 0xFFFF && CmapFormat8Layout.GetIs32Bit(is32, c)))
                {
                    uint gid = group.StartGlyphID + (c - group.StartCharCode);
                    if (gid != 0 && gid <= ushort.MaxValue)
                        result.Add(new(c, (ushort)gid));
                }
                if (c == group.EndCharCode) break;
            }
        }
        mappings = [.. result];
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>Full forward enumeration of one format 10 subtable: the window's scalar code
    /// points with nonzero GIDs. Validated windows never intersect the surrogate block.</summary>
    internal static ReadStatus EnumerateFormat10(CmapFormat10Subtable subtable,
        out CmapMapping[]? mappings, out FontDiagnostic diagnostic, BuildBudget budget)
    {
        mappings = null;
        var glyphs = subtable.GlyphArray;
        long span = glyphs.Length;
        if (!budget.Work(span + 24, subtable.Location, out diagnostic) ||
            !budget.Buffer(12L * span, subtable.Location, out diagnostic))
            return ReadStatus.ResourceLimit;
        var result = new List<CmapMapping>(capacity: (int)Math.Min(span, 1 << 20));
        uint start = subtable.StartCharCode;
        for (int i = 0; i < glyphs.Length; i++)
        {
            uint code = start + (uint)i;
            if (glyphs[i] != 0 && code is not (>= 0xD800 and <= 0xDFFF))
                result.Add(new(code, glyphs[i]));
        }
        mappings = [.. result];
        diagnostic = default;
        return ReadStatus.Done;
    }
}
