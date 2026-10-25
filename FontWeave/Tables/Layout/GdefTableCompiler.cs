using FontWeave.Core;
using FontWeave.Tables.Variation;

namespace FontWeave.Tables.Layout;

/// <summary>The first GDEF root writer: one planned, atomically published whole-table
/// emission for versions 1.0/1.2/1.3, with the typed item variation store accepted and
/// tail-placed (T4-02b; the OQ-T4-02-5 finite refusal is retired). Planning is
/// budget-first with two bounded layout candidates over the five Offset16 subtrees -
/// natural order and largest-leaf-last (the ScriptList family precedent); a refused
/// candidate keeps its charged work, and no generic repacker exists. The store is the
/// fixed sixth block after every subtree in both candidates: the three real witnesses
/// carry exactly this layout (store trailing), so their unedited round-trips are
/// byte-identical, the Offset32 store never competes for the 64K budget, and the
/// choice mirrors the HVAR Offset32 single-canonical ruling (a mid-position source
/// store re-emits wire-equivalent after the canonical re-placement, not byte-equal).
/// Same-kind/context sharing follows the T4-01 identity rules: equal ClassDef
/// instances collapse to one body, and parent-internal sub-bodies share by content
/// identity inside their own compilers.</summary>
public static class GdefTableCompiler
{
    private sealed record Block(object Model, int Position);
    private sealed record Plan(int Length, int[] HeaderOffsets, int StorePosition, List<Block> Blocks);

    /// <summary>Explicit root-model factory: every child and the glyph context are
    /// caller-supplied and never inferred; the store is a v1.3-only slot and a non-null
    /// store under an earlier version refuses exactly like the marksets admission. The
    /// raw offset is a placement outcome, never a factory input.</summary>
    public static ReadStatus TryCreate(uint version, int numGlyphs, ClassDefTable? glyphClasses,
        ClassDefTable? markAttachmentClasses, MarkGlyphSetsTable? markGlyphSets,
        AttachListTable? attachList, LigCaretListTable? ligCaretList,
        VariationStoreTable? itemVariationStore,
        out GdefTable? model, out FontDiagnostic diagnostic, BuildLimits? limits = null,
        DataLocation location = default)
    {
        model = null;
        ReadStatus status = version is not (GdefLayout.Version10 or GdefLayout.Version12
            or GdefLayout.Version13)
            ? ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedVersion,
                location.AtLocal(DataField.Version, 0), out diagnostic, version)
            : CoverageTableCompiler.CheckNumGlyphs(numGlyphs, location, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (markGlyphSets is not null && version < GdefLayout.Version12)
            return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
                location.AtLocal(DataField.Offset, GdefLayout.MarkSetsAt), out diagnostic);
        if (itemVariationStore is not null && version < GdefLayout.Version13)
            return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
                location.AtLocal(DataField.Offset, GdefLayout.VariationStoreAt), out diagnostic);
        if (!SameSource(location, glyphClasses) || !SameSource(location, markAttachmentClasses) ||
            !SameSource(location, markGlyphSets) || !SameSource(location, attachList) ||
            !SameSource(location, ligCaretList) || !SameSource(location, itemVariationStore))
            return ReadWire.Fail(ReadStatus.InvalidSource, DiagnosticCode.WrongSourceFace,
                location.AtLocal(DataField.None, 0), out diagnostic);
        model = new(version, numGlyphs, glyphClasses, markAttachmentClasses, markGlyphSets,
            attachList, ligCaretList, itemVariationStore, 0, location, 0);
        diagnostic = default;
        return ReadStatus.Done;
    }

    private static bool SameSource(DataLocation location, object? model)
    {
        DataLocation child = model switch
        {
            ClassDefTable classes => classes.Location,
            MarkGlyphSetsTable sets => sets.Location,
            AttachListTable attach => attach.Location,
            LigCaretListTable lig => lig.Location,
            VariationStoreTable store => store.Location,
            _ => location,
        };
        return location.SourceId == child.SourceId && location.FaceIndex == child.FaceIndex &&
            (location.SourceId == 0 || location.TableTag == child.TableTag);
    }

    public static ReadStatus TryCompile(GdefTable model, out byte[]? output,
        out FontDiagnostic diagnostic, BuildLimits? limits = null) =>
        TryCompile(model, out output, out diagnostic, new BuildBudget(limits));

    internal static ReadStatus TryCompile(GdefTable model, out byte[]? output,
        out FontDiagnostic diagnostic, BuildBudget budget)
    {
        ArgumentNullException.ThrowIfNull(model);
        output = null;
        ReadStatus status = TryPlan(model, out var plan, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        if (!budget.Work(plan!.Length, model.Location, out diagnostic) ||
            !budget.Buffer(plan.Length, model.Location, out diagnostic)) return ReadStatus.ResourceLimit;
        byte[] bytes = new byte[plan.Length];
        WritePlanned(model, plan, bytes);
        budget.RecordCompiledTable(bytes.Length);
        output = bytes;
        diagnostic = default;
        return ReadStatus.Done;
    }

    public static ReadStatus TryWrite(GdefTable model, Span<byte> destination,
        out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        var budget = new BuildBudget(limits);
        ReadStatus status = TryPlan(model, out var plan, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        // Every plan admission, including the emission work, completes before the
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

    private static ReadStatus TryPlan(GdefTable model, out Plan? plan,
        out FontDiagnostic diagnostic, BuildBudget budget)
    {
        plan = null;
        int headerLength = model.Version == GdefLayout.Version13 ? GdefLayout.Header13
            : model.Version >= GdefLayout.Version12 ? GdefLayout.Header12 : GdefLayout.Header10;
        bool sharedClasses = model.MarkAttachmentClasses is not null &&
            ReferenceEquals(model.GlyphClasses, model.MarkAttachmentClasses);
        int storeSize = model.ItemVariationStore is null ? 0 :
            VariationStoreTableCompiler.Measure(model.ItemVariationStore);
        var children = new List<(object? Model, int Size)>
        {
            (model.GlyphClasses, Size(model.GlyphClasses)),
            (sharedClasses ? null : model.MarkAttachmentClasses,
                sharedClasses ? 0 : Size(model.MarkAttachmentClasses)),
            (model.MarkGlyphSets, Size(model.MarkGlyphSets)),
            (model.AttachList, Size(model.AttachList)),
            (model.LigCaretList, Size(model.LigCaretList)),
        };
        if (!budget.Work(children.Count, model.Location, out diagnostic)) return ReadStatus.ResourceLimit;
        // Candidate 1: natural slot order. Candidate 2: largest leaf last (one chosen
        // maximum; ties keep slot order). Each candidate is a full plan; a refused
        // candidate's Work stays charged and is never refunded (OQ-T4-02-4). Both
        // candidates trail the store (the fixed sixth block), so only the five
        // Offset16 subtrees take part in the choice.
        foreach (bool largestLast in new[] { false, true })
        {
            ReadStatus status = Layout(model, children, storeSize, headerLength, largestLast,
                model.Location, budget, out var candidate, out diagnostic);
            if (status == ReadStatus.Done)
            {
                plan = candidate;
                diagnostic = default;
                return ReadStatus.Done;
            }
            if (status != ReadStatus.UnsupportedFormat) return status;
        }
        // Both candidates refused: the second candidate's width refusal stands, widened
        // to the slot-level contract (UnsupportedOperation at the Offset slot).
        return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
            model.Location.AtLocal(DataField.Offset, GdefLayout.GlyphClassesAt), out diagnostic,
            0, ushort.MaxValue);
    }

    private static int Size(ClassDefTable? model) =>
        model is null ? 0 : ClassDefTableCompiler.Measure(model);

    private static int Size(MarkGlyphSetsTable? model) =>
        model is null ? 0 : MarkGlyphSetsTableCompiler.Measure(model);

    private static int Size(AttachListTable? model) =>
        model is null ? 0 : AttachListTableCompiler.Measure(model);

    private static int Size(LigCaretListTable? model) =>
        model is null ? 0 : LigCaretListTableCompiler.Measure(model);

    private static int Size(VariationStoreTable? model) =>
        model is null ? 0 : VariationStoreTableCompiler.Measure(model);

    private static ReadStatus Layout(GdefTable model, List<(object? Model, int Size)> children,
        int storeSize, int headerLength, bool largestLast, DataLocation location,
        BuildBudget budget, out Plan? plan, out FontDiagnostic diagnostic)
    {
        plan = null;
        // Every Offset16 child's own internal words are preflighted here, before the
        // root's placement decides anything: the GDEF header slots are the root's
        // reach face, while an AttachList or LigCaretList can overflow its own records
        // at any root size. A refusal is the same finite too-narrow shape.
        if (model.AttachList is { } attachList)
        {
            ReadStatus inner = AttachListTableCompiler.TryPlan(attachList, budget,
                out diagnostic);
            if (inner != ReadStatus.Done) return inner;
        }
        if (model.LigCaretList is { } ligCaretList)
        {
            ReadStatus inner = LigCaretListTableCompiler.TryPlan(ligCaretList, budget,
                out diagnostic);
            if (inner != ReadStatus.Done) return inner;
        }
        int largest = -1;
        for (int i = 0; i < children.Count; i++)
        {
            if (children[i].Size == 0) continue;
            if (largest < 0 || children[i].Size > children[largest].Size) largest = i;
        }
        // Natural order: every child in slot order. Largest-leaf-last: the one chosen
        // maximum moves to the end; record/index order never changes either way.
        var order = new List<int>();
        for (int i = 0; i < children.Count; i++)
        {
            if (children[i].Size != 0) order.Add(i);
        }
        if (largestLast && largest >= 0)
        {
            order.Remove(largest);
            order.Add(largest);
        }
        var offsets = new int[children.Count];
        var blocks = new List<Block>();
        int cursor = headerLength;
        foreach (int index in order)
        {
            if (cursor > ushort.MaxValue)
                return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
                    location.AtLocal(DataField.Offset, GdefLayout.GlyphClassesAt), out diagnostic,
                    (ulong)cursor, ushort.MaxValue);
            if (!budget.Work(children[index].Size, location, out diagnostic)) return ReadStatus.ResourceLimit;
            offsets[index] = cursor;
            blocks.Add(new Block(children[index].Model!, cursor));
            cursor += children[index].Size;
        }
        // The store is the fixed trailing sixth block: its Offset32 target carries no
        // 64K pressure, so its position is admitted after the subtree layout without a
        // width check, charged like any child (a refused candidate keeps the charge).
        int storePosition = -1;
        if (model.ItemVariationStore is not null)
        {
            if (!budget.Work(storeSize, location, out diagnostic)) return ReadStatus.ResourceLimit;
            storePosition = cursor;
            blocks.Add(new Block(model.ItemVariationStore, cursor));
            cursor += storeSize;
        }
        plan = new Plan(cursor, offsets, storePosition, blocks);
        diagnostic = default;
        return ReadStatus.Done;
    }

    // Only a fully preflighted plan and destination reach this path.
    private static void WritePlanned(GdefTable model, Plan plan, Span<byte> destination)
    {
        var location = model.Location;
        CoverageTableCompiler.RequireWritten(GdefView.TryWriteVersion(destination, model.Version,
            out _, location));
        WriteSlot(destination, location, GdefLayout.GlyphClassesAt,
            model.GlyphClasses is not null, plan.HeaderOffsets[0]);
        WriteSlot(destination, location, GdefLayout.MarkAttachmentClassesAt,
            model.MarkAttachmentClasses is not null,
            ReferenceEquals(model.GlyphClasses, model.MarkAttachmentClasses)
                ? plan.HeaderOffsets[0] : plan.HeaderOffsets[1]);
        // Child-plan indices follow declared child order (glyph, mark, sets, attach,
        // lig), which is not the wire slot order; the mapping lives here.
        WriteSlot(destination, location, GdefLayout.AttachListAt,
            model.AttachList is not null, plan.HeaderOffsets[3]);
        WriteSlot(destination, location, GdefLayout.LigCaretListAt,
            model.LigCaretList is not null, plan.HeaderOffsets[4]);
        if (model.Version >= GdefLayout.Version12)
        {
            WriteMarkSetsSlot(destination, location, model.MarkGlyphSets is not null,
                plan.HeaderOffsets[2]);
            if (model.Version >= GdefLayout.Version13)
            {
                // The store slot encodes the trailing placement (Offset32 domain); a
                // null store encodes zero through present=false - the v1.3 null-store
                // emission is byte-identical to the pre-flip shape.
                WriteStoreSlot(destination, location, model.ItemVariationStore is not null,
                    plan.StorePosition < 0 ? 0 : plan.StorePosition);
            }
        }
        foreach (var block in plan.Blocks)
        {
            var slice = destination.Slice(block.Position);
            switch (block.Model)
            {
                case ClassDefTable classes:
                    ClassDefTableCompiler.WritePlanned(classes, slice);
                    break;
                case MarkGlyphSetsTable sets:
                    MarkGlyphSetsTableCompiler.WritePlanned(sets, slice);
                    break;
                case AttachListTable attach:
                    AttachListTableCompiler.WritePlanned(attach, slice);
                    break;
                case LigCaretListTable lig:
                    LigCaretListTableCompiler.WritePlanned(lig, slice);
                    break;
                case VariationStoreTable store:
                    // The accepted store compiler (T5-01, byte-proven on HVAR) emits the
                    // canonical header -> dataOffsets -> region list -> VarData order.
                    VariationStoreTableCompiler.WritePlanned(store, slice);
                    break;
                default:
                    throw new InvalidOperationException("Unknown GDEF child block.");
            }
        }
    }

    // The store slot helper mirrors the marksets record-slot discipline: present=false
    // encodes the zero offset, a refusal on a preflighted plan is an invariant breach.
    private static void WriteStoreSlot(Span<byte> destination, DataLocation location,
        bool present, int target)
    {
        ReadStatus status = GdefView.TryGetItemVariationStoreOffsetValue(present, 0UL,
            (ulong)target, out uint encoded, out _, location);
        if (status == ReadStatus.Done)
            status = GdefView.TryWriteVariationStoreHeaderAt(destination, GdefLayout.VariationStoreAt,
                new GdefView.VariationStoreHeaderData(-1, encoded), out _, location);
        CoverageTableCompiler.RequireWritten(status);
    }

    // Narrow-value refusals are the generated helpers' own branches; a preflighted plan
    // never reaches them, so any loud failure here is an invariant breach. Absent
    // children encode as 0 through the same helpers (present=false is not an error).
    private static void WriteSlot(Span<byte> destination, DataLocation location, int slotAt,
        bool present, int target)
    {
        ushort encoded;
        ReadStatus status;
        switch (slotAt)
        {
            case GdefLayout.GlyphClassesAt:
                status = GdefView.TryGetGlyphClassDefOffsetValue(present, 0UL, (ulong)target,
                    out encoded, out _, location);
                if (status != ReadStatus.Done) break;
                status = GdefView.TryWriteGlyphClassDefOffset(destination, encoded, out _, location);
                break;
            case GdefLayout.MarkAttachmentClassesAt:
                status = GdefView.TryGetMarkAttachClassDefOffsetValue(present, 0UL, (ulong)target,
                    out encoded, out _, location);
                if (status != ReadStatus.Done) break;
                status = GdefView.TryWriteMarkAttachClassDefOffset(destination, encoded, out _, location);
                break;
            case GdefLayout.AttachListAt:
                status = GdefView.TryGetAttachListOffsetValue(present, 0UL, (ulong)target,
                    out encoded, out _, location);
                if (status != ReadStatus.Done) break;
                status = GdefView.TryWriteAttachListOffset(destination, encoded, out _, location);
                break;
            default:
                status = GdefView.TryGetLigCaretListOffsetValue(present, 0UL, (ulong)target,
                    out encoded, out _, location);
                if (status != ReadStatus.Done) break;
                status = GdefView.TryWriteLigCaretListOffset(destination, encoded, out _, location);
                break;
        }
        if (status != ReadStatus.Done)
            throw new InvalidOperationException(
                $"A preflighted layout write failed at slot {slotAt} (present={present}, target={target}).");
    }

    private static void WriteMarkSetsSlot(Span<byte> destination, DataLocation location,
        bool present, int target)
    {
        if (GdefView.TryGetMarkGlyphSetsOffsetValue(present, 0UL, (ulong)target,
                out ushort encoded, out _, location) != ReadStatus.Done)
            throw new InvalidOperationException("A preflighted layout write failed.");
        ReadStatus written = GdefView.TryWriteMarkSetsHeaderAt(destination, GdefLayout.MarkSetsAt,
            new GdefView.MarkSetsHeaderData(-1, encoded), out _, location);
        CoverageTableCompiler.RequireWritten(written);
    }
}
