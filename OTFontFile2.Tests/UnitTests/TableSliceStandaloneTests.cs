using System.Buffers.Binary;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OTFontFile2.Tables;

namespace OTFontFile2.Tests.UnitTests;

[TestClass]
public sealed class TableSliceStandaloneTests
{
    [TestMethod]
    public void TryCreateStandalone_ComputesHeadDirectoryChecksum()
    {
        byte[] head = TestSfntTables.BuildValidHeadTable(unitsPerEm: 1000);
        BinaryPrimitives.WriteUInt32BigEndian(head.AsSpan().Slice(8, 4), 0x12345678u);

        Assert.IsTrue(Tag.TryParse("head", out var headTag));
        Assert.IsTrue(TableSlice.TryCreateStandalone(headTag, head, out var slice));

        Assert.AreEqual(headTag, slice.Tag);
        Assert.AreEqual(head.Length, slice.Length);
        CollectionAssert.AreEqual(head, slice.Span.ToArray());
        Assert.AreEqual(OpenTypeChecksum.ComputeHeadDirectoryChecksum(head), slice.DirectoryChecksum);
    }

    [TestMethod]
    public void TryCreateStandalone_CanParseNameTable()
    {
        var nameBuilder = new NameTableBuilder(format: 0);
        nameBuilder.AddOrReplaceString(
            platformId: (ushort)NameTable.PlatformId.Windows,
            encodingId: 1,
            languageId: 0x0409,
            nameId: (ushort)NameTable.NameId.FamilyName,
            value: "Test");

        byte[] nameBytes = BuildTableBytes(nameBuilder);

        Assert.IsTrue(Tag.TryParse("name", out var nameTag));
        Assert.IsTrue(TableSlice.TryCreateStandalone(nameTag, nameBytes, out var slice));
        Assert.AreEqual(OpenTypeChecksum.Compute(nameBytes), slice.DirectoryChecksum);

        Assert.IsTrue(NameTable.TryCreate(slice, out var name));
        string? value = name.GetString(
            platformId: (ushort)NameTable.PlatformId.Windows,
            encId: 1,
            langId: 0x0409,
            nameId: (ushort)NameTable.NameId.FamilyName);

        Assert.AreEqual("Test", value);
    }

    private static byte[] BuildTableBytes(ISfntTableSource source)
    {
        using var ms = new MemoryStream(source.Length);
        source.WriteTo(ms, headCheckSumAdjustment: 0);
        return ms.ToArray();
    }
}

