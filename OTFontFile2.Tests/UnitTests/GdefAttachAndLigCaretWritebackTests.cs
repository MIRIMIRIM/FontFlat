using Microsoft.VisualStudio.TestTools.UnitTesting;
using OTFontFile2.Tables;

namespace OTFontFile2.Tests.UnitTests;

[TestClass]
public sealed class GdefAttachAndLigCaretWritebackTests
{
    [TestMethod]
    public void FontModel_CanEditGdefAttachListAndLigCaretListAndWriteBack()
    {
        var attach = new GdefAttachListBuilder();
        attach.AddOrReplace(glyphId: 5, pointIndices: new ushort[] { 1, 2, 2, 5 });

        var lig = new GdefLigCaretListBuilder();
        lig.AddOrReplace(
            ligGlyphId: 10,
            carets: new[]
            {
                GdefLigCaretListBuilder.CaretValue.CoordinateValue(100),
                GdefLigCaretListBuilder.CaretValue.PointIndexValue(3),
            });

        var gdefBuilder = new GdefTableBuilder();
        gdefBuilder.Clear();
        gdefBuilder.SetAttachList(attach);
        gdefBuilder.SetLigCaretList(lig);

        byte[] head = TestSfntTables.BuildValidHeadTable(unitsPerEm: 1000);

        var sfnt = new SfntBuilder { SfntVersion = 0x00010000u };
        sfnt.SetTable(KnownTags.head, head);
        sfnt.SetTable(gdefBuilder);

        using var file = SfntFile.FromMemory(sfnt.ToArray());
        var font = file.GetFont(0);

        Assert.IsTrue(font.TryGetGdef(out var originalGdef));
        Assert.AreEqual(0x00010000u, originalGdef.Version.RawValue);

        Assert.IsTrue(originalGdef.TryGetAttachList(out var attachList));
        Assert.IsTrue(attachList.TryGetAttachPointTableForGlyph(glyphId: 5, out bool covered5, out var ap5));
        Assert.IsTrue(covered5);
        Assert.AreEqual((ushort)3, ap5.PointCount);
        Assert.IsTrue(ap5.TryGetPointIndex(0, out ushort p0));
        Assert.IsTrue(ap5.TryGetPointIndex(1, out ushort p1));
        Assert.IsTrue(ap5.TryGetPointIndex(2, out ushort p2));
        CollectionAssert.AreEqual(new ushort[] { 1, 2, 5 }, new[] { p0, p1, p2 });

        Assert.IsTrue(originalGdef.TryGetLigCaretList(out var ligCaretList));
        Assert.IsTrue(ligCaretList.TryGetLigGlyphTableForGlyph(glyphId: 10, out bool covered10, out var lg10));
        Assert.IsTrue(covered10);
        Assert.AreEqual((ushort)2, lg10.CaretCount);
        Assert.IsTrue(lg10.TryGetCaretValueTable(0, out var cv0));
        Assert.IsTrue(cv0.TryGetCoordinate(out short c0));
        Assert.AreEqual((short)100, c0);
        Assert.IsTrue(lg10.TryGetCaretValueTable(1, out var cv1));
        Assert.IsTrue(cv1.TryGetCaretValuePoint(out ushort pt1));
        Assert.AreEqual((ushort)3, pt1);

        var model = new FontModel(font);
        Assert.IsTrue(model.TryEdit<GdefTableBuilder>(out var edit));

        var newAttach = new GdefAttachListBuilder();
        newAttach.AddOrReplace(glyphId: 6, pointIndices: new ushort[] { 7 });
        edit.SetAttachList(newAttach);

        var newLig = new GdefLigCaretListBuilder();
        newLig.AddOrReplace(ligGlyphId: 10, carets: new[] { GdefLigCaretListBuilder.CaretValue.CoordinateValue(200) });
        edit.SetLigCaretList(newLig);

        byte[] editedBytes = model.ToArray();
        Assert.AreEqual(0xB1B0AFBAu, OpenTypeChecksum.Compute(editedBytes));

        using var editedFile = SfntFile.FromMemory(editedBytes);
        var editedFont = editedFile.GetFont(0);

        Assert.IsTrue(editedFont.TryGetGdef(out var editedGdef));
        Assert.IsTrue(editedGdef.TryGetAttachList(out var editedAttach));
        Assert.IsTrue(editedAttach.TryGetAttachPointTableForGlyph(glyphId: 5, out bool editedCovered5, out _));
        Assert.IsFalse(editedCovered5);
        Assert.IsTrue(editedAttach.TryGetAttachPointTableForGlyph(glyphId: 6, out bool editedCovered6, out var ap6));
        Assert.IsTrue(editedCovered6);
        Assert.AreEqual((ushort)1, ap6.PointCount);
        Assert.IsTrue(ap6.TryGetPointIndex(0, out ushort editedPoint));
        Assert.AreEqual((ushort)7, editedPoint);

        Assert.IsTrue(editedGdef.TryGetLigCaretList(out var editedLigCaret));
        Assert.IsTrue(editedLigCaret.TryGetLigGlyphTableForGlyph(glyphId: 10, out bool editedCovered10, out var editedLg10));
        Assert.IsTrue(editedCovered10);
        Assert.AreEqual((ushort)1, editedLg10.CaretCount);
        Assert.IsTrue(editedLg10.TryGetCaretValueTable(0, out var editedCv0));
        Assert.IsTrue(editedCv0.TryGetCoordinate(out short editedCoord));
        Assert.AreEqual((short)200, editedCoord);
    }
}

