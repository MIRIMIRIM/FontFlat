using Microsoft.VisualStudio.TestTools.UnitTesting;
using OTFontFile2.Tables;
using Legacy = OTFontFile;

namespace OTFontFile2.Tests.UnitTests;

[TestClass]
public sealed class GsubLigatureSubstTests
{
    [TestMethod]
    public void GsubLigatureSubst_MatchesLegacy_OnSampleFont()
    {
        var candidates = new[]
        {
            "medium.ttf",
            "AvenirNextW1G-Regular.OTF",
            "SourceHanSansCN-Regular.otf"
        };

        string? fileName = null;
        int ligatureOffset = 0;

        foreach (string candidate in candidates)
        {
            string path = GetFontPath(candidate);
            if (!File.Exists(path))
                continue;

            using var file = SfntFile.Open(path);
            var font = file.GetFont(0);
            if (!font.TryGetGsub(out var gsub))
                continue;
            if (!gsub.TryGetLookupList(out var lookupList))
                continue;

            if (TryFindFirstGsubSubtableOffset(gsub.Table, lookupList, desiredLookupType: 4, out ligatureOffset))
            {
                fileName = candidate;
                break;
            }
        }

        Assert.IsNotNull(fileName, "No GSUB ligature subtable found in sample fonts.");
        string foundPath = GetFontPath(fileName!);

        using var foundFile = SfntFile.Open(foundPath);
        var foundFont = foundFile.GetFont(0);
        Assert.IsTrue(foundFont.TryGetGsub(out var foundGsub));

        Assert.IsTrue(GsubLigatureSubstSubtable.TryCreate(foundGsub.Table, ligatureOffset, out var lig));
        Assert.IsTrue(lig.TryGetCoverage(out var coverage));

        Assert.IsTrue(TryGetFirstCoveredGlyph(coverage, out ushort coveredGlyph));

        Assert.IsTrue(lig.TryGetLigatureSetForGlyph(coveredGlyph, out bool hasSet, out var newSet));
        Assert.IsTrue(hasSet);
        Assert.IsTrue(newSet.LigatureCount > 0);

        using var legacyFile = new Legacy.OTFile();
        Assert.IsTrue(legacyFile.open(foundPath));
        var legacyFont = legacyFile.GetFont(0)!;

        var legacyGsub = (Legacy.Table_GSUB)legacyFont.GetTable("GSUB")!;
        var legacyBuf = legacyGsub.GetBuffer();
        var legacyLig = new Legacy.Table_GSUB.LigatureSubst((uint)ligatureOffset, legacyBuf);

        Assert.AreEqual(legacyLig.SubstFormat, lig.SubstFormat);
        Assert.AreEqual(legacyLig.CoverageOffset, lig.CoverageOffset);
        Assert.AreEqual(legacyLig.LigSetCount, lig.LigatureSetCount);

        var legacyCoverage = legacyLig.GetCoverageTable();
        var legacyCov = legacyCoverage.GetGlyphCoverage(coveredGlyph);
        Assert.IsTrue(legacyCov.bCovered);

        Assert.IsTrue(coverage.TryGetCoverage(coveredGlyph, out bool newCovered, out ushort newIndex));
        Assert.IsTrue(newCovered);
        Assert.AreEqual(legacyCov.CoverageIndex, newIndex);

        var oldSet = legacyLig.GetLigatureSetTable(legacyCov.CoverageIndex)!;
        Assert.AreEqual(oldSet.LigatureCount, newSet.LigatureCount);

        foreach (int ligIndex in GetSampleIndices(newSet.LigatureCount))
        {
            Assert.IsTrue(newSet.TryGetLigature(ligIndex, out var newLig));
            var oldLig = oldSet.GetLigatureTable((uint)ligIndex)!;

            Assert.AreEqual(oldLig.LigGlyph, newLig.LigGlyph);
            Assert.AreEqual(oldLig.CompCount, newLig.ComponentCount);

            int componentArrayCount = Math.Max(0, newLig.ComponentCount - 1);
            foreach (int compIndex in GetSampleIndices(componentArrayCount))
            {
                Assert.IsTrue(newLig.TryGetComponentGlyphId(compIndex, out ushort newComp));
                ushort oldComp = oldLig.GetComponentGlyphID((uint)compIndex);
                Assert.AreEqual(oldComp, newComp);
            }
        }
    }

    private static bool TryFindFirstGsubSubtableOffset(
        TableSlice gsubSlice,
        OtlLayoutTable.LookupList lookupList,
        ushort desiredLookupType,
        out int subtableOffset)
    {
        subtableOffset = 0;

        int lookupCount = lookupList.LookupCount;
        for (int lookupIndex = 0; lookupIndex < lookupCount; lookupIndex++)
        {
            if (!lookupList.TryGetLookup(lookupIndex, out var lookup))
                continue;

            ushort lookupType = lookup.LookupType;
            ushort subtableCount = lookup.SubtableCount;

            if (lookupType == desiredLookupType)
            {
                for (int subIndex = 0; subIndex < subtableCount; subIndex++)
                {
                    if (!lookup.TryGetSubtableOffset(subIndex, out ushort rel))
                        continue;

                    subtableOffset = lookup.Offset + rel;
                    return true;
                }
            }
            else if (lookupType == 7)
            {
                for (int subIndex = 0; subIndex < subtableCount; subIndex++)
                {
                    if (!lookup.TryGetSubtableOffset(subIndex, out ushort rel))
                        continue;

                    int extOffset = lookup.Offset + rel;
                    if (!GsubExtensionSubstSubtable.TryCreate(gsubSlice, extOffset, out var ext))
                        continue;

                    if (!ext.TryResolve(out ushort innerType, out int innerOffset))
                        continue;

                    if (innerType == desiredLookupType)
                    {
                        subtableOffset = innerOffset;
                        return true;
                    }
                }
            }
        }

        return false;
    }

    private static bool TryGetFirstCoveredGlyph(CoverageTable coverage, out ushort glyphId)
    {
        glyphId = 0;

        ushort format = coverage.CoverageFormat;
        if (format == 1)
            return coverage.TryGetFormat1GlyphId(0, out glyphId);

        if (format == 2 && coverage.TryGetFormat2RangeRecord(0, out var range))
        {
            glyphId = range.StartGlyphId;
            return true;
        }

        return false;
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

    private static string GetFontPath(string fileName)
        => Path.Combine(AppContext.BaseDirectory, "TestResources", "SampleFonts", fileName);
}

