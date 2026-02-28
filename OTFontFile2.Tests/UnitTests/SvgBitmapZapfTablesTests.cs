using Microsoft.VisualStudio.TestTools.UnitTesting;
using OTFontFile2.Tables;
using System.Buffers.Binary;
using System.Text;
using Legacy = OTFontFile;

namespace OTFontFile2.Tests.UnitTests;

[TestClass]
public sealed class SvgBitmapZapfTablesTests
{
    [TestMethod]
    public void SyntheticSvgTable_ParsesAndMatchesLegacy()
    {
        byte[] docBytes = Encoding.ASCII.GetBytes("<svg/>");
        byte[] svgBytes = BuildSvgTable(docBytes, out var expectedRecord);

        var builder = new SfntBuilder();
        builder.SetTable(KnownTags.SVG, svgBytes);
        byte[] fontBytes = builder.ToArray();

        using var file = SfntFile.FromMemory(fontBytes);
        var font = file.GetFont(0);

        Assert.IsTrue(font.TryGetSvg(out var newSvg));
        Assert.AreEqual((ushort)0, newSvg.Version);
        Assert.AreEqual((uint)10, newSvg.DocumentIndexOffset);

        Assert.IsTrue(newSvg.TryGetDocumentIndex(out var newIndex));
        Assert.AreEqual((ushort)1, newIndex.RecordCount);

        Assert.IsTrue(newIndex.TryGetRecord(0, out var newRecord));
        Assert.AreEqual(expectedRecord.StartGlyphId, newRecord.StartGlyphId);
        Assert.AreEqual(expectedRecord.EndGlyphId, newRecord.EndGlyphId);
        Assert.AreEqual(expectedRecord.DocumentOffset, newRecord.DocumentOffset);
        Assert.AreEqual(expectedRecord.DocumentLength, newRecord.DocumentLength);

        Assert.IsTrue(newSvg.TryGetDocumentSpan(newRecord, out var newDoc));
        Assert.AreEqual(docBytes.Length, newDoc.Length);
        for (int i = 0; i < docBytes.Length; i++)
            Assert.AreEqual(docBytes[i], newDoc[i]);

        string tempPath = Path.Combine(Path.GetTempPath(), $"synthetic-svg-{Guid.NewGuid():N}.ttf");
        try
        {
            File.WriteAllBytes(tempPath, fontBytes);

            using var legacyFile = new Legacy.OTFile();
            Assert.IsTrue(legacyFile.open(tempPath));
            var legacyFont = legacyFile.GetFont(0)!;

            var legacySvg = (Legacy.Table_SVG)legacyFont.GetTable("SVG ")!;

            Assert.AreEqual(legacySvg.version, newSvg.Version);
            Assert.AreEqual(legacySvg.offsetToSVGDocIndex, newSvg.DocumentIndexOffset);
            Assert.AreEqual(legacySvg.numEntries, newIndex.RecordCount);

            var legacyEntry = legacySvg.GetDocIndexEntry(0)!;
            Assert.AreEqual(legacyEntry.startGlyphID, newRecord.StartGlyphId);
            Assert.AreEqual(legacyEntry.endGlyphID, newRecord.EndGlyphId);
            Assert.AreEqual(legacyEntry.svgDocOffset, newRecord.DocumentOffset);
            Assert.AreEqual(legacyEntry.svgDocLength, newRecord.DocumentLength);

            byte[] legacyDoc = legacySvg.GetDoc(0, autodecompress: false)!;
            Assert.AreEqual(newDoc.Length, legacyDoc.Length);
            for (int i = 0; i < legacyDoc.Length; i++)
                Assert.AreEqual(newDoc[i], legacyDoc[i]);
        }
        finally
        {
            try { File.Delete(tempPath); } catch { }
        }
    }

    [TestMethod]
    public void SyntheticEbscTable_ParsesAndMatchesLegacy()
    {
        byte[] ebscBytes = BuildEbscTable();

        var builder = new SfntBuilder();
        builder.SetTable(KnownTags.EBSC, ebscBytes);
        byte[] fontBytes = builder.ToArray();

        using var file = SfntFile.FromMemory(fontBytes);
        var font = file.GetFont(0);

        Assert.IsTrue(font.TryGetEbsc(out var newEbsc));
        Assert.AreEqual(0x00020000u, newEbsc.Version.RawValue);
        Assert.AreEqual(1u, newEbsc.SizeCount);

        Assert.IsTrue(newEbsc.TryGetBitmapScale(0, out var newScale));
        Assert.AreEqual((byte)9, newScale.PpemX);
        Assert.AreEqual((byte)10, newScale.PpemY);
        Assert.AreEqual((byte)11, newScale.SubstitutePpemX);
        Assert.AreEqual((byte)12, newScale.SubstitutePpemY);

        Assert.AreEqual(unchecked((sbyte)-1), newScale.Hori.Ascender);
        Assert.AreEqual(unchecked((sbyte)-2), newScale.Hori.Descender);
        Assert.AreEqual((byte)3, newScale.Hori.WidthMax);

        string tempPath = Path.Combine(Path.GetTempPath(), $"synthetic-ebsc-{Guid.NewGuid():N}.ttf");
        try
        {
            File.WriteAllBytes(tempPath, fontBytes);

            using var legacyFile = new Legacy.OTFile();
            Assert.IsTrue(legacyFile.open(tempPath));
            var legacyFont = legacyFile.GetFont(0)!;

            var legacyEbsc = (Legacy.Table_EBSC)legacyFont.GetTable("EBSC")!;
            Assert.AreEqual(legacyEbsc.version.GetUint(), newEbsc.Version.RawValue);
            Assert.AreEqual(legacyEbsc.numSizes, newEbsc.SizeCount);

            var legacyScale = legacyEbsc.GetBitmapScaleTable(0)!;
            Assert.AreEqual(legacyScale.ppemX, newScale.PpemX);
            Assert.AreEqual(legacyScale.ppemY, newScale.PpemY);
            Assert.AreEqual(legacyScale.substitutePpemX, newScale.SubstitutePpemX);
            Assert.AreEqual(legacyScale.substitutePpemY, newScale.SubstitutePpemY);

            Assert.AreEqual(legacyScale.hori.ascender, newScale.Hori.Ascender);
            Assert.AreEqual(legacyScale.hori.descender, newScale.Hori.Descender);
            Assert.AreEqual(legacyScale.hori.widthMax, newScale.Hori.WidthMax);
        }
        finally
        {
            try { File.Delete(tempPath); } catch { }
        }
    }

    [TestMethod]
    public void SyntheticEblcAndEbdtTables_ParsesAndMatchesLegacy()
    {
        byte[] eblcBytes = BuildEblcTable();
        byte[] ebdtBytes = BuildEbdtTable();

        var builder = new SfntBuilder();
        builder.SetTable(KnownTags.EBLC, eblcBytes);
        builder.SetTable(KnownTags.EBDT, ebdtBytes);
        byte[] fontBytes = builder.ToArray();

        using var file = SfntFile.FromMemory(fontBytes);
        var font = file.GetFont(0);

        Assert.IsTrue(font.TryGetEblc(out var newEblc));
        Assert.IsTrue(font.TryGetEbdt(out var newEbdt));

        Assert.AreEqual(0x00020000u, newEblc.Version.RawValue);
        Assert.AreEqual(1u, newEblc.BitmapSizeTableCount);
        Assert.IsTrue(newEblc.TryGetBitmapSizeTable(0, out var newBst));
        Assert.AreEqual(123u, newBst.ColorRef);
        Assert.AreEqual((byte)9, newBst.PpemX);
        Assert.AreEqual((byte)10, newBst.PpemY);
        Assert.AreEqual((byte)1, newBst.BitDepth);
        Assert.AreEqual(unchecked((sbyte)2), newBst.Flags);

        Assert.AreEqual(0x00020000u, newEbdt.Version.RawValue);

        string tempPath = Path.Combine(Path.GetTempPath(), $"synthetic-eblc-ebdt-{Guid.NewGuid():N}.ttf");
        try
        {
            File.WriteAllBytes(tempPath, fontBytes);

            using var legacyFile = new Legacy.OTFile();
            Assert.IsTrue(legacyFile.open(tempPath));
            var legacyFont = legacyFile.GetFont(0)!;

            var legacyEblc = (Legacy.Table_EBLC)legacyFont.GetTable("EBLC")!;
            var legacyEbdt = (Legacy.Table_EBDT)legacyFont.GetTable("EBDT")!;

            Assert.AreEqual(legacyEblc.version.GetUint(), newEblc.Version.RawValue);
            Assert.AreEqual(legacyEblc.numSizes, newEblc.BitmapSizeTableCount);

            var legacyBst = legacyEblc.GetBitmapSizeTable(0)!;
            Assert.AreEqual(legacyBst.colorRef, newBst.ColorRef);
            Assert.AreEqual(legacyBst.ppemX, newBst.PpemX);
            Assert.AreEqual(legacyBst.ppemY, newBst.PpemY);
            Assert.AreEqual(legacyBst.bitDepth, newBst.BitDepth);
            Assert.AreEqual(legacyBst.flags, newBst.Flags);

            Assert.AreEqual(legacyEbdt.version.GetUint(), newEbdt.Version.RawValue);
        }
        finally
        {
            try { File.Delete(tempPath); } catch { }
        }
    }

    [TestMethod]
    public void SyntheticZapfTable_ParsesAndMatchesLegacy()
    {
        byte[] maxpBytes = BuildMaxp05Table(numGlyphs: 2);
        byte[] zapfBytes = BuildZapfTable();

        var builder = new SfntBuilder();
        builder.SetTable(KnownTags.maxp, maxpBytes);
        builder.SetTable(KnownTags.Zapf, zapfBytes);
        byte[] fontBytes = builder.ToArray();

        using var file = SfntFile.FromMemory(fontBytes);
        var font = file.GetFont(0);

        Assert.IsTrue(font.TryGetMaxp(out var maxp));
        Assert.AreEqual((ushort)2, maxp.NumGlyphs);

        Assert.IsTrue(font.TryGetZapf(out var newZapf));
        Assert.AreEqual(0x00010000u, newZapf.Version.RawValue);
        Assert.AreEqual(0u, newZapf.ExtraInfo);

        Assert.IsTrue(newZapf.TryGetGlyphInfo(0, maxp.NumGlyphs, out var newGlyph0));
        Assert.AreEqual((ushort)1, newGlyph0.UnicodeCount);
        Assert.IsTrue(newGlyph0.TryGetUnicodeCodePoint(0, out ushort cp));
        Assert.AreEqual((ushort)0x0041, cp);
        Assert.IsTrue(newGlyph0.TryGetKindNameCount(out ushort nameCount));
        Assert.AreEqual((ushort)1, nameCount);
        Assert.IsTrue(newGlyph0.TryGetKindName(0, out var newName));
        Assert.IsTrue(newName.TryGetPascalStringBytes(out var newNameBytes));
        Assert.AreEqual("foo", Encoding.ASCII.GetString(newNameBytes));
    }

    private static byte[] BuildSvgTable(byte[] docBytes, out SvgTable.SvgDocumentIndex.SvgDocumentRecord record)
    {
        const int headerLen = 10;
        const int docIndexOffset = headerLen;
        const ushort entryCount = 1;
        const int indexLen = 2 + (entryCount * 12);
        int docOffset = indexLen;

        byte[] table = new byte[headerLen + indexLen + docBytes.Length];
        var span = table.AsSpan();

        WriteU16(span, 0, 0); // version
        WriteU32(span, 2, (uint)docIndexOffset);
        WriteU32(span, 6, 0);

        WriteU16(span, docIndexOffset, entryCount);
        int entryOffset = docIndexOffset + 2;
        WriteU16(span, entryOffset + 0, 0); // startGid
        WriteU16(span, entryOffset + 2, 1); // endGid
        WriteU32(span, entryOffset + 4, (uint)docOffset);
        WriteU32(span, entryOffset + 8, (uint)docBytes.Length);

        docBytes.CopyTo(span.Slice(docIndexOffset + docOffset));

        record = new SvgTable.SvgDocumentIndex.SvgDocumentRecord(
            startGlyphId: 0,
            endGlyphId: 1,
            documentOffset: (uint)docOffset,
            documentLength: (uint)docBytes.Length);
        return table;
    }

    private static byte[] BuildEbscTable()
    {
        byte[] table = new byte[8 + 28];
        var span = table.AsSpan();

        WriteU32(span, 0, 0x00020000u); // version 2.0
        WriteU32(span, 4, 1u); // numSizes

        int o = 8;
        span[o + 0] = unchecked((byte)(sbyte)-1); // hori ascender
        span[o + 1] = unchecked((byte)(sbyte)-2); // hori descender
        span[o + 2] = 3; // hori widthMax

        // vert metrics left as 0
        span[o + 24] = 9;  // ppemX
        span[o + 25] = 10; // ppemY
        span[o + 26] = 11; // sub ppemX
        span[o + 27] = 12; // sub ppemY

        return table;
    }

    private static byte[] BuildEbdtTable()
    {
        byte[] table = new byte[4];
        WriteU32(table.AsSpan(), 0, 0x00020000u);
        return table;
    }

    private static byte[] BuildEblcTable()
    {
        byte[] table = new byte[8 + 48];
        var span = table.AsSpan();

        WriteU32(span, 0, 0x00020000u); // version 2.0
        WriteU32(span, 4, 1u); // numSizes

        int o = 8;
        WriteU32(span, o + 0, 0u); // indexSubTableArrayOffset
        WriteU32(span, o + 4, 0u); // indexTablesSize
        WriteU32(span, o + 8, 0u); // numberOfIndexSubTables
        WriteU32(span, o + 12, 123u); // colorRef

        span[o + 16 + 0] = unchecked((byte)(sbyte)-1); // hori ascender
        span[o + 16 + 1] = unchecked((byte)(sbyte)-2); // hori descender
        span[o + 16 + 2] = 3; // hori widthMax

        // vert metrics left as 0
        WriteU16(span, o + 40, 0); // startGlyphIndex
        WriteU16(span, o + 42, 1); // endGlyphIndex
        span[o + 44] = 9;  // ppemX
        span[o + 45] = 10; // ppemY
        span[o + 46] = 1;  // bitDepth
        span[o + 47] = 2;  // flags

        return table;
    }

    private static byte[] BuildMaxp05Table(ushort numGlyphs)
    {
        byte[] table = new byte[6];
        var span = table.AsSpan();
        WriteU32(span, 0, 0x00005000u); // v0.5
        WriteU16(span, 4, numGlyphs);
        return table;
    }

    private static byte[] BuildZapfTable()
    {
        // Zapf header(8) + offsets[2]*4 + glyphInfo @ 16
        byte[] table = new byte[35];
        var span = table.AsSpan();

        WriteU32(span, 0, 0x00010000u); // version 1.0
        WriteU32(span, 4, 0u); // extraInfo

        WriteU32(span, 8, 16u); // glyph0 glyphInfo offset
        WriteU32(span, 12, 0u); // glyph1 no info

        int o = 16;
        WriteU32(span, o + 0, 0u); // groupOffset
        WriteU32(span, o + 4, 0u); // featOffset
        WriteU16(span, o + 8, 1);  // unicode count
        WriteU16(span, o + 10, 0x0041); // 'A'

        WriteU16(span, o + 12, 1); // nNames
        span[o + 14] = 0; // KindName type (<64)
        span[o + 15] = 3; // pascal length
        span[o + 16] = (byte)'f';
        span[o + 17] = (byte)'o';
        span[o + 18] = (byte)'o';

        return table;
    }

    private static void WriteU16(Span<byte> data, int offset, ushort value)
        => BinaryPrimitives.WriteUInt16BigEndian(data.Slice(offset, 2), value);

    private static void WriteU32(Span<byte> data, int offset, uint value)
        => BinaryPrimitives.WriteUInt32BigEndian(data.Slice(offset, 4), value);
}
