using FontWeave.Core;

namespace FontWeave.Tables;

public static class VdmxTableCompiler
{
    public static ReadStatus TryCompile(VdmxTable model, out byte[]? output,
        out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        output = null;
        var budget = new BuildBudget(limits);
        if (!budget.Work((long)model.NumGroups + model.NumRatios, model.Location, out diagnostic))
            return ReadStatus.ResourceLimit;
        ReadStatus status = GetLength(model, out int length, out long entries, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (!budget.Work(length + (long)model.NumRatios + model.NumGroups + entries, model.Location, out diagnostic) ||
            !budget.Output(length, model.Location, out diagnostic) ||
            !budget.Buffer(length, model.Location, out diagnostic)) return ReadStatus.ResourceLimit;
        byte[] bytes = new byte[length];
        Write(model, bytes);
        budget.RecordCompiledTable(length);
        output = bytes;
        diagnostic = default;
        return ReadStatus.Done;
    }

    public static ReadStatus TryWrite(VdmxTable model, Span<byte> destination,
        out FontDiagnostic diagnostic)
    {
        ArgumentNullException.ThrowIfNull(model);
        ReadStatus status = GetLength(model, out int length, out _, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (destination.Length < length)
            return ReadWire.Fail(ReadStatus.DestinationTooSmall, DiagnosticCode.DestinationTooSmall,
                model.Location.At(DataField.OutputBytes, 0), out diagnostic,
                (ulong)destination.Length, (ulong)length);
        Write(model, destination.Slice(0, length));
        diagnostic = default;
        return ReadStatus.Done;
    }

    public static ReadStatus TryApply(VdmxTable model, VdmxEditPlan edit,
        out VdmxTable? updated, out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(edit);
        ArgumentNullException.ThrowIfNull(edit.Ratios);
        ArgumentNullException.ThrowIfNull(edit.Groups);
        ArgumentNullException.ThrowIfNull(edit.Entries);
        updated = null;
        var budget = new BuildBudget(limits);
        if (!budget.Work((long)model.NumGroups + model.NumRatios, model.Location, out diagnostic))
            return ReadStatus.ResourceLimit;
        ReadStatus status = GetLength(model, out int length, out long entryCount, out diagnostic);
        if (status != ReadStatus.Done) return status;
        long requestCount = (long)edit.Ratios.Count + edit.Groups.Count + edit.Entries.Count;
        if (!budget.Work(length + 32L * requestCount, model.Location, out diagnostic) ||
            !budget.Buffer(8L * model.NumRatios + (IntPtr.Size + 32L) * model.NumGroups +
                6L * entryCount + 64L * requestCount, model.Location, out diagnostic))
            return ReadStatus.ResourceLimit;

        foreach (var item in edit.Ratios)
        {
            if (item.RatioOrdinal < 0 || item.RatioOrdinal >= model.NumRatios)
                return InvalidIndex(model.Location.At(DataField.Count, 4, item.RatioOrdinal),
                    item.RatioOrdinal, model.NumRatios, out diagnostic);
            ulong row = RatioPosition(item.RatioOrdinal);
            if (item.CharSet is int charSet && !InRange(charSet, byte.MinValue, byte.MaxValue))
                return InvalidValue(model.Location.At(DataField.Payload, row, item.RatioOrdinal), charSet, out diagnostic);
            if (item.XRatio is int x && !InRange(x, byte.MinValue, byte.MaxValue))
                return InvalidValue(model.Location.At(DataField.Payload, row + 1, item.RatioOrdinal), x, out diagnostic);
            if (item.YStartRatio is int start && !InRange(start, byte.MinValue, byte.MaxValue))
                return InvalidValue(model.Location.At(DataField.Payload, row + 2, item.RatioOrdinal), start, out diagnostic);
            if (item.YEndRatio is int end && !InRange(end, byte.MinValue, byte.MaxValue))
                return InvalidValue(model.Location.At(DataField.Payload, row + 3, item.RatioOrdinal), end, out diagnostic);
            if (item.GroupIndex is int group)
            {
                if (group < 0 || group >= model.NumGroups)
                    return InvalidIndex(OffsetLocation(model, item.RatioOrdinal), group, model.NumGroups, out diagnostic);
                if (model.Groups[group].SourceOffset > ushort.MaxValue)
                    return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                        OffsetLocation(model, item.RatioOrdinal), out diagnostic,
                        model.Groups[group].SourceOffset, ushort.MaxValue);
            }
        }
        foreach (var item in edit.Groups)
        {
            if (item.GroupIndex < 0 || item.GroupIndex >= model.NumGroups)
                return InvalidIndex(model.Location.At(DataField.Count, 2, item.GroupIndex),
                    item.GroupIndex, model.NumGroups, out diagnostic);
            ulong row = model.Groups[item.GroupIndex].SourceOffset;
            if (item.StartSize is int start && !InRange(start, byte.MinValue, byte.MaxValue))
                return InvalidValue(model.Location.At(DataField.Payload, row + 2, item.GroupIndex), start, out diagnostic);
            if (item.EndSize is int end && !InRange(end, byte.MinValue, byte.MaxValue))
                return InvalidValue(model.Location.At(DataField.Payload, row + 3, item.GroupIndex), end, out diagnostic);
        }
        foreach (var item in edit.Entries)
        {
            if (item.GroupIndex < 0 || item.GroupIndex >= model.NumGroups)
                return InvalidIndex(model.Location.At(DataField.Count, 2, item.GroupIndex),
                    item.GroupIndex, model.NumGroups, out diagnostic);
            var group = model.Groups[item.GroupIndex];
            if (item.EntryOrdinal < 0 || item.EntryOrdinal >= group.Entries.Length)
                return InvalidIndex(model.Location.At(DataField.Count, group.SourceOffset, item.EntryOrdinal),
                    item.EntryOrdinal, group.Entries.Length, out diagnostic);
            ulong row = EntryPosition(group, item.EntryOrdinal);
            if (item.YPelHeight is int height && !InRange(height, ushort.MinValue, ushort.MaxValue))
                return InvalidValue(model.Location.At(DataField.Payload, row, item.EntryOrdinal), height, out diagnostic);
            if (item.YMax is int max && !InRange(max, short.MinValue, short.MaxValue))
                return InvalidValue(model.Location.At(DataField.Payload, row + 2, item.EntryOrdinal), max, out diagnostic);
            if (item.YMin is int min && !InRange(min, short.MinValue, short.MaxValue))
                return InvalidValue(model.Location.At(DataField.Payload, row + 4, item.EntryOrdinal), min, out diagnostic);
        }

        var ratioEdits = edit.Ratios.ToArray();
        var groupEdits = edit.Groups.ToArray();
        var entryEdits = edit.Entries.ToArray();
        Array.Sort(ratioEdits, static (a, b) => a.RatioOrdinal.CompareTo(b.RatioOrdinal));
        Array.Sort(groupEdits, static (a, b) => a.GroupIndex.CompareTo(b.GroupIndex));
        Array.Sort(entryEdits, static (a, b) => a.GroupIndex != b.GroupIndex
            ? a.GroupIndex.CompareTo(b.GroupIndex) : a.EntryOrdinal.CompareTo(b.EntryOrdinal));
        for (int i = 1; i < ratioEdits.Length; i++)
            if (ratioEdits[i].RatioOrdinal == ratioEdits[i - 1].RatioOrdinal)
                return Duplicate(model.Location.At(DataField.Count, 4, ratioEdits[i].RatioOrdinal),
                    ratioEdits[i].RatioOrdinal, out diagnostic);
        for (int i = 1; i < groupEdits.Length; i++)
            if (groupEdits[i].GroupIndex == groupEdits[i - 1].GroupIndex)
                return Duplicate(model.Location.At(DataField.Count, 2, groupEdits[i].GroupIndex),
                    groupEdits[i].GroupIndex, out diagnostic);
        for (int i = 1; i < entryEdits.Length; i++)
            if (entryEdits[i].GroupIndex == entryEdits[i - 1].GroupIndex &&
                entryEdits[i].EntryOrdinal == entryEdits[i - 1].EntryOrdinal)
                return Duplicate(model.Location.At(DataField.Count,
                    EntryPosition(model.Groups[entryEdits[i].GroupIndex], entryEdits[i].EntryOrdinal),
                    entryEdits[i].EntryOrdinal), entryEdits[i].EntryOrdinal, out diagnostic);

        var ratios = model.Ratios.ToArray();
        bool changed = false;
        foreach (var item in ratioEdits)
        {
            int i = item.RatioOrdinal;
            VdmxRatio old = ratios[i];
            VdmxRatio next = new(
                item.CharSet is int charSet ? (byte)charSet : old.CharSet,
                item.XRatio is int x ? (byte)x : old.XRatio,
                item.YStartRatio is int start ? (byte)start : old.YStartRatio,
                item.YEndRatio is int end ? (byte)end : old.YEndRatio,
                item.GroupIndex ?? old.GroupIndex);
            status = VdmxView.ValidateRatioValues(next, i, ratios.Length, model.Location, out diagnostic);
            if (status != ReadStatus.Done) return status;
            changed |= next != old;
            ratios[i] = next;
        }
        var groups = new VdmxGroup[model.NumGroups];
        int groupRequest = 0, entryRequest = 0;
        for (int i = 0; i < groups.Length; i++)
        {
            VdmxGroup old = model.Groups[i];
            byte start = old.StartSize, end = old.EndSize;
            if (groupRequest < groupEdits.Length && groupEdits[groupRequest].GroupIndex == i)
            {
                var item = groupEdits[groupRequest++];
                start = item.StartSize is int startValue ? (byte)startValue : old.StartSize;
                end = item.EndSize is int endValue ? (byte)endValue : old.EndSize;
            }
            status = VdmxView.ValidateGroupRange(start, end,
                model.Location.At(DataField.Payload, old.SourceOffset + 2, i), out diagnostic);
            if (status != ReadStatus.Done) return status;
            changed |= start != old.StartSize || end != old.EndSize;
            var entries = old.Entries.ToArray();
            while (entryRequest < entryEdits.Length && entryEdits[entryRequest].GroupIndex == i)
            {
                var item = entryEdits[entryRequest++];
                VdmxEntry previous = entries[item.EntryOrdinal];
                VdmxEntry next = new(
                    item.YPelHeight is int height ? (ushort)height : previous.YPelHeight,
                    item.YMax is int max ? (short)max : previous.YMax,
                    item.YMin is int min ? (short)min : previous.YMin);
                changed |= next != previous;
                entries[item.EntryOrdinal] = next;
            }
            int previousHeight = -1;
            for (int j = 0; j < entries.Length; j++)
            {
                VdmxEntry entry = entries[j];
                status = VdmxView.ValidateEntryValues(entry, previousHeight,
                    model.Location.At(DataField.Payload, EntryPosition(old, j), j), out diagnostic);
                if (status != ReadStatus.Done) return status;
                previousHeight = entry.YPelHeight;
            }
            groups[i] = new(old.SourceOffset, start, end, entries);
        }
        if (!changed)
            return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
                model.Location, out diagnostic);
        updated = new(model.Version, ratios, groups, model.Location);
        diagnostic = default;
        return ReadStatus.Done;
    }

    private static ReadStatus GetLength(VdmxTable model, out int length, out long entryCount,
        out FontDiagnostic diagnostic)
    {
        length = 0;
        entryCount = 0;
        long position = VdmxLayout.DirectoryEnd(model.NumRatios);
        for (int i = 0; i < model.NumGroups; i++)
        {
            VdmxGroup group = model.Groups[i];
            if (group.SourceOffset != (ulong)position)
                return ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.RangeOutsideSource,
                    model.Location.At(DataField.Offset, group.SourceOffset, i), out diagnostic,
                    group.SourceOffset, (ulong)position);
            entryCount += group.Entries.Length;
            position += VdmxLayout.GroupLength(group.Entries.Length);
        }
        for (int i = 0; i < model.NumRatios; i++)
        {
            VdmxRatio ratio = model.Ratios[i];
            if (ratio.GroupIndex < 0 || ratio.GroupIndex >= model.NumGroups)
                return InvalidIndex(OffsetLocation(model, i), ratio.GroupIndex, model.NumGroups, out diagnostic);
            ulong offset = model.Groups[ratio.GroupIndex].SourceOffset;
            if (offset > ushort.MaxValue)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    OffsetLocation(model, i), out diagnostic, offset, ushort.MaxValue);
        }
        if (position > int.MaxValue)
            return ReadWire.Fail(ReadStatus.ResourceLimit, DiagnosticCode.BudgetExceeded,
                model.Location.At(DataField.OutputBytes, 0), out diagnostic, (ulong)position, int.MaxValue);
        length = (int)position;
        diagnostic = default;
        return ReadStatus.Done;
    }

    private static void Write(VdmxTable model, Span<byte> bytes)
    {
        // After the capacity preflight these generated writers cannot fail; a failure
        // is a writer contract bug and surfaces loudly.
        RequireWritten(VdmxView.TryWriteVersion(bytes, model.Version, out _, model.Location));
        RequireWritten(VdmxView.TryWriteNumRecs(bytes, model.NumGroups, out _, model.Location));
        RequireWritten(VdmxView.TryWriteNumRatios(bytes, model.NumRatios, out _, model.Location));
        for (int i = 0; i < model.NumRatios; i++)
        {
            VdmxRatio ratio = model.Ratios[i];
            RequireWritten(VdmxView.TryWriteRatioAt(bytes, RatioPosition(i),
                new VdmxView.RatioData(i, ratio.CharSet, ratio.XRatio, ratio.YStartRatio, ratio.YEndRatio),
                out _, model.Location));
            // GetLength pinned the target offset to <= 0xFFFF before this runs, so the
            // (ushort) narrowing is the declared wire contract, byte-identical to the
            // 16-bit store it replaces.
            RequireWritten(VdmxView.TryWriteGroupOffsetAt(bytes, OffsetPosition(model, i),
                new VdmxView.GroupOffsetData(i, (ushort)model.Groups[ratio.GroupIndex].SourceOffset),
                out _, model.Location));
        }
        for (int i = 0; i < model.NumGroups; i++)
        {
            VdmxGroup group = model.Groups[i];
            int row = (int)group.SourceOffset;
            RequireWritten(VdmxView.TryWriteGroupHeaderAt(bytes, (ulong)row,
                new VdmxView.GroupHeaderData(i, (ushort)group.Entries.Length, group.StartSize, group.EndSize),
                out _, model.Location));
            for (int j = 0; j < group.Entries.Length; j++)
            {
                VdmxEntry entry = group.Entries[j];
                ulong at = (ulong)(row + VdmxLayout.GroupHeaderLength + j * VdmxLayout.EntryStride);
                RequireWritten(VdmxView.TryWriteEntryAt(bytes, at,
                    new VdmxView.EntryData(j, entry.YPelHeight, entry.YMax, entry.YMin),
                    out _, model.Location));
            }
        }
    }

    // Same contract as the pclt emission path: a preflighted write failing is a
    // mechanical bug, not a caller-facing outcome; it must never be silently discarded.
    private static void RequireWritten(ReadStatus status)
    {
        if (status != ReadStatus.Done) throw new InvalidOperationException("A preflighted vdmx write failed.");
    }

    private static ulong RatioPosition(int ordinal) =>
        VdmxLayout.HeaderLength + (ulong)ordinal * VdmxLayout.RatioStride;

    private static ulong OffsetPosition(VdmxTable model, int ordinal) =>
        VdmxLayout.HeaderLength + (ulong)model.NumRatios * VdmxLayout.RatioStride +
        (ulong)ordinal * VdmxLayout.OffsetStride;

    private static DataLocation OffsetLocation(VdmxTable model, int ordinal) =>
        model.Location.At(DataField.Offset, OffsetPosition(model, ordinal), ordinal);

    private static ulong EntryPosition(VdmxGroup group, int ordinal) =>
        group.SourceOffset + VdmxLayout.GroupHeaderLength + (ulong)ordinal * VdmxLayout.EntryStride;

    private static bool InRange(int value, int minimum, int maximum) => value >= minimum && value <= maximum;

    private static ReadStatus InvalidValue(DataLocation location, int value, out FontDiagnostic diagnostic) =>
        ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue, location, out diagnostic,
            unchecked((ulong)(long)value));

    private static ReadStatus InvalidIndex(DataLocation location, int index, int count,
        out FontDiagnostic diagnostic) =>
        ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange, location,
            out diagnostic, unchecked((ulong)(long)index), (ulong)count);

    private static ReadStatus Duplicate(DataLocation location, int ordinal,
        out FontDiagnostic diagnostic) =>
        ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue, location,
            out diagnostic, (ulong)ordinal);
}
