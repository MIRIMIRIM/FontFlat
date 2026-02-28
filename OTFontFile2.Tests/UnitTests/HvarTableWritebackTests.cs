using System.Buffers.Binary;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OTFontFile2.Tables;

namespace OTFontFile2.Tests.UnitTests;

[TestClass]
public sealed class HvarTableWritebackTests
{
    [TestMethod]
    public void HvarTable_CanEditAndWriteBack_WithSfntEditor()
    {
        // Minimal DeltaSetIndexMap (format 0), 1 entry, all zeros.
        byte[] mapBytes = new byte[6];
        mapBytes[0] = 0; // format
        mapBytes[1] = 0x10; // entrySize=2, innerIndexBitCount=1 (both stored as -1)
        BinaryPrimitives.WriteUInt16BigEndian(mapBytes.AsSpan(2, 2), 1); // mapCount
        // map data left as 0

        var hvarBuilder = new HvarTableBuilder
        {
            MajorVersion = 1,
            MinorVersion = 0
        };
        hvarBuilder.SetMinimalItemVariationStore(axisCount: 0);
        hvarBuilder.SetAdvanceWidthMapping(mapBytes);

        byte[] head = TestSfntTables.BuildValidHeadTable(unitsPerEm: 1000);

        var sfnt = new SfntBuilder { SfntVersion = 0x00010000u };
        sfnt.SetTable(KnownTags.head, head);
        sfnt.SetTable(hvarBuilder);
        byte[] originalFontBytes = sfnt.ToArray();

        using var originalFile = SfntFile.FromMemory(originalFontBytes);
        var originalFont = originalFile.GetFont(0);

        Assert.IsTrue(originalFont.TryGetHvar(out var originalHvar));
        Assert.AreEqual((ushort)1, originalHvar.MajorVersion);
        Assert.IsTrue(originalHvar.TryGetItemVariationStore(out var store));
        Assert.AreEqual((ushort)1, store.Format);

        Assert.IsTrue(originalHvar.TryGetAdvanceWidthMapping(out var advMap));
        Assert.AreEqual((ushort)1, advMap.MapCount);
        Assert.IsTrue(advMap.TryGetVarIdx(0, out var idx0));
        Assert.AreEqual(new VarIdx(0, 0), idx0);

        Assert.IsTrue(HvarTableBuilder.TryFrom(originalHvar, out var edit));
        edit.ClearAdvanceWidthMapping();
        edit.SetLsbMapping(mapBytes);
        edit.SetMinimalItemVariationStore(axisCount: 1);

        var editor = new SfntEditor(originalFont);
        editor.SetTable(edit);
        byte[] editedFontBytes = editor.ToArray();

        Assert.AreEqual(0xB1B0AFBAu, OpenTypeChecksum.Compute(editedFontBytes));

        using var editedFile = SfntFile.FromMemory(editedFontBytes);
        var editedFont = editedFile.GetFont(0);
        Assert.IsTrue(editedFont.TryGetHvar(out var editedHvar));
        Assert.IsFalse(editedHvar.TryGetAdvanceWidthMapping(out _));
        Assert.IsTrue(editedHvar.TryGetLsbMapping(out var lsbMap));
        Assert.AreEqual((ushort)1, lsbMap.MapCount);

        Assert.IsTrue(editedHvar.TryGetItemVariationStore(out var editedStore));
        Assert.IsTrue(editedStore.TryGetVariationRegionList(out var regions));
        Assert.AreEqual((ushort)1, regions.AxisCount);
    }
}
