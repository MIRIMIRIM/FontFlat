using System.Buffers.Binary;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OTFontFile2.Tables;

namespace OTFontFile2.Tests.UnitTests;

[TestClass]
public sealed class MathTableWritebackTests
{
    [TestMethod]
    public void MathTable_CanEditAndWriteBack_WithSfntEditor()
    {
        var mathBuilder = new MathTableBuilder();

        byte[] head = TestSfntTables.BuildValidHeadTable(unitsPerEm: 1000);

        var sfnt = new SfntBuilder { SfntVersion = 0x00010000u };
        sfnt.SetTable(KnownTags.head, head);
        sfnt.SetTable(mathBuilder);
        byte[] originalFontBytes = sfnt.ToArray();

        using var originalFile = SfntFile.FromMemory(originalFontBytes);
        var originalFont = originalFile.GetFont(0);
        Assert.IsTrue(originalFont.TryGetMath(out var originalMath));
        Assert.AreEqual(0x00010000u, originalMath.Version.RawValue);

        Assert.IsTrue(MathTableBuilder.TryFrom(originalMath, out var edit));

        // Patch the version (Fixed 16.16) and keep offsets at 0 for now (raw-bytes builder).
        byte[] editedTable = edit.DataBytes.ToArray();
        BinaryPrimitives.WriteUInt32BigEndian(editedTable.AsSpan(0, 4), 0x00020000u);
        edit.SetTableData(editedTable);

        var editor = new SfntEditor(originalFont);
        editor.SetTable(edit);
        byte[] editedFontBytes = editor.ToArray();

        Assert.AreEqual(0xB1B0AFBAu, OpenTypeChecksum.Compute(editedFontBytes));

        using var editedFile = SfntFile.FromMemory(editedFontBytes);
        var editedFont = editedFile.GetFont(0);
        Assert.IsTrue(editedFont.TryGetMath(out var editedMath));
        Assert.AreEqual(0x00020000u, editedMath.Version.RawValue);
    }
}
