using FontWeave.Core;

namespace FontWeave.Tables.Variation;

/// <summary>The STAT root writer (GdefTableCompiler shape, single candidate): one
/// planned, atomically published whole-table emission over the canonical layout -
/// header (source version preserved), the design axes at HeaderEnd, the offsets
/// array right after them, and every axis value contiguous in offsets-array order.
/// The canonical order front-loads the offsets array so the Offset16 axis value
/// offsets keep the whole remaining table as their window; a value placement past
/// 0xFFFF refuses at plan time (the card's DestinationTooSmall domain - the real
/// witness maximum is 772 bytes, the check stays hard). Sources whose values are
/// already canonical re-emit byte-identical (18 of the 19 frozen STAT witnesses);
/// an out-of-order source (AnekBangla) re-emits wire-equivalent after the canonical
/// re-placement, the T5-01 maps-before-store precedent. New writes are versions
/// 1.1/1.2 only - 1.0 is deprecated (the factory refuses; round-tripping a 1.0
/// source through the read model's own version stays legal, OQ-T5-02-6's finite
/// refusal for version upgrades). All fixups, counts and budget checks complete
/// before any byte is written.</summary>
public static class StatTableCompiler
{
    /// <summary>Explicit root-model factory: version, axes, values and the elided
    /// fallback are caller-supplied and never inferred. Version 1.0 refuses as
    /// deprecated for new writes; 1.1/1.2 require the elided fallback name ID. The
    /// semantic write contract mirrors the read side (designAxisSize is pinned 8,
    /// DuplicateTag, the name-ID wire domains, and per value: the format domain,
    /// axisIndex/designAxisCount, format 4's version activation, zero combination
    /// count and distinct combination axes).</summary>
    public static ReadStatus TryCreate(uint version, IReadOnlyList<StatDesignAxis> axes,
        IReadOnlyList<StatAxisValue> values, ushort? elidedFallbackNameID,
        out StatTable? model, out FontDiagnostic diagnostic, BuildLimits? limits = null,
        DataLocation location = default)
    {
        model = null;
        ArgumentNullException.ThrowIfNull(axes);
        ArgumentNullException.ThrowIfNull(values);
        var budget = new BuildBudget(limits);
        if (version == StatLayout.Version10)
            return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.DeprecatedValue,
                location.AtLocal(DataField.Version, 0), out diagnostic, version, StatLayout.Version11);
        if (version is not (StatLayout.Version11 or StatLayout.Version12))
            return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedVersion,
                location.AtLocal(DataField.Version, 0), out diagnostic, version);
        if (elidedFallbackNameID is null)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                location.AtLocal(DataField.Payload, StatLayout.ElidedFallbackNameIDAt),
                out diagnostic, 0ul, 256ul);
        if (NameIDDomain.StatAxisRefused(elidedFallbackNameID.Value))
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                location.AtLocal(DataField.Payload, StatLayout.ElidedFallbackNameIDAt),
                out diagnostic, elidedFallbackNameID.Value, 256);
        if (!budget.Work(axes.Count + values.Count, location, out diagnostic))
            return ReadStatus.ResourceLimit;
        // The header counts are UInt16 scalars; anything past the wire domain is the
        // plan's refusal, never a silent truncation at emission.
        if (axes.Count > ushort.MaxValue)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                location.AtLocal(DataField.Count, StatLayout.DesignAxisCountAt),
                out diagnostic, (ulong)axes.Count, (ulong)ushort.MaxValue);
        if (values.Count > ushort.MaxValue)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                location.AtLocal(DataField.Count, StatLayout.AxisValueCountAt),
                out diagnostic, (ulong)values.Count, (ulong)ushort.MaxValue);
        var seen = new HashSet<uint>();
        for (int i = 0; i < axes.Count; i++)
        {
            StatDesignAxis axis = axes[i];
            int at = StatLayout.Header11 + i * StatLayout.DesignAxisStride;
            if (!seen.Add(axis.AxisTag))
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.DuplicateTag,
                    location.AtLocal(DataField.Tag, (ulong)at, i), out diagnostic, axis.AxisTag, 1);
            if (NameIDDomain.StatAxisRefused(axis.AxisNameID))
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    location.AtLocal(DataField.Payload, (ulong)(at + 4), i), out diagnostic,
                    axis.AxisNameID, 256);
        }
        for (int i = 0; i < values.Count; i++)
        {
            ReadStatus status = ValidateValue(values[i], version, axes.Count, i, location,
                budget, out diagnostic);
            if (status != ReadStatus.Done) return status;
        }
        model = new(version, axes, values, elidedFallbackNameID,
            Array.Empty<StatNameIdMismatch>(), location, 0);
        diagnostic = default;
        return ReadStatus.Done;
    }

    // The per-value write contract (the read-side StatAxisValueView pass, applied to
    // the owned model; the combination axis set and row count are part of it).
    private static ReadStatus ValidateValue(StatAxisValue value, uint version, int designAxisCount,
        int ordinal, DataLocation location, BuildBudget budget, out FontDiagnostic diagnostic)
    {
        if (!budget.Work(value.WireLength, location, out diagnostic)) return ReadStatus.ResourceLimit;
        if (NameIDDomain.StatValueRefused(value.ValueNameID))
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                location.AtLocal(DataField.Payload, 0, ordinal), out diagnostic,
                value.ValueNameID, 256);
        switch (value.Format)
        {
            case 1 or 2 or 3:
                if (value.AxisIndex >= designAxisCount)
                    return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                        location.AtLocal(DataField.Payload, 0, ordinal), out diagnostic,
                        value.AxisIndex, (ulong)designAxisCount);
                return Done(out diagnostic);
            case 4:
                if (version < StatLayout.Version12)
                    return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                        location.AtLocal(DataField.Version, 0), out diagnostic,
                        value.Format, (ulong)StatLayout.Version12);
                if (value.Combination.Count == 0)
                    return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                        location.AtLocal(DataField.Count, 0, ordinal), out diagnostic, 0ul, 1ul);
                var seen = new HashSet<ushort>();
                for (int i = 0; i < value.Combination.Count; i++)
                {
                    StatCombinationEntry entry = value.Combination[i];
                    if (entry.AxisIndex >= designAxisCount)
                        return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                            location.AtLocal(DataField.Payload, 0, ordinal), out diagnostic,
                            entry.AxisIndex, (ulong)designAxisCount);
                    if (!seen.Add(entry.AxisIndex))
                        return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                            location.AtLocal(DataField.Payload, 0, ordinal), out diagnostic,
                            entry.AxisIndex, 1ul);
                }
                return Done(out diagnostic);
            default:
                return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedVersion,
                    location.AtLocal(DataField.Version, 0), out diagnostic, value.Format);
        }
    }

    internal static int Measure(StatTable model)
    {
        int header = model.Version >= StatLayout.Version11 ? StatLayout.Header11 : StatLayout.Header10;
        int length = header + model.DesignAxes.Count * StatLayout.DesignAxisStride +
            model.AxisValues.Count * StatLayout.AxisValueOffsetStride;
        foreach (StatAxisValue value in model.AxisValues)
            length += value.WireLength;
        return length;
    }

    public static ReadStatus TryCompile(StatTable model, out byte[]? output,
        out FontDiagnostic diagnostic, BuildLimits? limits = null) =>
        TryCompile(model, out output, out diagnostic, new BuildBudget(limits));

    internal static ReadStatus TryCompile(StatTable model, out byte[]? output,
        out FontDiagnostic diagnostic, BuildBudget budget)
    {
        ArgumentNullException.ThrowIfNull(model);
        output = null;
        ReadStatus status = TryPlan(model, out var plan, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        byte[] bytes = new byte[plan!.Length];
        WritePlanned(model, plan, bytes);
        budget.RecordCompiledTable(bytes.Length);
        output = bytes;
        diagnostic = default;
        return ReadStatus.Done;
    }

    public static ReadStatus TryWrite(StatTable model, Span<byte> destination,
        out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        var budget = new BuildBudget(limits);
        ReadStatus status = TryPlan(model, out var plan, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        if (destination.Length < plan!.Length)
            return ReadWire.Fail(ReadStatus.DestinationTooSmall, DiagnosticCode.DestinationTooSmall,
                model.Location.AtLocal(DataField.OutputBytes, 0), out diagnostic,
                (ulong)destination.Length, (ulong)plan.Length);
        WritePlanned(model, plan, destination);
        diagnostic = default;
        return ReadStatus.Done;
    }

    private sealed record Plan(int Length, int AxesAt, int OffsetsAt, int[] ValueOffsets);

    private static ReadStatus TryPlan(StatTable model, out Plan? plan,
        out FontDiagnostic diagnostic, BuildBudget budget)
    {
        plan = null;
        int header = model.Version >= StatLayout.Version11 ? StatLayout.Header11 : StatLayout.Header10;
        int axesAt = header;
        int offsetsAt = axesAt + model.DesignAxes.Count * StatLayout.DesignAxisStride;
        var valueOffsets = new int[model.AxisValues.Count];
        int cursor = offsetsAt + model.AxisValues.Count * StatLayout.AxisValueOffsetStride;
        for (int i = 0; i < model.AxisValues.Count; i++)
        {
            // The Offset16 axis value offsets carry the remaining window; the
            // canonical order front-loads the offsets array to maximize it.
            if (cursor - offsetsAt > ushort.MaxValue)
                return ReadWire.Fail(ReadStatus.DestinationTooSmall,
                    DiagnosticCode.DestinationTooSmall,
                    model.Location.AtLocal(DataField.Offset,
                        (ulong)(StatLayout.OffsetToAxisValueOffsetsAt + 2 * i), i),
                    out diagnostic, (ulong)(cursor - offsetsAt), ushort.MaxValue);
            if (!budget.Work(model.AxisValues[i].WireLength, model.Location, out diagnostic))
                return ReadStatus.ResourceLimit;
            valueOffsets[i] = cursor - offsetsAt;
            cursor += model.AxisValues[i].WireLength;
        }
        int length = cursor;
        if (!budget.Work(length + model.AxisValues.Count, model.Location, out diagnostic) ||
            !budget.Output(length, model.Location, out diagnostic) ||
            !budget.Buffer(length, model.Location, out diagnostic)) return ReadStatus.ResourceLimit;
        plan = new Plan(length, axesAt, offsetsAt, valueOffsets);
        diagnostic = default;
        return ReadStatus.Done;
    }

    private static void WritePlanned(StatTable model, Plan plan, Span<byte> destination)
    {
        var location = model.Location;
        RequireWritten(StatView.TryWriteVersion(destination, model.Version, out _, location));
        RequireWritten(StatView.TryWriteDesignAxisSize(destination, StatLayout.DesignAxisSize,
            out _, location));
        RequireWritten(StatView.TryWriteDesignAxisCount(destination, (ushort)model.DesignAxes.Count,
            out _, location));
        RequireWritten(StatView.TryWriteDesignAxesOffset(destination, (uint)plan.AxesAt,
            out _, location));
        RequireWritten(StatView.TryWriteAxisValueCount(destination, (ushort)model.AxisValues.Count,
            out _, location));
        // The existence domain is two-sided (count>0 iff offset>0, spec STAT.md):
        // an empty axis value array carries the null offsets pointer, whatever the
        // plan's cursor placement would be.
        RequireWritten(StatView.TryWriteOffsetToAxisValueOffsets(destination,
            model.AxisValues.Count == 0 ? 0 : (uint)plan.OffsetsAt, out _, location));
        if (model.Version >= StatLayout.Version11)
            RequireWritten(StatView.TryWriteElidedFallbackNameAt(destination,
                StatLayout.ElidedFallbackNameIDAt,
                new StatView.ElidedFallbackNameData(-1, model.ElidedFallbackNameID!.Value),
                out _, location));
        for (int i = 0; i < model.DesignAxes.Count; i++)
        {
            StatDesignAxis axis = model.DesignAxes[i];
            RequireWritten(StatView.TryWriteDesignAxisAt(destination,
                (ulong)(plan.AxesAt + i * StatLayout.DesignAxisStride),
                new StatView.DesignAxisData(i, axis.AxisTag, axis.AxisNameID, axis.AxisOrdering),
                out _, location));
        }
        for (int i = 0; i < model.AxisValues.Count; i++)
        {
            StatAxisValue value = model.AxisValues[i];
            RequireWritten(StatView.TryWriteAxisValueOffsetsAt(destination,
                (ulong)(plan.OffsetsAt + i * StatLayout.AxisValueOffsetStride),
                new StatView.AxisValueOffsetsData(i, (ushort)plan.ValueOffsets[i]), out _, location));
            int at = plan.OffsetsAt + plan.ValueOffsets[i];
            // The axis value's schema position is record-relative, so the format
            // scalar emitter takes the record slice (HvarTableCompiler's child-slice
            // discipline): the whole-table span would land on the header version.
            RequireWritten(StatAxisValueView.TryWriteFormat(destination.Slice(at),
                (ushort)value.Format, out _, location));
            switch (value.Format)
            {
                case 1:
                    RequireWritten(StatAxisValueView.TryWriteFormat1BodyAt(destination,
                        (ulong)(at + StatAxisValueLayout.PrefixLength),
                        new StatAxisValueView.Format1BodyData(i, value.AxisIndex, value.Flags,
                            value.ValueNameID, value.Value), out _, location));
                    break;
                case 2:
                    RequireWritten(StatAxisValueView.TryWriteFormat2BodyAt(destination,
                        (ulong)(at + StatAxisValueLayout.PrefixLength),
                        new StatAxisValueView.Format2BodyData(i, value.AxisIndex, value.Flags,
                            value.ValueNameID, value.NominalValue, value.RangeMinValue,
                            value.RangeMaxValue), out _, location));
                    break;
                case 3:
                    RequireWritten(StatAxisValueView.TryWriteFormat3BodyAt(destination,
                        (ulong)(at + StatAxisValueLayout.PrefixLength),
                        new StatAxisValueView.Format3BodyData(i, value.AxisIndex, value.Flags,
                            value.ValueNameID, value.Value, value.LinkedValue), out _, location));
                    break;
                default:
                    RequireWritten(StatAxisValueView.TryWriteFormat4HeaderAt(destination,
                        (ulong)(at + StatAxisValueLayout.PrefixLength),
                        new StatAxisValueView.Format4HeaderData(i, (ushort)value.Combination.Count,
                            value.Flags, value.ValueNameID), out _, location));
                    int entryAt = at + StatAxisValueLayout.PrefixLength +
                        StatAxisValueLayout.Format4HeaderStride;
                    // Big-endian byte extraction truncates by design (the generator's
                    // unchecked discipline); the assembly-wide overflow check stays out.
                    unchecked
                    {
                        foreach (StatCombinationEntry entry in value.Combination)
                        {
                            destination[entryAt] = (byte)(entry.AxisIndex >> 8);
                            destination[entryAt + 1] = (byte)entry.AxisIndex;
                            destination[entryAt + 2] = (byte)(entry.Value >> 24);
                            destination[entryAt + 3] = (byte)(entry.Value >> 16);
                            destination[entryAt + 4] = (byte)(entry.Value >> 8);
                            destination[entryAt + 5] = (byte)entry.Value;
                            entryAt += StatAxisValueLayout.Format4EntryStride;
                        }
                    }
                    break;
            }
        }
    }

    private static void RequireWritten(ReadStatus status)
    {
        if (status != ReadStatus.Done)
            throw new InvalidOperationException("A preflighted STAT write failed.");
    }

    private static ReadStatus Done(out FontDiagnostic diagnostic)
    {
        diagnostic = default;
        return ReadStatus.Done;
    }
}
