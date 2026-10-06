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
// T4-03 direct leaf slots (types 2/3/4) share the substitution slot shape.
internal sealed record MultipleSlotPlan(MultipleSubstTable Child, MultipleSubstPartPlan Part,
    ushort Target, int Length) : LookupSlotPlan(Target, Length);
internal sealed record AlternateSlotPlan(AlternateSubstTable Child, AlternateSubstPartPlan Part,
    ushort Target, int Length) : LookupSlotPlan(Target, Length);
internal sealed record LigatureSlotPlan(LigatureSubstTable Child, LigaturePartPlan Part,
    ushort Target, int Length) : LookupSlotPlan(Target, Length);
// The finite extension fallback (OQ-T4-03-1 wrap-all): one 8-byte ExtensionSubst
// format-1 header per subtable, all headers placed first so every Offset16 subtable
// target stays representable, and the wrapped leaf bodies placed after them, reachable
// only through the per-extension Offset32 (spec: "the total size of the subtables
// exceeds the 16-bit limits"). BodyTarget is the lookup-local body position.
internal sealed record ExtensionSlotPlan(ushort WrappedType, LookupSlotPlan Wrapped,
    ushort Target, int BodyTarget, int Length) : LookupSlotPlan(Target, Length);

// The whole-lookup plan: flattened source→slot entry order, the unique placed slots
// and the exact wire length (header + offset records + filter word + Σ part lengths).
// The plan's LookupType is the wire type: direct models keep their type; the
// extension candidate and a type-7 read model emit 7 (round-trip fidelity).
internal sealed record LookupEmitPlan(int LookupType, int[] Entries, LookupSlotPlan[] Slots,
    int UniqueCount, int Length);

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

    // The T4-03 explicit lookup factories (types 2/3/4): the same caller-supplied
    // source/kind/child identity rules as TryCreateSingleSubst, one shared body per
    // direct lookup type.
    public static ReadStatus TryCreateMultipleSubst(int numGlyphs, IReadOnlyList<MultipleSubstTable> subtables,
        ushort flags, ushort? markFilteringSet, GdefLookupContext? context, out LookupTable? model,
        out FontDiagnostic diagnostic, BuildLimits? limits = null, DataLocation location = default) =>
        TryCreateRules(numGlyphs, subtables, 2, flags, markFilteringSet, context,
            out model, out diagnostic, new BuildBudget(limits), location);

    public static ReadStatus TryCreateAlternateSubst(int numGlyphs, IReadOnlyList<AlternateSubstTable> subtables,
        ushort flags, ushort? markFilteringSet, GdefLookupContext? context, out LookupTable? model,
        out FontDiagnostic diagnostic, BuildLimits? limits = null, DataLocation location = default) =>
        TryCreateRules(numGlyphs, subtables, 3, flags, markFilteringSet, context,
            out model, out diagnostic, new BuildBudget(limits), location);

    public static ReadStatus TryCreateLigatureSubst(int numGlyphs, IReadOnlyList<LigatureSubstTable> subtables,
        ushort flags, ushort? markFilteringSet, GdefLookupContext? context, out LookupTable? model,
        out FontDiagnostic diagnostic, BuildLimits? limits = null, DataLocation location = default) =>
        TryCreateRules(numGlyphs, subtables, 4, flags, markFilteringSet, context,
            out model, out diagnostic, new BuildBudget(limits), location);

    private static ReadStatus TryCreateRules<TLeaf>(int numGlyphs, IReadOnlyList<TLeaf> subtables,
        ushort lookupType, ushort flags, ushort? markFilteringSet, GdefLookupContext? context,
        out LookupTable? model, out FontDiagnostic diagnostic, BuildBudget budget,
        DataLocation location = default) where TLeaf : class
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
        var copy = new TLeaf[count];
        for (int i = 0; i < count; i++)
        {
            TLeaf? child = subtables[i];
            if (child is null)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    location.AtLocal(DataField.Payload, LookupLayout.HeaderLength +
                        (ulong)i * LookupLayout.OffsetStride, i), out diagnostic);
            if (GlyphsOf(child) != numGlyphs || LocationOf(child).SourceId != location.SourceId ||
                LocationOf(child).FaceIndex != location.FaceIndex)
                return ReadWire.Fail(ReadStatus.InvalidSource, DiagnosticCode.WrongSourceFace,
                    location.AtLocal(DataField.None, 0, i), out diagnostic);
            status = LookupTable.ValidateGsubSource(LocationOf(child), out diagnostic);
            if (status != ReadStatus.Done) return status;
            copy[i] = child;
        }
        MultipleSubstTable[]? multiples = null;
        AlternateSubstTable[]? alternates = null;
        LigatureSubstTable[]? ligatures = null;
        switch (lookupType)
        {
            case 2: multiples = (MultipleSubstTable[])(object)copy; break;
            case 3: alternates = (AlternateSubstTable[])(object)copy; break;
            default: ligatures = (LigatureSubstTable[])(object)copy; break;
        }
        var candidate = new LookupTable(LayoutLookupKind.Gsub, numGlyphs, lookupType, flags,
            markFilteringSet, [], null, location: location, multiples: multiples,
            alternates: alternates, ligatures: ligatures);
        status = candidate.ValidateFlags(context, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        model = candidate;
        diagnostic = default;
        return ReadStatus.Done;
    }

    // A type-7 model's per-subtable wrapper must carry exactly the leaf its declared
    // extension lookup type names; anything else would NRE the planner downstream.
    private static bool MatchesWrapperType(ExtensionSubstTable child) => child.ExtensionLookupType switch
    {
        1 => child.Single is not null,
        2 => child.Multiple is not null,
        3 => child.Alternate is not null,
        _ => child.Ligature is not null,
    };

    private static int GlyphsOf<TLeaf>(TLeaf child) where TLeaf : class => child switch
    {
        MultipleSubstTable model => model.NumGlyphs,
        AlternateSubstTable model => model.NumGlyphs,
        LigatureSubstTable model => model.NumGlyphs,
        _ => throw new InvalidOperationException("Unknown direct rule child."),
    };

    private static DataLocation LocationOf<TLeaf>(TLeaf child) where TLeaf : class => child switch
    {
        MultipleSubstTable model => model.Location,
        AlternateSubstTable model => model.Location,
        LigatureSubstTable model => model.Location,
        _ => throw new InvalidOperationException("Unknown direct rule child."),
    };

    /// <summary>Explicit type-7 factory: every wrapped child is caller-supplied together
    /// with its declared extension lookup type; the wrapper carries no own coverage.</summary>
    public static ReadStatus TryCreateExtensionSubst(int numGlyphs, IReadOnlyList<ExtensionSubstTable> subtables,
        ushort flags, ushort? markFilteringSet, GdefLookupContext? context, out LookupTable? model,
        out FontDiagnostic diagnostic, BuildLimits? limits = null, DataLocation location = default) =>
        TryCreateExtensionSubst(numGlyphs, subtables, flags, markFilteringSet, context,
            out model, out diagnostic, new BuildBudget(limits), location);

    internal static ReadStatus TryCreateExtensionSubst(int numGlyphs, IReadOnlyList<ExtensionSubstTable> subtables,
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
        var copy = new ExtensionSubstTable[count];
        for (int i = 0; i < count; i++)
        {
            ExtensionSubstTable? child = subtables[i];
            if (child is null)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    location.AtLocal(DataField.Payload, LookupLayout.HeaderLength +
                        (ulong)i * LookupLayout.OffsetStride, i), out diagnostic);
            if (child.ExtensionLookupType is not (1 or 2 or 3 or 4) ||
                !MatchesWrapperType(child))
                return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
                    child.Location.AtLocal(DataField.Tag, ExtensionSubstLayout.TypeAt), out diagnostic,
                    child.ExtensionLookupType);
            if (child.Location.SourceId != location.SourceId || child.Location.FaceIndex != location.FaceIndex)
                return ReadWire.Fail(ReadStatus.InvalidSource, DiagnosticCode.WrongSourceFace,
                    location.AtLocal(DataField.None, 0, i), out diagnostic);
            copy[i] = child;
        }
        var candidate = new LookupTable(LayoutLookupKind.Gsub, numGlyphs, 7, flags, markFilteringSet,
            [], null, location: location, extensions: copy);
        status = candidate.ValidateFlags(context, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        model = candidate;
        diagnostic = default;
        return ReadStatus.Done;
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

    // Plan/preflight dispatcher (T4-03 §4.2): a Gsub type-7 read model has only the
    // extension shape (round-trip fidelity); every other model plans direct first and
    // - Gsub only - a refused direct width feeds the finite extension-wrapped candidate
    // (wrap-all, OQ-T4-03-1) on the same budget. Refused candidates' Work stays
    // charged; there is no third candidate and no repack retry. GPOS keeps exactly the
    // direct candidate (its extension story is T4-05/06).
    internal static ReadStatus TryPlan(LookupTable model, GdefLookupContext? context,
        out LookupEmitPlan? plan, out FontDiagnostic diagnostic, BuildBudget budget)
    {
        plan = null;
        if (model.Kind == LayoutLookupKind.Gsub && model.LookupType == 7)
        {
            // A metadata-only type-7 read model has no wrapped children to compile;
            // it keeps the shared unsupported-type refusal instead of planning.
            return model.Extensions is not null
                ? PlanExtension(model, context, out plan, out diagnostic, budget)
                : UnsupportedType(model, out diagnostic);
        }
        ReadStatus status = PlanDirect(model, context, out var direct, out diagnostic, budget);
        if (status == ReadStatus.Done)
        {
            plan = direct;
            diagnostic = default;
            return ReadStatus.Done;
        }
        // Candidate 2 runs only for a Gsub model whose typed payload exists (a
        // metadata-only read model keeps the direct refusal verbatim) and whose direct
        // refusal was a width refusal; budget refusals are never retried.
        if (model.Kind != LayoutLookupKind.Gsub || status != ReadStatus.UnsupportedFormat ||
            !HasTypedPayload(model))
            return status;
        return PlanExtension(model, context, out plan, out diagnostic, budget);
    }

    private static bool HasTypedPayload(LookupTable model) => model.LookupType switch
    {
        1 => model.SingleSubstitutions is not null,
        2 => model.Multiples is not null,
        3 => model.Alternates is not null,
        4 => model.Ligatures is not null,
        _ => model.Extensions is not null,
    };

    private static ReadStatus UnsupportedType(LookupTable model, out FontDiagnostic diagnostic) =>
        ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
            model.Location.AtLocal(DataField.Tag, LookupLayout.TypeAt), out diagnostic, model.LookupType);

    // One gathered leaf part: the owning child model, its part plan and (extension
    // mode) the wire lookup type the wrapper declares.
    private readonly record struct LeafPartPlan(object Child, object Part, ushort WrappedType);

    private static ReadStatus PlanDirect(LookupTable model, GdefLookupContext? context,
        out LookupEmitPlan? plan, out FontDiagnostic diagnostic, BuildBudget budget)
    {
        plan = null;
        ushort type = model.LookupType;
        bool substitution = model.Kind == LayoutLookupKind.Gsub && type == 1 && model.SingleSubstitutions is not null;
        bool positioning = model.Kind == LayoutLookupKind.Gpos && type == 1 && model.SinglePositions is not null;
        bool multiples = model.Kind == LayoutLookupKind.Gsub && type == 2 && model.Multiples is not null;
        bool alternates = model.Kind == LayoutLookupKind.Gsub && type == 3 && model.Alternates is not null;
        bool ligatures = model.Kind == LayoutLookupKind.Gsub && type == 4 && model.Ligatures is not null;
        if (!(substitution || positioning || multiples || alternates || ligatures))
            return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
                model.Location.AtLocal(DataField.Tag, LookupLayout.TypeAt), out diagnostic, model.LookupType);
        ReadStatus status = model.Kind == LayoutLookupKind.Gsub
            ? LookupTable.ValidateGsubSource(model.Location, out diagnostic)
            : LookupTable.ValidateGposSource(model.Location, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = model.ValidateFlags(context, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        status = GatherLeaves(model, budget, out var groups, out int entryCount, out diagnostic);
        if (status != ReadStatus.Done) return status;
        int sourceCount = groups.Length;
        // Header entries remain in semantic order. Only physical leaf placement changes.
        if (!budget.Work(entryCount, model.Location, out diagnostic) ||
            !budget.Buffer(18L * entryCount, model.Location, out diagnostic)) return ReadStatus.ResourceLimit;
        status = BuildSlots(groups, model.Location, budget, out var entries, out var slots,
            out var firstSourceEntries, out int distinct, out int largest, out diagnostic);
        if (status != ReadStatus.Done) return status;
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
        plan = new(model.LookupType, entries, slots, distinct, length);
        diagnostic = default;
        return ReadStatus.Done;
    }

    // The finite extension-wrapped candidate: every source subtable becomes one
    // 8-byte ExtensionSubst format-1 header wrapping its leaf part (wrap-all,
    // OQ-T4-03-1); the leaf parts are the same plans the direct candidate used, so a
    // part that is itself internally unreachable refuses again here. The emitted
    // LookupType is 7 with extensionLookupType = the real type; a type-7 read model
    // keeps its per-subtable wrapped types (uniform by construction on this path).
    private static ReadStatus PlanExtension(LookupTable model, GdefLookupContext? context,
        out LookupEmitPlan? plan, out FontDiagnostic diagnostic, BuildBudget budget)
    {
        plan = null;
        ReadStatus status = LookupTable.ValidateGsubSource(model.Location, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = model.ValidateFlags(context, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        status = GatherLeaves(model, budget, out var groups, out int entryCount, out diagnostic);
        if (status != ReadStatus.Done) return status;
        int sourceCount = groups.Length;
        if (!budget.Work(entryCount, model.Location, out diagnostic) ||
            !budget.Buffer(26L * entryCount, model.Location, out diagnostic)) return ReadStatus.ResourceLimit;
        status = BuildSlots(groups, model.Location, budget, out var entries, out var wrapped,
            out var firstSourceEntries, out int distinct, out int largest, out diagnostic);
        if (status != ReadStatus.Done) return status;
        // Wrap every unique slot: the extension header adds 8 bytes and the slot length
        // grows accordingly; entries keep pointing at their (one) wrapped slot.
        var slots = new ExtensionSlotPlan[distinct];
        for (int i = 0; i < distinct; i++)
            slots[i] = new ExtensionSlotPlan(WrappedOf(wrapped[i]), wrapped[i], 0, 0, wrapped[i].Length + 8);
        int headerLength = LookupLayout.HeaderLength + LookupLayout.OffsetStride * entryCount +
            (model.MarkFilteringSet.HasValue ? 2 : 0);
        long cursor = headerLength;
        // Pass 1 - every extension header is an Offset16 target of the Lookup, so all
        // headers go first and their targets must stay representable (the headers are
        // uniform 8 bytes, so no largest-last reordering applies).
        for (int i = 0; i < distinct; i++)
        {
            status = PlacePayload(model, 8, firstSourceEntries[i], ref cursor,
                out ushort target, out diagnostic);
            if (status != ReadStatus.Done) return status;
            slots[i] = slots[i] with { Target = target };
        }
        // Pass 2 - the bodies land after all headers, reachable only through each
        // extension's Offset32 (relative to that extension subtable's start), so no
        // 16-bit bound applies to them.
        for (int i = 0; i < distinct; i++)
        {
            if (!budget.Work(1, model.Location, out diagnostic)) return ReadStatus.ResourceLimit;
            long bodyTarget = cursor;
            ushort extAt = slots[i].Target;
            DataLocation fieldLocation = model.Location.AtLocal(DataField.Offset,
                (ulong)extAt + ExtensionSubstLayout.OffsetAt);
            status = slots[i].WrappedType switch
            {
                1 => ExtensionSubstView.TryGetWrappedSingleOffsetValue(true, 0UL, (ulong)(bodyTarget - extAt),
                    out _, out diagnostic, fieldLocation),
                2 => ExtensionSubstView.TryGetWrappedMultipleOffsetValue(true, 0UL, (ulong)(bodyTarget - extAt),
                    out _, out diagnostic, fieldLocation),
                3 => ExtensionSubstView.TryGetWrappedAlternateOffsetValue(true, 0UL, (ulong)(bodyTarget - extAt),
                    out _, out diagnostic, fieldLocation),
                _ => ExtensionSubstView.TryGetWrappedLigatureOffsetValue(true, 0UL, (ulong)(bodyTarget - extAt),
                    out _, out diagnostic, fieldLocation),
            };
            if (status != ReadStatus.Done) return status;
            slots[i] = slots[i] with { BodyTarget = (int)bodyTarget };
            cursor += slots[i].Wrapped.Length;
        }
        if (!budget.Work(cursor, model.Location, out diagnostic) ||
            !budget.Output(cursor, model.Location, out diagnostic)) return ReadStatus.ResourceLimit;
        int length = (int)cursor;
        if (!budget.Work(length, model.Location, out diagnostic)) return ReadStatus.ResourceLimit;
        plan = new(7, entries, slots, distinct, length);
        diagnostic = default;
        return ReadStatus.Done;
    }

    private static ushort WrappedOf(LookupSlotPlan slot) => slot switch
    {
        SubstitutionSlotPlan => 1,
        MultipleSlotPlan => 2,
        AlternateSlotPlan => 3,
        LigatureSlotPlan => 4,
        _ => throw new InvalidOperationException("An extension slot cannot wrap another extension slot."),
    };

    // The payload collections of one model, flattened into per-source leaf part plans.
    // Child-level plan dedup keeps reference equality: the same child model is planned
    // exactly once and aliased across all its source positions. Admissions follow the
    // original type-1 order (count, per-unique-child 16, per-entry overflow check).
    private static ReadStatus GatherLeaves(LookupTable model, BuildBudget budget,
        out LeafPartPlan[][] groups, out int entryCount, out FontDiagnostic diagnostic)
    {
        groups = [];
        entryCount = 0;
        ushort type = model.LookupType;
        int sourceCount = type switch
        {
            1 => model.Kind == LayoutLookupKind.Gsub ? model.SingleSubstitutions!.Count : model.SinglePositions!.Count,
            2 => model.Multiples!.Count,
            3 => model.Alternates!.Count,
            4 => model.Ligatures!.Count,
            _ => model.Extensions!.Count,
        };
        if (!budget.Work(sourceCount, model.Location, out diagnostic) ||
            !budget.Buffer(8L * sourceCount, model.Location, out diagnostic)) return ReadStatus.ResourceLimit;
        var result = sourceCount == 0 ? [] : new LeafPartPlan[sourceCount][];
        // Child-level dedup is per leaf compiler, keyed on the child instance.
        var singles = new Dictionary<SingleSubstTable, SingleSubstPartPlan[]>(ReferenceEqualityComparer.Instance);
        var positions = new Dictionary<SinglePosTable, SinglePosPartPlan[]>(ReferenceEqualityComparer.Instance);
        var multiples = new Dictionary<MultipleSubstTable, MultipleSubstPartPlan[]>(ReferenceEqualityComparer.Instance);
        var alternates = new Dictionary<AlternateSubstTable, AlternateSubstPartPlan[]>(ReferenceEqualityComparer.Instance);
        var ligatureSets = new Dictionary<LigatureSubstTable, LigaturePartPlan[]>(ReferenceEqualityComparer.Instance);
        ReadStatus status;
        for (int i = 0; i < sourceCount; i++)
        {
            switch (type)
            {
                case 1 when model.Kind == LayoutLookupKind.Gsub:
                {
                    SingleSubstTable child = model.SingleSubstitutions![i];
                    if (!singles.TryGetValue(child, out var group))
                    {
                        status = SingleSubstTableCompiler.TryPlanSubtables(child, out group, out diagnostic, budget);
                        if (status != ReadStatus.Done) return status;
                        if (!budget.Buffer(16, model.Location, out diagnostic)) return ReadStatus.ResourceLimit;
                        singles.Add(child, group!);
                    }
                    result[i] = ToParts(group!, child);
                    break;
                }
                case 1:
                {
                    SinglePosTable child = model.SinglePositions![i];
                    if (!positions.TryGetValue(child, out var group))
                    {
                        status = SinglePosTableCompiler.TryPlanSubtables(child, out group, out diagnostic, budget);
                        if (status != ReadStatus.Done) return status;
                        if (!budget.Buffer(16, model.Location, out diagnostic)) return ReadStatus.ResourceLimit;
                        positions.Add(child, group!);
                    }
                    result[i] = ToParts(group!, child);
                    break;
                }
                case 2:
                {
                    MultipleSubstTable child = model.Multiples![i];
                    if (!multiples.TryGetValue(child, out var group))
                    {
                        status = MultipleSubstTableCompiler.TryPlanSubtables(child, out group, out diagnostic, budget);
                        if (status != ReadStatus.Done) return status;
                        if (!budget.Buffer(16, model.Location, out diagnostic)) return ReadStatus.ResourceLimit;
                        multiples.Add(child, group!);
                    }
                    result[i] = ToParts(group!, child);
                    break;
                }
                case 3:
                {
                    AlternateSubstTable child = model.Alternates![i];
                    if (!alternates.TryGetValue(child, out var group))
                    {
                        status = AlternateSubstTableCompiler.TryPlanSubtables(child, out group, out diagnostic, budget);
                        if (status != ReadStatus.Done) return status;
                        if (!budget.Buffer(16, model.Location, out diagnostic)) return ReadStatus.ResourceLimit;
                        alternates.Add(child, group!);
                    }
                    result[i] = ToParts(group!, child);
                    break;
                }
                case 4:
                {
                    LigatureSubstTable child = model.Ligatures![i];
                    if (!ligatureSets.TryGetValue(child, out var group))
                    {
                        status = LigatureSubstTableCompiler.TryPlanSubtables(child, out group, out diagnostic, budget);
                        if (status != ReadStatus.Done) return status;
                        if (!budget.Buffer(16, model.Location, out diagnostic)) return ReadStatus.ResourceLimit;
                        ligatureSets.Add(child, group!);
                    }
                    result[i] = ToParts(group!, child);
                    break;
                }
                default:
                {
                    // A type-7 read model: the wrapped leaves keep the extension
                    // subtable's declared lookup type (uniform per model on this path).
                    ExtensionSubstTable child = model.Extensions![i];
                    LeafPartPlan[] parts;
                    switch (child.ExtensionLookupType)
                    {
                        case 1:
                            status = SingleSubstTableCompiler.TryPlanSubtables(child.Single!, out var singleGroup, out diagnostic, budget);
                            if (status != ReadStatus.Done) return status;
                            if (!budget.Buffer(16, model.Location, out diagnostic)) return ReadStatus.ResourceLimit;
                            parts = ToParts(singleGroup!, child.Single!);
                            break;
                        case 2:
                            status = MultipleSubstTableCompiler.TryPlanSubtables(child.Multiple!, out var multipleGroup, out diagnostic, budget);
                            if (status != ReadStatus.Done) return status;
                            if (!budget.Buffer(16, model.Location, out diagnostic)) return ReadStatus.ResourceLimit;
                            parts = ToParts(multipleGroup!, child.Multiple!);
                            break;
                        case 3:
                            status = AlternateSubstTableCompiler.TryPlanSubtables(child.Alternate!, out var alternateGroup, out diagnostic, budget);
                            if (status != ReadStatus.Done) return status;
                            if (!budget.Buffer(16, model.Location, out diagnostic)) return ReadStatus.ResourceLimit;
                            parts = ToParts(alternateGroup!, child.Alternate!);
                            break;
                        default:
                            status = LigatureSubstTableCompiler.TryPlanSubtables(child.Ligature!, out var ligatureGroup, out diagnostic, budget);
                            if (status != ReadStatus.Done) return status;
                            if (!budget.Buffer(16, model.Location, out diagnostic)) return ReadStatus.ResourceLimit;
                            parts = ToParts(ligatureGroup!, child.Ligature!);
                            break;
                    }
                    for (int j = 0; j < parts.Length; j++)
                        parts[j] = parts[j] with { WrappedType = child.ExtensionLookupType };
                    result[i] = parts;
                    break;
                }
            }
            entryCount += result[i].Length;
            if (entryCount > ushort.MaxValue)
                return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
                    model.Location.AtLocal(DataField.Count, LookupLayout.CountAt), out diagnostic,
                    (ulong)entryCount, ushort.MaxValue);
        }
        groups = result;
        diagnostic = default;
        return ReadStatus.Done;
    }

    private static LeafPartPlan[] ToParts(object[] group, object child)
    {
        var parts = new LeafPartPlan[group.Length];
        for (int i = 0; i < group.Length; i++) parts[i] = new LeafPartPlan(child, group[i], 0);
        return parts;
    }

    // Slot dedup keys on the leaf part plan instance; across children the instances
    // are distinct, so this equals the previous byte[]-reference semantics exactly.
    private static ReadStatus BuildSlots(LeafPartPlan[][] groups, DataLocation location,
        BuildBudget budget, out int[] entries, out LookupSlotPlan[] slots, out int[] firstSourceEntries,
        out int distinct, out int largest, out FontDiagnostic diagnostic)
    {
        int entryCount = 0;
        foreach (var group in groups) entryCount += group.Length;
        entries = entryCount == 0 ? [] : new int[entryCount];
        slots = entryCount == 0 ? [] : new LookupSlotPlan[entryCount];
        firstSourceEntries = entryCount == 0 ? [] : new int[entryCount];
        var leaves = new Dictionary<object, int>(ReferenceEqualityComparer.Instance);
        distinct = 0;
        largest = -1;
        int at = 0;
        for (int i = 0; i < groups.Length; i++)
        {
            foreach (var leaf in groups[i])
            {
                if (!leaves.TryGetValue(leaf.Part, out int index))
                {
                    index = distinct++;
                    slots[index] = NewSlot(leaf, location);
                    firstSourceEntries[index] = i;
                    if (largest < 0 || slots[index].Length > slots[largest].Length) largest = index;
                    if (!budget.Buffer(16, location, out diagnostic)) return ReadStatus.ResourceLimit;
                    leaves.Add(leaf.Part, index);
                }
                entries[at++] = index;
            }
        }
        diagnostic = default;
        return ReadStatus.Done;
    }

    private static LookupSlotPlan NewSlot(LeafPartPlan leaf, DataLocation location)
    {
        int length = LeafLength(leaf.Part);
        return leaf.Part switch
        {
            SingleSubstPartPlan part => new SubstitutionSlotPlan((SingleSubstTable)leaf.Child, part, 0, length),
            SinglePosPartPlan part => new PositioningSlotPlan((SinglePosTable)leaf.Child, part, 0, length),
            MultipleSubstPartPlan part => new MultipleSlotPlan((MultipleSubstTable)leaf.Child, part, 0, length),
            AlternateSubstPartPlan part => new AlternateSlotPlan((AlternateSubstTable)leaf.Child, part, 0, length),
            _ => new LigatureSlotPlan((LigatureSubstTable)leaf.Child, (LigaturePartPlan)leaf.Part, 0, length),
        };
    }

    private static int LeafLength(object part) => part switch
    {
        SingleSubstPartPlan plan => plan.Length,
        SinglePosPartPlan plan => plan.Length,
        MultipleSubstPartPlan plan => plan.Length,
        AlternateSubstPartPlan plan => plan.Length,
        _ => ((LigaturePartPlan)part).Length,
    };

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
        ReadStatus status = model.LookupType switch
        {
            2 => LookupView.TryGetMultipleSubstOffsetValue(true, 0UL, (ulong)cursor, out target, out diagnostic, fieldLocation),
            3 => LookupView.TryGetAlternateSubstOffsetValue(true, 0UL, (ulong)cursor, out target, out diagnostic, fieldLocation),
            4 => LookupView.TryGetLigatureSubstOffsetValue(true, 0UL, (ulong)cursor, out target, out diagnostic, fieldLocation),
            7 => LookupView.TryGetExtensionSubstOffsetValue(true, 0UL, (ulong)cursor, out target, out diagnostic, fieldLocation),
            _ => model.Kind == LayoutLookupKind.Gsub
                ? LookupView.TryGetSingleSubstOffsetValue(true, 0UL, (ulong)cursor, out target, out diagnostic, fieldLocation)
                : LookupView.TryGetSinglePosOffsetValue(true, 0UL, (ulong)cursor, out target, out diagnostic, fieldLocation),
        };
        if (status != ReadStatus.Done) return status;
        cursor += length;
        diagnostic = default;
        return ReadStatus.Done;
    }

    // Direct emission into the caller's final destination: header, offset records and
    // filter word through the generated At-writers, then each unique slot's leaf part
    // written in place — no prebuilt payload arrays and no CopyTo remain. Extension
    // slots write the 8-byte ExtensionSubst header first, the wrapped leaf after it.
    internal static void WritePlanned(LookupTable model, LookupEmitPlan plan, Span<byte> destination)
    {
        CoverageTableCompiler.RequireWritten(LookupView.TryWriteLookupType(destination, (ushort)plan.LookupType, out _, model.Location));
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
                case ExtensionSlotPlan extension:
                    WriteExtensionHeader(model, extension,
                        destination.Slice(extension.Target, 8), extension.BodyTarget - extension.Target);
                    WriteWrapped(extension.Wrapped, destination.Slice(extension.BodyTarget, extension.Wrapped.Length));
                    break;
                default:
                    WriteWrapped(plan.Slots[i], destination.Slice(plan.Slots[i].Target, plan.Slots[i].Length));
                    break;
            }
        }
    }

    private static void WriteExtensionHeader(LookupTable model, ExtensionSlotPlan extension,
        Span<byte> destination, int bodyDelta)
    {
        // The Offset32 value is the preflighted body delta relative to this extension
        // subtable's start; the generated helper owns the encoding and a failure here
        // is an invariant breach.
        DataLocation location = model.Location;
        CoverageTableCompiler.RequireWritten(ExtensionSubstView.TryWriteSubstFormat(destination, 1, out _, location));
        CoverageTableCompiler.RequireWritten(ExtensionSubstView.TryWriteExtensionLookupType(destination,
            extension.WrappedType, out _, location));
        uint encoded;
        ReadStatus status = extension.WrappedType switch
        {
            1 => ExtensionSubstView.TryGetWrappedSingleOffsetValue(true, 0UL, (ulong)bodyDelta, out encoded, out _, location),
            2 => ExtensionSubstView.TryGetWrappedMultipleOffsetValue(true, 0UL, (ulong)bodyDelta, out encoded, out _, location),
            3 => ExtensionSubstView.TryGetWrappedAlternateOffsetValue(true, 0UL, (ulong)bodyDelta, out encoded, out _, location),
            _ => ExtensionSubstView.TryGetWrappedLigatureOffsetValue(true, 0UL, (ulong)bodyDelta, out encoded, out _, location),
        };
        if (status != ReadStatus.Done)
            throw new InvalidOperationException("A preflighted extension Offset32 fixup failed.");
        CoverageTableCompiler.RequireWritten(ExtensionSubstView.TryWriteExtensionOffset(destination, encoded, out _, location));
    }

    private static void WriteWrapped(LookupSlotPlan slot, Span<byte> destination)
    {
        switch (slot)
        {
            case SubstitutionSlotPlan substitution:
                SingleSubstTableCompiler.WritePlanned(substitution.Child, substitution.Part, destination);
                break;
            case PositioningSlotPlan positioning:
                SinglePosTableCompiler.WritePlanned(positioning.Child, positioning.Part, destination);
                break;
            case MultipleSlotPlan multiple:
                MultipleSubstTableCompiler.WritePlanned(multiple.Child, multiple.Part, destination);
                break;
            case AlternateSlotPlan alternate:
                AlternateSubstTableCompiler.WritePlanned(alternate.Child, alternate.Part, destination);
                break;
            default:
                var ligature = (LigatureSlotPlan)slot;
                LigatureSubstTableCompiler.WritePlanned(ligature.Child, ligature.Part, destination);
                break;
        }
    }
}
