using System.Buffers.Binary;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OTFontFile2;
using OTFontFile2.Tables;
using Legacy = OTFontFile;

namespace OTFontFile2.Tests.UnitTests;

[TestClass]
public sealed class GposCursiveAndMarkAttachmentTests
{
    [TestMethod]
    public void SyntheticGposCursivePosFormat1_CanParseAndMatchesLegacy()
    {
        const ushort glyphId = 55;
        const short entryX = 100;
        const short entryY = -200;
        const short exitX = 300;
        const short exitY = 400;

        byte[] subtable = BuildCursivePosFormat1Subtable(glyphId, entryX, entryY, exitX, exitY);
        byte[] gposData = BuildGposWithSingleLookup(lookupType: 3, subtable, out int subtableOffsetInGpos);

        byte[] fontBytes = BuildFontWithSingleTable(KnownTags.GPOS, gposData);

        using var file = SfntFile.FromMemory(fontBytes);
        var font = file.GetFont(0);

        Assert.IsTrue(font.TryGetGpos(out var gpos));
        Assert.IsTrue(gpos.TryGetLookupList(out var lookupList));
        Assert.IsTrue(lookupList.TryGetLookup(0, out var lookup));
        Assert.AreEqual((ushort)3, lookup.LookupType);
        Assert.IsTrue(lookup.TryGetSubtableOffset(0, out ushort relSub));
        int cursiveOffset = lookup.Offset + relSub;
        Assert.AreEqual(subtableOffsetInGpos, cursiveOffset);

        Assert.IsTrue(GposCursivePosSubtable.TryCreate(gpos.Table, cursiveOffset, out var cursive));
        Assert.AreEqual((ushort)1, cursive.PosFormat);

        Assert.IsTrue(cursive.TryGetCoverage(out var coverage));
        Assert.IsTrue(coverage.TryGetCoverage(glyphId, out bool covered, out ushort covIndex));
        Assert.IsTrue(covered);
        Assert.AreEqual((ushort)0, covIndex);

        Assert.IsTrue(cursive.TryGetEntryExitRecordForGlyph(glyphId, out bool covered2, out ushort covIndex2, out var record));
        Assert.IsTrue(covered2);
        Assert.AreEqual(covIndex, covIndex2);

        Assert.IsTrue(record.TryGetEntryAnchorTable(out bool hasEntry, out var entryAnchor));
        Assert.IsTrue(hasEntry);
        Assert.AreEqual(entryX, entryAnchor.XCoordinate);
        Assert.AreEqual(entryY, entryAnchor.YCoordinate);

        Assert.IsTrue(record.TryGetExitAnchorTable(out bool hasExit, out var exitAnchor));
        Assert.IsTrue(hasExit);
        Assert.AreEqual(exitX, exitAnchor.XCoordinate);
        Assert.AreEqual(exitY, exitAnchor.YCoordinate);

        string tmp = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(tmp, fontBytes);

            using var legacyFile = new Legacy.OTFile();
            Assert.IsTrue(legacyFile.open(tmp));
            var legacyFont = legacyFile.GetFont(0)!;

            var legacyGpos = (Legacy.Table_GPOS)legacyFont.GetTable("GPOS")!;
            var legacyBuf = legacyGpos.GetBuffer();

            var legacyCursive = new Legacy.Table_GPOS.CursivePos((uint)subtableOffsetInGpos, legacyBuf);
            Assert.AreEqual((ushort)1, legacyCursive.PosFormat);

            var legacyCoverage = legacyCursive.GetCoverageTable();
            var legacyCov = legacyCoverage.GetGlyphCoverage(glyphId);
            Assert.IsTrue(legacyCov.bCovered);
            Assert.AreEqual((ushort)0, legacyCov.CoverageIndex);

            var legacyRecord = legacyCursive.GetEntryExitRecord(legacyCov.CoverageIndex);
            Assert.IsNotNull(legacyRecord);

            var legacyEntryAnchor = legacyRecord!.GetEntryAnchorTable();
            var legacyExitAnchor = legacyRecord!.GetExitAnchorTable();

            Assert.IsNotNull(legacyEntryAnchor);
            Assert.IsNotNull(legacyExitAnchor);

            var legacyEntryFmt1 = legacyEntryAnchor!.GetAnchorFormat1();
            var legacyExitFmt1 = legacyExitAnchor!.GetAnchorFormat1();

            Assert.AreEqual(entryX, unchecked((short)legacyEntryFmt1.XCoordinate));
            Assert.AreEqual(entryY, unchecked((short)legacyEntryFmt1.YCoordinate));
            Assert.AreEqual(exitX, unchecked((short)legacyExitFmt1.XCoordinate));
            Assert.AreEqual(exitY, unchecked((short)legacyExitFmt1.YCoordinate));
        }
        finally
        {
            try { File.Delete(tmp); } catch { /* ignore */ }
        }
    }

    [TestMethod]
    public void SyntheticGposMarkBasePosFormat1_CanParseAndMatchesLegacy()
    {
        const ushort markGlyphId = 5;
        const ushort baseGlyphId = 10;
        const short markX = -10;
        const short markY = 20;
        const short baseX = 30;
        const short baseY = -40;

        byte[] subtable = BuildMarkBasePosFormat1Subtable(markGlyphId, baseGlyphId, markX, markY, baseX, baseY);
        byte[] gposData = BuildGposWithSingleLookup(lookupType: 4, subtable, out int subtableOffsetInGpos);

        byte[] fontBytes = BuildFontWithSingleTable(KnownTags.GPOS, gposData);

        using var file = SfntFile.FromMemory(fontBytes);
        var font = file.GetFont(0);

        Assert.IsTrue(font.TryGetGpos(out var gpos));
        Assert.IsTrue(gpos.TryGetLookupList(out var lookupList));
        Assert.IsTrue(lookupList.TryGetLookup(0, out var lookup));
        Assert.AreEqual((ushort)4, lookup.LookupType);
        Assert.IsTrue(lookup.TryGetSubtableOffset(0, out ushort relSub));
        int markBaseOffset = lookup.Offset + relSub;
        Assert.AreEqual(subtableOffsetInGpos, markBaseOffset);

        Assert.IsTrue(GposMarkBasePosSubtable.TryCreate(gpos.Table, markBaseOffset, out var markBase));
        Assert.AreEqual((ushort)1, markBase.PosFormat);
        Assert.AreEqual((ushort)1, markBase.ClassCount);

        Assert.IsTrue(markBase.TryGetAnchorsForGlyphs(markGlyphId, baseGlyphId, out bool positioned, out var markAnchor, out var baseAnchor));
        Assert.IsTrue(positioned);
        Assert.AreEqual(markX, markAnchor.XCoordinate);
        Assert.AreEqual(markY, markAnchor.YCoordinate);
        Assert.AreEqual(baseX, baseAnchor.XCoordinate);
        Assert.AreEqual(baseY, baseAnchor.YCoordinate);

        string tmp = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(tmp, fontBytes);

            using var legacyFile = new Legacy.OTFile();
            Assert.IsTrue(legacyFile.open(tmp));
            var legacyFont = legacyFile.GetFont(0)!;

            var legacyGpos = (Legacy.Table_GPOS)legacyFont.GetTable("GPOS")!;
            var legacyBuf = legacyGpos.GetBuffer();

            var legacyMarkBase = new Legacy.Table_GPOS.MarkBasePos((uint)subtableOffsetInGpos, legacyBuf);
            Assert.AreEqual((ushort)1, legacyMarkBase.PosFormat);
            Assert.AreEqual((ushort)1, legacyMarkBase.ClassCount);

            var legacyMarkCov = legacyMarkBase.GetMarkCoverageTable().GetGlyphCoverage(markGlyphId);
            Assert.IsTrue(legacyMarkCov.bCovered);
            Assert.AreEqual((ushort)0, legacyMarkCov.CoverageIndex);

            var legacyBaseCov = legacyMarkBase.GetBaseCoverageTable().GetGlyphCoverage(baseGlyphId);
            Assert.IsTrue(legacyBaseCov.bCovered);
            Assert.AreEqual((ushort)0, legacyBaseCov.CoverageIndex);

            var legacyMarkArray = legacyMarkBase.GetMarkArrayTable();
            var legacyMarkRecord = legacyMarkArray.GetMarkRecord(0)!;
            Assert.AreEqual((ushort)0, legacyMarkRecord.Class);

            uint legacyMarkArrayAbs = (uint)subtableOffsetInGpos + legacyMarkBase.MarkArrayOffset;
            uint legacyMarkAnchorAbs = legacyMarkArrayAbs + legacyMarkRecord.MarkAnchorOffset;
            var legacyMarkAnchor = new Legacy.Table_GPOS.AnchorTable(legacyMarkAnchorAbs, legacyBuf).GetAnchorFormat1();

            var legacyBaseArray = legacyMarkBase.GetBaseArrayTable();
            var legacyBaseRecord = legacyBaseArray.GetBaseRecord(0)!;
            var legacyBaseAnchor = legacyBaseRecord.GetBaseAnchorTable(0).GetAnchorFormat1();

            Assert.AreEqual(markX, unchecked((short)legacyMarkAnchor.XCoordinate));
            Assert.AreEqual(markY, unchecked((short)legacyMarkAnchor.YCoordinate));
            Assert.AreEqual(baseX, unchecked((short)legacyBaseAnchor.XCoordinate));
            Assert.AreEqual(baseY, unchecked((short)legacyBaseAnchor.YCoordinate));
        }
        finally
        {
            try { File.Delete(tmp); } catch { /* ignore */ }
        }
    }

    [TestMethod]
    public void SyntheticGposMarkLigPosFormat1_CanParseAndMatchesLegacy()
    {
        const ushort markGlyphId = 5;
        const ushort ligatureGlyphId = 12;
        const short markX = 10;
        const short markY = 20;
        const short ligX = -30;
        const short ligY = 40;

        byte[] subtable = BuildMarkLigPosFormat1Subtable(markGlyphId, ligatureGlyphId, markX, markY, ligX, ligY);
        byte[] gposData = BuildGposWithSingleLookup(lookupType: 5, subtable, out int subtableOffsetInGpos);

        byte[] fontBytes = BuildFontWithSingleTable(KnownTags.GPOS, gposData);

        using var file = SfntFile.FromMemory(fontBytes);
        var font = file.GetFont(0);

        Assert.IsTrue(font.TryGetGpos(out var gpos));
        Assert.IsTrue(gpos.TryGetLookupList(out var lookupList));
        Assert.IsTrue(lookupList.TryGetLookup(0, out var lookup));
        Assert.AreEqual((ushort)5, lookup.LookupType);
        Assert.IsTrue(lookup.TryGetSubtableOffset(0, out ushort relSub));
        int markLigOffset = lookup.Offset + relSub;
        Assert.AreEqual(subtableOffsetInGpos, markLigOffset);

        Assert.IsTrue(GposMarkLigPosSubtable.TryCreate(gpos.Table, markLigOffset, out var markLig));
        Assert.AreEqual((ushort)1, markLig.PosFormat);
        Assert.AreEqual((ushort)1, markLig.ClassCount);

        Assert.IsTrue(markLig.TryGetAnchorsForGlyphs(markGlyphId, ligatureGlyphId, componentIndex: 0, out bool positioned, out var markAnchor, out var ligAnchor));
        Assert.IsTrue(positioned);
        Assert.AreEqual(markX, markAnchor.XCoordinate);
        Assert.AreEqual(markY, markAnchor.YCoordinate);
        Assert.AreEqual(ligX, ligAnchor.XCoordinate);
        Assert.AreEqual(ligY, ligAnchor.YCoordinate);

        string tmp = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(tmp, fontBytes);

            using var legacyFile = new Legacy.OTFile();
            Assert.IsTrue(legacyFile.open(tmp));
            var legacyFont = legacyFile.GetFont(0)!;

            var legacyGpos = (Legacy.Table_GPOS)legacyFont.GetTable("GPOS")!;
            var legacyBuf = legacyGpos.GetBuffer();

            var legacyMarkLig = new Legacy.Table_GPOS.MarkLigPos((uint)subtableOffsetInGpos, legacyBuf);
            Assert.AreEqual((ushort)1, legacyMarkLig.PosFormat);
            Assert.AreEqual((ushort)1, legacyMarkLig.ClassCount);

            var legacyMarkCov = legacyMarkLig.GetMarkCoverageTable().GetGlyphCoverage(markGlyphId);
            Assert.IsTrue(legacyMarkCov.bCovered);
            Assert.AreEqual((ushort)0, legacyMarkCov.CoverageIndex);

            var legacyLigCov = legacyMarkLig.GetLigatureCoverageTable().GetGlyphCoverage(ligatureGlyphId);
            Assert.IsTrue(legacyLigCov.bCovered);
            Assert.AreEqual((ushort)0, legacyLigCov.CoverageIndex);

            var legacyMarkArray = legacyMarkLig.GetMarkArrayTable();
            var legacyMarkRecord = legacyMarkArray.GetMarkRecord(0)!;
            Assert.AreEqual((ushort)0, legacyMarkRecord.Class);

            uint legacyMarkArrayAbs = (uint)subtableOffsetInGpos + legacyMarkLig.MarkArrayOffset;
            uint legacyMarkAnchorAbs = legacyMarkArrayAbs + legacyMarkRecord.MarkAnchorOffset;
            var legacyMarkAnchor = new Legacy.Table_GPOS.AnchorTable(legacyMarkAnchorAbs, legacyBuf).GetAnchorFormat1();

            var legacyLigArray = legacyMarkLig.GetLigatureArrayTable();
            var legacyAttach = legacyLigArray.GetLigatureAttachTable(0)!;
            var legacyComponent = legacyAttach.GetComponentRecord(0)!;
            var legacyLigAnchor = legacyComponent.GetLigatureAnchorTable(0)!.GetAnchorFormat1();

            Assert.AreEqual(markX, unchecked((short)legacyMarkAnchor.XCoordinate));
            Assert.AreEqual(markY, unchecked((short)legacyMarkAnchor.YCoordinate));
            Assert.AreEqual(ligX, unchecked((short)legacyLigAnchor.XCoordinate));
            Assert.AreEqual(ligY, unchecked((short)legacyLigAnchor.YCoordinate));
        }
        finally
        {
            try { File.Delete(tmp); } catch { /* ignore */ }
        }
    }

    [TestMethod]
    public void SyntheticGposMarkMarkPosFormat1_CanParseAndMatchesLegacy()
    {
        const ushort mark1GlyphId = 5;
        const ushort mark2GlyphId = 6;
        const short mark1X = -100;
        const short mark1Y = 200;
        const short mark2X = 300;
        const short mark2Y = -400;

        byte[] subtable = BuildMarkMarkPosFormat1Subtable(mark1GlyphId, mark2GlyphId, mark1X, mark1Y, mark2X, mark2Y);
        byte[] gposData = BuildGposWithSingleLookup(lookupType: 6, subtable, out int subtableOffsetInGpos);

        byte[] fontBytes = BuildFontWithSingleTable(KnownTags.GPOS, gposData);

        using var file = SfntFile.FromMemory(fontBytes);
        var font = file.GetFont(0);

        Assert.IsTrue(font.TryGetGpos(out var gpos));
        Assert.IsTrue(gpos.TryGetLookupList(out var lookupList));
        Assert.IsTrue(lookupList.TryGetLookup(0, out var lookup));
        Assert.AreEqual((ushort)6, lookup.LookupType);
        Assert.IsTrue(lookup.TryGetSubtableOffset(0, out ushort relSub));
        int markMarkOffset = lookup.Offset + relSub;
        Assert.AreEqual(subtableOffsetInGpos, markMarkOffset);

        Assert.IsTrue(GposMarkMarkPosSubtable.TryCreate(gpos.Table, markMarkOffset, out var markMark));
        Assert.AreEqual((ushort)1, markMark.PosFormat);
        Assert.AreEqual((ushort)1, markMark.ClassCount);

        Assert.IsTrue(markMark.TryGetAnchorsForGlyphs(mark1GlyphId, mark2GlyphId, out bool positioned, out var mark1Anchor, out var mark2Anchor));
        Assert.IsTrue(positioned);
        Assert.AreEqual(mark1X, mark1Anchor.XCoordinate);
        Assert.AreEqual(mark1Y, mark1Anchor.YCoordinate);
        Assert.AreEqual(mark2X, mark2Anchor.XCoordinate);
        Assert.AreEqual(mark2Y, mark2Anchor.YCoordinate);

        string tmp = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(tmp, fontBytes);

            using var legacyFile = new Legacy.OTFile();
            Assert.IsTrue(legacyFile.open(tmp));
            var legacyFont = legacyFile.GetFont(0)!;

            var legacyGpos = (Legacy.Table_GPOS)legacyFont.GetTable("GPOS")!;
            var legacyBuf = legacyGpos.GetBuffer();

            var legacyMarkMark = new Legacy.Table_GPOS.MarkMarkPos((uint)subtableOffsetInGpos, legacyBuf);
            Assert.AreEqual((ushort)1, legacyMarkMark.PosFormat);
            Assert.AreEqual((ushort)1, legacyMarkMark.ClassCount);

            var legacyMark1Cov = legacyMarkMark.GetMark1CoverageTable().GetGlyphCoverage(mark1GlyphId);
            Assert.IsTrue(legacyMark1Cov.bCovered);
            Assert.AreEqual((ushort)0, legacyMark1Cov.CoverageIndex);

            var legacyMark2Cov = legacyMarkMark.GetMark2CoverageTable().GetGlyphCoverage(mark2GlyphId);
            Assert.IsTrue(legacyMark2Cov.bCovered);
            Assert.AreEqual((ushort)0, legacyMark2Cov.CoverageIndex);

            var legacyMark1Array = legacyMarkMark.GetMark1ArrayTable();
            var legacyMark1Record = legacyMark1Array.GetMarkRecord(0)!;
            Assert.AreEqual((ushort)0, legacyMark1Record.Class);

            uint legacyMark1ArrayAbs = (uint)subtableOffsetInGpos + legacyMarkMark.Mark1ArrayOffset;
            uint legacyMark1AnchorAbs = legacyMark1ArrayAbs + legacyMark1Record.MarkAnchorOffset;
            var legacyMark1Anchor = new Legacy.Table_GPOS.AnchorTable(legacyMark1AnchorAbs, legacyBuf).GetAnchorFormat1();

            var legacyMark2Array = legacyMarkMark.GetMark2ArrayTable();
            var legacyMark2Record = legacyMark2Array.GetMark2Record(0)!;
            var legacyMark2Anchor = legacyMark2Record.GetMark2AnchorTable(0).GetAnchorFormat1();

            Assert.AreEqual(mark1X, unchecked((short)legacyMark1Anchor.XCoordinate));
            Assert.AreEqual(mark1Y, unchecked((short)legacyMark1Anchor.YCoordinate));
            Assert.AreEqual(mark2X, unchecked((short)legacyMark2Anchor.XCoordinate));
            Assert.AreEqual(mark2Y, unchecked((short)legacyMark2Anchor.YCoordinate));
        }
        finally
        {
            try { File.Delete(tmp); } catch { /* ignore */ }
        }
    }

    private static byte[] BuildFontWithSingleTable(Tag tag, byte[] tableBytes)
    {
        byte[] head = new byte[12];
        head[0] = 0xDE;
        head[1] = 0xAD;
        head[2] = 0xBE;
        head[3] = 0xEF;

        using var ms = new MemoryStream();
        SfntWriter.Write(
            destination: ms,
            sfntVersion: 0x00010000u,
            tables: new ISfntTableSource[]
            {
                new MemoryTableSource(KnownTags.head, head),
                new MemoryTableSource(tag, tableBytes)
            });
        return ms.ToArray();
    }

    private static byte[] BuildGposWithSingleLookup(ushort lookupType, byte[] subtable, out int subtableOffset)
    {
        const int headerOffset = 0;
        const int scriptListOffset = 10;
        const int scriptListLength = 20;
        const int featureListOffset = scriptListOffset + scriptListLength; // 30
        const int featureListLength = 14;
        const int lookupListOffset = featureListOffset + featureListLength; // 44

        const int lookupListHeaderLength = 4; // count + first offset
        const int lookupTableLength = 8;       // type, flag, subCount, subOffset[0]

        int lookupTableOffset = lookupListOffset + lookupListHeaderLength; // 48
        subtableOffset = lookupTableOffset + lookupTableLength;             // 56

        int totalLength = subtableOffset + subtable.Length;
        byte[] gpos = new byte[totalLength];
        var span = gpos.AsSpan();

        // GPOS header
        BinaryPrimitives.WriteUInt32BigEndian(span.Slice(headerOffset, 4), 0x00010000u); // version 1.0
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(4, 2), (ushort)scriptListOffset);
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(6, 2), (ushort)featureListOffset);
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(8, 2), (ushort)lookupListOffset);

        // ScriptList @ 10
        int scriptList = scriptListOffset;
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(scriptList + 0, 2), 1); // ScriptCount
        WriteTag(span, scriptList + 2, "DFLT");
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(scriptList + 6, 2), 8); // ScriptTableOffset

        int scriptTable = scriptList + 8;
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(scriptTable + 0, 2), 4); // DefaultLangSysOffset
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(scriptTable + 2, 2), 0); // LangSysCount

        int langSys = scriptTable + 4;
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(langSys + 0, 2), 0);      // LookupOrder
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(langSys + 2, 2), 0xFFFF); // ReqFeatureIndex
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(langSys + 4, 2), 1);      // FeatureCount
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(langSys + 6, 2), 0);      // FeatureIndex[0]

        // FeatureList @ 30
        int featureList = featureListOffset;
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(featureList + 0, 2), 1); // FeatureCount
        WriteTag(span, featureList + 2, "kern");
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(featureList + 6, 2), 8); // FeatureTableOffset

        int featureTable = featureList + 8;
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(featureTable + 0, 2), 0); // FeatureParamsOffset
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(featureTable + 2, 2), 1); // LookupCount
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(featureTable + 4, 2), 0); // LookupListIndex[0]

        // LookupList @ 44
        int lookupList = lookupListOffset;
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(lookupList + 0, 2), 1); // LookupCount
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(lookupList + 2, 2), 4); // LookupOffset[0]

        // Lookup table @ 48
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(lookupTableOffset + 0, 2), lookupType);
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(lookupTableOffset + 2, 2), 0); // LookupFlag
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(lookupTableOffset + 4, 2), 1); // SubTableCount
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(lookupTableOffset + 6, 2), (ushort)lookupTableLength); // SubtableOffset[0]

        subtable.CopyTo(span.Slice(subtableOffset));
        return gpos;
    }

    private static byte[] BuildCursivePosFormat1Subtable(ushort glyphId, short entryX, short entryY, short exitX, short exitY)
    {
        // header(6) + entryExitRecord(4) + entryAnchor(6) + exitAnchor(6) + coverage(6)
        byte[] st = new byte[28];
        var span = st.AsSpan();

        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(0, 2), 1);   // PosFormat
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(2, 2), 22);  // CoverageOffset
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(4, 2), 1);   // EntryExitCount
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(6, 2), 10);  // EntryAnchorOffset
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(8, 2), 16);  // ExitAnchorOffset

        // Entry anchor @ 10 (format 1)
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(10, 2), 1);
        BinaryPrimitives.WriteInt16BigEndian(span.Slice(12, 2), entryX);
        BinaryPrimitives.WriteInt16BigEndian(span.Slice(14, 2), entryY);

        // Exit anchor @ 16 (format 1)
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(16, 2), 1);
        BinaryPrimitives.WriteInt16BigEndian(span.Slice(18, 2), exitX);
        BinaryPrimitives.WriteInt16BigEndian(span.Slice(20, 2), exitY);

        // Coverage @ 22 (format 1, 1 glyph)
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(22, 2), 1);
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(24, 2), 1);
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(26, 2), glyphId);

        return st;
    }

    private static byte[] BuildMarkBasePosFormat1Subtable(ushort markGlyphId, ushort baseGlyphId, short markX, short markY, short baseX, short baseY)
    {
        // header(12) + markArray(12) + baseArray(10) + markCoverage(6) + baseCoverage(6)
        byte[] st = new byte[46];
        var span = st.AsSpan();

        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(0, 2), 1);   // PosFormat
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(2, 2), 34);  // MarkCoverageOffset
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(4, 2), 40);  // BaseCoverageOffset
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(6, 2), 1);   // ClassCount
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(8, 2), 12);  // MarkArrayOffset
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(10, 2), 24); // BaseArrayOffset

        // MarkArray @ 12
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(12, 2), 1); // MarkCount
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(14, 2), 0); // class
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(16, 2), 6); // markAnchorOffset (from MarkArray)

        // MarkAnchor @ 18 (format 1)
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(18, 2), 1);
        BinaryPrimitives.WriteInt16BigEndian(span.Slice(20, 2), markX);
        BinaryPrimitives.WriteInt16BigEndian(span.Slice(22, 2), markY);

        // BaseArray (AnchorMatrix) @ 24
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(24, 2), 1); // rows/baseCount
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(26, 2), 4); // anchorOffset[0] (from BaseArray)

        // BaseAnchor @ 28 (format 1)
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(28, 2), 1);
        BinaryPrimitives.WriteInt16BigEndian(span.Slice(30, 2), baseX);
        BinaryPrimitives.WriteInt16BigEndian(span.Slice(32, 2), baseY);

        // MarkCoverage @ 34
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(34, 2), 1); // CoverageFormat
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(36, 2), 1); // GlyphCount
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(38, 2), markGlyphId);

        // BaseCoverage @ 40
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(40, 2), 1);
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(42, 2), 1);
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(44, 2), baseGlyphId);

        return st;
    }

    private static byte[] BuildMarkLigPosFormat1Subtable(ushort markGlyphId, ushort ligatureGlyphId, short markX, short markY, short ligX, short ligY)
    {
        // header(12) + markArray(12) + ligatureArray(4) + ligatureAttach(10) + markCoverage(6) + ligCoverage(6)
        byte[] st = new byte[50];
        var span = st.AsSpan();

        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(0, 2), 1);   // PosFormat
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(2, 2), 38);  // MarkCoverageOffset
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(4, 2), 44);  // LigatureCoverageOffset
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(6, 2), 1);   // ClassCount
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(8, 2), 12);  // MarkArrayOffset
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(10, 2), 24); // LigatureArrayOffset

        // MarkArray @ 12
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(12, 2), 1); // MarkCount
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(14, 2), 0); // class
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(16, 2), 6); // markAnchorOffset (from MarkArray)

        // MarkAnchor @ 18 (format 1)
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(18, 2), 1);
        BinaryPrimitives.WriteInt16BigEndian(span.Slice(20, 2), markX);
        BinaryPrimitives.WriteInt16BigEndian(span.Slice(22, 2), markY);

        // LigatureArray @ 24
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(24, 2), 1); // LigatureCount
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(26, 2), 4); // LigatureAttachOffset[0] (from LigatureArray)

        // LigatureAttach (AnchorMatrix) @ 28
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(28, 2), 1); // rows/componentCount
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(30, 2), 4); // anchorOffset[0] (from LigatureAttach)

        // LigatureAnchor @ 32 (format 1)
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(32, 2), 1);
        BinaryPrimitives.WriteInt16BigEndian(span.Slice(34, 2), ligX);
        BinaryPrimitives.WriteInt16BigEndian(span.Slice(36, 2), ligY);

        // MarkCoverage @ 38
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(38, 2), 1);
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(40, 2), 1);
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(42, 2), markGlyphId);

        // LigatureCoverage @ 44
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(44, 2), 1);
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(46, 2), 1);
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(48, 2), ligatureGlyphId);

        return st;
    }

    private static byte[] BuildMarkMarkPosFormat1Subtable(ushort mark1GlyphId, ushort mark2GlyphId, short mark1X, short mark1Y, short mark2X, short mark2Y)
    {
        // header(12) + mark1Array(12) + mark2Array(10) + mark1Coverage(6) + mark2Coverage(6)
        byte[] st = new byte[46];
        var span = st.AsSpan();

        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(0, 2), 1);   // PosFormat
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(2, 2), 34);  // Mark1CoverageOffset
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(4, 2), 40);  // Mark2CoverageOffset
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(6, 2), 1);   // ClassCount
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(8, 2), 12);  // Mark1ArrayOffset
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(10, 2), 24); // Mark2ArrayOffset

        // Mark1Array @ 12
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(12, 2), 1); // MarkCount
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(14, 2), 0); // class
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(16, 2), 6); // markAnchorOffset (from MarkArray)

        // Mark1Anchor @ 18 (format 1)
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(18, 2), 1);
        BinaryPrimitives.WriteInt16BigEndian(span.Slice(20, 2), mark1X);
        BinaryPrimitives.WriteInt16BigEndian(span.Slice(22, 2), mark1Y);

        // Mark2Array (AnchorMatrix) @ 24
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(24, 2), 1); // rows/mark2Count
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(26, 2), 4); // anchorOffset[0] (from Mark2Array)

        // Mark2Anchor @ 28 (format 1)
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(28, 2), 1);
        BinaryPrimitives.WriteInt16BigEndian(span.Slice(30, 2), mark2X);
        BinaryPrimitives.WriteInt16BigEndian(span.Slice(32, 2), mark2Y);

        // Mark1Coverage @ 34
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(34, 2), 1);
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(36, 2), 1);
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(38, 2), mark1GlyphId);

        // Mark2Coverage @ 40
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(40, 2), 1);
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(42, 2), 1);
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(44, 2), mark2GlyphId);

        return st;
    }

    private static void WriteTag(Span<byte> data, int offset, string tag)
    {
        if (tag.Length != 4)
            throw new ArgumentException("tag must be 4 characters", nameof(tag));

        Encoding.ASCII.GetBytes(tag).CopyTo(data.Slice(offset, 4));
    }
}
