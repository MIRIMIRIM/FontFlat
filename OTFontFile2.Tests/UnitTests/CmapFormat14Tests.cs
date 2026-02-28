using Microsoft.VisualStudio.TestTools.UnitTesting;
using OTFontFile2.Tables;
using Legacy = OTFontFile;

namespace OTFontFile2.Tests.UnitTests;

[TestClass]
public sealed class CmapFormat14Tests
{
    [TestMethod]
    public void OpenCmap14Font_Format14_ParsesAndMatchesLegacySamples()
    {
        string path = GetFontPath("cmap14_font1.otf");

        using var file = SfntFile.Open(path);
        var font = file.GetFont(0);
        Assert.IsTrue(font.TryGetCmap(out var cmap));

        Assert.IsTrue(TryGetFirstFormat14(cmap, out var newF14));

        using var legacyFile = new Legacy.OTFile();
        Assert.IsTrue(legacyFile.open(path));
        var legacyFont = legacyFile.GetFont(0)!;
        var legacyCmap = (Legacy.Table_cmap)legacyFont.GetTable("cmap")!;
        var legacyF14 = GetLegacyFormat14(legacyCmap);

        Assert.AreEqual(legacyF14.NumVarSelectorRecs, newF14.VarSelectorRecordCount);

        // Compare first selector record.
        var legacyRec0 = legacyF14.GetIthSelectorRecord(0);
        Assert.IsNotNull(legacyRec0);
        Assert.IsTrue(newF14.TryGetVarSelectorRecord(0, out var newRec0));
        Assert.AreEqual(legacyRec0!.varSelector, newRec0.VarSelector);
        Assert.AreEqual(legacyRec0.defaultUVSOffset, newRec0.DefaultUvsOffset);
        Assert.AreEqual(legacyRec0.nonDefaultUVSOffset, newRec0.NonDefaultUvsOffset);

        bool checkedNonDefault = false;
        for (uint i = 0; i < legacyF14.NumVarSelectorRecs; i++)
        {
            var r = legacyF14.GetIthSelectorRecord(i);
            if (r is null || r.nonDefaultUVSOffset == 0)
                continue;

            var nd = r.GetNonDefaultUVSTable();
            if (nd?.mappings is null || nd.mappings.Count == 0)
                continue;

            var m0 = nd.mappings[0];
            Assert.IsTrue(newF14.TryGetNonDefaultGlyphId(m0.unicodeValue, r.varSelector, out ushort newGid));
            Assert.AreEqual(m0.glyphID, newGid);
            checkedNonDefault = true;
            break;
        }

        Assert.IsTrue(checkedNonDefault, "No non-default UVS mappings found in sample font.");

        bool checkedDefault = false;
        for (uint i = 0; i < legacyF14.NumVarSelectorRecs; i++)
        {
            var r = legacyF14.GetIthSelectorRecord(i);
            if (r is null || r.defaultUVSOffset == 0)
                continue;

            var def = r.GetDefaultUVSTable();
            if (def?.ranges is null || def.ranges.Count == 0)
                continue;

            var range0 = def.ranges[0];
            Assert.IsTrue(newF14.IsDefaultVariationSequence(range0.startUnicodeValue, r.varSelector));
            checkedDefault = true;
            break;
        }

        Assert.IsTrue(checkedDefault, "No default UVS ranges found in sample font.");
    }

    private static bool TryGetFirstFormat14(CmapTable cmap, out CmapTable.CmapSubtable.Format14Subtable format14)
    {
        int count = cmap.EncodingRecordCount;
        for (int i = 0; i < count; i++)
        {
            if (!cmap.TryGetEncodingRecord(i, out var rec))
                continue;

            if (!cmap.TryGetSubtable(rec, out var st))
                continue;

            if (st.Format == 14 && st.TryGetFormat14(out var f14))
            {
                format14 = f14;
                return true;
            }
        }

        format14 = default;
        return false;
    }

    private static Legacy.Table_cmap.Format14 GetLegacyFormat14(Legacy.Table_cmap cmap)
    {
        for (uint i = 0; i < cmap.NumberOfEncodingTables; i++)
        {
            var ete = cmap.GetEncodingTableEntry(i);
            var st = cmap.GetSubtable(ete);
            if (st is Legacy.Table_cmap.Format14 f14)
                return f14;
        }

        Assert.Fail("Legacy cmap subtable format 14 not found.");
        return null!;
    }

    private static string GetFontPath(string fileName)
        => Path.Combine(AppContext.BaseDirectory, "TestResources", "SampleFonts", fileName);
}

