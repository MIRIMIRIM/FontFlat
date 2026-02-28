using Microsoft.VisualStudio.TestTools.UnitTesting;
using OTFontFile2.Tables;
using Legacy = OTFontFile;

namespace OTFontFile2.Tests.UnitTests;

[TestClass]
public sealed class OtlAndVorgTablesTests
{
    [TestMethod]
    public void OpenMediumTtf_OtlTables_MatchLegacyHeaderAndCounts()
    {
        string path = GetFontPath("medium.ttf");

        using var file = SfntFile.Open(path);
        var font = file.GetFont(0);

        Assert.IsTrue(font.TryGetGsub(out var gsub));
        Assert.IsTrue(font.TryGetGpos(out var gpos));
        Assert.IsTrue(font.TryGetGdef(out var gdef));
        Assert.IsTrue(font.TryGetMaxp(out var maxp));

        Assert.IsTrue(gsub.TryGetScriptList(out var gsubScripts));
        Assert.IsTrue(gsub.TryGetFeatureList(out var gsubFeatures));
        Assert.IsTrue(gsub.TryGetLookupList(out var gsubLookups));

        Assert.IsTrue(gpos.TryGetScriptList(out var gposScripts));
        Assert.IsTrue(gpos.TryGetFeatureList(out var gposFeatures));
        Assert.IsTrue(gpos.TryGetLookupList(out var gposLookups));

        using var legacyFile = new Legacy.OTFile();
        Assert.IsTrue(legacyFile.open(path));
        var legacyFont = legacyFile.GetFont(0)!;

        var legacyGsub = (Legacy.Table_GSUB)legacyFont.GetTable("GSUB")!;
        var legacyGpos = (Legacy.Table_GPOS)legacyFont.GetTable("GPOS")!;
        var legacyGdef = (Legacy.Table_GDEF)legacyFont.GetTable("GDEF")!;

        Assert.AreEqual(legacyGsub.Version.GetUint(), gsub.Version.RawValue);
        Assert.AreEqual(legacyGsub.ScriptListOffset, gsub.ScriptListOffset);
        Assert.AreEqual(legacyGsub.FeatureListOffset, gsub.FeatureListOffset);
        Assert.AreEqual(legacyGsub.LookupListOffset, gsub.LookupListOffset);

        Assert.AreEqual(legacyGpos.Version.GetUint(), gpos.Version.RawValue);
        Assert.AreEqual(legacyGpos.ScriptListOffset, gpos.ScriptListOffset);
        Assert.AreEqual(legacyGpos.FeatureListOffset, gpos.FeatureListOffset);
        Assert.AreEqual(legacyGpos.LookupListOffset, gpos.LookupListOffset);

        var legacyGsubScripts = legacyGsub.GetScriptListTable();
        var legacyGsubFeatures = legacyGsub.GetFeatureListTable();
        var legacyGsubLookups = legacyGsub.GetLookupListTable();

        Assert.AreEqual(legacyGsubScripts.ScriptCount, gsubScripts.ScriptCount);
        Assert.AreEqual(legacyGsubFeatures.FeatureCount, gsubFeatures.FeatureCount);
        Assert.AreEqual(legacyGsubLookups.LookupCount, gsubLookups.LookupCount);

        var legacyGposScripts = legacyGpos.GetScriptListTable();
        var legacyGposFeatures = legacyGpos.GetFeatureListTable();
        var legacyGposLookups = legacyGpos.GetLookupListTable();

        Assert.AreEqual(legacyGposScripts.ScriptCount, gposScripts.ScriptCount);
        Assert.AreEqual(legacyGposFeatures.FeatureCount, gposFeatures.FeatureCount);
        Assert.AreEqual(legacyGposLookups.LookupCount, gposLookups.LookupCount);

        if (gsubScripts.ScriptCount > 0)
        {
            Assert.IsTrue(gsubScripts.TryGetScriptRecord(0, out var newScript0));
            var oldScript0 = legacyGsubScripts.GetScriptRecord(0)!;
            Assert.AreEqual(oldScript0.ScriptTag!.ToString(), newScript0.ScriptTag.ToString());
            Assert.AreEqual(oldScript0.ScriptTableOffset, newScript0.ScriptOffset);
        }

        if (gsubFeatures.FeatureCount > 0)
        {
            Assert.IsTrue(gsubFeatures.TryGetFeatureRecord(0, out var newFeature0));
            var oldFeature0 = legacyGsubFeatures.GetFeatureRecord(0)!;
            Assert.AreEqual(oldFeature0.FeatureTag!.ToString(), newFeature0.FeatureTag.ToString());
            Assert.AreEqual(oldFeature0.FeatureTableOffset, newFeature0.FeatureOffset);
        }

        if (gsubLookups.LookupCount > 0)
        {
            Assert.IsTrue(gsubLookups.TryGetLookup(0, out var newLookup0));
            var oldLookup0 = legacyGsubLookups.GetLookupTable(0)!;
            Assert.AreEqual(oldLookup0.LookupType, newLookup0.LookupType);
            Assert.AreEqual(oldLookup0.LookupFlag, newLookup0.LookupFlag);
            Assert.AreEqual(oldLookup0.SubTableCount, newLookup0.SubtableCount);
        }

        Assert.AreEqual(legacyGdef.Version.GetUint(), gdef.Version.RawValue);
        Assert.AreEqual(legacyGdef.GlyphClassDefOffset, gdef.GlyphClassDefOffset);
        Assert.AreEqual(legacyGdef.AttachListOffset, gdef.AttachListOffset);
        Assert.AreEqual(legacyGdef.LigCaretListOffset, gdef.LigCaretListOffset);
        Assert.AreEqual(legacyGdef.MarkAttachClassDefOffset, gdef.MarkAttachClassDefOffset);
        Assert.AreEqual(legacyGdef.MarkGlyphSetsDefOffset, gdef.MarkGlyphSetsDefOffset);

        var legacyGlyphClassDef = legacyGdef.GetGlyphClassDefTable();
        if (legacyGlyphClassDef == null)
        {
            Assert.IsFalse(gdef.TryGetGlyphClassDef(out _));
        }
        else
        {
            Assert.IsTrue(gdef.TryGetGlyphClassDef(out var newClassDef));
            Assert.AreEqual(legacyGlyphClassDef.ClassFormat, newClassDef.ClassFormat);

            foreach (ushort gid in GetSampleGlyphIds(maxp.NumGlyphs))
            {
                Assert.IsTrue(newClassDef.TryGetClass(gid, out ushort newClass));
                ushort oldClass = legacyGlyphClassDef.GetClassValue(gid);
                Assert.AreEqual(oldClass, newClass, $"gid={gid}");
            }
        }
    }

    [TestMethod]
    public void OpenCffOtf_VorgTable_MatchesLegacy()
    {
        string path = GetFontPath("SourceHanSansCN-Regular.otf");

        using var file = SfntFile.Open(path);
        var font = file.GetFont(0);

        Assert.IsTrue(font.TryGetVorg(out var vorg));

        Assert.IsTrue(vorg.MetricCount > 0);
        Assert.IsTrue(vorg.TryGetMetric(0, out var metric0));
        Assert.IsTrue(vorg.TryGetVertOriginY(metric0.GlyphIndex, out short origin0));
        Assert.AreEqual(metric0.VertOriginY, origin0);

        using var legacyFile = new Legacy.OTFile();
        Assert.IsTrue(legacyFile.open(path));
        var legacyFont = legacyFile.GetFont(0)!;
        var legacyVorg = (Legacy.Table_VORG)legacyFont.GetTable("VORG")!;

        Assert.AreEqual(legacyVorg.majorVersion, vorg.MajorVersion);
        Assert.AreEqual(legacyVorg.minorVersion, vorg.MinorVersion);
        Assert.AreEqual(legacyVorg.defaultVertOriginY, vorg.DefaultVertOriginY);
        Assert.AreEqual(legacyVorg.numVertOriginYMetrics, vorg.MetricCount);

        foreach (int i in GetSampleIndices(vorg.MetricCount))
        {
            Assert.IsTrue(vorg.TryGetMetric(i, out var newMetric));
            var oldMetric = legacyVorg.GetVertOriginYMetrics((uint)i)!;
            Assert.AreEqual(oldMetric.glyphIndex, newMetric.GlyphIndex);
            Assert.AreEqual(oldMetric.vertOriginY, newMetric.VertOriginY);
        }
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

    private static IEnumerable<ushort> GetSampleGlyphIds(ushort numGlyphs)
    {
        if (numGlyphs == 0)
            yield break;

        yield return 0;
        if (numGlyphs > 1)
            yield return 1;
        if (numGlyphs > 2)
            yield return 2;

        ushort mid = (ushort)(numGlyphs / 2);
        if (mid > 2 && mid < (ushort)(numGlyphs - 1))
            yield return mid;

        if (numGlyphs > 3)
            yield return (ushort)(numGlyphs - 1);
    }

    private static string GetFontPath(string fileName)
        => Path.Combine(AppContext.BaseDirectory, "TestResources", "SampleFonts", fileName);
}
