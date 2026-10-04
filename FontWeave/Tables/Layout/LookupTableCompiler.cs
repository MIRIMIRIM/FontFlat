using FontWeave.Core;

namespace FontWeave.Tables.Layout;

// One unique leaf placement (card A6 1.1-①): a child model and one leaf part plan
// instance — their reference identity replaces the previous byte[]-instance dedup,
// since each part plan is created exactly once per (child, part ordinal) — plus the
// placed 16-bit target and fragment length. Slot plans carry no payload bytes.
internal abstract record LookupSlotPlan(ushort Target, int Length);
internal sealed record SubstitutionSlotPlan(SingleSubstTable Child, SingleSubstPartPlan Part,
    ushort Target, int Length) : LookupSlotPlan(Target, Length);
internal sealed record PositioningSlotPlan(SinglePosTable Child, SinglePosPartPlan Part,
    ushort Target, int Length) : LookupSlotPlan(Target, Length);

// The whole-lookup plan: flattened source→slot entry order, the unique placed slots
// and the exact wire length (header + offset records + filter word + Σ part lengths).
internal sealed record LookupEmitPlan(int[] Entries, LookupSlotPlan[] Slots, int UniqueCount, int Length);

public static class LookupTableCompiler
{
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

    public static ReadStatus TryCreateSinglePos(int numGlyphs, IReadOnlyList<SinglePosTable> subtables,
        ushort flags, ushort? markFilteringSet, GdefLookupContext? context, out LookupTable? model,
        out FontDiagnostic diagnostic, BuildLimits? limits = null, DataLocation location = default) =>
        TryCreateSinglePos(numGlyphs, subtables, flags, markFilteringSet, context,
            out model, out diagnostic, new BuildBudget(limits), location);

    internal static ReadStatus TryCreateSinglePos(int numGlyphs, IReadOnlyList<SinglePosTable> subtables,
        ushort flags, ushort? markFilteringSet, GdefLookupContext? context, out LookupTable? model,
        out FontDiagnostic diagnostic, BuildBudget budget, DataLocation location = default)
    {
        ArgumentNullException.ThrowIfNull(subtables);
        model = null;
        ReadStatus status = CoverageTableCompiler.CheckNumGlyphs(numGlyphs, location, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = LookupTable.ValidateGposSource(location, out diagnostic);
        if (status != ReadStatus.Done) return status;
        int count = subtables.Count;
        if (count is < 0 or > ushort.MaxValue)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                location.AtLocal(DataField.Count, LookupLayout.CountAt), out diagnostic,
                unchecked((ulong)(long)count), ushort.MaxValue);
        if (!budget.Work(2L * count, location, out diagnostic) ||
            !budget.Buffer(8L * count, location, out diagnostic)) return ReadStatus.ResourceLimit;
        var copy = new SinglePosTable[count];
        for (int i = 0; i < count; i++)
        {
            SinglePosTable? child = subtables[i];
            if (child is null)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    location.AtLocal(DataField.Payload, 0, i), out diagnostic);
            if (child.NumGlyphs != numGlyphs || child.Location.SourceId != location.SourceId || child.Location.FaceIndex != location.FaceIndex)
                return ReadWire.Fail(ReadStatus.InvalidSource, DiagnosticCode.WrongSourceFace,
                    location.AtLocal(DataField.None, 0, i), out diagnostic);
            status = LookupTable.ValidateGposSource(child.Location, out diagnostic);
            if (status != ReadStatus.Done) return status;
            copy[i] = child;
        }
        var candidate = new LookupTable(LayoutLookupKind.Gpos, numGlyphs, 1, flags, markFilteringSet, [], null, location, copy);
        status = candidate.ValidateFlags(context, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        model = candidate; diagnostic = default; return ReadStatus.Done;
    }

    /// <summary>Encodes explicitly decoded GSUB/GPOS type-1 leaves. No opaque or Extension fallback.</summary>
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
        // Plan (including the absorbed emission Work admission) completes before the
        // destination capacity check (card A6 §1.5/OQ-A); the destination is untouched
        // on refusal and no intermediate root bytes are produced.
        ReadStatus status = TryPlan(model, context, out var plan, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        if (destination.Length < plan!.Length)
            return ReadWire.Fail(ReadStatus.DestinationTooSmall, DiagnosticCode.DestinationTooSmall,
                model.Location.AtLocal(DataField.OutputBytes, 0), out diagnostic,
                (ulong)destination.Length, (ulong)plan.Length);
        WritePlanned(model, plan, destination);
        diagnostic = default;
        return ReadStatus.Done;
    }

    // Plan/preflight: source/kind/flags first, then child leaf plans (child semantics,
    // split and device candidate searches are leaf-only), then place/fixup through the
    // generated per-reference helpers, then every budget admission including the
    // emission Work moved here from the former TryWrite copy (card A6 §1.2-c/§1.5).
    internal static ReadStatus TryPlan(LookupTable model, GdefLookupContext? context,
        out LookupEmitPlan? plan, out FontDiagnostic diagnostic, BuildBudget budget)
    {
        plan = null;
        bool substitution = model.Kind == LayoutLookupKind.Gsub && model.SingleSubstitutions is not null;
        bool positioning = model.Kind == LayoutLookupKind.Gpos && model.SinglePositions is not null;
        if (model.LookupType != 1 || (!substitution && !positioning))
            return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
                model.Location.AtLocal(DataField.Tag, LookupLayout.TypeAt), out diagnostic, model.LookupType);
        ReadStatus status = substitution ? LookupTable.ValidateGsubSource(model.Location, out diagnostic)
            : LookupTable.ValidateGposSource(model.Location, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = model.ValidateFlags(context, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        int sourceCount = substitution ? model.SingleSubstitutions!.Count : model.SinglePositions!.Count;
        if (!budget.Work(sourceCount, model.Location, out diagnostic) ||
            !budget.Buffer(8L * sourceCount, model.Location, out diagnostic)) return ReadStatus.ResourceLimit;
        SingleSubstPartPlan[][] substitutionGroups = substitution && sourceCount != 0 ? new SingleSubstPartPlan[sourceCount][] : [];
        SinglePosPartPlan[][] positioningGroups = positioning && sourceCount != 0 ? new SinglePosPartPlan[sourceCount][] : [];
        // Child-level plan dedup keeps reference equality: the same child model is
        // planned exactly once and aliased across all its source positions.
        var children = new Dictionary<SingleSubstTable, SingleSubstPartPlan[]>(ReferenceEqualityComparer.Instance);
        var positions = new Dictionary<SinglePosTable, SinglePosPartPlan[]>(ReferenceEqualityComparer.Instance);
        int entryCount = 0;
        for (int i = 0; i < sourceCount; i++)
        {
            if (substitution)
            {
                SingleSubstTable child = model.SingleSubstitutions![i];
                if (!children.TryGetValue(child, out var group))
                {
                    status = SingleSubstTableCompiler.TryPlanSubtables(child, out group, out diagnostic, budget);
                    if (status != ReadStatus.Done) return status;
                    if (!budget.Buffer(16, model.Location, out diagnostic)) return ReadStatus.ResourceLimit;
                    children.Add(child, group!);
                }
                substitutionGroups[i] = group!;
                entryCount += group!.Length;
            }
            else
            {
                SinglePosTable child = model.SinglePositions![i];
                if (!positions.TryGetValue(child, out var group))
                {
                    status = SinglePosTableCompiler.TryPlanSubtables(child, out group, out diagnostic, budget);
                    if (status != ReadStatus.Done) return status;
                    if (!budget.Buffer(16, model.Location, out diagnostic)) return ReadStatus.ResourceLimit;
                    positions.Add(child, group!);
                }
                positioningGroups[i] = group!;
                entryCount += group!.Length;
            }
            if (entryCount > ushort.MaxValue)
                return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
                    model.Location.AtLocal(DataField.Count, LookupLayout.CountAt), out diagnostic,
                    (ulong)entryCount, ushort.MaxValue);
        }
        // Header entries remain in semantic order. Only physical leaf placement changes.
        if (!budget.Work(entryCount, model.Location, out diagnostic) ||
            !budget.Buffer(18L * entryCount, model.Location, out diagnostic)) return ReadStatus.ResourceLimit;
        int[] entries = entryCount == 0 ? [] : new int[entryCount];
        LookupSlotPlan[] slots = entryCount == 0 ? [] : new LookupSlotPlan[entryCount];
        int[] firstSourceEntries = entryCount == 0 ? [] : new int[entryCount];
        // Slot dedup keys on the leaf part plan instance; across children the instances
        // are distinct, so this equals the previous byte[]-reference semantics exactly.
        var leaves = new Dictionary<object, int>(ReferenceEqualityComparer.Instance);
        int distinct = 0, at = 0, largest = -1;
        for (int i = 0; i < sourceCount; i++)
        {
            if (substitution)
            {
                SingleSubstTable child = model.SingleSubstitutions![i];
                foreach (var part in substitutionGroups[i])
                {
                    if (!leaves.TryGetValue(part, out int index))
                    {
                        index = distinct++;
                        slots[index] = new SubstitutionSlotPlan(child, part, 0, part.Length);
                        firstSourceEntries[index] = i;
                        if (largest < 0 || part.Length > slots[largest].Length) largest = index;
                        if (!budget.Buffer(16, model.Location, out diagnostic)) return ReadStatus.ResourceLimit;
                        leaves.Add(part, index);
                    }
                    entries[at++] = index;
                }
            }
            else
            {
                SinglePosTable child = model.SinglePositions![i];
                foreach (var part in positioningGroups[i])
                {
                    if (!leaves.TryGetValue(part, out int index))
                    {
                        index = distinct++;
                        slots[index] = new PositioningSlotPlan(child, part, 0, part.Length);
                        firstSourceEntries[index] = i;
                        if (largest < 0 || part.Length > slots[largest].Length) largest = index;
                        if (!budget.Buffer(16, model.Location, out diagnostic)) return ReadStatus.ResourceLimit;
                        leaves.Add(part, index);
                    }
                    entries[at++] = index;
                }
            }
        }
        int headerLength = LookupLayout.HeaderLength + LookupLayout.OffsetStride * entryCount +
            (model.MarkFilteringSet.HasValue ? 2 : 0);
        long cursor = headerLength;
        for (int i = 0; i < distinct; i++)
        {
            if (i == largest) continue;
            status = PlacePayload(model, slots[i].Length, firstSourceEntries[i], ref cursor,
                out ushort target, out diagnostic);
            if (status != ReadStatus.Done) return status;
            slots[i] = slots[i] with { Target = target };
        }
        if (largest >= 0)
        {
            status = PlacePayload(model, slots[largest].Length, firstSourceEntries[largest], ref cursor,
                out ushort target, out diagnostic);
            if (status != ReadStatus.Done) return status;
            slots[largest] = slots[largest] with { Target = target };
        }
        if (!budget.Work(cursor, model.Location, out diagnostic) ||
            !budget.Output(cursor, model.Location, out diagnostic)) return ReadStatus.ResourceLimit;
        int length = (int)cursor;
        // Emission Work admission absorbed from the former TryWrite copy; the value is
        // unchanged, only its position moved before the destination check (card A6
        // §1.1-②/§1.5).
        if (!budget.Work(length, model.Location, out diagnostic)) return ReadStatus.ResourceLimit;
        plan = new(entries, slots, distinct, length);
        diagnostic = default;
        return ReadStatus.Done;
    }

    private static ReadStatus PlacePayload(LookupTable model, int length, int sourceOrdinal,
        ref long cursor, out ushort target, out FontDiagnostic diagnostic)
    {
        target = 0;
        // Plan-time fixup through the generated per-reference helper (card 1.3-c). With
        // origin=0 the encoded value equals the cursor, so the too-narrow refusal carries
        // Detail=cursor and Expected=65535 at the same field location as the previous
        // hand-written guard; target<origin and a present delta of 0 are unreachable
        // inside the plan invariants (cursor starts at the header length and grows).
        DataLocation fieldLocation = model.Location.AtLocal(DataField.Offset,
            LookupLayout.HeaderLength + (ulong)sourceOrdinal * LookupLayout.OffsetStride, sourceOrdinal);
        ReadStatus status;
        if (model.Kind == LayoutLookupKind.Gsub)
            status = LookupView.TryGetSingleSubstOffsetValue(true, 0UL, (ulong)cursor,
                out target, out diagnostic, fieldLocation);
        else
            status = LookupView.TryGetSinglePosOffsetValue(true, 0UL, (ulong)cursor,
                out target, out diagnostic, fieldLocation);
        if (status != ReadStatus.Done) return status;
        cursor += length;
        diagnostic = default;
        return ReadStatus.Done;
    }

    // Direct emission into the caller's final destination: header, offset records and
    // filter word through the generated At-writers, then each unique slot's leaf part
    // written in place — no prebuilt payload arrays and no CopyTo remain.
    internal static void WritePlanned(LookupTable model, LookupEmitPlan plan, Span<byte> destination)
    {
        CoverageTableCompiler.RequireWritten(LookupView.TryWriteLookupType(destination, 1, out _, model.Location));
        CoverageTableCompiler.RequireWritten(LookupView.TryWriteFlags(destination, model.Flags, out _, model.Location));
        CoverageTableCompiler.RequireWritten(LookupView.TryWriteSubtableCount(destination,
            (ushort)plan.Entries.Length, out _, model.Location));
        for (int i = 0; i < plan.Entries.Length; i++)
            CoverageTableCompiler.RequireWritten(LookupView.TryWriteSubtableOffsetRecordAt(destination,
                LookupLayout.HeaderLength + (ulong)i * LookupLayout.OffsetStride,
                new LookupView.SubtableOffsetRecordData(i, plan.Slots[plan.Entries[i]].Target), out _, model.Location));
        if (model.MarkFilteringSet is ushort set)
            CoverageTableCompiler.RequireWritten(LookupView.TryWriteFilterWordAt(destination,
                LookupLayout.HeaderLength + (ulong)plan.Entries.Length * LookupLayout.OffsetStride,
                new LookupView.FilterWordData(-1, set), out _, model.Location));
        for (int i = 0; i < plan.UniqueCount; i++)
        {
            switch (plan.Slots[i])
            {
                case SubstitutionSlotPlan substitution:
                    SingleSubstTableCompiler.WritePlanned(substitution.Child, substitution.Part,
                        destination.Slice(substitution.Target, substitution.Length));
                    break;
                default:
                    var positioning = (PositioningSlotPlan)plan.Slots[i];
                    SinglePosTableCompiler.WritePlanned(positioning.Child, positioning.Part,
                        destination.Slice(positioning.Target, positioning.Length));
                    break;
            }
        }
    }
}
