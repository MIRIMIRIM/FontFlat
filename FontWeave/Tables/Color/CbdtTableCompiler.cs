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
    internal static int MetricsLength(ushort imageFormat) => imageFormat switch
        {
            CblcLayout.ImageFormat17 => CbdtLayout.SmallGlyphMetricsLength,
            CblcLayout.ImageFormat18 => CblcLayout.BigGlyphMetricsLength,
            CblcLayout.ImageFormat19 => 0,
            _ => BitmapImageCodec.LegacyMetricsLength(imageFormat),
        };

    internal static long FrameLength(CbdtImage image)
    {
        int metricsLength = MetricsLength(image.ImageFormat);
        if (metricsLength < 0) return -1;
        return image.ImageFormat switch
        {
            8 or 9 => BitmapImageCodec.LegacyHeadLength(image.ImageFormat) + 4L * image.Components.Length,
            _ => metricsLength + (CblcLayout.IsImageFormatPng(image.ImageFormat) ? 4L : 0L) + image.Payload.LongLength,
        };
    }

    internal static ReadStatus CheckImage(CbdtImage image, DataLocation location,
        int ordinal, out FontDiagnostic diagnostic)
    {
        int metricsLength = MetricsLength(image.ImageFormat);
        if (metricsLength < 0)
            return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
                location.AtLocal(DataField.Payload, 0, ordinal), out diagnostic,
                image.ImageFormat, 0UL);
        if (!CblcLayout.IsBitDepthDefined(image.BitDepth))
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                location.AtLocal(DataField.Payload, 0, ordinal), out diagnostic,
                image.BitDepth, 0UL);
        if ((image.Metrics?.Length ?? 0) != metricsLength ||
            metricsLength == 0 && image.Metrics is not null)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                location.AtLocal(DataField.Length, 0, ordinal), out diagnostic,
                (ulong)(image.Metrics?.Length ?? 0), (ulong)metricsLength);
        if (image.ImageFormat is 8 or 9)
        {
            if (image.Payload.Length != 0)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    location.AtLocal(DataField.Length, 0, ordinal), out diagnostic,
                    (ulong)image.Payload.Length, 0UL);
            if (image.Components.Length > ushort.MaxValue)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    location.AtLocal(DataField.Count, 0, ordinal), out diagnostic,
                    (ulong)image.Components.Length, ushort.MaxValue);
        }
        else
        {
            if (image.Components.Length != 0)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    location.AtLocal(DataField.Count, 0, ordinal), out diagnostic,
                    (ulong)image.Components.Length, 0UL);
            if (CblcLayout.IsImageFormatInherited(image.ImageFormat) && image.ImageFormat != 5)
            {
                long expected = BitmapImageCodec.DerivedDataLength(image.ImageFormat, image.Metrics, image.BitDepth);
                if (image.Payload.LongLength != expected)
                    return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                        location.AtLocal(DataField.Length, 0, ordinal), out diagnostic,
                        (ulong)image.Payload.LongLength, (ulong)expected);
            }
        }
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
            long charge = (image.Metrics?.LongLength ?? 0) + image.Payload.LongLength + 4L * image.Components.Length;
            if (!budget.Work(charge, location, out diagnostic) ||
                !budget.Buffer(charge, location, out diagnostic)) return ReadStatus.ResourceLimit;
            parts[i] = new CbdtImage(image.ImageFormat, image.Metrics?.ToArray(), image.Payload.ToArray(),
                image.BitDepth, image.Components.ToArray(), image.CompositePad) { SourcePosition = image.SourcePosition };
            buffer += FrameLength(image);
        }
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
                model.Location.AtLocal(DataField.OutputBytes, 0), out diagnostic,
                (ulong)destination.Length, (uint)encoded.Length);
        encoded.AsSpan().CopyTo(destination);
        diagnostic = default;
        return ReadStatus.Done;
    }

    public static ReadStatus TryCompile(CbdtTable model, out byte[]? bytes,
        out FontDiagnostic diagnostic, BuildLimits? limits = null)
        => Compile(model, out bytes, out diagnostic, new BuildBudget(limits));

    internal static ReadStatus Compile(CbdtTable model, out byte[]? bytes,
        out FontDiagnostic diagnostic, BuildBudget budget)
    {
        bytes = null;
        ArgumentNullException.ThrowIfNull(model);
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
        // The emitted head is always (3, 0): the version word is the EBLC
        // generational alias with no known structural difference, so the write
        // face normalizes a 2.0-sourced model (the T6-08 head-promotion
        // precedent; 2026-10-18 version-gate briefing lean, user-approved).
        RequireWritten(CbdtView.TryWriteMajorVersion(output, CbdtLayout.MajorVersion3,
            out _, model.Location));
        RequireWritten(CbdtView.TryWriteMinorVersion(output, 0,
            out _, model.Location));
        int at = CbdtLayout.HeaderLength;
        foreach (var image in model.Images)
        {
            int metricsLength = MetricsLength(image.ImageFormat);
            if (image.Metrics is { } metrics)
                metrics.AsSpan(0, metricsLength).CopyTo(output.AsSpan(at));
            if (CblcLayout.IsImageFormatPng(image.ImageFormat))
            {
                System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(
                    output.AsSpan(at + metricsLength), (uint)image.Payload.Length);
                image.Payload.AsSpan().CopyTo(output.AsSpan(at + metricsLength + 4));
            }
            else if (image.ImageFormat is 8 or 9)
            {
                int headLength = BitmapImageCodec.LegacyHeadLength(image.ImageFormat);
                if (image.ImageFormat == 8) output[at + metricsLength] = image.CompositePad;
                System.Buffers.Binary.BinaryPrimitives.WriteUInt16BigEndian(
                    output.AsSpan(at + headLength - 2, 2), (ushort)image.Components.Length);
                int componentAt = at + headLength;
                foreach (var component in image.Components)
                {
                    BitmapImageCodec.WriteComponent(output.AsSpan(componentAt, 4), component.GlyphId,
                        component.XOffset, component.YOffset);
                    componentAt += 4;
                }
            }
            else image.Payload.AsSpan().CopyTo(output.AsSpan(at + metricsLength));
            at += (int)FrameLength(image);
        }
        budget.RecordCompiledTable(output.Length);
        bytes = output;
        diagnostic = default;
        return ReadStatus.Done;
    }

    internal static ReadStatus CheckImages(IReadOnlyList<CbdtImage> images, DataLocation location,
        BuildBudget budget, out FontDiagnostic diagnostic)
    {
        int count = images.Count;
        if (!budget.Work(count, location, out diagnostic)) return ReadStatus.ResourceLimit;
        for (int i = 0; i < count; i++)
        {
            ReadStatus status = CheckImage(images[i], location, i, out diagnostic);
            if (status != ReadStatus.Done) return status;
        }
        diagnostic = default;
        return ReadStatus.Done;
    }

    internal static ReadStatus CheckCycles(IReadOnlyList<CbdtImage> images, int firstImage,
        ReadOnlySpan<ushort> glyphIds, DataLocation location, BuildBudget budget,
        out FontDiagnostic diagnostic)
    {
        int count = glyphIds.Length;
        if (!budget.Work(count, location, out diagnostic)) return ReadStatus.ResourceLimit;
        bool composite = false;
        for (int i = 0; i < count; i++) composite |= images[firstImage + i].ImageFormat is 8 or 9;
        if (!composite)
        {
            diagnostic = default;
            return ReadStatus.Done;
        }
        if (!budget.Buffer(9L * count, location, out diagnostic)) return ReadStatus.ResourceLimit;
        var states = new byte[count];
        var nodes = new int[count];
        var nextEdges = new int[count];
        for (int root = 0; root < count; root++)
        {
            if (!budget.Work(1, location, out diagnostic)) return ReadStatus.ResourceLimit;
            if (states[root] != 0) continue;
            int top = 0;
            nodes[top] = root;
            nextEdges[top] = 0;
            states[root] = 1;
            while (top >= 0)
            {
                int node = nodes[top];
                var image = images[firstImage + node];
                int edge = nextEdges[top];
                if (edge == image.Components.Length)
                {
                    states[node] = 2;
                    top--;
                    continue;
                }
                if (!budget.Work(1, image.ComponentLocation(location, edge), out diagnostic))
                    return ReadStatus.ResourceLimit;
                nextEdges[top]++;
                ushort target = image.Components[edge].GlyphId;
                int low = 0, high = count - 1, found = -1;
                while (low <= high)
                {
                    if (!budget.Work(1, location, out diagnostic)) return ReadStatus.ResourceLimit;
                    int middle = low + (high - low) / 2;
                    if (target < glyphIds[middle]) high = middle - 1;
                    else if (target > glyphIds[middle]) low = middle + 1;
                    else { found = middle; break; }
                }
                if (found < 0 || states[found] == 2) continue;
                if (states[found] == 1)
                    return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                        image.ComponentLocation(location, edge), out diagnostic, target, 0UL);
                states[found] = 1;
                nodes[++top] = found;
                nextEdges[top] = 0;
            }
        }
        diagnostic = default;
        return ReadStatus.Done;
    }

    internal static ReadStatus RequireVersion(ushort majorVersion, ushort minorVersion,
        DataLocation location, out FontDiagnostic diagnostic)
    {
        // The model domain is the read face {2, 3} — a 2.0-sourced model
        // compiles and the write sites normalize the emitted head to (3, 0)
        // (the 2026-10-18 version-gate briefing lean, user-approved).
        if (majorVersion is not (CbdtLayout.MajorVersion2 or CbdtLayout.MajorVersion3))
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
