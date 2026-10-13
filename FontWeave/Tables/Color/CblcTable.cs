using FontWeave.Core;

namespace FontWeave.Tables;

/// <summary>One IndexSubtableRecord's wire triple (E L179-181): the glyph range
/// plus the subtable offset from the IndexSubtableList start.</summary>
public readonly record struct CblcIndexSubtableRecord(int Ordinal,
    ushort FirstGlyphIndex, ushort LastGlyphIndex, uint IndexSubtableOffset);

/// <summary>One glyph slot's resolved CBDT window (the card §2 location face):
/// the image format to decode with, the absolute pool position and the byte
/// length derived from the index layer (the offset delta, the imageSize or the
/// pair delta). Zero length marks the absent form (the equal-consecutive
/// offsets pair, E L264; a missing sparse entry).</summary>
public readonly record struct CblcGlyphLocation(ushort ImageFormat,
    ulong PositionInCbdt, ulong ByteLength);

/// <summary>The typed five-format index subtable body (card T6-05 §2): the
/// 8-byte IndexSubHeader words plus the format's own body. The hierarchy is
/// the one T6-08 reuses (the same index codec, card §1); inherited image
/// formats never reach it through this card's validate gate.</summary>
public abstract class CblcIndexSubtable
{
    protected CblcIndexSubtable(int ordinal, ushort indexFormat, ushort imageFormat,
        uint imageDataOffset, ushort firstGlyphIndex, ushort lastGlyphIndex)
    {
        Ordinal = ordinal;
        IndexFormat = indexFormat;
        ImageFormat = imageFormat;
        ImageDataOffset = imageDataOffset;
        FirstGlyphIndex = firstGlyphIndex;
        LastGlyphIndex = lastGlyphIndex;
    }

    /// <summary>The record ordinal inside its strike's list (wire order).</summary>
    public int Ordinal { get; }
    public ushort IndexFormat { get; }
    public ushort ImageFormat { get; }
    /// <summary>The cross-table domain: the image data's base inside the CBDT
    /// pool (E L193-199; "Offset to image data in EBDT table" reads the CBDT
    /// table for the CBLC-indexed color glyphs).</summary>
    public uint ImageDataOffset { get; }
    public ushort FirstGlyphIndex { get; }
    public ushort LastGlyphIndex { get; }
}

/// <summary>Index formats 1 and 3 (E L205-231): the sbitOffsets array over the
/// strike's glyph span plus one sentinel entry, Offset32 on format 1 and
/// Offset16 on format 3. The owned array is widened to u32; the element width
/// is the format's own face (1 vs 3), never re-narrowed.</summary>
public sealed class CblcOffsetSubtable : CblcIndexSubtable
{
    public CblcOffsetSubtable(int ordinal, ushort indexFormat, ushort imageFormat,
        uint imageDataOffset, ushort firstGlyphIndex, ushort lastGlyphIndex, uint[] sbitOffsets)
        : base(ordinal, indexFormat, imageFormat, imageDataOffset, firstGlyphIndex,
              lastGlyphIndex) => SbitOffsets = sbitOffsets;

    /// <summary>The offsets relative to <see cref="CblcIndexSubtable.ImageDataOffset"/>,
    /// sentinel included (last-first+2 entries; the last entry sizes the final
    /// glyph, E L209-212).</summary>
    public uint[] SbitOffsets { get; }
}

/// <summary>Index format 2 (E L214-222): the constant byte size of every
/// glyph's image plus the shared big metrics (the metrics live here, so the
/// only defined image pairing is format 19, D L113's "metrics in CBLC table").</summary>
public sealed class CblcImageSizeSubtable : CblcIndexSubtable
{
    public CblcImageSizeSubtable(int ordinal, ushort imageFormat, uint imageDataOffset,
        ushort firstGlyphIndex, ushort lastGlyphIndex, uint imageSize, byte[] bigMetrics)
        : base(ordinal, CblcLayout.IndexFormat2, imageFormat, imageDataOffset,
              firstGlyphIndex, lastGlyphIndex)
    {
        ImageSize = imageSize;
        BigMetrics = bigMetrics;
    }

    public uint ImageSize { get; }

    /// <summary>The 8-byte BigGlyphMetrics block, raw (D L70-77; height/width
    /// u8, the bearing/advance words - values are application-domain, never
    /// interpreted here).</summary>
    public byte[] BigMetrics { get; }
}

/// <summary>Index format 4 (E L233-248): the sparse glyph ID / Offset16 pair
/// array, numGlyphs pairs plus the sentinel pair. The owned arrays keep all
/// n+1 pairs; the sentinel's offset sizes the final glyph.</summary>
public sealed class CblcPairSubtable : CblcIndexSubtable
{
    public CblcPairSubtable(int ordinal, ushort imageFormat, uint imageDataOffset,
        ushort firstGlyphIndex, ushort lastGlyphIndex, ushort[] glyphIds, ushort[] sbitOffsets)
        : base(ordinal, CblcLayout.IndexFormat4, imageFormat, imageDataOffset,
              firstGlyphIndex, lastGlyphIndex)
    {
        GlyphIds = glyphIds;
        SbitOffsets = sbitOffsets;
    }

    /// <summary>The pairs' glyph IDs in array order (the sentinel included);
    /// validated ascending against the E L262 contiguous-order premise.</summary>
    public ushort[] GlyphIds { get; }

    /// <summary>The pairs' offsets relative to
    /// <see cref="CblcIndexSubtable.ImageDataOffset"/> (the sentinel included).</summary>
    public ushort[] SbitOffsets { get; }
}

/// <summary>Index format 5 (E L250-258): the constant image size and shared
/// big metrics over a sorted sparse glyph ID array (E L258 "sorted by glyph
/// ID" is the read-side hard face).</summary>
public sealed class CblcGlyphIdArraySubtable : CblcIndexSubtable
{
    public CblcGlyphIdArraySubtable(int ordinal, ushort imageFormat, uint imageDataOffset,
        ushort firstGlyphIndex, ushort lastGlyphIndex, uint imageSize, byte[] bigMetrics,
        ushort[] glyphIds)
        : base(ordinal, CblcLayout.IndexFormat5, imageFormat, imageDataOffset,
              firstGlyphIndex, lastGlyphIndex)
    {
        ImageSize = imageSize;
        BigMetrics = bigMetrics;
        GlyphIds = glyphIds;
    }

    public uint ImageSize { get; }

    /// <summary>The 8-byte BigGlyphMetrics block, raw (same face as format 2).</summary>
    public byte[] BigMetrics { get; }

    /// <summary>The sparse glyph IDs, validated ascending (E L258).</summary>
    public ushort[] GlyphIds { get; }
}

/// <summary>One owned BitmapSize row plus its index layer (card T6-05 §2): the
/// twelve declared field values with the two SbitLineMetrics blocks riding raw
/// 12-byte runs (E L96 - the rasterizer never reads them here), and the
/// subtables in wire order with their deshaped bodies.</summary>
public sealed class CblcStrike
{
    public uint IndexSubtableListOffset { get; }
    public uint IndexSubtableListSize { get; }
    public uint ColorRef { get; }
    /// <summary>The hori SbitLineMetrics block's 12 raw bytes (E L83-94).</summary>
    public byte[] HoriMetrics { get; }
    /// <summary>The vert SbitLineMetrics block's 12 raw bytes.</summary>
    public byte[] VertMetrics { get; }
    public ushort StartGlyphIndex { get; }
    public ushort EndGlyphIndex { get; }
    public byte PpemX { get; }
    public byte PpemY { get; }
    public byte BitDepth { get; }
    /// <summary>The int8 flags word raw (bit0/1 the metric-direction keys,
    /// 0xFC reserved-zero - the reserved face is validated, never edited).</summary>
    public sbyte Flags { get; }
    public IReadOnlyList<CblcIndexSubtable> Subtables => Array.AsReadOnly(_subtables);
    private readonly CblcIndexSubtable[] _subtables;

    public CblcStrike(uint indexSubtableListOffset, uint indexSubtableListSize,
        uint colorRef, byte[] horiMetrics, byte[] vertMetrics, ushort startGlyphIndex,
        ushort endGlyphIndex, byte ppemX, byte ppemY, byte bitDepth, sbyte flags,
        CblcIndexSubtable[] subtables)
    {
        IndexSubtableListOffset = indexSubtableListOffset;
        IndexSubtableListSize = indexSubtableListSize;
        ColorRef = colorRef;
        HoriMetrics = horiMetrics;
        VertMetrics = vertMetrics;
        StartGlyphIndex = startGlyphIndex;
        EndGlyphIndex = endGlyphIndex;
        PpemX = ppemX;
        PpemY = ppemY;
        BitDepth = bitDepth;
        Flags = flags;
        _subtables = subtables;
    }

    /// <summary>The color-strike observation key (D L54): bitDepth 32 marks
    /// the BGRA pre-multiplied sRGB domain - a consumer note, never enforced.</summary>
    public bool IsColorStrike => BitDepth == CblcLayout.BitDepthColor;
}

/// <summary>Owned CBLC table (card T6-05 §2): the version receipts and the
/// bitmapSizes rows in wire order, each with its index subtables deshaped.
/// No PNG decoding, no strike selection, no scaling, no metrics application -
/// those stay consumer domains (card §11). Produced by
/// CblcView.TryReadTable; consumers re-root through CblcTableCompiler.</summary>
public sealed class CblcTable
{
    private readonly CblcStrike[] _sizes;

    internal CblcTable(ushort majorVersion, ushort minorVersion, CblcStrike[] sizes,
        DataLocation location, int consumedLength)
    {
        MajorVersion = majorVersion;
        MinorVersion = minorVersion;
        _sizes = sizes;
        Location = location;
        ConsumedLength = consumedLength;
    }

    /// <summary>The wire version receipt: major 3 (the only defined major) and
    /// minor 0. The version words have no edit surface (OQ-T6-05-13).</summary>
    public ushort MajorVersion { get; }
    public ushort MinorVersion { get; }
    public int SizeCount => _sizes.Length;
    public IReadOnlyList<CblcStrike> Sizes => Array.AsReadOnly(_sizes);

    public DataLocation Location { get; }
    public int ConsumedLength { get; }

    /// <summary>One strike's owned projection.</summary>
    public ReadStatus TryGetStrike(int sizeIndex, out CblcStrike strike,
        out FontDiagnostic diagnostic)
    {
        strike = null!;
        if ((uint)sizeIndex >= (uint)_sizes.Length)
            return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                Location.AtLocal(DataField.Count, CblcLayout.BitmapSizeAt, sizeIndex),
                out diagnostic, unchecked((ulong)(long)sizeIndex), (ulong)_sizes.Length);
        strike = _sizes[sizeIndex];
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>Atomically rewrites one strike's two raw SbitLineMetrics
    /// blocks (the card §4.1 edit face): 12 bytes each, traveling raw - the
    /// rasterizer never reads them here (E L96), so no value interpretation
    /// re-answers. The strike's index layer rides unchanged; the compile
    /// re-emits the rows with the fresh bytes.</summary>
    public ReadStatus TryWithStrikeMetrics(int sizeIndex, ReadOnlySpan<byte> hori,
        ReadOnlySpan<byte> vert, out CblcTable? updated, out FontDiagnostic diagnostic)
    {
        updated = null;
        if ((uint)sizeIndex >= (uint)_sizes.Length)
            return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                Location.AtLocal(DataField.Count, CblcLayout.BitmapSizeAt, sizeIndex),
                out diagnostic, unchecked((ulong)(long)sizeIndex), (ulong)_sizes.Length);
        if (hori.Length != CblcLayout.SbitLineMetricsLength ||
            vert.Length != CblcLayout.SbitLineMetricsLength)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.AtLocal(DataField.Length, 0, sizeIndex), out diagnostic,
                (ulong)hori.Length, (ulong)CblcLayout.SbitLineMetricsLength);
        var source = _sizes[sizeIndex];
        var sizes = new CblcStrike[_sizes.Length];
        Array.Copy(_sizes, sizes, _sizes.Length);
        sizes[sizeIndex] = new CblcStrike(source.IndexSubtableListOffset,
            source.IndexSubtableListSize, source.ColorRef, hori.ToArray(), vert.ToArray(),
            source.StartGlyphIndex, source.EndGlyphIndex, source.PpemX, source.PpemY,
            source.BitDepth, source.Flags, CloneSubtables(source));
        updated = new(MajorVersion, MinorVersion, sizes, Location, ConsumedLength);
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>Atomically rewrites one strike's ppem pair, bit depth and
    /// flags word (the card §4.1 edit face) with the value domains
    /// re-answered: the bitDepth closed set and the flags reserved mask are
    /// the read-side hard faces, so the edit refuses the same shapes. The
    /// glyph range and colorRef ride unchanged; structural changes (strike
    /// count changes, format swaps, version writes) have no surface
    /// (OQ-T6-05-13's finite refusals).</summary>
    public ReadStatus TryWithBitmapSize(int sizeIndex, byte ppemX, byte ppemY,
        byte bitDepth, sbyte flags, out CblcTable? updated, out FontDiagnostic diagnostic)
    {
        updated = null;
        if ((uint)sizeIndex >= (uint)_sizes.Length)
            return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                Location.AtLocal(DataField.Count, CblcLayout.BitmapSizeAt, sizeIndex),
                out diagnostic, unchecked((ulong)(long)sizeIndex), (ulong)_sizes.Length);
        if (!CblcLayout.IsBitDepthDefined(bitDepth))
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.AtLocal(DataField.Payload, 0, sizeIndex), out diagnostic,
                bitDepth, 0UL);
        if ((flags & CblcLayout.FlagsReservedMask) != 0)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.AtLocal(DataField.Flags, 0, sizeIndex), out diagnostic,
                (ulong)(flags & CblcLayout.FlagsReservedMask), 0UL);
        var source = _sizes[sizeIndex];
        var sizes = new CblcStrike[_sizes.Length];
        Array.Copy(_sizes, sizes, _sizes.Length);
        sizes[sizeIndex] = new CblcStrike(source.IndexSubtableListOffset,
            source.IndexSubtableListSize, source.ColorRef, source.HoriMetrics,
            source.VertMetrics, source.StartGlyphIndex, source.EndGlyphIndex,
            ppemX, ppemY, bitDepth, flags, CloneSubtables(source));
        updated = new(MajorVersion, MinorVersion, sizes, Location, ConsumedLength);
        diagnostic = default;
        return ReadStatus.Done;
    }

    private static CblcIndexSubtable[] CloneSubtables(CblcStrike strike)
    {
        var copies = new CblcIndexSubtable[strike.Subtables.Count];
        for (int r = 0; r < copies.Length; r++)
            copies[r] = strike.Subtables[r];
        return copies;
    }
}
