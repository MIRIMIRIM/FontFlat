using System.Buffers.Binary;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OTFontFile2.Tables;

namespace OTFontFile2.Tests.UnitTests;

[TestClass]
public sealed class CmapFormat13Tests
{
    [TestMethod]
    public void CmapTable_Format13_CanMapAndBuilderCanImport()
    {
        byte[] head = TestSfntTables.BuildValidHeadTable(unitsPerEm: 1000);
        var maxp = new MaxpTableBuilder { TableVersionNumber = new Fixed1616(0x00010000u), NumGlyphs = 20 };

        byte[] cmapBytes = BuildFormat13Cmap(platformId: 0, encodingId: 4, start: 0x0041u, end: 0x005Au, glyphId: 3);

        var sfnt = new SfntBuilder { SfntVersion = 0x00010000u };
        sfnt.SetTable(KnownTags.head, head);
        sfnt.SetTable(maxp);
        sfnt.SetTable(KnownTags.cmap, cmapBytes);

        byte[] bytes = sfnt.ToArray();
        Assert.AreEqual(0xB1B0AFBAu, OpenTypeChecksum.Compute(bytes));

        using var file = SfntFile.FromMemory(bytes);
        var font = file.GetFont(0);

        Assert.IsTrue(font.TryGetCmap(out var cmap));
        Assert.IsTrue(cmap.TryGetSubtable(platformId: 0, encodingId: 4, out var st));
        Assert.AreEqual((ushort)13, st.Format);

        Assert.IsTrue(st.TryMapCodePoint(0x0041u, out uint gidA));
        Assert.AreEqual(3u, gidA);
        Assert.IsTrue(st.TryMapCodePoint(0x0040u, out uint gidNotMapped));
        Assert.AreEqual(0u, gidNotMapped);

        Assert.IsTrue(CmapTableBuilder.TryFrom(cmap, out var builder));
        Assert.IsTrue(builder.TryGetGlyphId(0x0041u, out ushort importedGidA));
        Assert.AreEqual((ushort)3, importedGidA);
    }

    private static byte[] BuildFormat13Cmap(ushort platformId, ushort encodingId, uint start, uint end, uint glyphId)
    {
        const int headerLen = 4 + 8; // version + numTables + 1 encoding record
        const int subtableOffset = headerLen;

        const uint nGroups = 1;
        const int subtableLen = 16 + 12; // header(16) + groups(12)

        byte[] bytes = new byte[headerLen + subtableLen];
        Span<byte> span = bytes;

        // cmap header
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(0, 2), 0); // version
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(2, 2), 1); // numTables

        // encoding record
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(4, 2), platformId);
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(6, 2), encodingId);
        BinaryPrimitives.WriteUInt32BigEndian(span.Slice(8, 4), (uint)subtableOffset);

        // format 13 subtable
        int o = subtableOffset;
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(o, 2), 13);
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(o + 2, 2), 0); // reserved
        BinaryPrimitives.WriteUInt32BigEndian(span.Slice(o + 4, 4), (uint)subtableLen);
        BinaryPrimitives.WriteUInt32BigEndian(span.Slice(o + 8, 4), 0); // language
        BinaryPrimitives.WriteUInt32BigEndian(span.Slice(o + 12, 4), nGroups);

        int g = o + 16;
        BinaryPrimitives.WriteUInt32BigEndian(span.Slice(g, 4), start);
        BinaryPrimitives.WriteUInt32BigEndian(span.Slice(g + 4, 4), end);
        BinaryPrimitives.WriteUInt32BigEndian(span.Slice(g + 8, 4), glyphId);

        return bytes;
    }
}

