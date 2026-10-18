// CLI subset receipt projection pins (S-cli card: the JSON "report" subtree the CLI
// prints must carry every SubsetReport member the python SCli gate's REPORT_KEYS pins,
// so a new report member without a projection entry fails here instead of silently
// dropping from the auditor surface).
//
// The report is assembled through the internal constructor (the single assembly point
// the pipeline itself uses) rather than a wire fixture: the projection reads only the
// report's public getters, and the real-path report content is already pinned by the
// pipeline tests. The expected JSON shape is produced with the same serializer options
// the CLI's single emission site uses, so the assertions read the exact receipt bytes
// an auditor sees (member order included).
//
// The two members the constructor route cannot exercise — a non-empty diagnostics
// array (a refusal freezes its diagnostic into the report) and the top-level
// decisions projection (private to the CLI) — run through Program.Execute over a
// real wire fixture, so the assertions read both outcome paths' actual receipts.
using System.Text.Json;
using FontWeave.Core;
using FontWeave.Operations;
using FontWeave.Operations.Subset;
using FontWeave.Tables;
using Xunit;

namespace FontWeave.Tests;

public sealed class CliReceiptProjectionTests
{
    // The projection's top-level member set, in declaration (emission) order. Adding a
    // report member without projecting it, or removing/rename one, breaks the set match.
    private static readonly string[] ExpectedReceiptKeys =
    [
        "mode", "modeName", "unselectedSlotCount", "closureAddedGids",
        "layoutClosureAddedGids", "seacClosureAddedGids", "subrPrunedGlobal",
        "subrPrunedLocalByFd", "droppedTables", "optionRecords",
        "cmapConsumedNotEmitted", "mapArtifactSha256", "layoutReferenceCensus",
        "layoutRewriteCensus", "layoutScopeCensus", "cff", "vorg",
        "maxpMaximaNotRecomputed", "diagnostics"
    ];

    private const uint Gsub = 0x47535542; // "GSUB"
    private const uint Dsig = 0x44534947; // "DSIG"

    private static readonly JsonSerializerOptions ReceiptJsonOptions = new() { WriteIndented = true };

    [Fact]
    public void ProjectionCarriesTheFullReceiptKeySet()
    {
        object projection = FontWeave.Cli.Program.SubsetReportProjection(PopulatedReport());
        string[] keys = projection.GetType().GetProperties().Select(property => property.Name)
            .OrderBy(name => name).ToArray();
        Assert.Equal(ExpectedReceiptKeys.OrderBy(name => name).ToArray(), keys);
    }

    [Fact]
    public void ProjectionCarriesEveryPopulatedMemberValue()
    {
        JsonElement receipt = ReceiptJson(PopulatedReport());
        Assert.Equal((int)SubsetMode.CompactRemap, receipt.GetProperty("mode").GetInt32());
        Assert.Equal("CompactRemap", receipt.GetProperty("modeName").GetString());
        Assert.Equal(3, receipt.GetProperty("unselectedSlotCount").GetInt32());
        Assert.Equal([5], Ints(receipt.GetProperty("closureAddedGids")));
        Assert.Equal([7, 9], Ints(receipt.GetProperty("layoutClosureAddedGids")));
        Assert.Equal([1, 2], Ints(receipt.GetProperty("seacClosureAddedGids")));
        Assert.Equal(0, receipt.GetProperty("subrPrunedGlobal").GetInt32());
        Assert.Equal([2], Ints(receipt.GetProperty("subrPrunedLocalByFd")));
        Assert.Equal([Dsig], receipt.GetProperty("droppedTables").EnumerateArray()
            .Select(value => value.GetUInt32()).ToArray());
        JsonElement option = receipt.GetProperty("optionRecords")[0];
        Assert.Equal((int)SubsetOptionId.Mode, option.GetProperty("id").GetInt32());
        Assert.Equal("Mode", option.GetProperty("idName").GetString());
        Assert.Equal(Gsub, option.GetProperty("tag").GetUInt32());
        Assert.Equal("GSUB", option.GetProperty("tagText").GetString());
        Assert.Equal("compact", option.GetProperty("value").GetString());
        JsonElement consumed = receipt.GetProperty("cmapConsumedNotEmitted")[0];
        Assert.Equal(1, consumed.GetProperty("platformId").GetInt32());
        Assert.Equal(0, consumed.GetProperty("encodingId").GetInt32());
        Assert.Equal(new string('a', 64), receipt.GetProperty("mapArtifactSha256").GetString());
        JsonElement reference = receipt.GetProperty("layoutReferenceCensus")[0];
        Assert.Equal(Gsub, reference.GetProperty("tag").GetUInt32());
        Assert.Equal("GSUB", reference.GetProperty("tagText").GetString());
        Assert.Equal(4, reference.GetProperty("referencedGidCount").GetInt32());
        JsonElement rewrite = receipt.GetProperty("layoutRewriteCensus")[0];
        Assert.Equal(Gsub, rewrite.GetProperty("tag").GetUInt32());
        Assert.Equal("GSUB", rewrite.GetProperty("tagText").GetString());
        Assert.Equal(2, rewrite.GetProperty("inputSubtableCount").GetInt32());
        Assert.Equal(1, rewrite.GetProperty("outputSubtableCount").GetInt32());
        Assert.Equal(30, rewrite.GetProperty("inputBytes").GetInt32());
        Assert.Equal(20, rewrite.GetProperty("outputBytes").GetInt32());
        Assert.True(rewrite.GetProperty("countsInvarianceHeld").GetBoolean());
        JsonElement scope = receipt.GetProperty("layoutScopeCensus")[0];
        Assert.Equal(Gsub, scope.GetProperty("tag").GetUInt32());
        Assert.Equal("GSUB", scope.GetProperty("tagText").GetString());
        Assert.Equal(1, scope.GetProperty("inputScriptCount").GetInt32());
        Assert.Equal(1, scope.GetProperty("survivingScriptCount").GetInt32());
        Assert.Equal(2, scope.GetProperty("inputFeatureCount").GetInt32());
        Assert.Equal(1, scope.GetProperty("survivingFeatureCount").GetInt32());
        Assert.Equal(3, scope.GetProperty("inputLookupCount").GetInt32());
        Assert.Equal(2, scope.GetProperty("survivingLookupCount").GetInt32());
        Assert.Equal(1, scope.GetProperty("scriptTagMatches").GetInt32());
        Assert.Equal(0, scope.GetProperty("languageTagMatches").GetInt32());
        Assert.Equal(1, scope.GetProperty("featureTagMatches").GetInt32());
        Assert.Equal(0, scope.GetProperty("orphanMarkSets").GetInt32());
        JsonElement cff = receipt.GetProperty("cff");
        Assert.Equal("CFF1 name-keyed", cff.GetProperty("flavor").GetString());
        Assert.Equal(12, cff.GetProperty("charStringsCount").GetInt32());
        Assert.True(cff.GetProperty("charsetForm").GetProperty("predefined").GetBoolean());
        Assert.Equal(0, cff.GetProperty("charsetForm").GetProperty("format").GetInt32());
        Assert.True(cff.GetProperty("encodingForm").GetProperty("predefined").GetBoolean());
        Assert.Equal(0, cff.GetProperty("encodingForm").GetProperty("format").GetInt32());
        Assert.False(cff.GetProperty("encodingForm").GetProperty("hasSupplement").GetBoolean());
        Assert.True(cff.GetProperty("charsetSidColumnReachable").GetBoolean());
        Assert.Equal(JsonValueKind.Null, cff.GetProperty("rosSids").ValueKind);
        Assert.Equal(JsonValueKind.Null, cff.GetProperty("cidCount").ValueKind);
        Assert.Equal(JsonValueKind.Null, cff.GetProperty("fdSelectFormat").ValueKind);
        // The staleness wording is copied verbatim; compare against the source record
        // instead of restating the sentence here.
        Assert.Equal(PopulatedReport().Cff!.UnselectedSlotSemantics,
            cff.GetProperty("unselectedSlotSemantics").GetString());
        JsonElement vorg = receipt.GetProperty("vorg");
        Assert.Equal(-120, vorg.GetProperty("defaultVertOriginY").GetInt32());
        Assert.Equal(2, vorg.GetProperty("numVertOriginYMetrics").GetInt32());
        Assert.True(receipt.GetProperty("maxpMaximaNotRecomputed").GetBoolean());
        Assert.Equal(0, receipt.GetProperty("diagnostics").GetArrayLength());
    }

    [Fact]
    public void ProjectionCarriesTheEmptySeacAndPruneForm()
    {
        // A report whose face had no seac form and no prune keeps the three members
        // present and neutral — empty arrays and 0, never a null — and the member set
        // is payload-independent, so the empty receipt carries the same keys.
        SubsetReport report = new(SubsetMode.RetainGids, [], 0, [], [], [], [], []);
        JsonElement receipt = ReceiptJson(report);
        string[] keys = FontWeave.Cli.Program.SubsetReportProjection(report).GetType()
            .GetProperties().Select(property => property.Name).OrderBy(name => name).ToArray();
        Assert.Equal(ExpectedReceiptKeys.OrderBy(name => name).ToArray(), keys);
        Assert.Equal(JsonValueKind.Array, receipt.GetProperty("seacClosureAddedGids").ValueKind);
        Assert.Equal(0, receipt.GetProperty("seacClosureAddedGids").GetArrayLength());
        Assert.Equal(0, receipt.GetProperty("subrPrunedGlobal").GetInt32());
        Assert.Equal(JsonValueKind.Array, receipt.GetProperty("subrPrunedLocalByFd").ValueKind);
        Assert.Equal(0, receipt.GetProperty("subrPrunedLocalByFd").GetArrayLength());
    }

    // The refusal path's diagnostics projection: an undeclared out-of-catalogue tag
    // refuses after the wire-order decisions ahead of it formed, and the receipt
    // freezes both — the diagnostic carries its code pair, the tag-bearing location
    // and the tag as the detail word.
    [Fact]
    public void RefusedSubsetCarriesTheDiagnosticProjection()
    {
        (string input, string output) = TempSubsettableFont();
        try
        {
            var receipt = Json(FontWeave.Cli.Program.Execute(
                ["subset", input, "--mode", "compact", "--gids", "1", "--output", output],
                out int exitCode));
            Assert.Equal(1, exitCode);
            JsonElement diagnostic = receipt.GetProperty("report")
                .GetProperty("diagnostics")[0];
            Assert.Equal((int)DiagnosticCode.UnknownTable,
                diagnostic.GetProperty("code").GetInt32());
            Assert.Equal("UnknownTable", diagnostic.GetProperty("codeName").GetString());
            JsonElement location = diagnostic.GetProperty("location");
            Assert.Equal(ZzzzTag, location.GetProperty("tableTag").GetUInt32());
            Assert.Equal((int)OffsetBasis.Table, location.GetProperty("basis").GetInt32());
            Assert.Equal("Table", location.GetProperty("basisName").GetString());
            Assert.Equal(ZzzzTag, diagnostic.GetProperty("detail").GetUInt64());
            // The decisions formed before the refusing row travel at the top level
            // on the refusal path too (the same projection both outcomes read).
            JsonElement decisions = receipt.GetProperty("decisions");
            Assert.Equal(5, decisions.GetArrayLength());
            Assert.Equal(WireOrderKnownTags, decisions.EnumerateArray()
                .Select(row => row.GetProperty("tag").GetUInt32()).ToArray());
        }
        finally { Cleanup(input, output); }
    }

    // The success path's decisions projection: every row carries the full 8-key
    // decision set (tag/tagText/action/actionName/policy/policyName/origin/
    // originName) with each enum's wire value and projected name consistent, and a
    // compact run over a rebuilt-and-preserved table set exercises both actions.
    [Fact]
    public void SuccessfulCompactCarriesTheDecisionProjection()
    {
        (string input, string output) = TempSubsettableFont();
        try
        {
            var receipt = Json(FontWeave.Cli.Program.Execute(
                ["subset", input, "--mode", "compact", "--gids", "1",
                 "--unknown-table", "zzzz=preserve", "--output", output],
                out int exitCode));
            Assert.Equal(0, exitCode);
            Assert.Equal("Done", receipt.GetProperty("statusName").GetString());
            Assert.Equal(0, receipt.GetProperty("report")
                .GetProperty("diagnostics").GetArrayLength());
            bool rebuild = false, preserve = false;
            foreach (JsonElement row in receipt.GetProperty("decisions").EnumerateArray())
            {
                Assert.Equal(ExpectedDecisionKeys, row.EnumerateObject()
                    .Select(property => property.Name).OrderBy(name => name).ToArray());
                int action = row.GetProperty("action").GetInt32();
                Assert.Equal(ActionNames[action], row.GetProperty("actionName").GetString());
                int policy = row.GetProperty("policy").GetInt32();
                Assert.Equal(PolicyNames[policy], row.GetProperty("policyName").GetString());
                int origin = row.GetProperty("origin").GetInt32();
                Assert.Equal(OriginNames[origin], row.GetProperty("originName").GetString());
                rebuild |= action == (int)TableAction.Rebuild;
                preserve |= action == (int)TableAction.Preserve;
            }
            Assert.True(rebuild, "no Rebuild decision");
            Assert.True(preserve, "no Preserve decision");
            JsonElement zzzz = receipt.GetProperty("decisions").EnumerateArray()
                .Single(row => row.GetProperty("tag").GetUInt32() == ZzzzTag);
            Assert.Equal("zzzz", zzzz.GetProperty("tagText").GetString());
            Assert.Equal((int)TableAction.Preserve, zzzz.GetProperty("action").GetInt32());
            Assert.Equal("Preserve", zzzz.GetProperty("actionName").GetString());
            Assert.Equal((int)SubsetPolicy.RetainRaw, zzzz.GetProperty("policy").GetInt32());
            Assert.Equal("RetainRaw", zzzz.GetProperty("policyName").GetString());
            Assert.Equal((int)SubsetDecisionOrigin.RequestOption,
                zzzz.GetProperty("origin").GetInt32());
            Assert.Equal("RequestOption", zzzz.GetProperty("originName").GetString());
        }
        finally { Cleanup(input, output); }
    }

    private static SubsetReport PopulatedReport() => new(
        SubsetMode.CompactRemap,
        [new SubsetTableDecision(Gsub, TableAction.Rebuild, SubsetPolicy.RebuildTyped,
             SubsetDecisionOrigin.MatrixRow),
         new SubsetTableDecision(Dsig, TableAction.Drop, SubsetPolicy.Delete,
             SubsetDecisionOrigin.RequestOption)],
        3,
        [5],
        [Dsig],
        [new SubsetOptionRecord(SubsetOptionId.Mode, Gsub, "compact")],
        [new CmapConsumedRecord(1, 0)],
        [],
        mapArtifactSha256: new string('a', 64),
        cffStaleness: new CffStaleness("CFF1 name-keyed", 12,
            new Cff1CharsetForm(true, 0), new Cff1EncodingForm(true, 0, false),
            true, null, null, null),
        vorgStaleness: new VorgStaleness(-120, 2),
        layoutClosureAddedGids: [7, 9],
        layoutReferenceCensus: [new LayoutTableCensus(Gsub, 4)],
        layoutRewriteCensus: [new LayoutRewriteCensusRow(Gsub, 2, 1, 30, 20, true)],
        layoutScopeCensus: [new LayoutScopeCensusRow(Gsub, 1, 1, 2, 1, 3, 2, 1, 0, 1, 0)],
        seacClosureAddedGids: [1, 2],
        subrPrunedGlobal: 0,
        subrPrunedLocalByFd: [2]);

    private static JsonElement ReceiptJson(SubsetReport report) =>
        JsonSerializer.SerializeToElement(FontWeave.Cli.Program.SubsetReportProjection(report),
            ReceiptJsonOptions);

    private static JsonElement Json(Dictionary<string, object?> receipt) =>
        JsonSerializer.SerializeToElement(receipt);

    private static int[] Ints(JsonElement array) =>
        array.EnumerateArray().Select(value => value.GetInt32()).ToArray();

    // ---- the real-path fixtures (a subsettable TrueType face plus one
    // out-of-catalogue "zzzz" table, the receipt tests' refusal/preserve lever) ----

    private const uint ZzzzTag = 0x7A7A7A7A; // "zzzz", sorts after every known tag

    private static readonly uint[] WireOrderKnownTags =
        [0x636D6170, 0x676C7966, 0x68656164, 0x6C6F6361, 0x6D617870];

    private static readonly string[] ExpectedDecisionKeys =
        ["action", "actionName", "origin", "originName", "policy", "policyName",
         "tag", "tagText"];

    private static readonly IReadOnlyDictionary<int, string> ActionNames =
        new Dictionary<int, string>
        { [(int)TableAction.Preserve] = "Preserve", [(int)TableAction.Rebuild] = "Rebuild",
          [(int)TableAction.Drop] = "Drop" };

    private static readonly IReadOnlyDictionary<int, string> PolicyNames =
        new Dictionary<int, string>
        { [(int)SubsetPolicy.RetainRaw] = "RetainRaw",
          [(int)SubsetPolicy.RebuildTyped] = "RebuildTyped",
          [(int)SubsetPolicy.Delete] = "Delete" };

    private static readonly IReadOnlyDictionary<int, string> OriginNames =
        new Dictionary<int, string>
        { [(int)SubsetDecisionOrigin.MatrixRow] = "MatrixRow",
          [(int)SubsetDecisionOrigin.RequestOption] = "RequestOption",
          [(int)SubsetDecisionOrigin.ModeCoupling] = "ModeCoupling" };

    private static (string Input, string Output) TempSubsettableFont()
    {
        string input = Path.Combine(Path.GetTempPath(),
            $"fontweave-receipt-{Guid.NewGuid():N}.ttf");
        File.WriteAllBytes(input, SubsetFontFixture.Sfnt(false,
            (SubsetFontFixture.HeadTag, CliTtcFixture.ValidHead(indexToLocFormat: 1)),
            (SubsetFontFixture.MaxpTag, SubsetFontFixture.Maxp(4)),
            (SubsetFontFixture.GlyfTag,
            [
                .. SubsetFontFixture.InstructedSimpleGlyph(),   // glyph 0, 18 bytes
                .. GlyfImages.CompositeRecord(2),               // glyph 1 → 2
                .. GlyfImages.NoContourGlyphRecord(),           // glyph 2
                .. GlyfImages.NoContourGlyphRecord()            // glyph 3
            ]),
            (SubsetFontFixture.LocaTag, GlyfImages.LongLoca([0, 18, 34, 46, 58])),
            (SubsetFontFixture.CmapTag, SubsetFontFixture.CmapCanonical((0x41, 1))),
            (ZzzzTag, SubsetFontFixture.Dummy(8))));
        string output = Path.Combine(Path.GetTempPath(),
            $"fontweave-receipt-{Guid.NewGuid():N}.out");
        return (input, output);
    }

    private static void Cleanup(string input, string output)
    {
        File.Delete(input);
        if (File.Exists(output)) File.Delete(output);
    }
}
