using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OTFontFile2.Tables;

namespace OTFontFile2.Tests.UnitTests;

[TestClass]
public sealed class FftmAndDebgTablesTests
{
    [TestMethod]
    public void FftmTable_CanBuildAndParse_StandaloneSlice()
    {
        var builder = new FftmTableBuilder
        {
            Version = 1,
            FFTimeStamp = 123,
            SourceCreated = 456,
            SourceModified = 789
        };

        byte[] tableBytes = BuildTableBytes(builder);

        Assert.IsTrue(Tag.TryParse("FFTM", out var tag));
        Assert.IsTrue(TableSlice.TryCreateStandalone(tag, tableBytes, out var slice));
        Assert.IsTrue(FftmTable.TryCreate(slice, out var fftm));

        Assert.AreEqual((uint)1, fftm.Version);
        Assert.AreEqual((ulong)123, fftm.FFTimeStamp);
        Assert.AreEqual((ulong)456, fftm.SourceCreated);
        Assert.AreEqual((ulong)789, fftm.SourceModified);
    }

    [TestMethod]
    public void DebgTable_CanBuildAndParse_StandaloneSlice()
    {
        const string json = "{\"a\":1}";

        var builder = new DebgTableBuilder();
        builder.SetJsonString(json);

        byte[] tableBytes = BuildTableBytes(builder);

        Assert.IsTrue(Tag.TryParse("Debg", out var tag));
        Assert.IsTrue(TableSlice.TryCreateStandalone(tag, tableBytes, out var slice));
        Assert.IsTrue(DebgTable.TryCreate(slice, out var debg));

        Assert.AreEqual(json, Encoding.UTF8.GetString(debg.JsonUtf8));
    }

    private static byte[] BuildTableBytes(ISfntTableSource source)
    {
        using var ms = new MemoryStream(source.Length);
        source.WriteTo(ms, headCheckSumAdjustment: 0);
        return ms.ToArray();
    }
}

