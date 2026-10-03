using FontWeave.Core;

namespace FontWeave.Tables;

public static class HheaTableCompiler
{
    public static ReadStatus TryCompile(HheaTable model, out byte[]? output,
        out FontDiagnostic diagnostic, BuildLimits? limits = null) =>
        TryCompile(model, out output, out diagnostic, new BuildBudget(limits));

    internal static ReadStatus TryCompile(HheaTable model, out byte[]? output,
        out FontDiagnostic diagnostic, BuildBudget budget)
    {
        ArgumentNullException.ThrowIfNull(model);
        output = null;
        if (!budget.Work(HheaLayout.Length, model.Location, out diagnostic) ||
            !budget.Output(HheaLayout.Length, model.Location, out diagnostic) ||
            !budget.Buffer(HheaLayout.Length, model.Location, out diagnostic)) return ReadStatus.ResourceLimit;
        byte[] bytes = new byte[HheaLayout.Length];
        ReadStatus status = Write(model.Values, bytes, out diagnostic, model.Location);
        if (status != ReadStatus.Done) return status;
        budget.RecordCompiledTable(bytes.Length);
        output = bytes;
        diagnostic = default;
        return ReadStatus.Done;
    }

    public static ReadStatus TryWrite(HheaTable model, Span<byte> destination,
        out FontDiagnostic diagnostic)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (destination.Length < HheaLayout.Length)
            return ReadWire.Fail(ReadStatus.DestinationTooSmall, DiagnosticCode.DestinationTooSmall,
                model.Location.At(DataField.OutputBytes, 0), out diagnostic,
                (ulong)destination.Length, HheaLayout.Length);
        return Write(model.Values, destination, out diagnostic, model.Location);
    }

    private static ReadStatus Write(HheaValues v, Span<byte> bytes,
        out FontDiagnostic diagnostic, DataLocation location)
    {
        ReadStatus status = HheaView.TryWriteMajorVersion(bytes, v.MajorVersion, out diagnostic, location);
        if (status != ReadStatus.Done) return status;
        status = HheaView.TryWriteMinorVersion(bytes, v.MinorVersion, out diagnostic, location);
        if (status != ReadStatus.Done) return status;
        status = HheaView.TryWriteAscender(bytes, v.Ascender, out diagnostic, location);
        if (status != ReadStatus.Done) return status;
        status = HheaView.TryWriteDescender(bytes, v.Descender, out diagnostic, location);
        if (status != ReadStatus.Done) return status;
        status = HheaView.TryWriteLineGap(bytes, v.LineGap, out diagnostic, location);
        if (status != ReadStatus.Done) return status;
        status = HheaView.TryWriteAdvanceWidthMax(bytes, v.AdvanceWidthMax, out diagnostic, location);
        if (status != ReadStatus.Done) return status;
        status = HheaView.TryWriteMinLeftSideBearing(bytes, v.MinLeftSideBearing, out diagnostic, location);
        if (status != ReadStatus.Done) return status;
        status = HheaView.TryWriteMinRightSideBearing(bytes, v.MinRightSideBearing, out diagnostic, location);
        if (status != ReadStatus.Done) return status;
        status = HheaView.TryWriteXMaxExtent(bytes, v.XMaxExtent, out diagnostic, location);
        if (status != ReadStatus.Done) return status;
        status = HheaView.TryWriteCaretSlopeRise(bytes, v.CaretSlopeRise, out diagnostic, location);
        if (status != ReadStatus.Done) return status;
        status = HheaView.TryWriteCaretSlopeRun(bytes, v.CaretSlopeRun, out diagnostic, location);
        if (status != ReadStatus.Done) return status;
        status = HheaView.TryWriteCaretOffset(bytes, v.CaretOffset, out diagnostic, location);
        if (status != ReadStatus.Done) return status;
        status = HheaView.TryWriteReserved1(bytes, v.Reserved1, out diagnostic, location);
        if (status != ReadStatus.Done) return status;
        status = HheaView.TryWriteReserved2(bytes, v.Reserved2, out diagnostic, location);
        if (status != ReadStatus.Done) return status;
        status = HheaView.TryWriteReserved3(bytes, v.Reserved3, out diagnostic, location);
        if (status != ReadStatus.Done) return status;
        status = HheaView.TryWriteReserved4(bytes, v.Reserved4, out diagnostic, location);
        if (status != ReadStatus.Done) return status;
        status = HheaView.TryWriteMetricDataFormat(bytes, v.MetricDataFormat, out diagnostic, location);
        if (status != ReadStatus.Done) return status;
        status = HheaView.TryWriteNumberOfHMetrics(bytes, v.NumberOfHMetrics, out diagnostic, location);
        if (status != ReadStatus.Done) return status;
        return ReadStatus.Done;
    }
}
