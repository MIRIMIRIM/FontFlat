using FontWeave.Core;

namespace FontWeave.Tables;

public readonly record struct EbdtSmallGlyphMetrics(byte Height, byte Width,
    sbyte BearingX, sbyte BearingY, byte Advance)
{
    internal byte[] ToBytes() =>
        [Height, Width, unchecked((byte)BearingX), unchecked((byte)BearingY), Advance];
}

public readonly record struct EbdtBigGlyphMetrics(byte Height, byte Width,
    sbyte HoriBearingX, sbyte HoriBearingY, byte HoriAdvance,
    sbyte VertBearingX, sbyte VertBearingY, byte VertAdvance)
{
    internal byte[] ToBytes() =>
        [Height, Width, unchecked((byte)HoriBearingX), unchecked((byte)HoriBearingY),
         HoriAdvance, unchecked((byte)VertBearingX), unchecked((byte)VertBearingY), VertAdvance];
}

/// <summary>A flat composite reference. Bitmap lookup, nesting and rendering
/// remain the caller's responsibility.</summary>
public readonly record struct EbdtComponent(ushort GlyphId, sbyte XOffset, sbyte YOffset);

public readonly record struct EbdtGlyphImage(ushort ImageFormat, byte[]? Metrics,
    byte[] Data, EbdtComponent[] Components, byte BitDepth, byte CompositePad = 0);

/// <summary>One owned frame. Metrics and image bytes are preserved as supplied;
/// BitDepth records the strike authority used to admit the frame.</summary>
public sealed class EbdtImage
{
    public EbdtImage(ushort imageFormat, byte[]? metrics, byte[] data, byte bitDepth = 1,
        EbdtComponent[]? components = null, byte compositePad = 0)
    {
        ArgumentNullException.ThrowIfNull(data);
        ImageFormat = imageFormat;
        Metrics = metrics;
        Data = data;
        BitDepth = bitDepth;
        Components = components ?? [];
        CompositePad = compositePad;
    }

    public ushort ImageFormat { get; }
    public byte[]? Metrics { get; }
    public byte[] Data { get; }
    public byte BitDepth { get; }
    public EbdtComponent[] Components { get; }
    public byte CompositePad { get; }

    internal ReadStatus CheckShape(DataLocation location, int ordinal,
        out FontDiagnostic diagnostic)
    {
        int metricsLength = BitmapImageCodec.LegacyMetricsLength(ImageFormat);
        if (metricsLength < 0)
            return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
                location.AtLocal(DataField.Payload, 0, ordinal), out diagnostic, ImageFormat, 0UL);
        if (!EblcLayout.IsBitDepthDefined(BitDepth))
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                location.AtLocal(DataField.Payload, 0, ordinal), out diagnostic, BitDepth, 0UL);
        if ((Metrics?.Length ?? 0) != metricsLength || metricsLength == 0 && Metrics is not null)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                location.AtLocal(DataField.Length, 0, ordinal), out diagnostic,
                (ulong)(Metrics?.Length ?? 0), (ulong)metricsLength);
        if (ImageFormat is 8 or 9)
        {
            if (Data.Length != 0)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    location.AtLocal(DataField.Length, 0, ordinal), out diagnostic,
                    (ulong)Data.Length, 0UL);
            if (Components.Length > ushort.MaxValue)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    location.AtLocal(DataField.Count, 0, ordinal), out diagnostic,
                    (ulong)Components.Length, ushort.MaxValue);
        }
        else
        {
            if (Components.Length != 0)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    location.AtLocal(DataField.Count, 0, ordinal), out diagnostic,
                    (ulong)Components.Length, 0UL);
            if (ImageFormat != 5)
            {
                long expected = BitmapImageCodec.DerivedDataLength(ImageFormat, Metrics, BitDepth);
                if (Data.LongLength != expected)
                    return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                        location.AtLocal(DataField.Length, 0, ordinal), out diagnostic,
                        (ulong)Data.LongLength, (ulong)expected);
            }
        }
        diagnostic = default;
        return ReadStatus.Done;
    }
}

/// <summary>Frames in the joint EBLC read-chain order, with absent slots
/// omitted. Pool gaps and frame tails are not materialized.</summary>
public sealed class EbdtTable
{
    private readonly EbdtImage[] _images;

    internal EbdtTable(ushort majorVersion, ushort minorVersion, EbdtImage[] images,
        DataLocation location, int consumedLength)
    {
        MajorVersion = majorVersion;
        MinorVersion = minorVersion;
        _images = images;
        Location = location;
        ConsumedLength = consumedLength;
    }

    public ushort MajorVersion { get; }
    public ushort MinorVersion { get; }
    public int ImageCount => _images.Length;
    public IReadOnlyList<EbdtImage> Images => Array.AsReadOnly(_images);
    public DataLocation Location { get; }
    public int ConsumedLength { get; }

    public ReadStatus TryGetImage(int imageIndex, out EbdtImage image,
        out FontDiagnostic diagnostic)
    {
        image = null!;
        ReadStatus status = RequireImage(imageIndex, out diagnostic);
        if (status != ReadStatus.Done) return status;
        image = _images[imageIndex];
        return ReadStatus.Done;
    }

    /// <summary>Replaces image bytes at the recorded depth. Formats 1/2/6/7
    /// require their metrics-derived length; format 5 keeps its slot length.
    /// Composite formats have no image-data edit surface.</summary>
    public ReadStatus TryWithImageData(int imageIndex, ReadOnlySpan<byte> imageData,
        out EbdtTable? updated, out FontDiagnostic diagnostic)
    {
        updated = null;
        ReadStatus status = RequireImage(imageIndex, out diagnostic);
        if (status != ReadStatus.Done) return status;
        return TryWithImageData(imageIndex, imageData, _images[imageIndex].BitDepth,
            out updated, out diagnostic);
    }

    /// <summary>Explicitly changes the depth receipt and image bytes together.
    /// The matching EBLC strike must carry the same depth at pair compilation;
    /// no pixels are converted or padded implicitly.</summary>
    public ReadStatus TryWithImageData(int imageIndex, ReadOnlySpan<byte> imageData,
        byte bitDepth, out EbdtTable? updated, out FontDiagnostic diagnostic)
    {
        updated = null;
        ReadStatus status = RequireImage(imageIndex, out diagnostic);
        if (status != ReadStatus.Done) return status;
        var source = _images[imageIndex];
        if (source.ImageFormat is 8 or 9)
            return UnsupportedEdit(imageIndex, source.ImageFormat, out diagnostic);
        if (source.ImageFormat == 5 && imageData.Length != source.Data.Length)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.AtLocal(DataField.Length, 0, imageIndex), out diagnostic,
                (ulong)imageData.Length, (ulong)source.Data.Length);
        var replacement = new EbdtImage(source.ImageFormat, source.Metrics,
            imageData.ToArray(), bitDepth, source.Components, source.CompositePad);
        status = replacement.CheckShape(Location, imageIndex, out diagnostic);
        if (status != ReadStatus.Done) return status;
        updated = Replace(imageIndex, replacement);
        return ReadStatus.Done;
    }

    /// <summary>Changes raw metrics without changing pixels. If a dimension
    /// edit changes the required image length, this overload refuses and the
    /// original table remains unchanged; use the explicit data overload.</summary>
    public ReadStatus TryWithGlyphMetrics(int imageIndex, ReadOnlySpan<byte> metrics,
        out EbdtTable? updated, out FontDiagnostic diagnostic)
    {
        updated = null;
        ReadStatus status = RequireImage(imageIndex, out diagnostic);
        if (status != ReadStatus.Done) return status;
        return TryWithGlyphMetrics(imageIndex, metrics, _images[imageIndex].Data,
            out updated, out diagnostic);
    }

    /// <summary>Changes metrics and explicitly supplied image bytes together.
    /// Changed dimensions rederive the frame length and the next pair compile
    /// recomputes all offsets. Composite component arrays remain unchanged.</summary>
    public ReadStatus TryWithGlyphMetrics(int imageIndex, ReadOnlySpan<byte> metrics,
        ReadOnlySpan<byte> imageData, out EbdtTable? updated, out FontDiagnostic diagnostic)
    {
        updated = null;
        ReadStatus status = RequireImage(imageIndex, out diagnostic);
        if (status != ReadStatus.Done) return status;
        var source = _images[imageIndex];
        if (source.ImageFormat == 5)
            return UnsupportedEdit(imageIndex, source.ImageFormat, out diagnostic);
        var replacement = new EbdtImage(source.ImageFormat, metrics.ToArray(),
            imageData.ToArray(), source.BitDepth, source.Components, source.CompositePad);
        status = replacement.CheckShape(Location, imageIndex, out diagnostic);
        if (status != ReadStatus.Done) return status;
        updated = Replace(imageIndex, replacement);
        return ReadStatus.Done;
    }

    private EbdtTable Replace(int imageIndex, EbdtImage replacement)
    {
        var images = (EbdtImage[])_images.Clone();
        images[imageIndex] = replacement;
        return new(MajorVersion, MinorVersion, images, Location, ConsumedLength);
    }

    private ReadStatus RequireImage(int imageIndex, out FontDiagnostic diagnostic)
    {
        if (imageIndex < 0 || (uint)imageIndex >= (uint)_images.Length)
            return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                Location.AtLocal(DataField.Count, 0, imageIndex), out diagnostic,
                unchecked((ulong)(long)imageIndex), (ulong)_images.Length);
        diagnostic = default;
        return ReadStatus.Done;
    }

    private ReadStatus UnsupportedEdit(int ordinal, ushort format, out FontDiagnostic diagnostic) =>
        ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
            Location.AtLocal(DataField.Payload, 0, ordinal), out diagnostic, format, 0UL);
}
