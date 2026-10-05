using FontWeave.Core;

namespace FontWeave.Tables.Layout;

public readonly ref partial struct CaretValueView
{
    /// <summary>Observes the format-3 fields, including a non-conformant zero DeviceOffset.
    /// Typed validation, rather than this local header observation, rejects that null reference.
    /// Formats 1/2 refuse as known-but-inactive and unknown formats as unknown versions;
    /// the generated record reader owns both gates (M1c known-inactive takeover).</summary>
    public ReadStatus TryGetFormat3Header(out Format3HeaderData header, out FontDiagnostic diagnostic) =>
        TryReadFormat3HeaderAt(CaretValueLayout.BodyAt, -1, out header, out diagnostic);

    public ReadStatus Validate(out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        var budget = new BuildBudget(limits);
        if (!budget.Input(_data.Length, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        return Validate(out diagnostic, budget);
    }

    internal ReadStatus Validate(out FontDiagnostic diagnostic, BuildBudget budget)
    {
        ReadStatus status = OpenDevice(out _, out var device, out _, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        return device.Inspect(out _, out _, out diagnostic, budget);
    }

    public ReadStatus TryRead(out CaretValueTable? model, out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        model = null;
        var budget = new BuildBudget(limits);
        if (!budget.Input(_data.Length, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        return TryRead(out model, out diagnostic, budget);
    }

    internal ReadStatus TryRead(out CaretValueTable? model, out FontDiagnostic diagnostic, BuildBudget budget)
    {
        model = null;
        ReadStatus status = OpenDevice(out short coordinate, out var child, out ushort offset, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        status = child.TryRead(out var device, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        model = new(coordinate, device!, Location, offset + device!.ConsumedLength);
        diagnostic = default;
        return ReadStatus.Done;
    }

    private ReadStatus OpenDevice(out short coordinate, out StaticDeviceView child, out ushort offset,
        out FontDiagnostic diagnostic, BuildBudget budget)
    {
        coordinate = 0;
        child = default;
        offset = 0;
        ReadStatus status = TryGetFormat3Header(out var header, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (!budget.Work(CaretValueLayout.Format3Length, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        coordinate = header.Coordinate;
        offset = header.DeviceOffset;
        // The production zero refusal stays in the semantic layer (OQ-M1c-3): the frozen
        // caret-value3-null-device row is InvalidOffset@Offset/4, while the generated
        // ZeroFailure=InvalidValue branch would report InvalidFormat at opener level.
        if (offset == 0)
            return ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.InvalidValue,
                Location.AtLocal(DataField.Offset, CaretValueLayout.DeviceOffsetAt), out diagnostic, 0, 1);
        // The declared envelope owns the storage floor (Minimum=BodyEnd), the source
        // bounds and the child TryOpen with the location translated exactly once.
        return TryOpenDeviceAt(CaretValueLayout.BodyAt, -1, out child, out _, out diagnostic);
    }
}
