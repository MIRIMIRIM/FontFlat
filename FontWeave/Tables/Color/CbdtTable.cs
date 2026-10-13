using FontWeave.Core;

namespace FontWeave.Tables;

/// <summary>Format 17's five-byte SmallGlyphMetrics head, raw (D L83-87):
/// height/width u8, bearingX/Y i8, advance u8. The values are application
/// domain - placement and scaling never happen here (card §11).</summary>
public readonly record struct CbdtSmallGlyphMetrics(byte Height, byte Width,
    sbyte BearingX, sbyte BearingY, byte Advance)
{
    internal byte[] ToBytes() =>
    [
        Height, Width, unchecked((byte)BearingX), unchecked((byte)BearingY), Advance,
    ];
}

/// <summary>Formats 18's eight-byte BigGlyphMetrics head, raw (D L70-77):
/// height/width u8, the horizontal bearing/advance trio, the vertical
/// trio. Same raw-value discipline.</summary>
public readonly record struct CbdtBigGlyphMetrics(byte Height, byte Width,
    sbyte HoriBearingX, sbyte HoriBearingY, byte HoriAdvance,
    sbyte VertBearingX, sbyte VertBearingY, byte VertAdvance)
{
    internal byte[] ToBytes() =>
    [
        Height, Width, unchecked((byte)HoriBearingX), unchecked((byte)HoriBearingY),
        HoriAdvance, unchecked((byte)VertBearingX), unchecked((byte)VertBearingY),
        VertAdvance,
    ];
}

/// <summary>One glyph image decoded from the CBDT pool through a CBLC window
/// (the joint read's projection): the image format, the raw metrics head
/// (5 bytes on 17, 8 bytes on 18, absent on 19 - the metrics live in the CBLC
/// subtable there) and the opaque payload bytes.</summary>
public readonly record struct CbdtGlyphImage(ushort ImageFormat, byte[]? Metrics,
    byte[] Payload);

/// <summary>One owned CBDT image in joint-read-chain order (card T6-05 §2):
/// the format, the raw metrics head and the verbatim payload. PNG data is
/// never decoded, chunk-inspected or recompressed (D L60's "undefined
/// behavior" line excludes read-side obligations; the chunk whitelist is a
/// container-level observation key only).</summary>
public sealed class CbdtImage
{
    /// <summary>Creates one owned image part (the factory/edit paths and the
    /// public TryCreate surface share this shape - the SbixStrike line).</summary>
    public CbdtImage(ushort imageFormat, byte[]? metrics, byte[] payload)
    {
        ImageFormat = imageFormat;
        Metrics = metrics;
        Payload = payload;
    }

    public ushort ImageFormat { get; }
    /// <summary>The raw metrics head: 5 bytes on format 17, 8 on 18, null on
    /// 19 (the "metrics in CBLC table" pairing, D L113).</summary>
    public byte[]? Metrics { get; }
    /// <summary>The dataLen-counted opaque payload bytes, byte-for-byte.</summary>
    public byte[] Payload { get; }
}

/// <summary>Owned CBDT table (card T6-05 §2): the version receipts and the
/// images materialized in joint read-chain order (strikes in wire order,
/// records in wire order, glyph slots in ascending order - the compiler's
/// first-appearance emission order). The pool's unaddressed bytes (if any)
/// have no owned face - a gapped pool re-emits packed and lands in the
/// wire-equivalent tier. Produced by the joint CbdtView.TryReadTable.</summary>
public sealed class CbdtTable
{
    private readonly CbdtImage[] _images;

    internal CbdtTable(ushort majorVersion, ushort minorVersion, CbdtImage[] images,
        DataLocation location, int consumedLength)
    {
        MajorVersion = majorVersion;
        MinorVersion = minorVersion;
        _images = images;
        Location = location;
        ConsumedLength = consumedLength;
    }

    /// <summary>The wire version receipt: major 3, minor 0. No edit surface
    /// (OQ-T6-05-13: version writes are a finite refusal).</summary>
    public ushort MajorVersion { get; }
    public ushort MinorVersion { get; }
    public int ImageCount => _images.Length;
    public IReadOnlyList<CbdtImage> Images => Array.AsReadOnly(_images);

    public DataLocation Location { get; }
    public int ConsumedLength { get; }

    /// <summary>One image's owned projection.</summary>
    public ReadStatus TryGetImage(int imageIndex, out CbdtImage image,
        out FontDiagnostic diagnostic)
    {
        image = null!;
        if ((uint)imageIndex >= (uint)_images.Length)
            return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                Location.AtLocal(DataField.Count, 0, imageIndex), out diagnostic,
                unchecked((ulong)(long)imageIndex), (ulong)_images.Length);
        image = _images[imageIndex];
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>Atomically replaces one image's opaque payload whole (the
    /// card §4.1 edit face): the data length re-derives from the replacement
    /// and every cross-table fixup re-answers at the next compile - the
    /// payload bytes travel verbatim, never decoded. The format and metrics
    /// head ride unchanged; structural changes (image count changes, format
    /// swaps, version writes) have no surface (OQ-T6-05-13's finite
    /// refusals).</summary>
    public ReadStatus TryWithImagePayload(int imageIndex, ReadOnlySpan<byte> payload,
        out CbdtTable? updated, out FontDiagnostic diagnostic)
    {
        updated = null;
        ReadStatus status = RequireImageDomain(imageIndex, out diagnostic);
        if (status != ReadStatus.Done) return status;
        var images = new CbdtImage[_images.Length];
        Array.Copy(_images, images, _images.Length);
        var source = _images[imageIndex];
        images[imageIndex] = new CbdtImage(source.ImageFormat, source.Metrics,
            payload.ToArray());
        updated = new(MajorVersion, MinorVersion, images, Location, ConsumedLength);
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>Atomically rewrites one image's raw metrics head (the card
    /// §4.1 edit face): 5 bytes on format 17, 8 on 18 - the values travel
    /// raw (the application domain, never interpreted here); format 19 has
    /// no head (the metrics live in the CBLC subtable) and refuses.</summary>
    public ReadStatus TryWithGlyphMetrics(int imageIndex, ReadOnlySpan<byte> metrics,
        out CbdtTable? updated, out FontDiagnostic diagnostic)
    {
        updated = null;
        ReadStatus status = RequireImageDomain(imageIndex, out diagnostic);
        if (status != ReadStatus.Done) return status;
        var source = _images[imageIndex];
        int expectedLength = source.ImageFormat switch
        {
            CblcLayout.ImageFormat17 => CbdtLayout.SmallGlyphMetricsLength,
            CblcLayout.ImageFormat18 => CblcLayout.BigGlyphMetricsLength,
            _ => -1,
        };
        if (expectedLength < 0)
            return ReadWire.Fail(ReadStatus.UnsupportedFormat,
                DiagnosticCode.UnsupportedOperation,
                Location.AtLocal(DataField.Payload, 0, imageIndex), out diagnostic,
                source.ImageFormat, 0UL);
        if (metrics.Length != expectedLength)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.AtLocal(DataField.Length, 0, imageIndex), out diagnostic,
                (ulong)metrics.Length, (ulong)expectedLength);
        var images = new CbdtImage[_images.Length];
        Array.Copy(_images, images, _images.Length);
        images[imageIndex] = new CbdtImage(source.ImageFormat, metrics.ToArray(),
            source.Payload);
        updated = new(MajorVersion, MinorVersion, images, Location, ConsumedLength);
        diagnostic = default;
        return ReadStatus.Done;
    }

    private ReadStatus RequireImageDomain(int imageIndex, out FontDiagnostic diagnostic)
    {
        if ((uint)imageIndex >= (uint)_images.Length)
            return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                Location.AtLocal(DataField.Count, 0, imageIndex), out diagnostic,
                unchecked((ulong)(long)imageIndex), (ulong)_images.Length);
        diagnostic = default;
        return ReadStatus.Done;
    }
}
