using Microsoft.VisualStudio.TestTools.UnitTesting;
using OTFontFile2.Tables;

namespace OTFontFile2.Tests.UnitTests;

[TestClass]
public sealed class EbdtTableWritebackTests
{
    [TestMethod]
    public void EbdtTable_CanEditAndWriteBack_WithSfntEditor()
    {
        var ebdtBuilder = new EbdtTableBuilder
        {
            Version = new Fixed1616(0x00020000u)
        };
        ebdtBuilder.SetPayload(new byte[] { 0xAA, 0xBB });

        byte[] head = TestSfntTables.BuildValidHeadTable(unitsPerEm: 1000);

        var sfnt = new SfntBuilder { SfntVersion = 0x00010000u };
        sfnt.SetTable(KnownTags.head, head);
        sfnt.SetTable(ebdtBuilder);
        byte[] originalFontBytes = sfnt.ToArray();

        using var originalFile = SfntFile.FromMemory(originalFontBytes);
        var originalFont = originalFile.GetFont(0);

        Assert.IsTrue(originalFont.TryGetEbdt(out var originalEbdt));
        Assert.AreEqual(0x00020000u, originalEbdt.Version.RawValue);
        Assert.IsTrue(originalEbdt.TryGetGlyphSpan(offset: 4, length: 2, out var glyphBytes));
        CollectionAssert.AreEqual(new byte[] { 0xAA, 0xBB }, glyphBytes.ToArray());

        Assert.IsTrue(EbdtTableBuilder.TryFrom(originalEbdt, out var edit));
        edit.Version = new Fixed1616(0x00020000u);
        edit.SetPayload(new byte[] { 1, 2, 3, 4 });

        var editor = new SfntEditor(originalFont);
        editor.SetTable(edit);
        byte[] editedFontBytes = editor.ToArray();

        Assert.AreEqual(0xB1B0AFBAu, OpenTypeChecksum.Compute(editedFontBytes));

        using var editedFile = SfntFile.FromMemory(editedFontBytes);
        var editedFont = editedFile.GetFont(0);

        Assert.IsTrue(editedFont.TryGetEbdt(out var editedEbdt));
        Assert.AreEqual(0x00020000u, editedEbdt.Version.RawValue);
        Assert.IsTrue(editedEbdt.TryGetGlyphSpan(offset: 4, length: 4, out var editedGlyphBytes));
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3, 4 }, editedGlyphBytes.ToArray());
    }
}

