using FontWeave.Core;

namespace FontWeave.Tables.Variation;

/// <summary>The fvar root writer (HvarTableCompiler shape): one planned, atomically
/// published whole-table emission. The canonical layout is fixed - header(16), axes
/// at 16 (axesArrayOffset=16, axisSize=20, reserved=2), then the instance block in
/// the spec's "directly follows" position - and every frozen fvar witness already
/// carries exactly it (frozen facts: axesArrayOffset 16, exactEnd 21/21), so unedited
/// round-trips are byte-identical and no second layout candidate exists. The
/// instanceSize is the shape's own consequence: 4*axisCount+6 when every instance
/// carries a postScriptNameID, 4*axisCount+4 when none does; mixed shapes are the
/// OQ-T5-02-6 finite refusal (no shape mixing, no record insertion or removal). All
/// counts, the shape derivation and the budget admission complete before any byte is
/// written; a refused plan never touches the destination.</summary>
public static class FvarTableCompiler
{
    /// <summary>Explicit root-model factory: every axis and instance is caller-supplied
    /// and never inferred. The semantic write contract mirrors the read side
    /// (min<=default<=max, the reserved flag mask, the wire name-ID domains, the
    /// DuplicateTag scan, instance flags and name-ID domains, one instance shape).</summary>
    public static ReadStatus TryCreate(IReadOnlyList<FvarAxis> axes,
        IReadOnlyList<FvarInstance> instances, out FvarTable? model,
        out FontDiagnostic diagnostic, BuildLimits? limits = null,
        DataLocation location = default)
    {
        model = null;
        ArgumentNullException.ThrowIfNull(axes);
        ArgumentNullException.ThrowIfNull(instances);
        var budget = new BuildBudget(limits);
        // The header counts are UInt16 scalars; anything past the wire domain is the
        // plan's refusal, never a silent truncation at emission.
        if (axes.Count > ushort.MaxValue)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                location.AtLocal(DataField.Count, FvarLayout.AxisCountAt),
                out diagnostic, (ulong)axes.Count, (ulong)ushort.MaxValue);
        if (instances.Count > ushort.MaxValue)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                location.AtLocal(DataField.Count, FvarLayout.InstanceCountAt),
                out diagnostic, (ulong)instances.Count, (ulong)ushort.MaxValue);
        ReadStatus status = ValidateAxes(axes, location, budget, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = ValidateInstances(instances, axes.Count, location, budget, out diagnostic);
        if (status != ReadStatus.Done) return status;
        model = new(axes, instances, location, Measure(axes.Count, instances, axes.Count));
        diagnostic = default;
        return ReadStatus.Done;
    }

    private static ReadStatus ValidateAxes(IReadOnlyList<FvarAxis> axes, DataLocation location,
        BuildBudget budget, out FontDiagnostic diagnostic)
    {
        if (!budget.Work(axes.Count, location, out diagnostic)) return ReadStatus.ResourceLimit;
        var seen = new HashSet<uint>();
        for (int i = 0; i < axes.Count; i++)
        {
            FvarAxis axis = axes[i];
            int at = FvarLayout.HeaderLength + i * FvarLayout.AxisStride;
            if (axis.MinValue > axis.DefaultValue || axis.DefaultValue > axis.MaxValue)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    location.AtLocal(DataField.Payload, (ulong)(at + 4), i), out diagnostic,
                    unchecked((ulong)(long)axis.DefaultValue), unchecked((ulong)(long)Math.Min(axis.MinValue, axis.MaxValue)));
            if ((axis.Flags & FvarLayout.AxisFlagReservedMask) != 0)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    location.AtLocal(DataField.Flags, (ulong)(at + 16), i), out diagnostic,
                    axis.Flags, 0x0001);
            if (NameIDDomain.FvarAxisRefused(axis.AxisNameID))
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    location.AtLocal(DataField.Payload, (ulong)(at + 18), i), out diagnostic,
                    axis.AxisNameID, 256);
            if (!seen.Add(axis.Tag))
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.DuplicateTag,
                    location.AtLocal(DataField.Tag, (ulong)at, i), out diagnostic, axis.Tag, 1);
        }
        return Done(out diagnostic);
    }

    private static ReadStatus ValidateInstances(IReadOnlyList<FvarInstance> instances,
        int axisCount, DataLocation location, BuildBudget budget, out FontDiagnostic diagnostic)
    {
        if (!budget.Work(instances.Count, location, out diagnostic)) return ReadStatus.ResourceLimit;
        bool? withPostScript = null;
        for (int i = 0; i < instances.Count; i++)
        {
            FvarInstance instance = instances[i];
            if (instance.Coordinates.Count != axisCount)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    location.AtLocal(DataField.Count, (ulong)(FvarLayout.AxisSizeAt), i),
                    out diagnostic, (ulong)instance.Coordinates.Count, (ulong)axisCount);
            bool instanceHasPostScript = instance.PostScriptNameID.HasValue;
            if (withPostScript is null) withPostScript = instanceHasPostScript;
            else if (withPostScript != instanceHasPostScript)
                return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
                    location.AtLocal(DataField.Length, FvarLayout.InstanceSizeAt, i),
                    out diagnostic, (ulong)i, 0);
            int at = FvarLayout.HeaderLength + axisCount * FvarLayout.AxisStride +
                i * InstanceSize(axisCount, instanceHasPostScript);
            if (instance.Flags != 0)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    location.AtLocal(DataField.Flags, (ulong)(at + 2), i), out diagnostic,
                    instance.Flags, 0);
            if (NameIDDomain.SubfamilyRefused(instance.SubfamilyNameID))
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    location.AtLocal(DataField.Payload, (ulong)at, i), out diagnostic,
                    instance.SubfamilyNameID, 256);
            if (instanceHasPostScript && NameIDDomain.PostScriptRefused(instance.PostScriptNameID!.Value))
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    location.AtLocal(DataField.Payload,
                        (ulong)(at + 4 + axisCount * 4), i), out diagnostic,
                    instance.PostScriptNameID.Value, 256);
        }
        return Done(out diagnostic);
    }

    /// <summary>The instance record size for one shape: 4*axisCount+4, or +6 with the
    /// optional postScriptNameID.</summary>
    internal static int InstanceSize(int axisCount, bool withPostScript) =>
        axisCount * 4 + (withPostScript ? 6 : 4);

    private static int Measure(int axisCount, IReadOnlyList<FvarInstance> instances,
        int instanceAxisCount) =>
        instances.Count == 0
            ? FvarLayout.HeaderLength + axisCount * FvarLayout.AxisStride
            : FvarLayout.HeaderLength + axisCount * FvarLayout.AxisStride +
                instances.Count * InstanceSize(instanceAxisCount, instances[0].PostScriptNameID.HasValue);

    public static ReadStatus TryCompile(FvarTable model, out byte[]? output,
        out FontDiagnostic diagnostic, BuildLimits? limits = null) =>
        TryCompile(model, out output, out diagnostic, new BuildBudget(limits));

    internal static ReadStatus TryCompile(FvarTable model, out byte[]? output,
        out FontDiagnostic diagnostic, BuildBudget budget)
    {
        ArgumentNullException.ThrowIfNull(model);
        output = null;
        ReadStatus status = TryPlan(model, out int length, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        byte[] bytes = new byte[length];
        WritePlanned(model, bytes);
        budget.RecordCompiledTable(bytes.Length);
        output = bytes;
        diagnostic = default;
        return ReadStatus.Done;
    }

    public static ReadStatus TryWrite(FvarTable model, Span<byte> destination,
        out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        var budget = new BuildBudget(limits);
        ReadStatus status = TryPlan(model, out int length, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        // Every plan admission completes before the destination capacity check
        // (spec 7.3); the destination is untouched on refusal.
        if (destination.Length < length)
            return ReadWire.Fail(ReadStatus.DestinationTooSmall, DiagnosticCode.DestinationTooSmall,
                model.Location.AtLocal(DataField.OutputBytes, 0), out diagnostic,
                (ulong)destination.Length, (ulong)length);
        WritePlanned(model, destination);
        diagnostic = default;
        return ReadStatus.Done;
    }

    private static ReadStatus TryPlan(FvarTable model, out int length,
        out FontDiagnostic diagnostic, BuildBudget budget)
    {
        long emission = model.AxisCount + model.Instances.Count;
        length = Measure(model.AxisCount, model.Instances, model.AxisCount);
        if (!budget.Work(emission + length, model.Location, out diagnostic) ||
            !budget.Output(length, model.Location, out diagnostic) ||
            !budget.Buffer(length, model.Location, out diagnostic)) return ReadStatus.ResourceLimit;
        diagnostic = default;
        return ReadStatus.Done;
    }

    // Only a fully preflighted plan and destination reach this path; a generated or
    // plan-pinned write failing here is an invariant breach and surfaces loudly.
    private static void WritePlanned(FvarTable model, Span<byte> destination)
    {
        var location = model.Location;
        RequireWritten(FvarView.TryWriteVersion(destination, 0x00010000, out _, location));
        RequireWritten(FvarView.TryWriteAxesArrayOffset(destination, FvarLayout.HeaderLength,
            out _, location));
        RequireWritten(FvarView.TryWriteReserved(destination, FvarLayout.ReservedValue,
            out _, location));
        RequireWritten(FvarView.TryWriteAxisCount(destination, (ushort)model.AxisCount,
            out _, location));
        RequireWritten(FvarView.TryWriteAxisSize(destination, FvarLayout.AxisStride,
            out _, location));
        RequireWritten(FvarView.TryWriteInstanceCount(destination, (ushort)model.Instances.Count,
            out _, location));
        bool withPostScript = model.Instances.Count > 0 && model.Instances[0].PostScriptNameID.HasValue;
        ushort instanceSize = (ushort)InstanceSize(model.AxisCount, withPostScript);
        RequireWritten(FvarView.TryWriteInstanceSize(destination, instanceSize, out _, location));
        for (int i = 0; i < model.AxisCount; i++)
        {
            FvarAxis axis = model.Axes[i];
            RequireWritten(FvarView.TryWriteAxisAt(destination,
                (ulong)(FvarLayout.HeaderLength + i * FvarLayout.AxisStride),
                new FvarView.AxisData(i, axis.Tag, axis.MinValue, axis.DefaultValue, axis.MaxValue,
                    axis.Flags, axis.AxisNameID), out _, location));
        }
        int instancesStart = FvarLayout.HeaderLength + model.AxisCount * FvarLayout.AxisStride;
        // Big-endian byte extraction truncates by design; the assembly-wide overflow
        // check must not turn a wire truncation into an OverflowException (the
        // generator's unchecked((byte)...) discipline, ViewEmitter EmitWriteValue).
        unchecked
        {
            for (int i = 0; i < model.Instances.Count; i++)
            {
                FvarInstance instance = model.Instances[i];
                int at = instancesStart + i * instanceSize;
                destination[at] = (byte)(instance.SubfamilyNameID >> 8);
                destination[at + 1] = (byte)instance.SubfamilyNameID;
                destination[at + 2] = (byte)(instance.Flags >> 8);
                destination[at + 3] = (byte)instance.Flags;
                for (int c = 0; c < instance.Coordinates.Count; c++)
                {
                    int coordinate = instance.Coordinates[c];
                    destination[at + 4 + 4 * c] = (byte)(coordinate >> 24);
                    destination[at + 5 + 4 * c] = (byte)(coordinate >> 16);
                    destination[at + 6 + 4 * c] = (byte)(coordinate >> 8);
                    destination[at + 7 + 4 * c] = (byte)coordinate;
                }
                if (withPostScript)
                {
                    int postScriptAt = at + 4 + model.AxisCount * 4;
                    destination[postScriptAt] = (byte)(instance.PostScriptNameID!.Value >> 8);
                    destination[postScriptAt + 1] = (byte)instance.PostScriptNameID.Value;
                }
            }
        }
    }

    private static void RequireWritten(ReadStatus status)
    {
        if (status != ReadStatus.Done)
            throw new InvalidOperationException("A preflighted fvar write failed.");
    }

    private static ReadStatus Done(out FontDiagnostic diagnostic)
    {
        diagnostic = default;
        return ReadStatus.Done;
    }
}
