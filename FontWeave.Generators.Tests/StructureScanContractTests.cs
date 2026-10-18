using System.Text;
using Microsoft.CodeAnalysis;
using Xunit;

namespace FontWeave.Generators.Tests;

// A5 generator contracts (card 2.4, GD1-GD11): the finite structure-scan runtime, the
// Core source-closure probe driver mode, the spec 6.3 ledger examples and the build-time
// Follow-DAG/scan-declaration contracts. Cross-boundary assertions exchange strings and
// numbers only (design R4): probe FontWeave.Core objects are never cast to host types.
public sealed class StructureScanContractTests
{
    // A scan leaf (spec 6.3 example 1 shape: bound root, minHeader=2, one unconditional
    // UInt16 scalar, no version/refs/arrays) and the other shared synthetic schemas.
    private const string ScanLeafSchema = """
        [BinaryTable(0x11111111, 2, ContractVersion = 1, GenerateStructureScan = true)]
        [BinaryScalar("Seed", WireType.UInt16, 0, DataField.Payload)]
        public readonly ref partial struct ScanLeafView { }
        """;

    private const string ScanLeafSource = $$"""
        using FontWeave.Core;
        using FontWeave.Schema;
        namespace Probe;
        {{ScanLeafSchema}}
        """;

    private const string V0LeafSchema = """
        [BinaryTable(0x22222222, 2)]
        [BinaryScalar("Seed", WireType.UInt16, 0, DataField.Payload)]
        public readonly ref partial struct V0LeafView { }
        """;

    // Spec 6.3 example 2 root: minHeader=4, two UInt16 offsets A@0/B@2 both = 4, two
    // Follow references, window 6 bytes; the child suffix is 2 bytes.
    private const string LedgerRootReferences = """
        [BinaryTable(0x4C454432, 4, ContractVersion = 1, GenerateStructureScan = true)]
        [BinaryScalar("A", WireType.UInt16, 0, DataField.Offset, Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table)]
        [BinaryScalar("B", WireType.UInt16, 2, DataField.Offset, Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table)]
        [BinaryReference("ToA", "A", typeof(ScanLeafView), Origin = ReferenceOrigin.ViewStart, NullPolicy = ReferenceNullPolicy.Required, Scan = ReferenceScan.Follow)]
        [BinaryReference("ToB", "B", typeof(ScanLeafView), Origin = ReferenceOrigin.ViewStart, NullPolicy = ReferenceNullPolicy.Required, Scan = ReferenceScan.Follow)]
        public readonly ref partial struct LedgerTwoView { }
        """;

    // GD1 (design §8 gate 1): the internal mode compiles the real Core closure into the
    // probe assembly — accessing the internal BuildBudget/StructureScanScope members is
    // itself the same-assembly proof, and the primitive round-trips its facts slot.
    [Fact]
    public void InternalModeCompilesTheRealCoreClosureIntoTheProbe()
    {
        string result = SchemaProbeDriver.Execute($$"""
            using FontWeave.Core;
            using FontWeave.Schema;
            namespace Probe;
            {{ScanLeafSchema}}
            public static class Entry
            {
                public static string Run()
                {
                    var scope = new StructureScanScope(DataLocation.Anonymous(0x11111111));
                    bool empty = scope.TryGetCompleted(4, 2, 0, 0, out var none);
                    scope.Publish(4, 2, 0, 0, StructuralScanCoverage.DeclaredClosure);
                    bool hit = scope.TryGetCompleted(4, 2, 0, 0, out var value);
                    return $"{typeof(BuildBudget).Assembly.GetName().Name}|{typeof(StructureScanScope).Assembly.GetName().Name}|{typeof(Entry).Assembly.GetName().Name}|{empty}|{hit}|{(int)none}|{(int)value}";
                }
            }
            """);
        var parts = result.Split('|');
        Assert.Equal(7, parts.Length);
        Assert.Equal(parts[2], parts[0]);
        Assert.Equal(parts[2], parts[1]);
        Assert.NotEqual("FontWeave", parts[0]);
        Assert.StartsWith("SchemaContractInternal_", parts[0]);
        Assert.Equal("False|True|0|1", $"{parts[3]}|{parts[4]}|{parts[5]}|{parts[6]}");
    }

    // GD2 (spec 6.3 ledger example 1, exact and one-less): scalar probe 1 + body(1+2) = 4;
    // Work3 refuses the root body charge with ResourceLimit/BudgetExceeded at WorkUnits.
    [Fact]
    public void SpecLedgerExampleOneReproducesExactAndOneLess()
    {
        string result = SchemaProbeDriver.Execute($$"""
            using FontWeave.Core;
            using FontWeave.Schema;
            namespace Probe;
            [BinaryTable(0x4C454431, 2, ContractVersion = 1, GenerateStructureScan = true)]
            [BinaryScalar("Value", WireType.UInt16, 0, DataField.Payload)]
            public readonly ref partial struct LedgerOneView { }
            public static class Entry
            {
                public static string Run()
                {
                    byte[] wire = { 0xAA, 0xBB, 0xCC, 0xDD };
                    LedgerOneView.TryOpen(wire, out var view, out _);
                    var exact = new BuildBudget(new BuildLimits { MaxWorkUnits = 4 });
                    var exactStatus = view.TryScanStructure(exact, out var exactCoverage, out var exactDiagnostic);
                    var exactUsage = exact.Usage;
                    var tight = new BuildBudget(new BuildLimits { MaxWorkUnits = 3 });
                    var tightStatus = view.TryScanStructure(tight, out var tightCoverage, out var tightDiagnostic);
                    return $"{exactStatus}|{(int)exactCoverage}|{exactUsage.WorkUnits}|{exactUsage.TemporaryBytes}|{exactDiagnostic.Code}" +
                        $"|{tightStatus}|{(int)tightCoverage}|{tightDiagnostic.Code}|{tightDiagnostic.Location.Field}|{tightDiagnostic.Location.Offset}|{tightDiagnostic.Detail}|{tightDiagnostic.Expected}";
                }
            }
            """);
        Assert.Equal("Done|1|4|0|None|ResourceLimit|0|BudgetExceeded|WorkUnits|0|4|3", result);
    }

    // GD3 (spec 6.3 ledger example 2 with both one-less positions): root probes 2 +
    // body 5 + edge A 1 + child (probe 1 + body 3) + edge B 1 = 13 Work, one slot 40.
    // Work12 refuses at edge B (root B offset, local 2, Detail=13/Expected=12); Temp39
    // refuses at the child slot (child start, local 4, Detail=40/Expected=39).
    [Fact]
    public void SpecLedgerExampleTwoReproducesExactAndBothOneLess()
    {
        string result = SchemaProbeDriver.Execute($$"""
            using FontWeave.Core;
            using FontWeave.Schema;
            namespace Probe;
            {{ScanLeafSchema}}
            {{LedgerRootReferences}}
            public static class Entry
            {
                public static string Run()
                {
                    byte[] wire = { 0x00, 0x04, 0x00, 0x04, 0xAA, 0xBB };
                    LedgerTwoView.TryOpen(wire, out var view, out _);
                    var exact = new BuildBudget(new BuildLimits());
                    var exactStatus = view.TryScanStructure(exact, out var exactCoverage, out _);
                    var exactUsage = exact.Usage;
                    var workTight = new BuildBudget(new BuildLimits { MaxWorkUnits = 12 });
                    var workStatus = view.TryScanStructure(workTight, out var workCoverage, out var workDiagnostic);
                    var tempTight = new BuildBudget(new BuildLimits { MaxTemporaryBytes = 39 });
                    var tempStatus = view.TryScanStructure(tempTight, out var tempCoverage, out var tempDiagnostic);
                    return $"{exactStatus}|{(int)exactCoverage}|{exactUsage.WorkUnits}|{exactUsage.TemporaryBytes}" +
                        $"|{workStatus}|{(int)workCoverage}|{workDiagnostic.Code}|{workDiagnostic.Location.Field}|{workDiagnostic.Location.Offset}|{workDiagnostic.Detail}|{workDiagnostic.Expected}" +
                        $"|{tempStatus}|{(int)tempCoverage}|{tempDiagnostic.Code}|{tempDiagnostic.Location.Field}|{tempDiagnostic.Location.Offset}|{tempDiagnostic.Detail}|{tempDiagnostic.Expected}";
                }
            }
            """);
        Assert.Equal("Done|1|13|40" +
            "|ResourceLimit|0|BudgetExceeded|WorkUnits|2|13|12" +
            "|ResourceLimit|0|BudgetExceeded|TemporaryBytes|4|40|39", result);
    }

    // GD4 (spec 6.2 cache semantics): same-key aliases merge without a second child scan
    // or slot, failed nodes never publish and re-scan identically in a new operation,
    // different windows of one target are different keys, and a cached Partial propagates
    // to the root instead of being washed into DeclaredClosure.
    [Fact]
    public void CacheSemanticsCoverAliasesFailuresWindowsAndPartialMerge()
    {
        string result = SchemaProbeDriver.Execute($$"""
            using System.Collections.Generic;
            using FontWeave.Core;
            using FontWeave.Schema;
            namespace Probe;
            {{ScanLeafSchema}}
            [BinaryTable(0x33333333, 1, ContractVersion = 1, GenerateStructureScan = true)]
            [BinaryScalar("Seed", WireType.UInt8, 0, DataField.Payload)]
            public readonly ref partial struct ByteLeafView { }
            {{V0LeafSchema}}
            [BinaryTable(0x44444444, 2, ContractVersion = 1, GenerateStructureScan = true)]
            [BinaryScalar("At", WireType.UInt16, 0, DataField.Offset, Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table)]
            [BinaryReference("Leaf", "At", typeof(V0LeafView), Origin = ReferenceOrigin.ViewStart, NullPolicy = ReferenceNullPolicy.ZeroIsAddress, Scan = ReferenceScan.OpenOnly)]
            public readonly ref partial struct PartialLeafView { }
            [BinaryTable(0x51414153, 4, ContractVersion = 1, GenerateStructureScan = true)]
            [BinaryScalar("A", WireType.UInt16, 0, DataField.Offset, Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table)]
            [BinaryScalar("B", WireType.UInt16, 2, DataField.Offset, Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table)]
            [BinaryReference("First", "A", typeof(ScanLeafView), Origin = ReferenceOrigin.ViewStart, NullPolicy = ReferenceNullPolicy.Required, Scan = ReferenceScan.Follow)]
            [BinaryReference("Second", "B", typeof(ScanLeafView), Origin = ReferenceOrigin.ViewStart, NullPolicy = ReferenceNullPolicy.Required, Scan = ReferenceScan.Follow)]
            public readonly ref partial struct AliasRootView { }
            [BinaryTable(0x51464149, 4, ContractVersion = 1, GenerateStructureScan = true)]
            [BinaryScalar("A", WireType.UInt16, 0, DataField.Offset, Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table)]
            [BinaryScalar("B", WireType.UInt16, 2, DataField.Offset, Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table)]
            [BinaryReference("First", "A", typeof(ScanLeafView), Origin = ReferenceOrigin.ViewStart, NullPolicy = ReferenceNullPolicy.Required, Scan = ReferenceScan.Follow)]
            [BinaryReference("Second", "B", typeof(ScanLeafView), Origin = ReferenceOrigin.ViewStart, NullPolicy = ReferenceNullPolicy.Required, Scan = ReferenceScan.Follow)]
            public readonly ref partial struct FailRootView { }
            [BinaryTable(0x51474149, 6, ContractVersion = 1, GenerateStructureScan = true)]
            [BinaryScalar("A", WireType.UInt16, 0, DataField.Offset, Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table)]
            [BinaryScalar("B", WireType.UInt16, 2, DataField.Offset, Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table)]
            [BinaryScalar("LenRef", WireType.UInt16, 4, DataField.Length)]
            [BinaryReference("Wide", "A", typeof(ByteLeafView), Origin = ReferenceOrigin.ViewStart, NullPolicy = ReferenceNullPolicy.Required, Scan = ReferenceScan.Follow)]
            [BinaryReference("Narrow", "B", typeof(ByteLeafView), Origin = ReferenceOrigin.ViewStart, NullPolicy = ReferenceNullPolicy.Required, Length = "LenRef", Scan = ReferenceScan.Follow)]
            public readonly ref partial struct WindowRootView { }
            [BinaryTable(0x51504152, 4, ContractVersion = 1, GenerateStructureScan = true)]
            [BinaryScalar("A", WireType.UInt16, 0, DataField.Offset, Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table)]
            [BinaryScalar("B", WireType.UInt16, 2, DataField.Offset, Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table)]
            [BinaryReference("First", "A", typeof(PartialLeafView), Origin = ReferenceOrigin.ViewStart, NullPolicy = ReferenceNullPolicy.Required, Scan = ReferenceScan.Follow)]
            [BinaryReference("Second", "B", typeof(PartialLeafView), Origin = ReferenceOrigin.ViewStart, NullPolicy = ReferenceNullPolicy.Required, Scan = ReferenceScan.Follow)]
            public readonly ref partial struct PartialRootView { }
            public static class Entry
            {
                public static string Run()
                {
                    var results = new List<string>();
                    // The alias wire: A=B=4 share one child window; the Partial wire gives
                    // the child a ZeroIsAddress offset of 0 so its OpenOnly header opens
                    // inside the two-byte child window and marks the profile Partial.
                    byte[] alias = { 0x00, 0x04, 0x00, 0x04, 0xAA, 0xBB };
                    AliasRootView.TryOpen(alias, out var aliasView, out _);
                    var aliasBudget = new BuildBudget(new BuildLimits());
                    var aliasStatus = aliasView.TryScanStructure(aliasBudget, out var aliasCoverage, out _);
                    results.Add($"{aliasStatus}|{(int)aliasCoverage}|{aliasBudget.Usage.WorkUnits}|{aliasBudget.Usage.TemporaryBytes}");
                    var repeatBudget = new BuildBudget(new BuildLimits());
                    var repeatStatus = aliasView.TryScanStructure(repeatBudget, out var repeatCoverage, out _);
                    results.Add($"{repeatStatus}|{(int)repeatCoverage}|{repeatBudget.Usage.WorkUnits}|{repeatBudget.Usage.TemporaryBytes}");
                    byte[] shortWire = { 0x00, 0x04, 0x00, 0x04, 0xAA };
                    FailRootView.TryOpen(shortWire, out var failView, out _);
                    var failBudget = new BuildBudget(new BuildLimits());
                    var failStatus = failView.TryScanStructure(failBudget, out var failCoverage, out _);
                    results.Add($"{failStatus}|{(int)failCoverage}|{failBudget.Usage.WorkUnits}|{failBudget.Usage.TemporaryBytes}");
                    var retryBudget = new BuildBudget(new BuildLimits());
                    var retryStatus = failView.TryScanStructure(retryBudget, out var retryCoverage, out _);
                    results.Add($"{retryStatus}|{(int)retryCoverage}|{retryBudget.Usage.WorkUnits}|{retryBudget.Usage.TemporaryBytes}");
                    byte[] windowWire = { 0x00, 0x04, 0x00, 0x04, 0x00, 0x01 };
                    WindowRootView.TryOpen(windowWire, out var windowView, out _);
                    var windowBudget = new BuildBudget(new BuildLimits());
                    var windowStatus = windowView.TryScanStructure(windowBudget, out var windowCoverage, out _);
                    results.Add($"{windowStatus}|{(int)windowCoverage}|{windowBudget.Usage.WorkUnits}|{windowBudget.Usage.TemporaryBytes}");
                    byte[] partialWire = { 0x00, 0x04, 0x00, 0x04, 0x00, 0x00 };
                    PartialRootView.TryOpen(partialWire, out var partialView, out _);
                    var partialBudget = new BuildBudget(new BuildLimits());
                    var partialStatus = partialView.TryScanStructure(partialBudget, out var partialCoverage, out _);
                    results.Add($"{partialStatus}|{(int)partialCoverage}|{partialBudget.Usage.WorkUnits}|{partialBudget.Usage.TemporaryBytes}");
                    return string.Join(",", results);
                }
            }
            """);
        Assert.Equal("Done|1|13|40,Done|1|13|40," +
            "Truncated|0|8|0,Truncated|0|8|0," +
            "Done|1|18|80," +
            "Done|2|14|40", result);
    }

    // GD5 (spec 6.2 first sentence): the second edge still evaluates its own declared
    // checks even though the first edge already completed a facts slot for the target;
    // the cache is consulted only after the per-edge envelope.
    [Fact]
    public void PerEdgeChecksRunBeforeTheCacheLookup()
    {
        string result = SchemaProbeDriver.Execute($$"""
            using FontWeave.Core;
            using FontWeave.Schema;
            namespace Probe;
            {{ScanLeafSchema}}
            [BinaryTable(0x53000001, 6, ContractVersion = 1, GenerateStructureScan = true)]
            [BinaryScalar("A", WireType.UInt16, 0, DataField.Offset, Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table)]
            [BinaryScalar("B", WireType.UInt16, 2, DataField.StorageOffset, Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table)]
            [BinaryScalar("Floor", WireType.UInt16, 4, DataField.Count)]
            [BinaryReference("First", "A", typeof(ScanLeafView), Origin = ReferenceOrigin.ViewStart, NullPolicy = ReferenceNullPolicy.Required, Scan = ReferenceScan.Follow)]
            [BinaryReference("Second", "B", typeof(ScanLeafView), Origin = ReferenceOrigin.ViewStart, NullPolicy = ReferenceNullPolicy.Required, Minimum = "Floor", Scan = ReferenceScan.Follow)]
            public readonly ref partial struct PerEdgeRootView { }
            public static class Entry
            {
                public static string Run()
                {
                    byte[] wire = { 0x00, 0x06, 0x00, 0x06, 0x00, 0x08, 0xAA, 0xBB };
                    PerEdgeRootView.TryOpen(wire, out var view, out _);
                    var budget = new BuildBudget(new BuildLimits());
                    var status = view.TryScanStructure(budget, out var coverage, out var diagnostic);
                    var usage = budget.Usage;
                    return $"{status}|{(int)coverage}|{diagnostic.Code}|{diagnostic.Location.Field}|{diagnostic.Location.Offset}|{diagnostic.Detail}|{diagnostic.Expected}|{usage.WorkUnits}|{usage.TemporaryBytes}";
                }
            }
            """);
        Assert.Equal("InvalidOffset|0|StorageBeforeRecords|StorageOffset|2|6|8|16|40", result);
    }

    // GD6 (spec 6.1 wrapper contract): the budget overload neither calls Input nor touches
    // Usage.InputBytes, a negative BuildLimits keeps the CLR ArgumentOutOfRangeException,
    // and root input admission stays idempotent.
    [Fact]
    public void WrapperSemanticsSeparateBudgetAndLimitsEntryPoints()
    {
        string result = SchemaProbeDriver.Execute($$"""
            using FontWeave.Core;
            using FontWeave.Schema;
            namespace Probe;
            [BinaryTable(0x57524150, 4, ContractVersion = 1, GenerateStructureScan = true)]
            [BinaryScalar("A", WireType.UInt16, 0, DataField.Offset, Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table)]
            [BinaryScalar("B", WireType.UInt16, 2, DataField.Offset, Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table)]
            [BinaryReference("ToA", "A", typeof(ScanLeafView), Origin = ReferenceOrigin.ViewStart, NullPolicy = ReferenceNullPolicy.Required, Scan = ReferenceScan.Follow)]
            [BinaryReference("ToB", "B", typeof(ScanLeafView), Origin = ReferenceOrigin.ViewStart, NullPolicy = ReferenceNullPolicy.Required, Scan = ReferenceScan.Follow)]
            public readonly ref partial struct WrapperView { }
            {{ScanLeafSchema}}
            public static class Entry
            {
                public static string Run()
                {
                    byte[] wire = { 0x00, 0x04, 0x00, 0x04, 0xAA, 0xBB };
                    WrapperView.TryOpen(wire, out var view, out _);
                    var fresh = new BuildBudget(new BuildLimits());
                    var freshStatus = view.TryScanStructure(fresh, out var freshCoverage, out _);
                    var freshInput = fresh.Usage.InputBytes;
                    var pre = new BuildBudget(new BuildLimits());
                    bool admitted = pre.Input(wire.Length, DataLocation.Anonymous(0x57524150), out _);
                    var preStatus = view.TryScanStructure(pre, out _, out _);
                    var preInput = pre.Usage.InputBytes;
                    string negative;
                    try { _ = new BuildBudget(new BuildLimits { MaxWorkUnits = -1 }); negative = "NoThrow"; }
                    catch (ArgumentOutOfRangeException) { negative = "Threw"; }
                    var limitsStatus = view.TryScanStructure(out var limitsCoverage, out _);
                    return $"{freshStatus}|{freshInput}|{admitted}|{preStatus}|{preInput}|{negative}|{limitsStatus}|{(int)limitsCoverage}";
                }
            }
            """);
        Assert.Equal("Done|0|True|Done|6|Threw|Done|1", result);
    }

    // GD7 (spec 6.1/8.1, OQ-3): the declared Follow edges build the conservative graph —
    // a 65-node chain refuses at build time while 64 still emits, role conditions do not
    // shrink the build graph, cycles are the FWG004 identity termination, shared diamond
    // targets stay legal, and a Follow needs an explicit scan-capable target.
    [Fact]
    public void FollowDagBuildContractsAreEnforced()
    {
        static string ChainTable(int index, string prefix, string referenceTail) => $$"""
            [BinaryTable(0x5C000000 + {{index}}, 2, ContractVersion = 1, GenerateStructureScan = true)]
            [BinaryScalar("Next", WireType.UInt32, 0, DataField.Offset, Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table)]
            [BinaryReference("Chain", "Next", typeof({{prefix}}{{index + 1}}), Origin = ReferenceOrigin.ViewStart, NullPolicy = ReferenceNullPolicy.Required{{referenceTail}})]
            public readonly ref partial struct {{prefix}}{{index}} { }
            """;
        static string LeafTable(int index, string prefix) => $$"""
            [BinaryTable(0x5C000000 + {{index}}, 2, ContractVersion = 1, GenerateStructureScan = true)]
            [BinaryScalar("Seed", WireType.UInt16, 0, DataField.Payload)]
            public readonly ref partial struct {{prefix}}{{index}} { }
            """;
        var deep = new StringBuilder();
        for (int i = 0; i < 64; i++) deep.Append(ChainTable(i, "Deep", ", Scan = ReferenceScan.Follow")).Append('\n');
        deep.Append(LeafTable(64, "Deep")).Append('\n');
        var (deepDriver, _) = SchemaProbeDriver.Generate(SchemaProbeDriver.Tree($$"""
            using FontWeave.Core;
            using FontWeave.Schema;
            namespace Probe;
            {{deep}}
            """, "Deep.cs"));
        var deepDiagnostics = deepDriver.GetRunResult().Diagnostics;
        Assert.Contains(deepDiagnostics, d => d.Id == "FWG005");
        var deepSources = deepDriver.GetRunResult().Results.SelectMany(static r => r.GeneratedSources).ToList();
        Assert.DoesNotContain(deepSources, s => s.HintName == "Probe.Deep0.g.cs");
        Assert.Contains(deepSources, s => s.HintName == "Probe.Deep64.g.cs");

        // A role-conditioned edge still counts (OQ-3: conditions only delete runtime edges).
        var conditional = new StringBuilder();
        conditional.Append("""
            public enum WireKind : byte { Sub = 1, Pos = 2 }
            [BinaryTable(0x5C000000 + 0, 2, ContractVersion = 1, GenerateStructureScan = true, RoleType = typeof(WireKind), SupportedRoles = new long[] { (long)WireKind.Sub, (long)WireKind.Pos })]
            [BinaryScalar("Next", WireType.UInt32, 0, DataField.Offset, Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table)]
            [BinaryReference("Chain", "Next", typeof(Cond1), Origin = ReferenceOrigin.ViewStart, NullPolicy = ReferenceNullPolicy.Required, WhenRoles = new long[] { (long)WireKind.Sub }, Scan = ReferenceScan.Follow)]
            public readonly ref partial struct Cond0 { }
            """).Append('\n');
        for (int i = 1; i < 64; i++) conditional.Append(ChainTable(i, "Cond", ", Scan = ReferenceScan.Follow")).Append('\n');
        conditional.Append(LeafTable(64, "Cond")).Append('\n');
        var (conditionalDriver, _) = SchemaProbeDriver.Generate(SchemaProbeDriver.Tree($$"""
            using FontWeave.Core;
            using FontWeave.Schema;
            namespace Probe;
            {{conditional}}
            """, "Conditional.cs"));
        Assert.Contains(conditionalDriver.GetRunResult().Diagnostics, d => d.Id == "FWG005");

        // A Follow cycle is terminated by the existing FWG004 identity rule (card 1.2-c).
        var (cycleDriver, _) = SchemaProbeDriver.Generate(SchemaProbeDriver.Tree($$"""
            using FontWeave.Core;
            using FontWeave.Schema;
            namespace Probe;
            [BinaryTable(0x43414341, 2, ContractVersion = 1, GenerateStructureScan = true)]
            [BinaryScalar("Next", WireType.UInt16, 0, DataField.Offset, Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table)]
            [BinaryReference("Loop", "Next", typeof(CycB), Origin = ReferenceOrigin.ViewStart, NullPolicy = ReferenceNullPolicy.Required, Scan = ReferenceScan.Follow)]
            public readonly ref partial struct CycA { }
            [BinaryTable(0x43414342, 2, ContractVersion = 1, GenerateStructureScan = true)]
            [BinaryScalar("Next", WireType.UInt16, 0, DataField.Offset, Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table)]
            [BinaryReference("Loop", "Next", typeof(CycA), Origin = ReferenceOrigin.ViewStart, NullPolicy = ReferenceNullPolicy.Required, Scan = ReferenceScan.Follow)]
            public readonly ref partial struct CycB { }
            """, "Cycle.cs"));
        var cycleDiagnostics = cycleDriver.GetRunResult().Diagnostics;
        Assert.Contains(cycleDiagnostics, d => d.Id == "FWG004");
        var cycleSources = cycleDriver.GetRunResult().Results.SelectMany(static r => r.GeneratedSources).ToList();
        Assert.DoesNotContain(cycleSources, s => s.HintName == "Probe.CycA.g.cs");
        Assert.DoesNotContain(cycleSources, s => s.HintName == "Probe.CycB.g.cs");

        // A shared diamond target is a legal DAG, not a cycle.
        var (diamondDriver, _) = SchemaProbeDriver.Generate(SchemaProbeDriver.Tree($$"""
            using FontWeave.Core;
            using FontWeave.Schema;
            namespace Probe;
            {{ScanLeafSchema}}
            [BinaryTable(0x44494131, 2, ContractVersion = 1, GenerateStructureScan = true)]
            [BinaryScalar("Next", WireType.UInt16, 0, DataField.Offset, Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table)]
            [BinaryReference("ToLeaf", "Next", typeof(ScanLeafView), Origin = ReferenceOrigin.ViewStart, NullPolicy = ReferenceNullPolicy.Required, Scan = ReferenceScan.Follow)]
            public readonly ref partial struct Dia1 { }
            [BinaryTable(0x44494132, 2, ContractVersion = 1, GenerateStructureScan = true)]
            [BinaryScalar("Next", WireType.UInt16, 0, DataField.Offset, Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table)]
            [BinaryReference("ToLeaf", "Next", typeof(ScanLeafView), Origin = ReferenceOrigin.ViewStart, NullPolicy = ReferenceNullPolicy.Required, Scan = ReferenceScan.Follow)]
            public readonly ref partial struct Dia2 { }
            [BinaryTable(0x44494130, 4, ContractVersion = 1, GenerateStructureScan = true)]
            [BinaryScalar("One", WireType.UInt16, 0, DataField.Offset, Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table)]
            [BinaryScalar("Two", WireType.UInt16, 2, DataField.Offset, Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table)]
            [BinaryReference("Left", "One", typeof(Dia1), Origin = ReferenceOrigin.ViewStart, NullPolicy = ReferenceNullPolicy.Required, Scan = ReferenceScan.Follow)]
            [BinaryReference("Right", "Two", typeof(Dia2), Origin = ReferenceOrigin.ViewStart, NullPolicy = ReferenceNullPolicy.Required, Scan = ReferenceScan.Follow)]
            public readonly ref partial struct Dia0 { }
            """, "Diamond.cs"));
        Assert.DoesNotContain(diamondDriver.GetRunResult().Diagnostics, d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
        Assert.Contains(diamondDriver.GetRunResult().Results.SelectMany(static r => r.GeneratedSources),
            s => s.HintName == "Probe.Dia0.g.cs");

        // A V0 target and a targetless slot both refuse the automatic Follow (card 1.2-c).
        var (targetDriver, _) = SchemaProbeDriver.Generate(SchemaProbeDriver.Tree($$"""
            using FontWeave.Core;
            using FontWeave.Schema;
            namespace Probe;
            {{V0LeafSchema}}
            [BinaryTable(0x53524330, 4, ContractVersion = 1, GenerateStructureScan = true)]
            [BinaryScalar("At", WireType.UInt16, 0, DataField.Offset, Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table)]
            [BinaryReference("Leaf", "At", typeof(V0LeafView), Origin = ReferenceOrigin.ViewStart, NullPolicy = ReferenceNullPolicy.Required, Scan = ReferenceScan.Follow)]
            public readonly ref partial struct V0FollowView { }
            [BinaryTable(0x54000001, 6, ContractVersion = 1, GenerateStructureScan = true)]
            [BinaryScalar("SetCount", WireType.UInt16, 2, DataField.Count, Semantic = NumberSemantic.Count)]
            [BinaryArray("Row", "SetCount", 6, 2)]
            [BinaryElement("Row", "SetAt", WireType.UInt16, 0, DataField.Offset, Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = false)]
            [BinaryReference("NoTarget", "SetAt", Array = "Row", Origin = ReferenceOrigin.ViewStart, NullPolicy = ReferenceNullPolicy.Required, Scan = ReferenceScan.Follow)]
            public readonly ref partial struct TargetlessView { }
            """, "Targets.cs"));
        var targetDiagnostics = targetDriver.GetRunResult().Diagnostics;
        Assert.Contains(targetDiagnostics, d => d.Id == "FWG005");
        Assert.DoesNotContain(targetDiagnostics, d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error && d.Id == "FWG004");
        var targetSources = targetDriver.GetRunResult().Results.SelectMany(static r => r.GeneratedSources).ToList();
        Assert.DoesNotContain(targetSources, s => s.HintName == "Probe.V0FollowView.g.cs");
        Assert.DoesNotContain(targetSources, s => s.HintName == "Probe.TargetlessView.g.cs");
    }

    // GD8 (card 1.1 ①③④, spec 6.1): GenerateStructureScan is V1-only, a scanned table
    // requires an explicit policy on every reference, and every declared Offset field must
    // be covered by a reference slot, a payload slot or a payload storage anchor.
    [Fact]
    public void ScanDeclarationContractsAreBuildTimeErrors()
    {
        var (driver, _) = SchemaProbeDriver.Generate(SchemaProbeDriver.Tree($$"""
            using FontWeave.Core;
            using FontWeave.Schema;
            namespace Probe;
            {{ScanLeafSchema}}
            [BinaryTable(0x56000001, 2, GenerateStructureScan = true)]
            [BinaryScalar("Value", WireType.UInt16, 0, DataField.Payload)]
            public readonly ref partial struct V0ScanView { }
            [BinaryTable(0x56000002, 4, ContractVersion = 1, GenerateStructureScan = true)]
            [BinaryScalar("At", WireType.UInt16, 0, DataField.Offset, Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table)]
            [BinaryReference("Leaf", "At", typeof(ScanLeafView), Origin = ReferenceOrigin.ViewStart, NullPolicy = ReferenceNullPolicy.Required)]
            public readonly ref partial struct UnspecifiedView { }
            [BinaryTable(0x56000003, 2, ContractVersion = 1, GenerateStructureScan = true)]
            [BinaryScalar("Dangle", WireType.UInt16, 0, DataField.Offset, Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table)]
            public readonly ref partial struct DanglingView { }
            """, "Declarations.cs"));
        var diagnostics = driver.GetRunResult().Diagnostics;
        Assert.Contains(diagnostics, d => d.Id == "FWG001" && d.Location.IsInSource);
        Assert.Contains(diagnostics, d => d.Id == "FWG005" && d.Location.IsInSource);
        var sources = driver.GetRunResult().Results.SelectMany(static r => r.GeneratedSources).ToList();
        Assert.DoesNotContain(sources, s => s.HintName == "Probe.V0ScanView.g.cs");
        Assert.DoesNotContain(sources, s => s.HintName == "Probe.UnspecifiedView.g.cs");
        Assert.DoesNotContain(sources, s => s.HintName == "Probe.DanglingView.g.cs");
    }

    // GD8 positive counterpart: the three coverage forms (reference slot, payload slot,
    // payload storage anchor) compile, and the V1 payload table carries the spec 5.1
    // location overload beside the unchanged legacy overload.
    [Fact]
    public void ScanCoverageFormsCompileAndPayloadOverloadCarriesLocation()
    {
        string result = SchemaProbeDriver.Execute($$"""
            using FontWeave.Core;
            using FontWeave.Schema;
            namespace Probe;
            [BinaryTable(0x5041524B, 4, ContractVersion = 1, GenerateStructureScan = true)]
            [BinaryScalar("Storage", WireType.UInt16, 0, DataField.StorageOffset, Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table)]
            [BinaryScalar("Count", WireType.UInt16, 2, DataField.Count, Semantic = NumberSemantic.Count)]
            [BinaryArray("Item", "Count", 4, 4)]
            [BinaryElement("Item", "At", WireType.UInt16, 0, DataField.Offset, Semantic = NumberSemantic.Offset, Origin = WireOrigin.NameStorage)]
            [BinaryElement("Item", "Len", WireType.UInt16, 2, DataField.Length, Semantic = NumberSemantic.Count)]
            [BinaryPayload("Item", "At", "Len", "Storage", "Storage")]
            public readonly ref partial struct PayloadScanView { }
            public static class Entry
            {
                public static string Run()
                {
                    // Storage anchors the payload area at 8; record 0 holds At=0, Len=2,
                    // so the envelope is [8,10) inside the ten-byte window.
                    byte[] wire = { 0x00, 0x08, 0x00, 0x01, 0x00, 0x00, 0x00, 0x02, 0x11, 0x22 };
                    PayloadScanView.TryOpen(wire, out var view, out _);
                    var budget = new BuildBudget(new BuildLimits());
                    var status = view.TryScanStructure(budget, out var coverage, out _);
                    var usage = budget.Usage;
                    var located = view.TryGetItemPayload(0, out var locatedPayload, out var payloadLocation, out var locatedDiagnostic);
                    var legacy = view.TryGetItemPayload(0, out var legacyPayload, out var legacyDiagnostic);
                    return $"{status}|{(int)coverage}|{usage.WorkUnits}|{usage.TemporaryBytes}" +
                        $"|{located}|{Convert.ToHexString(locatedPayload)}|{legacy}|{Convert.ToHexString(legacyPayload)}|{legacyDiagnostic.Code}" +
                        $"|{locatedDiagnostic.Code}|{payloadLocation.Field}|{payloadLocation.Offset}|{payloadLocation.RecordOrdinal}";
                }
            }
            """);
        Assert.Equal("Done|1|14|0|Done|1122|Done|1122|None|None|Payload|8|0", result);
    }

    // GD9 (spec 6.1 coverage column, card 1.2-e): OpenOnly/Observe present mark Partial
    // with the edge charged, Manual marks Partial with no edge fee at all, and a legal
    // absent reference marks nothing; roots and leaves never form slots (Temp stays 0).
    [Theory]
    [InlineData("ReferenceScan.OpenOnly", "Required", "4", 2, 7)]
    [InlineData("ReferenceScan.Observe", "Required", "4", 2, 7)]
    [InlineData("ReferenceScan.Manual", "Required", "4", 2, 6)]
    [InlineData("ReferenceScan.Observe", "Optional", "0", 1, 7)]
    public void CoveragePoliciesFollowTheSpecifiedSemantics(string scan, string nullPolicy, string offset, int coverage, int work)
    {
        string result = SchemaProbeDriver.Execute($$"""
            using FontWeave.Core;
            using FontWeave.Schema;
            namespace Probe;
            {{V0LeafSchema}}
            [BinaryTable(0x59000001, 4, ContractVersion = 1, GenerateStructureScan = true)]
            [BinaryScalar("At", WireType.UInt16, 0, DataField.Offset, Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table)]
            [BinaryReference("Leaf", "At", typeof(V0LeafView), Origin = ReferenceOrigin.ViewStart, NullPolicy = ReferenceNullPolicy.{{nullPolicy}}, Scan = {{scan}})]
            public readonly ref partial struct PolicyView { }
            public static class Entry
            {
                public static string Run()
                {
                    byte[] wire = { 0x00, {{offset}}, 0xAA, 0xBB, 0xCC, 0xDD };
                    PolicyView.TryOpen(wire, out var view, out _);
                    var budget = new BuildBudget(new BuildLimits());
                    var status = view.TryScanStructure(budget, out var scanCoverage, out _);
                    var usage = budget.Usage;
                    return $"{status}|{(int)scanCoverage}|{usage.WorkUnits}|{usage.TemporaryBytes}";
                }
            }
            """);
        Assert.Equal($"Done|{coverage}|{work}|0", result);
    }

    // GD10 (spec 8.2, card 1.2-h): the scan declaration joins the canonical identity —
    // policy/enable changes re-emit, comments stay cached, and an invalid declaration
    // round-trips back to the original emission.
    [Fact]
    public void ScanIdentityChangesReemitAndRoundTrip()
    {
        var leaf = SchemaProbeDriver.Tree(ScanLeafSource, "ScanLeaf.cs");
        const string scanOff = """
            using FontWeave.Core;
            using FontWeave.Schema;
            namespace Probe;
            [BinaryTable(0x74657374, 4, ContractVersion = 1)]
            [BinaryScalar("At", WireType.UInt16, 0, DataField.Offset, Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table)]
            [BinaryReference("Leaf", "At", typeof(ScanLeafView), Origin = ReferenceOrigin.ViewStart, NullPolicy = ReferenceNullPolicy.Required, Scan = ReferenceScan.Observe)]
            public readonly ref partial struct ScanIdView { }
            """;
        const string scanOn = """
            using FontWeave.Core;
            using FontWeave.Schema;
            namespace Probe;
            [BinaryTable(0x74657374, 4, ContractVersion = 1, GenerateStructureScan = true)]
            [BinaryScalar("At", WireType.UInt16, 0, DataField.Offset, Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table)]
            [BinaryReference("Leaf", "At", typeof(ScanLeafView), Origin = ReferenceOrigin.ViewStart, NullPolicy = ReferenceNullPolicy.Required, Scan = ReferenceScan.Follow)]
            public readonly ref partial struct ScanIdView { }
            """;
        const string commented = """
            using FontWeave.Core;
            using FontWeave.Schema;
            namespace Probe;
            // provenance comment only; the declared contract is unchanged.
            [BinaryTable(0x74657374, 4, ContractVersion = 1, GenerateStructureScan = true)]
            [BinaryScalar("At", WireType.UInt16, 0, DataField.Offset, Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table)]
            [BinaryReference("Leaf", "At", typeof(ScanLeafView), Origin = ReferenceOrigin.ViewStart, NullPolicy = ReferenceNullPolicy.Required, Scan = ReferenceScan.Follow)]
            public readonly ref partial struct ScanIdView { }
            """;
        const string followWithoutScan = """
            using FontWeave.Core;
            using FontWeave.Schema;
            namespace Probe;
            [BinaryTable(0x74657374, 4, ContractVersion = 1)]
            [BinaryScalar("At", WireType.UInt16, 0, DataField.Offset, Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table)]
            [BinaryReference("Leaf", "At", typeof(ScanLeafView), Origin = ReferenceOrigin.ViewStart, NullPolicy = ReferenceNullPolicy.Required, Scan = ReferenceScan.Follow)]
            public readonly ref partial struct ScanIdView { }
            """;
        var one = SchemaProbeDriver.Tree(scanOff, "ScanId.cs");
        var input = SchemaProbeDriver.NewInput(leaf, one);
        var (initialDriver, _) = SchemaProbeDriver.GenerateInternal(SchemaProbeDriver.Driver(), input);
        Assert.Contains(initialDriver.GetRunResult().Results.SelectMany(static r => r.GeneratedSources),
            static s => s.HintName == "Probe.ScanIdView.g.cs");

        // Enabling the scan and switching the policy to Follow re-emits the referrer.
        var (enabledDriver, _) = SchemaProbeDriver.GenerateInternal(initialDriver,
            input.ReplaceSyntaxTree(one, SchemaProbeDriver.Tree(scanOn, "ScanId.cs")));
        Assert.Equal(["Modified"], EmittedReasons(enabledDriver, "Probe.ScanIdView.g.cs"));

        // A comment on the declaration is not a contract change.
        var (commentedDriver, _) = SchemaProbeDriver.GenerateInternal(enabledDriver,
            input.ReplaceSyntaxTree(one, SchemaProbeDriver.Tree(commented, "ScanId.cs")));
        Assert.All(EmittedReasons(commentedDriver, "Probe.ScanIdView.g.cs"),
            static r => Assert.Equal("Cached", r));

        // Follow without an enabled scan is invalid; restoring the declaration recovers.
        var (brokenDriver, _) = SchemaProbeDriver.GenerateInternal(commentedDriver,
            input.ReplaceSyntaxTree(one, SchemaProbeDriver.Tree(followWithoutScan, "ScanId.cs")));
        Assert.Contains(brokenDriver.GetRunResult().Diagnostics, static d => d.Id == "FWG005");
        Assert.DoesNotContain(brokenDriver.GetRunResult().Results.SelectMany(static r => r.GeneratedSources),
            static s => s.HintName == "Probe.ScanIdView.g.cs");
        var (recoveredDriver, _) = SchemaProbeDriver.GenerateInternal(brokenDriver,
            input.ReplaceSyntaxTree(one, SchemaProbeDriver.Tree(scanOn, "ScanId.cs")));
        Assert.DoesNotContain(recoveredDriver.GetRunResult().Diagnostics, static d => d.Severity == DiagnosticSeverity.Error);
        Assert.Contains(recoveredDriver.GetRunResult().Results.SelectMany(static r => r.GeneratedSources),
            static s => s.HintName == "Probe.ScanIdView.g.cs");
    }

    // GD11 (spec 6.1 role form + card 1.2-b, OQ-2 ruling): a RoleType view's scan family
    // leads with the concrete RoleEnum and an unknown role refuses at None@0 before any
    // budget traffic; a slot with two selector branches selects the first matching
    // declaration, runs exactly that one branch and charges the occurrence once.
    [Fact]
    public void RoleScanSignatureLeadsWithTheEnumAndSlotsSelectOneBranch()
    {
        string result = SchemaProbeDriver.Execute($$"""
            using FontWeave.Core;
            using FontWeave.Schema;
            namespace Probe;
            public enum WireKind : byte { Sub = 1, Pos = 2 }
            {{ScanLeafSchema}}
            {{V0LeafSchema}}
            [BinaryTable(0x524F4C53, 4, ContractVersion = 1, GenerateStructureScan = true,
                RoleType = typeof(WireKind), SupportedRoles = new long[] { (long)WireKind.Sub, (long)WireKind.Pos })]
            [BinaryScalar("Kind", WireType.UInt16, 0, DataField.Payload)]
            [BinaryScalar("At", WireType.UInt16, 2, DataField.Offset, Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table)]
            [BinaryReference("FollowBranch", "At", typeof(ScanLeafView), Origin = ReferenceOrigin.ViewStart, NullPolicy = ReferenceNullPolicy.Required, Selector = "Kind", SelectorValues = new uint[] { 1 }, Scan = ReferenceScan.Follow)]
            [BinaryReference("ObserveBranch", "At", typeof(V0LeafView), Origin = ReferenceOrigin.ViewStart, NullPolicy = ReferenceNullPolicy.Required, Selector = "Kind", SelectorValues = new uint[] { 2 }, Scan = ReferenceScan.Observe)]
            public readonly ref partial struct RoleSlotView { }
            public static class Entry
            {
                public static string Run()
                {
                    byte[] followWire = { 0x00, 0x01, 0x00, 0x04, 0xAA, 0xBB };
                    RoleSlotView.TryOpen(followWire, out var followView, out _);
                    var followBudget = new BuildBudget(new BuildLimits());
                    var followStatus = followView.TryScanStructure((WireKind)1, followBudget, out var followCoverage, out _);
                    var followUsage = followBudget.Usage;
                    byte[] observeWire = { 0x00, 0x02, 0x00, 0x04, 0xAA, 0xBB };
                    RoleSlotView.TryOpen(observeWire, out var observeView, out _);
                    var observeBudget = new BuildBudget(new BuildLimits());
                    var observeStatus = observeView.TryScanStructure((WireKind)1, observeBudget, out var observeCoverage, out _);
                    var observeUsage = observeBudget.Usage;
                    var unknown = new BuildBudget(new BuildLimits());
                    var unknownStatus = observeView.TryScanStructure((WireKind)3, unknown, out var unknownCoverage, out var unknownDiagnostic);
                    var unknownUsage = unknown.Usage;
                    return $"{followStatus}|{(int)followCoverage}|{followUsage.WorkUnits}|{followUsage.TemporaryBytes}" +
                        $"|{observeStatus}|{(int)observeCoverage}|{observeUsage.WorkUnits}|{observeUsage.TemporaryBytes}" +
                        $"|{unknownStatus}|{(int)unknownCoverage}|{unknownDiagnostic.Code}|{unknownDiagnostic.Location.Field}|{unknownDiagnostic.Location.Offset}|{unknownDiagnostic.Detail}|{unknownDiagnostic.Expected}|{unknownUsage.WorkUnits}|{unknownUsage.InputBytes}";
                }
            }
            """);
        Assert.Equal("Done|1|12|40" +
            "|Done|2|8|0" +
            "|UnsupportedFormat|0|UnsupportedOperation|None|0|3|0|0|0", result);
    }

    // Spec 3.1 on the scan edge: a Follow window's Length may name a derived or
    // computed value (the same member resolution the typed opener path uses), the
    // core cuts the envelope with the evaluated value, and an overlong window is
    // refused at the value's declared source (derived: the raw count scalar;
    // computed: the declared DiagnosticField/DiagnosticSource position).
    [Fact]
    public void ScanWindowLengthFollowsDerivedAndComputedValues()
    {
        string result = SchemaProbeDriver.Execute($$"""
            using System.Collections.Generic;
            using FontWeave.Core;
            using FontWeave.Schema;
            namespace Probe;
            {{ScanLeafSchema}}
            [BinaryTable(0x57564431, 4, ContractVersion = 1, GenerateStructureScan = true)]
            [BinaryScalar("At", WireType.UInt16, 0, DataField.Offset, Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table)]
            [BinaryScalar("Span", WireType.UInt16, 2, DataField.Length)]
            [BinaryDerived("Window", Constant = 1, Count = "Span")]
            [BinaryReference("Piece", "At", typeof(ScanLeafView), Origin = ReferenceOrigin.ViewStart, NullPolicy = ReferenceNullPolicy.Required, Length = "Window", Scan = ReferenceScan.Follow)]
            public readonly ref partial struct DerivedWindowRootView { }
            [BinaryTable(0x57564432, 4, ContractVersion = 1, GenerateStructureScan = true)]
            [BinaryScalar("At", WireType.UInt16, 0, DataField.Offset, Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table)]
            [BinaryScalar("Span", WireType.UInt16, 2, DataField.Length)]
            [BinaryComputed("Hooked", "EvalWindow", DiagnosticField = DataField.Length, DiagnosticSource = "Span", Maximum = 1024ul)]
            [BinaryReference("Piece", "At", typeof(ScanLeafView), Origin = ReferenceOrigin.ViewStart, NullPolicy = ReferenceNullPolicy.Required, Length = "Hooked", Scan = ReferenceScan.Follow)]
            public readonly ref partial struct ComputedWindowRootView
            {
                public static ulong HookValue;
                private static ReadStatus EvalWindow(DataLocation diagnosticLocation, out ulong value, out FontDiagnostic diagnostic)
                { value = HookValue; diagnostic = default; return ReadStatus.Done; }
            }
            public static class Entry
            {
                public static string Run()
                {
                    var results = new List<string>();
                    // At=6 puts the leaf at [6,10); the evaluated window is the only
                    // thing deciding whether the envelope reaches to the wire end.
                    byte[] wire = { 0, 6, 0, 3, 0, 0, 0xAA, 0xBB, 0xCC, 0xDD };
                    DerivedWindowRootView.TryOpen(wire, out var derivedView, out _);
                    var fit = new BuildBudget(new BuildLimits());
                    var fitStatus = derivedView.TryScanStructure(fit, out var fitCoverage, out _);
                    var fitUsage = fit.Usage;
                    results.Add($"{fitStatus}|{(int)fitCoverage}|{fitUsage.WorkUnits}|{fitUsage.TemporaryBytes}");
                    byte[] overlong = { 0, 6, 0, 5, 0, 0, 0xAA, 0xBB, 0xCC, 0xDD };
                    DerivedWindowRootView.TryOpen(overlong, out var overView, out _);
                    var over = new BuildBudget(new BuildLimits());
                    var overStatus = overView.TryScanStructure(over, out var overCoverage, out var overDiagnostic);
                    results.Add($"{overStatus}|{(int)overCoverage}|{overDiagnostic.Code}|{overDiagnostic.Location.Field}|{overDiagnostic.Location.Offset}|{overDiagnostic.Detail}|{overDiagnostic.Expected}");
                    ComputedWindowRootView.TryOpen(wire, out var computedView, out _);
                    ComputedWindowRootView.HookValue = 4;
                    var hooked = new BuildBudget(new BuildLimits());
                    var hookedStatus = computedView.TryScanStructure(hooked, out var hookedCoverage, out _);
                    var hookedUsage = hooked.Usage;
                    results.Add($"{hookedStatus}|{(int)hookedCoverage}|{hookedUsage.WorkUnits}|{hookedUsage.TemporaryBytes}");
                    ComputedWindowRootView.HookValue = 9;
                    var hookedOver = new BuildBudget(new BuildLimits());
                    var hookedOverStatus = computedView.TryScanStructure(hookedOver, out var hookedOverCoverage, out var hookedOverDiagnostic);
                    results.Add($"{hookedOverStatus}|{(int)hookedOverCoverage}|{hookedOverDiagnostic.Code}|{hookedOverDiagnostic.Location.Field}|{hookedOverDiagnostic.Location.Offset}|{hookedOverDiagnostic.Detail}|{hookedOverDiagnostic.Expected}");
                    return string.Join(",", results);
                }
            }
            """);
        Assert.Equal("Done|1|13|40," +
            "InvalidOffset|0|RangeOutsideSource|Length|2|6|4," +
            "Done|1|13|40," +
            "InvalidOffset|0|RangeOutsideSource|Length|2|9|4", result);
    }

    private static string[] EmittedReasons(GeneratorDriver driver, string hintName) =>
        driver.GetRunResult().Results.Single().TrackedSteps["EmittedTables"]
            .SelectMany(static s => s.Outputs)
            .Select(static o => (Output: ((string, string, string?))o.Value, Reason: o.Reason))
            .Where(o => o.Output.Item1 == hintName)
            .Select(static o => $"{o.Reason}").ToArray();
}
