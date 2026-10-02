using FontWeave.Core;

namespace FontWeave.Tables;

/// <summary>Whole-table validation and materialization for the simple-glyph slice.
///
/// Composite glyphs are located and their headers read, but their internal structure is
/// not decoded here; they are counted and reported as not parsed so no caller can mistake
/// this slice's success for composite conformance.</summary>
public readonly ref partial struct GlyfView
{
    /// <summary>Validates every glyph that loca describes. <paramref name="ranges"/> must be
    /// the paired loca ranges over the same source face and the same table image.</summary>
    public ReadStatus Validate(LocaTable loca, out GlyfValidationSummary summary,
        out FontDiagnostic diagnostic, bool strict = true)
    {
        summary = default;
        ReadStatus status = RequireLoca(loca, out diagnostic);
        if (status != ReadStatus.Done) return status;
        int empty = 0, simple = 0, composite = 0, padding = 0;
        for (int gid = 0; gid < loca.NumGlyphs; gid++)
        {
            status = loca.TryGetGlyphRange(gid, out var range, out diagnostic);
            if (status != ReadStatus.Done) return status;
            status = TryDecodeGlyph(range, out var glyph, out diagnostic, strict);
            if (status != ReadStatus.Done) return status;
            switch (glyph!.Kind)
            {
                case GlyfGlyphKind.Empty: empty++; break;
                case GlyfGlyphKind.Simple:
                    simple++;
                    padding += glyph.Simple!.PaddingLength;
                    break;
                default: composite++; break;
            }
        }
        summary = new(empty, simple, composite, padding);
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>Validates the simple-glyph subset and materializes owned contents.
    /// Composite glyphs are retained as not-parsed markers, not as decoded structure.</summary>
    public ReadStatus TryRead(LocaTable loca, out GlyfTable? model, out FontDiagnostic diagnostic,
        BuildLimits? limits = null, bool strict = true)
    {
        model = null;
        var budget = new BuildBudget(limits);
        if (!budget.Input(_data.Length, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        return TryRead(loca, out model, out diagnostic, budget, strict);
    }

    internal ReadStatus TryRead(LocaTable loca, out GlyfTable? model, out FontDiagnostic diagnostic,
        BuildBudget budget, bool strict = true)
    {
        model = null;
        ReadStatus status = RequireLoca(loca, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (!budget.Work(_data.Length, Location, out diagnostic) ||
            !budget.Buffer(2L * _data.Length, Location, out diagnostic))
            return ReadStatus.ResourceLimit;
        var glyphs = new GlyfGlyph?[loca.NumGlyphs];
        for (int gid = 0; gid < glyphs.Length; gid++)
        {
            status = loca.TryGetGlyphRange(gid, out var range, out diagnostic);
            if (status != ReadStatus.Done) return status;
            status = TryDecodeGlyph(range, out var glyph, out diagnostic, strict);
            if (status != ReadStatus.Done) return status;
            glyphs[gid] = glyph;
        }
        model = new(glyphs, _data.ToArray(), Location, loca.Location);
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>Validates only the glyphs in one half-open range, so a caller can bound work.
    /// The same per-glyph rules apply; composite glyphs stay not parsed.</summary>
    public ReadStatus ValidateRange(LocaTable loca, int firstGlyph, int glyphCount,
        out GlyfValidationSummary summary, out FontDiagnostic diagnostic, bool strict = true)
    {
        summary = default;
        ReadStatus status = RequireLoca(loca, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (firstGlyph < 0 || glyphCount < 0 || firstGlyph > loca.NumGlyphs - glyphCount)
            return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                Location.At(DataField.GlyphId, 0, firstGlyph), out diagnostic,
                unchecked((ulong)(long)firstGlyph), loca.NumGlyphs);
        int empty = 0, simple = 0, composite = 0, padding = 0;
        for (int gid = firstGlyph; gid < firstGlyph + glyphCount; gid++)
        {
            status = loca.TryGetGlyphRange(gid, out var range, out diagnostic);
            if (status != ReadStatus.Done) return status;
            status = TryDecodeGlyph(range, out var glyph, out diagnostic, strict);
            if (status != ReadStatus.Done) return status;
            switch (glyph!.Kind)
            {
                case GlyfGlyphKind.Empty: empty++; break;
                case GlyfGlyphKind.Simple:
                    simple++;
                    padding += glyph.Simple!.PaddingLength;
                    break;
                default: composite++; break;
            }
        }
        summary = new(empty, simple, composite, padding);
        diagnostic = default;
        return ReadStatus.Done;
    }

    private ReadStatus RequireLoca(LocaTable loca, out FontDiagnostic diagnostic)
    {
        ArgumentNullException.ThrowIfNull(loca);
        if (!IsBound)
            return ReadWire.Fail(ReadStatus.Uninitialized, DiagnosticCode.UnboundView,
                Location.At(DataField.None, 0), out diagnostic);
        if (loca.Location.SourceId != 0 && !LocaContext.SameFace(loca.Location, Location))
            return ReadWire.Fail(ReadStatus.InvalidSource, DiagnosticCode.WrongSourceFace,
                Location, out diagnostic);
        // The paired loca must not describe bytes beyond this image.
        if (loca.GlyfLength > (uint)_data.Length)
            return ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.RangeOutsideSource,
                Location.At(DataField.Length, loca.GlyfLength), out diagnostic,
                loca.GlyfLength, (ulong)_data.Length);
        diagnostic = default;
        return ReadStatus.Done;
    }
}
