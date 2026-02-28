using Microsoft.VisualStudio.TestTools.UnitTesting;
using OTFontFile2.Tables;
using System.Buffers.Binary;

namespace OTFontFile2.Tests.UnitTests;

[TestClass]
public sealed class GvarCvarTablesTests
{
    [TestMethod]
    public void SyntheticGvarTable_ParsesHeaderAndGlyphVariationDataBounds()
    {
        byte[] gvarBytes = BuildGvarTable();

        var builder = new SfntBuilder { SfntVersion = 0x00010000 };
        builder.SetTable(KnownTags.gvar, gvarBytes);
        byte[] fontBytes = builder.ToArray();

        using var file = SfntFile.FromMemory(fontBytes);
        var font = file.GetFont(0);

        Assert.IsTrue(font.TryGetGvar(out var gvar));
        Assert.AreEqual(1u << 16, gvar.Version.RawValue);
        Assert.AreEqual((ushort)1, gvar.AxisCount);
        Assert.AreEqual((ushort)0, gvar.SharedTupleCount);
        Assert.AreEqual((ushort)1, gvar.GlyphCount);
        Assert.IsFalse(gvar.OffsetsAreLong);

        Assert.IsTrue(gvar.TryGetGlyphVariationDataBounds(0, out int dataOffset, out int dataLength));
        Assert.AreEqual(24, dataOffset);
        Assert.AreEqual(4, dataLength);

        Assert.IsTrue(gvar.TryGetGlyphTupleVariationStore(0, out var store));
        Assert.AreEqual((ushort)0, store.TupleVariationCount);
        Assert.IsTrue(store.TryGetSharedPointNumbersByteLength(out int sharedLen));
        Assert.AreEqual(0, sharedLen);
    }

    [TestMethod]
    public void SyntheticCvarTable_ParsesTupleVariationStore()
    {
        byte[] cvarBytes = BuildCvarTable();

        var builder = new SfntBuilder { SfntVersion = 0x00010000 };
        builder.SetTable(KnownTags.cvar, cvarBytes);
        byte[] fontBytes = builder.ToArray();

        using var file = SfntFile.FromMemory(fontBytes);
        var font = file.GetFont(0);

        Assert.IsTrue(font.TryGetCvar(out var cvar));
        Assert.AreEqual(1u << 16, cvar.Version.RawValue);
        Assert.AreEqual((ushort)1, cvar.TupleVariationCount);

        Assert.IsTrue(cvar.TryGetTupleVariationStore(axisCount: 2, out var store));
        Assert.AreEqual((ushort)1, store.TupleVariationCount);
        Assert.IsFalse(store.HasSharedPointNumbers);
        Assert.AreEqual((ushort)16, store.OffsetToData);

        Assert.IsTrue(store.TryGetTupleVariation(0, out var tv));
        Assert.IsTrue(tv.HasEmbeddedPeakTuple);
        Assert.IsFalse(tv.HasIntermediateRegion);
        Assert.IsTrue(tv.HasPrivatePointNumbers);

        Assert.IsTrue(tv.TryGetPeakTupleCoordinate(0, out var a0));
        Assert.IsTrue(tv.TryGetPeakTupleCoordinate(1, out var a1));
        Assert.AreEqual((short)0, a0.RawValue);
        Assert.AreEqual(unchecked((short)0xC000), a1.RawValue);

        Assert.IsTrue(tv.TryGetVariationDataSpan(out var varData));
        Assert.AreEqual(9, varData.Length);
        Assert.AreEqual((byte)0x03, varData[0]);
        Assert.AreEqual((byte)0x04, varData[^1]);
    }

    private static byte[] BuildGvarTable()
    {
        // Minimal gvar with:
        // axisCount=1, sharedTupleCount=0, glyphCount=1, short offsets,
        // glyphVariationDataArrayOffset=24 and a single glyph record of 4 bytes (tupleCount=0, offsetToData=4).
        byte[] table = new byte[28];
        var span = table.AsSpan();

        // Header
        BinaryPrimitives.WriteUInt32BigEndian(span.Slice(0, 4), 0x00010000u); // version
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(4, 2), 1); // axisCount
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(6, 2), 0); // sharedTupleCount
        BinaryPrimitives.WriteUInt32BigEndian(span.Slice(8, 4), 0u); // sharedTuplesOffset
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(12, 2), 1); // glyphCount
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(14, 2), 0); // flags (short offsets)
        BinaryPrimitives.WriteUInt32BigEndian(span.Slice(16, 4), 24u); // glyphVariationDataArrayOffset

        // Offsets array (glyphCount+1 entries, short offsets are in words).
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(20, 2), 0); // glyph0 start
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(22, 2), 2); // glyph1 end (4 bytes => 2 words)

        // Glyph variation data array at offset 24: tupleVariationCount=0, offsetToData=4.
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(24, 2), 0);
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(26, 2), 4);

        return table;
    }

    private static byte[] BuildCvarTable()
    {
        // Based on a minimal real-world pattern:
        // version(4) + tupleVariationCount(2) + offsetToData(2)
        // + tupleVariationHeader(4) + peakTuple(axisCount*2=4)
        // + variationData(9)
        byte[] table = new byte[25];
        var span = table.AsSpan();

        BinaryPrimitives.WriteUInt32BigEndian(span.Slice(0, 4), 0x00010000u); // version
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(4, 2), 1); // tupleVariationCount
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(6, 2), 16); // offsetToData (from start of table)

        // TupleVariationHeader at offset 8
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(8, 2), 9); // variationDataSize
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(10, 2), 0xA000); // tupleIndex (embedded peak tuple + private points)

        // Peak tuple for 2 axes: [0, -1]
        BinaryPrimitives.WriteInt16BigEndian(span.Slice(12, 2), 0);
        BinaryPrimitives.WriteInt16BigEndian(span.Slice(14, 2), unchecked((short)0xC000));

        // Variation data (9 bytes) at offset 16.
        span[16] = 0x03;
        span[17] = 0x02;
        span[18] = 0x01;
        span[19] = 0x05;
        span[20] = 0x01;
        span[21] = 0x02;
        span[22] = 0x1C;
        span[23] = 0x7B;
        span[24] = 0x04;

        return table;
    }
}

