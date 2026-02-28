using Microsoft.VisualStudio.TestTools.UnitTesting;
using OTFontFile2.Tables;
using System.Buffers.Binary;

namespace OTFontFile2.Tests.UnitTests;

[TestClass]
public sealed class CvarStructuredWritebackTests
{
    [TestMethod]
    public void FontModel_CanImportStructuredCvar_EditAndWriteBack()
    {
        byte[] head = TestSfntTables.BuildValidHeadTable(unitsPerEm: 1000);

        var fvar = new FvarTableBuilder();
        fvar.AddAxis(new Tag(0x77676874u), minValue: new Fixed1616(0), defaultValue: new Fixed1616(0), maxValue: new Fixed1616(0), flags: 0, axisNameId: 256); // 'wght'
        fvar.AddAxis(new Tag(0x77647468u), minValue: new Fixed1616(0), defaultValue: new Fixed1616(0), maxValue: new Fixed1616(0), flags: 0, axisNameId: 257); // 'wdth'

        var cvt = new CvtTableBuilder();
        for (int i = 0; i < 10; i++)
            cvt.AddValue((short)i);

        byte[] cvarBytes = BuildSyntheticCvar(axisCount: 2);

        var sfnt = new SfntBuilder { SfntVersion = 0x00010000u };
        sfnt.SetTable(KnownTags.head, head);
        sfnt.SetTable(fvar);
        sfnt.SetTable(cvt);
        sfnt.SetTable(KnownTags.cvar, cvarBytes);

        using var file = SfntFile.FromMemory(sfnt.ToArray());
        var font = file.GetFont(0);

        var model = new FontModel(font);
        Assert.IsTrue(model.TryEdit<CvarTableBuilder>(out var edit));
        Assert.IsTrue(edit.IsStructured);
        Assert.AreEqual(2, edit.AxisCount);
        Assert.AreEqual(10, edit.CvtCount);
        Assert.AreEqual(1, edit.TupleVariationCount);

        Assert.IsTrue(edit.TryGetTupleVariation(0, out var v0));
        Assert.AreEqual(CvarTableBuilder.PointSelectionKind.Private, v0.SelectionKind);
        CollectionAssert.AreEqual(new ushort[] { 1, 6, 7 }, v0.PrivatePointNumbers);
        CollectionAssert.AreEqual(new short[] { 28, 123, 4 }, v0.Deltas);

        var newDeltas = (short[])v0.Deltas.Clone();
        newDeltas[1] = -7;

        var replacement = new CvarTableBuilder.CvarTupleVariation(
            peakTupleRaw: v0.PeakTupleRaw,
            intermediateStartRaw: v0.IntermediateStartRaw,
            intermediateEndRaw: v0.IntermediateEndRaw,
            selectionKind: v0.SelectionKind,
            privatePointNumbers: v0.PrivatePointNumbers,
            deltas: newDeltas);

        edit.ReplaceTupleVariation(0, replacement);

        byte[] editedBytes = model.ToArray();
        Assert.AreEqual(0xB1B0AFBAu, OpenTypeChecksum.Compute(editedBytes));

        using var editedFile = SfntFile.FromMemory(editedBytes);
        var editedFont = editedFile.GetFont(0);

        Assert.IsTrue(editedFont.TryGetCvar(out var editedCvar));
        Assert.IsTrue(editedCvar.TryGetTupleVariationStore(axisCount: 2, out var store));
        Assert.AreEqual((ushort)1, store.TupleVariationCount);

        Assert.IsTrue(CvarTableBuilder.TryFrom(editedFont, out var imported));
        Assert.IsTrue(imported.IsStructured);
        Assert.IsTrue(imported.TryGetTupleVariation(0, out var importedV0));
        CollectionAssert.AreEqual(new short[] { 28, -7, 4 }, importedV0.Deltas);
    }

    private static byte[] BuildSyntheticCvar(ushort axisCount)
    {
        // version(4) + tupleVariationCount(2) + offsetToData(2)
        // + tupleVariationHeader(4) + peakTuple(axisCount*2)
        // + variationData:
        //   packed point numbers for [1, 6, 7]
        //   packed deltas for [28, 123, 4]

        if (axisCount != 2)
            throw new ArgumentOutOfRangeException(nameof(axisCount));

        byte[] table = new byte[25];
        var span = table.AsSpan();

        BinaryPrimitives.WriteUInt32BigEndian(span.Slice(0, 4), 0x00010000u);
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(4, 2), 1);
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(6, 2), 16);

        // TupleVariationHeader @ 8
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(8, 2), 9);
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(10, 2), 0xA000); // embedded peak tuple + private points

        // Peak tuple for 2 axes: [0, -1]
        BinaryPrimitives.WriteInt16BigEndian(span.Slice(12, 2), 0);
        BinaryPrimitives.WriteInt16BigEndian(span.Slice(14, 2), unchecked((short)0xC000));

        // Variation data @ 16
        span[16] = 0x03; // pointCount=3
        span[17] = 0x02; // byte run, length=3
        span[18] = 0x01; // +1 => 1
        span[19] = 0x05; // +5 => 6
        span[20] = 0x01; // +1 => 7

        span[21] = 0x02; // byte deltas, length=3
        span[22] = 0x1C; // 28
        span[23] = 0x7B; // 123
        span[24] = 0x04; // 4

        return table;
    }
}
