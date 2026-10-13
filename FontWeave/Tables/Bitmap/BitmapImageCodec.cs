using System.Buffers.Binary;
using FontWeave.Core;

namespace FontWeave.Tables;

/// <summary>Legacy bitmap frame mechanics over caller-provided source bytes.
/// The caller owns table version, depth policy and materialization.</summary>
internal static class BitmapImageCodec
{
    internal readonly ref struct FrameParts(ReadOnlySpan<byte> metrics, ReadOnlySpan<byte> data,
        ReadOnlySpan<byte> components, byte pad, ulong length)
    {
        internal ReadOnlySpan<byte> Metrics { get; } = metrics;
        internal ReadOnlySpan<byte> Data { get; } = data;
        internal ReadOnlySpan<byte> Components { get; } = components;
        internal byte Pad { get; } = pad;
        internal ulong Length { get; } = length;
    }

    internal static int LegacyMetricsLength(ushort imageFormat) => imageFormat switch
    {
        1 or 2 or 8 => EblcLayout.SmallGlyphMetricsLength,
        6 or 7 or 9 => EblcLayout.BigGlyphMetricsLength,
        5 => 0,
        _ => -1,
    };

    internal static long DerivedDataLength(ushort imageFormat, ReadOnlySpan<byte> metrics,
        byte bitDepth) => imageFormat is 1 or 6
            ? metrics[0] * ((metrics[1] * bitDepth + 7L) / 8L)
            : (metrics[0] * metrics[1] * bitDepth + 7L) / 8L;

    internal static int LegacyHeadLength(ushort imageFormat) => imageFormat switch
    {
        8 => EbdtLayout.Format8HeadLength,
        9 => EbdtLayout.Format9HeadLength,
        _ => LegacyMetricsLength(imageFormat),
    };

    internal static void ReadComponent(ReadOnlySpan<byte> source, out ushort glyphId,
        out sbyte xOffset, out sbyte yOffset)
    {
        glyphId = BinaryPrimitives.ReadUInt16BigEndian(source.Slice(0, 2));
        xOffset = unchecked((sbyte)source[2]);
        yOffset = unchecked((sbyte)source[3]);
    }

    internal static ReadStatus AnalyzeLegacyFrame(ReadOnlySpan<byte> source, DataLocation location,
        ushort format, ulong position, ulong slotLength, byte bitDepth, int? numGlyphs,
        out FrameParts frame, out FontDiagnostic diagnostic, BuildBudget budget)
    {
        frame = default;
        int metricsLength = LegacyMetricsLength(format);
        if (metricsLength < 0)
            return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
                location.AtLocal(DataField.Payload, position), out diagnostic, format, 0UL);
        int headLength = format switch
        {
            8 => EbdtLayout.Format8HeadLength,
            9 => EbdtLayout.Format9HeadLength,
            _ => metricsLength,
        };
        if (position > (ulong)source.Length)
            return ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.RangeOutsideSource,
                location.AtLocal(DataField.Offset, position), out diagnostic, position, (ulong)source.Length);
        ulong remaining = (ulong)source.Length - position;
        if ((ulong)headLength > remaining)
            return ReadWire.Fail(ReadStatus.Truncated, DiagnosticCode.TruncatedData,
                location.AtLocal(DataField.Payload, position), out diagnostic, remaining, (ulong)headLength);
        if ((ulong)headLength > slotLength)
            return ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.RangeOutsideSource,
                location.AtLocal(DataField.Length, position), out diagnostic, (ulong)headLength, slotLength);
        ReadOnlySpan<byte> metrics = source.Slice((int)position, metricsLength);
        int componentCount = format is 8 or 9
            ? BinaryPrimitives.ReadUInt16BigEndian(source.Slice((int)position + headLength - 2, 2))
            : 0;
        ulong dataLength = format switch
        {
            5 => slotLength,
            8 or 9 => 0UL,
            _ => (ulong)DerivedDataLength(format, metrics, bitDepth),
        };
        ulong componentLength = (ulong)componentCount * EbdtLayout.EbdtComponentStride;
        ulong frameLength = (ulong)headLength + dataLength + componentLength;
        if (frameLength > slotLength)
            return ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.RangeOutsideSource,
                location.AtLocal(DataField.Length, position), out diagnostic, frameLength, slotLength);
        if (frameLength > remaining)
            return ReadWire.Fail(ReadStatus.Truncated, DiagnosticCode.TruncatedData,
                location.AtLocal(DataField.Payload, position + (ulong)headLength), out diagnostic,
                remaining - (ulong)headLength, dataLength + componentLength);
        if (!budget.Work(1L + componentCount, location, out diagnostic)) return ReadStatus.ResourceLimit;
        ReadOnlySpan<byte> components = source.Slice((int)(position + (ulong)headLength), (int)componentLength);
        if (numGlyphs is { } domain)
        {
            for (int c = 0; c < componentCount; c++)
            {
                ushort gid = BinaryPrimitives.ReadUInt16BigEndian(components.Slice(c * 4, 2));
                if (gid >= domain)
                    return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                        location.AtLocal(DataField.GlyphId, position + (ulong)headLength + (ulong)c * 4UL, c),
                        out diagnostic, gid, (uint)domain);
            }
        }
        byte pad = format == 8 ? source[(int)position + metricsLength] : (byte)0;
        ReadOnlySpan<byte> data = source.Slice((int)(position + (ulong)headLength), (int)dataLength);
        frame = new(metrics, data, components, pad, frameLength);
        diagnostic = default;
        return ReadStatus.Done;
    }

    internal static void WriteComponent(Span<byte> destination, ushort glyphId,
        sbyte xOffset, sbyte yOffset)
    {
        BinaryPrimitives.WriteUInt16BigEndian(destination.Slice(0, 2), glyphId);
        destination[2] = unchecked((byte)xOffset);
        destination[3] = unchecked((byte)yOffset);
    }
}
