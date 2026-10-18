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
using System.Text.Json;
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

    private static int[] Ints(JsonElement array) =>
        array.EnumerateArray().Select(value => value.GetInt32()).ToArray();
}
