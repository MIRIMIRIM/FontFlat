using Microsoft.VisualStudio.TestTools.UnitTesting;
using OTFontFile2.Tables;
using System.Buffers.Binary;
using System.Text;
using Legacy = OTFontFile;

namespace OTFontFile2.Tests.UnitTests;

[TestClass]
public sealed class LowCouplingTablesTests
{
    [TestMethod]
    public void OpenMediumTtf_LowCouplingTables_ParseAndMatchLegacy()
    {
        string path = GetFontPath("medium.ttf");

        using var file = SfntFile.Open(path);
        var font = file.GetFont(0);

        Assert.IsTrue(font.TryGetGasp(out var gasp));
        Assert.IsTrue(font.TryGetMeta(out var meta));
        Assert.IsTrue(font.TryGetDsig(out var dsig));
        Assert.IsTrue(font.TryGetCvt(out var cvt));
        Assert.IsTrue(font.TryGetFpgm(out var fpgm));
        Assert.IsTrue(font.TryGetPrep(out var prep));
        Assert.IsTrue(font.TryGetKern(out var kern));

        Assert.IsTrue(gasp.RangeCount > 0);
        Assert.IsTrue(meta.DataMapCount > 0);
        Assert.IsTrue(dsig.Version != 0);
        Assert.IsTrue(cvt.ValueCount > 0);
        Assert.IsTrue(fpgm.Length > 0);
        Assert.IsTrue(prep.Length > 0);
        Assert.IsTrue(kern.SubtableCount > 0);

        using var legacyFile = new Legacy.OTFile();
        Assert.IsTrue(legacyFile.open(path));
        var legacyFont = legacyFile.GetFont(0)!;

        var legacyGasp = (Legacy.Table_gasp)legacyFont.GetTable("gasp")!;
        var legacyMeta = (Legacy.Table_meta)legacyFont.GetTable("meta")!;
        var legacyDsig = (Legacy.Table_DSIG)legacyFont.GetTable("DSIG")!;
        var legacyCvt = (Legacy.Table_cvt)legacyFont.GetTable("cvt ")!;
        var legacyFpgm = (Legacy.Table_fpgm)legacyFont.GetTable("fpgm")!;
        var legacyPrep = (Legacy.Table_prep)legacyFont.GetTable("prep")!;
        var legacyKern = (Legacy.Table_kern)legacyFont.GetTable("kern")!;

        Assert.AreEqual(legacyGasp.version, gasp.Version);
        Assert.AreEqual(legacyGasp.numRanges, gasp.RangeCount);
        for (int i = 0; i < gasp.RangeCount; i++)
        {
            Assert.IsTrue(gasp.TryGetRange(i, out var newRange));
            var oldRange = legacyGasp.GetGaspRange((uint)i)!;
            Assert.AreEqual(oldRange.rangeMaxPPEM, newRange.RangeMaxPpem);
            Assert.AreEqual(oldRange.rangeGaspBehavior, (ushort)newRange.Behavior);
        }

        Assert.AreEqual(legacyMeta.version, meta.Version);
        Assert.AreEqual(legacyMeta.flags, meta.Flags);
        Assert.AreEqual(legacyMeta.dataOffset, meta.DataOffset);
        Assert.AreEqual(legacyMeta.numDataMaps, meta.DataMapCount);
        Assert.IsTrue(meta.DataMapCount <= (uint)int.MaxValue);
        int metaCount = (int)meta.DataMapCount;
        for (int i = 0; i < metaCount; i++)
        {
            Assert.IsTrue(meta.TryGetDataMap(i, out var newMap));
            var oldMap = legacyMeta.GetDataMap((uint)i)!;
            Assert.AreEqual(oldMap.tag, newMap.Tag.ToString());
            Assert.AreEqual(oldMap.dataOffset, newMap.DataOffset);
            Assert.AreEqual(oldMap.dataLength, newMap.DataLength);
            Assert.AreEqual(legacyMeta.GetStringData((uint)i), meta.GetUtf8String(i));
        }

        Assert.AreEqual(legacyDsig.ulVersion, dsig.Version);
        Assert.AreEqual(legacyDsig.usNumSigs, dsig.SignatureCount);
        Assert.AreEqual(legacyDsig.usFlag, dsig.Flags);
        if (dsig.SignatureCount > 0)
        {
            Assert.IsTrue(dsig.TryGetSignatureRecord(0, out var newRecord));
            var oldRecord = legacyDsig.GetSigFormatOffset(0)!;
            Assert.AreEqual(oldRecord.ulFormat, newRecord.Format);
            Assert.AreEqual(oldRecord.ulLength, newRecord.Length);
            Assert.AreEqual(oldRecord.ulOffset, newRecord.Offset);

            Assert.IsTrue(dsig.TryGetSignatureBlock(0, out var newBlock));
            Assert.IsTrue(newBlock.TryGetSignatureSpan(out var newSig));

            var oldBlock = legacyDsig.GetSignatureBlock(0)!;
            Assert.AreEqual(oldBlock.usReserved1, newBlock.Reserved1);
            Assert.AreEqual(oldBlock.usReserved2, newBlock.Reserved2);
            Assert.AreEqual(oldBlock.cbSignature, newBlock.SignatureLength);
            Assert.AreEqual((int)oldBlock.cbSignature, newSig.Length);

            int toCheck = Math.Min(newSig.Length, 32);
            for (int i = 0; i < toCheck; i++)
                Assert.AreEqual(oldBlock.bSignature![i], newSig[i]);
        }

        Assert.AreEqual((int)legacyCvt.GetLength(), cvt.Table.Length);
        Assert.AreEqual(cvt.Table.Length / 2, cvt.ValueCount);
        foreach (int i in GetSampleIndices(cvt.ValueCount))
        {
            Assert.IsTrue(cvt.TryGetValue(i, out short newValue));
            Assert.AreEqual(legacyCvt.GetValue((uint)i), newValue);
        }

        Assert.AreEqual((int)legacyFpgm.GetLength(), fpgm.Length);
        foreach (int i in GetSampleIndices(fpgm.Length))
        {
            Assert.IsTrue(fpgm.TryGetByte(i, out byte newByte));
            Assert.AreEqual(legacyFpgm.GetByte((uint)i), newByte);
        }

        Assert.AreEqual((int)legacyPrep.GetLength(), prep.Length);
        foreach (int i in GetSampleIndices(prep.Length))
        {
            Assert.IsTrue(prep.TryGetByte(i, out byte newByte));
            Assert.AreEqual(legacyPrep.GetByte((uint)i), newByte);
        }

        Assert.AreEqual(legacyKern.version, kern.Version);
        Assert.AreEqual(legacyKern.nTables, kern.SubtableCount);

        Assert.IsTrue(kern.TryGetSubtable(0, out var newSubtable));
        var oldSubtable = legacyKern.GetSubTable(0) as Legacy.Table_kern.SubTableFormat0;
        Assert.IsNotNull(oldSubtable);

        Assert.AreEqual(oldSubtable.coverage, newSubtable.Coverage);
        Assert.AreEqual(oldSubtable.length, newSubtable.Length);
        Assert.IsTrue(newSubtable.TryGetFormat0(out var newFmt0));

        Assert.AreEqual(oldSubtable.nPairs, newFmt0.PairCount);

        foreach (int i in GetSampleIndices(newFmt0.PairCount))
        {
            ushort oldLeft = 0, oldRight = 0;
            short oldValue = 0;
            oldSubtable.GetKerningPairAndValue(i, ref oldLeft, ref oldRight, ref oldValue);

            Assert.IsTrue(newFmt0.TryGetPair(i, out var newPair));
            Assert.AreEqual(oldLeft, newPair.Left);
            Assert.AreEqual(oldRight, newPair.Right);
            Assert.AreEqual(oldValue, newPair.Value);

            Assert.IsTrue(newFmt0.TryFindKerningValue(newPair.Left, newPair.Right, out short searched));
            Assert.AreEqual(newPair.Value, searched);
        }
    }

    [TestMethod]
    public void OpenMsyhbdTtc_HdmxLtshVdmx_ParseAndMatchLegacy()
    {
        string path = GetFontPath("msyhbd.ttc");

        using var file = SfntFile.Open(path);
        Assert.IsTrue(file.IsTtc);

        var font = file.GetFont(0);

        Assert.IsTrue(font.TryGetMaxp(out var maxp));
        Assert.IsTrue(font.TryGetHdmx(out var hdmx));
        Assert.IsTrue(font.TryGetLtsh(out var ltsh));
        Assert.IsTrue(font.TryGetVdmx(out var vdmx));

        Assert.IsTrue(hdmx.RecordCount > 0);
        Assert.IsTrue(hdmx.DeviceRecordSize >= 2);
        Assert.IsTrue(hdmx.TryGetDeviceRecord(0, out var newDeviceRecord));
        Assert.IsTrue(newDeviceRecord.TryGetWidths(maxp.NumGlyphs, out var widths));
        Assert.AreEqual(maxp.NumGlyphs, widths.Length);

        Assert.IsTrue(ltsh.NumGlyphs > 0);
        Assert.IsTrue(ltsh.TryGetYPelSpan(out var yPels));
        Assert.AreEqual(ltsh.NumGlyphs, yPels.Length);

        Assert.IsTrue(vdmx.RatioCount > 0);
        Assert.IsTrue(vdmx.TryGetRatio(0, out var newRatio));
        Assert.IsTrue(vdmx.TryGetGroupForRatio(0, out var newGroup));
        Assert.IsTrue(newGroup.EntryCount > 0);
        Assert.IsTrue(newGroup.TryGetEntry(0, out var newEntry));

        using var legacyFile = new Legacy.OTFile();
        Assert.IsTrue(legacyFile.open(path));
        var legacyFont = legacyFile.GetFont(0)!;

        var legacyMaxp = (Legacy.Table_maxp)legacyFont.GetTable("maxp")!;
        var legacyHdmx = (Legacy.Table_hdmx)legacyFont.GetTable("hdmx")!;
        var legacyLtsh = (Legacy.Table_LTSH)legacyFont.GetTable("LTSH")!;
        var legacyVdmx = (Legacy.Table_VDMX)legacyFont.GetTable("VDMX")!;

        Assert.AreEqual(legacyHdmx.TableVersionNumber, hdmx.Version);
        Assert.AreEqual((ushort)legacyHdmx.NumberDeviceRecords, hdmx.RecordCount);
        Assert.AreEqual(unchecked((uint)legacyHdmx.SizeofDeviceRecord), hdmx.DeviceRecordSize);

        var oldDeviceRecord = legacyHdmx.GetDeviceRecord(0, legacyMaxp.NumGlyphs)!;
        Assert.AreEqual(oldDeviceRecord.PixelSize, newDeviceRecord.PixelSize);
        Assert.AreEqual(oldDeviceRecord.MaxWidth, newDeviceRecord.MaxWidth);

        foreach (int gid in GetSampleGlyphIds(maxp.NumGlyphs))
            Assert.AreEqual(oldDeviceRecord.GetWidth((uint)gid), widths[gid]);

        Assert.AreEqual(legacyLtsh.version, ltsh.Version);
        Assert.AreEqual(legacyLtsh.numGlyphs, ltsh.NumGlyphs);
        foreach (int gid in GetSampleGlyphIds(ltsh.NumGlyphs))
        {
            Assert.IsTrue(ltsh.TryGetYPel(gid, out byte newYPel));
            Assert.AreEqual(legacyLtsh.GetYPel((uint)gid), newYPel);
        }

        Assert.AreEqual(legacyVdmx.version, vdmx.Version);
        Assert.AreEqual(legacyVdmx.numRecs, vdmx.GroupCount);
        Assert.AreEqual(legacyVdmx.numRatios, vdmx.RatioCount);

        var oldRatio = legacyVdmx.GetRatioRange(0);
        Assert.AreEqual(oldRatio.bCharSet, newRatio.CharSet);
        Assert.AreEqual(oldRatio.xRatio, newRatio.XRatio);
        Assert.AreEqual(oldRatio.yStartRatio, newRatio.YStartRatio);
        Assert.AreEqual(oldRatio.yEndRatio, newRatio.YEndRatio);

        var oldGroup = legacyVdmx.GetVdmxGroup(0);
        Assert.AreEqual(oldGroup.recs, newGroup.EntryCount);
        Assert.AreEqual(oldGroup.startsz, newGroup.StartSize);
        Assert.AreEqual(oldGroup.endsz, newGroup.EndSize);

        var oldEntry = oldGroup.GetEntry(0);
        Assert.AreEqual(oldEntry.yPelHeight, newEntry.YPelHeight);
        Assert.AreEqual(oldEntry.yMax, newEntry.YMax);
        Assert.AreEqual(oldEntry.yMin, newEntry.YMin);
    }

    [TestMethod]
    public void SyntheticPcltTable_CanParseFields()
    {
        byte[] pcltData = BuildPcltData();

        using var ms = new MemoryStream();
        SfntWriter.Write(
            destination: ms,
            sfntVersion: 0x00010000u,
            tables: new[] { new MemoryTableSource(KnownTags.PCLT, pcltData) });

        byte[] fontBytes = ms.ToArray();

        using var file = SfntFile.FromMemory(fontBytes);
        var font = file.GetFont(0);
        Assert.IsTrue(font.TryGetPclt(out var pclt));

        Assert.AreEqual(0x00010000u, pclt.Version.RawValue);
        Assert.AreEqual(123456u, pclt.FontNumber);
        Assert.AreEqual((ushort)500, pclt.Pitch);
        Assert.AreEqual((ushort)250, pclt.XHeight);
        Assert.AreEqual((ushort)0x1234, pclt.Style);
        Assert.AreEqual((ushort)0x5678, pclt.TypeFamily);
        Assert.AreEqual((ushort)700, pclt.CapHeight);
        Assert.AreEqual((ushort)0x0020, pclt.SymbolSet);
        Assert.AreEqual(unchecked((sbyte)-3), pclt.StrokeWeight);
        Assert.AreEqual((sbyte)5, pclt.WidthType);
        Assert.AreEqual((byte)2, pclt.SerifStyle);
        Assert.AreEqual((byte)0, pclt.Reserved);

        var expectedTypeface = new byte[16];
        Encoding.ASCII.GetBytes("TestTypeface").CopyTo(expectedTypeface, 0);
        Assert.IsTrue(pclt.Typeface.SequenceEqual(expectedTypeface));

        var expectedComplement = Encoding.ASCII.GetBytes("ABCDEFGH");
        Assert.IsTrue(pclt.CharacterComplement.SequenceEqual(expectedComplement));

        var expectedFileName = Encoding.ASCII.GetBytes("FILE01");
        Assert.IsTrue(pclt.FileName.SequenceEqual(expectedFileName));

        string tmp = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(tmp, fontBytes);

            using var legacyFile = new Legacy.OTFile();
            Assert.IsTrue(legacyFile.open(tmp));
            var legacyFont = legacyFile.GetFont(0)!;
            var legacyPclt = (Legacy.Table_PCLT)legacyFont.GetTable("PCLT")!;

            Assert.AreEqual(pclt.Version.RawValue, legacyPclt.Version.GetUint());
            Assert.AreEqual(pclt.FontNumber, legacyPclt.FontNumber);
            Assert.AreEqual(pclt.Pitch, legacyPclt.Pitch);
            Assert.AreEqual(pclt.XHeight, legacyPclt.xHeight);
            Assert.AreEqual(pclt.Style, legacyPclt.Style);
            Assert.AreEqual(pclt.TypeFamily, legacyPclt.TypeFamily);
            Assert.AreEqual(pclt.CapHeight, legacyPclt.CapHeight);
            Assert.AreEqual(pclt.SymbolSet, legacyPclt.SymbolSet);

            Assert.IsTrue(pclt.Typeface.SequenceEqual(legacyPclt.Typeface));
            Assert.IsTrue(pclt.CharacterComplement.SequenceEqual(legacyPclt.CharacterComplement));
            Assert.IsTrue(pclt.FileName.SequenceEqual(legacyPclt.FileName));

            Assert.AreEqual(pclt.StrokeWeight, legacyPclt.StrokeWeight);
            Assert.AreEqual(pclt.WidthType, legacyPclt.WidthType);
            Assert.AreEqual(pclt.SerifStyle, legacyPclt.SerifStyle);
            Assert.AreEqual(pclt.Reserved, legacyPclt.Reserved);
        }
        finally
        {
            try { File.Delete(tmp); } catch { /* ignore */ }
        }
    }

    private static byte[] BuildPcltData()
    {
        byte[] data = new byte[54];

        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(0, 4), 0x00010000u); // Version 1.0
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(4, 4), 123456u);     // FontNumber
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(8, 2), 500);         // Pitch
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(10, 2), 250);        // xHeight
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(12, 2), 0x1234);     // Style
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(14, 2), 0x5678);     // TypeFamily
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(16, 2), 700);        // CapHeight
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(18, 2), 0x0020);     // SymbolSet

        Encoding.ASCII.GetBytes("TestTypeface").CopyTo(data, 20);
        Encoding.ASCII.GetBytes("ABCDEFGH").CopyTo(data, 36);
        Encoding.ASCII.GetBytes("FILE01").CopyTo(data, 44);

        data[50] = unchecked((byte)(sbyte)-3); // StrokeWeight
        data[51] = unchecked((byte)(sbyte)5);  // WidthType
        data[52] = 2;                          // SerifStyle
        data[53] = 0;                          // Reserved

        return data;
    }

    private static IEnumerable<int> GetSampleIndices(int count)
    {
        if (count <= 0)
            yield break;

        yield return 0;
        if (count > 1)
            yield return 1;

        int mid = count / 2;
        if (mid > 1 && mid < count - 1)
            yield return mid;

        if (count > 2)
            yield return count - 1;
    }

    private static IEnumerable<int> GetSampleGlyphIds(int numGlyphs)
    {
        if (numGlyphs <= 0)
            yield break;

        yield return 0;
        if (numGlyphs > 1)
            yield return 1;
        if (numGlyphs > 2)
            yield return 2;

        int mid = numGlyphs / 2;
        if (mid > 2 && mid < numGlyphs - 1)
            yield return mid;

        if (numGlyphs > 3)
            yield return numGlyphs - 1;
    }

    private static string GetFontPath(string fileName)
        => Path.Combine(AppContext.BaseDirectory, "TestResources", "SampleFonts", fileName);
}
