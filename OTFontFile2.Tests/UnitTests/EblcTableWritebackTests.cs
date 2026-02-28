using Microsoft.VisualStudio.TestTools.UnitTesting;
using OTFontFile2.Tables;

namespace OTFontFile2.Tests.UnitTests;

[TestClass]
public sealed class EblcTableWritebackTests
{
    [TestMethod]
    public void EblcTable_CanEditAndWriteBack_WithSfntEditor()
    {
        var eblcBuilder = new EblcTableBuilder
        {
            Version = new Fixed1616(0x00020000u),
            BitmapSizeTableCount = 0
        };

        byte[] head = TestSfntTables.BuildValidHeadTable(unitsPerEm: 1000);

        var sfnt = new SfntBuilder { SfntVersion = 0x00010000u };
        sfnt.SetTable(KnownTags.head, head);
        sfnt.SetTable(eblcBuilder);
        byte[] originalFontBytes = sfnt.ToArray();

        using var originalFile = SfntFile.FromMemory(originalFontBytes);
        var originalFont = originalFile.GetFont(0);

        Assert.IsTrue(originalFont.TryGetEblc(out var originalEblc));
        Assert.AreEqual(0x00020000u, originalEblc.Version.RawValue);
        Assert.AreEqual(0u, originalEblc.BitmapSizeTableCount);

        Assert.IsTrue(EblcTableBuilder.TryFrom(originalEblc, out var edit));
        edit.SetBody(bitmapSizeTableCount: 0, bodyBytes: new byte[] { 1, 2, 3, 4, 5 });

        var editor = new SfntEditor(originalFont);
        editor.SetTable(edit);
        byte[] editedFontBytes = editor.ToArray();

        Assert.AreEqual(0xB1B0AFBAu, OpenTypeChecksum.Compute(editedFontBytes));

        using var editedFile = SfntFile.FromMemory(editedFontBytes);
        var editedFont = editedFile.GetFont(0);

        Assert.IsTrue(editedFont.TryGetEblc(out var editedEblc));
        Assert.AreEqual(0x00020000u, editedEblc.Version.RawValue);
        Assert.AreEqual(0u, editedEblc.BitmapSizeTableCount);

        Assert.IsTrue(editedFont.TryGetTableSlice(KnownTags.EBLC, out var slice));
        CollectionAssert.AreEqual(edit.ToArray(), slice.Span.ToArray());
    }
}

