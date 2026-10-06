using FontWeave.Core;

namespace FontWeave.Tables.Layout;

/// <summary>One ligature rule (GSUB type 4 leaf): the output ligature glyph and the
/// second component onwards. <see cref="ComponentCount"/> includes the first component,
/// which the owning set's Coverage entry carries, so it is always
/// <see cref="Components"/>.Length + 1 (the declarative rawCount-1 contract).</summary>
public sealed class LigatureTable
{
    private readonly ushort[] _components;

    internal LigatureTable(ushort ligatureGlyph, ushort[] components,
        DataLocation location, int consumedLength)
    {
        LigatureGlyph = ligatureGlyph;
        _components = components;
        Location = location;
        ConsumedLength = consumedLength;
    }

    public ushort LigatureGlyph { get; }
    public int ComponentCount => _components.Length + 1;
    /// <summary>Components after the first, in writing direction.</summary>
    public ReadOnlySpan<ushort> Components => _components;
    public DataLocation Location { get; }
    public int ConsumedLength { get; }
}

/// <summary>All ligatures beginning with one covered glyph, in wire preference order
/// (the order is semantic, so it is retained, not normalized).</summary>
public sealed class LigatureSetTable
{
    private readonly LigatureTable[] _ligatures;

    internal LigatureSetTable(LigatureTable[] ligatures, DataLocation location, int consumedLength)
    {
        _ligatures = ligatures;
        Location = location;
        ConsumedLength = consumedLength;
    }

    public int LigatureCount => _ligatures.Length;
    public ReadOnlySpan<LigatureTable> Ligatures => _ligatures;
    public DataLocation Location { get; }
    public int ConsumedLength { get; }
}

/// <summary>A static GSUB type-4 fragment: one Coverage (first ligature components)
/// plus the per-coverage-index ligature sets. Coverage index i corresponds to set i.</summary>
public sealed class LigatureSubstTable
{
    private readonly LigatureSetTable[] _sets;

    internal LigatureSubstTable(CoverageTable coverage, LigatureSetTable[] sets,
        DataLocation location, int consumedLength)
    {
        Coverage = coverage;
        _sets = sets;
        Location = location;
        ConsumedLength = consumedLength;
    }

    public CoverageTable Coverage { get; }
    public int NumGlyphs => Coverage.NumGlyphs;
    public int SetCount => _sets.Length;
    public LigatureSetTable GetSet(int ordinal) => _sets[ordinal];
    public DataLocation Location { get; }
    public int ConsumedLength { get; }

    public ReadStatus TryGetSet(int glyphId, out LigatureSetTable? set, out FontDiagnostic diagnostic)
    {
        set = null;
        if (glyphId < 0 || glyphId >= NumGlyphs)
            return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                Location.AtLocal(DataField.GlyphId, 0), out diagnostic,
                unchecked((ulong)(long)glyphId), (ulong)NumGlyphs);
        int ordinal = Coverage.FindGlyph(glyphId);
        if (ordinal < 0)
            return ReadWire.Fail(ReadStatus.NotFound, DiagnosticCode.GlyphNotMapped,
                Coverage.Location.AtLocal(DataField.GlyphId, 0), out diagnostic, (ulong)glyphId);
        set = _sets[ordinal];
        diagnostic = default;
        return ReadStatus.Done;
    }

    public ReadStatus TryWithSets(ReadOnlySpan<ushort> glyphs, ReadOnlySpan<LigatureSetTable> sets,
        out LigatureSubstTable? updated, out FontDiagnostic diagnostic, BuildLimits? limits = null) =>
        LigatureSubstTableCompiler.TryCreate(glyphs, sets, NumGlyphs, out updated,
            out diagnostic, limits, Location, Coverage.Format);

    /// <summary>Replaces one set's ligature rules in preference order; the set keeps
    /// its Coverage-derived identity through the subtable factory.</summary>
    public static ReadStatus TryCreateSet(ReadOnlySpan<LigatureTable> ligatures,
        out LigatureSetTable? set, out FontDiagnostic diagnostic, DataLocation location = default)
    {
        set = null;
        long length = LigatureSetLayout.HeaderLength + LigatureSetLayout.OffsetStride * ligatures.Length;
        foreach (var ligature in ligatures)
            length += LigatureLayout.HeaderLength + 2L * ligature.Components.Length;
        set = new(ligatures.ToArray(), location, (int)length);
        diagnostic = default;
        return ReadStatus.Done;
    }
}
