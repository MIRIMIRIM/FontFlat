using FontWeave.Core;

namespace FontWeave.Tables.Layout;

public static class MarkGlyphSetsTableCompiler
{
    private sealed record Plan(uint[] Offsets, CoverageTable[] Unique, uint[] Targets, int UniqueCount, int Length);

    public static ReadStatus TryCreate(IReadOnlyList<CoverageTable> sets, int numGlyphs,
        out MarkGlyphSetsTable? model, out FontDiagnostic diagnostic, BuildLimits? limits = null,
        DataLocation location = default) =>
        TryCreate(sets, numGlyphs, out model, out diagnostic, new BuildBudget(limits), location);

    internal static ReadStatus TryCreate(IReadOnlyList<CoverageTable> sets, int numGlyphs,
        out MarkGlyphSetsTable? model, out FontDiagnostic diagnostic, BuildBudget budget,
        DataLocation location = default)
    {
        ArgumentNullException.ThrowIfNull(sets);
        model = null;
        ReadStatus status = CoverageTableCompiler.CheckNumGlyphs(numGlyphs, location, out diagnostic);
        if (status != ReadStatus.Done) return status;
        int count = sets.Count;
        if (count > ushort.MaxValue)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                location.AtLocal(DataField.Count, MarkGlyphSetsLayout.CountAt), out diagnostic,
                (ulong)count, ushort.MaxValue);
        if (!budget.Work(2L * count, location, out diagnostic) ||
            !budget.Buffer(8L * count, location, out diagnostic)) return ReadStatus.ResourceLimit;
        CoverageTable[] copy = count == 0 ? [] : new CoverageTable[count];
        for (int i = 0; i < copy.Length; i++)
        {
            CoverageTable? set = sets[i];
            if (set is null)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    location.AtLocal(DataField.Payload, MarkGlyphSetsLayout.HeaderLength +
                        (ulong)i * MarkGlyphSetsLayout.OffsetStride, i), out diagnostic);
            if (set.NumGlyphs != numGlyphs || set.Location.SourceId != location.SourceId ||
                set.Location.FaceIndex != location.FaceIndex ||
                location.SourceId != 0 && set.Location.TableTag != location.TableTag)
                return ReadWire.Fail(ReadStatus.InvalidSource, DiagnosticCode.WrongSourceFace,
                    location.AtLocal(DataField.None, 0, i), out diagnostic);
            copy[i] = set;
        }
        model = new(copy, numGlyphs, location, 0, []);
        diagnostic = default;
        return ReadStatus.Done;
    }

    public static ReadStatus TryCompile(MarkGlyphSetsTable model, out byte[]? output,
        out FontDiagnostic diagnostic, BuildLimits? limits = null) =>
        TryCompile(model, out output, out diagnostic, new BuildBudget(limits));

    internal static ReadStatus TryCompile(MarkGlyphSetsTable model, out byte[]? output,
        out FontDiagnostic diagnostic, BuildBudget budget)
    {
        ArgumentNullException.ThrowIfNull(model);
        output = null;
        ReadStatus status = TryPlan(model, out var plan, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        if (!budget.Buffer(plan!.Length, model.Location, out diagnostic)) return ReadStatus.ResourceLimit;
        byte[] bytes = new byte[plan.Length];
        WritePlanned(model, plan, bytes);
        budget.RecordCompiledTable(bytes.Length);
        output = bytes;
        diagnostic = default;
        return ReadStatus.Done;
    }

    public static ReadStatus TryWrite(MarkGlyphSetsTable model, Span<byte> destination,
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

    private static ReadStatus TryPlan(MarkGlyphSetsTable model, out Plan? plan,
        out FontDiagnostic diagnostic, BuildBudget budget)
    {
        plan = null;
        int count = model.Sets.Count;
        if (!budget.Work(count, model.Location, out diagnostic) ||
            !budget.Buffer(16L * count, model.Location, out diagnostic)) return ReadStatus.ResourceLimit;
        uint[] offsets = count == 0 ? [] : new uint[count];
        uint[] targets = count == 0 ? [] : new uint[count];
        CoverageTable[] unique = count == 0 ? [] : new CoverageTable[count];
        var cache = new Dictionary<CoverageTable, uint>(ReferenceEqualityComparer.Instance);
        long cursor = MarkGlyphSetsLayout.HeaderLength + MarkGlyphSetsLayout.OffsetStride * count;
        long expansionWork = 0;
        int distinct = 0;
        for (int i = 0; i < count; i++)
        {
            CoverageTable set = model.Sets[i];
            if (!cache.TryGetValue(set, out uint target))
            {
                if (cursor > uint.MaxValue)
                    return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
                        model.Location.AtLocal(DataField.Offset, MarkGlyphSetsLayout.HeaderLength +
                            (ulong)i * MarkGlyphSetsLayout.OffsetStride, i), out diagnostic, (ulong)cursor, uint.MaxValue);
                target = (uint)cursor;
                if (!budget.Buffer(16, model.Location, out diagnostic)) return ReadStatus.ResourceLimit;
                cache.Add(set, target);
                targets[distinct] = target;
                unique[distinct++] = set;
                cursor += CoverageTableCompiler.Measure(set);
                expansionWork += CoverageTableCompiler.ExpansionWriteWork(set);
            }
            offsets[i] = target;
        }
        if (!budget.Work(cursor + expansionWork, model.Location, out diagnostic) ||
            !budget.Output(cursor, model.Location, out diagnostic)) return ReadStatus.ResourceLimit;
        plan = new(offsets, unique, targets, distinct, (int)cursor);
        diagnostic = default;
        return ReadStatus.Done;
    }

    private static void WritePlanned(MarkGlyphSetsTable model, Plan plan, Span<byte> destination)
    {
        CoverageTableCompiler.RequireWritten(MarkGlyphSetsView.TryWriteFormat(destination, 1, out _, model.Location));
        CoverageTableCompiler.RequireWritten(MarkGlyphSetsView.TryWriteSetCount(destination,
            (ushort)model.Sets.Count, out _, model.Location));
        for (int i = 0; i < plan.Offsets.Length; i++)
            CoverageTableCompiler.RequireWritten(MarkGlyphSetsView.TryWriteCoverageOffsetRecordAt(destination,
                MarkGlyphSetsLayout.HeaderLength + (ulong)i * MarkGlyphSetsLayout.OffsetStride,
                new MarkGlyphSetsView.CoverageOffsetRecordData(i, plan.Offsets[i]), out _, model.Location));
        for (int i = 0; i < plan.UniqueCount; i++)
        {
            CoverageTable set = plan.Unique[i];
            CoverageTableCompiler.WritePlanned(set,
                destination.Slice((int)plan.Targets[i], CoverageTableCompiler.Measure(set)));
        }
    }
}
