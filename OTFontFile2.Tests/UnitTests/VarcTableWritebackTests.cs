using System.Buffers.Binary;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OTFontFile2.Tables;

namespace OTFontFile2.Tests.UnitTests;

[TestClass]
public sealed class VarcTableWritebackTests
{
    [TestMethod]
    public void VarcTable_CanEditAndWriteBack_WithSfntEditor()
    {
        var varcBuilder = new VarcTableBuilder();

        byte[] head = TestSfntTables.BuildValidHeadTable(unitsPerEm: 1000);

        var sfnt = new SfntBuilder { SfntVersion = 0x00010000u };
        sfnt.SetTable(KnownTags.head, head);
        sfnt.SetTable(varcBuilder);
        byte[] originalFontBytes = sfnt.ToArray();

        using var originalFile = SfntFile.FromMemory(originalFontBytes);
        var originalFont = originalFile.GetFont(0);
        Assert.IsTrue(originalFont.TryGetVarc(out var originalVarc));
        Assert.AreEqual(0x00010000u, originalVarc.Version.RawValue);

        Assert.IsTrue(VarcTableBuilder.TryFrom(originalVarc, out var edit));

        byte[] editedTable = edit.DataBytes.ToArray();
        BinaryPrimitives.WriteUInt32BigEndian(editedTable.AsSpan(0, 4), 0x00020000u);
        edit.SetTableData(editedTable);

        var editor = new SfntEditor(originalFont);
        editor.SetTable(edit);
        byte[] editedFontBytes = editor.ToArray();

        Assert.AreEqual(0xB1B0AFBAu, OpenTypeChecksum.Compute(editedFontBytes));

        using var editedFile = SfntFile.FromMemory(editedFontBytes);
        var editedFont = editedFile.GetFont(0);
        Assert.IsTrue(editedFont.TryGetVarc(out var editedVarc));
        Assert.AreEqual(0x00020000u, editedVarc.Version.RawValue);
    }
}

