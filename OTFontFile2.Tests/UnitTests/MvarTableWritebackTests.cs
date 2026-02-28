using Microsoft.VisualStudio.TestTools.UnitTesting;
using OTFontFile2.Tables;

namespace OTFontFile2.Tests.UnitTests;

[TestClass]
public sealed class MvarTableWritebackTests
{
    [TestMethod]
    public void MvarTable_CanEditAndWriteBack_WithSfntEditor()
    {
        Assert.IsTrue(Tag.TryParse("test", out var test));

        var mvarBuilder = new MvarTableBuilder
        {
            MajorVersion = 1,
            MinorVersion = 0
        };
        mvarBuilder.AddValueRecord(test, new VarIdx(outerIndex: 0, innerIndex: 1));
        mvarBuilder.SetMinimalItemVariationStore(axisCount: 0);

        byte[] head = TestSfntTables.BuildValidHeadTable(unitsPerEm: 1000);

        var sfnt = new SfntBuilder { SfntVersion = 0x00010000u };
        sfnt.SetTable(KnownTags.head, head);
        sfnt.SetTable(mvarBuilder);
        byte[] originalFontBytes = sfnt.ToArray();

        using var originalFile = SfntFile.FromMemory(originalFontBytes);
        var originalFont = originalFile.GetFont(0);

        Assert.IsTrue(originalFont.TryGetMvar(out var originalMvar));
        Assert.AreEqual((ushort)1, originalMvar.MajorVersion);
        Assert.AreEqual((ushort)0, originalMvar.MinorVersion);
        Assert.AreEqual((ushort)1, originalMvar.ValueRecordCount);
        Assert.IsTrue(originalMvar.TryGetValueRecord(0, out var record0));
        Assert.AreEqual(test.Value, record0.ValueTag.Value);
        Assert.AreEqual(new VarIdx(0, 1), record0.DeltaSetIndex);

        Assert.IsTrue(originalMvar.TryGetItemVariationStore(out var store));
        Assert.AreEqual((ushort)1, store.Format);
        Assert.AreEqual((ushort)0, store.ItemVariationDataCount);
        Assert.IsTrue(store.TryGetVariationRegionList(out var regions));
        Assert.AreEqual((ushort)0, regions.AxisCount);
        Assert.AreEqual((ushort)0, regions.RegionCount);

        Assert.IsTrue(MvarTableBuilder.TryFrom(originalMvar, out var edit));
        edit.ClearValueRecords();
        edit.AddValueRecord(test, new VarIdx(outerIndex: 2, innerIndex: 3));
        edit.SetMinimalItemVariationStore(axisCount: 1);

        var editor = new SfntEditor(originalFont);
        editor.SetTable(edit);
        byte[] editedFontBytes = editor.ToArray();

        Assert.AreEqual(0xB1B0AFBAu, OpenTypeChecksum.Compute(editedFontBytes));

        using var editedFile = SfntFile.FromMemory(editedFontBytes);
        var editedFont = editedFile.GetFont(0);
        Assert.IsTrue(editedFont.TryGetMvar(out var editedMvar));
        Assert.AreEqual((ushort)1, editedMvar.ValueRecordCount);
        Assert.IsTrue(editedMvar.TryGetValueRecord(0, out var editedRecord0));
        Assert.AreEqual(new VarIdx(2, 3), editedRecord0.DeltaSetIndex);

        Assert.IsTrue(editedMvar.TryGetItemVariationStore(out var editedStore));
        Assert.IsTrue(editedStore.TryGetVariationRegionList(out var editedRegions));
        Assert.AreEqual((ushort)1, editedRegions.AxisCount);
    }
}

