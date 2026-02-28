using Microsoft.VisualStudio.TestTools.UnitTesting;
using OTFontFile2.Tables;

namespace OTFontFile2.Tests.UnitTests;

[TestClass]
public sealed class CoreTableWritebackTests
{
    [TestMethod]
    public void HeadTable_CanBuildAndWriteBack_WithSfntEditor()
    {
        var headBuilder = new HeadTableBuilder
        {
            UnitsPerEm = 1000,
            MagicNumber = 0x5F0F3CF5u
        };

        var sfnt = new SfntBuilder { SfntVersion = 0x00010000u };
        sfnt.SetTable(headBuilder);
        byte[] originalFontBytes = sfnt.ToArray();

        Assert.AreEqual(0xB1B0AFBAu, OpenTypeChecksum.Compute(originalFontBytes));

        using var file = SfntFile.FromMemory(originalFontBytes);
        var font = file.GetFont(0);
        Assert.IsTrue(font.TryGetHead(out var head));
        Assert.AreEqual((ushort)1000, head.UnitsPerEm);

        Assert.IsTrue(HeadTableBuilder.TryFrom(head, out var edit));
        edit.UnitsPerEm = 2048;

        var editor = new SfntEditor(font);
        editor.SetTable(edit);
        byte[] editedFontBytes = editor.ToArray();

        Assert.AreEqual(0xB1B0AFBAu, OpenTypeChecksum.Compute(editedFontBytes));

        using var editedFile = SfntFile.FromMemory(editedFontBytes);
        var editedFont = editedFile.GetFont(0);
        Assert.IsTrue(editedFont.TryGetHead(out var editedHead));
        Assert.AreEqual((ushort)2048, editedHead.UnitsPerEm);
    }

    [TestMethod]
    public void HheaTable_CanEditAndWriteBack_WithSfntEditor()
    {
        byte[] head = TestSfntTables.BuildValidHeadTable(unitsPerEm: 1000);

        var hheaBuilder = new HheaTableBuilder
        {
            Ascender = 800,
            Descender = -200,
            LineGap = 20,
            AdvanceWidthMax = 1200,
            NumberOfHMetrics = 3
        };

        var sfnt = new SfntBuilder { SfntVersion = 0x00010000u };
        sfnt.SetTable(KnownTags.head, head);
        sfnt.SetTable(hheaBuilder);
        byte[] originalFontBytes = sfnt.ToArray();

        using var file = SfntFile.FromMemory(originalFontBytes);
        var font = file.GetFont(0);
        Assert.IsTrue(font.TryGetHhea(out var originalHhea));
        Assert.AreEqual((short)800, originalHhea.Ascender);
        Assert.AreEqual((short)-200, originalHhea.Descender);

        Assert.IsTrue(HheaTableBuilder.TryFrom(originalHhea, out var edit));
        edit.Ascender = 900;

        var editor = new SfntEditor(font);
        editor.SetTable(edit);
        byte[] editedFontBytes = editor.ToArray();

        Assert.AreEqual(0xB1B0AFBAu, OpenTypeChecksum.Compute(editedFontBytes));

        using var editedFile = SfntFile.FromMemory(editedFontBytes);
        var editedFont = editedFile.GetFont(0);
        Assert.IsTrue(editedFont.TryGetHhea(out var editedHhea));
        Assert.AreEqual((short)900, editedHhea.Ascender);
    }

    [TestMethod]
    public void Os2Table_CanEditAndWriteBack_WithSfntEditor()
    {
        Assert.IsTrue(Tag.TryParse("TEST", out var testVendor));

        byte[] head = TestSfntTables.BuildValidHeadTable(unitsPerEm: 1000);

        var os2Builder = new Os2TableBuilder
        {
            Version = 4,
            UsWeightClass = 400,
            UsWidthClass = 5,
            AchVendId = testVendor,
            UsWinAscent = 800,
            UsWinDescent = 200
        };

        var sfnt = new SfntBuilder { SfntVersion = 0x00010000u };
        sfnt.SetTable(KnownTags.head, head);
        sfnt.SetTable(os2Builder);
        byte[] originalFontBytes = sfnt.ToArray();

        using var file = SfntFile.FromMemory(originalFontBytes);
        var font = file.GetFont(0);
        Assert.IsTrue(font.TryGetOs2(out var originalOs2));
        Assert.AreEqual((ushort)4, originalOs2.Version);
        Assert.AreEqual((ushort)400, originalOs2.UsWeightClass);
        Assert.AreEqual("TEST", originalOs2.AchVendId.ToString());

        Assert.IsTrue(Os2TableBuilder.TryFrom(originalOs2, out var edit));
        edit.UsWeightClass = 700;

        var editor = new SfntEditor(font);
        editor.SetTable(edit);
        byte[] editedFontBytes = editor.ToArray();

        Assert.AreEqual(0xB1B0AFBAu, OpenTypeChecksum.Compute(editedFontBytes));

        using var editedFile = SfntFile.FromMemory(editedFontBytes);
        var editedFont = editedFile.GetFont(0);
        Assert.IsTrue(editedFont.TryGetOs2(out var editedOs2));
        Assert.AreEqual((ushort)700, editedOs2.UsWeightClass);
    }

    [TestMethod]
    public void FontModel_ValidatesPostVersion2MatchesMaxp_WithMaxpOverride()
    {
        byte[] head = TestSfntTables.BuildValidHeadTable(unitsPerEm: 1000);

        var maxp = new MaxpTableBuilder
        {
            TableVersionNumber = new Fixed1616(0x00010000u),
            NumGlyphs = 3
        };

        var post = new PostTableBuilder
        {
            Version = new Fixed1616(0x00030000u)
        };

        var sfnt = new SfntBuilder { SfntVersion = 0x00010000u };
        sfnt.SetTable(KnownTags.head, head);
        sfnt.SetTable(maxp);
        sfnt.SetTable(post);

        using var file = SfntFile.FromMemory(sfnt.ToArray());
        var font = file.GetFont(0);

        var model = new FontModel(font);
        Assert.IsTrue(model.TryEdit<MaxpTableBuilder>(out var editMaxp));
        editMaxp.NumGlyphs = 4;

        Assert.IsTrue(model.TryEdit<PostTableBuilder>(out var editPost));
        editPost.Version = new Fixed1616(0x00020000u);
        editPost.NumberOfGlyphs = 3; // mismatch

        _ = Assert.ThrowsException<InvalidOperationException>(() => model.ToArray());

        editPost.NumberOfGlyphs = 4; // match
        byte[] editedBytes = model.ToArray();

        Assert.AreEqual(0xB1B0AFBAu, OpenTypeChecksum.Compute(editedBytes));

        using var editedFile = SfntFile.FromMemory(editedBytes);
        var editedFont = editedFile.GetFont(0);

        Assert.IsTrue(editedFont.TryGetMaxp(out var editedMaxp));
        Assert.AreEqual((ushort)4, editedMaxp.NumGlyphs);

        Assert.IsTrue(editedFont.TryGetPost(out var editedPost));
        Assert.IsTrue(editedPost.TryGetNumberOfGlyphs(out ushort postNumGlyphs));
        Assert.AreEqual((ushort)4, postNumGlyphs);
    }
}

