using System.Buffers.Binary;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OTFontFile2.Tables;

namespace OTFontFile2.Tests.UnitTests;

[TestClass]
public sealed class VvarTableWritebackTests
{
    [TestMethod]
    public void VvarTable_CanEditAndWriteBack_WithSfntEditor()
    {
        // Minimal DeltaSetIndexMap (format 0), 1 entry, all zeros.
        byte[] mapBytes = new byte[6];
        mapBytes[0] = 0; // format
        mapBytes[1] = 0x10; // entrySize=2, innerIndexBitCount=1 (both stored as -1)
        BinaryPrimitives.WriteUInt16BigEndian(mapBytes.AsSpan(2, 2), 1); // mapCount

        var vvarBuilder = new VvarTableBuilder
        {
            MajorVersion = 1,
            MinorVersion = 0
        };
        vvarBuilder.SetMinimalItemVariationStore(axisCount: 0);
        vvarBuilder.SetAdvanceHeightMapping(mapBytes);
        vvarBuilder.SetVorgMapping(mapBytes);

        byte[] head = TestSfntTables.BuildValidHeadTable(unitsPerEm: 1000);

        var sfnt = new SfntBuilder { SfntVersion = 0x00010000u };
        sfnt.SetTable(KnownTags.head, head);
        sfnt.SetTable(vvarBuilder);
        byte[] originalFontBytes = sfnt.ToArray();

        using var originalFile = SfntFile.FromMemory(originalFontBytes);
        var originalFont = originalFile.GetFont(0);

        Assert.IsTrue(originalFont.TryGetVvar(out var originalVvar));
        Assert.AreEqual((ushort)1, originalVvar.MajorVersion);
        Assert.IsTrue(originalVvar.TryGetItemVariationStore(out var store));
        Assert.AreEqual((ushort)1, store.Format);

        Assert.IsTrue(originalVvar.TryGetAdvanceHeightMapping(out var advMap));
        Assert.AreEqual((ushort)1, advMap.MapCount);
        Assert.IsTrue(advMap.TryGetVarIdx(0, out var idx0));
        Assert.AreEqual(new VarIdx(0, 0), idx0);

        Assert.IsTrue(originalVvar.TryGetVorgMapping(out var vorgMap));
        Assert.AreEqual((ushort)1, vorgMap.MapCount);

        Assert.IsTrue(VvarTableBuilder.TryFrom(originalVvar, out var edit));
        edit.ClearAdvanceHeightMapping();
        edit.ClearVorgMapping();
        edit.SetTsbMapping(mapBytes);
        edit.SetBsbMapping(mapBytes);
        edit.SetMinimalItemVariationStore(axisCount: 1);

        var editor = new SfntEditor(originalFont);
        editor.SetTable(edit);
        byte[] editedFontBytes = editor.ToArray();

        Assert.AreEqual(0xB1B0AFBAu, OpenTypeChecksum.Compute(editedFontBytes));

        using var editedFile = SfntFile.FromMemory(editedFontBytes);
        var editedFont = editedFile.GetFont(0);
        Assert.IsTrue(editedFont.TryGetVvar(out var editedVvar));
        Assert.IsFalse(editedVvar.TryGetAdvanceHeightMapping(out _));
        Assert.IsFalse(editedVvar.TryGetVorgMapping(out _));
        Assert.IsTrue(editedVvar.TryGetTsbMapping(out var tsbMap));
        Assert.AreEqual((ushort)1, tsbMap.MapCount);
        Assert.IsTrue(editedVvar.TryGetBsbMapping(out var bsbMap));
        Assert.AreEqual((ushort)1, bsbMap.MapCount);

        Assert.IsTrue(editedVvar.TryGetItemVariationStore(out var editedStore));
        Assert.IsTrue(editedStore.TryGetVariationRegionList(out var regions));
        Assert.AreEqual((ushort)1, regions.AxisCount);
    }
}

