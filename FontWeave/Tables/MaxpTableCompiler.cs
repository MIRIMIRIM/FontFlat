using FontWeave.Core;

namespace FontWeave.Tables;

public static class MaxpTableCompiler
{
    public static ReadStatus TryCompile(MaxpTable model, out byte[]? output,
        out FontDiagnostic diagnostic, BuildLimits? limits = null) =>
        TryCompile(model, out output, out diagnostic, new BuildBudget(limits));

    internal static ReadStatus TryCompile(MaxpTable model, out byte[]? output,
        out FontDiagnostic diagnostic, BuildBudget budget)
    {
        ArgumentNullException.ThrowIfNull(model);
        output = null;
        int length = model is Maxp05Table ? MaxpLayout.Length05 : MaxpLayout.Length10;
        if (!budget.Work(length, model.Location, out diagnostic) ||
            !budget.Output(length, model.Location, out diagnostic) ||
            !budget.Buffer(length, model.Location, out diagnostic))
            return ReadStatus.ResourceLimit;
        byte[] bytes = new byte[length];
        ReadStatus status = Write(model, bytes, out diagnostic);
        if (status != ReadStatus.Done) return status;
        budget.RecordCompiledTable(length);
        output = bytes;
        diagnostic = default;
        return ReadStatus.Done;
    }

    public static ReadStatus TryWrite(MaxpTable model, Span<byte> destination,
        out FontDiagnostic diagnostic)
    {
        ArgumentNullException.ThrowIfNull(model);
        int length = model is Maxp05Table ? MaxpLayout.Length05 : MaxpLayout.Length10;
        if (destination.Length < length)
            return ReadWire.Fail(ReadStatus.DestinationTooSmall, DiagnosticCode.DestinationTooSmall,
                model.Location.At(DataField.OutputBytes, 0), out diagnostic,
                (ulong)destination.Length, (ulong)length);
        return Write(model, destination, out diagnostic);
    }

    private static ReadStatus Write(MaxpTable model, Span<byte> bytes,
        out FontDiagnostic diagnostic)
    {
        DataLocation location = model.Location;
        ReadStatus status = MaxpView.TryWriteVersion(bytes, model.VersionRaw, out diagnostic, location);
        if (status != ReadStatus.Done) return status;
        status = MaxpView.TryWriteNumGlyphs(bytes, model.NumGlyphs, out diagnostic, location);
        if (status != ReadStatus.Done) return status;
        if (model is not Maxp10Table v10) return ReadStatus.Done;
        Maxp10Values value = v10.Values;
        status = MaxpView.TryWriteMaxPoints(bytes, value.MaxPoints, out diagnostic, location);
        if (status != ReadStatus.Done) return status;
        status = MaxpView.TryWriteMaxContours(bytes, value.MaxContours, out diagnostic, location);
        if (status != ReadStatus.Done) return status;
        status = MaxpView.TryWriteMaxCompositePoints(bytes, value.MaxCompositePoints, out diagnostic, location);
        if (status != ReadStatus.Done) return status;
        status = MaxpView.TryWriteMaxCompositeContours(bytes, value.MaxCompositeContours, out diagnostic, location);
        if (status != ReadStatus.Done) return status;
        status = MaxpView.TryWriteMaxZones(bytes, value.MaxZones, out diagnostic, location);
        if (status != ReadStatus.Done) return status;
        status = MaxpView.TryWriteMaxTwilightPoints(bytes, value.MaxTwilightPoints, out diagnostic, location);
        if (status != ReadStatus.Done) return status;
        status = MaxpView.TryWriteMaxStorage(bytes, value.MaxStorage, out diagnostic, location);
        if (status != ReadStatus.Done) return status;
        status = MaxpView.TryWriteMaxFunctionDefs(bytes, value.MaxFunctionDefs, out diagnostic, location);
        if (status != ReadStatus.Done) return status;
        status = MaxpView.TryWriteMaxInstructionDefs(bytes, value.MaxInstructionDefs, out diagnostic, location);
        if (status != ReadStatus.Done) return status;
        status = MaxpView.TryWriteMaxStackElements(bytes, value.MaxStackElements, out diagnostic, location);
        if (status != ReadStatus.Done) return status;
        status = MaxpView.TryWriteMaxSizeOfInstructions(bytes, value.MaxSizeOfInstructions, out diagnostic, location);
        if (status != ReadStatus.Done) return status;
        status = MaxpView.TryWriteMaxComponentElements(bytes, value.MaxComponentElements, out diagnostic, location);
        if (status != ReadStatus.Done) return status;
        status = MaxpView.TryWriteMaxComponentDepth(bytes, value.MaxComponentDepth, out diagnostic, location);
        if (status != ReadStatus.Done) return status;
        return ReadStatus.Done;
    }
}
