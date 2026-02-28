using Microsoft.VisualStudio.TestTools.UnitTesting;
using OTFontFile2.Tables;
using System.Text;
using Legacy = OTFontFile;

namespace OTFontFile2.Tests.UnitTests;

[TestClass]
public sealed class CffTableTests
{
    [TestMethod]
    public void OpenCffOtf_CffTable_HeaderAndIndexCounts_MatchLegacy()
    {
        string path = GetFontPath("SourceHanSansCN-Regular.otf");

        using var file = SfntFile.Open(path);
        var font = file.GetFont(0);

        Assert.IsTrue(font.TryGetCff(out var newCff));

        Assert.IsTrue(newCff.TryGetNameIndex(out var newName));
        Assert.IsTrue(newCff.TryGetTopDictIndex(out var newTopDict));
        Assert.IsTrue(newCff.TryGetStringIndex(out var newStrings));
        Assert.IsTrue(newCff.TryGetGlobalSubrIndex(out var newGlobalSubr));

        using var legacyFile = new Legacy.OTFile();
        Assert.IsTrue(legacyFile.open(path));
        var legacyFont = legacyFile.GetFont(0)!;

        var legacyCff = (Legacy.Table_CFF)legacyFont.GetTable("CFF ")!;

        Assert.AreEqual(legacyCff.major, newCff.Major);
        Assert.AreEqual(legacyCff.minor, newCff.Minor);
        Assert.AreEqual(legacyCff.hdrSize, newCff.HeaderSize);
        Assert.AreEqual(legacyCff.offSize, newCff.OffSize);

        Assert.AreEqual(legacyCff.Name.count, newName.Count);
        Assert.AreEqual(legacyCff.Name.size, (uint)newName.ByteLength);

        Assert.AreEqual(legacyCff.TopDICT.count, newTopDict.Count);
        Assert.AreEqual(legacyCff.TopDICT.size, (uint)newTopDict.ByteLength);

        Assert.AreEqual(legacyCff.String.count, newStrings.Count);
        Assert.AreEqual(legacyCff.String.size, (uint)newStrings.ByteLength);

        Assert.AreEqual(legacyCff.GlobalSubr.count, newGlobalSubr.Count);
        Assert.AreEqual(legacyCff.GlobalSubr.size, (uint)newGlobalSubr.ByteLength);

        if (newName.Count > 0)
        {
            Assert.IsTrue(newName.TryGetObjectSpan(0, out var newName0));
            Assert.AreEqual(legacyCff.Name.GetString(0), Encoding.ASCII.GetString(newName0));
        }
    }

    [TestMethod]
    public void OpenCffOtf_TopAndPrivateDict_ParseAndMatchLegacy()
    {
        string path = GetFontPath("SourceHanSansCN-Regular.otf");

        using var file = SfntFile.Open(path);
        var font = file.GetFont(0);

        Assert.IsTrue(font.TryGetCff(out var newCff));
        Assert.IsTrue(newCff.TryGetTopDict(out var newTop));

        using var legacyFile = new Legacy.OTFile();
        Assert.IsTrue(legacyFile.open(path));
        var legacyFont = legacyFile.GetFont(0)!;
        var legacyCff = (Legacy.Table_CFF)legacyFont.GetTable("CFF ")!;

        var legacyTop = legacyCff.GetTopDICT(0)!;

        Assert.AreEqual(legacyTop.offsetCharStrings, newTop.CharStringsOffset);
        Assert.AreEqual(legacyTop.offsetCharset, newTop.CharsetIdOrOffset);
        Assert.AreEqual(legacyTop.offsetEncoding, newTop.EncodingIdOrOffset);
        Assert.AreEqual(legacyTop.sizePrivate, newTop.PrivateSize);
        Assert.AreEqual(legacyTop.offsetPrivate, newTop.PrivateOffset);
        Assert.AreEqual(legacyTop.offsetFDArray, newTop.FdArrayOffset);
        Assert.AreEqual(legacyTop.offsetFDSelect, newTop.FdSelectOffset);

        if (newTop.HasFullNameSid)
        {
            Assert.IsTrue(newCff.TryGetSidString(newTop.FullNameSid, out string newFullName));
            Assert.AreEqual(legacyTop.FullName, newFullName);
        }

        if (newTop.HasFontNameSid)
        {
            Assert.IsTrue(newCff.TryGetSidString(newTop.FontNameSid, out string newFontName));
            Assert.AreEqual(legacyTop.FontName, newFontName);
        }

        if (newTop.CharStringsOffset > 0)
        {
            Assert.IsTrue(newTop.TryGetCharStringsIndex(out var newCharStrings));
            var oldCharStrings = legacyCff.GetINDEX(legacyTop.offsetCharStrings);
            Assert.AreEqual(oldCharStrings.count, newCharStrings.Count);
            Assert.AreEqual(oldCharStrings.size, (uint)newCharStrings.ByteLength);
        }

        if (newTop.FdArrayOffset > 0)
        {
            Assert.IsTrue(newTop.TryGetFdArrayIndex(out var newFdArray));
            var oldFdArray = legacyCff.GetINDEX(legacyTop.offsetFDArray);
            Assert.AreEqual(oldFdArray.count, newFdArray.Count);
            Assert.AreEqual(oldFdArray.size, (uint)newFdArray.ByteLength);

            if (newFdArray.Count > 0)
            {
                Assert.IsTrue(newTop.TryGetFontDict(0, out var newFontDict0));
                Assert.IsTrue(newFontDict0.Length > 0);

                if (newFontDict0.TryGetPrivateDict(out var newFontPrivate0))
                {
                    Assert.IsTrue(newFontPrivate0.Length > 0);

                    if (newFontPrivate0.SubrsOffset > 0)
                    {
                        Assert.IsTrue(newFontPrivate0.TryGetSubrsIndex(out var newFontLocalSubrs0));
                        Assert.IsTrue(newFontLocalSubrs0.ByteLength >= 2);
                    }
                }
            }
        }

        if (newTop.FdSelectOffset > 0)
        {
            Assert.IsTrue(newTop.TryGetFdSelect(out var newFdSelect));

            Assert.IsTrue(newTop.TryGetFdArrayIndex(out var newFdArray));
            Assert.IsTrue(newFdArray.Count > 0);

            foreach (int gid in GetSampleGlyphIds(newFdSelect.GlyphCount))
            {
                Assert.IsTrue(newFdSelect.TryGetFontDictIndex(gid, out ushort fdIndex));
                Assert.IsTrue(fdIndex < newFdArray.Count, $"gid={gid}, fd={fdIndex}, fdArrayCount={newFdArray.Count}");

                Assert.IsTrue(newTop.TryGetFontDict(fdIndex, out _));
            }
        }

        var legacyPrivate = legacyCff.GetPrivate(legacyTop);
        if (legacyPrivate is null)
        {
            Assert.IsFalse(newTop.TryGetPrivateDict(out _));
        }
        else
        {
            Assert.IsTrue(newTop.TryGetPrivateDict(out var newPrivate));
            Assert.AreEqual(legacyPrivate.Subrs, newPrivate.SubrsOffset);

            if (newPrivate.SubrsOffset > 0)
            {
                Assert.IsTrue(newPrivate.TryGetSubrsIndex(out var newLocalSubrs));
                var oldLocalSubrs = legacyCff.GetINDEX(legacyTop.offsetPrivate + legacyPrivate.Subrs);
                Assert.AreEqual(oldLocalSubrs.count, newLocalSubrs.Count);
                Assert.AreEqual(oldLocalSubrs.size, (uint)newLocalSubrs.ByteLength);
            }
        }
    }

    private static string GetFontPath(string fileName)
        => Path.Combine(AppContext.BaseDirectory, "TestResources", "SampleFonts", fileName);

    private static IEnumerable<int> GetSampleGlyphIds(int glyphCount)
    {
        if (glyphCount <= 0)
            yield break;

        yield return 0;
        if (glyphCount > 1)
            yield return 1;

        int mid = glyphCount / 2;
        if (mid > 1 && mid < glyphCount - 1)
            yield return mid;

        if (glyphCount > 2)
            yield return glyphCount - 1;
    }
}
