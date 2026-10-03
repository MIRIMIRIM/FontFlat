using FontWeave.Core;

namespace FontWeave.Tables.Layout;

/// <summary>Ordered mark-filtering sets. Repeated references retain Coverage identity.</summary>
public sealed class MarkGlyphSetsTable
{
    private readonly uint[] _sourceOffsets;

    internal MarkGlyphSetsTable(CoverageTable[] sets, int numGlyphs, DataLocation location,
        int consumedLength, uint[] sourceOffsets)
    {
        Sets = Array.AsReadOnly(sets);
        NumGlyphs = numGlyphs;
        Location = location;
        ConsumedLength = consumedLength;
        _sourceOffsets = sourceOffsets;
    }

    public IReadOnlyList<CoverageTable> Sets { get; }
    public int NumGlyphs { get; }
    public DataLocation Location { get; }
    public int ConsumedLength { get; }
    /// <summary>Original parent-relative wire references; empty for caller-created models.</summary>
    public ReadOnlySpan<uint> SourceCoverageOffsets => _sourceOffsets;

    public ReadStatus TryWithSets(IReadOnlyList<CoverageTable> sets, out MarkGlyphSetsTable? updated,
        out FontDiagnostic diagnostic, BuildLimits? limits = null) =>
        MarkGlyphSetsTableCompiler.TryCreate(sets, NumGlyphs, out updated, out diagnostic, limits, Location);
}
