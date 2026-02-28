using Microsoft.VisualStudio.TestTools.UnitTesting;
using OTFontFile2.Tables;

namespace OTFontFile2.Tests.UnitTests;

[TestClass]
public sealed class GlyphletsTablesTests
{
    [TestMethod]
    public void SingTable_CanBuildAndParse()
    {
        var builder = new SingTableBuilder
        {
            TableVersionMajor = 1,
            TableVersionMinor = 0,
            GlyphletVersion = 2,
            Permissions = -1,
            MainGid = 42,
            UnitsPerEm = 1000,
            VertAdvance = 500,
            VertOrigin = -20
        };

        builder.SetUniqueNameBytes(System.Text.Encoding.ASCII.GetBytes("Unique"));
        builder.SetMetaMd5Bytes(new byte[16]);
        builder.SetBaseGlyphNameString("A");

        byte[] tableBytes = BuildTableBytes(builder);

        Assert.IsTrue(Tag.TryParse("SING", out var tag));
        Assert.IsTrue(TableSlice.TryCreateStandalone(tag, tableBytes, out var slice));
        Assert.IsTrue(SingTable.TryCreate(slice, out var sing));

        Assert.AreEqual((ushort)1, sing.TableVersionMajor);
        Assert.AreEqual((ushort)0, sing.TableVersionMinor);
        Assert.AreEqual((ushort)2, sing.GlyphletVersion);
        Assert.AreEqual((short)-1, sing.Permissions);
        Assert.AreEqual((ushort)42, sing.MainGid);
        Assert.AreEqual((ushort)1000, sing.UnitsPerEm);
        Assert.AreEqual((short)500, sing.VertAdvance);
        Assert.AreEqual((short)-20, sing.VertOrigin);

        Assert.IsTrue(sing.TryGetBaseGlyphNameString(out string baseName));
        Assert.AreEqual("A", baseName);
    }

    [TestMethod]
    public void GmapTable_CanBuildAndParse()
    {
        var builder = new GmapTableBuilder
        {
            TableVersionMajor = 1,
            TableVersionMinor = 0,
            Flags = 0
        };

        builder.SetPsFontNameString("TestPS");
        builder.AddRecord(unicodeValue: 0x41, cid: 1, gid: 2, glyphletGid: 3, name: "A");

        byte[] tableBytes = BuildTableBytes(builder);

        Assert.IsTrue(Tag.TryParse("GMAP", out var tag));
        Assert.IsTrue(TableSlice.TryCreateStandalone(tag, tableBytes, out var slice));
        Assert.IsTrue(GmapTable.TryCreate(slice, out var gmap));

        Assert.IsTrue(gmap.TryGetPsFontNameString(out string psName));
        Assert.AreEqual("TestPS", psName);

        Assert.AreEqual((ushort)1, gmap.RecordCount);
        Assert.IsTrue(gmap.TryGetRecord(0, out var record));
        Assert.AreEqual((uint)0x41, record.UnicodeValue);
        Assert.AreEqual((ushort)1, record.Cid);
        Assert.AreEqual((ushort)2, record.Gid);
        Assert.AreEqual((ushort)3, record.GlyphletGid);
        Assert.AreEqual("A", record.GetNameString());
    }

    [TestMethod]
    public void GpkgTable_CanBuildAndParse()
    {
        byte[] gmapPayload = { 1, 2, 3, 4 };
        byte[] glyphletPayload = { 0xAA, 0xBB };

        var builder = new GpkgTableBuilder
        {
            Version = 1,
            Flags = 0
        };

        builder.AddGmap(gmapPayload);
        builder.AddGlyphlet(glyphletPayload);

        byte[] tableBytes = BuildTableBytes(builder);

        Assert.IsTrue(Tag.TryParse("GPKG", out var tag));
        Assert.IsTrue(TableSlice.TryCreateStandalone(tag, tableBytes, out var slice));
        Assert.IsTrue(GpkgTable.TryCreate(slice, out var gpkg));

        Assert.AreEqual((ushort)1, gpkg.Version);
        Assert.AreEqual((ushort)0, gpkg.Flags);
        Assert.AreEqual((ushort)1, gpkg.GmapCount);
        Assert.AreEqual((ushort)1, gpkg.GlyphletCount);

        Assert.IsTrue(gpkg.TryGetGmapData(0, out var gmap));
        CollectionAssert.AreEqual(gmapPayload, gmap.ToArray());

        Assert.IsTrue(gpkg.TryGetGlyphletData(0, out var glyphlet));
        CollectionAssert.AreEqual(glyphletPayload, glyphlet.ToArray());
    }

    private static byte[] BuildTableBytes(ISfntTableSource source)
    {
        using var ms = new MemoryStream(source.Length);
        source.WriteTo(ms, headCheckSumAdjustment: 0);
        return ms.ToArray();
    }
}
