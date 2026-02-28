using Microsoft.VisualStudio.TestTools.UnitTesting;
using OTFontFile2.Tables;
using Legacy = OTFontFile;

namespace OTFontFile2.Tests.UnitTests;

[TestClass]
public sealed class NameTableWritebackTests
{
    [TestMethod]
    public void NameTable_CanEditAndWriteBack_WithSfntEditor()
    {
        var nameBuilder = new NameTableBuilder(format: 0);
        nameBuilder.AddOrReplaceString(
            platformId: (ushort)NameTable.PlatformId.Windows,
            encodingId: 1,
            languageId: 0x0409,
            nameId: (ushort)NameTable.NameId.FamilyName,
            value: "OldFamily");
        nameBuilder.AddOrReplaceString(
            platformId: (ushort)NameTable.PlatformId.Windows,
            encodingId: 1,
            languageId: 0x0409,
            nameId: (ushort)NameTable.NameId.FullName,
            value: "OldFamily Regular");

        byte[] head = TestSfntTables.BuildValidHeadTable(unitsPerEm: 1000);

        var sfnt = new SfntBuilder { SfntVersion = 0x00010000u };
        sfnt.SetTable(KnownTags.head, head);
        sfnt.SetTable(nameBuilder);
        byte[] originalFontBytes = sfnt.ToArray();

        using var originalFile = SfntFile.FromMemory(originalFontBytes);
        var originalFont = originalFile.GetFont(0);
        Assert.IsTrue(originalFont.TryGetName(out var originalName));
        Assert.AreEqual("OldFamily Regular", originalName.GetFullNameString());

        Assert.IsTrue(NameTableBuilder.TryFrom(originalName, out var edit));
        edit.AddOrReplaceString(
            platformId: (ushort)NameTable.PlatformId.Windows,
            encodingId: 1,
            languageId: 0x0409,
            nameId: (ushort)NameTable.NameId.FamilyName,
            value: "NewFamily");
        edit.AddOrReplaceString(
            platformId: (ushort)NameTable.PlatformId.Windows,
            encodingId: 1,
            languageId: 0x0409,
            nameId: (ushort)NameTable.NameId.FullName,
            value: "NewFamily Regular");

        var editor = new SfntEditor(originalFont);
        editor.SetTable(edit);
        byte[] editedFontBytes = editor.ToArray();

        uint fileChecksum = OpenTypeChecksum.Compute(editedFontBytes);
        Assert.AreEqual(0xB1B0AFBAu, fileChecksum);

        using var editedFile = SfntFile.FromMemory(editedFontBytes);
        var editedFont = editedFile.GetFont(0);
        Assert.IsTrue(editedFont.TryGetName(out var editedName));
        Assert.AreEqual("NewFamily", editedName.GetGeneralStringByNameId(NameTable.NameId.FamilyName, validateSurrogates: true));
        Assert.AreEqual("NewFamily Regular", editedName.GetFullNameString());

        string tmp = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(tmp, editedFontBytes);

            using var legacyFile = new Legacy.OTFile();
            Assert.IsTrue(legacyFile.open(tmp));
            var legacyFont = legacyFile.GetFont(0)!;
            var legacyName = (Legacy.Table_name)legacyFont.GetTable("name")!;
            Assert.AreEqual("NewFamily Regular", legacyName.GetFullNameString());
        }
        finally
        {
            try { File.Delete(tmp); } catch { /* ignore */ }
        }
    }

}
