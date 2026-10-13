using FontWeave.Core;

namespace FontWeave.Tables;

/// <summary>Canonical CBDT compiler (card T6-05 §4, OQ-T6-05-13's single
/// candidate). One planned layout: header(4) → the images packed in joint
/// read-chain order, each = the raw metrics head (5 bytes on 17, 8 on 18,
/// none on 19) + the u32 dataLen + the verbatim payload ("byte alignment is
/// sufficient", D L50 - no pool padding). Payload bytes are never decoded,
/// recompressed or reordered; the frames' lengths are the compiler's own
/// derivation from the owned parts. The version words re-answer strict {3}
/// with minor 0; everything structural about the pool arrives through the
/// CBLC index, so this compiler has no offset surface of its own - the
/// imageDataOffset/sbitOffset fixups are CblcTableCompiler's face, derived
/// from the same chain walk over the same image sequence (the two compilers'
/// coordination contract: the k-th populated slot of the CBLC chain pairs
/// with the k-th image, and both sides derive identical positions).</summary>
public static class CbdtTableCompiler
{
    /// <summary>One image's canonical frame length: the metrics head plus the
    /// counted length word plus the payload.</summary>
    private static int MetricsLength(ushort imageFormat) => imageFormat switch
        {
            CblcLayout.ImageFormat17 => CbdtLayout.SmallGlyphMetricsLength,
            CblcLayout.ImageFormat18 => CblcLayout.BigGlyphMetricsLength,
            CblcLayout.ImageFormat19 => 0,
            _ => -1,
        };

    internal static long FrameLength(CbdtImage image)
    {
        int metricsLength = MetricsLength(image.ImageFormat);
        return metricsLength < 0 ? -1 : metricsLength + 4L + image.Payload.LongLength;
    }

    private static ReadStatus CheckImage(CbdtImage image, DataLocation location,
        int ordinal, out FontDiagnostic diagnostic)
    {
        int metricsLength = MetricsLength(image.ImageFormat);
        if (metricsLength < 0)
            return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
                location.AtLocal(DataField.Payload, 0, ordinal), out diagnostic,
                image.ImageFormat, 0UL);
        if ((image.Metrics?.Length ?? 0) != metricsLength ||
            metricsLength == 0 && image.Metrics is not null)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                location.AtLocal(DataField.Length, 0, ordinal), out diagnostic,
                (ulong)(image.Metrics?.Length ?? 0), (ulong)metricsLength);
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>The canonical pool's total byte length: the 4-byte head plus
    /// every frame packed in order.</summary>
    internal static long PackedLength(CbdtTable model)
    {
        long length = CbdtLayout.HeaderLength;
        foreach (var image in model.Images)
            length += FrameLength(image);
        return length;
    }

    /// <summary>Explicit root-model factory from owned parts (card §4.1): the
    /// root re-answers the strict version domain; the images travel as given
    /// (their frame shapes are re-answered at compile time). Factory models
    /// carry the canonical plan's own coordinates (the COLR factory line).</summary>
    public static ReadStatus TryCreate(ushort majorVersion, ushort minorVersion,
        IReadOnlyList<CbdtImage> images, out CbdtTable? model, out FontDiagnostic diagnostic,
        BuildLimits? limits = null, DataLocation location = default)
    {
        model = null;
        ArgumentNullException.ThrowIfNull(images);
        location = location == default ? DataLocation.Anonymous(CbdtLayout.Tag) : location;
        var budget = new BuildBudget(limits);
        ReadStatus status = RequireVersion(majorVersion, minorVersion, location,
            out diagnostic);
        if (status != ReadStatus.Done) return status;
        int imageCount = images.Count;
        if (!budget.Work(imageCount, location, out diagnostic) ||
            !budget.Buffer(imageCount * (long)IntPtr.Size, location, out diagnostic))
            return ReadStatus.ResourceLimit;
        var parts = new CbdtImage[imageCount];
        long buffer = 0L;
        for (int i = 0; i < imageCount; i++)
        {
            var image = images[i];
            ArgumentNullException.ThrowIfNull(image);
            status = CheckImage(image, location, i, out diagnostic);
            if (status != ReadStatus.Done) return status;
            parts[i] = image;
            buffer += FrameLength(image);
        }
        if (!budget.Work(buffer, location, out diagnostic) ||
            !budget.Buffer(buffer, location, out diagnostic)) return ReadStatus.ResourceLimit;
        if (CbdtLayout.HeaderLength + buffer > int.MaxValue)
            return ReadWire.Fail(ReadStatus.ResourceLimit, DiagnosticCode.BudgetExceeded,
                location.AtLocal(DataField.OutputBytes, 0), out diagnostic,
                (ulong)(CbdtLayout.HeaderLength + buffer), int.MaxValue);
        model = new(majorVersion, minorVersion, parts, location,
            (int)(CbdtLayout.HeaderLength + buffer));
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>Deterministic write through the same planned emission; every
    /// budget admission completes before any byte lands, and a short
    /// destination leaves the span untouched (the T5-05 atomicity
    /// discipline).</summary>
    public static ReadStatus TryWrite(CbdtTable model, Span<byte> destination,
        out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        ReadStatus status = TryCompile(model, out byte[]? encoded, out diagnostic, limits);
        if (status != ReadStatus.Done) return status;
        if (destination.Length < encoded!.Length)
            return ReadWire.Fail(ReadStatus.DestinationTooSmall, DiagnosticCode.DestinationTooSmall,
                model.Location.At(DataField.OutputBytes, 0), out diagnostic,
                (ulong)destination.Length, (uint)encoded.Length);
        encoded.AsSpan().CopyTo(destination);
        diagnostic = default;
        return ReadStatus.Done;
    }

    public static ReadStatus TryCompile(CbdtTable model, out byte[]? bytes,
        out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        bytes = null;
        ArgumentNullException.ThrowIfNull(model);
        var budget = new BuildBudget(limits);
        // The compile-time re-answers (a hand-built model cannot smuggle a
        // refused shape past the emission).
        ReadStatus status = RequireVersion(model.MajorVersion, model.MinorVersion,
            model.Location, out diagnostic);
        if (status != ReadStatus.Done) return status;
        for (int i = 0; i < model.ImageCount; i++)
        {
            status = CheckImage(model.Images[i], model.Location, i, out diagnostic);
            if (status != ReadStatus.Done) return status;
        }
        long length = PackedLength(model);
        if (!budget.Work(length, model.Location, out diagnostic) ||
            !budget.Buffer(length, model.Location, out diagnostic) ||
            !budget.Output(length, model.Location, out diagnostic))
            return ReadStatus.ResourceLimit;
        var output = new byte[length];
        RequireWritten(CbdtView.TryWriteMajorVersion(output, model.MajorVersion,
            out _, model.Location));
        RequireWritten(CbdtView.TryWriteMinorVersion(output, model.MinorVersion,
            out _, model.Location));
        int at = CbdtLayout.HeaderLength;
        foreach (var image in model.Images)
        {
            int metricsLength = image.ImageFormat switch
            {
                CblcLayout.ImageFormat17 => CbdtLayout.SmallGlyphMetricsLength,
                CblcLayout.ImageFormat18 => CblcLayout.BigGlyphMetricsLength,
                _ => 0,
            };
            if (image.Metrics is { } metrics)
                metrics.AsSpan(0, metricsLength).CopyTo(output.AsSpan(at));
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(
                output.AsSpan(at + metricsLength), (uint)image.Payload.Length);
            image.Payload.AsSpan().CopyTo(output.AsSpan(at + metricsLength + 4));
            at += metricsLength + 4 + image.Payload.Length;
        }
        budget.RecordCompiledTable(output.Length);
        bytes = output;
        diagnostic = default;
        return ReadStatus.Done;
    }

    private static ReadStatus RequireVersion(ushort majorVersion, ushort minorVersion,
        DataLocation location, out FontDiagnostic diagnostic)
    {
        if (majorVersion != CbdtLayout.MajorVersion3)
            return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedVersion,
                location.AtLocal(DataField.Version, 0), out diagnostic, majorVersion,
                CbdtLayout.MajorVersion3);
        if (minorVersion != 0)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                location.AtLocal(DataField.Payload, 2), out diagnostic, minorVersion, 0UL);
        diagnostic = default;
        return ReadStatus.Done;
    }

    private static void RequireWritten(ReadStatus status)
    {
        if (status != ReadStatus.Done)
            throw new InvalidOperationException("A preflighted CBDT write failed.");
    }
}
