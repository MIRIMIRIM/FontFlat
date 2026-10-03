using FontWeave.Core;

namespace FontWeave.Tables.Layout;

public static class CaretValueTableCompiler
{
    public static ReadStatus TryCreate(short coordinate, StaticDeviceTable device, out CaretValueTable? model,
        out FontDiagnostic diagnostic, BuildLimits? limits = null, DataLocation location = default) =>
        TryCreate(coordinate, device, out model, out diagnostic, new BuildBudget(limits), location);

    internal static ReadStatus TryCreate(short coordinate, StaticDeviceTable device, out CaretValueTable? model,
        out FontDiagnostic diagnostic, BuildBudget budget, DataLocation location = default)
    {
        ArgumentNullException.ThrowIfNull(device);
        model = null;
        if (device.Location.SourceId != location.SourceId || device.Location.FaceIndex != location.FaceIndex ||
            location.SourceId != 0 && device.Location.TableTag != location.TableTag)
            return ReadWire.Fail(ReadStatus.InvalidSource, DiagnosticCode.WrongSourceFace,
                location.AtLocal(DataField.None, 0), out diagnostic);
        if (!budget.Work(1, location, out diagnostic)) return ReadStatus.ResourceLimit;
        model = new(coordinate, device, location, CaretValueLayout.Format3Length + StaticDeviceTableCompiler.Measure(device));
        diagnostic = default;
        return ReadStatus.Done;
    }

    public static ReadStatus TryCompile(CaretValueTable model, out byte[]? output,
        out FontDiagnostic diagnostic, BuildLimits? limits = null) =>
        TryCompile(model, out output, out diagnostic, new BuildBudget(limits));

    internal static ReadStatus TryCompile(CaretValueTable model, out byte[]? output,
        out FontDiagnostic diagnostic, BuildBudget budget)
    {
        ArgumentNullException.ThrowIfNull(model);
        output = null;
        int length = Measure(model);
        if (!budget.Work(EmissionWork(model), model.Location, out diagnostic) ||
            !budget.Output(length, model.Location, out diagnostic) ||
            !budget.Buffer(length, model.Location, out diagnostic)) return ReadStatus.ResourceLimit;
        byte[] bytes = new byte[length];
        WritePlanned(model, bytes);
        budget.RecordCompiledTable(length);
        output = bytes;
        diagnostic = default;
        return ReadStatus.Done;
    }

    public static ReadStatus TryWrite(CaretValueTable model, Span<byte> destination,
        out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        int length = Measure(model);
        if (destination.Length < length)
            return ReadWire.Fail(ReadStatus.DestinationTooSmall, DiagnosticCode.DestinationTooSmall,
                model.Location.AtLocal(DataField.OutputBytes, 0), out diagnostic,
                (ulong)destination.Length, (ulong)length);
        var budget = new BuildBudget(limits);
        if (!budget.Work(EmissionWork(model), model.Location, out diagnostic) ||
            !budget.Output(length, model.Location, out diagnostic)) return ReadStatus.ResourceLimit;
        WritePlanned(model, destination);
        diagnostic = default;
        return ReadStatus.Done;
    }

    internal static int Measure(CaretValueTable model) => CaretValueLayout.Format3Length + StaticDeviceTableCompiler.Measure(model.Device);
    internal static long EmissionWork(CaretValueTable model) => CaretValueLayout.Format3Length + StaticDeviceTableCompiler.EmissionWork(model.Device);

    internal static void WritePlanned(CaretValueTable model, Span<byte> destination)
    {
        CoverageTableCompiler.RequireWritten(CaretValueView.TryWriteFormat(destination, 3, out _, model.Location));
        CoverageTableCompiler.RequireWritten(CaretValueView.TryWriteFormat3HeaderAt(destination,
            CaretValueLayout.BodyAt, new CaretValueView.Format3HeaderData(-1, model.Coordinate, CaretValueLayout.Format3Length),
            out _, model.Location));
        StaticDeviceTableCompiler.WritePlanned(model.Device,
            destination.Slice(CaretValueLayout.Format3Length, StaticDeviceTableCompiler.Measure(model.Device)));
    }
}
