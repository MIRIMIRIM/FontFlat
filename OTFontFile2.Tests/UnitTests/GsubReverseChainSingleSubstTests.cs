using System.Buffers.Binary;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OTFontFile2;
using OTFontFile2.Tables;
using Legacy = OTFontFile;

namespace OTFontFile2.Tests.UnitTests;

[TestClass]
public sealed class GsubReverseChainSingleSubstTests
{
    [TestMethod]
    public void SyntheticGsubReverseChainSingleSubstFormat1_CanParseAndMatchesLegacy()
    {
        const ushort coveredGlyphId = 5;
        const ushort substituteGlyphId = 10;

        byte[] subtable = BuildReverseChainSingleSubstFormat1Subtable(coveredGlyphId, substituteGlyphId);
        byte[] gsubData = BuildGsubWithSingleLookup(lookupType: 8, subtable, out int subtableOffsetInGsub);

        byte[] fontBytes = BuildFontWithSingleTable(KnownTags.GSUB, gsubData);

        using var file = SfntFile.FromMemory(fontBytes);
        var font = file.GetFont(0);

        Assert.IsTrue(font.TryGetGsub(out var gsub));
        Assert.IsTrue(gsub.TryGetLookupList(out var lookupList));
        Assert.IsTrue(lookupList.TryGetLookup(0, out var lookup));
        Assert.AreEqual((ushort)8, lookup.LookupType);
        Assert.IsTrue(lookup.TryGetSubtableOffset(0, out ushort relSub));
        int reverseOffset = lookup.Offset + relSub;
        Assert.AreEqual(subtableOffsetInGsub, reverseOffset);

        Assert.IsTrue(GsubReverseChainSingleSubstSubtable.TryCreate(gsub.Table, reverseOffset, out var reverse));
        Assert.AreEqual((ushort)1, reverse.SubstFormat);

        Assert.IsTrue(reverse.TrySubstituteGlyph(coveredGlyphId, out bool substituted, out ushort subst));
        Assert.IsTrue(substituted);
        Assert.AreEqual(substituteGlyphId, subst);

        Assert.IsTrue(reverse.TrySubstituteGlyph((ushort)(coveredGlyphId + 1), out bool notSubstituted, out ushort same));
        Assert.IsFalse(notSubstituted);
        Assert.AreEqual((ushort)(coveredGlyphId + 1), same);

        string tmp = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(tmp, fontBytes);

            using var legacyFile = new Legacy.OTFile();
            Assert.IsTrue(legacyFile.open(tmp));
            var legacyFont = legacyFile.GetFont(0)!;

            var legacyGsub = (Legacy.Table_GSUB)legacyFont.GetTable("GSUB")!;
            var legacyBuf = legacyGsub.GetBuffer();

            var legacyReverse = new Legacy.Table_GSUB.ReverseChainSubst((uint)subtableOffsetInGsub, legacyBuf);
            Assert.AreEqual((ushort)1, legacyReverse.SubstFormat);

            var legacyCov = legacyReverse.GetCoverageTable().GetGlyphCoverage(coveredGlyphId);
            Assert.IsTrue(legacyCov.bCovered);
            Assert.AreEqual((ushort)0, legacyCov.CoverageIndex);

            Assert.AreEqual((ushort)0, legacyReverse.BacktrackGlyphCount);
            Assert.AreEqual((ushort)0, legacyReverse.LookaheadGlyphCount);
            Assert.AreEqual((ushort)1, legacyReverse.SubstituteGlyphCount);
            Assert.AreEqual(substituteGlyphId, legacyReverse.GetSubstituteGlyphID(0));
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

    private static byte[] BuildGsubWithSingleLookup(ushort lookupType, byte[] subtable, out int subtableOffset)
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
        byte[] gsub = new byte[totalLength];
        var span = gsub.AsSpan();

        // GSUB header
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
        WriteTag(span, featureList + 2, "liga");
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
        return gsub;
    }

    private static byte[] BuildReverseChainSingleSubstFormat1Subtable(ushort coveredGlyphId, ushort substituteGlyphId)
    {
        // format(2) + coverageOffset(2) + backtrackCount(2) + lookaheadCount(2) + substCount(2) + subst[1](2) + coverage(6)
        byte[] st = new byte[18];
        var span = st.AsSpan();

        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(0, 2), 1);  // SubstFormat
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(2, 2), 12); // CoverageOffset
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(4, 2), 0);  // BacktrackGlyphCount
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(6, 2), 0);  // LookaheadGlyphCount
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(8, 2), 1);  // SubstituteGlyphCount
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(10, 2), substituteGlyphId);

        // Coverage @ 12 (format 1, 1 glyph)
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(12, 2), 1);
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(14, 2), 1);
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(16, 2), coveredGlyphId);

        return st;
    }

    private static void WriteTag(Span<byte> data, int offset, string tag)
    {
        if (tag.Length != 4)
            throw new ArgumentException("tag must be 4 characters", nameof(tag));

        Encoding.ASCII.GetBytes(tag).CopyTo(data.Slice(offset, 4));
    }
}

