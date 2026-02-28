using Microsoft.VisualStudio.TestTools.UnitTesting;
using OTFontFile2.Tables;
using Legacy = OTFontFile;

namespace OTFontFile2.Tests.UnitTests;

[TestClass]
public sealed class GsubSingleSubstTests
{
    [TestMethod]
    public void OpenMediumTtf_GsubSingleSubst_MatchesLegacy()
    {
        string path = GetFontPath("medium.ttf");

        using var file = SfntFile.Open(path);
        var font = file.GetFont(0);

        Assert.IsTrue(font.TryGetGsub(out var gsub));
        Assert.IsTrue(gsub.TryGetLookupList(out var lookupList));

        Assert.IsTrue(TryFindFirstSingleSubstSubtableOffset(gsub.Table, lookupList, out int singleSubstOffset));

        Assert.IsTrue(GsubSingleSubstSubtable.TryCreate(gsub.Table, singleSubstOffset, out var single));
        Assert.IsTrue(single.TryGetCoverage(out var coverage));

        using var legacyFile = new Legacy.OTFile();
        Assert.IsTrue(legacyFile.open(path));
        var legacyFont = legacyFile.GetFont(0)!;

        var legacyGsub = (Legacy.Table_GSUB)legacyFont.GetTable("GSUB")!;
        var legacyBuf = legacyGsub.GetBuffer();
        var legacySingle = new Legacy.Table_GSUB.SingleSubst((uint)singleSubstOffset, legacyBuf);

        Assert.AreEqual(legacySingle.SubstFormat, single.SubstFormat);

        Legacy.OTL.CoverageTable legacyCoverage;
        ushort legacySubstFormat = legacySingle.SubstFormat;
        ushort legacyDelta = 0;
        Legacy.Table_GSUB.SingleSubst.SingleSubstFormat2? legacyF2 = null;
        if (legacySubstFormat == 1)
        {
            var legacyF1 = legacySingle.GetSingleSubstFormat1();
            legacyCoverage = legacyF1.GetCoverageTable();
            legacyDelta = legacyF1.DeltaGlyphID;
        }
        else if (legacySubstFormat == 2)
        {
            legacyF2 = legacySingle.GetSingleSubstFormat2();
            legacyCoverage = legacyF2.GetCoverageTable();
        }
        else
        {
            Assert.Fail($"Unexpected legacy single subst format: {legacySubstFormat}");
            return;
        }

        foreach (ushort gid in GetCoverageGlyphSamples(coverage))
        {
            var oldCov = legacyCoverage.GetGlyphCoverage(gid);
            Assert.IsTrue(coverage.TryGetCoverage(gid, out bool newCovered, out ushort newIndex));
            Assert.AreEqual(oldCov.bCovered, newCovered, $"gid={gid}");
            if (newCovered)
                Assert.AreEqual(oldCov.CoverageIndex, newIndex, $"gid={gid}");

            Assert.IsTrue(single.TrySubstituteGlyph(gid, out bool newSubstituted, out ushort newSubstitute));

            ushort expectedSubstitute = gid;
            bool expectedSubstituted = false;
            if (oldCov.bCovered)
            {
                expectedSubstituted = true;
                expectedSubstitute = legacySubstFormat == 1
                    ? unchecked((ushort)(gid + legacyDelta))
                    : legacyF2!.GetSubstituteGlyphID(oldCov.CoverageIndex);
            }

            Assert.AreEqual(expectedSubstituted, newSubstituted, $"gid={gid}");
            Assert.AreEqual(expectedSubstitute, newSubstitute, $"gid={gid}");
        }
    }

    private static bool TryFindFirstSingleSubstSubtableOffset(
        TableSlice gsubSlice,
        OtlLayoutTable.LookupList lookupList,
        out int singleSubstOffset)
    {
        singleSubstOffset = 0;

        int lookupCount = lookupList.LookupCount;
        for (int lookupIndex = 0; lookupIndex < lookupCount; lookupIndex++)
        {
            if (!lookupList.TryGetLookup(lookupIndex, out var lookup))
                continue;

            ushort lookupType = lookup.LookupType;
            ushort subtableCount = lookup.SubtableCount;

            if (lookupType == 1)
            {
                for (int subIndex = 0; subIndex < subtableCount; subIndex++)
                {
                    if (!lookup.TryGetSubtableOffset(subIndex, out ushort rel))
                        continue;

                    int offset = lookup.Offset + rel;
                    if (!GsubSingleSubstSubtable.TryCreate(gsubSlice, offset, out var single))
                        continue;

                    ushort fmt = single.SubstFormat;
                    if (fmt is 1 or 2)
                    {
                        singleSubstOffset = offset;
                        return true;
                    }
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

                    if (innerType != 1)
                        continue;

                    if (!GsubSingleSubstSubtable.TryCreate(gsubSlice, innerOffset, out var single))
                        continue;

                    ushort fmt = single.SubstFormat;
                    if (fmt is 1 or 2)
                    {
                        singleSubstOffset = innerOffset;
                        return true;
                    }
                }
            }
        }

        return false;
    }

    private static IEnumerable<ushort> GetCoverageGlyphSamples(CoverageTable coverage)
    {
        // Likely not covered; used to validate the "not substituted" path.
        yield return 0xFFFF;

        ushort format = coverage.CoverageFormat;
        if (format == 1 && coverage.TryGetFormat1GlyphCount(out ushort count) && count > 0)
        {
            foreach (int i in GetSampleIndices(count))
            {
                if (coverage.TryGetFormat1GlyphId(i, out ushort glyphId))
                    yield return glyphId;
            }

            if (coverage.TryGetFormat1GlyphId(0, out ushort first) && first > 0)
                yield return (ushort)(first - 1);

            yield break;
        }

        if (format == 2 && coverage.TryGetFormat2RangeCount(out ushort rangeCount) && rangeCount > 0)
        {
            if (coverage.TryGetFormat2RangeRecord(0, out var first))
            {
                yield return first.StartGlyphId;
                if (first.EndGlyphId > first.StartGlyphId)
                    yield return (ushort)(first.StartGlyphId + 1);
                yield return first.EndGlyphId;
                if (first.StartGlyphId > 0)
                    yield return (ushort)(first.StartGlyphId - 1);
            }

            if (rangeCount > 1 && coverage.TryGetFormat2RangeRecord(rangeCount - 1, out var last))
                yield return last.EndGlyphId;
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

    private static string GetFontPath(string fileName)
        => Path.Combine(AppContext.BaseDirectory, "TestResources", "SampleFonts", fileName);
}

