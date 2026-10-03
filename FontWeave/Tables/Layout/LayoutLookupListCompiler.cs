using FontWeave.Core;

namespace FontWeave.Tables.Layout;

public static partial class LayoutListsTableCompiler
{
    private sealed record LookupBlock(LookupTable Model, byte[] Bytes, int Position);
    private sealed record LookupPlan(LookupTable[] Records, int[] Targets, LookupBlock[] Blocks,
        int Length, DataLocation Location);

    public static ReadStatus TryCompileLookups(ReadOnlySpan<LookupTable> lookups, GdefLookupContext? context,
        out byte[]? output, out FontDiagnostic diagnostic, BuildLimits? limits = null) =>
        CompileLookups(lookups, context, out output, out diagnostic, new BuildBudget(limits));

    private static ReadStatus CompileLookups(ReadOnlySpan<LookupTable> lookups, GdefLookupContext? context,
        out byte[]? output, out FontDiagnostic diagnostic, BuildBudget budget)
    {
        output = null;
        ReadStatus status = PlanLookups(lookups, context, out var plan, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        if (!budget.Work(plan!.Length, plan.Location, out diagnostic) ||
            !budget.Buffer(plan.Length, plan.Location, out diagnostic)) return ReadStatus.ResourceLimit;
        byte[] bytes = new byte[plan.Length]; WriteLookups(plan, bytes);
        budget.RecordCompiledTable(bytes.Length); output = bytes;
        diagnostic = default; return ReadStatus.Done;
    }

    public static ReadStatus TryWriteLookups(ReadOnlySpan<LookupTable> lookups, GdefLookupContext? context,
        Span<byte> destination, out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        var budget = new BuildBudget(limits);
        ReadStatus status = PlanLookups(lookups, context, out var plan, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        status = Destination(destination, plan!.Length, plan.Location, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (!budget.Work(plan.Length, plan.Location, out diagnostic)) return ReadStatus.ResourceLimit;
        WriteLookups(plan, destination); diagnostic = default; return ReadStatus.Done;
    }

    private static ReadStatus PlanLookups(ReadOnlySpan<LookupTable> input, GdefLookupContext? context,
        out LookupPlan? plan, out FontDiagnostic diagnostic, BuildBudget budget, DataLocation sourceFallback = default)
    {
        plan = null;
        DataLocation at = input.IsEmpty ? sourceFallback : input[0] is null ? default : input[0].Location;
        ReadStatus status = CheckCount(input.Length, at, 0, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (!budget.Work(2L * input.Length + 1, at, out diagnostic) ||
            !budget.Buffer(16L * input.Length, at, out diagnostic)) return ReadStatus.ResourceLimit;
        var records = input.ToArray();
        int cursor = LayoutLookupListLayout.HeaderLength + LayoutLookupListLayout.OffsetStride * records.Length;
        if (!budget.Output(cursor, at, out diagnostic)) return ReadStatus.ResourceLimit;
        if (records.Length != 0 && cursor > ushort.MaxValue)
            return Width(cursor, at.AtLocal(DataField.Offset, LayoutLookupListLayout.HeaderLength), out diagnostic);
        var unique = new List<(LookupTable Model, byte[] Bytes)>();
        var indexes = new Dictionary<LookupTable, int>();
        var recordIndexes = new int[records.Length];
        for (int i = 0; i < records.Length; i++)
        {
            LookupTable? model = records[i];
            if (model is null) return InvalidModel(at, i, out diagnostic);
            if (!SameSource(at, model.Location) || records.Length != 0 &&
                (model.NumGlyphs != records[0].NumGlyphs || model.Kind != records[0].Kind))
                return ReadWire.Fail(ReadStatus.InvalidSource, DiagnosticCode.WrongSourceFace, model.Location, out diagnostic);
            if (indexes.TryGetValue(model, out int existing))
            { recordIndexes[i] = existing; continue; }
            // This is typed compilation, not a copy of SourceSubtableOffsets or opaque bytes.
            status = LookupTableCompiler.TryCompile(model, context, out byte[]? bytes, out diagnostic, budget);
            if (status != ReadStatus.Done) return status;
            if (!budget.Buffer(16, model.Location, out diagnostic)) return ReadStatus.ResourceLimit;
            recordIndexes[i] = unique.Count; indexes.Add(model, unique.Count); unique.Add((model, bytes!));
        }
        if (!budget.Work(unique.Count, at, out diagnostic) ||
            !budget.Buffer(12L * unique.Count + 4L * records.Length, at, out diagnostic)) return ReadStatus.ResourceLimit;
        var positions = new int[unique.Count];
        var blocks = new LookupBlock[unique.Count];
        int largest = -1;
        for (int i = 0; i < unique.Count; i++)
            if (largest < 0 || unique[i].Bytes.Length > unique[largest].Bytes.Length) largest = i;
        // Physical payload order may change; record/index order never changes.
        // Put one largest leaf last so its body may cross 64K without pushing
        // another Lookup's Offset16 target out of range.
        int blockAt = 0;
        for (int i = 0; i < unique.Count; i++)
        {
            if (i == largest) continue;
            status = PlaceLookup(i, unique, positions, blocks, ref blockAt, ref cursor, at, budget, out diagnostic);
            if (status != ReadStatus.Done) return status;
        }
        if (largest >= 0)
        {
            status = PlaceLookup(largest, unique, positions, blocks, ref blockAt, ref cursor, at, budget, out diagnostic);
            if (status != ReadStatus.Done) return status;
        }
        var targets = new int[records.Length];
        for (int i = 0; i < targets.Length; i++) targets[i] = positions[recordIndexes[i]];
        plan = new(records, targets, blocks, cursor, at);
        diagnostic = default; return ReadStatus.Done;
    }

    private static ReadStatus PlaceLookup(int index, List<(LookupTable Model, byte[] Bytes)> unique,
        int[] positions, LookupBlock[] blocks, ref int blockAt, ref int cursor, DataLocation at,
        BuildBudget budget, out FontDiagnostic diagnostic)
    {
        if (cursor > ushort.MaxValue)
            return Width(cursor, at.AtLocal(DataField.Offset, LayoutLookupListLayout.HeaderLength, index), out diagnostic);
        var item = unique[index]; positions[index] = cursor;
        blocks[blockAt++] = new(item.Model, item.Bytes, cursor);
        return Advance(ref cursor, item.Bytes.Length, item.Model.Location, budget, out diagnostic);
    }

    private static void WriteLookups(LookupPlan plan, Span<byte> destination)
    {
        CoverageTableCompiler.RequireWritten(LayoutLookupListView.TryWriteCount(destination, (ushort)plan.Records.Length, out _, plan.Location));
        for (int i = 0; i < plan.Records.Length; i++)
            CoverageTableCompiler.RequireWritten(LayoutLookupListView.TryWriteLookupOffsetAt(destination,
                LayoutLookupListLayout.HeaderLength + (ulong)i * LayoutLookupListLayout.OffsetStride,
                new LayoutLookupListView.LookupOffsetData(i, (ushort)plan.Targets[i]), out _, plan.Location));
        foreach (var block in plan.Blocks) block.Bytes.CopyTo(destination.Slice(block.Position));
    }

    /// <summary>Compiles three static fragments together, not a GSUB/GPOS root. Every
    /// dependency, target width and shared budget is admitted before publication.</summary>
    public static ReadStatus TryCompile(LayoutListsTable model, GdefLookupContext? context,
        out LayoutListFragments? output, out FontDiagnostic diagnostic, BuildLimits? limits = null) =>
        TryCompile(model, context, out output, out diagnostic, new BuildBudget(limits));

    internal static ReadStatus TryCompile(LayoutListsTable model, GdefLookupContext? context,
        out LayoutListFragments? output, out FontDiagnostic diagnostic, BuildBudget budget)
    {
        ArgumentNullException.ThrowIfNull(model);
        output = null;
        if (model.FeatureVariationsOffsetRaw != 0)
            return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
                model.Location.AtLocal(DataField.Offset, LayoutListsLayout.VariationsOffsetField),
                out diagnostic, model.FeatureVariationsOffsetRaw, 0);
        ReadStatus status = model.Kind == LayoutLookupKind.Gsub
            ? LookupTable.ValidateGsubSource(model.Location, out diagnostic)
            : LookupTable.ValidateGposSource(model.Location, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = PlanScripts(model.Scripts, model.Features.Length, out var scripts, out diagnostic, budget, model.Location);
        if (status != ReadStatus.Done) return status;
        status = PlanFeatures(model.Features, model.Lookups.Length, out var features, out diagnostic, budget, model.Location);
        if (status != ReadStatus.Done) return status;
        status = PlanLookups(model.Lookups, context, out var lookups, out diagnostic, budget, model.Location);
        if (status != ReadStatus.Done) return status;
        long total = (long)scripts!.Length + features!.Length + lookups!.Length;
        if (!budget.Output(total, model.Location, out diagnostic) ||
            !budget.Work(total, model.Location, out diagnostic) ||
            !budget.Buffer(total, model.Location, out diagnostic)) return ReadStatus.ResourceLimit;
        byte[] scriptBytes = new byte[scripts.Length], featureBytes = new byte[features.Length], lookupBytes = new byte[lookups.Length];
        WriteScripts(scripts, scriptBytes); WriteFeatures(features, featureBytes); WriteLookups(lookups, lookupBytes);
        budget.RecordCompiledTable(scriptBytes.Length); budget.RecordCompiledTable(featureBytes.Length); budget.RecordCompiledTable(lookupBytes.Length);
        output = new(scriptBytes, featureBytes, lookupBytes);
        diagnostic = default; return ReadStatus.Done;
    }
}
