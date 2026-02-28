using Microsoft.VisualStudio.TestTools.UnitTesting;
using OTFontFile2.Tables;
using Legacy = OTFontFile;

namespace OTFontFile2.Tests.UnitTests;

[TestClass]
public sealed class RealFontSmokeTests
{
    [TestMethod]
    public void OpenSmallTtf_CoreTables_ParseAndNameMatchesLegacy()
    {
        string path = GetFontPath("small.ttf");

        using var file = SfntFile.Open(path);
        var font = file.GetFont(0);

        Assert.IsTrue(font.TryGetHead(out var head));
        Assert.IsTrue(font.TryGetHhea(out var hhea));
        Assert.IsTrue(font.TryGetMaxp(out var maxp));
        Assert.IsTrue(font.TryGetOs2(out var os2));
        Assert.IsTrue(font.TryGetCmap(out var cmap));
        Assert.IsTrue(font.TryGetName(out var name));
        Assert.IsTrue(font.TryGetPost(out var post));
        Assert.IsTrue(font.TryGetFftm(out var fftm));

        Assert.IsTrue(head.UnitsPerEm is > 0 and <= 16384);
        Assert.IsTrue(maxp.NumGlyphs > 0);
        Assert.IsTrue(hhea.NumberOfHMetrics > 0);
        Assert.IsTrue(hhea.NumberOfHMetrics <= maxp.NumGlyphs);

        Assert.AreEqual((uint)1, fftm.Version);

        string? fullName = name.GetFullNameString();
        Assert.IsFalse(string.IsNullOrWhiteSpace(fullName));

        // Cross-check a few fields against legacy OTFontFile.
        using var legacyFile = new Legacy.OTFile();
        Assert.IsTrue(legacyFile.open(path));
        var legacyFont = legacyFile.GetFont(0)!;

        var legacyHead = (Legacy.Table_head)legacyFont.GetTable("head")!;
        var legacyHhea = (Legacy.Table_hhea)legacyFont.GetTable("hhea")!;
        var legacyMaxp = (Legacy.Table_maxp)legacyFont.GetTable("maxp")!;
        var legacyOs2 = (Legacy.Table_OS2)legacyFont.GetTable("OS/2")!;
        var legacyName = (Legacy.Table_name)legacyFont.GetTable("name")!;

        Assert.AreEqual(legacyHead.unitsPerEm, head.UnitsPerEm);
        Assert.AreEqual(legacyHhea.numberOfHMetrics, hhea.NumberOfHMetrics);
        Assert.AreEqual(legacyMaxp.NumGlyphs, maxp.NumGlyphs);
        Assert.AreEqual(legacyOs2.version, os2.Version);
        Assert.AreEqual(legacyName.GetFullNameString(), name.GetFullNameString());
        Assert.AreEqual(legacyName.GetVersionString(), name.GetVersionString());
    }

    [TestMethod]
    public void Writer_CanRoundtripRepackRealFont_WithValidChecksumAdjustment()
    {
        string path = GetFontPath("small.ttf");

        using var file = SfntFile.Open(path);
        var font = file.GetFont(0);

        using var ms = new MemoryStream();
        SfntWriter.Write(ms, font);
        byte[] written = ms.ToArray();

        uint fileChecksum = OpenTypeChecksum.Compute(written);
        Assert.AreEqual(0xB1B0AFBAu, fileChecksum);
    }

    [DataTestMethod]
    [DataRow("cmap0_font1.otf", (ushort)0)]
    [DataRow("cmap2_font1.otf", (ushort)2)]
    [DataRow("cmap4_font1.otf", (ushort)4)]
    [DataRow("cmap6_font1.otf", (ushort)6)]
    [DataRow("cmap8_font1.otf", (ushort)8)]
    [DataRow("cmap10_font1.otf", (ushort)10)]
    [DataRow("cmap12_font1.otf", (ushort)12)]
    public void Cmap_SubtableMapping_MatchesLegacy(string fileName, ushort format)
    {
        string path = GetFontPath(fileName);

        using var file = SfntFile.Open(path);
        var font = file.GetFont(0);
        Assert.IsTrue(font.TryGetCmap(out var cmap));

        Assert.IsTrue(TryGetFirstSubtableByFormat(cmap, format, out var newSubtable));

        using var legacyFile = new Legacy.OTFile();
        Assert.IsTrue(legacyFile.open(path));
        var legacyFont = legacyFile.GetFont(0)!;
        var legacyCmap = (Legacy.Table_cmap)legacyFont.GetTable("cmap")!;
        var legacySubtable = GetFirstLegacySubtableByFormat(legacyCmap, format);

        // Pick representative code points, ensuring we test at least one mapped entry.
        var samples = format switch
        {
            0 => GetSamplesFormat0(),
            10 => GetSamplesFormat10((Legacy.Table_cmap.Format10)legacySubtable),
            12 => GetSamplesFormat12((Legacy.Table_cmap.Format12)legacySubtable),
            _ => GetSamplesBmpFindFirstMapped(legacySubtable)
        };

        foreach (uint cp in samples)
        {
            uint legacyGid = LegacyMap(legacySubtable, cp);
            Assert.IsTrue(newSubtable.TryMapCodePoint(cp, out uint newGid));
            Assert.AreEqual(legacyGid, newGid, $"cp=U+{cp:X}");
        }
    }

    private static bool TryGetFirstSubtableByFormat(CmapTable cmap, ushort format, out CmapTable.CmapSubtable subtable)
    {
        int count = cmap.EncodingRecordCount;
        for (int i = 0; i < count; i++)
        {
            if (!cmap.TryGetEncodingRecord(i, out var rec))
                continue;

            if (!cmap.TryGetSubtable(rec, out var st))
                continue;

            if (st.Format == format)
            {
                subtable = st;
                return true;
            }
        }

        subtable = default;
        return false;
    }

    private static Legacy.Table_cmap.Subtable GetFirstLegacySubtableByFormat(Legacy.Table_cmap cmap, ushort format)
    {
        for (uint i = 0; i < cmap.NumberOfEncodingTables; i++)
        {
            var ete = cmap.GetEncodingTableEntry(i);
            var st = cmap.GetSubtable(ete);
            if (st != null && st.format == format)
                return st;
        }

        Assert.Fail($"Legacy cmap subtable format {format} not found.");
        return null!;
    }

    private static IEnumerable<uint> GetSamplesFormat0()
    {
        // Full range for format 0 is small.
        for (uint i = 0; i < 256; i++)
            yield return i;
    }

    private static IEnumerable<uint> GetSamplesFormat10(Legacy.Table_cmap.Format10 st)
    {
        // Test a couple around the start char code (if present).
        uint start = st.startCharCode;
        yield return start;
        if (st.numChars > 1)
            yield return start + 1;
    }

    private static IEnumerable<uint> GetSamplesFormat12(Legacy.Table_cmap.Format12 st)
    {
        // Use first group bounds.
        var g0 = st.GetGroup(0);
        yield return g0.startCharCode;
        if (g0.endCharCode > g0.startCharCode)
            yield return g0.startCharCode + 1;
        yield return g0.endCharCode;
    }

    private static IEnumerable<uint> GetSamplesBmpFindFirstMapped(Legacy.Table_cmap.Subtable st)
    {
        // Find at least one mapped code point in BMP, then sample a few nearby.
        uint first = 0xFFFFFFFF;
        for (uint cp = 0; cp <= 0xFFFF; cp++)
        {
            if (LegacyMap(st, cp) != 0)
            {
                first = cp;
                break;
            }
        }

        Assert.AreNotEqual(0xFFFFFFFFu, first, "No mapped code point found in BMP sample scan.");

        uint start = first >= 2 ? first - 2 : 0;
        uint end = Math.Min(0xFFFFu, first + 32);
        for (uint cp = start; cp <= end; cp++)
            yield return cp;
    }

    private static uint LegacyMap(Legacy.Table_cmap.Subtable st, uint codePoint)
    {
        ushort fmt = st.format;
        if (fmt == 10 && st is Legacy.Table_cmap.Format10 f10)
            return f10.MapCharToGlyph(codePoint);
        if (fmt == 12 && st is Legacy.Table_cmap.Format12 f12)
            return f12.MapCharToGlyph(codePoint);

        if (codePoint > 0xFFFF)
            return 0;

        ushort cp16 = (ushort)codePoint;
        byte[] buf = new byte[2];
        buf[0] = (byte)cp16;
        buf[1] = (byte)(cp16 >> 8);
        return st.MapCharToGlyph(buf, 0);
    }

    private static string GetFontPath(string fileName)
        => Path.Combine(AppContext.BaseDirectory, "TestResources", "SampleFonts", fileName);
}
