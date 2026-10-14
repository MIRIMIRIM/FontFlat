using FontWeave.Core;

namespace FontWeave.Tables;

public readonly ref partial struct HdmxView
{
    public ReadStatus TryGetDeviceRecordHeader(LegacyMetricsContext context, int ordinal,
        out DeviceRecordHeaderData record, out FontDiagnostic diagnostic)
    {
        record = default;
        ReadStatus status = TryGetRecordPosition(context, ordinal, out ulong position, out diagnostic);
        return status == ReadStatus.Done
            ? TryReadDeviceRecordHeaderAt(position, ordinal, out record, out diagnostic) : status;
    }

    public ReadStatus TryGetWidth(LegacyMetricsContext context, int recordOrdinal, int glyphId,
        out byte width, out FontDiagnostic diagnostic)
    {
        width = default;
        ReadStatus status = TryGetRecordPosition(context, recordOrdinal, out ulong position, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (glyphId < 0 || glyphId >= context.NumGlyphs)
            return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                Location.At(DataField.GlyphId, position + HdmxLayout.RecordHeaderLength, glyphId),
                out diagnostic, unchecked((ulong)(long)glyphId), context.NumGlyphs);
        status = TryReadDeviceByteAt(position + HdmxLayout.RecordHeaderLength + (uint)glyphId,
            glyphId, out var value, out diagnostic);
        if (status == ReadStatus.Done) width = value.Value;
        return status;
    }

    /// <summary>Checks stored metrics and zero padding, not rasterizer or head/outline applicability.</summary>
    public ReadStatus Validate(LegacyMetricsContext context, out FontDiagnostic diagnostic,
        BuildLimits? limits = null)
    {
        var budget = new BuildBudget(limits);
        ReadStatus status = CheckLayout(context, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (!budget.Input(_data.Length, Location, out diagnostic) ||
            !budget.Work(_data.Length, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        return ValidateRecords(context, true, out diagnostic);
    }

    /// <summary>Accepts nonzero, semantically unused record padding; compilation writes zero padding.</summary>
    public ReadStatus TryRead(LegacyMetricsContext context, out HdmxTable? model,
        out FontDiagnostic diagnostic, BuildLimits? limits = null) =>
        TryRead(context, out model, out diagnostic, new BuildBudget(limits));

    internal ReadStatus TryRead(LegacyMetricsContext context, out HdmxTable? model,
        out FontDiagnostic diagnostic, BuildBudget budget)
    {
        model = null;
        ReadStatus status = CheckLayout(context, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (!budget.Input(_data.Length, Location, out diagnostic) ||
            !budget.Work(2L * _data.Length, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        status = ValidateRecords(context, false, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (!budget.Buffer((long)NumRecords * (context.NumGlyphs + IntPtr.Size + 48L),
                Location, out diagnostic)) return ReadStatus.ResourceLimit;
        var records = new HdmxDeviceRecord[NumRecords];
        for (int i = 0; i < records.Length; i++)
        {
            ulong position = RecordPosition(i);
            _ = TryReadDeviceRecordHeaderAt(position, i, out var header, out _);
            var widths = new byte[context.NumGlyphs];
            for (int gid = 0; gid < widths.Length; gid++)
            {
                _ = TryReadDeviceByteAt(position + HdmxLayout.RecordHeaderLength + (uint)gid,
                    gid, out var value, out _);
                widths[gid] = value.Value;
            }
            records[i] = new(header.PixelSize, header.MaxWidth, widths);
        }
        model = new(context.NumGlyphs, SizeDeviceRecord, records, Location, context.MaxpLocation);
        diagnostic = default;
        return ReadStatus.Done;
    }

    private ReadStatus CheckLayout(LegacyMetricsContext context, out FontDiagnostic diagnostic)
    {
        ReadStatus status = CheckContextAndStride(context, out diagnostic);
        if (status != ReadStatus.Done) return status;
        ulong expected = HdmxLayout.HeaderLength + (ulong)NumRecords * SizeDeviceRecord;
        if ((ulong)_data.Length < expected)
            return ReadWire.Fail(ReadStatus.Truncated, DiagnosticCode.TruncatedData,
                Location.At(DataField.Length, 4), out diagnostic, (ulong)_data.Length, expected);
        if ((ulong)_data.Length > expected)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.UnexpectedTrailingData,
                Location.At(DataField.Length, expected), out diagnostic, (ulong)_data.Length, expected);
        diagnostic = default;
        return ReadStatus.Done;
    }

    private ReadStatus CheckContextAndStride(LegacyMetricsContext context, out FontDiagnostic diagnostic)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!IsBound)
            return ReadWire.Fail(ReadStatus.Uninitialized, DiagnosticCode.UnboundView, Location, out diagnostic);
        if (Location != context.Location)
            return ReadWire.Fail(ReadStatus.InvalidSource, DiagnosticCode.WrongSourceFace, Location, out diagnostic);
        ReadStatus status = TryRequireKnownVersion(out _, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (SizeDeviceRecord < context.NumGlyphs + HdmxLayout.RecordHeaderLength || (SizeDeviceRecord & 3) != 0)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.At(DataField.Length, 4), out diagnostic, SizeDeviceRecord,
                (ulong)HdmxLayout.CanonicalStride(context.NumGlyphs));
        diagnostic = default;
        return ReadStatus.Done;
    }

    private ReadStatus TryGetRecordPosition(LegacyMetricsContext context, int ordinal,
        out ulong position, out FontDiagnostic diagnostic)
    {
        position = 0;
        ReadStatus status = CheckContextAndStride(context, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (ordinal < 0 || ordinal >= NumRecords)
            return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                Location.At(DataField.Count, 2, ordinal), out diagnostic,
                unchecked((ulong)(long)ordinal), NumRecords);
        position = RecordPosition(ordinal);
        return ReadStatus.Done;
    }

    private ulong RecordPosition(int ordinal) => HdmxLayout.HeaderLength + (ulong)ordinal * SizeDeviceRecord;

    private ReadStatus ValidateRecords(LegacyMetricsContext context, bool strictPadding,
        out FontDiagnostic diagnostic)
    {
        int previousPixelSize = -1;
        for (int i = 0; i < NumRecords; i++)
        {
            ulong position = RecordPosition(i);
            _ = TryReadDeviceRecordHeaderAt(position, i, out var header, out _);
            if (header.PixelSize <= previousPixelSize)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    Location.At(DataField.Payload, position, i), out diagnostic,
                    header.PixelSize, (ulong)previousPixelSize);
            previousPixelSize = header.PixelSize;
            byte maximum = 0;
            for (int gid = 0; gid < context.NumGlyphs; gid++)
            {
                _ = TryReadDeviceByteAt(position + HdmxLayout.RecordHeaderLength + (uint)gid,
                    gid, out var value, out _);
                maximum = Math.Max(maximum, value.Value);
            }
            if (header.MaxWidth != maximum)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    Location.At(DataField.Payload, position + 1, i), out diagnostic, header.MaxWidth, maximum);
            if (strictPadding)
                for (ulong offset = (ulong)context.NumGlyphs + HdmxLayout.RecordHeaderLength;
                     offset < SizeDeviceRecord; offset++)
                {
                    _ = TryReadDeviceByteAt(position + offset, i, out var value, out _);
                    if (value.Value != 0)
                        return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                            Location.At(DataField.Payload, position + offset, i), out diagnostic, value.Value);
                }
        }
        diagnostic = default;
        return ReadStatus.Done;
    }
}
