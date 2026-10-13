using System.Buffers.Binary;
using FontWeave.Core;

namespace FontWeave.Tables;

/// <summary>Canonical EBDT emission: the version head followed by frames in
/// joint-read order, with metrics, image bits and component records preserved.</summary>
public static class EbdtTableCompiler
{
    internal static long FrameLength(EbdtImage image) => image.ImageFormat switch
    {
        8 => EbdtLayout.Format8HeadLength + 4L * image.Components.Length,
        9 => EbdtLayout.Format9HeadLength + 4L * image.Components.Length,
        _ => EbdtImage.MetricsLength(image.ImageFormat) + image.Data.LongLength,
    };

    internal static long PackedLength(EbdtTable model)
    {
        long length = EbdtLayout.HeaderLength;
        foreach (var image in model.Images) length += FrameLength(image);
        return length;
    }

    public static ReadStatus TryCreate(ushort majorVersion, ushort minorVersion,
        IReadOnlyList<EbdtImage> images, out EbdtTable? model, out FontDiagnostic diagnostic,
        BuildLimits? limits = null, DataLocation location = default)
    {
        model = null;
        ArgumentNullException.ThrowIfNull(images);
        location = location == default ? DataLocation.Anonymous(EbdtLayout.Tag) : location;
        var budget = new BuildBudget(limits);
        ReadStatus status = RequireVersion(majorVersion, minorVersion, location, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = CheckImages(images, location, out long length, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        if (!budget.Buffer(length + images.Count * (long)IntPtr.Size, location, out diagnostic))
            return ReadStatus.ResourceLimit;
        var parts = new EbdtImage[images.Count];
        for (int i = 0; i < images.Count; i++)
        {
            var image = images[i];
            parts[i] = new(image.ImageFormat, image.Metrics?.ToArray(), image.Data.ToArray(),
                image.BitDepth, image.Components.ToArray(), image.CompositePad);
        }
        model = new(majorVersion, minorVersion, parts, location, (int)length);
        diagnostic = default;
        return ReadStatus.Done;
    }

    public static ReadStatus TryWrite(EbdtTable model, Span<byte> destination,
        out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        ReadStatus status = TryCompile(model, out byte[]? bytes, out diagnostic, limits);
        if (status != ReadStatus.Done) return status;
        if (destination.Length < bytes!.Length)
            return ReadWire.Fail(ReadStatus.DestinationTooSmall, DiagnosticCode.DestinationTooSmall,
                model.Location.AtLocal(DataField.OutputBytes, 0), out diagnostic,
                (ulong)destination.Length, (ulong)bytes.Length);
        bytes.AsSpan().CopyTo(destination);
        diagnostic = default;
        return ReadStatus.Done;
    }

    public static ReadStatus TryCompile(EbdtTable model, out byte[]? bytes,
        out FontDiagnostic diagnostic, BuildLimits? limits = null) =>
        Compile(model, out bytes, out diagnostic, new BuildBudget(limits));

    internal static ReadStatus Compile(EbdtTable model, out byte[]? bytes,
        out FontDiagnostic diagnostic, BuildBudget budget)
    {
        bytes = null;
        ArgumentNullException.ThrowIfNull(model);
        ReadStatus status = RequireVersion(model.MajorVersion, model.MinorVersion,
            model.Location, out diagnostic);
        if (status != ReadStatus.Done) return status;
        var images = model.Images;
        status = CheckImages(images, model.Location, out long length, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        if (!budget.Buffer(length, model.Location, out diagnostic) ||
            !budget.Output(length, model.Location, out diagnostic)) return ReadStatus.ResourceLimit;
        var output = new byte[(int)length];
        RequireWritten(EbdtView.TryWriteMajorVersion(output, model.MajorVersion, out _, model.Location));
        RequireWritten(EbdtView.TryWriteMinorVersion(output, model.MinorVersion, out _, model.Location));
        int at = EbdtLayout.HeaderLength;
        foreach (var image in images)
        {
            int metricsLength = EbdtImage.MetricsLength(image.ImageFormat);
            if (image.Metrics is { } metrics) metrics.AsSpan().CopyTo(output.AsSpan(at, metricsLength));
            if (image.ImageFormat is 8 or 9)
            {
                int headLength = image.ImageFormat == 8 ? EbdtLayout.Format8HeadLength : EbdtLayout.Format9HeadLength;
                if (image.ImageFormat == 8) output[at + metricsLength] = image.CompositePad;
                BinaryPrimitives.WriteUInt16BigEndian(output.AsSpan(at + headLength - 2, 2),
                    (ushort)image.Components.Length);
                int componentAt = at + headLength;
                foreach (var component in image.Components)
                {
                    BinaryPrimitives.WriteUInt16BigEndian(output.AsSpan(componentAt, 2), component.GlyphId);
                    output[componentAt + 2] = unchecked((byte)component.XOffset);
                    output[componentAt + 3] = unchecked((byte)component.YOffset);
                    componentAt += EbdtLayout.EbdtComponentStride;
                }
            }
            else
                image.Data.AsSpan().CopyTo(output.AsSpan(at + metricsLength));
            at += (int)FrameLength(image);
        }
        budget.RecordCompiledTable(output.Length);
        bytes = output;
        diagnostic = default;
        return ReadStatus.Done;
    }

    internal static ReadStatus CheckImages(IReadOnlyList<EbdtImage> images, DataLocation location,
        out long length, out FontDiagnostic diagnostic, BuildBudget budget)
    {
        length = EbdtLayout.HeaderLength;
        if (!budget.Work(images.Count, location, out diagnostic)) return ReadStatus.ResourceLimit;
        for (int i = 0; i < images.Count; i++)
        {
            var image = images[i];
            ArgumentNullException.ThrowIfNull(image);
            ReadStatus status = image.CheckShape(location, i, out diagnostic);
            if (status != ReadStatus.Done) return status;
            length += FrameLength(image);
        }
        if (length > int.MaxValue)
            return ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.RangeOutsideSource,
                location.AtLocal(DataField.Length, 0), out diagnostic, (ulong)length, int.MaxValue);
        if (!budget.Work(length, location, out diagnostic)) return ReadStatus.ResourceLimit;
        diagnostic = default;
        return ReadStatus.Done;
    }

    internal static ReadStatus RequireVersion(ushort majorVersion, ushort minorVersion,
        DataLocation location, out FontDiagnostic diagnostic)
    {
        if (majorVersion != EbdtLayout.MajorVersion2)
            return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedVersion,
                location.AtLocal(DataField.Version, 0), out diagnostic, majorVersion, EbdtLayout.MajorVersion2);
        if (minorVersion != 0)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                location.AtLocal(DataField.Payload, 2), out diagnostic, minorVersion, 0UL);
        diagnostic = default;
        return ReadStatus.Done;
    }

    private static void RequireWritten(ReadStatus status)
    {
        if (status != ReadStatus.Done)
            throw new InvalidOperationException("A preflighted EBDT write failed.");
    }
}
