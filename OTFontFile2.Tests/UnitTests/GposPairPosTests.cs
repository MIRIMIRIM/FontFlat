using System.Buffers.Binary;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OTFontFile2.Tables;
using Legacy = OTFontFile;

namespace OTFontFile2.Tests.UnitTests;

[TestClass]
public sealed class GposPairPosTests
{
    [TestMethod]
    public void SyntheticGposPairPosFormat1_CanParseAndMatchesLegacy()
    {
        const ushort firstGlyph = 10;
        const ushort secondGlyph0 = 20;
        const ushort secondGlyph1 = 21;

        const short v1_0 = -30;
        const short v2_0 = 0;
        const short v1_1 = 10;
        const short v2_1 = 5;

        byte[] gposData = BuildSyntheticGposPairPosFormat1(
            firstGlyph,
            secondGlyph0, v1_0, v2_0,
            secondGlyph1, v1_1, v2_1,
            out int pairPosOffsetInGpos);

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
                new MemoryTableSource(KnownTags.GPOS, gposData)
            });

        byte[] fontBytes = ms.ToArray();

        using var file = SfntFile.FromMemory(fontBytes);
        var font = file.GetFont(0);
        Assert.IsTrue(font.TryGetGpos(out var gpos));
        Assert.IsTrue(gpos.TryGetLookupList(out var lookupList));

        Assert.IsTrue(lookupList.TryGetLookup(0, out var lookup));
        Assert.AreEqual((ushort)2, lookup.LookupType);
        Assert.IsTrue(lookup.TryGetSubtableOffset(0, out ushort relSub));
        int pairPosOffset = lookup.Offset + relSub;
        Assert.AreEqual(pairPosOffsetInGpos, pairPosOffset);

        Assert.IsTrue(GposPairPosSubtable.TryCreate(gpos.Table, pairPosOffset, out var pairPos));
        Assert.AreEqual((ushort)1, pairPos.PosFormat);

        Assert.IsTrue(pairPos.TryGetPairAdjustment(firstGlyph, secondGlyph0, out bool positioned0, out var newValue1_0, out var newValue2_0));
        Assert.IsTrue(positioned0);
        Assert.IsTrue(newValue1_0.TryGetXAdvance(out short newV1_0));
        Assert.IsTrue(newValue2_0.TryGetXAdvance(out short newV2_0));
        Assert.AreEqual(v1_0, newV1_0);
        Assert.AreEqual(v2_0, newV2_0);

        Assert.IsTrue(pairPos.TryGetPairAdjustment(firstGlyph, secondGlyph1, out bool positioned1, out var newValue1_1, out var newValue2_1));
        Assert.IsTrue(positioned1);
        Assert.IsTrue(newValue1_1.TryGetXAdvance(out short newV1_1));
        Assert.IsTrue(newValue2_1.TryGetXAdvance(out short newV2_1));
        Assert.AreEqual(v1_1, newV1_1);
        Assert.AreEqual(v2_1, newV2_1);

        Assert.IsTrue(pairPos.TryGetPairAdjustment(firstGlyph, (ushort)(secondGlyph1 + 1), out bool notPositioned, out _, out _));
        Assert.IsFalse(notPositioned);

        Assert.IsTrue(pairPos.TryGetPairAdjustment((ushort)(firstGlyph + 1), secondGlyph0, out bool notCovered, out _, out _));
        Assert.IsFalse(notCovered);

        string tmp = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(tmp, fontBytes);

            using var legacyFile = new Legacy.OTFile();
            Assert.IsTrue(legacyFile.open(tmp));
            var legacyFont = legacyFile.GetFont(0)!;

            var legacyGpos = (Legacy.Table_GPOS)legacyFont.GetTable("GPOS")!;
            var legacyBuf = legacyGpos.GetBuffer();

            var legacyPair = new Legacy.Table_GPOS.PairPos((uint)pairPosOffsetInGpos, legacyBuf);
            Assert.AreEqual((ushort)1, legacyPair.PosFormat);

            var legacyF1 = legacyPair.GetPairPosFormat1();
            Assert.AreEqual((ushort)0x0004, legacyF1.ValueFormat1);
            Assert.AreEqual((ushort)0x0004, legacyF1.ValueFormat2);

            var legacyCoverage = legacyF1.GetCoverageTable();
            var legacyCov = legacyCoverage.GetGlyphCoverage(firstGlyph);
            Assert.IsTrue(legacyCov.bCovered);
            Assert.AreEqual((ushort)0, legacyCov.CoverageIndex);

            var legacyPairSet = legacyF1.GetPairSetTable(legacyCov.CoverageIndex)!;

            var legacyPvr0 = FindLegacyPairValueRecord(legacyPairSet, secondGlyph0);
            var legacyPvr1 = FindLegacyPairValueRecord(legacyPairSet, secondGlyph1);

            short legacyV1_0 = unchecked((short)legacyPvr0.Value1.XAdvance);
            short legacyV2_0 = unchecked((short)legacyPvr0.Value2.XAdvance);
            short legacyV1_1 = unchecked((short)legacyPvr1.Value1.XAdvance);
            short legacyV2_1 = unchecked((short)legacyPvr1.Value2.XAdvance);

            Assert.AreEqual(v1_0, legacyV1_0);
            Assert.AreEqual(v2_0, legacyV2_0);
            Assert.AreEqual(v1_1, legacyV1_1);
            Assert.AreEqual(v2_1, legacyV2_1);
        }
        finally
        {
            try { File.Delete(tmp); } catch { /* ignore */ }
        }
    }

    [TestMethod]
    public void SyntheticGposPairPosFormat2_CanParseAndMatchesLegacy()
    {
        const ushort firstGlyphClass0 = 10;
        const ushort firstGlyphClass1 = 11;

        const ushort secondGlyph0 = 20;
        const ushort secondGlyph1 = 21;

        const short v1_0 = -30;
        const short v2_0 = 0;
        const short v1_1 = 10;
        const short v2_1 = 5;

        const short v1_2 = -2;
        const short v2_2 = 3;
        const short v1_3 = 7;
        const short v2_3 = -9;

        byte[] gposData = BuildSyntheticGposPairPosFormat2(
            firstGlyphClass0,
            firstGlyphClass1,
            secondGlyph0, secondGlyph1,
            v1_0, v2_0,
            v1_1, v2_1,
            v1_2, v2_2,
            v1_3, v2_3,
            out int pairPosOffsetInGpos);

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
                new MemoryTableSource(KnownTags.GPOS, gposData)
            });

        byte[] fontBytes = ms.ToArray();

        using var file = SfntFile.FromMemory(fontBytes);
        var font = file.GetFont(0);
        Assert.IsTrue(font.TryGetGpos(out var gpos));
        Assert.IsTrue(gpos.TryGetLookupList(out var lookupList));

        Assert.IsTrue(lookupList.TryGetLookup(0, out var lookup));
        Assert.AreEqual((ushort)2, lookup.LookupType);
        Assert.IsTrue(lookup.TryGetSubtableOffset(0, out ushort relSub));
        int pairPosOffset = lookup.Offset + relSub;
        Assert.AreEqual(pairPosOffsetInGpos, pairPosOffset);

        Assert.IsTrue(GposPairPosSubtable.TryCreate(gpos.Table, pairPosOffset, out var pairPos));
        Assert.AreEqual((ushort)2, pairPos.PosFormat);

        Assert.IsTrue(pairPos.TryGetPairAdjustment(firstGlyphClass0, secondGlyph0, out bool positioned0, out var newValue1_0, out var newValue2_0));
        Assert.IsTrue(positioned0);
        Assert.IsTrue(newValue1_0.TryGetXAdvance(out short newV1_0));
        Assert.IsTrue(newValue2_0.TryGetXAdvance(out short newV2_0));
        Assert.AreEqual(v1_0, newV1_0);
        Assert.AreEqual(v2_0, newV2_0);

        Assert.IsTrue(pairPos.TryGetPairAdjustment(firstGlyphClass0, secondGlyph1, out bool positioned1, out var newValue1_1, out var newValue2_1));
        Assert.IsTrue(positioned1);
        Assert.IsTrue(newValue1_1.TryGetXAdvance(out short newV1_1));
        Assert.IsTrue(newValue2_1.TryGetXAdvance(out short newV2_1));
        Assert.AreEqual(v1_1, newV1_1);
        Assert.AreEqual(v2_1, newV2_1);

        Assert.IsTrue(pairPos.TryGetPairAdjustment(firstGlyphClass1, secondGlyph0, out bool positioned2, out var newValue1_2, out var newValue2_2));
        Assert.IsTrue(positioned2);
        Assert.IsTrue(newValue1_2.TryGetXAdvance(out short newV1_2));
        Assert.IsTrue(newValue2_2.TryGetXAdvance(out short newV2_2));
        Assert.AreEqual(v1_2, newV1_2);
        Assert.AreEqual(v2_2, newV2_2);

        Assert.IsTrue(pairPos.TryGetPairAdjustment(firstGlyphClass1, secondGlyph1, out bool positioned3, out var newValue1_3, out var newValue2_3));
        Assert.IsTrue(positioned3);
        Assert.IsTrue(newValue1_3.TryGetXAdvance(out short newV1_3));
        Assert.IsTrue(newValue2_3.TryGetXAdvance(out short newV2_3));
        Assert.AreEqual(v1_3, newV1_3);
        Assert.AreEqual(v2_3, newV2_3);

        Assert.IsTrue(pairPos.TryGetPairAdjustment((ushort)(firstGlyphClass1 + 1), secondGlyph0, out bool notCovered, out _, out _));
        Assert.IsFalse(notCovered);

        string tmp = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(tmp, fontBytes);

            using var legacyFile = new Legacy.OTFile();
            Assert.IsTrue(legacyFile.open(tmp));
            var legacyFont = legacyFile.GetFont(0)!;

            var legacyGpos = (Legacy.Table_GPOS)legacyFont.GetTable("GPOS")!;
            var legacyBuf = legacyGpos.GetBuffer();

            var legacyPair = new Legacy.Table_GPOS.PairPos((uint)pairPosOffsetInGpos, legacyBuf);
            Assert.AreEqual((ushort)2, legacyPair.PosFormat);

            var legacyF2 = legacyPair.GetPairPosFormat2();
            Assert.AreEqual((ushort)0x0004, legacyF2.ValueFormat1);
            Assert.AreEqual((ushort)0x0004, legacyF2.ValueFormat2);

            var legacyCoverage = legacyF2.GetCoverageTable();
            Assert.IsTrue(legacyCoverage.GetGlyphCoverage(firstGlyphClass0).bCovered);

            ushort legacyClass1 = legacyF2.GetClassDef1Table().GetClassValue(firstGlyphClass0);
            Assert.AreEqual((ushort)0, legacyClass1);

            ushort legacyClass2_0 = legacyF2.GetClassDef2Table().GetClassValue(secondGlyph0);
            ushort legacyClass2_1 = legacyF2.GetClassDef2Table().GetClassValue(secondGlyph1);

            var legacyC1 = legacyF2.GetClass1Record(legacyClass1)!;
            var legacyC2_0 = legacyC1.GetClass2Record(legacyClass2_0)!;
            var legacyC2_1 = legacyC1.GetClass2Record(legacyClass2_1)!;

            short legacyV1_0 = unchecked((short)legacyC2_0.Value1.XAdvance);
            short legacyV2_0 = unchecked((short)legacyC2_0.Value2.XAdvance);
            short legacyV1_1 = unchecked((short)legacyC2_1.Value1.XAdvance);
            short legacyV2_1 = unchecked((short)legacyC2_1.Value2.XAdvance);

            Assert.AreEqual(v1_0, legacyV1_0);
            Assert.AreEqual(v2_0, legacyV2_0);
            Assert.AreEqual(v1_1, legacyV1_1);
            Assert.AreEqual(v2_1, legacyV2_1);
        }
        finally
        {
            try { File.Delete(tmp); } catch { /* ignore */ }
        }
    }

    private static Legacy.Table_GPOS.PairPos.PairPosFormat1.PairValueRecord FindLegacyPairValueRecord(
        Legacy.Table_GPOS.PairPos.PairPosFormat1.PairSetTable pairSet,
        ushort secondGlyph)
    {
        ushort count = pairSet.PairValueCount;
        for (ushort i = 0; i < count; i++)
        {
            var pvr = pairSet.GetPairValueRecord(i)!;
            if (pvr.SecondGlyph == secondGlyph)
                return pvr;
        }

        Assert.Fail($"Legacy PairValueRecord for secondGlyph={secondGlyph} not found.");
        return null!;
    }

    private static byte[] BuildSyntheticGposPairPosFormat1(
        ushort firstGlyphId,
        ushort secondGlyph0, short value1_0, short value2_0,
        ushort secondGlyph1, short value1_1, short value2_1,
        out int pairPosOffset)
    {
        const int headerOffset = 0;
        const int scriptListOffset = 10;

        const int scriptListLength = 20;
        const int featureListOffset = scriptListOffset + scriptListLength; // 30

        const int featureListLength = 14;
        const int lookupListOffset = featureListOffset + featureListLength; // 44

        const int lookupListLength = 44;
        int totalLength = lookupListOffset + lookupListLength; // 88

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
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(scriptList + 6, 2), 8); // ScriptTableOffset (from ScriptList)

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

        int lookupTable = lookupList + 4;
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(lookupTable + 0, 2), 2); // LookupType = PairPos
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(lookupTable + 2, 2), 0); // LookupFlag
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(lookupTable + 4, 2), 1); // SubTableCount
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(lookupTable + 6, 2), 8); // SubTableOffset[0]

        pairPosOffset = lookupTable + 8;
        int pairPos = pairPosOffset;

        // PairPos format 1
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(pairPos + 0, 2), 1);      // PosFormat
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(pairPos + 2, 2), 26);     // CoverageOffset (from subtable)
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(pairPos + 4, 2), 0x0004); // ValueFormat1 = XAdvance
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(pairPos + 6, 2), 0x0004); // ValueFormat2 = XAdvance
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(pairPos + 8, 2), 1);      // PairSetCount
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(pairPos + 10, 2), 12);    // PairSetOffset[0]

        int pairSet = pairPos + 12;
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(pairSet + 0, 2), 2); // PairValueCount

        // PairValueRecord[0] @ +2
        int pvr0 = pairSet + 2;
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(pvr0 + 0, 2), secondGlyph0);
        BinaryPrimitives.WriteInt16BigEndian(span.Slice(pvr0 + 2, 2), value1_0);
        BinaryPrimitives.WriteInt16BigEndian(span.Slice(pvr0 + 4, 2), value2_0);

        // PairValueRecord[1] @ +8
        int pvr1 = pairSet + 8;
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(pvr1 + 0, 2), secondGlyph1);
        BinaryPrimitives.WriteInt16BigEndian(span.Slice(pvr1 + 2, 2), value1_1);
        BinaryPrimitives.WriteInt16BigEndian(span.Slice(pvr1 + 4, 2), value2_1);

        int coverage = pairPos + 26;
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(coverage + 0, 2), 1); // CoverageFormat 1
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(coverage + 2, 2), 1); // GlyphCount
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(coverage + 4, 2), firstGlyphId);

        return gpos;
    }

    private static byte[] BuildSyntheticGposPairPosFormat2(
        ushort firstGlyphClass0,
        ushort firstGlyphClass1,
        ushort secondGlyph0,
        ushort secondGlyph1,
        short value1_0,
        short value2_0,
        short value1_1,
        short value2_1,
        short value1_2,
        short value2_2,
        short value1_3,
        short value2_3,
        out int pairPosOffset)
    {
        const int headerOffset = 0;
        const int scriptListOffset = 10;

        const int scriptListLength = 20;
        const int featureListOffset = scriptListOffset + scriptListLength; // 30

        const int featureListLength = 14;
        const int lookupListOffset = featureListOffset + featureListLength; // 44

        const int lookupListLength = 78;
        int totalLength = lookupListOffset + lookupListLength; // 122

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
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(scriptList + 6, 2), 8); // ScriptTableOffset (from ScriptList)

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

        int lookupTable = lookupList + 4;
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(lookupTable + 0, 2), 2); // LookupType = PairPos
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(lookupTable + 2, 2), 0); // LookupFlag
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(lookupTable + 4, 2), 1); // SubTableCount
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(lookupTable + 6, 2), 8); // SubTableOffset[0]

        pairPosOffset = lookupTable + 8;
        int pairPos = pairPosOffset;

        // PairPos format 2
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(pairPos + 0, 2), 2);      // PosFormat
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(pairPos + 2, 2), 40);     // CoverageOffset (from subtable)
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(pairPos + 4, 2), 0x0004); // ValueFormat1 = XAdvance
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(pairPos + 6, 2), 0x0004); // ValueFormat2 = XAdvance
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(pairPos + 8, 2), 48);     // ClassDef1Offset
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(pairPos + 10, 2), 56);    // ClassDef2Offset
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(pairPos + 12, 2), 2);     // Class1Count
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(pairPos + 14, 2), 3);     // Class2Count

        // Class1Records start @ +16 (row-major)
        int matrix = pairPos + 16;

        // row 0 (class 0)
        WriteClass2Record(span, matrix + (0 * 12) + (0 * 4), 0, 0);            // col 0
        WriteClass2Record(span, matrix + (0 * 12) + (1 * 4), value1_0, value2_0); // col 1
        WriteClass2Record(span, matrix + (0 * 12) + (2 * 4), value1_1, value2_1); // col 2

        // row 1 (class 1)
        WriteClass2Record(span, matrix + (1 * 12) + (0 * 4), 0, 0);            // col 0
        WriteClass2Record(span, matrix + (1 * 12) + (1 * 4), value1_2, value2_2); // col 1
        WriteClass2Record(span, matrix + (1 * 12) + (2 * 4), value1_3, value2_3); // col 2

        int coverage = pairPos + 40;
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(coverage + 0, 2), 1); // CoverageFormat 1
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(coverage + 2, 2), 2); // GlyphCount
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(coverage + 4, 2), firstGlyphClass0);
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(coverage + 6, 2), firstGlyphClass1);

        int classDef1 = pairPos + 48;
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(classDef1 + 0, 2), 1); // ClassFormat 1
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(classDef1 + 2, 2), firstGlyphClass1);
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(classDef1 + 4, 2), 1); // GlyphCount
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(classDef1 + 6, 2), 1); // ClassValueArray[0] = 1

        int classDef2 = pairPos + 56;
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(classDef2 + 0, 2), 1); // ClassFormat 1
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(classDef2 + 2, 2), secondGlyph0);
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(classDef2 + 4, 2), 2); // GlyphCount
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(classDef2 + 6, 2), 1); // secondGlyph0 => class 1
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(classDef2 + 8, 2), 2); // secondGlyph1 => class 2

        return gpos;
    }

    private static void WriteClass2Record(Span<byte> data, int offset, short value1XAdvance, short value2XAdvance)
    {
        BinaryPrimitives.WriteInt16BigEndian(data.Slice(offset + 0, 2), value1XAdvance);
        BinaryPrimitives.WriteInt16BigEndian(data.Slice(offset + 2, 2), value2XAdvance);
    }

    private static void WriteTag(Span<byte> data, int offset, string tag)
    {
        if (tag.Length != 4)
            throw new ArgumentException("tag must be 4 characters", nameof(tag));

        Encoding.ASCII.GetBytes(tag).CopyTo(data.Slice(offset, 4));
    }
}
