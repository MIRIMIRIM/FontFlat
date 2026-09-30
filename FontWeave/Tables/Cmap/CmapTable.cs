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
    /// The format 4 structural U+FFFF sentinel and surrogate code points are excluded.</summary>
    public ReadStatus TryGetMappings(CmapSubtable subtable, out CmapMapping[]? mappings,
        out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        mappings = null;
        ArgumentNullException.ThrowIfNull(subtable);
        var budget = new BuildBudget(limits);
        switch (subtable)
        {
            case CmapFormat4Subtable format4:
                return EnumerateFormat4(format4, out mappings, out diagnostic, budget);
            case CmapFormat12Subtable format12:
                return EnumerateFormat12(format12, out mappings, out diagnostic, budget);
            case CmapFormat13Subtable format13:
                return EnumerateFormat13(format13, out mappings, out diagnostic, budget);
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
    /// unmapped. The variation subtable must be format 14 and the base one 4/12/13; choosing
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
        if (baseSubtable is not (CmapFormat4Subtable or CmapFormat12Subtable or CmapFormat13Subtable))
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
}
