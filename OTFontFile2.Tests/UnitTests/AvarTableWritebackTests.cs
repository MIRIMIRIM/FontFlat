using Microsoft.VisualStudio.TestTools.UnitTesting;
using OTFontFile2.Tables;

namespace OTFontFile2.Tests.UnitTests;

[TestClass]
public sealed class AvarTableWritebackTests
{
    [TestMethod]
    public void AvarTable_CanEditAndWriteBack_WithSfntEditor()
    {
        var avarBuilder = new AvarTableBuilder();
        avarBuilder.AddAxis();
        avarBuilder.AddAxis();

        var neg1 = new F2Dot14(-16384);
        var zero = new F2Dot14(0);
        var pos1 = new F2Dot14(16384);

        // Axis 0: identity
        avarBuilder.AddMap(axisIndex: 0, fromCoordinate: neg1, toCoordinate: neg1);
        avarBuilder.AddMap(axisIndex: 0, fromCoordinate: zero, toCoordinate: zero);
        avarBuilder.AddMap(axisIndex: 0, fromCoordinate: pos1, toCoordinate: pos1);

        // Axis 1: compress positive end.
        var pos08 = new F2Dot14(13107); // ~0.8
        avarBuilder.AddMap(axisIndex: 1, fromCoordinate: neg1, toCoordinate: neg1);
        avarBuilder.AddMap(axisIndex: 1, fromCoordinate: zero, toCoordinate: zero);
        avarBuilder.AddMap(axisIndex: 1, fromCoordinate: pos1, toCoordinate: pos08);

        byte[] head = TestSfntTables.BuildValidHeadTable(unitsPerEm: 1000);

        var sfnt = new SfntBuilder { SfntVersion = 0x00010000u };
        sfnt.SetTable(KnownTags.head, head);
        sfnt.SetTable(avarBuilder);
        byte[] originalFontBytes = sfnt.ToArray();

        using var originalFile = SfntFile.FromMemory(originalFontBytes);
        var originalFont = originalFile.GetFont(0);

        Assert.IsTrue(originalFont.TryGetAvar(out var originalAvar));
        Assert.AreEqual(0x00010000u, originalAvar.Version.RawValue);
        Assert.AreEqual((ushort)2, originalAvar.AxisCount);

        Assert.IsTrue(originalAvar.TryGetSegmentMap(1, out var seg1));
        Assert.AreEqual((ushort)3, seg1.PositionMapCount);
        Assert.IsTrue(seg1.TryGetAxisValueMap(2, out var last));
        Assert.AreEqual(pos1.RawValue, last.FromCoordinate.RawValue);
        Assert.AreEqual(pos08.RawValue, last.ToCoordinate.RawValue);

        Assert.IsTrue(AvarTableBuilder.TryFrom(originalAvar, out var edit));
        edit.ClearAxisMaps(1);

        var pos09 = new F2Dot14(14746); // ~0.9
        edit.AddMap(axisIndex: 1, fromCoordinate: neg1, toCoordinate: neg1);
        edit.AddMap(axisIndex: 1, fromCoordinate: zero, toCoordinate: zero);
        edit.AddMap(axisIndex: 1, fromCoordinate: pos1, toCoordinate: pos09);

        var editor = new SfntEditor(originalFont);
        editor.SetTable(edit);
        byte[] editedFontBytes = editor.ToArray();

        Assert.AreEqual(0xB1B0AFBAu, OpenTypeChecksum.Compute(editedFontBytes));

        using var editedFile = SfntFile.FromMemory(editedFontBytes);
        var editedFont = editedFile.GetFont(0);
        Assert.IsTrue(editedFont.TryGetAvar(out var editedAvar));
        Assert.IsTrue(editedAvar.TryGetSegmentMap(1, out var editedSeg1));
        Assert.IsTrue(editedSeg1.TryGetAxisValueMap(2, out var editedLast));
        Assert.AreEqual(pos1.RawValue, editedLast.FromCoordinate.RawValue);
        Assert.AreEqual(pos09.RawValue, editedLast.ToCoordinate.RawValue);
    }
}

