using FontWeave.Core;

namespace FontWeave.Tables.Layout;

public readonly ref partial struct CaretValueView
{
    /// <summary>Observes the format-3 fields, including a non-conformant zero DeviceOffset.
    /// Typed validation, rather than this local header observation, rejects that null reference.</summary>
    public ReadStatus TryGetFormat3Header(out Format3HeaderData header, out FontDiagnostic diagnostic)
    {
        header = default;
        ReadStatus status = RequireFormat3(out diagnostic);
        return status == ReadStatus.Done
            ? TryReadFormat3HeaderAt(CaretValueLayout.BodyAt, -1, out header, out diagnostic) : status;
    }

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
        if (offset == 0)
            return ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.InvalidValue,
                Location.AtLocal(DataField.Offset, CaretValueLayout.DeviceOffsetAt), out diagnostic, 0, 1);
        if (offset < CaretValueLayout.Format3Length)
            return ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.StorageBeforeRecords,
                Location.AtLocal(DataField.Offset, CaretValueLayout.DeviceOffsetAt), out diagnostic,
                offset, CaretValueLayout.Format3Length);
        if (offset >= _data.Length)
            return ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.RangeOutsideSource,
                Location.AtLocal(DataField.Offset, CaretValueLayout.DeviceOffsetAt), out diagnostic,
                offset, (ulong)_data.Length);
        return StaticDeviceView.TryOpen(_data.Slice(offset), out child, out diagnostic,
            Location.AtLocal(DataField.None, offset));
    }

    private ReadStatus RequireFormat3(out FontDiagnostic diagnostic)
    {
        ReadStatus status = TryGetFormat(out ushort format, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (format != 3)
            return ReadWire.Fail(ReadStatus.UnsupportedFormat, format is 1 or 2
                    ? DiagnosticCode.UnsupportedOperation : DiagnosticCode.UnsupportedVersion,
                Location.AtLocal(DataField.Version, 0), out diagnostic, format, 3);
        diagnostic = default;
        return ReadStatus.Done;
    }
}
