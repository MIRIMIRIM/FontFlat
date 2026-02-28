using System.Buffers.Binary;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OTFontFile2.Tables;

namespace OTFontFile2.Tests.UnitTests;

[TestClass]
public sealed class SbixStructuredWritebackTests
{
    [TestMethod]
    public void FontModel_CanEditSbixStructuredAndWriteBack()
    {
        const ushort numGlyphs = 3;

        byte[] head = TestSfntTables.BuildValidHeadTable(unitsPerEm: 1000);
        byte[] maxp = BuildMaxpV05(numGlyphs);

        var sbixBuilder = new SbixTableBuilder();
        sbixBuilder.SetNumGlyphs(numGlyphs);

        var strike = sbixBuilder.AddStrike(ppem: 16, resolution: 72);
        strike.SetGlyph(
            glyphId: 0,
            new SbixTableBuilder.GlyphRecord(
                originOffsetX: -1,
                originOffsetY: 2,
                graphicType: new Tag(0x706E6720u), // 'png '
                payload: new byte[] { 1, 2, 3, 4 }));

        strike.SetGlyph(
            glyphId: 2,
            new SbixTableBuilder.GlyphRecord(
                originOffsetX: 0,
                originOffsetY: 0,
                graphicType: new Tag(0x6A706567u), // 'jpeg'
                payload: new byte[] { 9 }));

        var sfnt = new SfntBuilder { SfntVersion = 0x00010000u };
        sfnt.SetTable(KnownTags.head, head);
        sfnt.SetTable(KnownTags.maxp, maxp);
        sfnt.SetTable(sbixBuilder);

        using var file = SfntFile.FromMemory(sfnt.ToArray());
        var font = file.GetFont(0);

        Assert.IsTrue(font.TryGetSbix(out var sbix));
        Assert.AreEqual((ushort)1, sbix.Version);
        Assert.AreEqual(1u, sbix.StrikeCount);

        Assert.IsTrue(sbix.TryGetStrike(strikeIndex: 0, numGlyphs: numGlyphs, out var originalStrike));
        Assert.AreEqual((ushort)16, originalStrike.Ppem);
        Assert.AreEqual((ushort)72, originalStrike.Resolution);

        Assert.IsTrue(originalStrike.TryGetGlyphDataSpan(glyphId: 0, out var glyph0Data));
        Assert.IsTrue(SbixTable.TryReadGlyphHeader(glyph0Data, out var glyph0Header, out var glyph0Payload));
        Assert.AreEqual((short)-1, glyph0Header.OriginOffsetX);
        Assert.AreEqual((short)2, glyph0Header.OriginOffsetY);
        Assert.AreEqual(new Tag(0x706E6720u), glyph0Header.GraphicType);
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3, 4 }, glyph0Payload.ToArray());

        var model = new FontModel(font);
        Assert.IsTrue(model.TryEdit<SbixTableBuilder>(out var edit));
        Assert.IsFalse(edit.IsRaw);
        Assert.IsTrue(edit.HasNumGlyphs);
        Assert.AreEqual(numGlyphs, edit.NumGlyphs);
        Assert.AreEqual(1, edit.StrikeCount);

        edit.Strikes[0].SetGlyph(
            glyphId: 1,
            new SbixTableBuilder.GlyphRecord(
                originOffsetX: 10,
                originOffsetY: 20,
                graphicType: new Tag(0x706E6720u), // 'png '
                payload: new byte[] { 0xAA, 0xBB }));

        byte[] editedBytes = model.ToArray();
        Assert.AreEqual(0xB1B0AFBAu, OpenTypeChecksum.Compute(editedBytes));

        using var editedFile = SfntFile.FromMemory(editedBytes);
        var editedFont = editedFile.GetFont(0);
        Assert.IsTrue(editedFont.TryGetSbix(out var editedSbix));
        Assert.IsTrue(editedSbix.TryGetStrike(strikeIndex: 0, numGlyphs: numGlyphs, out var editedStrike));

        Assert.IsTrue(editedStrike.TryGetGlyphDataSpan(glyphId: 1, out var glyph1Data));
        Assert.IsTrue(SbixTable.TryReadGlyphHeader(glyph1Data, out var glyph1Header, out var glyph1Payload));
        Assert.AreEqual((short)10, glyph1Header.OriginOffsetX);
        Assert.AreEqual((short)20, glyph1Header.OriginOffsetY);
        Assert.AreEqual(new Tag(0x706E6720u), glyph1Header.GraphicType);
        CollectionAssert.AreEqual(new byte[] { 0xAA, 0xBB }, glyph1Payload.ToArray());
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

