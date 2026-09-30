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
            default:
                return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
                    subtable.Location.At(DataField.Version, subtable.SourceOffset), out diagnostic,
                    subtable.Format);
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
}
