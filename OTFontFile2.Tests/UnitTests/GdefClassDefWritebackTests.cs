using Microsoft.VisualStudio.TestTools.UnitTesting;
using OTFontFile2.Tables;

namespace OTFontFile2.Tests.UnitTests;

[TestClass]
public sealed class GdefClassDefWritebackTests
{
    [TestMethod]
    public void FontModel_CanEditGdefGlyphClassDefAndWriteBack()
    {
        var glyphClassDef = new ClassDefTableBuilder();
        glyphClassDef.SetClass(glyphId: 5, classValue: 1);
        glyphClassDef.SetClass(glyphId: 6, classValue: 1);
        glyphClassDef.SetClass(glyphId: 7, classValue: 2);

        var gdefBuilder = new GdefTableBuilder();
        gdefBuilder.Clear();
        gdefBuilder.SetGlyphClassDef(glyphClassDef);

        byte[] head = TestSfntTables.BuildValidHeadTable(unitsPerEm: 1000);

        var sfnt = new SfntBuilder { SfntVersion = 0x00010000u };
        sfnt.SetTable(KnownTags.head, head);
        sfnt.SetTable(gdefBuilder);

        using var file = SfntFile.FromMemory(sfnt.ToArray());
        var font = file.GetFont(0);

        Assert.IsTrue(font.TryGetGdef(out var originalGdef));
        Assert.AreEqual(0x00010000u, originalGdef.Version.RawValue);
        Assert.IsTrue(originalGdef.TryGetGlyphClassDef(out var originalClassDef));

        Assert.IsTrue(originalClassDef.TryGetClass(glyphId: 4, out ushort c4));
        Assert.AreEqual((ushort)0, c4);
        Assert.IsTrue(originalClassDef.TryGetClass(glyphId: 5, out ushort c5));
        Assert.AreEqual((ushort)1, c5);
        Assert.IsTrue(originalClassDef.TryGetClass(glyphId: 6, out ushort c6));
        Assert.AreEqual((ushort)1, c6);
        Assert.IsTrue(originalClassDef.TryGetClass(glyphId: 7, out ushort c7));
        Assert.AreEqual((ushort)2, c7);

        var model = new FontModel(font);
        Assert.IsTrue(model.TryEdit<GdefTableBuilder>(out var edit));

        var newClassDef = new ClassDefTableBuilder();
        newClassDef.SetClass(glyphId: 5, classValue: 9);
        newClassDef.SetClass(glyphId: 6, classValue: 9);
        edit.SetGlyphClassDef(newClassDef);

        byte[] editedBytes = model.ToArray();
        Assert.AreEqual(0xB1B0AFBAu, OpenTypeChecksum.Compute(editedBytes));

        using var editedFile = SfntFile.FromMemory(editedBytes);
        var editedFont = editedFile.GetFont(0);

        Assert.IsTrue(editedFont.TryGetGdef(out var editedGdef));
        Assert.AreEqual(0x00010000u, editedGdef.Version.RawValue);
        Assert.IsTrue(editedGdef.TryGetGlyphClassDef(out var editedClassDef));
        Assert.IsTrue(editedClassDef.TryGetClass(glyphId: 5, out ushort editedC5));
        Assert.AreEqual((ushort)9, editedC5);
        Assert.IsTrue(editedClassDef.TryGetClass(glyphId: 6, out ushort editedC6));
        Assert.AreEqual((ushort)9, editedC6);
        Assert.IsTrue(editedClassDef.TryGetClass(glyphId: 7, out ushort editedC7));
        Assert.AreEqual((ushort)0, editedC7);
    }
}

