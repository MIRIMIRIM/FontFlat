using System.Buffers.Binary;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OTFontFile2.Tables;

namespace OTFontFile2.Tests.UnitTests;

[TestClass]
public sealed class DeviceTableTests
{
    [TestMethod]
    public void DeviceTable_Format2_UnpacksDeltas()
    {
        // start=10, end=13, format=2 (4-bit signed)
        // deltas: [-1, 0, 3, -8] packed MSB-first => 0xF038
        byte[] table = new byte[8];
        var span = table.AsSpan();
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(0, 2), 10);
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(2, 2), 13);
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(4, 2), 2);
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(6, 2), 0xF038);

        var tag = new Tag(0x64657663); // 'devc'
        var builder = new SfntBuilder { SfntVersion = 0x00010000 };
        builder.SetTable(tag, table);
        byte[] fontBytes = builder.ToArray();

        using var file = SfntFile.FromMemory(fontBytes);
        var font = file.GetFont(0);
        Assert.IsTrue(font.TryGetTableSlice(tag, out var slice));

        Assert.IsTrue(DeviceTable.TryCreate(slice, 0, out var device));
        Assert.IsFalse(device.IsVariationIndex);
        Assert.IsFalse(device.TryGetVarIdx(out _));

        Assert.IsTrue(device.TryGetDelta(9, out sbyte d9));
        Assert.AreEqual((sbyte)0, d9);

        Assert.IsTrue(device.TryGetDelta(10, out sbyte d10));
        Assert.AreEqual((sbyte)-1, d10);

        Assert.IsTrue(device.TryGetDelta(11, out sbyte d11));
        Assert.AreEqual((sbyte)0, d11);

        Assert.IsTrue(device.TryGetDelta(12, out sbyte d12));
        Assert.AreEqual((sbyte)3, d12);

        Assert.IsTrue(device.TryGetDelta(13, out sbyte d13));
        Assert.AreEqual((sbyte)-8, d13);

        Assert.IsTrue(device.TryGetDelta(14, out sbyte d14));
        Assert.AreEqual((sbyte)0, d14);
    }

    [TestMethod]
    public void DeviceTable_VariationIndex_ExposesVarIdx()
    {
        // VariationIndex table uses the Device table header with deltaFormat=0x8000.
        byte[] table = new byte[6];
        var span = table.AsSpan();
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(0, 2), 2);      // outerIndex
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(2, 2), 7);      // innerIndex
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(4, 2), 0x8000); // deltaFormat

        var tag = new Tag(0x64657676); // 'devv'
        var builder = new SfntBuilder { SfntVersion = 0x00010000 };
        builder.SetTable(tag, table);
        byte[] fontBytes = builder.ToArray();

        using var file = SfntFile.FromMemory(fontBytes);
        var font = file.GetFont(0);
        Assert.IsTrue(font.TryGetTableSlice(tag, out var slice));

        Assert.IsTrue(DeviceTable.TryCreate(slice, 0, out var device));
        Assert.IsTrue(device.IsVariationIndex);
        Assert.IsTrue(device.TryGetVarIdx(out var varIdx));
        Assert.AreEqual((ushort)2, varIdx.OuterIndex);
        Assert.AreEqual((ushort)7, varIdx.InnerIndex);
        Assert.IsFalse(device.TryGetDelta(10, out _));
    }
}

