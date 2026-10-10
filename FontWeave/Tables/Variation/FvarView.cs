using FontWeave.Core;

namespace FontWeave.Tables.Variation;

/// <summary>The whole-table read/validate face of fvar 1.0 (T5-02). The version gate,
/// the axes array bounds and the raw rows are declared; this partial owns the header
/// value domains (reserved==2, axisSize==20), the axes-array placement (a non-empty
/// array may not start inside the header), the per-axis relations and value domains,
/// the instanceSize relation and the dynamic instance walk - the card's hand-written
/// boundary, addressed as axesArrayOffset + axisCount*axisSize with instanceSize
/// strides ("the instances array directly follows the axes array", spec fvar.md).
/// axisCount 0 is a legal table ("treated as a non-variable font", spec fvar.md);
/// what consumers do with it is their own contract (OQ-T5-02-5). Instance coordinate
/// ranges and duplicate instance names are the spec's "should" domain and are not
/// checked here (card 3.1).</summary>
public readonly ref partial struct FvarView
{
    public ReadStatus ValidateTable(out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        var budget = new BuildBudget(limits);
        if (!budget.Input(_data.Length, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        return ValidateTable(out diagnostic, budget);
    }

    internal ReadStatus ValidateTable(out FontDiagnostic diagnostic, BuildBudget budget)
    {
        ReadStatus status = TryRequireKnownVersion(out _, out diagnostic);
        if (status != ReadStatus.Done) return status;
        // Header value domains: the two permanently pinned u16s refuse with the field's
        // raw value as detail and the spec pin as expected.
        status = TryGetReserved(out ushort reserved, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (reserved != FvarLayout.ReservedValue)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.AtLocal(DataField.Payload, FvarLayout.ReservedAt),
                out diagnostic, reserved, FvarLayout.ReservedValue);
        status = TryGetAxisSize(out ushort axisSize, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (axisSize != FvarLayout.AxisStride)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.AtLocal(DataField.Length, FvarLayout.AxisSizeAt),
                out diagnostic, axisSize, FvarLayout.AxisStride);
        status = ValidateAxes(budget, out diagnostic);
        if (status != ReadStatus.Done) return status;
        return ValidateInstances(budget, out diagnostic);
    }

    // The axes array: declared extent (backward placements refuse through the header
    // overlap guard, out-of-window through the native extent), then the per-axis
    // relations and value domains in one pass (min<=default<=max, the reserved flag
    // mask, the axis name ID's hard [256,32768) domain) and the DuplicateTag scan.
    private ReadStatus ValidateAxes(BuildBudget budget, out FontDiagnostic diagnostic)
    {
        ReadStatus status = TryGetAxisCount(out ushort axisCount, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (axisCount == 0) return Done(out diagnostic);
        status = TryGetAxesArrayOffset(out ushort axesOffset, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (axesOffset < FvarLayout.HeaderLength)
            return ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.RangeOutsideSource,
                Location.AtLocal(DataField.Offset, FvarLayout.AxesArrayOffsetAt),
                out diagnostic, axesOffset, FvarLayout.HeaderLength);
        if (!budget.Work(axisCount, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        status = TryRequireAxisExtent(out diagnostic);
        if (status != ReadStatus.Done) return status;
        var seen = new HashSet<uint>();
        for (int ordinal = 0; ordinal < axisCount; ordinal++)
        {
            status = TryGetAxis(ordinal, out var axis, out diagnostic);
            if (status != ReadStatus.Done) return status;
            if (axis.MinValue > axis.DefaultValue || axis.DefaultValue > axis.MaxValue)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    Location.AtLocal(DataField.Payload,
                        (ulong)(axesOffset + ordinal * FvarLayout.AxisStride + 4), ordinal),
                    out diagnostic, unchecked((ulong)(long)axis.DefaultValue),
                    unchecked((ulong)(long)Math.Min(axis.MinValue, axis.MaxValue)));
            if ((axis.Flags & FvarLayout.AxisFlagReservedMask) != 0)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    Location.AtLocal(DataField.Flags,
                        (ulong)(axesOffset + ordinal * FvarLayout.AxisStride + 16), ordinal),
                    out diagnostic, axis.Flags, 0x0001);
            if (NameIDDomain.FvarAxisRefused(axis.AxisNameID))
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    Location.AtLocal(DataField.Payload,
                        (ulong)(axesOffset + ordinal * FvarLayout.AxisStride + 18), ordinal),
                    out diagnostic, axis.AxisNameID, 256);
            if (!seen.Add(axis.AxisTag))
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.DuplicateTag,
                    Location.AtLocal(DataField.Tag,
                        (ulong)(axesOffset + ordinal * FvarLayout.AxisStride), ordinal),
                    out diagnostic, axis.AxisTag, 1);
        }
        return Done(out diagnostic);
    }

    // The instance block: the instanceSize relation first (4*axisCount+4 or +6, the
    // two InstanceRecord shapes), then the extent of the block that directly follows
    // the axes array, then the per-record reserved-flags and name-ID domains. The
    // name-ID domains are the spec's hard text (card 1.2-II); no name-table
    // existence is consulted.
    private ReadStatus ValidateInstances(BuildBudget budget, out FontDiagnostic diagnostic)
    {
        ReadStatus status = TryGetAxisCount(out ushort axisCount, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = TryGetInstanceCount(out ushort instanceCount, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (instanceCount == 0) return Done(out diagnostic);
        status = TryGetInstanceSize(out ushort instanceSize, out diagnostic);
        if (status != ReadStatus.Done) return status;
        ushort baseSize = checked((ushort)(axisCount * 4 + 4));
        if (instanceSize != baseSize && instanceSize != baseSize + 2)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.AtLocal(DataField.Length, FvarLayout.InstanceSizeAt),
                out diagnostic, instanceSize, baseSize);
        status = TryGetAxesArrayOffset(out ushort axesOffset, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = TryGetAxisSize(out ushort axisSize, out diagnostic);
        if (status != ReadStatus.Done) return status;
        long instancesStart = (long)axesOffset + (long)axisCount * axisSize;
        long declaredEnd = instancesStart + (long)instanceCount * instanceSize;
        if (!budget.Work((long)instanceCount * instanceSize, Location, out diagnostic))
            return ReadStatus.ResourceLimit;
        if (declaredEnd > _data.Length)
            return ReadWire.Fail(ReadStatus.Truncated, DiagnosticCode.TruncatedData,
                Location.AtLocal(DataField.Length, FvarLayout.InstanceSizeAt),
                out diagnostic, (ulong)_data.Length, (ulong)declaredEnd);
        bool withPostScript = instanceSize != baseSize;
        int coordinateBytes = axisCount * 4;
        for (int ordinal = 0; ordinal < instanceCount; ordinal++)
        {
            int at = (int)instancesStart + ordinal * instanceSize;
            ushort subfamilyNameID = ReadWire.U16(_data, at);
            ushort flags = ReadWire.U16(_data, at + 2);
            if (flags != 0)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    Location.AtLocal(DataField.Flags, (ulong)(at + 2), ordinal),
                    out diagnostic, flags, 0);
            if (NameIDDomain.SubfamilyRefused(subfamilyNameID))
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    Location.AtLocal(DataField.Payload, (ulong)at, ordinal),
                    out diagnostic, subfamilyNameID, 256);
            if (withPostScript)
            {
                ushort postScriptNameID = ReadWire.U16(_data, at + 4 + coordinateBytes);
                if (NameIDDomain.PostScriptRefused(postScriptNameID))
                    return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                        Location.AtLocal(DataField.Payload, (ulong)(at + 4 + coordinateBytes), ordinal),
                        out diagnostic, postScriptNameID, 256);
            }
        }
        return Done(out diagnostic);
    }

    /// <summary>Reads the complete table into an owned <see cref="FvarTable"/>: the
    /// axes in wire order and every instance materialized (card 3.2, witness bound
    /// R8). The consumed length is the exact end of the instance block.</summary>
    public ReadStatus TryReadTable(out FvarTable? model, out FontDiagnostic diagnostic,
        BuildLimits? limits = null)
    {
        model = null;
        var budget = new BuildBudget(limits);
        if (!budget.Input(_data.Length, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        return TryReadTable(out model, out diagnostic, budget);
    }

    internal ReadStatus TryReadTable(out FvarTable? model, out FontDiagnostic diagnostic,
        BuildBudget budget)
    {
        model = null;
        ReadStatus status = ValidateTable(out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        status = TryGetAxisCount(out ushort axisCount, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = TryGetAxesArrayOffset(out ushort axesOffset, out diagnostic);
        if (status != ReadStatus.Done) return status;
        var axes = new FvarAxis[axisCount];
        for (int ordinal = 0; ordinal < axisCount; ordinal++)
        {
            status = TryGetAxis(ordinal, out var axis, out diagnostic);
            if (status != ReadStatus.Done) return status;
            axes[ordinal] = new(axis.AxisTag, axis.MinValue, axis.DefaultValue, axis.MaxValue,
                axis.Flags, axis.AxisNameID);
        }
        status = TryGetInstanceCount(out ushort instanceCount, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = TryGetInstanceSize(out ushort instanceSize, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = TryGetAxisSize(out ushort axisSize, out diagnostic);
        if (status != ReadStatus.Done) return status;
        int instancesStart = axesOffset + axisCount * axisSize;
        int coordinateBytes = axisCount * 4;
        bool withPostScript = instanceSize != axisCount * 4 + 4;
        var instances = new FvarInstance[instanceCount];
        for (int ordinal = 0; ordinal < instanceCount; ordinal++)
        {
            int at = instancesStart + ordinal * instanceSize;
            var coordinates = new int[axisCount];
            for (int c = 0; c < axisCount; c++)
                coordinates[c] = VariationWire.I32(_data, at + 4 + 4 * c);
            ushort? postScriptNameID = withPostScript
                ? ReadWire.U16(_data, at + 4 + coordinateBytes) : null;
            instances[ordinal] = new(ReadWire.U16(_data, at), ReadWire.U16(_data, at + 2),
                coordinates, postScriptNameID);
        }
        int consumed = FvarLayout.HeaderLength;
        consumed = Math.Max(consumed, checked(axesOffset + axisCount * FvarLayout.AxisStride));
        if (instanceCount > 0)
            consumed = Math.Max(consumed, checked(instancesStart + instanceCount * instanceSize));
        model = new(axes, instances, Location, consumed);
        diagnostic = default;
        return ReadStatus.Done;
    }

    private static ReadStatus Done(out FontDiagnostic diagnostic)
    {
        diagnostic = default;
        return ReadStatus.Done;
    }
}
