using Microsoft.VisualStudio.TestTools.UnitTesting;
using OTFontFile2.Tables;

namespace OTFontFile2.Tests.UnitTests;

[TestClass]
public sealed class EbscTableWritebackTests
{
    [TestMethod]
    public void EbscTable_CanEditAndWriteBack_WithSfntEditor()
    {
        var ebscBuilder = new EbscTableBuilder
        {
            Version = new Fixed1616(0x00020000u)
        };

        var hori = new SbitLineMetricsData(
            ascender: 1,
            descender: -2,
            widthMax: 3,
            caretSlopeNumerator: 4,
            caretSlopeDenominator: 5,
            caretOffset: 6,
            minOriginSb: -7,
            minAdvanceSb: -8,
            maxBeforeBl: 9,
            minAfterBl: -10);

        var vert = new SbitLineMetricsData(
            ascender: 11,
            descender: -12,
            widthMax: 13,
            caretSlopeNumerator: 14,
            caretSlopeDenominator: 15,
            caretOffset: 16,
            minOriginSb: -17,
            minAdvanceSb: -18,
            maxBeforeBl: 19,
            minAfterBl: -20);

        ebscBuilder.AddScale(hori, vert, ppemX: 12, ppemY: 13, substitutePpemX: 14, substitutePpemY: 15);

        byte[] head = TestSfntTables.BuildValidHeadTable(unitsPerEm: 1000);

        var sfnt = new SfntBuilder { SfntVersion = 0x00010000u };
        sfnt.SetTable(KnownTags.head, head);
        sfnt.SetTable(ebscBuilder);
        byte[] originalFontBytes = sfnt.ToArray();

        using var originalFile = SfntFile.FromMemory(originalFontBytes);
        var originalFont = originalFile.GetFont(0);

        Assert.IsTrue(originalFont.TryGetEbsc(out var originalEbsc));
        Assert.AreEqual(0x00020000u, originalEbsc.Version.RawValue);
        Assert.AreEqual(1u, originalEbsc.SizeCount);
        Assert.IsTrue(originalEbsc.TryGetBitmapScale(0, out var scale));
        Assert.AreEqual((sbyte)1, scale.Hori.Ascender);
        Assert.AreEqual((sbyte)-2, scale.Hori.Descender);
        Assert.AreEqual((byte)3, scale.Hori.WidthMax);
        Assert.AreEqual((byte)12, scale.PpemX);
        Assert.AreEqual((byte)15, scale.SubstitutePpemY);

        Assert.IsTrue(EbscTableBuilder.TryFrom(originalEbsc, out var edit));
        edit.Clear();

        var newHori = new SbitLineMetricsData(
            ascender: 21,
            descender: -22,
            widthMax: 23,
            caretSlopeNumerator: 24,
            caretSlopeDenominator: 25,
            caretOffset: 26,
            minOriginSb: -27,
            minAdvanceSb: -28,
            maxBeforeBl: 29,
            minAfterBl: -30);

        var newVert = new SbitLineMetricsData(
            ascender: 31,
            descender: -32,
            widthMax: 33,
            caretSlopeNumerator: 34,
            caretSlopeDenominator: 35,
            caretOffset: 36,
            minOriginSb: -37,
            minAdvanceSb: -38,
            maxBeforeBl: 39,
            minAfterBl: -40);

        edit.AddScale(newHori, newVert, ppemX: 22, ppemY: 23, substitutePpemX: 24, substitutePpemY: 25);

        var editor = new SfntEditor(originalFont);
        editor.SetTable(edit);
        byte[] editedFontBytes = editor.ToArray();

        Assert.AreEqual(0xB1B0AFBAu, OpenTypeChecksum.Compute(editedFontBytes));

        using var editedFile = SfntFile.FromMemory(editedFontBytes);
        var editedFont = editedFile.GetFont(0);

        Assert.IsTrue(editedFont.TryGetEbsc(out var editedEbsc));
        Assert.AreEqual(1u, editedEbsc.SizeCount);
        Assert.IsTrue(editedEbsc.TryGetBitmapScale(0, out var editedScale));
        Assert.AreEqual((sbyte)21, editedScale.Hori.Ascender);
        Assert.AreEqual((byte)23, editedScale.Hori.WidthMax);
        Assert.AreEqual((byte)22, editedScale.PpemX);
        Assert.AreEqual((byte)25, editedScale.SubstitutePpemY);
    }
}

