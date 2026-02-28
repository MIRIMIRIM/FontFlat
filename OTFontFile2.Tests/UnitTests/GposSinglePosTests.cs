using System.Buffers.Binary;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OTFontFile2.Tables;
using Legacy = OTFontFile;

namespace OTFontFile2.Tests.UnitTests;

[TestClass]
public sealed class GposSinglePosTests
{
    [TestMethod]
    public void SyntheticGposSinglePos_CanParseAndMatchesLegacy()
    {
        const ushort coveredGlyph = 5;
        const short xAdvance = -50;

        byte[] gposData = BuildSyntheticGposSinglePos(coveredGlyph, xAdvance, out int singlePosOffsetInGpos);

        // Minimal head (12 bytes) so writer can emit a valid checkSumAdjustment.
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
        Assert.AreEqual((ushort)1, lookupList.LookupCount);

        Assert.IsTrue(lookupList.TryGetLookup(0, out var lookup));
        Assert.AreEqual((ushort)1, lookup.LookupType);
        Assert.AreEqual((ushort)1, lookup.SubtableCount);

        Assert.IsTrue(lookup.TryGetSubtableOffset(0, out ushort relSub));
        int singlePosOffset = lookup.Offset + relSub;
        Assert.AreEqual(singlePosOffsetInGpos, singlePosOffset);

        Assert.IsTrue(GposSinglePosSubtable.TryCreate(gpos.Table, singlePosOffset, out var singlePos));
        Assert.AreEqual((ushort)1, singlePos.PosFormat);
        Assert.AreEqual((ushort)0x0004, singlePos.ValueFormat); // XAdvance only

        Assert.IsTrue(singlePos.TryGetValueRecordForGlyph(coveredGlyph, out bool positioned, out var newValue));
        Assert.IsTrue(positioned);
        Assert.IsTrue(newValue.TryGetXAdvance(out short newXAdv));
        Assert.AreEqual(xAdvance, newXAdv);

        Assert.IsTrue(singlePos.TryGetValueRecordForGlyph((ushort)(coveredGlyph + 1), out bool notPositioned, out _));
        Assert.IsFalse(notPositioned);

        string tmp = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(tmp, fontBytes);

            using var legacyFile = new Legacy.OTFile();
            Assert.IsTrue(legacyFile.open(tmp));
            var legacyFont = legacyFile.GetFont(0)!;

            var legacyGpos = (Legacy.Table_GPOS)legacyFont.GetTable("GPOS")!;
            var legacyBuf = legacyGpos.GetBuffer();

            var legacySingle = new Legacy.Table_GPOS.SinglePos((uint)singlePosOffsetInGpos, legacyBuf);
            Assert.AreEqual((ushort)1, legacySingle.PosFormat);

            var legacyF1 = legacySingle.GetSinglePosFormat1();
            Assert.AreEqual((ushort)0x0004, legacyF1.ValueFormat);

            var legacyValue = legacyF1.Value;
            short legacyXAdv = unchecked((short)legacyValue.XAdvance);
            Assert.AreEqual(xAdvance, legacyXAdv);
        }
        finally
        {
            try { File.Delete(tmp); } catch { /* ignore */ }
        }
    }

    private static byte[] BuildSyntheticGposSinglePos(ushort coveredGlyphId, short xAdvance, out int singlePosOffset)
    {
        // Minimal GPOS with one lookup (SinglePos, format 1), one covered glyph.
        // Offsets are relative to GPOS table start.

        const int headerOffset = 0;
        const int scriptListOffset = 10;

        const int scriptListLength = 20;
        const int featureListOffset = scriptListOffset + scriptListLength; // 30

        const int featureListLength = 14;
        const int lookupListOffset = featureListOffset + featureListLength; // 44

        const int lookupListLength = 26;
        int totalLength = lookupListOffset + lookupListLength; // 70

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
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(scriptTable + 0, 2), 4); // DefaultLangSysOffset (from ScriptTable)
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(scriptTable + 2, 2), 0); // LangSysCount

        int langSys = scriptTable + 4;
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(langSys + 0, 2), 0);       // LookupOrder
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(langSys + 2, 2), 0xFFFF);  // ReqFeatureIndex
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(langSys + 4, 2), 1);       // FeatureCount
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(langSys + 6, 2), 0);       // FeatureIndex[0]

        // FeatureList @ 30
        int featureList = featureListOffset;
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(featureList + 0, 2), 1); // FeatureCount
        WriteTag(span, featureList + 2, "kern");
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(featureList + 6, 2), 8); // FeatureTableOffset (from FeatureList)

        int featureTable = featureList + 8;
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(featureTable + 0, 2), 0); // FeatureParamsOffset
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(featureTable + 2, 2), 1); // LookupCount
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(featureTable + 4, 2), 0); // LookupListIndex[0]

        // LookupList @ 44
        int lookupList = lookupListOffset;
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(lookupList + 0, 2), 1); // LookupCount
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(lookupList + 2, 2), 4); // LookupOffset[0] (from LookupList)

        int lookupTable = lookupList + 4;
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(lookupTable + 0, 2), 1); // LookupType = SinglePos
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(lookupTable + 2, 2), 0); // LookupFlag
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(lookupTable + 4, 2), 1); // SubTableCount
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(lookupTable + 6, 2), 8); // SubTableOffset[0] (from LookupTable)

        singlePosOffset = lookupTable + 8;
        int singlePos = singlePosOffset;
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(singlePos + 0, 2), 1);       // PosFormat
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(singlePos + 2, 2), 8);       // CoverageOffset (from subtable)
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(singlePos + 4, 2), 0x0004);  // ValueFormat = XAdvance
        BinaryPrimitives.WriteInt16BigEndian(span.Slice(singlePos + 6, 2), xAdvance); // ValueRecord (XAdvance)

        int coverage = singlePos + 8;
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(coverage + 0, 2), 1); // CoverageFormat 1
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(coverage + 2, 2), 1); // GlyphCount
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(coverage + 4, 2), coveredGlyphId);

        return gpos;
    }

    private static void WriteTag(Span<byte> data, int offset, string tag)
    {
        if (tag.Length != 4)
            throw new ArgumentException("tag must be 4 characters", nameof(tag));

        Encoding.ASCII.GetBytes(tag).CopyTo(data.Slice(offset, 4));
    }
}

