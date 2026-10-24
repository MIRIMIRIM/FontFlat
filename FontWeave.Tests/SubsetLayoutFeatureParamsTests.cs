// The layout rewrite's FeatureParams preservation face and the PairPos format-2
// compact emission profile (the 2026-10-18 baseline round's two probe-driven
// widenings).
//
// The params face: the three spec-defined FeatureParams families (SsParameters,
// CvParameters, FeatureParamsSize) are GID-free, so the materialized read byte-
// copies the block and the whole-table/scoped rewrite re-emits it inside the
// FeatureList fragment — pinned here end-to-end over the synthetic face (the
// source-sans/source-serif corpus shape: cv/ss rows in GSUB, the shared 'size'
// row family in GPOS), plus the conservative boundary (a nonzero raw without a
// payload still refuses at the writer, Detail=raw) and the read's strict version
// word. The PairPos face: the canonical emission's subtable-relative offsets can
// overflow once the rebuilt ClassDefs scatter under the remap; the compact
// profile re-plan (Coverage+Devices ahead of the matrix) lands the emission.
using FontWeave.Core;
using FontWeave.Operations;
using FontWeave.Operations.Subset;
using FontWeave.Tables.Layout;
using Xunit;

namespace FontWeave.Tests;

public sealed class SubsetLayoutFeatureParamsTests
{
    private const int NumGlyphs = 10;
    private const uint CvTag = 0x63763031; // 'cv01'
    private const uint SsTag = 0x73733031; // 'ss01'
    private const uint SizeFeatureTag = 0x73697A65; // 'size'

    private static void Ok(ReadStatus status)
    {
        Assert.Equal(ReadStatus.Done, status);
    }

    private static void NoDiagnostic(FontDiagnostic diagnostic)
    {
        Assert.Equal(DiagnosticCode.None, diagnostic.Code);
    }

    // CvParameters: version 0, uiLabel 1, tooltip 2, sample 3, numNamed 0,
    // firstParamUI 4, charCount 2, characters {U+0041, U+1F600} as uint24 values
    // (the spec's 3-byte character element).
    private static readonly byte[] CvParams =
    [
        0x00, 0x00, 0x00, 0x01, 0x00, 0x02, 0x00, 0x03,
        0x00, 0x00, 0x00, 0x04, 0x00, 0x02,
        0x00, 0x00, 0x41, 0x01, 0xF6, 0x00,
    ];

    // SsParameters: version 0, uiNameID 10.
    private static readonly byte[] SsParams = [0x00, 0x00, 0x00, 0x0A];

    // FeatureParamsSize: designSize 100 (10 pt), the name/range words zero.
    private static readonly byte[] SizeParams =
    [
        0x00, 0x64, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
    ];

    private static LookupTable SingleLookup()
    {
        Ok(SingleSubstTableCompiler.TryCreate(stackalloc ushort[] { 1 },
            stackalloc ushort[] { 2 }, NumGlyphs, 1, out var single, out var diagnostic));
        NoDiagnostic(diagnostic);
        Ok(LookupTableCompiler.TryCreateSingleSubst(NumGlyphs, [single!], 0, null,
            null, out var lookup, out diagnostic));
        NoDiagnostic(diagnostic);
        return lookup!;
    }

    private static (LayoutScript Script, LayoutFeature Cv, LayoutFeature Ss) ScriptAndFeatures(
        byte[]? cvPayload, byte[]? ssPayload)
    {
        Ok(LayoutListsTableCompiler.TryCreateLangSys(ushort.MaxValue,
            stackalloc ushort[] { 0 }, 1, out var langSys, out var diagnostic));
        NoDiagnostic(diagnostic);
        Ok(LayoutListsTableCompiler.TryCreateScript(0x44464C54, langSys!, [],
            2, out var script, out diagnostic));
        NoDiagnostic(diagnostic);
        var budget = new BuildBudget(null);
        Ok(LayoutListsTableCompiler.TryCreateFeature(CvTag, 1, [0], 1,
            out var cvFeature, out diagnostic, budget, default, cvPayload));
        NoDiagnostic(diagnostic);
        Ok(LayoutListsTableCompiler.TryCreateFeature(SsTag, 1, [0], 1,
            out var ssFeature, out diagnostic, budget, default, ssPayload));
        NoDiagnostic(diagnostic);
        return (script!, cvFeature!, ssFeature!);
    }

    private static byte[] CompiledGsub(LayoutScript script, params LayoutFeature[] features)
    {
        Ok(GsubTableCompiler.TryCreate(LayoutListsLayout.Version10, 0, [script],
            features, [SingleLookup()], NumGlyphs, out var root, out var diagnostic));
        NoDiagnostic(diagnostic);
        Ok(GsubTableCompiler.TryCompile(root!, null, out var bytes, out diagnostic));
        NoDiagnostic(diagnostic);
        Assert.NotNull(bytes);
        return bytes!;
    }

    private static FontFaceRef Face(LayoutLookupKind kind, byte[] tableBytes)
    {
        uint tag = kind == LayoutLookupKind.Gsub
            ? SubsetFontFixture.GsubTag
            : SubsetFontFixture.GposTag;
        var glyf = new List<byte>();
        var loca = new List<uint> { 0 };
        for (int i = 0; i < NumGlyphs; i++)
        {
            var record = GlyfImages.NoContourGlyphRecord();
            glyf.AddRange(record);
            loca.Add(loca[^1] + (uint)record.Length);
        }
        return SubsetFontFixture.Face(
            (SubsetFontFixture.HeadTag, SubsetFontFixture.Head()),
            (SubsetFontFixture.MaxpTag, SubsetFontFixture.Maxp(NumGlyphs)),
            (tag, tableBytes),
            (SubsetFontFixture.GlyfTag, [.. glyf]),
            (SubsetFontFixture.LocaTag, GlyfImages.LongLoca([.. loca])),
            (SubsetFontFixture.CmapTag, SubsetFontFixture.CmapCanonical((0x41, 1))));
    }

    private static SubsetResult Run(FontFaceRef face, IReadOnlyList<ushort> gids)
    {
        var request = new SubsetRequest
        {
            Face = face,
            Root = new SubsetGlyphRoot { Kind = SubsetRootKind.ExplicitGlyphSet, Gids = gids },
            Mode = SubsetMode.CompactRemap,
            Options = new SubsetOptions { PreserveLayout = true }
        };
        var status = SubsetOperation.TryPrepare(request, out var result, out _,
            out var diagnostic);
        Assert.Equal(ReadStatus.Done, status);
        NoDiagnostic(diagnostic);
        Assert.NotNull(result);
        return result!;
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

    private static LayoutListsTable ReadLists(byte[] table, LayoutLookupKind kind,
        int numGlyphs)
    {
        uint tag = kind == LayoutLookupKind.Gsub
            ? SubsetFontFixture.GsubTag
            : SubsetFontFixture.GposTag;
        Ok(LayoutListsView.TryOpen(table, out var view, out var diagnostic,
            DataLocation.Anonymous(tag)));
        NoDiagnostic(diagnostic);
        Ok(view!.TryReadTable(numGlyphs, kind, null, out var model, out diagnostic));
        NoDiagnostic(diagnostic);
        Assert.NotNull(model);
        return model!;
    }

    // The corpus shape (source-sans GSUB): cv/ss rows carry FeatureParams; the
    // compact+preserve rewrite re-emits both blocks byte-identically with their
    // slots re-pointed, and the typed read decodes them from the OUTPUT wire.
    [Fact]
    public void CompactPreserveReEmitsTheSsAndCvParameterBlocks()
    {
        var (script, cv, ss) = ScriptAndFeatures(CvParams, SsParams);
        byte[] gsub = CompiledGsub(script, cv, ss);
        // Round trip 0: the emitted wire's typed read decodes both payloads.
        var source = ReadLists(gsub, LayoutLookupKind.Gsub, NumGlyphs);
        Assert.Equal(2, source.Features.Length);
        Assert.Equal(CvParams, source.Features[0].ParamsPayload);
        Assert.Equal(SsParams, source.Features[1].ParamsPayload);
        // The closure retains {0, 1, 2, 3}; the whole-table rewrite rides.
        var result = Run(Face(LayoutLookupKind.Gsub, gsub), [1]);
        var output = ReadLists(TableBytes(OutputBytes(result), SubsetFontFixture.GsubTag),
            LayoutLookupKind.Gsub, 4);
        Assert.Equal(2, output.Features.Length);
        Assert.Equal(CvTag, output.Features[0].Tag);
        Assert.NotEqual((ushort)0, output.Features[0].ParamsOffsetRaw);
        Assert.Equal(CvParams, output.Features[0].ParamsPayload);
        Assert.Equal(SsTag, output.Features[1].Tag);
        Assert.NotEqual((ushort)0, output.Features[1].ParamsOffsetRaw);
        Assert.Equal(SsParams, output.Features[1].ParamsPayload);
        // Both lookups' subtable count rode (the RF-S2b-2 census identity).
        Assert.Equal(1, output.Lookups.Length);
    }

    // The GPOS shape (source-sans): 'size' rows carry the unversioned
    // FeatureParamsSize; the same preservation face rides the GPOS rewrite.
    [Fact]
    public void CompactPreserveReEmitsTheSizeParameterBlock()
    {
        Ok(SinglePosTableCompiler.TryCreate(stackalloc ushort[] { 1 },
            [new StaticValueRecord(0, 0, 5, 0)], NumGlyphs, 2, 0x0004,
            out var single, out var diagnostic));
        NoDiagnostic(diagnostic);
        Ok(LookupTableCompiler.TryCreateSinglePos(NumGlyphs, [single!], 0, null,
            null, out var lookup, out diagnostic));
        NoDiagnostic(diagnostic);
        Ok(LayoutListsTableCompiler.TryCreateLangSys(ushort.MaxValue,
            stackalloc ushort[] { 0 }, 1, out var langSys, out diagnostic));
        NoDiagnostic(diagnostic);
        Ok(LayoutListsTableCompiler.TryCreateScript(0x44464C54, langSys!, [],
            1, out var script, out diagnostic));
        NoDiagnostic(diagnostic);
        var budget = new BuildBudget(null);
        Ok(LayoutListsTableCompiler.TryCreateFeature(SizeFeatureTag, 1, [0], 1,
            out var sizeFeature, out diagnostic, budget, default, SizeParams));
        NoDiagnostic(diagnostic);
        Ok(GposTableCompiler.TryCreate(LayoutListsLayout.Version10, 0, [script!],
            [sizeFeature!], [lookup!], NumGlyphs, out var root, out diagnostic));
        NoDiagnostic(diagnostic);
        Ok(GposTableCompiler.TryCompile(root!, null, out var gpos, out diagnostic));
        NoDiagnostic(diagnostic);
        Assert.NotNull(gpos);
        var result = Run(Face(LayoutLookupKind.Gpos, gpos!), [1]);
        var output = ReadLists(TableBytes(OutputBytes(result), SubsetFontFixture.GposTag),
            LayoutLookupKind.Gpos, 4);
        var feature = Assert.Single(output.Features.ToArray());
        Assert.Equal(SizeFeatureTag, feature.Tag);
        Assert.NotEqual((ushort)0, feature.ParamsOffsetRaw);
        Assert.Equal(SizeParams, feature.ParamsPayload);
    }

    // The conservative boundary: a nonzero raw without a materialized payload
    // still refuses at the writer — the pre-fix refusal shape unchanged
    // (Detail=raw, Expected=0, at the feature's own params slot). The raw echoes
    // the source-sans cv01 witness.
    [Fact]
    public void NonZeroParamsRawWithoutPayloadStillRefusesAtTheWriter()
    {
        Ok(LayoutListsTableCompiler.TryCreateLangSys(ushort.MaxValue,
            stackalloc ushort[] { 0 }, 1, out var langSys, out var diagnostic));
        NoDiagnostic(diagnostic);
        Ok(LayoutListsTableCompiler.TryCreateScript(0x44464C54, langSys!, [],
            1, out var script, out diagnostic));
        NoDiagnostic(diagnostic);
        Ok(LayoutListsTableCompiler.TryCreateFeature(CvTag, 7528, [0], 1,
            out var cvFeature, out diagnostic));
        NoDiagnostic(diagnostic);
        Ok(GsubTableCompiler.TryCreate(LayoutListsLayout.Version10, 0, [script!],
            [cvFeature!], [SingleLookup()], NumGlyphs, out var root, out diagnostic));
        NoDiagnostic(diagnostic);
        var status = GsubTableCompiler.TryCompile(root!, null, out _,
            out diagnostic);
        Assert.Equal(ReadStatus.UnsupportedFormat, status);
        Assert.Equal(DiagnosticCode.UnsupportedOperation, diagnostic.Code);
        Assert.Equal((ulong)7528, diagnostic.Detail);
        Assert.Equal((ulong)0, diagnostic.Expected);
        Assert.Equal(DataField.Offset, diagnostic.Location.Field);
    }

    // The read traversal's strict checks: a CvParameters version word other than
    // 0 refuses the materialized read (validate mode keeps the raw observation).
    [Fact]
    public void CorruptCvParameterVersionWordRefusesTheMaterializedRead()
    {
        var (script, cv, ss) = ScriptAndFeatures(CvParams, SsParams);
        byte[] gsub = CompiledGsub(script, cv, ss);
        // Locate the emitted CvParameters block through the wire: FeatureList →
        // first feature table (cv01 sorts before ss01) → its params slot, then
        // flip the block's version word (the raw is feature-table-relative).
        ushort featureListAt = (ushort)(gsub[6] << 8 | gsub[7]);
        ushort featureCount = (ushort)(gsub[featureListAt] << 8 | gsub[featureListAt + 1]);
        Assert.True(featureCount >= 2);
        int cvTableAt = featureListAt + (gsub[featureListAt + 6] << 8 |
            gsub[featureListAt + 7]);
        int cvParamsAt = cvTableAt + (gsub[cvTableAt] << 8 | gsub[cvTableAt + 1]);
        Assert.Equal(0, gsub[cvParamsAt]);
        gsub[cvParamsAt + 1] = 0x01;
        Ok(LayoutListsView.TryOpen(gsub, out var view, out var diagnostic,
            DataLocation.Anonymous(SubsetFontFixture.GsubTag)));
        NoDiagnostic(diagnostic);
        var status = view!.TryReadTable(NumGlyphs, LayoutLookupKind.Gsub, null,
            out _, out diagnostic);
        Assert.Equal(ReadStatus.UnsupportedFormat, status);
        Assert.Equal(DiagnosticCode.UnsupportedVersion, diagnostic.Code);
        Assert.Equal((ulong)1, diagnostic.Detail);
    }

    // The PairPos format-2 compact profile: the canonical emission's Coverage
    // offset overflows behind the 64K matrix, so the two-candidate re-plan pulls
    // Coverage (and the Device region) ahead of the matrix and the emission lands;
    // the re-read answers the same matrix cells.
    [Fact]
    public void PairPosFormat2RePlansTheCompactProfileWhenCanonicalOverflows()
    {
        const int class1Count = 162, class2Count = 100;
        const int glyphDomain = 200;
        Ok(CoverageTableCompiler.TryCreate(stackalloc ushort[] { 1 }, glyphDomain, 1,
            out var coverage, out var diagnostic));
        NoDiagnostic(diagnostic);
        Ok(ClassDefTableCompiler.TryCreate([new ClassDefRange(0, 199, 1)], glyphDomain,
            1, out var classDef1, out diagnostic));
        NoDiagnostic(diagnostic);
        Ok(ClassDefTableCompiler.TryCreate([new ClassDefRange(0, 199, 1)], glyphDomain,
            1, out var classDef2, out diagnostic));
        NoDiagnostic(diagnostic);
        var values1 = new StaticValueRecord[class1Count * class2Count];
        var values2 = new StaticValueRecord[values1.Length];
        values1[1 * class2Count + 1] = new StaticValueRecord(0, 0, 42, 0);
        Ok(PairPosTableCompiler.TryCreate(coverage!.Glyphs, classDef1!.Ranges,
            classDef2!.Ranges, class1Count, class2Count, values1, values2,
            glyphDomain, 0x0004, 0, out var pair, out diagnostic));
        NoDiagnostic(diagnostic);
        Ok(PairPosTableCompiler.TryCompile(pair!, out var bytes, out diagnostic));
        NoDiagnostic(diagnostic);
        Assert.NotNull(bytes);
        // The canonical order would place Coverage behind the 64800-byte matrix
        // and the two dense ClassDefs (>65535); the emitted wire carries the
        // compact profile's layout and re-reads to the same answers.
        Assert.True(bytes!.Length < 66000);
        Ok(PairPosView.TryOpen(bytes, out var view, out diagnostic,
            DataLocation.Anonymous(SubsetFontFixture.GposTag)));
        NoDiagnostic(diagnostic);
        Ok(view!.TryRead(glyphDomain, out var model, out diagnostic));
        NoDiagnostic(diagnostic);
        Assert.Equal(2, model!.Format);
        Assert.Equal(class1Count, model.Class1Count);
        Assert.Equal(class2Count, model.Class2Count);
        model.TryGetMatrixValue(1, 1, out var cell, out _, out diagnostic);
        NoDiagnostic(diagnostic);
        Assert.Equal(42, cell.XAdvance);
        Assert.Equal(new ushort[] { 1 }, model.Coverage.Glyphs.ToArray());
    }
}
