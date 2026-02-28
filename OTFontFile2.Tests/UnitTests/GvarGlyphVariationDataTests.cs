using Microsoft.VisualStudio.TestTools.UnitTesting;
using OTFontFile2.Tables;

namespace OTFontFile2.Tests.UnitTests;

[TestClass]
public sealed class GvarGlyphVariationDataTests
{
    [TestMethod]
    public void GvarGlyphVariationData_BuildAndParse_WithSharedPoints_RoundTrips()
    {
        var gv = new GvarTableBuilder.GlyphVariationData(axisCount: 1, pointCountWithPhantoms: 6);
        gv.SetSharedPointNumbers(new ushort[] { 1, 4 });

        gv.AddTupleVariation(new GvarTableBuilder.TupleVariation(
            peakTupleRaw: new short[] { 0 },
            intermediateStartRaw: null,
            intermediateEndRaw: null,
            selectionKind: GvarTableBuilder.PointSelectionKind.Shared,
            privatePointNumbers: Array.Empty<ushort>(),
            xDeltas: new short[] { 10, -3 },
            yDeltas: new short[] { 0, 7 }));

        byte[] record = gv.BuildGlyphVariationDataRecord();

        Assert.IsTrue(GvarTableBuilder.GlyphVariationData.TryParse(axisCount: 1, pointCountWithPhantoms: 6, record, out var parsed));
        Assert.IsTrue(parsed.HasSharedPointNumbers);
        CollectionAssert.AreEqual(new ushort[] { 1, 4 }, parsed.SharedPointNumbers);

        Assert.AreEqual(1, parsed.TupleVariationCount);
        Assert.IsTrue(parsed.TryGetTupleVariation(0, out var tv0));
        Assert.AreEqual(GvarTableBuilder.PointSelectionKind.Shared, tv0.SelectionKind);
        CollectionAssert.AreEqual(new short[] { 10, -3 }, tv0.XDeltas);
        CollectionAssert.AreEqual(new short[] { 0, 7 }, tv0.YDeltas);
    }

    [TestMethod]
    public void GvarGlyphVariationData_BuildAndParse_AllPoints_WithNonEmptySharedPoints_UsesPrivateAllPointsSemantics()
    {
        var gv = new GvarTableBuilder.GlyphVariationData(axisCount: 1, pointCountWithPhantoms: 6);
        gv.SetSharedPointNumbers(new ushort[] { 0, 2 });

        gv.AddTupleVariation(new GvarTableBuilder.TupleVariation(
            peakTupleRaw: new short[] { 0 },
            intermediateStartRaw: null,
            intermediateEndRaw: null,
            selectionKind: GvarTableBuilder.PointSelectionKind.AllPoints,
            privatePointNumbers: Array.Empty<ushort>(),
            xDeltas: new short[] { 1, 2, 3, 4, 5, 6 },
            yDeltas: new short[] { -1, -2, -3, -4, -5, -6 }));

        byte[] record = gv.BuildGlyphVariationDataRecord();

        Assert.IsTrue(GvarTableBuilder.GlyphVariationData.TryParse(axisCount: 1, pointCountWithPhantoms: 6, record, out var parsed));
        Assert.IsTrue(parsed.HasSharedPointNumbers);
        CollectionAssert.AreEqual(new ushort[] { 0, 2 }, parsed.SharedPointNumbers);

        Assert.AreEqual(1, parsed.TupleVariationCount);
        Assert.IsTrue(parsed.TryGetTupleVariation(0, out var tv0));
        Assert.AreEqual(GvarTableBuilder.PointSelectionKind.AllPoints, tv0.SelectionKind);
        Assert.AreEqual(0, tv0.PrivatePointNumbers.Length);
        CollectionAssert.AreEqual(new short[] { 1, 2, 3, 4, 5, 6 }, tv0.XDeltas);
        CollectionAssert.AreEqual(new short[] { -1, -2, -3, -4, -5, -6 }, tv0.YDeltas);
    }
}

