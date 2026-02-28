using Microsoft.VisualStudio.TestTools.UnitTesting;
using OTFontFile2.Tables;
using System.Buffers.Binary;
using Legacy = OTFontFile;

namespace OTFontFile2.Tests.UnitTests;

[TestClass]
public sealed class BaseAndJstfTablesTests
{
    [TestMethod]
    public void OpenCffOtf_BaseTable_MatchesLegacy()
    {
        string path = GetFontPath("SourceHanSansCN-Regular.otf");

        using var file = SfntFile.Open(path);
        var font = file.GetFont(0);

        Assert.IsTrue(font.TryGetBase(out var newBase));

        using var legacyFile = new Legacy.OTFile();
        Assert.IsTrue(legacyFile.open(path));
        var legacyFont = legacyFile.GetFont(0)!;

        var legacyBase = (Legacy.Table_BASE)legacyFont.GetTable("BASE")!;

        Assert.AreEqual(legacyBase.Version.GetUint(), newBase.Version.RawValue);
        Assert.AreEqual(legacyBase.HorizAxisOffset, newBase.HorizAxisOffset);
        Assert.AreEqual(legacyBase.VertAxisOffset, newBase.VertAxisOffset);

        if (newBase.HorizAxisOffset != 0)
        {
            Assert.IsTrue(newBase.TryGetHorizAxis(out var newAxis));

            var legacyAxis = legacyBase.GetHorizAxisTable();
            Assert.AreEqual(legacyAxis.BaseTagListOffset, newAxis.BaseTagListOffset);
            Assert.AreEqual(legacyAxis.BaseScriptListOffset, newAxis.BaseScriptListOffset);

            var legacyTags = legacyAxis.GetBaseTagListTable();
            if (legacyTags == null)
            {
                Assert.IsFalse(newAxis.TryGetBaseTagList(out _));
            }
            else
            {
                Assert.IsTrue(newAxis.TryGetBaseTagList(out var newTags));
                Assert.AreEqual(legacyTags.BaseTagCount, newTags.BaseTagCount);

                int count = newTags.BaseTagCount;
                for (int i = 0; i < count; i++)
                {
                    var oldTag = legacyTags.GetBaselineTag((uint)i);
                    Assert.IsNotNull(oldTag);
                    Assert.IsTrue(newTags.TryGetBaselineTag(i, out var newTag));
                    Assert.AreEqual(oldTag!.ToString(), newTag.ToString());
                }
            }

            var legacyScripts = legacyAxis.GetBaseScriptListTable();
            if (legacyScripts == null)
            {
                Assert.IsFalse(newAxis.TryGetBaseScriptList(out _));
            }
            else
            {
                Assert.IsTrue(newAxis.TryGetBaseScriptList(out var newScripts));
                Assert.AreEqual(legacyScripts.BaseScriptCount, newScripts.BaseScriptCount);

                if (newScripts.BaseScriptCount > 0)
                {
                    Assert.IsTrue(newScripts.TryGetBaseScriptRecord(0, out var newScript0));
                    var oldScript0 = legacyScripts.GetBaseScriptRecord(0)!;
                    Assert.AreEqual(oldScript0.BaseScriptTag.ToString(), newScript0.BaseScriptTag.ToString());
                    Assert.AreEqual(oldScript0.BaseScriptOffset, newScript0.BaseScriptOffset);

                    Assert.IsTrue(newScripts.TryFindBaseScriptRecord(newScript0.BaseScriptTag, out var baseScriptByTag));
                    Assert.AreEqual(newScript0.BaseScriptOffset, baseScriptByTag.BaseScriptOffset);
                    Assert.IsTrue(newScripts.TryFindBaseScript(newScript0.BaseScriptTag, out var baseScriptTableByTag));

                    var oldScriptTable0 = legacyScripts.GetBaseScriptTable(oldScript0)!;
                    Assert.IsTrue(newScripts.TryGetBaseScript(newScript0, out var newScriptTable0));
                    Assert.AreEqual(newScriptTable0.BaseValuesOffset, baseScriptTableByTag.BaseValuesOffset);

                    Assert.AreEqual(oldScriptTable0.BaseValuesOffset, newScriptTable0.BaseValuesOffset);
                    Assert.AreEqual(oldScriptTable0.DefaultMinMaxOffset, newScriptTable0.DefaultMinMaxOffset);
                    Assert.AreEqual(oldScriptTable0.BaseLangSysCount, newScriptTable0.BaseLangSysCount);

                    var oldValues = oldScriptTable0.GetBaseValuesTable();
                    if (oldValues is not null)
                    {
                        Assert.IsTrue(newScriptTable0.TryGetBaseValues(out var newValues));
                        Assert.AreEqual(oldValues.DefaultIndex, newValues.DefaultIndex);
                        Assert.AreEqual(oldValues.BaseCoordCount, newValues.BaseCoordCount);

                        if (newValues.BaseCoordCount > 0)
                        {
                            var oldCoord0 = oldValues.GetBaseCoordTable(0)!;
                            Assert.IsTrue(newValues.TryGetBaseCoord(0, out var newCoord0));
                            Assert.AreEqual(oldCoord0.BaseCoordFormat, newCoord0.BaseCoordFormat);
                            Assert.AreEqual(unchecked((short)oldCoord0.Coordinate), newCoord0.Coordinate);
                        }
                    }
                }
            }
        }
    }

    [TestMethod]
    public void SyntheticJstfTable_ParsesAndMatchesLegacy()
    {
        byte[] jstfBytes = BuildSyntheticJstfTable();

        var builder = new SfntBuilder();
        builder.SetTable(KnownTags.JSTF, jstfBytes);
        byte[] fontBytes = builder.ToArray();

        using var file = SfntFile.FromMemory(fontBytes);
        var font = file.GetFont(0);

        Assert.IsTrue(font.TryGetJstf(out var newJstf));
        Assert.AreEqual(0x00010000u, newJstf.Version.RawValue);
        Assert.AreEqual((ushort)1, newJstf.ScriptCount);

        Assert.IsTrue(newJstf.TryGetScriptRecord(0, out var newRec0));
        Assert.AreEqual("latn", newRec0.ScriptTag.ToString());
        Assert.IsTrue(newJstf.TryGetScript(newRec0, out var newScript));

        Assert.IsTrue(Tag.TryParse("latn", out var latnTag));
        Assert.IsTrue(newJstf.TryFindScriptRecord(latnTag, out var recByTag));
        Assert.AreEqual(newRec0.ScriptOffset, recByTag.ScriptOffset);
        Assert.IsTrue(newJstf.TryFindScript(latnTag, out var scriptByTag));
        Assert.AreEqual(newScript.DefaultLangSysOffset, scriptByTag.DefaultLangSysOffset);

        Assert.AreEqual((ushort)0, newScript.ExtenderGlyphOffset);
        Assert.AreEqual((ushort)6, newScript.DefaultLangSysOffset);
        Assert.AreEqual((ushort)0, newScript.LangSysCount);

        Assert.IsTrue(newScript.TryGetDefaultLangSys(out var newLangSys));
        Assert.AreEqual((ushort)1, newLangSys.PriorityCount);

        Assert.IsTrue(newLangSys.TryGetPriority(0, out var newPri));
        Assert.IsTrue(newPri.TryGetShrinkageEnableGsub(out var newGsubMod));
        Assert.AreEqual((ushort)2, newGsubMod.LookupCount);
        Assert.IsTrue(newGsubMod.TryGetLookupIndex(0, out ushort gsub0));
        Assert.IsTrue(newGsubMod.TryGetLookupIndex(1, out ushort gsub1));
        Assert.AreEqual((ushort)3, gsub0);
        Assert.AreEqual((ushort)7, gsub1);

        Assert.IsTrue(newPri.TryGetShrinkageEnableGpos(out var newGposMod));
        Assert.AreEqual((ushort)1, newGposMod.LookupCount);
        Assert.IsTrue(newGposMod.TryGetLookupIndex(0, out ushort gpos0));
        Assert.AreEqual((ushort)5, gpos0);

        Assert.IsTrue(newPri.TryGetShrinkageJstfMax(out var newMax));
        Assert.AreEqual((ushort)1, newMax.LookupCount);
        Assert.IsTrue(newMax.TryGetLookup(0, out var newLookup));
        Assert.AreEqual((ushort)1, newLookup.LookupType);
        Assert.AreEqual((ushort)0, newLookup.LookupFlag);
        Assert.AreEqual((ushort)1, newLookup.SubtableCount);
        Assert.IsTrue(newLookup.TryGetSubtableOffset(0, out ushort subOff));
        Assert.AreEqual((ushort)8, subOff);

        string tempPath = Path.Combine(Path.GetTempPath(), $"synthetic-jstf-{Guid.NewGuid():N}.ttf");
        try
        {
            File.WriteAllBytes(tempPath, fontBytes);

            using var legacyFile = new Legacy.OTFile();
            Assert.IsTrue(legacyFile.open(tempPath));
            var legacyFont = legacyFile.GetFont(0)!;
            var legacyJstf = (Legacy.Table_JSTF)legacyFont.GetTable("JSTF")!;

            Assert.AreEqual(legacyJstf.Version.GetUint(), newJstf.Version.RawValue);
            Assert.AreEqual(legacyJstf.JstfScriptCount, newJstf.ScriptCount);

            var legacyRec0 = legacyJstf.GetJstfScriptRecord(0)!;
            Assert.AreEqual(legacyRec0.JstfScriptTag.ToString(), newRec0.ScriptTag.ToString());
            Assert.AreEqual(legacyRec0.JstfScriptOffset, newRec0.ScriptOffset);

            var legacyScript = legacyRec0.GetJstfScriptTable();
            Assert.AreEqual(legacyScript.ExtenderGlyphOffset, newScript.ExtenderGlyphOffset);
            Assert.AreEqual(legacyScript.DefJstfLangSysOffset, newScript.DefaultLangSysOffset);
            Assert.AreEqual(legacyScript.JstfLangSysCount, newScript.LangSysCount);

            var legacyLangSys = legacyScript.GetDefJstfLangSysTable();
            Assert.IsNotNull(legacyLangSys);
            Assert.AreEqual(legacyLangSys!.JstfPriorityCount, newLangSys.PriorityCount);

            var legacyPri = legacyLangSys.GetJstfPriorityTable(0)!;
            var legacyGsub = legacyPri.GetShrinkageEnableGSUBTable();
            Assert.IsNotNull(legacyGsub);
            Assert.AreEqual(legacyGsub!.LookupCount, newGsubMod.LookupCount);
            Assert.AreEqual(legacyGsub.GetGSUBLookupIndex(0), gsub0);
            Assert.AreEqual(legacyGsub.GetGSUBLookupIndex(1), gsub1);

            var legacyGpos = legacyPri.GetShrinkageEnableGPOSTable();
            Assert.IsNotNull(legacyGpos);
            Assert.AreEqual(legacyGpos!.LookupCount, newGposMod.LookupCount);
            Assert.AreEqual(legacyGpos.GetGPOSLookupIndex(0), gpos0);

            var legacyMax = legacyPri.GetShrinkageJstfMaxTable();
            Assert.IsNotNull(legacyMax);
            Assert.AreEqual(legacyMax!.LookupCount, newMax.LookupCount);

            var legacyLookup = legacyMax.GetLookupTable(0);
            Assert.AreEqual(legacyLookup.LookupType, newLookup.LookupType);
            Assert.AreEqual(legacyLookup.LookupFlag, newLookup.LookupFlag);
            Assert.AreEqual(legacyLookup.SubTableCount, newLookup.SubtableCount);
        }
        finally
        {
            try { File.Delete(tempPath); } catch { }
        }
    }

    private static byte[] BuildSyntheticJstfTable()
    {
        // A minimal JSTF with one script ("latn"), one default langsys, one priority:
        // - ShrinkageEnableGSUBModList: [3, 7]
        // - ShrinkageEnableGPOSModList: [5]
        // - ShrinkageJstfMax: one lookup table (type=1, one subtable offset=8)
        byte[] bytes = new byte[66];
        var span = bytes.AsSpan();

        WriteU32(span, 0, 0x00010000u); // version
        WriteU16(span, 4, 1); // scriptCount

        WriteTag(span, 6, "latn");
        WriteU16(span, 10, 12); // scriptOffset

        int script = 12;
        WriteU16(span, script + 0, 0); // extenderGlyphOffset
        WriteU16(span, script + 2, 6); // defLangSysOffset (script+6 = 18)
        WriteU16(span, script + 4, 0); // langSysCount

        int langSys = script + 6; // 18
        WriteU16(span, langSys + 0, 1); // priorityCount
        WriteU16(span, langSys + 2, 4); // priorityOffset[0] (langSys+4 = 22)

        int pri = langSys + 4; // 22
        WriteU16(span, pri + 0, 20); // shrinkEnableGSUB (pri+20 = 42)
        WriteU16(span, pri + 2, 0);
        WriteU16(span, pri + 4, 26); // shrinkEnableGPOS (pri+26 = 48)
        WriteU16(span, pri + 6, 0);
        WriteU16(span, pri + 8, 30); // shrinkJstfMax (pri+30 = 52)
        WriteU16(span, pri + 10, 0);
        WriteU16(span, pri + 12, 0);
        WriteU16(span, pri + 14, 0);
        WriteU16(span, pri + 16, 0);
        WriteU16(span, pri + 18, 0);

        int gsubMod = pri + 20; // 42
        WriteU16(span, gsubMod + 0, 2); // lookupCount
        WriteU16(span, gsubMod + 2, 3);
        WriteU16(span, gsubMod + 4, 7);

        int gposMod = gsubMod + 6; // 48
        WriteU16(span, gposMod + 0, 1); // lookupCount
        WriteU16(span, gposMod + 2, 5);

        int max = gposMod + 4; // 52
        WriteU16(span, max + 0, 1); // lookupCount
        WriteU16(span, max + 2, 4); // lookupOffset[0] (max+4 = 56)

        int lookup = max + 4; // 56
        WriteU16(span, lookup + 0, 1); // lookupType
        WriteU16(span, lookup + 2, 0); // lookupFlag
        WriteU16(span, lookup + 4, 1); // subtableCount
        WriteU16(span, lookup + 6, 8); // subtableOffset[0]
        WriteU16(span, lookup + 8, 1); // dummy subtable data

        return bytes;
    }

    private static void WriteU16(Span<byte> data, int offset, ushort value)
        => BinaryPrimitives.WriteUInt16BigEndian(data.Slice(offset, 2), value);

    private static void WriteU32(Span<byte> data, int offset, uint value)
        => BinaryPrimitives.WriteUInt32BigEndian(data.Slice(offset, 4), value);

    private static void WriteTag(Span<byte> data, int offset, string tag)
    {
        Assert.AreEqual(4, tag.Length);
        data[offset + 0] = (byte)tag[0];
        data[offset + 1] = (byte)tag[1];
        data[offset + 2] = (byte)tag[2];
        data[offset + 3] = (byte)tag[3];
    }

    private static string GetFontPath(string fileName)
        => Path.Combine(AppContext.BaseDirectory, "TestResources", "SampleFonts", fileName);
}
