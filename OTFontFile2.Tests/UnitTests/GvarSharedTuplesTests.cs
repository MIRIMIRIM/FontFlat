using Microsoft.VisualStudio.TestTools.UnitTesting;
using OTFontFile2.Tables;
using OTFontFile2.Tables.Glyf;
using System.Buffers.Binary;

namespace OTFontFile2.Tests.UnitTests;

[TestClass]
public sealed class GvarSharedTuplesTests
{
    [TestMethod]
    public void GvarBuilder_ParsesTupleVariation_WithSharedPeakTuple()
    {
        byte[] head = TestSfntTables.BuildValidHeadTable(unitsPerEm: 1000);

        var maxp = new MaxpTableBuilder { NumGlyphs = 1 };

        var fvar = new FvarTableBuilder();
        fvar.AddAxis(new Tag(0x77676874u), minValue: new Fixed1616(0), defaultValue: new Fixed1616(0), maxValue: new Fixed1616(0), flags: 0, axisNameId: 256); // 'wght'

        // glyph 0: empty outline (0 points) -> phantom-only count = 4
        byte[] glyf = new byte[] { 0 };
        byte[] loca = new byte[] { 0, 0, 0, 0 };

        byte[] record = BuildGlyphVariationDataRecordWithSharedPeakTuple();
        byte[] gvar = BuildGvarTableWithSharedTuples(axisCount: 1, glyphCount: 1, sharedPeakTupleRaw: 0, glyph0Record: record);

        var sfnt = new SfntBuilder { SfntVersion = 0x00010000u };
        sfnt.SetTable(KnownTags.head, head);
        sfnt.SetTable(maxp);
        sfnt.SetTable(KnownTags.glyf, glyf);
        sfnt.SetTable(KnownTags.loca, loca);
        sfnt.SetTable(fvar);
        sfnt.SetTable(KnownTags.gvar, gvar);

        using var file = SfntFile.FromMemory(sfnt.ToArray());
        var font = file.GetFont(0);

        var model = new FontModel(font);
        Assert.IsTrue(model.TryEdit<GvarTableBuilder>(out var gvarBuilder));
        Assert.IsTrue(gvarBuilder.IsLinkedBaseFont);

        Assert.IsTrue(gvarBuilder.TryGetGlyphVariations(0, out var parsed));
        Assert.AreEqual(1, parsed.TupleVariationCount);
        Assert.IsTrue(parsed.TryGetTupleVariation(0, out var tv0));
        Assert.AreEqual((short)0, tv0.PeakTupleRaw[0]);
    }

    private static byte[] BuildGlyphVariationDataRecordWithSharedPeakTuple()
    {
        // tupleVariationCount=1, offsetToData=8
        // variationDataSize=7
        // tupleIndexRaw: private points flag + sharedTupleIndex=0 (no embedded peak)
        // variationData: points=[0], x=[1], y=[2]
        byte[] bytes = new byte[16]; // pad to even
        var span = bytes.AsSpan();

        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(0, 2), 1);
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(2, 2), 8);
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(4, 2), 7);
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(6, 2), 0x2000);

        int p = 8;
        span[p + 0] = 0x01; // pointCount=1
        span[p + 1] = 0x00; // run: byte, len=1
        span[p + 2] = 0x00; // delta=0 => point 0

        span[p + 3] = 0x00; // x run: byte, len=1
        span[p + 4] = 0x01; // x=1

        span[p + 5] = 0x00; // y run: byte, len=1
        span[p + 6] = 0x02; // y=2

        return bytes;
    }

    private static byte[] BuildGvarTableWithSharedTuples(ushort axisCount, ushort glyphCount, short sharedPeakTupleRaw, ReadOnlySpan<byte> glyph0Record)
    {
        int headerLen = 20;
        int offsetsBytes = (glyphCount + 1) * 2;
        int sharedTuplesOffset = headerLen + offsetsBytes;
        sharedTuplesOffset = (sharedTuplesOffset + 1) & ~1;

        int sharedTuplesBytes = axisCount * 2;
        int dataOffset = sharedTuplesOffset + sharedTuplesBytes;
        dataOffset = (dataOffset + 1) & ~1;

        int recordLenAligned = (glyph0Record.Length + 1) & ~1;
        ushort endWords = checked((ushort)(recordLenAligned >> 1));

        byte[] table = new byte[checked(dataOffset + recordLenAligned)];
        var span = table.AsSpan();

        BinaryPrimitives.WriteUInt32BigEndian(span.Slice(0, 4), 0x00010000u);
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(4, 2), axisCount);
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(6, 2), 1); // sharedTupleCount
        BinaryPrimitives.WriteUInt32BigEndian(span.Slice(8, 4), (uint)sharedTuplesOffset);
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(12, 2), glyphCount);
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(14, 2), 0); // short offsets
        BinaryPrimitives.WriteUInt32BigEndian(span.Slice(16, 4), (uint)dataOffset);

        // offsets (glyphCount+1) in words: [0, end]
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(20, 2), 0);
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(22, 2), endWords);

        // shared tuple 0
        BinaryPrimitives.WriteInt16BigEndian(span.Slice(sharedTuplesOffset, 2), sharedPeakTupleRaw);

        glyph0Record.CopyTo(span.Slice(dataOffset, glyph0Record.Length));
        return table;
    }
}

