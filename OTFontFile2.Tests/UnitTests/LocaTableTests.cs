using Microsoft.VisualStudio.TestTools.UnitTesting;
using OTFontFile2.Tables;

namespace OTFontFile2.Tests.UnitTests;

[TestClass]
public sealed class LocaTableTests
{
    [TestMethod]
    public void Loca_Format0_DecodesOffsets()
    {
        // numGlyphs = 2 => entryCount = 3, each entry is uint16 of (offset/2).
        // Offsets: 0, 10, 22.
        byte[] loca =
        {
            0x00, 0x00, // 0
            0x00, 0x05, // 10/2
            0x00, 0x0B  // 22/2
        };

        using var buffer = CreateFontBufferWithLoca(loca, out var font);
        Assert.IsTrue(font.TryGetLoca(out var table));

        Assert.IsTrue(table.TryGetGlyphOffsetLength(glyphId: 0, indexToLocFormat: 0, numGlyphs: 2, out int off0, out int len0));
        Assert.AreEqual(0, off0);
        Assert.AreEqual(10, len0);

        Assert.IsTrue(table.TryGetGlyphOffsetLength(glyphId: 1, indexToLocFormat: 0, numGlyphs: 2, out int off1, out int len1));
        Assert.AreEqual(10, off1);
        Assert.AreEqual(12, len1);

        Assert.IsFalse(table.TryGetGlyphOffsetLength(glyphId: 2, indexToLocFormat: 0, numGlyphs: 2, out _, out _));
    }

    [TestMethod]
    public void Loca_Format1_DecodesOffsets()
    {
        // numGlyphs = 2 => entryCount = 3, each entry is uint32 byte offset.
        // Offsets: 0, 10, 22.
        byte[] loca =
        {
            0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x0A,
            0x00, 0x00, 0x00, 0x16
        };

        using var buffer = CreateFontBufferWithLoca(loca, out var font);
        Assert.IsTrue(font.TryGetLoca(out var table));

        Assert.IsTrue(table.TryGetGlyphOffsetLength(glyphId: 0, indexToLocFormat: 1, numGlyphs: 2, out int off0, out int len0));
        Assert.AreEqual(0, off0);
        Assert.AreEqual(10, len0);

        Assert.IsTrue(table.TryGetGlyphOffsetLength(glyphId: 1, indexToLocFormat: 1, numGlyphs: 2, out int off1, out int len1));
        Assert.AreEqual(10, off1);
        Assert.AreEqual(12, len1);
    }

    [TestMethod]
    public void Loca_TruncatedTable_ReturnsFalse()
    {
        // numGlyphs = 2 => needs 6 bytes for format0, but only provide 4.
        byte[] loca =
        {
            0x00, 0x00,
            0x00, 0x05
        };

        using var buffer = CreateFontBufferWithLoca(loca, out var font);
        Assert.IsTrue(font.TryGetLoca(out var table));
        Assert.IsFalse(table.TryGetGlyphOffsetLength(glyphId: 0, indexToLocFormat: 0, numGlyphs: 2, out _, out _));
    }

    private static FontBuffer CreateFontBufferWithLoca(byte[] locaTableBytes, out SfntFont font)
    {
        var builder = new SfntBuilder();
        builder.SetTable(KnownTags.loca, locaTableBytes);

        byte[] bytes = builder.ToArray();

        var buffer = FontBuffer.FromMemory(bytes);
        Assert.IsTrue(SfntFont.TryCreate(buffer, offsetTableOffset: 0, out font, out _));
        return buffer;
    }
}
