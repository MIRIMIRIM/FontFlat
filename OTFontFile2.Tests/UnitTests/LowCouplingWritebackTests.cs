using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OTFontFile2.Tables;

namespace OTFontFile2.Tests.UnitTests;

[TestClass]
public sealed class LowCouplingWritebackTests
{
    [TestMethod]
    public void GaspTable_CanEditAndWriteBack_WithSfntEditor()
    {
        var gaspBuilder = new GaspTableBuilder { Version = 1 };
        gaspBuilder.AddOrReplaceRange(8, GaspTable.GaspBehavior.Gridfit | GaspTable.GaspBehavior.DoGray);
        gaspBuilder.AddOrReplaceRange(16, GaspTable.GaspBehavior.Gridfit);

        byte[] head = TestSfntTables.BuildValidHeadTable(unitsPerEm: 1000);

        var sfnt = new SfntBuilder { SfntVersion = 0x00010000u };
        sfnt.SetTable(KnownTags.head, head);
        sfnt.SetTable(gaspBuilder);
        byte[] originalFontBytes = sfnt.ToArray();

        using var originalFile = SfntFile.FromMemory(originalFontBytes);
        var originalFont = originalFile.GetFont(0);
        Assert.IsTrue(originalFont.TryGetGasp(out var originalGasp));
        Assert.AreEqual(gaspBuilder.Version, originalGasp.Version);
        Assert.AreEqual(2, originalGasp.RangeCount);
        Assert.AreEqual(
            GaspTable.GaspBehavior.Gridfit | GaspTable.GaspBehavior.DoGray,
            originalGasp.GetBehaviorForPpem(8));

        Assert.IsTrue(GaspTableBuilder.TryFrom(originalGasp, out var edit));
        edit.AddOrReplaceRange(8, GaspTable.GaspBehavior.SymmetricGridfit | GaspTable.GaspBehavior.SymmetricSmoothing);
        edit.AddOrReplaceRange(32, GaspTable.GaspBehavior.SymmetricSmoothing);

        var editor = new SfntEditor(originalFont);
        editor.SetTable(edit);
        byte[] editedFontBytes = editor.ToArray();

        Assert.AreEqual(0xB1B0AFBAu, OpenTypeChecksum.Compute(editedFontBytes));

        using var editedFile = SfntFile.FromMemory(editedFontBytes);
        var editedFont = editedFile.GetFont(0);
        Assert.IsTrue(editedFont.TryGetGasp(out var editedGasp));
        Assert.AreEqual(3, editedGasp.RangeCount);
        Assert.AreEqual(
            GaspTable.GaspBehavior.SymmetricGridfit | GaspTable.GaspBehavior.SymmetricSmoothing,
            editedGasp.GetBehaviorForPpem(8));
    }

    [TestMethod]
    public void MetaTable_CanEditAndWriteBack_WithSfntEditor()
    {
        Assert.IsTrue(Tag.TryParse("dlng", out var dlng));
        Assert.IsTrue(Tag.TryParse("test", out var test));
        Assert.IsTrue(Tag.TryParse("more", out var more));

        var metaBuilder = new MetaTableBuilder { Version = 1, Flags = 0 };
        metaBuilder.AddOrReplaceUtf8String(dlng, "en");
        metaBuilder.AddOrReplaceUtf8String(test, "hello");

        byte[] head = TestSfntTables.BuildValidHeadTable(unitsPerEm: 1000);

        var sfnt = new SfntBuilder { SfntVersion = 0x00010000u };
        sfnt.SetTable(KnownTags.head, head);
        sfnt.SetTable(metaBuilder);
        byte[] originalFontBytes = sfnt.ToArray();

        using var originalFile = SfntFile.FromMemory(originalFontBytes);
        var originalFont = originalFile.GetFont(0);
        Assert.IsTrue(originalFont.TryGetMeta(out var originalMeta));

        Assert.IsTrue(originalMeta.TryFindDataMap(dlng, out var dlngMap));
        Assert.IsTrue(originalMeta.TryGetDataSpan(dlngMap, out var dlngBytes));
        Assert.AreEqual("en", Encoding.UTF8.GetString(dlngBytes));

        Assert.IsTrue(MetaTableBuilder.TryFrom(originalMeta, out var edit));
        edit.AddOrReplaceUtf8String(dlng, "zh");
        edit.AddOrReplaceUtf8String(more, "world");

        var editor = new SfntEditor(originalFont);
        editor.SetTable(edit);
        byte[] editedFontBytes = editor.ToArray();

        Assert.AreEqual(0xB1B0AFBAu, OpenTypeChecksum.Compute(editedFontBytes));

        using var editedFile = SfntFile.FromMemory(editedFontBytes);
        var editedFont = editedFile.GetFont(0);
        Assert.IsTrue(editedFont.TryGetMeta(out var editedMeta));

        Assert.IsTrue(editedMeta.TryFindDataMap(dlng, out var editedDlngMap));
        Assert.IsTrue(editedMeta.TryGetDataSpan(editedDlngMap, out var editedDlngBytes));
        Assert.AreEqual("zh", Encoding.UTF8.GetString(editedDlngBytes));

        Assert.IsTrue(editedMeta.TryFindDataMap(more, out var moreMap));
        Assert.IsTrue(editedMeta.TryGetDataSpan(moreMap, out var moreBytes));
        Assert.AreEqual("world", Encoding.UTF8.GetString(moreBytes));
    }

    [TestMethod]
    public void LtagTable_CanEditAndWriteBack_WithSfntEditor()
    {
        var ltagBuilder = new LtagTableBuilder { Version = 1, Flags = 0 };
        ltagBuilder.AddOrGetIndex("en");
        ltagBuilder.AddOrGetIndex("zh-Hans");

        byte[] head = TestSfntTables.BuildValidHeadTable(unitsPerEm: 1000);

        var sfnt = new SfntBuilder { SfntVersion = 0x00010000u };
        sfnt.SetTable(KnownTags.head, head);
        sfnt.SetTable(ltagBuilder);
        byte[] originalFontBytes = sfnt.ToArray();

        using var originalFile = SfntFile.FromMemory(originalFontBytes);
        var originalFont = originalFile.GetFont(0);
        Assert.IsTrue(originalFont.TryGetLtag(out var originalLtag));
        Assert.AreEqual((uint)1, originalLtag.Version);
        Assert.AreEqual((uint)2, originalLtag.TagCount);
        Assert.AreEqual("en", originalLtag.GetLanguageTagString(0));
        Assert.AreEqual("zh-Hans", originalLtag.GetLanguageTagString(1));

        Assert.IsTrue(LtagTableBuilder.TryFrom(originalLtag, out var edit));
        edit.AddOrGetIndex("ja");
        edit.Remove("en");

        var editor = new SfntEditor(originalFont);
        editor.SetTable(edit);
        byte[] editedFontBytes = editor.ToArray();

        Assert.AreEqual(0xB1B0AFBAu, OpenTypeChecksum.Compute(editedFontBytes));

        using var editedFile = SfntFile.FromMemory(editedFontBytes);
        var editedFont = editedFile.GetFont(0);
        Assert.IsTrue(editedFont.TryGetLtag(out var editedLtag));
        Assert.AreEqual((uint)2, editedLtag.TagCount);
        Assert.AreEqual("zh-Hans", editedLtag.GetLanguageTagString(0));
        Assert.AreEqual("ja", editedLtag.GetLanguageTagString(1));
    }

    [TestMethod]
    public void CvtFpgmPrepTables_CanEditAndWriteBack_WithSfntEditor()
    {
        var cvtBuilder = new CvtTableBuilder();
        cvtBuilder.AddValue(0);
        cvtBuilder.AddValue(12);
        cvtBuilder.AddValue(-34);

        var fpgmBuilder = new FpgmTableBuilder();
        fpgmBuilder.SetProgram(new byte[] { 0xB0, 0x00, 0x2C });

        var prepBuilder = new PrepTableBuilder();
        prepBuilder.SetProgram(new byte[] { 0xB0, 0x01, 0x2C });

        byte[] head = TestSfntTables.BuildValidHeadTable(unitsPerEm: 1000);

        var sfnt = new SfntBuilder { SfntVersion = 0x00010000u };
        sfnt.SetTable(KnownTags.head, head);
        sfnt.SetTable(cvtBuilder);
        sfnt.SetTable(fpgmBuilder);
        sfnt.SetTable(prepBuilder);
        byte[] originalFontBytes = sfnt.ToArray();

        using var originalFile = SfntFile.FromMemory(originalFontBytes);
        var originalFont = originalFile.GetFont(0);

        Assert.IsTrue(originalFont.TryGetCvt(out var originalCvt));
        Assert.AreEqual(3, originalCvt.ValueCount);
        Assert.IsTrue(originalCvt.TryGetValue(2, out short v));
        Assert.AreEqual(-34, v);

        Assert.IsTrue(originalFont.TryGetFpgm(out var originalFpgm));
        CollectionAssert.AreEqual(new byte[] { 0xB0, 0x00, 0x2C }, originalFpgm.Program.ToArray());

        Assert.IsTrue(originalFont.TryGetPrep(out var originalPrep));
        CollectionAssert.AreEqual(new byte[] { 0xB0, 0x01, 0x2C }, originalPrep.Program.ToArray());

        Assert.IsTrue(CvtTableBuilder.TryFrom(originalCvt, out var editCvt));
        editCvt.SetValue(1, 99);

        Assert.IsTrue(FpgmTableBuilder.TryFrom(originalFpgm, out var editFpgm));
        editFpgm.SetProgram(new byte[] { 0xB0, 0x02, 0x2C });

        Assert.IsTrue(PrepTableBuilder.TryFrom(originalPrep, out var editPrep));
        editPrep.SetProgram(new byte[] { 0xB0, 0x03, 0x2C });

        var editor = new SfntEditor(originalFont);
        editor.SetTable(editCvt);
        editor.SetTable(editFpgm);
        editor.SetTable(editPrep);
        byte[] editedFontBytes = editor.ToArray();

        Assert.AreEqual(0xB1B0AFBAu, OpenTypeChecksum.Compute(editedFontBytes));

        using var editedFile = SfntFile.FromMemory(editedFontBytes);
        var editedFont = editedFile.GetFont(0);

        Assert.IsTrue(editedFont.TryGetCvt(out var editedCvt));
        Assert.IsTrue(editedCvt.TryGetValue(1, out short editedValue));
        Assert.AreEqual(99, editedValue);

        Assert.IsTrue(editedFont.TryGetFpgm(out var editedFpgm));
        CollectionAssert.AreEqual(new byte[] { 0xB0, 0x02, 0x2C }, editedFpgm.Program.ToArray());

        Assert.IsTrue(editedFont.TryGetPrep(out var editedPrep));
        CollectionAssert.AreEqual(new byte[] { 0xB0, 0x03, 0x2C }, editedPrep.Program.ToArray());
    }

    [TestMethod]
    public void PcltTable_CanEditAndWriteBack_WithSfntEditor()
    {
        var pcltBuilder = new PcltTableBuilder
        {
            FontNumber = 1234,
            Pitch = 10,
            XHeight = 20,
            Style = 0,
            TypeFamily = 0,
            CapHeight = 30,
            SymbolSet = 0,
            StrokeWeight = 1,
            WidthType = 2,
            SerifStyle = 3,
            Reserved = 0
        };
        pcltBuilder.SetTypefaceString("Typeface");
        pcltBuilder.SetCharacterComplementString("ABC");
        pcltBuilder.SetFileNameString("file");

        byte[] head = TestSfntTables.BuildValidHeadTable(unitsPerEm: 1000);

        var sfnt = new SfntBuilder { SfntVersion = 0x00010000u };
        sfnt.SetTable(KnownTags.head, head);
        sfnt.SetTable(pcltBuilder);
        byte[] originalFontBytes = sfnt.ToArray();

        using var originalFile = SfntFile.FromMemory(originalFontBytes);
        var originalFont = originalFile.GetFont(0);
        Assert.IsTrue(originalFont.TryGetPclt(out var originalPclt));
        Assert.AreEqual((uint)1234, originalPclt.FontNumber);
        Assert.AreEqual("Typeface", originalPclt.GetTypefaceString());
        Assert.AreEqual("file", originalPclt.GetFileNameString());

        Assert.IsTrue(PcltTableBuilder.TryFrom(originalPclt, out var edit));
        edit.Pitch = 77;
        edit.SetFileNameString("new");

        var editor = new SfntEditor(originalFont);
        editor.SetTable(edit);
        byte[] editedFontBytes = editor.ToArray();

        Assert.AreEqual(0xB1B0AFBAu, OpenTypeChecksum.Compute(editedFontBytes));

        using var editedFile = SfntFile.FromMemory(editedFontBytes);
        var editedFont = editedFile.GetFont(0);
        Assert.IsTrue(editedFont.TryGetPclt(out var editedPclt));
        Assert.AreEqual((ushort)77, editedPclt.Pitch);
        Assert.AreEqual("new", editedPclt.GetFileNameString());
    }

    [TestMethod]
    public void DsigTable_CanEditAndWriteBack_WithSfntEditor()
    {
        var dsigBuilder = new DsigTableBuilder { Version = 1, Flags = 0 };
        dsigBuilder.AddSignature(format: 1, reserved1: 0, reserved2: 0, signature: new byte[] { 1, 2, 3, 4 });

        byte[] head = TestSfntTables.BuildValidHeadTable(unitsPerEm: 1000);

        var sfnt = new SfntBuilder { SfntVersion = 0x00010000u };
        sfnt.SetTable(KnownTags.head, head);
        sfnt.SetTable(dsigBuilder);
        byte[] originalFontBytes = sfnt.ToArray();

        using var originalFile = SfntFile.FromMemory(originalFontBytes);
        var originalFont = originalFile.GetFont(0);
        Assert.IsTrue(originalFont.TryGetDsig(out var originalDsig));
        Assert.AreEqual((uint)1, originalDsig.Version);
        Assert.AreEqual((ushort)1, originalDsig.SignatureCount);
        Assert.IsTrue(originalDsig.TryGetSignatureRecord(0, out var record));
        Assert.AreEqual((uint)1, record.Format);
        Assert.IsTrue(originalDsig.TryGetSignatureBlock(0, out var block));
        Assert.IsTrue(block.TryGetSignatureSpan(out var sig));
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3, 4 }, sig.ToArray());

        Assert.IsTrue(DsigTableBuilder.TryFrom(originalDsig, out var edit));
        edit.AddSignature(format: 2, reserved1: 0, reserved2: 0, signature: new byte[] { 9, 9 });

        var editor = new SfntEditor(originalFont);
        editor.SetTable(edit);
        byte[] editedFontBytes = editor.ToArray();

        Assert.AreEqual(0xB1B0AFBAu, OpenTypeChecksum.Compute(editedFontBytes));

        using var editedFile = SfntFile.FromMemory(editedFontBytes);
        var editedFont = editedFile.GetFont(0);
        Assert.IsTrue(editedFont.TryGetDsig(out var editedDsig));
        Assert.AreEqual((ushort)2, editedDsig.SignatureCount);
        Assert.IsTrue(editedDsig.TryGetSignatureRecord(1, out var record2));
        Assert.AreEqual((uint)2, record2.Format);
        Assert.IsTrue(editedDsig.TryGetSignatureBlock(1, out var block2));
        Assert.IsTrue(block2.TryGetSignatureSpan(out var sig2));
        CollectionAssert.AreEqual(new byte[] { 9, 9 }, sig2.ToArray());
    }

    [TestMethod]
    public void LtshTable_CanEditAndWriteBack_WithSfntEditor()
    {
        var ltshBuilder = new LtshTableBuilder { Version = 0 };
        ltshBuilder.Resize(numGlyphs: 3);
        ltshBuilder.SetYPel(0, 10);
        ltshBuilder.SetYPel(1, 20);
        ltshBuilder.SetYPel(2, 30);

        byte[] head = TestSfntTables.BuildValidHeadTable(unitsPerEm: 1000);

        var sfnt = new SfntBuilder { SfntVersion = 0x00010000u };
        sfnt.SetTable(KnownTags.head, head);
        sfnt.SetTable(ltshBuilder);
        byte[] originalFontBytes = sfnt.ToArray();

        using var originalFile = SfntFile.FromMemory(originalFontBytes);
        var originalFont = originalFile.GetFont(0);
        Assert.IsTrue(originalFont.TryGetLtsh(out var originalLtsh));
        Assert.AreEqual((ushort)3, originalLtsh.NumGlyphs);
        Assert.IsTrue(originalLtsh.TryGetYPel(2, out byte y2));
        Assert.AreEqual((byte)30, y2);

        Assert.IsTrue(LtshTableBuilder.TryFrom(originalLtsh, out var edit));
        edit.SetYPel(1, 99);

        var editor = new SfntEditor(originalFont);
        editor.SetTable(edit);
        byte[] editedFontBytes = editor.ToArray();

        Assert.AreEqual(0xB1B0AFBAu, OpenTypeChecksum.Compute(editedFontBytes));

        using var editedFile = SfntFile.FromMemory(editedFontBytes);
        var editedFont = editedFile.GetFont(0);
        Assert.IsTrue(editedFont.TryGetLtsh(out var editedLtsh));
        Assert.IsTrue(editedLtsh.TryGetYPel(1, out byte editedY1));
        Assert.AreEqual((byte)99, editedY1);
    }

    [TestMethod]
    public void VdmxTable_CanEditAndWriteBack_WithSfntEditor()
    {
        var vdmxBuilder = new VdmxTableBuilder { Version = 0 };
        int g0 = vdmxBuilder.AddGroup(startSize: 8, endSize: 10);
        vdmxBuilder.AddGroupEntry(g0, yPelHeight: 8, yMax: 7, yMin: -2);
        vdmxBuilder.AddRatio(charSet: 1, xRatio: 1, yStartRatio: 1, yEndRatio: 1, groupIndex: g0);

        byte[] head = TestSfntTables.BuildValidHeadTable(unitsPerEm: 1000);

        var sfnt = new SfntBuilder { SfntVersion = 0x00010000u };
        sfnt.SetTable(KnownTags.head, head);
        sfnt.SetTable(vdmxBuilder);
        byte[] originalFontBytes = sfnt.ToArray();

        using var originalFile = SfntFile.FromMemory(originalFontBytes);
        var originalFont = originalFile.GetFont(0);
        Assert.IsTrue(originalFont.TryGetVdmx(out var originalVdmx));
        Assert.AreEqual((ushort)1, originalVdmx.RatioCount);
        Assert.IsTrue(originalVdmx.TryGetGroupForRatio(0, out var group));
        Assert.AreEqual((ushort)1, group.EntryCount);

        Assert.IsTrue(VdmxTableBuilder.TryFrom(originalVdmx, out var edit));
        int g1 = edit.AddGroup(startSize: 11, endSize: 12);
        edit.AddGroupEntry(g1, yPelHeight: 11, yMax: 10, yMin: -3);
        edit.AddRatio(charSet: 1, xRatio: 1, yStartRatio: 1, yEndRatio: 1, groupIndex: g1);

        var editor = new SfntEditor(originalFont);
        editor.SetTable(edit);
        byte[] editedFontBytes = editor.ToArray();

        Assert.AreEqual(0xB1B0AFBAu, OpenTypeChecksum.Compute(editedFontBytes));

        using var editedFile = SfntFile.FromMemory(editedFontBytes);
        var editedFont = editedFile.GetFont(0);
        Assert.IsTrue(editedFont.TryGetVdmx(out var editedVdmx));
        Assert.AreEqual((ushort)2, editedVdmx.RatioCount);
        Assert.IsTrue(editedVdmx.TryGetGroupForRatio(1, out var group1));
        Assert.AreEqual((ushort)1, group1.EntryCount);
        Assert.IsTrue(group1.TryGetEntry(0, out var entry));
        Assert.AreEqual((ushort)11, entry.YPelHeight);
    }

    [TestMethod]
    public void HdmxTable_CanEditAndWriteBack_WithSfntEditor()
    {
        const ushort numGlyphs = 4;

        var hdmxBuilder = new HdmxTableBuilder(numGlyphs) { Version = 0 };
        hdmxBuilder.AddOrReplaceRecord(pixelSize: 12, widths: new byte[] { 1, 2, 3, 4 });
        hdmxBuilder.AddOrReplaceRecord(pixelSize: 13, widths: new byte[] { 4, 3, 2, 1 });

        byte[] head = TestSfntTables.BuildValidHeadTable(unitsPerEm: 1000);

        var sfnt = new SfntBuilder { SfntVersion = 0x00010000u };
        sfnt.SetTable(KnownTags.head, head);
        sfnt.SetTable(hdmxBuilder);
        byte[] originalFontBytes = sfnt.ToArray();

        using var originalFile = SfntFile.FromMemory(originalFontBytes);
        var originalFont = originalFile.GetFont(0);
        Assert.IsTrue(originalFont.TryGetHdmx(out var originalHdmx));
        Assert.AreEqual((ushort)2, originalHdmx.RecordCount);
        Assert.AreEqual((uint)8, originalHdmx.DeviceRecordSize); // 2 + 4 glyph widths, padded to 4

        Assert.IsTrue(originalHdmx.TryGetDeviceRecord(1, out var dr));
        Assert.AreEqual((byte)13, dr.PixelSize);
        Assert.IsTrue(dr.TryGetWidths(numGlyphs, out var widths));
        CollectionAssert.AreEqual(new byte[] { 4, 3, 2, 1 }, widths.ToArray());

        Assert.IsTrue(HdmxTableBuilder.TryFrom(originalHdmx, numGlyphs, out var edit));
        edit.AddOrReplaceRecord(pixelSize: 12, widths: new byte[] { 9, 9, 9, 9 });

        var editor = new SfntEditor(originalFont);
        editor.SetTable(edit);
        byte[] editedFontBytes = editor.ToArray();

        Assert.AreEqual(0xB1B0AFBAu, OpenTypeChecksum.Compute(editedFontBytes));

        using var editedFile = SfntFile.FromMemory(editedFontBytes);
        var editedFont = editedFile.GetFont(0);
        Assert.IsTrue(editedFont.TryGetHdmx(out var editedHdmx));
        Assert.IsTrue(editedHdmx.TryGetDeviceRecord(0, out var editedDr));
        Assert.IsTrue(editedDr.TryGetWidths(numGlyphs, out var editedWidths));
        CollectionAssert.AreEqual(new byte[] { 9, 9, 9, 9 }, editedWidths.ToArray());
    }

    [TestMethod]
    public void KernTable_CanEditAndWriteBack_WithSfntEditor()
    {
        var kernBuilder = new KernTableBuilder { Version = 0 };
        var st = kernBuilder.AddFormat0Subtable(horizontal: true);
        st.AddOrReplacePair(left: 1, right: 2, value: -50);
        st.AddOrReplacePair(left: 3, right: 4, value: 25);

        byte[] head = TestSfntTables.BuildValidHeadTable(unitsPerEm: 1000);

        var sfnt = new SfntBuilder { SfntVersion = 0x00010000u };
        sfnt.SetTable(KnownTags.head, head);
        sfnt.SetTable(kernBuilder);
        byte[] originalFontBytes = sfnt.ToArray();

        using var originalFile = SfntFile.FromMemory(originalFontBytes);
        var originalFont = originalFile.GetFont(0);
        Assert.IsTrue(originalFont.TryGetKern(out var originalKern));
        Assert.AreEqual((ushort)0, originalKern.Version);
        Assert.AreEqual((ushort)1, originalKern.SubtableCount);
        Assert.IsTrue(originalKern.TryGetSubtable(0, out var subtable));
        Assert.IsTrue(subtable.TryGetFormat0(out var fmt0));
        Assert.IsTrue(fmt0.TryFindKerningValue(1, 2, out short value));
        Assert.AreEqual(-50, value);

        Assert.IsTrue(KernTableBuilder.TryFrom(originalKern, out var edit));
        edit.Subtables[0].AddOrReplacePair(left: 1, right: 2, value: -60);

        var editor = new SfntEditor(originalFont);
        editor.SetTable(edit);
        byte[] editedFontBytes = editor.ToArray();

        Assert.AreEqual(0xB1B0AFBAu, OpenTypeChecksum.Compute(editedFontBytes));

        using var editedFile = SfntFile.FromMemory(editedFontBytes);
        var editedFont = editedFile.GetFont(0);
        Assert.IsTrue(editedFont.TryGetKern(out var editedKern));
        Assert.IsTrue(editedKern.TryGetSubtable(0, out var editedSubtable));
        Assert.IsTrue(editedSubtable.TryGetFormat0(out var editedFmt0));
        Assert.IsTrue(editedFmt0.TryFindKerningValue(1, 2, out short editedValue));
        Assert.AreEqual(-60, editedValue);
    }
}
