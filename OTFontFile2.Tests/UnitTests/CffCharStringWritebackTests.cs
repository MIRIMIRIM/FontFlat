using Microsoft.VisualStudio.TestTools.UnitTesting;
using OTFontFile2.Tables;
using OTFontFile2.Tables.Cff;
using System.Buffers.Binary;

namespace OTFontFile2.Tests.UnitTests;

[TestClass]
public sealed class CffCharStringWritebackTests
{
    [TestMethod]
    public void FontModel_CanOverrideCffCharString_AndWriteBack()
    {
        string path = GetFontPath("AvenirNextW1G-Regular.OTF");

        using var file = SfntFile.Open(path);
        var font = file.GetFont(0);

        var model = new FontModel(font);
        Assert.IsTrue(model.TryEdit<CffTableBuilder>(out var cff));
        Assert.IsTrue(cff.IsLinkedBaseFont);

        int gid = 0;
        byte[] newCharString = new byte[] { 139, 0x0E }; // push 0, endchar
        cff.SetGlyphCharString(gid, newCharString);

        Assert.IsTrue(cff.TryGetGlyphCharStringProgram(gid, out var prog));
        Assert.AreEqual(2, prog.Tokens.Count);
        Assert.AreEqual(Type2TokenKind.Number, prog.Tokens[0].Kind);
        Assert.AreEqual(0, prog.Tokens[0].Number.Value);
        Assert.AreEqual(Type2TokenKind.Operator, prog.Tokens[1].Kind);
        Assert.AreEqual((ushort)0x0E, prog.Tokens[1].Operator);

        byte[] editedBytes = model.ToArray();

        using var editedFile = SfntFile.FromMemory(editedBytes);
        var editedFont = editedFile.GetFont(0);

        Assert.IsTrue(editedFont.TryGetCff(out var editedCff));
        Assert.IsTrue(editedCff.TryGetTopDict(out var top));
        Assert.IsTrue(top.TryGetCharStringsIndex(out var charStrings));
        Assert.IsTrue(charStrings.TryGetObjectSpan(gid, out var cs));
        Assert.IsTrue(cs.SequenceEqual(newCharString));
    }

    [TestMethod]
    public void FontModel_CanOverrideCffCharString_WithFdArrayAndFdSelect_AndWriteBack()
    {
        string path = GetFontPath("SourceHanSansCN-Regular.otf");

        using var file = SfntFile.Open(path);
        var font = file.GetFont(0);

        Assert.IsTrue(font.TryGetCff(out var baseCff));
        Assert.IsTrue(baseCff.TryGetTopDict(out var baseTop));
        Assert.IsTrue(baseTop.FdArrayOffset > 0, "Test fixture should have FDArray.");
        Assert.IsTrue(baseTop.FdSelectOffset > 0, "Test fixture should have FDSelect.");

        var model = new FontModel(font);
        Assert.IsTrue(model.TryEdit<CffTableBuilder>(out var cff));
        Assert.IsTrue(cff.IsLinkedBaseFont);
        Assert.IsTrue(cff.GlyphCount > 0);

        int gid = 0;
        byte[] newCharString = new byte[] { 139, 0x0E };
        cff.SetGlyphCharString(gid, newCharString);

        byte[] editedBytes = model.ToArray();

        using var editedFile = SfntFile.FromMemory(editedBytes);
        var editedFont = editedFile.GetFont(0);

        Assert.IsTrue(editedFont.TryGetCff(out var editedCff));
        Assert.IsTrue(editedCff.TryGetTopDict(out var top));
        Assert.IsTrue(top.FdArrayOffset > 0);
        Assert.IsTrue(top.FdSelectOffset > 0);
        Assert.IsTrue(top.TryGetCharStringsIndex(out var charStrings));
        Assert.IsTrue(charStrings.TryGetObjectSpan(gid, out var cs));
        Assert.IsTrue(cs.SequenceEqual(newCharString));
    }

    [TestMethod]
    public void FontModel_CanOverrideCff2CharString_AndWriteBack()
    {
        var sfnt = new SfntBuilder { SfntVersion = 0x4F54544Fu }; // 'OTTO'
        sfnt.SetTable(KnownTags.CFF2, BuildSyntheticCff2Table());
        byte[] fontBytes = sfnt.ToArray();

        using var file = SfntFile.FromMemory(fontBytes);
        var font = file.GetFont(0);

        var model = new FontModel(font);
        Assert.IsTrue(model.TryEdit<Cff2TableBuilder>(out var cff2));
        Assert.IsTrue(cff2.IsLinkedBaseFont);
        Assert.AreEqual(1, cff2.GlyphCount);

        byte[] newCharString = new byte[] { 139, 0x0E };
        cff2.SetGlyphCharString(0, newCharString);

        byte[] editedBytes = model.ToArray();

        using var editedFile = SfntFile.FromMemory(editedBytes);
        var editedFont = editedFile.GetFont(0);

        Assert.IsTrue(editedFont.TryGetCff2(out var editedCff2));
        Assert.IsTrue(editedCff2.TryGetCharStringsIndex(out var charStrings));
        Assert.IsTrue(charStrings.TryGetObjectSpan(0, out var cs));
        Assert.IsTrue(cs.SequenceEqual(newCharString));
    }

    private static string GetFontPath(string fileName)
        => Path.Combine(AppContext.BaseDirectory, "TestResources", "SampleFonts", fileName);

    private static byte[] BuildSyntheticCff2Table()
    {
        // Minimal valid CFF2 with 1 glyph and an empty GlobalSubrs INDEX.
        // Layout:
        // header(5) + TopDict(18) + GlobalSubrs INDEX(empty, 4)
        // + FDSelect(format0, 2) + FDArray INDEX(14) + CharStrings INDEX(8)
        // + Private DICT(4) + Subrs INDEX(empty, 4)

        const int headerSize = 5;
        const int topDictLength = 18;

        int globalSubrsOffset = headerSize + topDictLength;
        const int globalSubrsLength = 4; // count(4) == 0

        int fdSelectOffset = globalSubrsOffset + globalSubrsLength;
        const int fdSelectLength = 2; // format(1) + fdIndex[1]

        int fdArrayOffset = fdSelectOffset + fdSelectLength;
        const int fdArrayLength = 14;

        int charStringsOffset = fdArrayOffset + fdArrayLength;
        const int charStringsLength = 8;

        int privateOffset = charStringsOffset + charStringsLength;
        const int privateSize = 4;

        int subrsOffset = privateOffset + privateSize;
        const int subrsLength = 4;

        int totalLength = subrsOffset + subrsLength;
        byte[] table = new byte[totalLength];
        var span = table.AsSpan();

        // Header
        span[0] = 2; // major
        span[1] = 0; // minor
        span[2] = headerSize;
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(3, 2), topDictLength);

        // Top DICT (starts at offset 5)
        int td = headerSize;

        // maxstack 513: 28 0x02 0x01 25
        span[td + 0] = 28;
        span[td + 1] = 0x02;
        span[td + 2] = 0x01;
        span[td + 3] = 25;

        // FDSelect offset: 28 hi lo 12 37
        span[td + 4] = 28;
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(td + 5, 2), (ushort)fdSelectOffset);
        span[td + 7] = 12;
        span[td + 8] = 37;

        // FDArray offset: 28 hi lo 12 36
        span[td + 9] = 28;
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(td + 10, 2), (ushort)fdArrayOffset);
        span[td + 12] = 12;
        span[td + 13] = 36;

        // CharStrings offset: 28 hi lo 17
        span[td + 14] = 28;
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(td + 15, 2), (ushort)charStringsOffset);
        span[td + 17] = 17;

        // GlobalSubrs INDEX (empty): count(4)=0
        BinaryPrimitives.WriteUInt32BigEndian(span.Slice(globalSubrsOffset, 4), 0u);

        // FDSelect (format 0, glyphCount 1): [format=0][fdIndex=0]
        span[fdSelectOffset + 0] = 0;
        span[fdSelectOffset + 1] = 0;

        // FDArray INDEX (count 1, offSize 1, offsets [1,8], data len 7)
        BinaryPrimitives.WriteUInt32BigEndian(span.Slice(fdArrayOffset, 4), 1u);
        span[fdArrayOffset + 4] = 1; // offSize
        span[fdArrayOffset + 5] = 1; // first offset
        span[fdArrayOffset + 6] = 8; // last offset (1 + 7 bytes)
        int fontDictOffset = fdArrayOffset + 7;

        // Font DICT: Private(size, offset) operator (18)
        span[fontDictOffset + 0] = 28;
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(fontDictOffset + 1, 2), privateSize);
        span[fontDictOffset + 3] = 28;
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(fontDictOffset + 4, 2), (ushort)privateOffset);
        span[fontDictOffset + 6] = 18;

        // CharStrings INDEX (count 1, offSize 1, offsets [1,2], data [0x0E])
        BinaryPrimitives.WriteUInt32BigEndian(span.Slice(charStringsOffset, 4), 1u);
        span[charStringsOffset + 4] = 1;
        span[charStringsOffset + 5] = 1;
        span[charStringsOffset + 6] = 2;
        span[charStringsOffset + 7] = 0x0E;

        // Private DICT: Subrs offset (4) operator (19)
        span[privateOffset + 0] = 28;
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(privateOffset + 1, 2), 4);
        span[privateOffset + 3] = 19;

        // Local Subrs INDEX (empty)
        BinaryPrimitives.WriteUInt32BigEndian(span.Slice(subrsOffset, 4), 0u);

        return table;
    }
}
