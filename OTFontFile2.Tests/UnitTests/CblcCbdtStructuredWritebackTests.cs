using System.Buffers.Binary;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OTFontFile2.Tables;

namespace OTFontFile2.Tests.UnitTests;

[TestClass]
public sealed class CblcCbdtStructuredWritebackTests
{
    [TestMethod]
    public void FontModel_CanEditCblcStructuredAndWritesDerivedCbdt()
    {
        const ushort numGlyphs = 4;

        byte[] head = TestSfntTables.BuildValidHeadTable(unitsPerEm: 1000);
        byte[] maxp = BuildMaxpV05(numGlyphs);

        var cblcBuilder = new CblcTableBuilder();
        cblcBuilder.EnableStructured();

        var strike = cblcBuilder.AddStrike(ppemX: 16, ppemY: 16, bitDepth: 32);
        var sub = strike.AddIndexSubTable(firstGlyphIndex: 0, lastGlyphIndex: 2, imageFormat: 19);
        sub.SetGlyphData(glyphId: 0, data: BuildCbdtFormat19(new byte[] { 1, 2, 3 }));
        sub.SetGlyphData(glyphId: 2, data: BuildCbdtFormat19(new byte[] { 9 }));

        Assert.IsTrue(cblcBuilder.TryBuildDerivedCbdt(out var cbdtBuilder));

        var sfnt = new SfntBuilder { SfntVersion = 0x00010000u };
        sfnt.SetTable(KnownTags.head, head);
        sfnt.SetTable(KnownTags.maxp, maxp);
        sfnt.SetTable(cblcBuilder);
        sfnt.SetTable(cbdtBuilder);

        using var file = SfntFile.FromMemory(sfnt.ToArray());
        var font = file.GetFont(0);

        AssertHasCbdtData(font, sizeIndex: 0, glyphId: 0, expected: new byte[] { 1, 2, 3 });
        AssertHasCbdtData(font, sizeIndex: 0, glyphId: 2, expected: new byte[] { 9 });

        var model = new FontModel(font);
        Assert.IsTrue(model.TryEdit<CblcTableBuilder>(out var edit));
        Assert.IsTrue(edit.IsStructured);
        Assert.IsFalse(edit.IsRaw);

        edit.Strikes[0].IndexSubTables[0].SetGlyphData(glyphId: 1, data: BuildCbdtFormat19(new byte[] { 0xAA, 0xBB }));

        byte[] editedFontBytes = model.ToArray();
        Assert.AreEqual(0xB1B0AFBAu, OpenTypeChecksum.Compute(editedFontBytes));

        using var editedFile = SfntFile.FromMemory(editedFontBytes);
        var editedFont = editedFile.GetFont(0);

        AssertHasCbdtData(editedFont, sizeIndex: 0, glyphId: 1, expected: new byte[] { 0xAA, 0xBB });
    }

    private static void AssertHasCbdtData(SfntFont font, int sizeIndex, ushort glyphId, byte[] expected)
    {
        Assert.IsTrue(font.TryGetCblc(out var cblc));
        Assert.IsTrue(font.TryGetCbdt(out var cbdt));

        Assert.IsTrue(cblc.TryGetBitmapSizeTable(sizeIndex, out var size));
        Assert.IsTrue(size.TryGetGlyphImageBounds(glyphId, out ushort imageFormat, out int cbdtOffset, out int length));
        Assert.AreEqual((ushort)19, imageFormat);

        Assert.IsTrue(cbdt.TryGetGlyphSpan(cbdtOffset, length, out var glyphData));
        Assert.IsTrue(CbdtTable.TryGetFormat19Data(glyphData, out var payload));
        CollectionAssert.AreEqual(expected, payload.ToArray());
    }

    private static byte[] BuildCbdtFormat19(byte[] payload)
    {
        byte[] bytes = new byte[4 + payload.Length];
        var span = bytes.AsSpan();
        BinaryPrimitives.WriteUInt32BigEndian(span.Slice(0, 4), (uint)payload.Length);
        payload.AsSpan().CopyTo(span.Slice(4));
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
