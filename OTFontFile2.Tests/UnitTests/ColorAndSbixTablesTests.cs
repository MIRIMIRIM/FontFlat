using Microsoft.VisualStudio.TestTools.UnitTesting;
using OTFontFile2.Tables;
using System.Buffers.Binary;

namespace OTFontFile2.Tests.UnitTests;

[TestClass]
public sealed class ColorAndSbixTablesTests
{
    [TestMethod]
    public void SyntheticCpalTable_ParsesPaletteColors()
    {
        byte[] cpalBytes = BuildCpalV0();

        var builder = new SfntBuilder();
        builder.SetTable(KnownTags.CPAL, cpalBytes);
        byte[] fontBytes = builder.ToArray();

        using var file = SfntFile.FromMemory(fontBytes);
        var font = file.GetFont(0);

        Assert.IsTrue(font.TryGetCpal(out var cpal));
        Assert.AreEqual((ushort)0, cpal.Version);
        Assert.AreEqual((ushort)2, cpal.PaletteEntryCount);
        Assert.AreEqual((ushort)1, cpal.PaletteCount);
        Assert.AreEqual((ushort)2, cpal.ColorRecordCount);

        Assert.IsTrue(cpal.TryGetPaletteStartIndex(0, out ushort start));
        Assert.AreEqual((ushort)0, start);

        Assert.IsTrue(cpal.TryGetPaletteColor(0, 0, out var c0));
        Assert.AreEqual((byte)0, c0.Blue);
        Assert.AreEqual((byte)0, c0.Green);
        Assert.AreEqual((byte)255, c0.Red);
        Assert.AreEqual((byte)255, c0.Alpha);

        Assert.IsTrue(cpal.TryGetPaletteColor(0, 1, out var c1));
        Assert.AreEqual((byte)0, c1.Blue);
        Assert.AreEqual((byte)255, c1.Green);
        Assert.AreEqual((byte)0, c1.Red);
        Assert.AreEqual((byte)255, c1.Alpha);

        Assert.IsTrue(cpal.TryGetPaletteType(0, out uint paletteType));
        Assert.AreEqual(0u, paletteType);
        Assert.IsTrue(cpal.TryGetPaletteLabelNameId(0, out ushort label));
        Assert.AreEqual(CpalTable.NoNameId, label);
        Assert.IsTrue(cpal.TryGetPaletteEntryLabelNameId(0, out ushort entryLabel));
        Assert.AreEqual(CpalTable.NoNameId, entryLabel);
    }

    [TestMethod]
    public void SyntheticColrV0Table_ParsesLayers()
    {
        byte[] colrBytes = BuildColrV0();

        var builder = new SfntBuilder();
        builder.SetTable(KnownTags.COLR, colrBytes);
        byte[] fontBytes = builder.ToArray();

        using var file = SfntFile.FromMemory(fontBytes);
        var font = file.GetFont(0);

        Assert.IsTrue(font.TryGetColr(out var colr));
        Assert.AreEqual((ushort)0, colr.Version);
        Assert.AreEqual((ushort)1, colr.BaseGlyphRecordCount);
        Assert.AreEqual((ushort)2, colr.LayerRecordCount);

        Assert.IsTrue(colr.TryFindBaseGlyphRecord(5, out var baseRec));
        Assert.AreEqual((ushort)5, baseRec.BaseGlyphId);
        Assert.AreEqual((ushort)0, baseRec.FirstLayerIndex);
        Assert.AreEqual((ushort)2, baseRec.NumLayers);

        var e = colr.EnumerateLayers(baseRec);
        Assert.IsTrue(e.MoveNext());
        Assert.AreEqual((ushort)10, e.Current.LayerGlyphId);
        Assert.AreEqual((ushort)0, e.Current.PaletteIndex);
        Assert.IsTrue(e.MoveNext());
        Assert.AreEqual((ushort)11, e.Current.LayerGlyphId);
        Assert.AreEqual((ushort)1, e.Current.PaletteIndex);
        Assert.IsFalse(e.MoveNext());
    }

    [TestMethod]
    public void SyntheticSbixTable_ParsesStrikeAndGlyph()
    {
        byte[] maxpBytes = BuildMaxp05(numGlyphs: 1);
        byte[] sbixBytes = BuildSbix(numGlyphs: 1);

        var builder = new SfntBuilder();
        builder.SetTable(KnownTags.maxp, maxpBytes);
        builder.SetTable(KnownTags.sbix, sbixBytes);
        byte[] fontBytes = builder.ToArray();

        using var file = SfntFile.FromMemory(fontBytes);
        var font = file.GetFont(0);

        Assert.IsTrue(font.TryGetMaxp(out var maxp));
        Assert.AreEqual((ushort)1, maxp.NumGlyphs);

        Assert.IsTrue(font.TryGetSbix(out var sbix));
        Assert.AreEqual(1u, sbix.StrikeCount);

        Assert.IsTrue(sbix.TryGetStrike(0, maxp.NumGlyphs, out var strike));
        Assert.AreEqual((ushort)16, strike.Ppem);
        Assert.AreEqual((ushort)72, strike.Resolution);

        Assert.IsTrue(strike.TryGetGlyphDataSpan(0, out var glyphData));
        Assert.IsTrue(SbixTable.TryReadGlyphHeader(glyphData, out var header, out var payload));
        Assert.AreEqual((short)1, header.OriginOffsetX);
        Assert.AreEqual((short)-2, header.OriginOffsetY);
        Assert.AreEqual(0x706E6720u, header.GraphicType.Value); // 'png '
        Assert.IsFalse(header.IsReferenceType);
        Assert.AreEqual(3, payload.Length);
        Assert.AreEqual((byte)1, payload[0]);
        Assert.AreEqual((byte)2, payload[1]);
        Assert.AreEqual((byte)3, payload[2]);
    }

    [TestMethod]
    public void SyntheticBitmapAliases_AccessibleViaCblcCbdtBlocBdat()
    {
        byte[] locationBytes = BuildEblcLikeEmpty();
        byte[] dataBytes = BuildEbdtLikeEmpty();

        var builder = new SfntBuilder();
        builder.SetTable(KnownTags.CBLC, locationBytes);
        builder.SetTable(KnownTags.CBDT, dataBytes);
        builder.SetTable(KnownTags.BLOC, locationBytes);
        builder.SetTable(KnownTags.BDAT, dataBytes);
        byte[] fontBytes = builder.ToArray();

        using var file = SfntFile.FromMemory(fontBytes);
        var font = file.GetFont(0);

        Assert.IsTrue(font.TryGetCblc(out var cblc));
        Assert.AreEqual(0u, cblc.BitmapSizeTableCount);
        Assert.IsTrue(font.TryGetCbdt(out var cbdt));
        Assert.AreEqual(0x00020000u, cbdt.Version.RawValue);

        Assert.IsTrue(font.TryGetBloc(out var bloc));
        Assert.AreEqual(0u, bloc.BitmapSizeTableCount);
        Assert.IsTrue(font.TryGetBdat(out var bdat));
        Assert.AreEqual(0x00020000u, bdat.Version.RawValue);
    }

    private static byte[] BuildCpalV0()
    {
        // header(12) + paletteIndices(2) + colorRecords(8)
        byte[] table = new byte[22];
        var span = table.AsSpan();

        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(0, 2), 0); // version
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(2, 2), 2); // numPaletteEntries
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(4, 2), 1); // numPalettes
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(6, 2), 2); // numColorRecords
        BinaryPrimitives.WriteUInt32BigEndian(span.Slice(8, 4), 14u); // offsetFirstColorRecord

        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(12, 2), 0); // palette[0] start index

        // color record 0: RGBA=(255,0,0,255) stored as BGRA
        span[14 + 0] = 0;
        span[14 + 1] = 0;
        span[14 + 2] = 255;
        span[14 + 3] = 255;

        // color record 1: RGBA=(0,255,0,255) stored as BGRA
        span[18 + 0] = 0;
        span[18 + 1] = 255;
        span[18 + 2] = 0;
        span[18 + 3] = 255;

        return table;
    }

    private static byte[] BuildColrV0()
    {
        // header(14) + baseGlyphRecords(6) + layerRecords(8)
        byte[] table = new byte[28];
        var span = table.AsSpan();

        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(0, 2), 0); // version
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(2, 2), 1); // numBaseGlyphRecords
        BinaryPrimitives.WriteUInt32BigEndian(span.Slice(4, 4), 14u); // baseGlyphRecordsOffset
        BinaryPrimitives.WriteUInt32BigEndian(span.Slice(8, 4), 20u); // layerRecordsOffset
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(12, 2), 2); // numLayerRecords

        // BaseGlyphRecord: baseGlyph=5, firstLayer=0, numLayers=2
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(14, 2), 5);
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(16, 2), 0);
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(18, 2), 2);

        // LayerRecord[0]: glyph=10, paletteIndex=0
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(20, 2), 10);
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(22, 2), 0);

        // LayerRecord[1]: glyph=11, paletteIndex=1
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(24, 2), 11);
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(26, 2), 1);

        return table;
    }

    private static byte[] BuildSbix(ushort numGlyphs)
    {
        // sbixHeader(8) + strikeOffsets(4) + strike
        // strike: header(4) + glyphOffsets((numGlyphs+1)*4) + glyphData
        const int sbixHeaderLen = 8;
        const int strikeOffsetsLen = 4;
        int strikeOffset = sbixHeaderLen + strikeOffsetsLen;

        int glyphOffsetsLen = checked((numGlyphs + 1) * 4);

        // One glyph: originX(2)+originY(2)+graphicType(4)+payload(3)
        byte[] glyphData = new byte[11];
        BinaryPrimitives.WriteInt16BigEndian(glyphData.AsSpan(0, 2), 1);
        BinaryPrimitives.WriteInt16BigEndian(glyphData.AsSpan(2, 2), -2);
        glyphData[4] = (byte)'p';
        glyphData[5] = (byte)'n';
        glyphData[6] = (byte)'g';
        glyphData[7] = (byte)' ';
        glyphData[8] = 1;
        glyphData[9] = 2;
        glyphData[10] = 3;

        int firstGlyphDataOffset = 4 + glyphOffsetsLen;
        int strikeLen = 4 + glyphOffsetsLen + glyphData.Length;
        byte[] table = new byte[strikeOffset + strikeLen];
        var span = table.AsSpan();

        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(0, 2), 1); // version
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(2, 2), 0); // flags
        BinaryPrimitives.WriteUInt32BigEndian(span.Slice(4, 4), 1u); // numStrikes
        BinaryPrimitives.WriteUInt32BigEndian(span.Slice(8, 4), (uint)strikeOffset);

        int o = strikeOffset;
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(o + 0, 2), 16); // ppem
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(o + 2, 2), 72); // resolution

        BinaryPrimitives.WriteUInt32BigEndian(span.Slice(o + 4, 4), (uint)firstGlyphDataOffset);
        BinaryPrimitives.WriteUInt32BigEndian(span.Slice(o + 8, 4), (uint)(firstGlyphDataOffset + glyphData.Length));

        glyphData.CopyTo(span.Slice(o + firstGlyphDataOffset));
        return table;
    }

    private static byte[] BuildMaxp05(ushort numGlyphs)
    {
        byte[] table = new byte[6];
        var span = table.AsSpan();
        BinaryPrimitives.WriteUInt32BigEndian(span.Slice(0, 4), 0x00005000u); // v0.5
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(4, 2), numGlyphs);
        return table;
    }

    private static byte[] BuildEblcLikeEmpty()
    {
        byte[] table = new byte[8];
        var span = table.AsSpan();
        BinaryPrimitives.WriteUInt32BigEndian(span.Slice(0, 4), 0x00020000u);
        BinaryPrimitives.WriteUInt32BigEndian(span.Slice(4, 4), 0u);
        return table;
    }

    private static byte[] BuildEbdtLikeEmpty()
    {
        byte[] table = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(table.AsSpan(0, 4), 0x00020000u);
        return table;
    }
}

