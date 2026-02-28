using Microsoft.VisualStudio.TestTools.UnitTesting;
using OTFontFile2.Tables;

namespace OTFontFile2.Tests.UnitTests;

[TestClass]
public sealed class TtfaTableTests
{
    [TestMethod]
    public void TtfaTable_CanBuildAndParse_StandaloneSlice()
    {
        var builder = new TtfaTableBuilder();
        builder.SetAsciiString("--foo --bar=baz");

        byte[] tableBytes = BuildTableBytes(builder);

        Assert.IsTrue(Tag.TryParse("TTFA", out var tag));
        Assert.IsTrue(TableSlice.TryCreateStandalone(tag, tableBytes, out var slice));
        Assert.IsTrue(TtfaTable.TryCreate(slice, out var ttfa));

        Assert.AreEqual("--foo --bar=baz", ttfa.GetAsciiString());
    }

    private static byte[] BuildTableBytes(ISfntTableSource source)
    {
        using var ms = new MemoryStream(source.Length);
        source.WriteTo(ms, headCheckSumAdjustment: 0);
        return ms.ToArray();
    }
}

