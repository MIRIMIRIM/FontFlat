using FontWeave.Core;

namespace FontWeave.Tables;

public static class VheaTableCompiler
{
    public static ReadStatus TryCompile(VheaTable model, out byte[]? output,
        out FontDiagnostic diagnostic, BuildLimits? limits = null) =>
        TryCompile(model, out output, out diagnostic, new BuildBudget(limits));

    internal static ReadStatus TryCompile(VheaTable model, out byte[]? output,
        out FontDiagnostic diagnostic, BuildBudget budget)
    {
        ArgumentNullException.ThrowIfNull(model);
        output = null;
        if (!budget.Work(VheaLayout.Length, model.Location, out diagnostic) ||
            !budget.Output(VheaLayout.Length, model.Location, out diagnostic) ||
            !budget.Buffer(VheaLayout.Length, model.Location, out diagnostic)) return ReadStatus.ResourceLimit;
        byte[] bytes = new byte[VheaLayout.Length];
        ReadStatus status = Write(model.Values, bytes, out diagnostic, model.Location);
        if (status != ReadStatus.Done) return status;
        budget.RecordCompiledTable(bytes.Length);
        output = bytes;
        diagnostic = default;
        return ReadStatus.Done;
    }

    public static ReadStatus TryWrite(VheaTable model, Span<byte> destination,
        out FontDiagnostic diagnostic)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (destination.Length < VheaLayout.Length)
            return ReadWire.Fail(ReadStatus.DestinationTooSmall, DiagnosticCode.DestinationTooSmall,
                model.Location.At(DataField.OutputBytes, 0), out diagnostic,
                (ulong)destination.Length, VheaLayout.Length);
        return Write(model.Values, destination, out diagnostic, model.Location);
    }

    private static ReadStatus Write(VheaValues v, Span<byte> bytes,
        out FontDiagnostic diagnostic, DataLocation location)
    {
        ReadStatus status = VheaView.TryWriteVersionRaw(bytes, v.VersionRaw, out diagnostic, location);
        if (status != ReadStatus.Done) return status;
        status = VheaView.TryWriteAscender(bytes, v.Ascender, out diagnostic, location);
        if (status != ReadStatus.Done) return status;
        status = VheaView.TryWriteDescender(bytes, v.Descender, out diagnostic, location);
        if (status != ReadStatus.Done) return status;
        status = VheaView.TryWriteLineGap(bytes, v.LineGap, out diagnostic, location);
        if (status != ReadStatus.Done) return status;
        status = VheaView.TryWriteAdvanceHeightMax(bytes, v.AdvanceHeightMax, out diagnostic, location);
        if (status != ReadStatus.Done) return status;
        status = VheaView.TryWriteMinTopSideBearing(bytes, v.MinTopSideBearing, out diagnostic, location);
        if (status != ReadStatus.Done) return status;
        status = VheaView.TryWriteMinBottomSideBearing(bytes, v.MinBottomSideBearing, out diagnostic, location);
        if (status != ReadStatus.Done) return status;
        status = VheaView.TryWriteYMaxExtent(bytes, v.YMaxExtent, out diagnostic, location);
        if (status != ReadStatus.Done) return status;
        status = VheaView.TryWriteCaretSlopeRise(bytes, v.CaretSlopeRise, out diagnostic, location);
        if (status != ReadStatus.Done) return status;
        status = VheaView.TryWriteCaretSlopeRun(bytes, v.CaretSlopeRun, out diagnostic, location);
        if (status != ReadStatus.Done) return status;
        status = VheaView.TryWriteCaretOffset(bytes, v.CaretOffset, out diagnostic, location);
        if (status != ReadStatus.Done) return status;
        status = VheaView.TryWriteReserved1(bytes, v.Reserved1, out diagnostic, location);
        if (status != ReadStatus.Done) return status;
        status = VheaView.TryWriteReserved2(bytes, v.Reserved2, out diagnostic, location);
        if (status != ReadStatus.Done) return status;
        status = VheaView.TryWriteReserved3(bytes, v.Reserved3, out diagnostic, location);
        if (status != ReadStatus.Done) return status;
        status = VheaView.TryWriteReserved4(bytes, v.Reserved4, out diagnostic, location);
        if (status != ReadStatus.Done) return status;
        status = VheaView.TryWriteMetricDataFormat(bytes, v.MetricDataFormat, out diagnostic, location);
        if (status != ReadStatus.Done) return status;
        status = VheaView.TryWriteNumOfLongVerMetrics(bytes, v.NumOfLongVerMetrics, out diagnostic, location);
        if (status != ReadStatus.Done) return status;
        return ReadStatus.Done;
    }
}
