using System.Buffers.Binary;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OTFontFile2.Tables;

namespace OTFontFile2.Tests.UnitTests;

[TestClass]
public sealed class GdefSubtablesTests
{
    [TestMethod]
    public void SyntheticGdef_Subtables_Parse()
    {
        byte[] gdefBytes = BuildSyntheticGdefWithSubtables();

        byte[] head = new byte[12];
        head[0] = 0xDE;
        head[1] = 0xAD;
        head[2] = 0xBE;
        head[3] = 0xEF;

        var builder = new SfntBuilder { SfntVersion = 0x00010000 };
        builder.SetTable(KnownTags.head, head);
        builder.SetTable(KnownTags.GDEF, gdefBytes);
        byte[] fontBytes = builder.ToArray();

        using var file = SfntFile.FromMemory(fontBytes);
        var font = file.GetFont(0);

        Assert.IsTrue(font.TryGetGdef(out var gdef));
        Assert.AreEqual(0x00010002u, gdef.Version.RawValue);
        Assert.AreEqual((ushort)14, gdef.AttachListOffset);
        Assert.AreEqual((ushort)40, gdef.LigCaretListOffset);
        Assert.AreEqual((ushort)66, gdef.MarkGlyphSetsDefOffset);

        Assert.IsTrue(gdef.TryGetAttachList(out var attachList));
        Assert.IsTrue(attachList.TryGetAttachPointTableForGlyph(5, out bool attachCovered0, out var attach0));
        Assert.IsTrue(attachCovered0);
        Assert.AreEqual((ushort)2, attach0.PointCount);
        Assert.IsTrue(attach0.TryGetPointIndex(0, out ushort attach0Point0));
        Assert.IsTrue(attach0.TryGetPointIndex(1, out ushort attach0Point1));
        Assert.AreEqual((ushort)1, attach0Point0);
        Assert.AreEqual((ushort)3, attach0Point1);

        Assert.IsTrue(attachList.TryGetAttachPointTableForGlyph(8, out bool attachCovered1, out var attach1));
        Assert.IsTrue(attachCovered1);
        Assert.AreEqual((ushort)1, attach1.PointCount);
        Assert.IsTrue(attach1.TryGetPointIndex(0, out ushort attach1Point0));
        Assert.AreEqual((ushort)0, attach1Point0);

        Assert.IsTrue(attachList.TryGetAttachPointTableForGlyph(6, out bool attachNotCovered, out _));
        Assert.IsFalse(attachNotCovered);

        Assert.IsTrue(gdef.TryGetLigCaretList(out var ligCaretList));
        Assert.IsTrue(ligCaretList.TryGetLigGlyphTableForGlyph(9, out bool ligCovered, out var ligGlyph));
        Assert.IsTrue(ligCovered);
        Assert.AreEqual((ushort)2, ligGlyph.CaretCount);

        Assert.IsTrue(ligGlyph.TryGetCaretValueTable(0, out var caret0));
        Assert.AreEqual((ushort)1, caret0.CaretValueFormat);
        Assert.IsTrue(caret0.TryGetCoordinate(out short coord0));
        Assert.AreEqual((short)100, coord0);

        Assert.IsTrue(ligGlyph.TryGetCaretValueTable(1, out var caret1));
        Assert.AreEqual((ushort)2, caret1.CaretValueFormat);
        Assert.IsTrue(caret1.TryGetCaretValuePoint(out ushort point1));
        Assert.AreEqual((ushort)7, point1);

        Assert.IsTrue(ligCaretList.TryGetLigGlyphTableForGlyph(10, out bool ligNotCovered, out _));
        Assert.IsFalse(ligNotCovered);

        Assert.IsTrue(gdef.TryGetMarkGlyphSetsDef(out var markSets));
        Assert.AreEqual((ushort)1, markSets.MarkGlyphSetsDefFormat);
        Assert.AreEqual((ushort)2, markSets.MarkGlyphSetCount);

        Assert.IsTrue(markSets.TryIsGlyphInSet(0, 12, out bool set0Has12));
        Assert.IsTrue(set0Has12);
        Assert.IsTrue(markSets.TryIsGlyphInSet(0, 13, out bool set0Has13));
        Assert.IsFalse(set0Has13);

        Assert.IsTrue(markSets.TryIsGlyphInSet(1, 13, out bool set1Has13));
        Assert.IsTrue(set1Has13);
        Assert.IsTrue(markSets.TryIsGlyphInSet(1, 14, out bool set1Has14));
        Assert.IsTrue(set1Has14);
        Assert.IsTrue(markSets.TryIsGlyphInSet(1, 12, out bool set1Has12));
        Assert.IsFalse(set1Has12);
    }

    private static byte[] BuildSyntheticGdefWithSubtables()
    {
        // Layout:
        // GDEF header v1.2 (14 bytes)
        // AttachList @ 14 (26 bytes)
        // LigCaretList @ 40 (26 bytes)
        // MarkGlyphSetsDef @ 66 (26 bytes)
        byte[] gdef = new byte[92];
        var span = gdef.AsSpan();

        // Header (v1.2)
        BinaryPrimitives.WriteUInt32BigEndian(span.Slice(0, 4), 0x00010002u);
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(4, 2), 0);   // GlyphClassDefOffset
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(6, 2), 14);  // AttachListOffset
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(8, 2), 40);  // LigCaretListOffset
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(10, 2), 0);  // MarkAttachClassDefOffset
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(12, 2), 66); // MarkGlyphSetsDefOffset

        // AttachList @ 14
        int attachList = 14;
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(attachList + 0, 2), 8); // CoverageOffset
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(attachList + 2, 2), 2); // GlyphCount
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(attachList + 4, 2), 16); // AttachPointOffset[0]
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(attachList + 6, 2), 22); // AttachPointOffset[1]

        int attachCoverage = attachList + 8;
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(attachCoverage + 0, 2), 1); // CoverageFormat1
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(attachCoverage + 2, 2), 2); // GlyphCount
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(attachCoverage + 4, 2), 5);
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(attachCoverage + 6, 2), 8);

        int attachPoint0 = attachList + 16;
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(attachPoint0 + 0, 2), 2); // PointCount
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(attachPoint0 + 2, 2), 1);
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(attachPoint0 + 4, 2), 3);

        int attachPoint1 = attachList + 22;
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(attachPoint1 + 0, 2), 1); // PointCount
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(attachPoint1 + 2, 2), 0);

        // LigCaretList @ 40
        int ligCaretList = 40;
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(ligCaretList + 0, 2), 6); // CoverageOffset
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(ligCaretList + 2, 2), 1); // LigGlyphCount
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(ligCaretList + 4, 2), 12); // LigGlyphOffset[0]

        int ligCoverage = ligCaretList + 6;
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(ligCoverage + 0, 2), 1); // CoverageFormat1
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(ligCoverage + 2, 2), 1); // GlyphCount
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(ligCoverage + 4, 2), 9);

        int ligGlyph = ligCaretList + 12;
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(ligGlyph + 0, 2), 2); // CaretCount
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(ligGlyph + 2, 2), 6); // CaretValueOffset[0]
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(ligGlyph + 4, 2), 10); // CaretValueOffset[1]

        int caret0 = ligGlyph + 6;
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(caret0 + 0, 2), 1); // Format1
        BinaryPrimitives.WriteInt16BigEndian(span.Slice(caret0 + 2, 2), 100);

        int caret1 = ligGlyph + 10;
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(caret1 + 0, 2), 2); // Format2
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(caret1 + 2, 2), 7);

        // MarkGlyphSetsDef @ 66
        int markSets = 66;
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(markSets + 0, 2), 1); // MarkGlyphSetsDefFormat
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(markSets + 2, 2), 2); // MarkGlyphSetCount
        BinaryPrimitives.WriteUInt32BigEndian(span.Slice(markSets + 4, 4), 12u); // CoverageOffset[0]
        BinaryPrimitives.WriteUInt32BigEndian(span.Slice(markSets + 8, 4), 18u); // CoverageOffset[1]

        int markCoverage0 = markSets + 12;
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(markCoverage0 + 0, 2), 1); // CoverageFormat1
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(markCoverage0 + 2, 2), 1); // GlyphCount
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(markCoverage0 + 4, 2), 12);

        int markCoverage1 = markSets + 18;
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(markCoverage1 + 0, 2), 1); // CoverageFormat1
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(markCoverage1 + 2, 2), 2); // GlyphCount
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(markCoverage1 + 4, 2), 13);
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(markCoverage1 + 6, 2), 14);

        return gdef;
    }
}

