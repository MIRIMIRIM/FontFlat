using FontWeave.Core;

namespace FontWeave.Tables;

/// <summary>Outcome of whole-table validation. Composite glyphs are reported separately
/// because this slice decodes simple structure only.</summary>
public readonly record struct GlyfValidationSummary(int EmptyGlyphs, int SimpleGlyphs,
    int CompositeGlyphs, int TotalPaddingBytes)
{
    /// <summary>Glyphs whose internal structure this slice decoded and verified.</summary>
    public int ValidatedGlyphs => EmptyGlyphs + SimpleGlyphs;

    /// <summary>Glyphs deliberately left unparsed by this slice.</summary>
    public int NotParsedGlyphs => CompositeGlyphs;
}

/// <summary>Owned glyf contents: one decoded description per glyph plus the table bytes
/// needed to preserve unedited records byte-for-byte.</summary>
public sealed class GlyfTable
{
    private readonly GlyfGlyph?[] _glyphs;
    private readonly byte[] _original;

    internal GlyfTable(GlyfGlyph?[] glyphs, byte[] original, DataLocation location,
        DataLocation locaLocation)
    {
        _glyphs = glyphs; _original = original; Location = location; LocaLocation = locaLocation;
    }

    public DataLocation Location { get; }
    public DataLocation LocaLocation { get; }
    public int NumGlyphs => _glyphs.Length;

    /// <summary>The exact glyf image these glyphs were read from.</summary>
    public ReadOnlySpan<byte> OriginalBytes => _original;

    public ReadStatus TryGetGlyph(int glyphId, out GlyfGlyph? glyph,
        out FontDiagnostic diagnostic)
    {
        glyph = null;
        if (glyphId < 0 || glyphId >= _glyphs.Length)
            return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                Location.At(DataField.GlyphId, 0, glyphId), out diagnostic,
                unchecked((ulong)(long)glyphId), (ulong)_glyphs.Length);
        glyph = _glyphs[glyphId];
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>Decoded simple description, or null when the glyph is empty or composite.</summary>
    public ReadStatus TryGetSimpleGlyph(int glyphId, out SimpleGlyph? simple,
        out FontDiagnostic diagnostic)
    {
        simple = null;
        ReadStatus status = TryGetGlyph(glyphId, out var glyph, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (glyph is null || glyph.Kind == GlyfGlyphKind.Composite)
            return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
                Location.At(DataField.GlyphId, 0, glyphId), out diagnostic,
                unchecked((ulong)(long)glyphId), 0);
        simple = glyph.Simple;
        diagnostic = default;
        return ReadStatus.Done;
    }

    public int EmptyGlyphCount
    {
        get
        {
            int count = 0;
            foreach (var glyph in _glyphs)
                if (glyph is null || glyph.IsEmpty) count++;
            return count;
        }
    }

    public int CompositeGlyphCount
    {
        get
        {
            int count = 0;
            foreach (var glyph in _glyphs)
                if (glyph is { Kind: GlyfGlyphKind.Composite }) count++;
            return count;
        }
    }

    /// <summary>Largest decoded point count among simple glyphs; 0 when none.</summary>
    public int MaxSimplePointCount
    {
        get
        {
            int maximum = 0;
            foreach (var glyph in _glyphs)
                if (glyph?.Simple is { } simple) maximum = Math.Max(maximum, simple.PointCount);
            return maximum;
        }
    }

    /// <summary>Largest decoded contour count among simple glyphs; 0 when none.</summary>
    public int MaxSimpleContourCount
    {
        get
        {
            int maximum = 0;
            foreach (var glyph in _glyphs)
                if (glyph?.Simple is { } simple) maximum = Math.Max(maximum, simple.ContourCount);
            return maximum;
        }
    }

    /// <summary>Counts every glyph by kind and totals the trailing padding bytes, so a
    /// caller can compare whole-table shape without walking the glyphs again.</summary>
    public GlyfValidationSummary Summarize()
    {
        int empty = 0, simple = 0, composite = 0, padding = 0;
        foreach (var glyph in _glyphs)
        {
            if (glyph is null || glyph.IsEmpty) { empty++; continue; }
            if (glyph.Simple is { } simpleGlyph)
            {
                simple++;
                padding += simpleGlyph.PaddingLength;
            }
            else
            {
                composite++;
                padding += glyph.PaddingLength;
            }
        }
        return new(empty, simple, composite, padding);
    }

    /// <summary>Total number of component records across every composite glyph.</summary>
    public int ComponentCount
    {
        get
        {
            int total = 0;
            foreach (var glyph in _glyphs)
                if (glyph?.Composite is { } composite) total += composite.ComponentCount;
            return total;
        }
    }

    /// <summary>The decoded composite description of one glyph, or null when that glyph is
    /// empty, simple, or was materialized without composite decoding.</summary>
    public GlyfCompositeGlyph? CompositeOf(int glyphId) =>
        glyphId >= 0 && glyphId < _glyphs.Length ? _glyphs[glyphId]?.Composite : null;
}
