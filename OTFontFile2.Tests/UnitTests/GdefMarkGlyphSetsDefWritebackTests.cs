using Microsoft.VisualStudio.TestTools.UnitTesting;
using OTFontFile2.Tables;

namespace OTFontFile2.Tests.UnitTests;

[TestClass]
public sealed class GdefMarkGlyphSetsDefWritebackTests
{
    [TestMethod]
    public void FontModel_CanEditGdefMarkGlyphSetsDefAndWriteBack()
    {
        var cov = new CoverageTableBuilder();
        cov.AddGlyphs(new ushort[] { 5, 6, 7 });

        var mg = new GdefMarkGlyphSetsDefBuilder();
        mg.AddGlyphSet(cov);

        var gdefBuilder = new GdefTableBuilder();
        gdefBuilder.Clear();
        gdefBuilder.SetMarkGlyphSetsDef(mg);

        byte[] head = TestSfntTables.BuildValidHeadTable(unitsPerEm: 1000);

        var sfnt = new SfntBuilder { SfntVersion = 0x00010000u };
        sfnt.SetTable(KnownTags.head, head);
        sfnt.SetTable(gdefBuilder);

        using var file = SfntFile.FromMemory(sfnt.ToArray());
        var font = file.GetFont(0);

        Assert.IsTrue(font.TryGetGdef(out var originalGdef));
        Assert.AreEqual(0x00010002u, originalGdef.Version.RawValue); // v1.2 (auto-bumped)
        Assert.IsTrue(originalGdef.TryGetMarkGlyphSetsDef(out var originalMg));
        Assert.AreEqual((ushort)1, originalMg.MarkGlyphSetCount);

        Assert.IsTrue(originalMg.TryIsGlyphInSet(markSetIndex: 0, glyphId: 5, out bool in5));
        Assert.IsTrue(in5);
        Assert.IsTrue(originalMg.TryIsGlyphInSet(markSetIndex: 0, glyphId: 8, out bool in8));
        Assert.IsFalse(in8);

        var model = new FontModel(font);
        Assert.IsTrue(model.TryEdit<GdefTableBuilder>(out var edit));

        var newCov = new CoverageTableBuilder();
        newCov.AddGlyphs(new ushort[] { 8 });
        var newMg = new GdefMarkGlyphSetsDefBuilder();
        newMg.AddGlyphSet(newCov);
        edit.SetMarkGlyphSetsDef(newMg);

        byte[] editedBytes = model.ToArray();
        Assert.AreEqual(0xB1B0AFBAu, OpenTypeChecksum.Compute(editedBytes));

        using var editedFile = SfntFile.FromMemory(editedBytes);
        var editedFont = editedFile.GetFont(0);

        Assert.IsTrue(editedFont.TryGetGdef(out var editedGdef));
        Assert.AreEqual(0x00010002u, editedGdef.Version.RawValue);
        Assert.IsTrue(editedGdef.TryGetMarkGlyphSetsDef(out var editedMg));
        Assert.AreEqual((ushort)1, editedMg.MarkGlyphSetCount);

        Assert.IsTrue(editedMg.TryIsGlyphInSet(markSetIndex: 0, glyphId: 5, out bool editedIn5));
        Assert.IsFalse(editedIn5);
        Assert.IsTrue(editedMg.TryIsGlyphInSet(markSetIndex: 0, glyphId: 8, out bool editedIn8));
        Assert.IsTrue(editedIn8);
    }
}

