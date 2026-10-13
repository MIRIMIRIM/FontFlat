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
    byte[] Payload)
{
    public byte BitDepth { get; init; }
    public CbdtComponent[] Components { get; init; } = [];
    public byte CompositePad { get; init; }
}

public readonly record struct CbdtComponent(ushort GlyphId, sbyte XOffset, sbyte YOffset);

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
        : this(imageFormat, metrics, payload, 1)
    {
    }

    /// <summary>The depth receipt admits legacy pixels without converting them.
    /// Components remain flat; format 8 preserves its pad byte.</summary>
    public CbdtImage(ushort imageFormat, byte[]? metrics, byte[] payload, byte bitDepth,
        CbdtComponent[]? components = null, byte compositePad = 0)
    {
        ImageFormat = imageFormat;
        Metrics = metrics;
        Payload = payload;
        BitDepth = bitDepth;
        Components = components ?? [];
        CompositePad = compositePad;
    }

    public ushort ImageFormat { get; }
    /// <summary>Raw per-image metrics, or null when metrics live in CBLC.</summary>
    public byte[]? Metrics { get; }
    /// <summary>Opaque pixels or PNG bytes. Composite frames have an empty payload.</summary>
    public byte[] Payload { get; }
    public byte BitDepth { get; }
    public CbdtComponent[] Components { get; }
    public byte CompositePad { get; }
    internal ulong? SourcePosition { get; init; }

    internal DataLocation ComponentLocation(DataLocation location, int componentIndex) =>
        location.AtLocal(DataField.GlyphId, SourcePosition is { } position
            ? position + (ulong)BitmapImageCodec.LegacyHeadLength(ImageFormat) + 4UL * (ulong)componentIndex
            : 0UL, componentIndex);
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
        if (imageIndex < 0 || (uint)imageIndex >= (uint)_images.Length)
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
        return TryWithImagePayload(imageIndex, payload, _images[imageIndex].BitDepth,
            out updated, out diagnostic);
    }

    /// <summary>Replaces payload and the explicit depth receipt together.
    /// Legacy dimensions must match; format 5 preserves its slot length.
    /// No implicit padding, pixel conversion or component edits occur.</summary>
    public ReadStatus TryWithImagePayload(int imageIndex, ReadOnlySpan<byte> payload,
        byte bitDepth, out CbdtTable? updated, out FontDiagnostic diagnostic)
    {
        updated = null;
        ReadStatus status = RequireImageDomain(imageIndex, out diagnostic);
        if (status != ReadStatus.Done) return status;
        var source = _images[imageIndex];
        if (source.ImageFormat is 8 or 9)
            return UnsupportedEdit(imageIndex, source.ImageFormat, out diagnostic);
        if (source.ImageFormat == 5 && payload.Length != source.Payload.Length)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.AtLocal(DataField.Length, 0, imageIndex), out diagnostic,
                (ulong)payload.Length, (ulong)source.Payload.Length);
        var replacement = new CbdtImage(source.ImageFormat, source.Metrics, payload.ToArray(),
            bitDepth, source.Components, source.CompositePad) { SourcePosition = source.SourcePosition };
        status = CbdtTableCompiler.CheckImage(replacement, Location, imageIndex, out diagnostic);
        if (status != ReadStatus.Done) return status;
        updated = Replace(imageIndex, replacement);
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
        return TryWithGlyphMetrics(imageIndex, metrics, _images[imageIndex].Payload,
            out updated, out diagnostic);
    }

    /// <summary>Atomically replaces metrics and supplied pixels.
    /// A metrics-only dimension edit refuses when the old legacy payload
    /// no longer matches; components and PNG bytes remain verbatim.</summary>
    public ReadStatus TryWithGlyphMetrics(int imageIndex, ReadOnlySpan<byte> metrics,
        ReadOnlySpan<byte> payload, out CbdtTable? updated, out FontDiagnostic diagnostic)
    {
        updated = null;
        ReadStatus status = RequireImageDomain(imageIndex, out diagnostic);
        if (status != ReadStatus.Done) return status;
        var source = _images[imageIndex];
        int expectedLength = CbdtTableCompiler.MetricsLength(source.ImageFormat);
        if (expectedLength == 0) expectedLength = -1;
        if (expectedLength < 0)
            return ReadWire.Fail(ReadStatus.UnsupportedFormat,
                DiagnosticCode.UnsupportedOperation,
                Location.AtLocal(DataField.Payload, 0, imageIndex), out diagnostic,
                source.ImageFormat, 0UL);
        if (metrics.Length != expectedLength)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.AtLocal(DataField.Length, 0, imageIndex), out diagnostic,
                (ulong)metrics.Length, (ulong)expectedLength);
        var replacement = new CbdtImage(source.ImageFormat, metrics.ToArray(), payload.ToArray(),
            source.BitDepth, source.Components, source.CompositePad) { SourcePosition = source.SourcePosition };
        status = CbdtTableCompiler.CheckImage(replacement, Location, imageIndex, out diagnostic);
        if (status != ReadStatus.Done) return status;
        updated = Replace(imageIndex, replacement);
        return ReadStatus.Done;
    }

    private CbdtTable Replace(int imageIndex, CbdtImage replacement)
    {
        var images = (CbdtImage[])_images.Clone();
        images[imageIndex] = replacement;
        return new(MajorVersion, MinorVersion, images, Location, ConsumedLength);
    }

    private ReadStatus UnsupportedEdit(int imageIndex, ushort format, out FontDiagnostic diagnostic) =>
        ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
            Location.AtLocal(DataField.Payload, 0, imageIndex), out diagnostic, format, 0UL);

    private ReadStatus RequireImageDomain(int imageIndex, out FontDiagnostic diagnostic)
    {
        if (imageIndex < 0 || (uint)imageIndex >= (uint)_images.Length)
            return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                Location.AtLocal(DataField.Count, 0, imageIndex), out diagnostic,
                unchecked((ulong)(long)imageIndex), (ulong)_images.Length);
        diagnostic = default;
        return ReadStatus.Done;
    }
}
