using FontWeave.Core;

namespace FontWeave.Tables;

public readonly ref partial struct VdmxView
{
    public ReadStatus TryGetRatioGroupHeader(int ratioOrdinal, out GroupHeaderData header,
        out FontDiagnostic diagnostic)
    {
        header = default;
        ReadStatus status = TryGetGroupPosition(ratioOrdinal, out ulong position, out diagnostic);
        return status == ReadStatus.Done
            ? TryReadGroupHeaderAt(position, ratioOrdinal, out header, out diagnostic) : status;
    }

    public ReadStatus TryGetEntry(int ratioOrdinal, int entryOrdinal, out EntryData entry,
        out FontDiagnostic diagnostic)
    {
        entry = default;
        ReadStatus status = TryGetGroupPosition(ratioOrdinal, out ulong position, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = TryReadGroupHeaderAt(position, ratioOrdinal, out var header, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (entryOrdinal < 0 || entryOrdinal >= header.NumEntries)
            return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                Location.At(DataField.Count, position, entryOrdinal), out diagnostic,
                unchecked((ulong)(long)entryOrdinal), header.NumEntries);
        return TryReadEntryAt(position + VdmxLayout.GroupHeaderLength + (ulong)entryOrdinal * VdmxLayout.EntryStride,
            entryOrdinal, out entry, out diagnostic);
    }

    /// <summary>Validates stored ratios/groups, not grid-fitting or glyph-subset claims.</summary>
    public ReadStatus Validate(out FontDiagnostic diagnostic, BuildLimits? limits = null) =>
        Analyze(out _, out _, out diagnostic, new BuildBudget(limits));

    public ReadStatus TryRead(out VdmxTable? model, out FontDiagnostic diagnostic, BuildLimits? limits = null) =>
        TryRead(out model, out diagnostic, new BuildBudget(limits));

    internal ReadStatus TryRead(out VdmxTable? model, out FontDiagnostic diagnostic, BuildBudget budget)
    {
        model = null;
        ReadStatus status = Analyze(out var descriptors, out var links, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        if (!budget.Work(_data.Length, Location, out diagnostic) ||
            !budget.Buffer(_data.Length + 16L * NumRatios + (IntPtr.Size + 32L) * NumRecs,
                Location, out diagnostic)) return ReadStatus.ResourceLimit;
        var ratios = new VdmxRatio[NumRatios];
        for (int i = 0; i < ratios.Length; i++)
        {
            _ = TryGetRatio(i, out var ratio, out _);
            ratios[i] = new(ratio.CharSet, ratio.XRatio, ratio.YStartRatio, ratio.YEndRatio, links![i]);
        }
        var groups = new VdmxGroup[NumRecs];
        for (int i = 0; i < groups.Length; i++)
        {
            var descriptor = descriptors![i];
            var entries = new VdmxEntry[descriptor.Count];
            for (int j = 0; j < entries.Length; j++)
            {
                _ = TryReadEntryAt(descriptor.Offset + VdmxLayout.GroupHeaderLength + (ulong)j * VdmxLayout.EntryStride,
                    j, out var entry, out _);
                entries[j] = new(entry.YPelHeight, entry.YMax, entry.YMin);
            }
            groups[i] = new(descriptor.Offset, descriptor.StartSize, descriptor.EndSize, entries);
        }
        model = new(Version, ratios, groups, Location);
        diagnostic = default;
        return ReadStatus.Done;
    }

    private ReadStatus TryGetGroupPosition(int ratioOrdinal, out ulong position, out FontDiagnostic diagnostic)
    {
        position = 0;
        ReadStatus status = TryGetGroupOffset(ratioOrdinal, out var offset, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = TryGetDirectoryEnd(out ulong minimum, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (offset.Offset < minimum)
            return ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.StorageBeforeRecords,
                OffsetLocation(ratioOrdinal), out diagnostic, offset.Offset, minimum);
        position = offset.Offset;
        return ReadStatus.Done;
    }

    private ReadStatus Analyze(out GroupDescriptor[]? resultGroups, out int[]? resultLinks,
        out FontDiagnostic diagnostic, BuildBudget budget)
    {
        resultGroups = null;
        resultLinks = null;
        if (!IsBound)
            return ReadWire.Fail(ReadStatus.Uninitialized, DiagnosticCode.UnboundView, Location, out diagnostic);
        if (!budget.Input(_data.Length, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        ReadStatus status = TryRequireKnownVersion(out _, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (NumRecs == 0)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.At(DataField.Count, 2), out diagnostic, 0, 1);
        status = TryGetDirectoryEnd(out ulong position, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (position > (ulong)_data.Length)
            return ReadWire.Fail(ReadStatus.Truncated, DiagnosticCode.TruncatedData,
                Location.At(DataField.Count, 4), out diagnostic, (ulong)_data.Length, position);
        if (!budget.Work(_data.Length + 32L * (NumRecs + NumRatios), Location, out diagnostic) ||
            !budget.Buffer(16L * NumRecs + 4L * NumRatios, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        var groups = new GroupDescriptor[NumRecs];
        var links = new int[NumRatios];
        for (int i = 0; i < groups.Length; i++)
        {
            status = TryReadGroupHeaderAt(position, i, out var header, out diagnostic);
            if (status != ReadStatus.Done) return status;
            status = ValidateGroupRange(header.StartSize, header.EndSize,
                Location.At(DataField.Payload, position + 2, i), out diagnostic);
            if (status != ReadStatus.Done) return status;
            ulong end = position + (uint)VdmxLayout.GroupLength(header.NumEntries);
            if (end > (ulong)_data.Length)
                return ReadWire.Fail(ReadStatus.Truncated, DiagnosticCode.TruncatedData,
                    Location.At(DataField.Length, position, i), out diagnostic, (ulong)_data.Length, end);
            int previousHeight = -1;
            for (int j = 0; j < header.NumEntries; j++)
            {
                ulong row = position + VdmxLayout.GroupHeaderLength + (ulong)j * VdmxLayout.EntryStride;
                _ = TryReadEntryAt(row, j, out var entry, out _);
                status = ValidateEntryValues(new(entry.YPelHeight, entry.YMax, entry.YMin), previousHeight,
                    Location.At(DataField.Payload, row, j), out diagnostic);
                if (status != ReadStatus.Done) return status;
                previousHeight = entry.YPelHeight;
            }
            groups[i] = new(position, header.NumEntries, header.StartSize, header.EndSize);
            position = end;
        }
        if (position != (ulong)_data.Length)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.UnexpectedTrailingData,
                Location.At(DataField.Length, position), out diagnostic, (ulong)_data.Length, position);
        for (int i = 0; i < links.Length; i++)
        {
            _ = TryGetRatio(i, out var ratio, out _);
            status = ValidateRatioValues(new(ratio.CharSet, ratio.XRatio, ratio.YStartRatio, ratio.YEndRatio, 0),
                i, NumRatios, Location, out diagnostic);
            if (status != ReadStatus.Done) return status;
            status = TryGetGroupPosition(i, out ulong offset, out diagnostic);
            if (status != ReadStatus.Done) return status;
            int group = FindGroup(groups, offset);
            if (group < 0)
                return ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.RangeOutsideSource,
                    OffsetLocation(i), out diagnostic, offset);
            links[i] = group;
        }
        resultGroups = groups;
        resultLinks = links;
        diagnostic = default;
        return ReadStatus.Done;
    }

    private DataLocation OffsetLocation(int ordinal) => Location.At(DataField.Offset,
        VdmxLayout.HeaderLength + (ulong)NumRatios * VdmxLayout.RatioStride + (ulong)ordinal * VdmxLayout.OffsetStride, ordinal);

    private static int FindGroup(GroupDescriptor[] groups, ulong offset)
    {
        int low = 0, high = groups.Length - 1;
        while (low <= high)
        {
            int middle = low + (high - low) / 2;
            if (groups[middle].Offset == offset) return middle;
            if (groups[middle].Offset < offset) low = middle + 1;
            else high = middle - 1;
        }
        return -1;
    }

    internal static ReadStatus ValidateRatioValues(VdmxRatio ratio, int ordinal, int count,
        DataLocation location, out FontDiagnostic diagnostic)
    {
        ulong row = VdmxLayout.HeaderLength + (ulong)ordinal * VdmxLayout.RatioStride;
        if (ratio.CharSet > 1)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                location.At(DataField.Payload, row, ordinal), out diagnostic, ratio.CharSet, 1);
        if (ratio.XRatio == 0 && ratio.YStartRatio == 0 && ratio.YEndRatio == 0)
        {
            if (ordinal != count - 1)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    location.At(DataField.Payload, row + 1, ordinal), out diagnostic, (uint)ordinal, (uint)(count - 1));
        }
        else if (ratio.XRatio == 0 || ratio.YStartRatio == 0 || ratio.YEndRatio == 0 || ratio.YStartRatio > ratio.YEndRatio)
        {
            ulong field = ratio.XRatio == 0 ? 1UL : ratio.YStartRatio == 0 || ratio.YStartRatio > ratio.YEndRatio ? 2UL : 3UL;
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                location.At(DataField.Payload, row + field, ordinal), out diagnostic);
        }
        diagnostic = default;
        return ReadStatus.Done;
    }

    internal static ReadStatus ValidateGroupRange(byte start, byte end, DataLocation location,
        out FontDiagnostic diagnostic)
    {
        if (start > end)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                location, out diagnostic, start, end);
        diagnostic = default;
        return ReadStatus.Done;
    }

    internal static ReadStatus ValidateEntryValues(VdmxEntry entry, int previousHeight, DataLocation location,
        out FontDiagnostic diagnostic)
    {
        if (entry.YPelHeight <= previousHeight)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                location, out diagnostic, entry.YPelHeight, (uint)previousHeight);
        if (entry.YMin > entry.YMax)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                location.At(DataField.Payload, location.Offset + 4, location.RecordOrdinal), out diagnostic,
                unchecked((ulong)(long)entry.YMin), unchecked((ulong)(long)entry.YMax));
        diagnostic = default;
        return ReadStatus.Done;
    }

    private readonly record struct GroupDescriptor(ulong Offset, ushort Count, byte StartSize, byte EndSize);
}
