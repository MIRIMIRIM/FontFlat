using Microsoft.VisualStudio.TestTools.UnitTesting;
using OTFontFile2.Tables;

namespace OTFontFile2.Tests.UnitTests;

[TestClass]
public sealed class DeviceTableBuilderTests
{
    [TestMethod]
    public void DeviceTableBuilder_CanBuildDeltaFormat1_AndParseViaDeviceTable()
    {
        var builder = new DeviceTableBuilder();
        builder.SetDeltas(
            startSize: 9,
            endSize: 16,
            deltas: new sbyte[] { -2, -1, 0, 1, -2, -1, 0, 1 });

        byte[] deviceBytes = builder.ToArray();

        Assert.IsTrue(Tag.TryParse("TEST", out var testTag));

        var sfnt = new SfntBuilder { SfntVersion = 0x00010000u };
        sfnt.SetTable(KnownTags.head, TestSfntTables.BuildValidHeadTable(unitsPerEm: 1000));
        sfnt.SetTable(testTag, deviceBytes);

        using var file = SfntFile.FromMemory(sfnt.ToArray());
        var font = file.GetFont(0);

        Assert.IsTrue(font.TryGetTableSlice(testTag, out var slice));
        Assert.IsTrue(DeviceTable.TryCreate(slice, offset: 0, out var device));
        Assert.AreEqual((ushort)1, device.DeltaFormat);
        Assert.AreEqual((ushort)9, device.StartSize);
        Assert.AreEqual((ushort)16, device.EndSize);

        Assert.IsTrue(device.TryGetDelta(ppemSize: 9, out sbyte d9));
        Assert.IsTrue(device.TryGetDelta(ppemSize: 10, out sbyte d10));
        Assert.IsTrue(device.TryGetDelta(ppemSize: 11, out sbyte d11));
        Assert.IsTrue(device.TryGetDelta(ppemSize: 12, out sbyte d12));
        Assert.IsTrue(device.TryGetDelta(ppemSize: 13, out sbyte d13));
        Assert.IsTrue(device.TryGetDelta(ppemSize: 14, out sbyte d14));
        Assert.IsTrue(device.TryGetDelta(ppemSize: 15, out sbyte d15));
        Assert.IsTrue(device.TryGetDelta(ppemSize: 16, out sbyte d16));

        CollectionAssert.AreEqual(
            new sbyte[] { -2, -1, 0, 1, -2, -1, 0, 1 },
            new sbyte[] { d9, d10, d11, d12, d13, d14, d15, d16 });
    }

    [TestMethod]
    public void DeviceTableBuilder_CanBuildVariationIndex_AndParseViaDeviceTable()
    {
        var builder = new DeviceTableBuilder();
        builder.SetVariationIndex(outerIndex: 12, innerIndex: 34);

        byte[] deviceBytes = builder.ToArray();

        Assert.IsTrue(Tag.TryParse("TEST", out var testTag));

        var sfnt = new SfntBuilder { SfntVersion = 0x00010000u };
        sfnt.SetTable(KnownTags.head, TestSfntTables.BuildValidHeadTable(unitsPerEm: 1000));
        sfnt.SetTable(testTag, deviceBytes);

        using var file = SfntFile.FromMemory(sfnt.ToArray());
        var font = file.GetFont(0);

        Assert.IsTrue(font.TryGetTableSlice(testTag, out var slice));
        Assert.IsTrue(DeviceTable.TryCreate(slice, offset: 0, out var device));

        Assert.AreEqual((ushort)0x8000, device.DeltaFormat);
        Assert.IsTrue(device.IsVariationIndex);
        Assert.IsTrue(device.TryGetVarIdx(out var varIdx));
        Assert.AreEqual(new VarIdx(12, 34), varIdx);

        Assert.IsFalse(device.TryGetDelta(ppemSize: 10, out _));
    }

    [TestMethod]
    public void DeviceTableBuilder_TryFrom_RoundTripsBytes()
    {
        var builder = new DeviceTableBuilder();
        builder.SetDeltas(
            startSize: 10,
            endSize: 13,
            deltas: new sbyte[] { -8, -7, 6, 7 },
            deltaFormat: 2);

        byte[] original = builder.ToArray();

        Assert.IsTrue(Tag.TryParse("TEST", out var testTag));

        var sfnt = new SfntBuilder { SfntVersion = 0x00010000u };
        sfnt.SetTable(KnownTags.head, TestSfntTables.BuildValidHeadTable(unitsPerEm: 1000));
        sfnt.SetTable(testTag, original);

        using var file = SfntFile.FromMemory(sfnt.ToArray());
        var font = file.GetFont(0);

        Assert.IsTrue(font.TryGetTableSlice(testTag, out var slice));
        Assert.IsTrue(DeviceTable.TryCreate(slice, offset: 0, out var device));

        Assert.IsTrue(DeviceTableBuilder.TryFrom(device, out var rebuilt));
        CollectionAssert.AreEqual(original, rebuilt.ToArray());
    }
}

