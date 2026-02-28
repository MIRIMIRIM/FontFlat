using System.Buffers.Binary;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OTFontFile2.Tables;

namespace OTFontFile2.Tests.UnitTests;

[TestClass]
public sealed class EblcEbdtStructuredWritebackTests
{
    [TestMethod]
    public void FontModel_CanEditEblcStructuredAndWritesDerivedEbdt()
    {
        const ushort numGlyphs = 4;

        byte[] head = TestSfntTables.BuildValidHeadTable(unitsPerEm: 1000);
        byte[] maxp = BuildMaxpV05(numGlyphs);

        var eblcBuilder = new EblcTableBuilder();
        eblcBuilder.EnableStructured();

        var strike = eblcBuilder.AddStrike(ppemX: 12, ppemY: 12, bitDepth: 1);
        var sub = strike.AddIndexSubTable(firstGlyphIndex: 0, lastGlyphIndex: 2, imageFormat: 1);
        sub.SetGlyphData(glyphId: 0, data: BuildEbdtFormat1(bitmap: new byte[] { 0x01 }));
        sub.SetGlyphData(glyphId: 2, data: BuildEbdtFormat1(bitmap: new byte[] { 0x7F }));

        Assert.IsTrue(eblcBuilder.TryBuildDerivedEbdt(out var ebdtBuilder));

        var sfnt = new SfntBuilder { SfntVersion = 0x00010000u };
        sfnt.SetTable(KnownTags.head, head);
        sfnt.SetTable(KnownTags.maxp, maxp);
        sfnt.SetTable(eblcBuilder);
        sfnt.SetTable(ebdtBuilder);

        using var file = SfntFile.FromMemory(sfnt.ToArray());
        var font = file.GetFont(0);

        AssertHasEbdtBitmap(font, sizeIndex: 0, glyphId: 0, expectedBitmap: new byte[] { 0x01 });
        AssertHasEbdtBitmap(font, sizeIndex: 0, glyphId: 2, expectedBitmap: new byte[] { 0x7F });

        var model = new FontModel(font);
        Assert.IsTrue(model.TryEdit<EblcTableBuilder>(out var edit));
        Assert.IsTrue(edit.IsStructured);
        Assert.IsFalse(edit.IsRaw);

        edit.Strikes[0].IndexSubTables[0].SetGlyphData(glyphId: 1, data: BuildEbdtFormat1(bitmap: new byte[] { 0x55 }));

        byte[] editedFontBytes = model.ToArray();
        Assert.AreEqual(0xB1B0AFBAu, OpenTypeChecksum.Compute(editedFontBytes));

        using var editedFile = SfntFile.FromMemory(editedFontBytes);
        var editedFont = editedFile.GetFont(0);

        AssertHasEbdtBitmap(editedFont, sizeIndex: 0, glyphId: 1, expectedBitmap: new byte[] { 0x55 });
    }

    private static void AssertHasEbdtBitmap(SfntFont font, int sizeIndex, ushort glyphId, byte[] expectedBitmap)
    {
        Assert.IsTrue(font.TryGetEblc(out var eblc));
        Assert.IsTrue(font.TryGetEbdt(out var ebdt));

        Assert.IsTrue(eblc.TryGetBitmapSizeTable(sizeIndex, out var size));
        Assert.IsTrue(size.TryGetGlyphImageBounds(glyphId, out ushort imageFormat, out int ebdtOffset, out int length));
        Assert.AreEqual((ushort)1, imageFormat);

        Assert.IsTrue(ebdt.TryGetGlyphSpan(ebdtOffset, length, out var glyphData));
        Assert.IsTrue(EbdtTable.TryGetSmallMetricsAndBitmap(glyphData, out _, out var bitmap));
        CollectionAssert.AreEqual(expectedBitmap, bitmap.ToArray());
    }

    private static byte[] BuildEbdtFormat1(byte[] bitmap)
    {
        // smallMetrics(5) + bitmap data
        byte[] bytes = new byte[5 + bitmap.Length];
        var span = bytes.AsSpan();
        span[0] = 1; // height
        span[1] = 1; // width
        span[2] = 0; // bearingX
        span[3] = 0; // bearingY
        span[4] = 1; // advance
        bitmap.AsSpan().CopyTo(span.Slice(5));
        return bytes;
    }

    private static byte[] BuildMaxpV05(ushort numGlyphs)
    {
        byte[] maxp = new byte[6];
        var span = maxp.AsSpan();
        BinaryPrimitives.WriteUInt32BigEndian(span.Slice(0, 4), 0x00005000u); // v0.5
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(4, 2), numGlyphs);
        return maxp;
    }
}

