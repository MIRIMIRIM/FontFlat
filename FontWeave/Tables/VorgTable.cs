using FontWeave.Core;

namespace FontWeave.Tables;

public readonly record struct VorgRecord(ushort GlyphIndex, short VertOriginY);

/// <summary>Owned VORG records in original ordinal order, including redundant default overrides.
/// This table alone does not establish applicability to TrueType or consistency with outlines.</summary>
public sealed class VorgTable
{
    private readonly VorgRecord[] _records;

    internal VorgTable(short defaultVertOriginY, VorgRecord[] records, ushort numGlyphs,
        DataLocation location, DataLocation maxpLocation)
    {
        DefaultVertOriginY = defaultVertOriginY; _records = records; NumGlyphs = numGlyphs;
        Location = location; MaxpLocation = maxpLocation;
    }

    public ushort MajorVersion => 1;
    public ushort MinorVersion => 0;
    public short DefaultVertOriginY { get; }
    public ushort NumVertOriginYMetrics => (ushort)_records.Length;
    public ushort NumGlyphs { get; }
    public ReadOnlySpan<VorgRecord> Records => _records;
    public DataLocation Location { get; }
    public DataLocation MaxpLocation { get; }

    public ReadStatus TryGetOrigin(int glyphId, out short originY, out FontDiagnostic diagnostic) =>
        TryGetOrigin(glyphId, out originY, out _, out diagnostic);

    /// <summary>Resolves a stored override or the default without consulting glyph outlines.</summary>
    public ReadStatus TryGetOrigin(int glyphId, out short originY, out bool isOverride,
        out FontDiagnostic diagnostic)
    {
        originY = default; isOverride = false;
        if (glyphId < 0 || glyphId >= NumGlyphs)
            return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                Location.At(DataField.GlyphId, 0, glyphId), out diagnostic,
                unchecked((ulong)(long)glyphId), NumGlyphs);
        int ordinal = FindRecord(glyphId);
        isOverride = ordinal >= 0;
        originY = isOverride ? _records[ordinal].VertOriginY : DefaultVertOriginY;
        diagnostic = default;
        return ReadStatus.Done;
    }

    internal int FindRecord(int glyphId)
    {
        int low = 0, high = _records.Length - 1;
        while (low <= high)
        {
            int mid = low + (high - low) / 2;
            int id = _records[mid].GlyphIndex;
            if (id == glyphId) return mid;
            if (id < glyphId) low = mid + 1;
            else high = mid - 1;
        }
        return -1;
    }
}

/// <summary>Edits an existing override without adding, removing or reordering records.</summary>
public readonly record struct VorgRecordEdit(int GlyphId, int VertOriginY);

public sealed record VorgEditPlan
{
    public int? DefaultVertOriginY { get; init; }
    public IReadOnlyList<VorgRecordEdit> Records { get; init; } = [];
}
