// Regression tests for glyf editing and compilation.
//
// These cover three ways an edit could be accepted but produce an image that is not what the
// caller asked for, or is not readable at all.
using FontWeave.Core;
using FontWeave.Tables;
using Xunit;

namespace FontWeave.Tests;

public sealed class GlyfSimpleEditTests
{
    /// <summary>Reads a one-glyph model, applies an edit and rereads the result. The loca that
    /// describes the compiled image is the one TryApply returns: an edit that changes a
    /// glyph's size moves every later glyph, so reusing the source loca would read the wrong
    /// ranges (or truncate).</summary>
    private static (ReadStatus Apply, DiagnosticCode ApplyCode, byte[]? Bytes, SimpleGlyph? Glyph)
        ApplyAndReread(GlyphEdit edit, int points = 2)
    {
        var (locaBytes, glyfBytes, numGlyphs) = GlyfImages.SingleSimple(points);
        var loca = GlyfImages.LocaOrThrow(locaBytes, numGlyphs);
        _ = GlyfView.TryOpen(glyfBytes, out var view, out _, GlyfImages.GlyfLocation);
        Assert.Equal(ReadStatus.Done, view.TryRead(loca, out var model, out _));

        var plan = new GlyfEditPlan { Glyphs = [edit] };
        var apply = GlyfTableCompiler.TryApply(model!, plan, out var bytes, out var offsets,
            out var diagnostic);
        if (apply != ReadStatus.Done) return (apply, diagnostic.Code, null, null);
        Assert.NotNull(offsets);

        var newLoca = GlyfImages.LocaOrThrow(GlyfImages.LongLoca(offsets!), numGlyphs);
        _ = GlyfView.TryOpen(bytes!, out var reopened, out _, GlyfImages.GlyfLocation);
        Assert.Equal(ReadStatus.Done, reopened.TryRead(newLoca, out var again, out _));
        _ = again!.TryGetSimpleGlyph(edit.GlyphId, out var glyph, out _);
        return (apply, diagnostic.Code, bytes, glyph);
    }

    /// <summary>Two points sitting at the extremes of the signed 16-bit range are each
    /// representable, but their difference is not. A delta that cannot be written must be
    /// diagnosed rather than silently truncated, which would move the point.
    /// </summary>
    [Fact]
    public void DeltaOutsideTheSignedRangeIsDiagnosed()
    {
        var edit = new GlyphEdit
        {
            GlyphId = 0,
            Points =
            [
                new GlyphPointEdit(0, short.MinValue, 0),
                new GlyphPointEdit(1, short.MaxValue, 0)
            ]
        };
        var (apply, code, bytes, _) = ApplyAndReread(edit);

        Assert.Equal(ReadStatus.InvalidFormat, apply);
        Assert.Equal(DiagnosticCode.InvalidValue, code);
        Assert.Null(bytes);
    }

    /// <summary>A delta exactly at the representable limit must still be accepted, so the
    /// guard is a range check rather than a blanket rejection of large coordinates.
    /// </summary>
    [Fact]
    public void DeltaAtTheSignedLimitIsAccepted()
    {
        var edit = new GlyphEdit
        {
            GlyphId = 0,
            Points =
            [
                new GlyphPointEdit(0, 0, 0),
                new GlyphPointEdit(1, short.MaxValue, short.MinValue)
            ]
        };
        var (apply, _, bytes, glyph) = ApplyAndReread(edit);

        Assert.Equal(ReadStatus.Done, apply);
        Assert.NotNull(bytes);
        Assert.Equal(short.MaxValue, glyph!.Points[1].X);
        Assert.Equal(short.MinValue, glyph.Points[1].Y);
    }

    /// <summary>An OnCurve-only change must actually reach the compiled flag byte. Before the
    /// fix the flag was rebuilt from the original byte, so the request was accepted and then
    /// dropped.
    /// </summary>
    [Fact]
    public void OnCurveChangeReachesTheCompiledFlags()
    {
        // The fixture's two points are both ON_CURVE, so clearing one is observable.
        var edit = new GlyphEdit
        {
            GlyphId = 0,
            Points = [new GlyphPointEdit(0, 0, 0, OnCurve: false)]
        };
        var (apply, _, bytes, glyph) = ApplyAndReread(edit);

        Assert.Equal(ReadStatus.Done, apply);
        Assert.NotNull(bytes);
        Assert.False(glyph!.Points[0].OnCurve);
        // The untouched point keeps its original flag, so only the requested bit changed.
        Assert.True(glyph.Points[1].OnCurve);
    }

    /// <summary>Setting OnCurve on a point that was off-curve must work in the other
    /// direction too, so the fix is not a one-way overwrite.
    /// </summary>
    [Fact]
    public void OnCurveChangeIsNotOneWay()
    {
        // Build a glyph whose flags are all off-curve by clearing ON_CURVE (0x39 -> 0x38).
        var (locaBytes, glyfBytes, numGlyphs) = GlyfImages.SingleSimple(2);
        var cleared = (byte[])glyfBytes.Clone();
        for (int i = 0; i < cleared.Length; i++) if (cleared[i] == 0x39) cleared[i] = 0x38;
        var loca = GlyfImages.LocaOrThrow(locaBytes, numGlyphs);
        _ = GlyfView.TryOpen(cleared, out var view, out _, GlyfImages.GlyfLocation);
        Assert.Equal(ReadStatus.Done, view.TryRead(loca, out var model, out _));
        _ = model!.TryGetSimpleGlyph(0, out var before, out _);
        Assert.False(before!.Points[0].OnCurve);

        var edit = new GlyphEdit
        {
            GlyphId = 0,
            Points = [new GlyphPointEdit(0, 0, 0, OnCurve: true)]
        };
        var plan = new GlyfEditPlan { Glyphs = [edit] };
        Assert.Equal(ReadStatus.Done,
            GlyfTableCompiler.TryApply(model, plan, out var bytes, out var offsets, out _));
        var newLoca = GlyfImages.LocaOrThrow(GlyfImages.LongLoca(offsets!), numGlyphs);
        _ = GlyfView.TryOpen(bytes!, out var reopened, out _, GlyfImages.GlyfLocation);
        Assert.Equal(ReadStatus.Done, reopened.TryRead(newLoca, out var again, out _));
        _ = again!.TryGetSimpleGlyph(0, out var after, out _);
        Assert.True(after!.Points[0].OnCurve);
    }

    /// <summary>An edit naming a point ordinal that does not exist must be refused rather than
    /// applied to a neighbouring point.</summary>
    [Fact]
    public void PointOrdinalPastThePointCountIsRefused()
    {
        var edit = new GlyphEdit
        {
            GlyphId = 0,
            Points = [new GlyphPointEdit(9, 5, 5)]
        };
        var (apply, code, bytes, _) = ApplyAndReread(edit);
        Assert.Equal(ReadStatus.InvalidIndex, apply);
        Assert.Equal(DiagnosticCode.IndexOutOfRange, code);
        Assert.Null(bytes);
    }

    /// <summary>An edit naming a glyph that does not exist must be refused.</summary>
    [Fact]
    public void GlyphIdPastTheGlyphCountIsRefused()
    {
        var (locaBytes, glyfBytes, numGlyphs) = GlyfImages.SingleSimple(2);
        var loca = GlyfImages.LocaOrThrow(locaBytes, numGlyphs);
        _ = GlyfView.TryOpen(glyfBytes, out var view, out _, GlyfImages.GlyfLocation);
        Assert.Equal(ReadStatus.Done, view.TryRead(loca, out var model, out _));

        var plan = new GlyfEditPlan
        {
            Glyphs = [new GlyphEdit { GlyphId = 5, Points = [] }]
        };
        Assert.Equal(ReadStatus.InvalidIndex,
            GlyfTableCompiler.TryApply(model!, plan, out var bytes, out _, out _));
        Assert.Null(bytes);
    }
}

