using System.Buffers.Binary;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OTFontFile2.Tables;

namespace OTFontFile2.Tests.UnitTests;

[TestClass]
public sealed class SbixTableWritebackTests
{
    [TestMethod]
    public void FontModel_CanEditSbixRawAndWriteBack()
    {
        byte[] head = TestSfntTables.BuildValidHeadTable(unitsPerEm: 1000);

        var sbixBuilder = new SbixTableBuilder();

        var sfnt = new SfntBuilder { SfntVersion = 0x00010000u };
        sfnt.SetTable(KnownTags.head, head);
        sfnt.SetTable(sbixBuilder);

        using var file = SfntFile.FromMemory(sfnt.ToArray());
        var font = file.GetFont(0);

        Assert.IsTrue(font.TryGetSbix(out var originalSbix));
        Assert.AreEqual((ushort)1, originalSbix.Version);
        Assert.AreEqual(0u, originalSbix.StrikeCount);

        var model = new FontModel(font);
        Assert.IsTrue(model.TryEdit<SbixTableBuilder>(out var edit));

        byte[] bytes = edit.ToArray();
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(0, 2), 2); // version
        edit.SetTableData(bytes);

        byte[] editedBytes = model.ToArray();
        Assert.AreEqual(0xB1B0AFBAu, OpenTypeChecksum.Compute(editedBytes));

        using var editedFile = SfntFile.FromMemory(editedBytes);
        var editedFont = editedFile.GetFont(0);
        Assert.IsTrue(editedFont.TryGetSbix(out var editedSbix));
        Assert.AreEqual((ushort)2, editedSbix.Version);
    }
}

