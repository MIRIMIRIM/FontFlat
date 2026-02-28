using Microsoft.VisualStudio.TestTools.UnitTesting;
using OTFontFile2.Tables;

namespace OTFontFile2.Tests.UnitTests;

[TestClass]
public sealed class GdefLigCaretDeviceWritebackTests
{
    [TestMethod]
    public void FontModel_CanWriteGdefLigCaretList_WithCaretValueFormat3DeviceTable()
    {
        byte[] head = TestSfntTables.BuildValidHeadTable(unitsPerEm: 1000);

        var sfnt = new SfntBuilder { SfntVersion = 0x00010000u };
        sfnt.SetTable(KnownTags.head, head);

        using var file = SfntFile.FromMemory(sfnt.ToArray());
        var font = file.GetFont(0);

        var model = new FontModel(font);
        Assert.IsTrue(model.TryEdit<GdefTableBuilder>(out var gdefBuilder));

        var device = new DeviceTableBuilder();
        device.SetDeltas(startSize: 9, endSize: 12, deltas: new sbyte[] { -1, 0, 1, -2 });

        var lig = new GdefLigCaretListBuilder();
        lig.AddOrReplace(
            ligGlyphId: 10,
            carets: new[]
            {
                GdefLigCaretListBuilder.CaretValue.DeviceValue(coordinate: 123, device: device),
            });

        gdefBuilder.Clear();
        gdefBuilder.SetLigCaretList(lig);

        byte[] editedBytes = model.ToArray();
        Assert.AreEqual(0xB1B0AFBAu, OpenTypeChecksum.Compute(editedBytes));

        using var editedFile = SfntFile.FromMemory(editedBytes);
        var editedFont = editedFile.GetFont(0);

        Assert.IsTrue(editedFont.TryGetGdef(out var gdef));
        Assert.IsTrue(gdef.TryGetLigCaretList(out var ligCaretList));
        Assert.IsTrue(ligCaretList.TryGetLigGlyphTableForGlyph(glyphId: 10, out bool covered10, out var ligGlyph));
        Assert.IsTrue(covered10);
        Assert.AreEqual((ushort)1, ligGlyph.CaretCount);

        Assert.IsTrue(ligGlyph.TryGetCaretValueTable(0, out var caret));
        Assert.AreEqual((ushort)3, caret.CaretValueFormat);
        Assert.IsTrue(caret.TryGetCoordinate(out short coord));
        Assert.AreEqual((short)123, coord);

        Assert.IsTrue(caret.TryGetDeviceTableAbsoluteOffset(out int deviceOffset));
        Assert.IsTrue(DeviceTable.TryCreate(gdef.Table, deviceOffset, out var deviceTable));
        Assert.AreEqual((ushort)1, deviceTable.DeltaFormat);

        Assert.IsTrue(deviceTable.TryGetDelta(ppemSize: 9, out sbyte d9));
        Assert.IsTrue(deviceTable.TryGetDelta(ppemSize: 10, out sbyte d10));
        Assert.IsTrue(deviceTable.TryGetDelta(ppemSize: 11, out sbyte d11));
        Assert.IsTrue(deviceTable.TryGetDelta(ppemSize: 12, out sbyte d12));

        CollectionAssert.AreEqual(new sbyte[] { -1, 0, 1, -2 }, new sbyte[] { d9, d10, d11, d12 });
    }
}

