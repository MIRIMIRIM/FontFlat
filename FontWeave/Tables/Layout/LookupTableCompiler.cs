using FontWeave.Core;

namespace FontWeave.Tables.Layout;

public static class LookupTableCompiler
{
    private sealed record Plan(int[] Entries, byte[][] Payloads, ushort[] Targets,
        int UniqueCount, int Length);

    public static ReadStatus TryCreateSingleSubst(int numGlyphs, IReadOnlyList<SingleSubstTable> subtables,
        ushort flags, ushort? markFilteringSet, GdefLookupContext? context, out LookupTable? model,
        out FontDiagnostic diagnostic, BuildLimits? limits = null, DataLocation location = default) =>
        TryCreateSingleSubst(numGlyphs, subtables, flags, markFilteringSet, context,
            out model, out diagnostic, new BuildBudget(limits), location);

    internal static ReadStatus TryCreateSingleSubst(int numGlyphs, IReadOnlyList<SingleSubstTable> subtables,
        ushort flags, ushort? markFilteringSet, GdefLookupContext? context, out LookupTable? model,
        out FontDiagnostic diagnostic, BuildBudget budget, DataLocation location = default)
    {
        ArgumentNullException.ThrowIfNull(subtables);
        model = null;
        ReadStatus status = CoverageTableCompiler.CheckNumGlyphs(numGlyphs, location, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = LookupTable.ValidateGsubSource(location, out diagnostic);
        if (status != ReadStatus.Done) return status;
        int count = subtables.Count;
        if (count > ushort.MaxValue)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                location.AtLocal(DataField.Count, LookupLayout.CountAt), out diagnostic,
                (ulong)count, ushort.MaxValue);
        if (!budget.Work(2L * count, location, out diagnostic) ||
            !budget.Buffer(8L * count, location, out diagnostic)) return ReadStatus.ResourceLimit;
        SingleSubstTable[] copy = count == 0 ? [] : new SingleSubstTable[count];
        for (int i = 0; i < copy.Length; i++)
        {
            SingleSubstTable? subtable = subtables[i];
            if (subtable is null)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    location.AtLocal(DataField.Payload, LookupLayout.HeaderLength +
                        (ulong)i * LookupLayout.OffsetStride, i), out diagnostic);
            if (subtable.NumGlyphs != numGlyphs || subtable.Location.SourceId != location.SourceId ||
                subtable.Location.FaceIndex != location.FaceIndex)
                return ReadWire.Fail(ReadStatus.InvalidSource, DiagnosticCode.WrongSourceFace,
                    location.AtLocal(DataField.None, 0, i), out diagnostic);
            status = LookupTable.ValidateGsubSource(subtable.Location, out diagnostic);
            if (status != ReadStatus.Done) return status;
            copy[i] = subtable;
        }
        var candidate = new LookupTable(LayoutLookupKind.Gsub, numGlyphs, 1, flags, markFilteringSet, [], copy, location);
        status = candidate.ValidateFlags(context, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        model = candidate;
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>Encodes only explicitly decoded GSUB type-1 leaves. No opaque or Extension fallback.</summary>
    public static ReadStatus TryCompile(LookupTable model, GdefLookupContext? context, out byte[]? output,
        out FontDiagnostic diagnostic, BuildLimits? limits = null) =>
        TryCompile(model, context, out output, out diagnostic, new BuildBudget(limits));

    internal static ReadStatus TryCompile(LookupTable model, GdefLookupContext? context, out byte[]? output,
        out FontDiagnostic diagnostic, BuildBudget budget)
    {
        ArgumentNullException.ThrowIfNull(model);
        output = null;
        ReadStatus status = TryPlan(model, context, out var plan, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        if (!budget.Buffer(plan!.Length, model.Location, out diagnostic)) return ReadStatus.ResourceLimit;
        byte[] bytes = new byte[plan.Length];
        WritePlanned(model, plan, bytes);
        budget.RecordCompiledTable(bytes.Length);
        output = bytes;
        diagnostic = default;
        return ReadStatus.Done;
    }

    public static ReadStatus TryWrite(LookupTable model, GdefLookupContext? context, Span<byte> destination,
        out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        var budget = new BuildBudget(limits);
        ReadStatus status = TryCompile(model, context, out var bytes, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        if (destination.Length < bytes!.Length)
            return ReadWire.Fail(ReadStatus.DestinationTooSmall, DiagnosticCode.DestinationTooSmall,
                model.Location.AtLocal(DataField.OutputBytes, 0), out diagnostic,
                (ulong)destination.Length, (ulong)bytes.Length);
        if (!budget.Work(bytes.Length, model.Location, out diagnostic)) return ReadStatus.ResourceLimit;
        bytes.CopyTo(destination);
        diagnostic = default;
        return ReadStatus.Done;
    }

    private static ReadStatus TryPlan(LookupTable model, GdefLookupContext? context,
        out Plan? plan, out FontDiagnostic diagnostic, BuildBudget budget)
    {
        plan = null;
        if (model.Kind != LayoutLookupKind.Gsub || model.LookupType != 1 || model.SingleSubstitutions is null)
            return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
                model.Location.AtLocal(DataField.Tag, LookupLayout.TypeAt), out diagnostic, model.LookupType);
        ReadStatus status = LookupTable.ValidateGsubSource(model.Location, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = model.ValidateFlags(context, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        int sourceCount = model.SingleSubstitutions.Count;
        if (!budget.Work(sourceCount, model.Location, out diagnostic) ||
            !budget.Buffer(8L * sourceCount, model.Location, out diagnostic)) return ReadStatus.ResourceLimit;
        byte[][][] groups = sourceCount == 0 ? [] : new byte[sourceCount][][];
        var children = new Dictionary<SingleSubstTable, byte[][]>(ReferenceEqualityComparer.Instance);
        int entryCount = 0;
        for (int i = 0; i < sourceCount; i++)
        {
            SingleSubstTable child = model.SingleSubstitutions[i];
            if (!children.TryGetValue(child, out byte[][]? group))
            {
                status = SingleSubstTableCompiler.TryCompileSubtables(child, out group, out diagnostic, budget);
                if (status != ReadStatus.Done) return status;
                if (!budget.Buffer(16, model.Location, out diagnostic)) return ReadStatus.ResourceLimit;
                children.Add(child, group!);
            }
            groups[i] = group!;
            entryCount += group!.Length;
            if (entryCount > ushort.MaxValue)
                return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
                    model.Location.AtLocal(DataField.Count, LookupLayout.CountAt), out diagnostic,
                    (ulong)entryCount, ushort.MaxValue);
        }
        // Header entries remain in semantic order. Only physical leaf placement changes.
        if (!budget.Work(entryCount, model.Location, out diagnostic) ||
            !budget.Buffer(18L * entryCount, model.Location, out diagnostic)) return ReadStatus.ResourceLimit;
        int[] entries = entryCount == 0 ? [] : new int[entryCount];
        byte[][] payloads = entryCount == 0 ? [] : new byte[entryCount][];
        ushort[] targets = entryCount == 0 ? [] : new ushort[entryCount];
        int[] firstSourceEntries = entryCount == 0 ? [] : new int[entryCount];
        var leaves = new Dictionary<byte[], int>(ReferenceEqualityComparer.Instance);
        int distinct = 0, at = 0, largest = -1;
        for (int i = 0; i < groups.Length; i++)
        {
            foreach (byte[] payload in groups[i])
            {
                if (!leaves.TryGetValue(payload, out int index))
                {
                    index = distinct++;
                    payloads[index] = payload;
                    firstSourceEntries[index] = i;
                    if (largest < 0 || payload.Length > payloads[largest].Length) largest = index;
                    if (!budget.Buffer(16, model.Location, out diagnostic)) return ReadStatus.ResourceLimit;
                    leaves.Add(payload, index);
                }
                entries[at++] = index;
            }
        }
        int headerLength = LookupLayout.HeaderLength + LookupLayout.OffsetStride * entryCount +
            (model.MarkFilteringSet.HasValue ? 2 : 0);
        long cursor = headerLength;
        for (int i = 0; i < distinct; i++)
        {
            if (i == largest) continue;
            status = PlacePayload(model, payloads[i].Length, firstSourceEntries[i], ref cursor,
                out targets[i], out diagnostic);
            if (status != ReadStatus.Done) return status;
        }
        if (largest >= 0)
        {
            status = PlacePayload(model, payloads[largest].Length, firstSourceEntries[largest], ref cursor,
                out targets[largest], out diagnostic);
            if (status != ReadStatus.Done) return status;
        }
        if (!budget.Work(cursor, model.Location, out diagnostic) ||
            !budget.Output(cursor, model.Location, out diagnostic)) return ReadStatus.ResourceLimit;
        plan = new(entries, payloads, targets, distinct, (int)cursor);
        diagnostic = default;
        return ReadStatus.Done;

    }

    private static ReadStatus PlacePayload(LookupTable model, int length, int sourceOrdinal,
        ref long cursor, out ushort target, out FontDiagnostic diagnostic)
    {
        target = 0;
        if (cursor > ushort.MaxValue)
            return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
                model.Location.AtLocal(DataField.Offset, LookupLayout.HeaderLength +
                    (ulong)sourceOrdinal * LookupLayout.OffsetStride, sourceOrdinal),
                out diagnostic, (ulong)cursor, ushort.MaxValue);
        target = (ushort)cursor;
        cursor += length;
        diagnostic = default;
        return ReadStatus.Done;
    }

    private static void WritePlanned(LookupTable model, Plan plan, Span<byte> destination)
    {
        CoverageTableCompiler.RequireWritten(LookupView.TryWriteLookupType(destination, 1, out _, model.Location));
        CoverageTableCompiler.RequireWritten(LookupView.TryWriteFlags(destination, model.Flags, out _, model.Location));
        CoverageTableCompiler.RequireWritten(LookupView.TryWriteSubtableCount(destination,
            (ushort)plan.Entries.Length, out _, model.Location));
        for (int i = 0; i < plan.Entries.Length; i++)
            CoverageTableCompiler.RequireWritten(LookupView.TryWriteSubtableOffsetRecordAt(destination,
                LookupLayout.HeaderLength + (ulong)i * LookupLayout.OffsetStride,
                new LookupView.SubtableOffsetRecordData(i, plan.Targets[plan.Entries[i]]), out _, model.Location));
        if (model.MarkFilteringSet is ushort set)
            CoverageTableCompiler.RequireWritten(LookupView.TryWriteFilterWordAt(destination,
                LookupLayout.HeaderLength + (ulong)plan.Entries.Length * LookupLayout.OffsetStride,
                new LookupView.FilterWordData(-1, set), out _, model.Location));
        for (int i = 0; i < plan.UniqueCount; i++)
            plan.Payloads[i].CopyTo(destination.Slice(plan.Targets[i], plan.Payloads[i].Length));
    }
}
