// Regression tests for glyf editing and compilation.
//
// These cover three ways an edit could be accepted but produce an image that is not what the
// caller asked for, or is not readable at all.
using FontWeave.Core;
using FontWeave.Tables;
using Xunit;

namespace FontWeave.Tests;

public sealed class GlyfCompositeEditTests
{
    /// <summary>Builds a two-composite model, applies edits and reports the outcome. The
    /// compiled loca offsets are returned so a caller can reread the image correctly.</summary>
    private static (ReadStatus Status, DiagnosticCode Code, byte[]? Bytes, uint[]? Offsets)
        Apply(params GlyfCompositeEdit[] edits)
    {
        var (locaBytes, glyfBytes, numGlyphs) = GlyfImages.TwoComposites();
        var loca = GlyfImages.LocaOrThrow(locaBytes, numGlyphs);
        _ = GlyfView.TryOpen(glyfBytes, out var compositesView, out _, GlyfImages.GlyfLocation);
        Assert.Equal(ReadStatus.Done, compositesView.TryReadComposites(loca, out var model, out _));

        var plan = new GlyfEditPlan { Glyphs = [], Composites = edits };
        var status = GlyfTableCompiler.TryApply(model!, plan, out var bytes, out var offsets,
            out var diagnostic);
        return (status, diagnostic.Code, bytes, offsets);
    }

    /// <summary>Pointing a component at its own glyph creates a cycle. The edit must be
    /// refused: emitting a cyclic glyf and letting the reader reject it later would hand the
    /// caller an unreadable image without saying so.
    /// </summary>
    [Fact]
    public void SelfReferenceEditIsRefused()
    {
        var (status, code, bytes, _) = Apply(new GlyfCompositeEdit
        {
            GlyphId = 1,
            Components = [new GlyfComponentEdit { Ordinal = 0, GlyphIndex = 1 }]
        });
        Assert.Equal(ReadStatus.InvalidFormat, status);
        Assert.Equal(DiagnosticCode.InvalidValue, code);
        Assert.Null(bytes);
    }

    /// <summary>Making two composites reference each other creates a cycle that reaches no
    /// leaf. Both edges have to move: retargeting only one of them leaves an acyclic graph
    /// (glyph1 to glyph2 to glyph0), which the compiler accepts.
    /// </summary>
    [Fact]
    public void MutualReferenceEditIsRefused()
    {
        var (status, code, bytes, _) = Apply(
            new GlyfCompositeEdit
            {
                GlyphId = 1,
                Components = [new GlyfComponentEdit { Ordinal = 0, GlyphIndex = 2 }]
            },
            new GlyfCompositeEdit
            {
                GlyphId = 2,
                Components = [new GlyfComponentEdit { Ordinal = 0, GlyphIndex = 1 }]
            });
        Assert.Equal(ReadStatus.InvalidFormat, status);
        Assert.Equal(DiagnosticCode.InvalidValue, code);
        Assert.Null(bytes);
    }

    /// <summary>An acyclic edit must still be accepted and produce a readable image, so the
    /// new verification is not rejecting every composite edit.</summary>
    [Fact]
    public void AcyclicEditIsAcceptedAndRereadable()
    {
        var (status, _, bytes, offsets) = Apply(new GlyfCompositeEdit
        {
            GlyphId = 1,
            Components = [new GlyfComponentEdit { Ordinal = 0, GlyphIndex = 0, XOffset = 12 }]
        });
        Assert.Equal(ReadStatus.Done, status);
        Assert.NotNull(bytes);

        var newLoca = GlyfImages.LocaOrThrow(GlyfImages.LongLoca(offsets!), 3);
        _ = GlyfView.TryOpen(bytes!, out var reopened, out _, GlyfImages.GlyfLocation);
        Assert.Equal(ReadStatus.Done, reopened.TryReadComposites(newLoca, out var again, out _));
        Assert.Equal(ReadStatus.Done, reopened.ValidateComponentGraph(again!, out _, out _));
    }

    /// <summary>Appending a component that points at the composite itself is also a cycle and
    /// must be caught, since the added component is part of the graph that gets compiled.
    /// </summary>
    [Fact]
    public void AddedSelfReferencingComponentIsRefused()
    {
        var (status, code, bytes, _) = Apply(new GlyfCompositeEdit
        {
            GlyphId = 1,
            AddComponents = [new GlyfNewComponent { GlyphIndex = 1, XOffset = 3, YOffset = 4 }]
        });
        Assert.Equal(ReadStatus.InvalidFormat, status);
        Assert.Equal(DiagnosticCode.InvalidValue, code);
        Assert.Null(bytes);
    }
}