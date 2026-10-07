using FontWeave.Core;

namespace FontWeave.Tables.Layout;

/// <summary>The first GDEF root writer: one planned, atomically published whole-table
/// emission for the static T4-02 slice (versions 1.0/1.2/1.3 with a null variation
/// store; a non-null store is a limited typed refusal, OQ-T4-02-5). Planning is
/// budget-first with two bounded layout candidates - natural order and largest-leaf-
/// last (the ScriptList family precedent); a refused candidate keeps its charged work,
/// and no generic repacker exists. Same-kind/context sharing follows the T4-01 identity
/// rules: equal ClassDef instances collapse to one body, and parent-internal sub-bodies
/// share by content identity inside their own compilers.</summary>
public static class GdefTableCompiler
{
    private sealed record Block(object Model, int Position);
    private sealed record Plan(int Length, int[] HeaderOffsets, List<Block> Blocks);

    /// <summary>Explicit root-model factory: every child and the glyph context are
    /// caller-supplied and never inferred; the variation store must be the null-store
    /// zero (typed v1.3 compilation refuses anything else, OQ-T4-02-5).</summary>
    public static ReadStatus TryCreate(uint version, int numGlyphs, ClassDefTable? glyphClasses,
        ClassDefTable? markAttachmentClasses, MarkGlyphSetsTable? markGlyphSets,
        AttachListTable? attachList, LigCaretListTable? ligCaretList,
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
        if (!SameSource(location, glyphClasses) || !SameSource(location, markAttachmentClasses) ||
            !SameSource(location, markGlyphSets) || !SameSource(location, attachList) ||
            !SameSource(location, ligCaretList))
            return ReadWire.Fail(ReadStatus.InvalidSource, DiagnosticCode.WrongSourceFace,
                location.AtLocal(DataField.None, 0), out diagnostic);
        model = new(version, numGlyphs, glyphClasses, markAttachmentClasses, markGlyphSets,
            attachList, ligCaretList, null, 0, location, 0);
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

    // The v1.3 store boundary: a non-null raw store value is refused before any byte is
    // planned (UnsupportedOperation at the VariationStoreOffset slot; the raw value
    // travels in Detail - OQ-T4-02-5). ivs=0 compiles legally as version 1.3.
    private static ReadStatus CheckStore(GdefTable model, out FontDiagnostic diagnostic)
    {
        if (model.ItemVariationStoreOffset != 0)
            return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
                model.Location.AtLocal(DataField.Offset, GdefLayout.VariationStoreAt), out diagnostic,
                model.ItemVariationStoreOffset, 0);
        diagnostic = default;
        return ReadStatus.Done;
    }

    private static ReadStatus TryPlan(GdefTable model, out Plan? plan,
        out FontDiagnostic diagnostic, BuildBudget budget)
    {
        plan = null;
        ReadStatus status = CheckStore(model, out diagnostic);
        if (status != ReadStatus.Done) return status;
        int headerLength = model.Version == GdefLayout.Version13 ? GdefLayout.Header13
            : model.Version >= GdefLayout.Version12 ? GdefLayout.Header12 : GdefLayout.Header10;
        bool sharedClasses = model.MarkAttachmentClasses is not null &&
            ReferenceEquals(model.GlyphClasses, model.MarkAttachmentClasses);
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
        // candidate's Work stays charged and is never refunded (OQ-T4-02-4).
        foreach (bool largestLast in new[] { false, true })
        {
            status = Layout(children, headerLength, largestLast, model.Location, budget,
                out var candidate, out diagnostic);
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

    private static ReadStatus Layout(List<(object? Model, int Size)> children, int headerLength,
        bool largestLast, DataLocation location, BuildBudget budget, out Plan? plan,
        out FontDiagnostic diagnostic)
    {
        plan = null;
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
        plan = new Plan(cursor, offsets, blocks);
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
                // The store stays null on the static path; the Offset32 slot is zero.
                CoverageTableCompiler.RequireWritten(GdefView.TryWriteVariationStoreHeaderAt(destination,
                    GdefLayout.VariationStoreAt, new GdefView.VariationStoreHeaderData(-1, 0),
                    out _, location));
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
                default:
                    throw new InvalidOperationException("Unknown GDEF child block.");
            }
        }
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
