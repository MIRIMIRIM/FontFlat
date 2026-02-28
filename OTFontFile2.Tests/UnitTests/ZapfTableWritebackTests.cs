using Microsoft.VisualStudio.TestTools.UnitTesting;
using OTFontFile2.Tables;

namespace OTFontFile2.Tests.UnitTests;

[TestClass]
public sealed class ZapfTableWritebackTests
{
    [TestMethod]
    public void ZapfTable_CanEditAndWriteBack_WithSfntEditor()
    {
        const ushort glyphCount = 3;

        var zapfBuilder = new ZapfTableBuilder(glyphCount)
        {
            Version = new Fixed1616(0x00010000u),
            ExtraInfo = 0
        };
        zapfBuilder.SetGlyphInfoOffset(glyphId: 1, glyphInfoOffset: 123u);

        byte[] head = TestSfntTables.BuildValidHeadTable(unitsPerEm: 1000);

        var sfnt = new SfntBuilder { SfntVersion = 0x00010000u };
        sfnt.SetTable(KnownTags.head, head);
        sfnt.SetTable(zapfBuilder);
        byte[] originalFontBytes = sfnt.ToArray();

        using var originalFile = SfntFile.FromMemory(originalFontBytes);
        var originalFont = originalFile.GetFont(0);

        Assert.IsTrue(originalFont.TryGetZapf(out var originalZapf));
        Assert.AreEqual(0x00010000u, originalZapf.Version.RawValue);
        Assert.AreEqual(0u, originalZapf.ExtraInfo);
        Assert.IsTrue(originalZapf.TryGetGlyphInfoOffset(glyphId: 1, glyphCount: glyphCount, out uint off1));
        Assert.AreEqual(123u, off1);

        Assert.IsTrue(ZapfTableBuilder.TryFrom(originalZapf, glyphCount, out var edit));
        edit.ExtraInfo = 999;
        edit.SetGlyphInfoOffset(glyphId: 2, glyphInfoOffset: 456u);

        var editor = new SfntEditor(originalFont);
        editor.SetTable(edit);
        byte[] editedFontBytes = editor.ToArray();

        Assert.AreEqual(0xB1B0AFBAu, OpenTypeChecksum.Compute(editedFontBytes));

        using var editedFile = SfntFile.FromMemory(editedFontBytes);
        var editedFont = editedFile.GetFont(0);
        Assert.IsTrue(editedFont.TryGetZapf(out var editedZapf));
        Assert.AreEqual(999u, editedZapf.ExtraInfo);
        Assert.IsTrue(editedZapf.TryGetGlyphInfoOffset(glyphId: 1, glyphCount: glyphCount, out uint editedOff1));
        Assert.AreEqual(123u, editedOff1);
        Assert.IsTrue(editedZapf.TryGetGlyphInfoOffset(glyphId: 2, glyphCount: glyphCount, out uint editedOff2));
        Assert.AreEqual(456u, editedOff2);
    }
}

