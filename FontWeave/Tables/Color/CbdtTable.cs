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
    internal CbdtImage(ushort imageFormat, byte[]? metrics, byte[] payload)
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
}
