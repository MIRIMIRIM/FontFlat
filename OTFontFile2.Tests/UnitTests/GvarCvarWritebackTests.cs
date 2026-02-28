using System.Buffers.Binary;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OTFontFile2.Tables;

namespace OTFontFile2.Tests.UnitTests;

[TestClass]
public sealed class GvarCvarWritebackTests
{
    [TestMethod]
    public void FontModel_CanEditRawGvarAndWriteBack()
    {
        byte[] head = TestSfntTables.BuildValidHeadTable(unitsPerEm: 1000);

        var gvarBuilder = new GvarTableBuilder();
        var fvarBuilder = new FvarTableBuilder(); // axisCount=0 is valid when gvar has no variations
        var maxpBuilder = new MaxpTableBuilder { NumGlyphs = 0 };

        var sfnt = new SfntBuilder { SfntVersion = 0x00010000u };
        sfnt.SetTable(KnownTags.head, head);
        sfnt.SetTable(fvarBuilder);
        sfnt.SetTable(maxpBuilder);
        sfnt.SetTable(gvarBuilder);

        using var file = SfntFile.FromMemory(sfnt.ToArray());
        var font = file.GetFont(0);

        Assert.IsTrue(font.TryGetGvar(out var originalGvar));
        Assert.AreEqual(0x00010000u, originalGvar.Version.RawValue);

        var model = new FontModel(font);
        Assert.IsTrue(model.TryEdit<GvarTableBuilder>(out var edit));

        byte[] bytes = edit.ToArray();
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(0, 4), 0x00010001u);
        edit.SetTableData(bytes);

        byte[] editedBytes = model.ToArray();
        Assert.AreEqual(0xB1B0AFBAu, OpenTypeChecksum.Compute(editedBytes));

        using var editedFile = SfntFile.FromMemory(editedBytes);
        var editedFont = editedFile.GetFont(0);
        Assert.IsTrue(editedFont.TryGetGvar(out var editedGvar));
        Assert.AreEqual(0x00010001u, editedGvar.Version.RawValue);
    }

    [TestMethod]
    public void FontModel_CanEditRawCvarAndWriteBack()
    {
        byte[] head = TestSfntTables.BuildValidHeadTable(unitsPerEm: 1000);

        var cvarBuilder = new CvarTableBuilder();
        var fvarBuilder = new FvarTableBuilder(); // axisCount=0 is valid when cvar has no variations

        var sfnt = new SfntBuilder { SfntVersion = 0x00010000u };
        sfnt.SetTable(KnownTags.head, head);
        sfnt.SetTable(fvarBuilder);
        sfnt.SetTable(cvarBuilder);

        using var file = SfntFile.FromMemory(sfnt.ToArray());
        var font = file.GetFont(0);

        Assert.IsTrue(font.TryGetCvar(out var originalCvar));
        Assert.AreEqual(0x00010000u, originalCvar.Version.RawValue);

        var model = new FontModel(font);
        Assert.IsTrue(model.TryEdit<CvarTableBuilder>(out var edit));

        byte[] bytes = edit.ToArray();
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(0, 4), 0x00010001u);
        edit.SetTableData(bytes);

        byte[] editedBytes = model.ToArray();
        Assert.AreEqual(0xB1B0AFBAu, OpenTypeChecksum.Compute(editedBytes));

        using var editedFile = SfntFile.FromMemory(editedBytes);
        var editedFont = editedFile.GetFont(0);
        Assert.IsTrue(editedFont.TryGetCvar(out var editedCvar));
        Assert.AreEqual(0x00010001u, editedCvar.Version.RawValue);
    }
}
