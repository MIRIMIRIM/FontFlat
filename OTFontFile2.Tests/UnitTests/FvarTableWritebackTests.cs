using Microsoft.VisualStudio.TestTools.UnitTesting;
using OTFontFile2.Tables;

namespace OTFontFile2.Tests.UnitTests;

[TestClass]
public sealed class FvarTableWritebackTests
{
    [TestMethod]
    public void FvarTable_CanEditAndWriteBack_WithSfntEditor()
    {
        Assert.IsTrue(Tag.TryParse("wght", out var wght));
        Assert.IsTrue(Tag.TryParse("wdth", out var wdth));

        var fvarBuilder = new FvarTableBuilder
        {
            Version = new Fixed1616(0x00010000u),
            WritePostScriptNameId = true
        };

        fvarBuilder.AddAxis(
            axisTag: wght,
            minValue: new Fixed1616(100u << 16),
            defaultValue: new Fixed1616(400u << 16),
            maxValue: new Fixed1616(900u << 16),
            flags: 0,
            axisNameId: 256);

        fvarBuilder.AddAxis(
            axisTag: wdth,
            minValue: new Fixed1616(50u << 16),
            defaultValue: new Fixed1616(100u << 16),
            maxValue: new Fixed1616(200u << 16),
            flags: 0,
            axisNameId: 257);

        fvarBuilder.AddInstance(
            subfamilyNameId: 300,
            flags: 0,
            coordinates: new[]
            {
                new Fixed1616(400u << 16), // wght
                new Fixed1616(100u << 16), // wdth
            },
            postScriptNameId: 301);

        byte[] head = TestSfntTables.BuildValidHeadTable(unitsPerEm: 1000);

        var sfnt = new SfntBuilder { SfntVersion = 0x00010000u };
        sfnt.SetTable(KnownTags.head, head);
        sfnt.SetTable(fvarBuilder);
        byte[] originalFontBytes = sfnt.ToArray();

        using var originalFile = SfntFile.FromMemory(originalFontBytes);
        var originalFont = originalFile.GetFont(0);

        Assert.IsTrue(originalFont.TryGetFvar(out var originalFvar));
        Assert.AreEqual(0x00010000u, originalFvar.Version.RawValue);
        Assert.AreEqual((ushort)2, originalFvar.AxisCount);
        Assert.AreEqual((ushort)1, originalFvar.InstanceCount);

        Assert.IsTrue(originalFvar.TryGetAxisRecord(0, out var axis0));
        Assert.AreEqual(wght.Value, axis0.AxisTag.Value);
        Assert.AreEqual((100u << 16), axis0.MinValue.RawValue);
        Assert.AreEqual((400u << 16), axis0.DefaultValue.RawValue);
        Assert.AreEqual((900u << 16), axis0.MaxValue.RawValue);
        Assert.AreEqual((ushort)256, axis0.AxisNameId);

        Assert.IsTrue(originalFvar.TryGetInstanceRecord(0, out var inst0));
        Assert.AreEqual((ushort)300, inst0.SubfamilyNameId);
        Assert.IsTrue(inst0.TryGetCoordinate(0, out var w0));
        Assert.IsTrue(inst0.TryGetCoordinate(1, out var wd0));
        Assert.AreEqual((400u << 16), w0.RawValue);
        Assert.AreEqual((100u << 16), wd0.RawValue);
        Assert.IsTrue(inst0.TryGetPostScriptNameId(out ushort psId0));
        Assert.AreEqual((ushort)301, psId0);

        Assert.IsTrue(FvarTableBuilder.TryFrom(originalFvar, out var edit));
        edit.ClearInstances();
        edit.AddInstance(
            subfamilyNameId: 310,
            flags: 0,
            coordinates: new[]
            {
                new Fixed1616(700u << 16),
                new Fixed1616(120u << 16),
            },
            postScriptNameId: 311);

        var editor = new SfntEditor(originalFont);
        editor.SetTable(edit);
        byte[] editedFontBytes = editor.ToArray();

        Assert.AreEqual(0xB1B0AFBAu, OpenTypeChecksum.Compute(editedFontBytes));

        using var editedFile = SfntFile.FromMemory(editedFontBytes);
        var editedFont = editedFile.GetFont(0);
        Assert.IsTrue(editedFont.TryGetFvar(out var editedFvar));
        Assert.AreEqual((ushort)2, editedFvar.AxisCount);
        Assert.AreEqual((ushort)1, editedFvar.InstanceCount);
        Assert.IsTrue(editedFvar.TryGetInstanceRecord(0, out var editedInst0));
        Assert.AreEqual((ushort)310, editedInst0.SubfamilyNameId);
        Assert.IsTrue(editedInst0.TryGetCoordinate(0, out var ew0));
        Assert.IsTrue(editedInst0.TryGetCoordinate(1, out var ewd0));
        Assert.AreEqual((700u << 16), ew0.RawValue);
        Assert.AreEqual((120u << 16), ewd0.RawValue);
        Assert.IsTrue(editedInst0.TryGetPostScriptNameId(out ushort epsId0));
        Assert.AreEqual((ushort)311, epsId0);
    }
}

