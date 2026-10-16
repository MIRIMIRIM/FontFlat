// S2b compact layout rewrite unit tests — the merged file hosting both rewrite
// families: S2b-1's GDEF/GSUB/GPOS commits (②/③) and S2b-2's kern/MATH/JSTF/BASE
// commits (⑤/⑥/⑦/⑧) over the one frozen map and refusal contract.
//
// A synthetic face drives the compact+PreserveLayout path at the unit level: the
// GDEF class-domain per-entry shrink with identical class values (OQ-S2b-3), the
// all-members-kept form, the mark-sets remap under stable set ordinals, and the
// kern family (S2b card §2.3): the identity-map byte fixed point, the partial-map
// renumbering, the class-domain shift, the Apple decide refusal, and the
// remap-miss non-vacuity through the test bridge. The output tables re-read
// through the typed views — the same read chain the pipeline itself validates
// against — never a byte comparison except where the canonical emission is a
// fixed point by design.
using System.Buffers.Binary;
using FontWeave.Containers;
using FontWeave.Core;
using FontWeave.Operations;
using FontWeave.Operations.Subset;
using FontWeave.Tables.Layout;
using Xunit;

namespace FontWeave.Tests;

public sealed class SubsetLayoutRewriteTests
{
    private const int GdefNumGlyphs = 8;
    private const uint GdefVersion10 = 0x00010000, GdefVersion12 = 0x00010002;

    private static void Ok(ReadStatus status)
    {
        Assert.Equal(ReadStatus.Done, status);
    }

    private static ClassDefTable Classes(params ClassDefRange[] ranges)
    {
        Ok(ClassDefTableCompiler.TryCreate(ranges, GdefNumGlyphs, 2, out var model,
            out var diagnostic));
        Assert.Equal(DiagnosticCode.None, diagnostic.Code);
        return model!;
    }

    private static byte[] GdefBytes(uint version, ClassDefTable? classes,
        ClassDefTable? markAttachment, MarkGlyphSetsTable? markSets)
    {
        Ok(GdefTableCompiler.TryCreate(version, GdefNumGlyphs, classes, markAttachment,
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
            (SubsetFontFixture.MaxpTag, SubsetFontFixture.Maxp(GdefNumGlyphs)),
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
        Ok(CoverageTableCompiler.TryCreate(stackalloc ushort[] { 2, 3 }, GdefNumGlyphs, 1,
            out var set, out var diagnostic));
        Assert.Equal(DiagnosticCode.None, diagnostic.Code);
        Ok(MarkGlyphSetsTableCompiler.TryCreate([set!], GdefNumGlyphs, out var markSets,
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

    // The merged tree's cross-family composition (the S2b-1 prefix-discipline test,
    // retired with the transitional face it pinned): the compact prefix rewrites
    // GDEF (S2b-1) and kern (S2b-2) in ONE request over the one frozen map — the
    // former unrewritten-row decide refusal for kern is gone with its migration.
    [Fact]
    public void CompactPreserveRewritesGdefAndKernTogether()
    {
        byte[] gdef = GdefBytes(GdefVersion10,
            Classes(new ClassDefRange(1, 1, 1)), null, null);
        var status = SubsetOperation.TryPrepare(
            CompactPreserve(Face(gdef,
                (SubsetFontFixture.KernTag, Format0Kern((2, 5, -20)))), [1]),
            out var result, out var report, out var diagnostic);
        Ok(status);
        Assert.Equal(DiagnosticCode.None, diagnostic.Code);
        Assert.NotNull(result);
        // Both family rows answered PreserveLayout with the executed rebuild.
        SubsetTableDecision gdefDecision = DecisionsOf(report!)
            .Single(d => d.Tag == SubsetFontFixture.GdefTag);
        Assert.Equal(TableAction.Rebuild, gdefDecision.Action);
        Assert.Equal(SubsetPolicy.RebuildTyped, gdefDecision.Policy);
        SubsetTableDecision kernDecision = DecisionsOf(report!)
            .Single(d => d.Tag == SubsetFontFixture.KernTag);
        Assert.Equal(TableAction.Rebuild, kernDecision.Action);
        Assert.Equal(SubsetPolicy.RebuildTyped, kernDecision.Policy);
        // The census carries the family rows in rewrite order: the GDEF row now,
        // the kern row joining with the census unification (S2b-2 commit ⑧).
        Assert.Equal(SubsetFontFixture.GdefTag,
            report!.LayoutRewriteCensus.Single().Tag);
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

    // ---------------------------------------------------------------------
    // The S2b-2 kern family (S2b card §2.3, merged commit a3bccb6): fixtures,
    // pipeline tests over the shared CompactPreserve request, and the test
    // bridge's non-vacuity proof. GdefNumGlyphs (8) is the GDEF family's face
    // domain; the kern family's faces use NumGlyphs (12).
    // ---------------------------------------------------------------------

    private const int NumGlyphs = 12;
    private const uint KernTag = 0x6B65726E;

    private static byte[] KernBytes(IReadOnlyList<KernSubtable> subtables)
    {
        Ok(KernTableCompiler.TryCreate(KernDialect.Version0, NumGlyphs, subtables,
            out var model, out var diagnostic));
        Assert.Equal(DiagnosticCode.None, diagnostic.Code);
        Ok(KernTableCompiler.TryCompile(model!, out var bytes, out diagnostic));
        Assert.Equal(DiagnosticCode.None, diagnostic.Code);
        return bytes!;
    }

    private static byte[] Format0Kern(params (int Left, int Right, short Value)[] pairs)
    {
        var records = new KernPair[pairs.Length];
        for (int i = 0; i < pairs.Length; i++)
            records[i] = new((ushort)pairs[i].Left, (ushort)pairs[i].Right, pairs[i].Value);
        Ok(KernSubtableCompiler.TryCreateFormat0(NumGlyphs, records, 0x01,
            out var subtable, out var diagnostic));
        Assert.Equal(DiagnosticCode.None, diagnostic.Code);
        return KernBytes([subtable!]);
    }

    private static byte[] Format2Kern(ushort leftFirst, ushort[] leftRows,
        ushort rightFirst, ushort[] rightColumns, short[] values)
    {
        Ok(KernSubtableCompiler.TryCreateFormat2(NumGlyphs, leftFirst, leftRows,
            rightFirst, rightColumns, values, 0x01, out var subtable,
            out var diagnostic));
        Assert.Equal(DiagnosticCode.None, diagnostic.Code);
        return KernBytes([subtable!]);
    }

    private static byte[] AppleKernBytes()
    {
        // The Apple dialect root (fixed32 version word + u32 count) over one
        // Apple-typed format-0 subtable (u32 length, coverage word with the format
        // in the low half, tupleIndex, then the v0-shaped pair body). The read side
        // admits it; the decide stage must refuse the rewrite (RF-S2b-3).
        byte[] root = [0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01];
        byte[] subtable =
        [
            0x00, 0x00, 0x00, 0x16,      // u32 length: 8 header + 14 + 6
            0x00, 0x00,                  // coverage: format 0, no flags
            0x00, 0x00,                  // tupleIndex
            0x00, 0x01,                  // nPairs
            0x00, 0x06, 0x00, 0x00, 0x00, 0x00,   // search triple (1 pair)
            0x00, 0x02, 0x00, 0x03, 0xFF, 0xFF,   // left 2, right 3, value -1
        ];
        return [.. root, .. subtable];
    }

    private static FontFaceRef KernFace(byte[] kern) => SubsetFontFixture.Face(
        (SubsetFontFixture.HeadTag, SubsetFontFixture.Head()),
        (SubsetFontFixture.MaxpTag, SubsetFontFixture.Maxp(NumGlyphs)),
        (KernTag, kern),
        (SubsetFontFixture.GlyfTag,
            [.. Enumerable.Range(0, NumGlyphs)
                .SelectMany(_ => GlyfImages.NoContourGlyphRecord())]),
        (SubsetFontFixture.LocaTag,
            GlyfImages.LongLoca([.. Enumerable.Range(0, NumGlyphs + 1)
                .Select(i => (uint)(12 * i)).ToArray()])),
        (SubsetFontFixture.CmapTag, SubsetFontFixture.CmapCanonical((0x41, 1))));

    private static Dictionary<int, ushort> InputToOutput(SubsetResult result)
    {
        var forward = new Dictionary<int, ushort>();
        foreach (FrozenMapEntry entry in result.Map!.Entries)
            forward[entry.InputGid] = entry.OutputGid;
        return forward;
    }

    private static byte[] OutputKernPayload(SubsetResult result)
    {
        var bytes = new byte[result.Font.OutputLength];
        Ok(result.Font.TryCopyTo(bytes, out int written, out _));
        Assert.Equal(bytes.Length, written);
        ushort count = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(4));
        for (int i = 0; i < count; i++)
        {
            int at = 12 + 16 * i;
            uint tag = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(at));
            if (tag != KernTag) continue;
            uint offset = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(at + 8));
            uint length = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(at + 12));
            return bytes.Skip((int)offset).Take((int)length).ToArray();
        }
        throw new InvalidOperationException("no kern record in the output face");
    }

    private static IReadOnlyList<SubsetTableDecision> DecisionsOf(
        SubsetReport report) => report.Decisions;

    // The identity tier's kern face: the full-GID seed keeps every glyph, the map
    // is the identity, and the canonical re-emission is a fixed point — the output
    // kern payload is byte-identical to the input (the S2b card §7.2 caliber's
    // table-level assertion).
    [Fact]
    public void CompactPreserveKernFormat0IdentityMapIsByteIdentical()
    {
        byte[] kern = Format0Kern((2, 5, -20), (5, 9, 30));
        var status = SubsetOperation.TryPrepare(
            CompactPreserve(KernFace(kern),
                [.. Enumerable.Range(0, NumGlyphs).Select(i => (ushort)i)]),
            out var result, out var report, out var diagnostic);
        Ok(status);
        Assert.Equal(DiagnosticCode.None, diagnostic.Code);
        Assert.NotNull(result);
        SubsetTableDecision decision = DecisionsOf(report!).Single(d => d.Tag == KernTag);
        Assert.Equal(TableAction.Rebuild, decision.Action);
        Assert.Equal(SubsetPolicy.RebuildTyped, decision.Policy);
        Assert.Equal(SubsetDecisionOrigin.MatrixRow, decision.Origin);
        Assert.True(OutputKernPayload(result!).AsSpan().SequenceEqual(kern));
    }

    // The partial tier: seeds {1, 2} plus the closure's format-0 additions
    // {5, 9} shrink the domain; both pair ends renumber through the frozen map and
    // the canonical emission keeps the strict pair order.
    [Fact]
    public void CompactPreserveKernFormat0RemapsBothEnds()
    {
        byte[] kern = Format0Kern((2, 5, -20), (5, 9, 30));
        var status = SubsetOperation.TryPrepare(
            CompactPreserve(KernFace(kern), [1, 2]), out var result, out var report,
            out var diagnostic);
        Ok(status);
        Assert.NotNull(result);
        var forward = InputToOutput(result!);
        // The closure merged both pair ends beyond the seeds.
        Assert.Equal(new ushort[] { 5, 9 }, report!.LayoutClosureAddedGids);
        byte[] output = OutputKernPayload(result!);
        Assert.Equal(1, BinaryPrimitives.ReadUInt16BigEndian(output.AsSpan(2)));
        // subtable at 4: version@4, length@6, coverage@8, pairCount@10, pairs@18.
        Assert.Equal(26, BinaryPrimitives.ReadUInt16BigEndian(output.AsSpan(6)));
        Assert.Equal(0x0001,
            BinaryPrimitives.ReadUInt16BigEndian(output.AsSpan(8)));
        int pairCount = BinaryPrimitives.ReadUInt16BigEndian(output.AsSpan(10));
        Assert.Equal(2, pairCount);
        var expected = new List<(int Left, int Right, short Value)>
        {
            (forward[2], forward[5], -20),
            (forward[5], forward[9], 30),
        };
        var observed = new List<(int Left, int Right, short Value)>();
        for (int p = 0; p < pairCount; p++)
        {
            int at = 18 + 6 * p;
            observed.Add((BinaryPrimitives.ReadUInt16BigEndian(output.AsSpan(at)),
                BinaryPrimitives.ReadUInt16BigEndian(output.AsSpan(at + 2)),
                BinaryPrimitives.ReadInt16BigEndian(output.AsSpan(at + 4))));
        }
        Assert.Equal(expected, observed);
    }

    // Format 2: the class domains shift contiguously — both firstGlyph words
    // renumber, the class words and the matrix cells re-emit verbatim (the decoded
    // matrix at the new array position).
    [Fact]
    public void CompactPreserveKernFormat2ShiftsClassDomainsContiguously()
    {
        byte[] kern = Format2Kern(3, [0, 1, 2], 7, [0, 1],
            [0, 0, -10, -20, -40, -80]);
        var status = SubsetOperation.TryPrepare(
            CompactPreserve(KernFace(kern), [1]), out var result, out var report,
            out var diagnostic);
        Ok(status);
        Assert.NotNull(result);
        var forward = InputToOutput(result!);
        // The closure merged both covered class ranges.
        Assert.Equal(new ushort[] { 3, 4, 5, 7, 8 }, report!.LayoutClosureAddedGids);
        byte[] output = OutputKernPayload(result!);
        int subtable = 4;
        // version@0, length@2, coverage@4 (format in the high byte), rowWidth@6,
        // left/right/array offsets@8/10/12 (subtable-relative).
        Assert.Equal(0x0201,
            BinaryPrimitives.ReadUInt16BigEndian(output.AsSpan(subtable + 4)));
        int leftAt = subtable +
            BinaryPrimitives.ReadUInt16BigEndian(output.AsSpan(subtable + 8));
        int rightAt = subtable +
            BinaryPrimitives.ReadUInt16BigEndian(output.AsSpan(subtable + 10));
        Assert.Equal(forward[3],
            BinaryPrimitives.ReadUInt16BigEndian(output.AsSpan(leftAt)));
        Assert.Equal(forward[7],
            BinaryPrimitives.ReadUInt16BigEndian(output.AsSpan(rightAt)));
        // Contiguity: the covered ordinals sit at +1/+2 (rows) and +1 (columns) of
        // the images — the domain-continuity invariant held by construction.
        Assert.Equal(forward[3] + 1, forward[4]);
        Assert.Equal(forward[3] + 2, forward[5]);
        Assert.Equal(forward[7] + 1, forward[8]);
        // One decoded matrix cell: (row ordinal 2, column ordinal 1) carries the
        // source's -80; the class words re-premultiply as subtable-relative byte
        // offsets (the left rows include the array base, the right columns step 2).
        Assert.Equal(4,
            BinaryPrimitives.ReadUInt16BigEndian(output.AsSpan(subtable + 6)));
        int leftWord = BinaryPrimitives.ReadUInt16BigEndian(
            output.AsSpan(leftAt + 4 + 2 * 2));
        int rightWord = BinaryPrimitives.ReadUInt16BigEndian(
            output.AsSpan(rightAt + 4 + 2 * 1));
        int cellAt = subtable + leftWord + rightWord;
        Assert.Equal(-80, BinaryPrimitives.ReadInt16BigEndian(output.AsSpan(cellAt)));
    }

    // The Apple dialect refuses at the decide stage under the code-23 contract
    // (RF-S2b-3): the read side admits it, the rewrite cannot serve it, and no
    // GPOS conversion is invented.
    [Fact]
    public void CompactPreserveKernAppleDialectRefuses()
    {
        var status = SubsetOperation.TryPrepare(
            CompactPreserve(KernFace(AppleKernBytes()), [1]), out var result,
            out var report, out var diagnostic);
        Assert.Equal(ReadStatus.UnsupportedFormat, status);
        Assert.Null(result);
        Assert.Equal(DiagnosticCode.LayoutFamilyUnsupported, diagnostic.Code);
        Assert.Equal(KernTag, diagnostic.Location.TableTag);
        Assert.Equal(DataField.Version, diagnostic.Location.Field);
        Assert.Equal(1UL, diagnostic.Detail);
        // The refusal is the decide stage's: no kern rebuild decision formed.
        Assert.DoesNotContain(DecisionsOf(report!), d => d.Tag == KernTag);
    }

    // The merged tree's migrated rows (the S2b-2 un-migrated-row refusal test,
    // retired with the RF-S2b-5 transitional face it pinned): the compact window
    // rewrites GSUB (S2b-1) and kern (S2b-2) in one request — the refusal is gone
    // with the prefix discipline that kept the rows back.
    [Fact]
    public void CompactPreserveRewritesGsubAndKernTogether()
    {
        Ok(CoverageTableCompiler.TryCreate(stackalloc ushort[] { 1, 2 },
            NumGlyphs, 1, out var coverage, out var diagnostic));
        Assert.Equal(DiagnosticCode.None, diagnostic.Code);
        Ok(SingleSubstTableCompiler.TryCreate(stackalloc ushort[] { 1, 2 },
            stackalloc ushort[] { 3, 4 }, NumGlyphs, 1, out var single,
            out diagnostic));
        Assert.Equal(DiagnosticCode.None, diagnostic.Code);
        Ok(LookupTableCompiler.TryCreateSingleSubst(NumGlyphs, [single!],
            0, null, null, out var lookup, out diagnostic));
        Assert.Equal(DiagnosticCode.None, diagnostic.Code);
        Ok(LayoutListsTableCompiler.TryCreate(LayoutListsLayout.Version10, 0,
            [], [], [lookup!], NumGlyphs, LayoutLookupKind.Gsub, out var model,
            out diagnostic));
        Assert.Equal(DiagnosticCode.None, diagnostic.Code);
        Ok(GsubTableCompiler.TryCompile(model!, null, out var gsub, out diagnostic));
        Assert.Equal(DiagnosticCode.None, diagnostic.Code);
        const uint gsubTag = 0x47535542;
        FontFaceRef face = SubsetFontFixture.Face(
            (SubsetFontFixture.HeadTag, SubsetFontFixture.Head()),
            (SubsetFontFixture.MaxpTag, SubsetFontFixture.Maxp(NumGlyphs)),
            (KernTag, Format0Kern((2, 5, -20))),
            (gsubTag, gsub!),
            (SubsetFontFixture.GlyfTag,
                [.. Enumerable.Range(0, NumGlyphs)
                    .SelectMany(_ => GlyfImages.NoContourGlyphRecord())]),
            (SubsetFontFixture.LocaTag,
                GlyfImages.LongLoca([.. Enumerable.Range(0, NumGlyphs + 1)
                    .Select(i => (uint)(12 * i)).ToArray()])),
            (SubsetFontFixture.CmapTag, SubsetFontFixture.CmapCanonical((0x41, 1))));
        var status = SubsetOperation.TryPrepare(CompactPreserve(face, [1]),
            out var result, out var report, out diagnostic);
        Ok(status);
        Assert.Equal(DiagnosticCode.None, diagnostic.Code);
        Assert.NotNull(result);
        // Both rows rebuilt typed: the one frozen map served GSUB and kern alike.
        SubsetTableDecision gsubDecision = DecisionsOf(report!)
            .Single(d => d.Tag == gsubTag);
        Assert.Equal(TableAction.Rebuild, gsubDecision.Action);
        SubsetTableDecision kernDecision = DecisionsOf(report!)
            .Single(d => d.Tag == KernTag);
        Assert.Equal(TableAction.Rebuild, kernDecision.Action);
    }

    // The non-vacuity of the remap-miss invariant: a map that drops a referenced
    // glyph refuses under RF-S2b-2 (unreachable through the pipeline — the closure
    // merges every reference before the freeze — so the bridge drives the producer
    // directly, the S2a §3 layer-1 registration shape).
    [Fact]
    public void KernRewriteMissRefusesUnderCode23()
    {
        Ok(KernSubtableCompiler.TryCreateFormat0(NumGlyphs,
            [new KernPair(2, 5, -20)], 0x01, out var subtable, out var diagnostic));
        Assert.Equal(DiagnosticCode.None, diagnostic.Code);
        Ok(KernTableCompiler.TryCreate(KernDialect.Version0, NumGlyphs, [subtable!],
            out var kern, out diagnostic));
        Assert.Equal(DiagnosticCode.None, diagnostic.Code);
        FontFaceRef face = SubsetFontFixture.Face(
            (SubsetFontFixture.HeadTag, SubsetFontFixture.Head()),
            (SubsetFontFixture.MaxpTag, SubsetFontFixture.Maxp(NumGlyphs)));
        Ok(GlyphRemap.TryCreate(face, NumGlyphs, stackalloc ushort[] { 0, 2 },
            out var map, out diagnostic));
        Assert.Equal(DiagnosticCode.None, diagnostic.Code);
        var status = SubsetOperation.RebuildKernForTests(kern!, map!, out _,
            out diagnostic);
        Assert.Equal(ReadStatus.UnsupportedFormat, status);
        Assert.Equal(DiagnosticCode.LayoutFamilyUnsupported, diagnostic.Code);
        Assert.Equal(DataField.GlyphId, diagnostic.Location.Field);
        Assert.Equal(5UL, diagnostic.Detail);
    }
}
