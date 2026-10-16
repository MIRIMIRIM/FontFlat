// S2b-1 compact layout rewrite unit tests (S2b card §2, commits ②/③ acceptance).
//
// A synthetic face drives the compact+PreserveLayout path at the unit level: the
// GDEF class-domain per-entry shrink with identical class values (OQ-S2b-3), the
// all-members-kept form, the mark-sets remap under stable set ordinals, and the
// unrewritten-table prefix discipline (kern keeps its decide-stage code-23 face).
// The output GDEF re-reads through the typed view — the same read chain the
// pipeline itself validates against — never a byte comparison (the canonical run
// grouping makes the rewrite wire-equivalent, not byte-identical, by design).
using FontWeave.Containers;
using FontWeave.Core;
using FontWeave.Operations;
using FontWeave.Operations.Subset;
using FontWeave.Tables.Layout;
using Xunit;

namespace FontWeave.Tests;

public sealed class SubsetLayoutRewriteTests
{
    private const int NumGlyphs = 8;
    private const uint GdefVersion10 = 0x00010000, GdefVersion12 = 0x00010002;

    private static void Ok(ReadStatus status)
    {
        Assert.Equal(ReadStatus.Done, status);
    }

    private static ClassDefTable Classes(params ClassDefRange[] ranges)
    {
        Ok(ClassDefTableCompiler.TryCreate(ranges, NumGlyphs, 2, out var model,
            out var diagnostic));
        Assert.Equal(DiagnosticCode.None, diagnostic.Code);
        return model!;
    }

    private static byte[] GdefBytes(uint version, ClassDefTable? classes,
        ClassDefTable? markAttachment, MarkGlyphSetsTable? markSets)
    {
        Ok(GdefTableCompiler.TryCreate(version, NumGlyphs, classes, markAttachment,
            markSets, null, null, null, out var model, out var diagnostic));
        Assert.Equal(DiagnosticCode.None, diagnostic.Code);
        Ok(GdefTableCompiler.TryCompile(model!, out var bytes, out diagnostic));
        Assert.Equal(DiagnosticCode.None, diagnostic.Code);
        return bytes!;
    }

    private static FontFaceRef Face(byte[] gdef, params (uint Tag, byte[] Bytes)[] extra)
    {
        var tables = new List<(uint, byte[])>
        {
            (SubsetFontFixture.HeadTag, SubsetFontFixture.Head()),
            (SubsetFontFixture.MaxpTag, SubsetFontFixture.Maxp(NumGlyphs)),
            (SubsetFontFixture.GdefTag, gdef),
            (SubsetFontFixture.GlyfTag,
                [.. GlyfImages.NoContourGlyphRecord(), .. GlyfImages.NoContourGlyphRecord(),
                 .. GlyfImages.NoContourGlyphRecord(), .. GlyfImages.NoContourGlyphRecord(),
                 .. GlyfImages.NoContourGlyphRecord(), .. GlyfImages.NoContourGlyphRecord(),
                 .. GlyfImages.NoContourGlyphRecord(), .. GlyfImages.NoContourGlyphRecord()]),
            (SubsetFontFixture.LocaTag,
                GlyfImages.LongLoca([0u, 12, 24, 36, 48, 60, 72, 84, 96])),
            (SubsetFontFixture.CmapTag, SubsetFontFixture.CmapCanonical((0x41, 1))),
        };
        tables.AddRange(extra);
        return SubsetFontFixture.Face([.. tables]);
    }

    private static SubsetRequest CompactPreserve(FontFaceRef face,
        IReadOnlyList<ushort> gids) => new()
    {
        Face = face,
        Root = new SubsetGlyphRoot { Kind = SubsetRootKind.ExplicitGlyphSet, Gids = gids },
        Mode = SubsetMode.CompactRemap,
        Options = new SubsetOptions { PreserveLayout = true }
    };

    private static GdefTable ReadBack(SubsetResult result, ushort expectedNumGlyphs)
    {
        byte[] output = OutputBytes(result);
        byte[] gdef = TableBytes(output, SubsetFontFixture.GdefTag);
        Ok(GdefView.TryOpen(gdef, out var view, out var diagnostic,
            DataLocation.Anonymous(SubsetFontFixture.GdefTag)));
        Assert.Equal(DiagnosticCode.None, diagnostic.Code);
        Ok(view!.TryReadTable(expectedNumGlyphs, 0, out var model, out diagnostic));
        Assert.Equal(DiagnosticCode.None, diagnostic.Code);
        Assert.NotNull(model);
        return model!;
    }

    // The shrink form (OQ-S2b-3): a class entry whose glyph the map dropped drops,
    // the surviving entries keep their class values exactly (no renumbering), and
    // the rebuilt table re-answers through the typed read over the output domain.
    [Fact]
    public void CompactPreserveShrinksClassDefEntriesAndKeepsClassValues()
    {
        byte[] gdef = GdefBytes(GdefVersion10,
            Classes(new ClassDefRange(1, 1, 1), new ClassDefRange(2, 2, 1),
                new ClassDefRange(6, 6, 2)), null, null);
        var status = SubsetOperation.TryPrepare(
            CompactPreserve(Face(gdef), [1]), out var result, out var report,
            out var diagnostic);
        Assert.Equal(ReadStatus.Done, status);
        Assert.Equal(DiagnosticCode.None, diagnostic.Code);
        Assert.NotNull(result);
        // The seed {0, 1} keeps GID 1's base-class entry and drops 2 and 6's.
        var model = ReadBack(result!, 2);
        var classes = model.GlyphClasses;
        Assert.NotNull(classes);
        Assert.Equal(1, classes.Ranges.Length);
        Assert.Equal(new ClassDefRange(1, 1, 1), classes.Ranges[0]);
        Assert.Equal(1, classes.ClassOf(1));
        Assert.Equal(0, classes.ClassOf(0));
    }

    // The keep form: every classed glyph retained — the entries re-group into
    // maximal runs (the canonical emission), so the class answers are identical
    // per glyph while the wire is wire-equivalent, not byte-identical.
    [Fact]
    public void CompactPreserveKeepsAllEntriesWithCanonicalRunGrouping()
    {
        byte[] gdef = GdefBytes(GdefVersion10,
            Classes(new ClassDefRange(1, 1, 1), new ClassDefRange(2, 2, 1),
                new ClassDefRange(6, 6, 2)), null, null);
        var status = SubsetOperation.TryPrepare(
            CompactPreserve(Face(gdef), [1, 2, 6]), out var result, out _,
            out var diagnostic);
        Assert.Equal(ReadStatus.Done, status);
        Assert.Equal(DiagnosticCode.None, diagnostic.Code);
        var model = ReadBack(result!, 4);
        Assert.NotNull(model.GlyphClasses);
        // The map renumbers: retained {0,1,2,6} -> output GIDs {0,1,2,3}, so the
        // input-6 entry lands on output 3 with its class value untouched.
        Assert.Equal(
            new ClassDefRange[] { new(1, 2, 1), new(3, 3, 2) },
            model.GlyphClasses.Ranges.ToArray());
        Assert.Equal(1, model.GlyphClasses.ClassOf(2));
        Assert.Equal(2, model.GlyphClasses.ClassOf(3));
    }

    // Mark sets remap with their set ordinals stable (the markFilteringSet index
    // domain) and every member present — the closure merged the mark coverage into
    // the retained set, so the rewrite is total.
    [Fact]
    public void CompactPreserveRemapsMarkSetsWithStableOrdinals()
    {
        Ok(CoverageTableCompiler.TryCreate(stackalloc ushort[] { 2, 3 }, NumGlyphs, 1,
            out var set, out var diagnostic));
        Assert.Equal(DiagnosticCode.None, diagnostic.Code);
        Ok(MarkGlyphSetsTableCompiler.TryCreate([set!], NumGlyphs, out var markSets,
            out diagnostic));
        Assert.Equal(DiagnosticCode.None, diagnostic.Code);
        byte[] gdef = GdefBytes(GdefVersion12, null, null, markSets);
        var status = SubsetOperation.TryPrepare(
            CompactPreserve(Face(gdef), [1]), out var result, out _,
            out diagnostic);
        Assert.Equal(ReadStatus.Done, status);
        Assert.Equal(DiagnosticCode.None, diagnostic.Code);
        // The mark coverage merged GIDs 2 and 3 into the retained set: {0,1,2,3}.
        var model = ReadBack(result!, 4);
        Assert.NotNull(model.MarkGlyphSets);
        Assert.Single(model.MarkGlyphSets.Sets);
        Assert.Equal(new ushort[] { 2, 3 }, model.MarkGlyphSets.Sets[0].Glyphs.ToArray());
    }

    // The prefix discipline (OQ-S2b-2): an unrewritten layout table keeps its
    // decide-stage code-23 refusal as the first diagnostic under compact+Preserve.
    [Fact]
    public void CompactPreserveKeepsTheDecideRefusalForUnrewrittenTables()
    {
        byte[] gdef = GdefBytes(GdefVersion10,
            Classes(new ClassDefRange(1, 1, 1)), null, null);
        var status = SubsetOperation.TryPrepare(
            CompactPreserve(Face(gdef,
                (SubsetFontFixture.KernTag, SubsetFontFixture.Dummy())), [1]),
            out var result, out _, out var diagnostic);
        Assert.Equal(ReadStatus.UnsupportedFormat, status);
        Assert.Equal(DiagnosticCode.LayoutFamilyUnsupported, diagnostic.Code);
        Assert.Equal(SubsetFontFixture.KernTag, diagnostic.Location.TableTag);
        Assert.Null(result);
    }

    // Zero drift (M1): compact without PreserveLayout still refuses at the decide
    // stage with the unchanged code-23 contract.
    [Fact]
    public void CompactWithoutPreserveStillRefusesTheLayoutFamily()
    {
        byte[] gdef = GdefBytes(GdefVersion10,
            Classes(new ClassDefRange(1, 1, 1)), null, null);
        var request = new SubsetRequest
        {
            Face = Face(gdef),
            Root = new SubsetGlyphRoot { Kind = SubsetRootKind.ExplicitGlyphSet, Gids = [1] },
            Mode = SubsetMode.CompactRemap,
            Options = new SubsetOptions()
        };
        var status = SubsetOperation.TryPrepare(request, out var result, out _,
            out var diagnostic);
        Assert.Equal(ReadStatus.UnsupportedFormat, status);
        Assert.Equal(DiagnosticCode.LayoutFamilyUnsupported, diagnostic.Code);
        Assert.Equal(SubsetFontFixture.GdefTag, diagnostic.Location.TableTag);
        Assert.Null(result);
    }

    private static byte[] OutputBytes(SubsetResult result)
    {
        byte[] output = new byte[result.Font.OutputLength];
        Assert.Equal(ReadStatus.Done,
            result.Font.TryCopyTo(output, out int written, out _));
        Assert.Equal(output.Length, written);
        return output;
    }

    private static byte[] TableBytes(byte[] sfnt, uint tag)
    {
        int numTables = (sfnt[4] << 8) | sfnt[5];
        for (int i = 0; i < numTables; i++)
        {
            int at = 12 + 16 * i;
            uint found = (uint)(sfnt[at] << 24 | sfnt[at + 1] << 16 |
                sfnt[at + 2] << 8 | sfnt[at + 3]);
            if (found != tag) continue;
            int offset = (sfnt[at + 8] << 24) | (sfnt[at + 9] << 16) |
                (sfnt[at + 10] << 8) | sfnt[at + 11];
            int length = (sfnt[at + 12] << 24) | (sfnt[at + 13] << 16) |
                (sfnt[at + 14] << 8) | sfnt[at + 15];
            return sfnt[offset..(offset + length)];
        }
        throw new Xunit.Sdk.XunitException($"tag {tag} missing from output");
    }
}
