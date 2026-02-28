using System.Buffers.Binary;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OTFontFile2.Tables;

namespace OTFontFile2.Tests.UnitTests;

[TestClass]
public sealed class RawComplexTableWritebackTests
{
    [TestMethod]
    public void FontModel_CanEditRawOtlTablesAndWriteBack()
    {
        byte[] head = TestSfntTables.BuildValidHeadTable(unitsPerEm: 1000);

        var sfnt = new SfntBuilder { SfntVersion = 0x00010000u };
        sfnt.SetTable(KnownTags.head, head);
        sfnt.SetTable(new GdefTableBuilder());
        sfnt.SetTable(new GsubTableBuilder());
        sfnt.SetTable(new GposTableBuilder());

        using var file = SfntFile.FromMemory(sfnt.ToArray());
        var font = file.GetFont(0);

        var model = new FontModel(font);

        Assert.IsTrue(model.TryEdit<GdefTableBuilder>(out var gdef));
        byte[] gdefBytes = gdef.ToArray();
        BinaryPrimitives.WriteUInt32BigEndian(gdefBytes.AsSpan(0, 4), 0x00020000u);
        gdef.SetTableData(gdefBytes);

        Assert.IsTrue(model.TryEdit<GsubTableBuilder>(out var gsub));
        byte[] gsubBytes = gsub.ToArray();
        BinaryPrimitives.WriteUInt32BigEndian(gsubBytes.AsSpan(0, 4), 0x00020000u);
        gsub.SetTableData(gsubBytes);

        Assert.IsTrue(model.TryEdit<GposTableBuilder>(out var gpos));
        byte[] gposBytes = gpos.ToArray();
        BinaryPrimitives.WriteUInt32BigEndian(gposBytes.AsSpan(0, 4), 0x00020000u);
        gpos.SetTableData(gposBytes);

        byte[] editedBytes = model.ToArray();
        Assert.AreEqual(0xB1B0AFBAu, OpenTypeChecksum.Compute(editedBytes));

        using var editedFile = SfntFile.FromMemory(editedBytes);
        var editedFont = editedFile.GetFont(0);

        Assert.IsTrue(editedFont.TryGetGdef(out var editedGdef));
        Assert.AreEqual(0x00020000u, editedGdef.Version.RawValue);

        Assert.IsTrue(editedFont.TryGetGsub(out var editedGsub));
        Assert.AreEqual(0x00020000u, editedGsub.Version.RawValue);

        Assert.IsTrue(editedFont.TryGetGpos(out var editedGpos));
        Assert.AreEqual(0x00020000u, editedGpos.Version.RawValue);
    }

    [TestMethod]
    public void FontModel_CanEditRawCffTablesAndWriteBack()
    {
        byte[] head = TestSfntTables.BuildValidHeadTable(unitsPerEm: 1000);

        var sfnt = new SfntBuilder { SfntVersion = 0x4F54544Fu }; // 'OTTO'
        sfnt.SetTable(KnownTags.head, head);
        sfnt.SetTable(new CffTableBuilder());
        sfnt.SetTable(new Cff2TableBuilder());

        using var file = SfntFile.FromMemory(sfnt.ToArray());
        var font = file.GetFont(0);

        var model = new FontModel(font);

        Assert.IsTrue(model.TryEdit<CffTableBuilder>(out var cff));
        byte[] cffBytes = cff.ToArray();
        cffBytes[1] = 1; // minor
        cff.SetTableData(cffBytes);

        Assert.IsTrue(model.TryEdit<Cff2TableBuilder>(out var cff2));
        byte[] cff2Bytes = cff2.ToArray();
        cff2Bytes[1] = 1; // minor
        cff2.SetTableData(cff2Bytes);

        byte[] editedBytes = model.ToArray();
        Assert.AreEqual(0xB1B0AFBAu, OpenTypeChecksum.Compute(editedBytes));

        using var editedFile = SfntFile.FromMemory(editedBytes);
        var editedFont = editedFile.GetFont(0);

        Assert.IsTrue(editedFont.TryGetCff(out var editedCff));
        Assert.AreEqual((byte)1, editedCff.Minor);

        Assert.IsTrue(editedFont.TryGetCff2(out var editedCff2));
        Assert.AreEqual((byte)1, editedCff2.Minor);
    }

    [TestMethod]
    public void FontModel_CanEditRawGlyfAndLocaAndWriteBack()
    {
        byte[] head = TestSfntTables.BuildValidHeadTable(unitsPerEm: 1000);

        var sfnt = new SfntBuilder { SfntVersion = 0x00010000u };
        sfnt.SetTable(KnownTags.head, head);
        sfnt.SetTable(new GlyfTableBuilder());
        sfnt.SetTable(new LocaTableBuilder());

        using var file = SfntFile.FromMemory(sfnt.ToArray());
        var font = file.GetFont(0);

        var model = new FontModel(font);

        Assert.IsTrue(model.TryEdit<GlyfTableBuilder>(out var glyf));
        Assert.IsTrue(model.TryEdit<LocaTableBuilder>(out var loca));

        glyf.SetTableData(new byte[] { 1, 2 });
        loca.SetTableData(new byte[] { 0, 0, 0, 0 });

        byte[] editedBytes = model.ToArray();
        Assert.AreEqual(0xB1B0AFBAu, OpenTypeChecksum.Compute(editedBytes));

        using var editedFile = SfntFile.FromMemory(editedBytes);
        var editedFont = editedFile.GetFont(0);

        Assert.IsTrue(editedFont.TryGetGlyf(out var editedGlyf));
        Assert.AreEqual(2, editedGlyf.Table.Length);

        Assert.IsTrue(editedFont.TryGetLoca(out var editedLoca));
        Assert.AreEqual(4, editedLoca.Table.Length);
    }
}

