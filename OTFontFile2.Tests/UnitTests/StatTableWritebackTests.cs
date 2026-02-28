using Microsoft.VisualStudio.TestTools.UnitTesting;
using OTFontFile2.Tables;

namespace OTFontFile2.Tests.UnitTests;

[TestClass]
public sealed class StatTableWritebackTests
{
    [TestMethod]
    public void StatTable_CanEditAndWriteBack_WithSfntEditor()
    {
        Assert.IsTrue(Tag.TryParse("wght", out var wght));
        Assert.IsTrue(Tag.TryParse("wdth", out var wdth));

        var statBuilder = new StatTableBuilder
        {
            MajorVersion = 1,
            MinorVersion = 0,
            ElidedFallbackNameId = 0
        };

        statBuilder.AddDesignAxis(axisTag: wght, axisNameId: 256, axisOrdering: 0);
        statBuilder.AddDesignAxis(axisTag: wdth, axisNameId: 257, axisOrdering: 1);

        statBuilder.AddAxisValueFormat1(
            axisIndex: 0,
            flags: 0,
            valueNameId: 300,
            value: new Fixed1616(400u << 16));

        statBuilder.AddAxisValueFormat4(
            flags: 0,
            valueNameId: 310,
            records: new[]
            {
                new StatTableBuilder.AxisValueRecord(axisIndex: 0, value: new Fixed1616(700u << 16)),
                new StatTableBuilder.AxisValueRecord(axisIndex: 1, value: new Fixed1616(120u << 16)),
            });

        byte[] head = TestSfntTables.BuildValidHeadTable(unitsPerEm: 1000);

        var sfnt = new SfntBuilder { SfntVersion = 0x00010000u };
        sfnt.SetTable(KnownTags.head, head);
        sfnt.SetTable(statBuilder);
        byte[] originalFontBytes = sfnt.ToArray();

        using var originalFile = SfntFile.FromMemory(originalFontBytes);
        var originalFont = originalFile.GetFont(0);

        Assert.IsTrue(originalFont.TryGetStat(out var originalStat));
        Assert.IsTrue(originalStat.IsSupported);
        Assert.AreEqual((ushort)1, originalStat.MajorVersion);
        Assert.AreEqual((ushort)0, originalStat.MinorVersion);
        Assert.AreEqual((ushort)2, originalStat.DesignAxisCount);
        Assert.AreEqual((ushort)2, originalStat.AxisValueCount);

        Assert.IsTrue(originalStat.TryGetDesignAxisRecord(0, out var axis0));
        Assert.AreEqual(wght.Value, axis0.AxisTag.Value);
        Assert.AreEqual((ushort)256, axis0.AxisNameId);

        Assert.IsTrue(originalStat.TryGetAxisValueTable(0, out var value0));
        Assert.AreEqual((ushort)1, value0.Format);
        Assert.IsTrue(value0.TryGetFormat1(out var f1));
        Assert.AreEqual((ushort)0, f1.AxisIndex);
        Assert.AreEqual((ushort)300, f1.ValueNameId);
        Assert.AreEqual((400u << 16), f1.Value.RawValue);

        Assert.IsTrue(originalStat.TryGetAxisValueTable(1, out var value1));
        Assert.AreEqual((ushort)4, value1.Format);
        Assert.IsTrue(value1.TryGetFormat4(out var f4));
        Assert.AreEqual((ushort)2, f4.AxisValueRecordCount);
        Assert.IsTrue(f4.TryGetAxisValueRecord(1, out var r1));
        Assert.AreEqual((ushort)1, r1.AxisIndex);
        Assert.AreEqual((120u << 16), r1.Value.RawValue);

        Assert.IsTrue(StatTableBuilder.TryFrom(originalStat, out var edit));
        edit.ElidedFallbackNameId = 999;
        edit.ClearAxisValues();
        edit.AddAxisValueFormat3(
            axisIndex: 0,
            flags: 1,
            valueNameId: 400,
            value: new Fixed1616(500u << 16),
            linkedValue: new Fixed1616(600u << 16));

        var editor = new SfntEditor(originalFont);
        editor.SetTable(edit);
        byte[] editedFontBytes = editor.ToArray();

        Assert.AreEqual(0xB1B0AFBAu, OpenTypeChecksum.Compute(editedFontBytes));

        using var editedFile = SfntFile.FromMemory(editedFontBytes);
        var editedFont = editedFile.GetFont(0);
        Assert.IsTrue(editedFont.TryGetStat(out var editedStat));
        Assert.AreEqual((ushort)999, editedStat.ElidedFallbackNameId);
        Assert.AreEqual((ushort)1, editedStat.AxisValueCount);
        Assert.IsTrue(editedStat.TryGetAxisValueTable(0, out var editedValue0));
        Assert.AreEqual((ushort)3, editedValue0.Format);
        Assert.IsTrue(editedValue0.TryGetFormat3(out var f3));
        Assert.AreEqual((ushort)0, f3.AxisIndex);
        Assert.AreEqual((500u << 16), f3.Value.RawValue);
        Assert.AreEqual((600u << 16), f3.LinkedValue.RawValue);
    }
}

