using FontWeave.Core;

namespace FontWeave.Tables.Layout;

// The GPOS family root layout constants. The value equals SinglePosLayout.Tag; the
// leaf views declared by T4-05 name this family constant for the same reason the GSUB
// children name GsubLayout.Tag. The root header surface itself is LayoutListsView (the
// declared GSUB/GPOS root: identical v1.0 header layout, version tree and the three
// Offset16 slots, whose generated writers and slot fixup helpers the root writer
// consumes); no second root view exists, so the read face and the write face stay one
// structure definition.
internal static class GposLayout
{
    internal const uint Tag = 0x47504F53;
    internal const uint Version10 = 0x00010000, Version11 = 0x00010001;
    internal const int Header10 = 10, Header11 = 14;
    internal const int ScriptListOffsetField = 4, FeatureListOffsetField = 6,
        LookupListOffsetField = 8, VariationsOffsetField = 10;
}

/// <summary>The first GPOS root writer (T4-05 section 4.3, GsubTableCompiler 同构): one
/// planned, atomically published whole-table emission for the static v1.0 slice with a
/// null variations header. A v1.1 root or any nonzero FeatureVariations raw refuses at
/// the VariationsOffsetField slot (the variable domain is T4-06/T5-02). Planning is
/// budget-first with two bounded layout candidates - natural order
/// (script→feature→lookup) and lookupList-first (OQ-T4-05-6); a refused candidate keeps
/// its charged Work and no generic repacker exists. Scripts, features and lookups are
/// all caller-supplied; their same-source/numGlyphs/kind validation follows the
/// existing LayoutListsTableCompiler rules (kind fixed to Gpos), and the three
/// fragments are emitted through the existing PlanScripts/PlanFeatures/PlanLookups
/// plans - whose per-lookup planning carries the GPOS extension rejection pin.</summary>
public static class GposTableCompiler
{
    /// <summary>Explicit root-model factory: version must be 1.0 and the variations raw
    /// must be zero (both refusals land on the VariationsOffsetField slot); everything
    /// else validates along LayoutListsTableCompiler.TryCreate with kind fixed to Gpos.</summary>
    public static ReadStatus TryCreate(uint version, uint featureVariationsOffsetRaw,
        ReadOnlySpan<LayoutScript> scripts, ReadOnlySpan<LayoutFeature> features,
        ReadOnlySpan<LookupTable> lookups, int numGlyphs, out LayoutListsTable? model,
        out FontDiagnostic diagnostic, BuildLimits? limits = null, DataLocation location = default)
    {
        model = null;
        if (version != GposLayout.Version10)
            return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
                location.AtLocal(DataField.Offset, GposLayout.VariationsOffsetField),
                out diagnostic, version, GposLayout.Version10);
        if (featureVariationsOffsetRaw != 0)
            return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
                location.AtLocal(DataField.Offset, GposLayout.VariationsOffsetField),
                out diagnostic, featureVariationsOffsetRaw, 0);
        return LayoutListsTableCompiler.TryCreate(version, featureVariationsOffsetRaw,
            scripts, features, lookups, numGlyphs, LayoutLookupKind.Gpos, out model,
            out diagnostic, limits, location);
    }

    public static ReadStatus TryCompile(LayoutListsTable model, GdefLookupContext? context,
        out byte[]? output, out FontDiagnostic diagnostic, BuildLimits? limits = null) =>
        TryCompile(model, context, out output, out diagnostic, new BuildBudget(limits));

    internal static ReadStatus TryCompile(LayoutListsTable model, GdefLookupContext? context,
        out byte[]? output, out FontDiagnostic diagnostic, BuildBudget budget)
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

    public static ReadStatus TryWrite(LayoutListsTable model, GdefLookupContext? context,
        Span<byte> destination, out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        var budget = new BuildBudget(limits);
        ReadStatus status = TryPlan(model, context, out var plan, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        // Every plan admission, including the emission Work, completes before the
        // destination capacity check (spec 7.3; A6 1.5 O4 order); the destination is
        // untouched on refusal.
        if (!budget.Work(plan!.Length, model.Location, out diagnostic)) return ReadStatus.ResourceLimit;
        if (destination.Length < plan.Length)
            return ReadWire.Fail(ReadStatus.DestinationTooSmall, DiagnosticCode.DestinationTooSmall,
                model.Location.AtLocal(DataField.OutputBytes, 0), out diagnostic,
                (ulong)destination.Length, (ulong)plan.Length);
        WritePlanned(model, plan, destination);
        diagnostic = default;
        return ReadStatus.Done;
    }

    private sealed record RootPlan(int Length, int[] SlotOffsets,
        LayoutListsTableCompiler.ScriptPlan Scripts,
        LayoutListsTableCompiler.FeaturePlan Features,
        LayoutListsTableCompiler.LookupPlan Lookups);

    // The v1.0/null-variations root boundary mirrors the T4-02 store refusal: the raw
    // value travels in Detail at the variations slot, before any byte is planned.
    private static ReadStatus CheckRoot(LayoutListsTable model, out FontDiagnostic diagnostic)
    {
        if (model.Version != GposLayout.Version10)
            return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
                model.Location.AtLocal(DataField.Offset, GposLayout.VariationsOffsetField),
                out diagnostic, model.Version, GposLayout.Version10);
        if (model.FeatureVariationsOffsetRaw != 0)
            return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
                model.Location.AtLocal(DataField.Offset, GposLayout.VariationsOffsetField),
                out diagnostic, model.FeatureVariationsOffsetRaw, 0);
        if (model.Kind != LayoutLookupKind.Gpos)
            return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
                model.Location.AtLocal(DataField.Tag, 0), out diagnostic, (ulong)model.Kind);
        diagnostic = default;
        return ReadStatus.Done;
    }

    private static ReadStatus TryPlan(LayoutListsTable model, GdefLookupContext? context,
        out RootPlan? plan, out FontDiagnostic diagnostic, BuildBudget budget)
    {
        plan = null;
        ReadStatus status = CheckRoot(model, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = LookupTable.ValidateGposSource(model.Location, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = LayoutListsTableCompiler.PlanScripts(model.Scripts, model.Features.Length,
            out var scripts, out diagnostic, budget, model.Location);
        if (status != ReadStatus.Done) return status;
        status = LayoutListsTableCompiler.PlanFeatures(model.Features, model.Lookups.Length,
            out var features, out diagnostic, budget, model.Location);
        if (status != ReadStatus.Done) return status;
        status = LayoutListsTableCompiler.PlanLookups(model.Lookups, context,
            out var lookups, out diagnostic, budget, model.Location);
        if (status != ReadStatus.Done) return status;
        // Candidate 1: natural order (script→feature→lookup). Candidate 2:
        // lookupList-first (OQ-T4-05-6). Each candidate is a full plan; a refused
        // candidate's Work stays charged and the second candidate's width refusal
        // stands (GsubTableCompiler two-candidate form).
        status = Layout(model, scripts!, features!, lookups!, false, budget, out var candidate, out diagnostic);
        if (status == ReadStatus.Done)
        {
            plan = candidate;
            diagnostic = default;
            return ReadStatus.Done;
        }
        if (status != ReadStatus.UnsupportedFormat) return status;
        status = Layout(model, scripts!, features!, lookups!, true, budget, out candidate, out diagnostic);
        if (status == ReadStatus.Done)
        {
            plan = candidate;
            diagnostic = default;
            return ReadStatus.Done;
        }
        if (status != ReadStatus.UnsupportedFormat) return status;
        // Both candidates refused: the second candidate's width refusal stands, widened
        // to the slot-level contract at the first Offset16 slot.
        return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
            model.Location.AtLocal(DataField.Offset, GposLayout.ScriptListOffsetField),
            out diagnostic, 0, ushort.MaxValue);
    }

    private static ReadStatus Layout(LayoutListsTable model,
        LayoutListsTableCompiler.ScriptPlan scripts, LayoutListsTableCompiler.FeaturePlan features,
        LayoutListsTableCompiler.LookupPlan lookups, bool lookupListFirst, BuildBudget budget,
        out RootPlan? plan, out FontDiagnostic diagnostic)
    {
        plan = null;
        var blocks = new (object Plan, int Size)[] { (scripts, scripts.Length),
            (features, features.Length), (lookups, lookups.Length) };
        if (lookupListFirst)
            (blocks[0], blocks[2]) = (blocks[2], blocks[0]);
        var offsets = new int[3];
        long cursor = GposLayout.Header10;
        for (int i = 0; i < blocks.Length; i++)
        {
            // Every root slot is an Offset16 from the root start; the placement refusal
            // carries the offending cursor at the slot's own field location.
            if (cursor > ushort.MaxValue)
                return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
                    model.Location.AtLocal(DataField.Offset, GposLayout.ScriptListOffsetField),
                    out diagnostic, (ulong)cursor, ushort.MaxValue);
            if (!budget.Work(blocks[i].Size, model.Location, out diagnostic)) return ReadStatus.ResourceLimit;
            offsets[i] = (int)cursor;
            cursor += blocks[i].Size;
        }
        if (!budget.Output(cursor, model.Location, out diagnostic) ||
            !budget.Work(cursor, model.Location, out diagnostic)) return ReadStatus.ResourceLimit;
        plan = new((int)cursor, offsets, scripts, features, lookups);
        diagnostic = default;
        return ReadStatus.Done;
    }

    // Only a fully preflighted plan and destination reach this path. Absent-children
    // encoding does not exist at the GPOS root: all three lists are required (their
    // Offset16 slots are ZeroAllowed=false), and empty lists are 2-byte bodies.
    private static void WritePlanned(LayoutListsTable model, RootPlan plan, Span<byte> destination)
    {
        var location = model.Location;
        CoverageTableCompiler.RequireWritten(LayoutListsView.TryWriteVersion(destination, model.Version, out _, location));
        WriteSlot(destination, location, GposLayout.ScriptListOffsetField, plan.SlotOffsets[0]);
        WriteSlot(destination, location, GposLayout.FeatureListOffsetField, plan.SlotOffsets[1]);
        WriteSlot(destination, location, GposLayout.LookupListOffsetField, plan.SlotOffsets[2]);
        LayoutListsTableCompiler.WriteScripts(plan.Scripts, destination.Slice(plan.SlotOffsets[0], plan.Scripts.Length));
        LayoutListsTableCompiler.WriteFeatures(plan.Features, destination.Slice(plan.SlotOffsets[1], plan.Features.Length));
        LayoutListsTableCompiler.WriteLookups(plan.Lookups, destination.Slice(plan.SlotOffsets[2], plan.Lookups.Length));
    }

    private static void WriteSlot(Span<byte> destination, DataLocation location, int slotAt, int target)
    {
        ushort encoded;
        ReadStatus status = slotAt switch
        {
            GposLayout.ScriptListOffsetField => LayoutListsView.TryGetScriptListOffsetValue(true, 0UL,
                (ulong)target, out encoded, out _, location),
            GposLayout.FeatureListOffsetField => LayoutListsView.TryGetFeatureListOffsetValue(true, 0UL,
                (ulong)target, out encoded, out _, location),
            _ => LayoutListsView.TryGetLookupListOffsetValue(true, 0UL, (ulong)target, out encoded, out _, location),
        };
        if (status != ReadStatus.Done)
            throw new InvalidOperationException(
                $"A preflighted root write failed at slot {slotAt} (target={target}).");
        ReadStatus written = slotAt switch
        {
            GposLayout.ScriptListOffsetField => LayoutListsView.TryWriteScriptListOffset(destination, encoded, out _, location),
            GposLayout.FeatureListOffsetField => LayoutListsView.TryWriteFeatureListOffset(destination, encoded, out _, location),
            _ => LayoutListsView.TryWriteLookupListOffset(destination, encoded, out _, location),
        };
        CoverageTableCompiler.RequireWritten(written);
    }
}
