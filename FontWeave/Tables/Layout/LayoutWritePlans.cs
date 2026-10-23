using FontWeave.Core;

namespace FontWeave.Tables.Layout;

public static partial class LayoutListsTableCompiler
{
    private static readonly IEqualityComparer<byte[]> ParameterContent =
        EqualityComparer<byte[]>.Create(
            static (left, right) => ReferenceEquals(left, right) ||
                left is not null && right is not null && left.AsSpan().SequenceEqual(right),
            static payload =>
            {
                var hash = new HashCode();
                foreach (byte value in payload) hash.Add(value);
                return hash.ToHashCode();
            });

    private static readonly IEqualityComparer<LayoutLangSys> LanguageContent =
        EqualityComparer<LayoutLangSys>.Create(
            static (left, right) => ReferenceEquals(left, right) ||
                left is not null && right is not null &&
                left.RequiredFeatureIndex == right.RequiredFeatureIndex &&
                left.FeatureIndices.SequenceEqual(right.FeatureIndices),
            static language =>
            {
                var hash = new HashCode();
                hash.Add(language.RequiredFeatureIndex);
                foreach (ushort index in language.FeatureIndices) hash.Add(index);
                return hash.ToHashCode();
            });

    private static readonly IEqualityComparer<LayoutScript> ScriptContent =
        EqualityComparer<LayoutScript>.Create(
            static (left, right) =>
            {
                if (ReferenceEquals(left, right)) return true;
                if (left is null || right is null ||
                    !LanguageContent.Equals(left.DefaultLanguage, right.DefaultLanguage) ||
                    left.Languages.Length != right.Languages.Length) return false;
                for (int i = 0; i < left.Languages.Length; i++)
                    if (left.Languages[i].Tag != right.Languages[i].Tag ||
                        !LanguageContent.Equals(left.Languages[i].Language,
                            right.Languages[i].Language)) return false;
                return true;
            },
            static script =>
            {
                var hash = new HashCode();
                hash.Add(script.DefaultLanguage is not null);
                if (script.DefaultLanguage is { } defaultLanguage)
                    hash.Add(defaultLanguage, LanguageContent);
                foreach (var language in script.Languages)
                {
                    hash.Add(language.Tag);
                    hash.Add(language.Language, LanguageContent);
                }
                return hash.ToHashCode();
            });

    private static readonly IEqualityComparer<LayoutFeature> FeatureContent =
        EqualityComparer<LayoutFeature>.Create(
            static (left, right) => ReferenceEquals(left, right) ||
                left is not null && right is not null &&
                left.LookupIndices.SequenceEqual(right.LookupIndices) &&
                ParameterContent.Equals(left.ParamsPayload, right.ParamsPayload),
            static feature =>
            {
                var hash = new HashCode();
                foreach (ushort index in feature.LookupIndices) hash.Add(index);
                hash.Add(feature.ParamsPayload is not null);
                if (feature.ParamsPayload is { } payload)
                    hash.Add(payload, ParameterContent);
                return hash.ToHashCode();
            });

    internal sealed class ScriptBlock(LayoutScript model, int position, int[] languageTargets)
    {
        internal LayoutScript Model { get; } = model;
        internal int Position { get; } = position;
        internal int[] LanguageTargets { get; } = languageTargets;
        internal int DefaultTarget { get; set; }
    }

    internal sealed record LanguageBlock(LayoutLangSys Model, int Position);
    internal sealed record ScriptPlan(LayoutScript[] Records, int[] Targets, ScriptBlock[] Blocks,
        LanguageBlock[] Languages, int Length, DataLocation Location);
    internal sealed record FeatureBlock(LayoutFeature Model, int Position);
    internal sealed record ParamsBlock(byte[] Payload, int Position);
    internal sealed record FeaturePlan(LayoutFeature[] Records, int[] Targets, FeatureBlock[] Blocks,
        int Length, DataLocation Location, ParamsBlock[] Params);

    public static ReadStatus TryCompileScripts(ReadOnlySpan<LayoutScript> scripts, int featureCount,
        out byte[]? output, out FontDiagnostic diagnostic, BuildLimits? limits = null) =>
        CompileScripts(scripts, featureCount, out output, out diagnostic, new BuildBudget(limits));

    private static ReadStatus CompileScripts(ReadOnlySpan<LayoutScript> scripts, int featureCount,
        out byte[]? output, out FontDiagnostic diagnostic, BuildBudget budget)
    {
        output = null;
        ReadStatus status = PlanScripts(scripts, featureCount, out var plan, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        if (!budget.Work(plan!.Length, plan.Location, out diagnostic) ||
            !budget.Buffer(plan.Length, plan.Location, out diagnostic)) return ReadStatus.ResourceLimit;
        byte[] bytes = new byte[plan.Length];
        WriteScripts(plan, bytes);
        budget.RecordCompiledTable(bytes.Length); output = bytes;
        diagnostic = default; return ReadStatus.Done;
    }

    public static ReadStatus TryWriteScripts(ReadOnlySpan<LayoutScript> scripts, int featureCount,
        Span<byte> destination, out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        var budget = new BuildBudget(limits);
        ReadStatus status = PlanScripts(scripts, featureCount, out var plan, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        status = Destination(destination, plan!.Length, plan.Location, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (!budget.Work(plan.Length, plan.Location, out diagnostic)) return ReadStatus.ResourceLimit;
        WriteScripts(plan, destination); diagnostic = default; return ReadStatus.Done;
    }

    internal static ReadStatus PlanScripts(ReadOnlySpan<LayoutScript> input, int featureCount,
        out ScriptPlan? plan, out FontDiagnostic diagnostic, BuildBudget budget, DataLocation sourceFallback = default)
    {
        plan = null;
        DataLocation at = input.IsEmpty ? sourceFallback : input[0] is null ? default : input[0].ListLocation;
        ReadStatus status = CheckCount(input.Length, at, 0, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (!budget.Work(input.Length, at, out diagnostic) ||
            !budget.Buffer(8L * input.Length, at, out diagnostic)) return ReadStatus.ResourceLimit;
        var records = input.ToArray();
        status = ValidateScripts(records, featureCount, at, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        status = ScriptCandidate(records, false, out plan, out diagnostic, budget, at);
        if (status == ReadStatus.Done || status != ReadStatus.UnsupportedFormat) return status;
        // The only alternate profile is owner-local leaf copying. At most two
        // candidates; admission charges are cumulative and never retried away.
        return ScriptCandidate(records, true, out plan, out diagnostic, budget, at);
    }

    private static ReadStatus ScriptCandidate(LayoutScript[] records, bool ownerLocal,
        out ScriptPlan? plan, out FontDiagnostic diagnostic, BuildBudget budget, DataLocation location)
    {
        plan = null;
        long references = 0;
        foreach (var script in records) references += script.Languages.Length + (script.DefaultLanguage is null ? 0 : 1);
        if (!budget.Work(records.Length + references + 1, location, out diagnostic) ||
            !budget.Buffer(12L * records.Length, location, out diagnostic)) return ReadStatus.ResourceLimit;
        int cursor = LayoutTagListLayout.HeaderLength + LayoutTagListLayout.RecordStride * records.Length;
        if (!budget.Output(cursor, location, out diagnostic)) return ReadStatus.ResourceLimit;
        if (records.Length != 0 && cursor > ushort.MaxValue)
            return Width(cursor, records[0].RecordLocation.AtLocal(DataField.Offset,
                LayoutTagListLayout.RecordOffsetField, records[0].RecordLocation.RecordOrdinal), out diagnostic);
        var targets = new int[records.Length];
        var blocks = new List<ScriptBlock>();
        var leaves = new List<LanguageBlock>();
        var owners = new Dictionary<LayoutScript, ScriptBlock>(ScriptContent);
        var shared = new Dictionary<LayoutLangSys, LanguageBlock>(LanguageContent);
        for (int i = 0; i < records.Length; i++)
        {
            var script = records[i];
            long contentWork = script.DefaultLanguage?.FeatureIndices.Length ?? 0;
            foreach (var language in script.Languages)
                contentWork += language.Language.FeatureIndices.Length + 2L;
            if (!budget.Work(2 * contentWork + 2, script.Location, out diagnostic))
                return ReadStatus.ResourceLimit;
            if (owners.TryGetValue(script.PayloadOwner, out var previous))
            { targets[i] = previous.Position; continue; }
            if (cursor > ushort.MaxValue)
                return Width(cursor, script.RecordLocation.AtLocal(DataField.Offset,
                    LayoutTagListLayout.RecordOffsetField, script.RecordLocation.RecordOrdinal), out diagnostic);
            if (!budget.Buffer(4L * script.Languages.Length + 16, script.Location, out diagnostic)) return ReadStatus.ResourceLimit;
            var block = new ScriptBlock(script, cursor, new int[script.Languages.Length]);
            targets[i] = cursor;
            ReadStatus status = Advance(ref cursor, ScriptHeaderLength(script), script.Location, budget, out diagnostic);
            if (status != ReadStatus.Done) return status;
            blocks.Add(block); owners.Add(script.PayloadOwner, block);
            if (ownerLocal)
            {
                shared.Clear();
                status = PlaceLanguages(block, shared, leaves, ref cursor, budget, out diagnostic);
                if (status != ReadStatus.Done) return status;
            }
        }
        if (!ownerLocal)
        {
            foreach (var block in blocks)
            {
                ReadStatus status = PlaceLanguages(block, shared, leaves, ref cursor, budget, out diagnostic);
                if (status != ReadStatus.Done) return status;
            }
        }
        foreach (var block in blocks)
        {
            if (block.Model.DefaultLanguage is not null)
            {
                ReadStatus status = Relative(block.Position, block.DefaultTarget, block.Model.Location,
                    LayoutScriptLayout.DefaultOffsetField, -1, out diagnostic);
                if (status != ReadStatus.Done) return status;
            }
            for (int i = 0; i < block.LanguageTargets.Length; i++)
            {
                ReadStatus status = Relative(block.Position, block.LanguageTargets[i], block.Model.Location,
                    LayoutScriptLayout.HeaderLength + i * LayoutTagListLayout.RecordStride +
                        LayoutTagListLayout.RecordOffsetField, i, out diagnostic);
                if (status != ReadStatus.Done) return status;
            }
        }
        if (!budget.Buffer(8L * (blocks.Count + leaves.Count), location, out diagnostic)) return ReadStatus.ResourceLimit;
        plan = new(records, targets, blocks.ToArray(), leaves.ToArray(), cursor, location);
        diagnostic = default; return ReadStatus.Done;
    }

    private static ReadStatus PlaceLanguages(ScriptBlock owner, Dictionary<LayoutLangSys, LanguageBlock> shared,
        List<LanguageBlock> leaves, ref int cursor, BuildBudget budget, out FontDiagnostic diagnostic)
    {
        if (owner.Model.DefaultLanguage is LayoutLangSys defaultLanguage)
        {
            ReadStatus status = PlaceLanguage(defaultLanguage, shared, leaves, ref cursor, budget,
                out int target, out diagnostic);
            if (status != ReadStatus.Done) return status;
            owner.DefaultTarget = target;
        }
        for (int i = 0; i < owner.Model.Languages.Length; i++)
        {
            ReadStatus status = PlaceLanguage(owner.Model.Languages[i].Language, shared, leaves,
                ref cursor, budget, out owner.LanguageTargets[i], out diagnostic);
            if (status != ReadStatus.Done) return status;
        }
        diagnostic = default; return ReadStatus.Done;
    }

    private static ReadStatus PlaceLanguage(LayoutLangSys language, Dictionary<LayoutLangSys, LanguageBlock> shared,
        List<LanguageBlock> leaves, ref int cursor, BuildBudget budget, out int target, out FontDiagnostic diagnostic)
    {
        target = 0;
        if (!budget.Work(2L * language.FeatureIndices.Length + 2,
            language.Location, out diagnostic)) return ReadStatus.ResourceLimit;
        if (shared.TryGetValue(language, out var existing))
        { target = existing.Position; diagnostic = default; return ReadStatus.Done; }
        if (!budget.Buffer(16, language.Location, out diagnostic)) return ReadStatus.ResourceLimit;
        var block = new LanguageBlock(language, cursor);
        target = cursor;
        ReadStatus status = Advance(ref cursor, LanguageLength(language), language.Location, budget, out diagnostic);
        if (status != ReadStatus.Done) return status;
        leaves.Add(block); shared.Add(language, block);
        return ReadStatus.Done;
    }

    internal static void WriteScripts(ScriptPlan plan, Span<byte> destination)
    {
        CoverageTableCompiler.RequireWritten(LayoutTagListView.TryWriteCount(destination, (ushort)plan.Records.Length, out _, plan.Location));
        for (int i = 0; i < plan.Records.Length; i++)
            CoverageTableCompiler.RequireWritten(LayoutTagListView.TryWriteTagRecord(destination,
                (ulong)plan.Records.Length,
                new LayoutTagListView.TagRecordData(i, plan.Records[i].Tag, (ushort)plan.Targets[i]), out _, plan.Location));
        foreach (var block in plan.Blocks)
        {
            Span<byte> bytes = destination.Slice(block.Position);
            ushort defaultOffset = block.Model.DefaultLanguage is null ? (ushort)0 : (ushort)(block.DefaultTarget - block.Position);
            CoverageTableCompiler.RequireWritten(LayoutScriptView.TryWriteDefaultLanguageOffset(bytes, defaultOffset, out _, block.Model.Location));
            CoverageTableCompiler.RequireWritten(LayoutScriptView.TryWriteLanguageCount(bytes, (ushort)block.Model.Languages.Length, out _, block.Model.Location));
            for (int i = 0; i < block.Model.Languages.Length; i++)
                CoverageTableCompiler.RequireWritten(LayoutScriptView.TryWriteLanguageRecord(bytes,
                    (ulong)block.Model.Languages.Length,
                    new LayoutScriptView.LanguageRecordData(i, block.Model.Languages[i].Tag,
                        (ushort)(block.LanguageTargets[i] - block.Position)), out _, block.Model.Location));
        }
        foreach (var leaf in plan.Languages) WriteLanguage(leaf.Model, destination.Slice(leaf.Position));
    }

    private static void WriteLanguage(LayoutLangSys model, Span<byte> destination)
    {
        CoverageTableCompiler.RequireWritten(LayoutLangSysView.TryWriteLookupOrderOffset(destination, 0, out _, model.Location));
        CoverageTableCompiler.RequireWritten(LayoutLangSysView.TryWriteRequiredFeatureIndex(destination, model.RequiredFeatureIndex, out _, model.Location));
        CoverageTableCompiler.RequireWritten(LayoutLangSysView.TryWriteFeatureCount(destination, (ushort)model.FeatureIndices.Length, out _, model.Location));
        for (int i = 0; i < model.FeatureIndices.Length; i++)
            CoverageTableCompiler.RequireWritten(LayoutLangSysView.TryWriteFeatureIndexAt(destination,
                LayoutLangSysLayout.HeaderLength + (ulong)i * LayoutLangSysLayout.IndexStride,
                new LayoutLangSysView.FeatureIndexData(i, model.FeatureIndices[i]), out _, model.Location));
    }

    public static ReadStatus TryCompileFeatures(ReadOnlySpan<LayoutFeature> features, int lookupCount,
        out byte[]? output, out FontDiagnostic diagnostic, BuildLimits? limits = null) =>
        CompileFeatures(features, lookupCount, out output, out diagnostic, new BuildBudget(limits));

    private static ReadStatus CompileFeatures(ReadOnlySpan<LayoutFeature> features, int lookupCount,
        out byte[]? output, out FontDiagnostic diagnostic, BuildBudget budget)
    {
        output = null;
        ReadStatus status = PlanFeatures(features, lookupCount, out var plan, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        if (!budget.Work(plan!.Length, plan.Location, out diagnostic) ||
            !budget.Buffer(plan.Length, plan.Location, out diagnostic)) return ReadStatus.ResourceLimit;
        byte[] bytes = new byte[plan.Length]; WriteFeatures(plan, bytes);
        budget.RecordCompiledTable(bytes.Length); output = bytes;
        diagnostic = default; return ReadStatus.Done;
    }

    public static ReadStatus TryWriteFeatures(ReadOnlySpan<LayoutFeature> features, int lookupCount,
        Span<byte> destination, out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        var budget = new BuildBudget(limits);
        ReadStatus status = PlanFeatures(features, lookupCount, out var plan, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        status = Destination(destination, plan!.Length, plan.Location, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (!budget.Work(plan.Length, plan.Location, out diagnostic)) return ReadStatus.ResourceLimit;
        WriteFeatures(plan, destination); diagnostic = default; return ReadStatus.Done;
    }

    internal static ReadStatus PlanFeatures(ReadOnlySpan<LayoutFeature> input, int lookupCount,
        out FeaturePlan? plan, out FontDiagnostic diagnostic, BuildBudget budget, DataLocation sourceFallback = default)
    {
        plan = null;
        DataLocation at = input.IsEmpty ? sourceFallback : input[0] is null ? default : input[0].ListLocation;
        ReadStatus status = CheckCount(input.Length, at, 0, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (!budget.Work(2L * input.Length + 1, at, out diagnostic) ||
            !budget.Buffer(12L * input.Length, at, out diagnostic)) return ReadStatus.ResourceLimit;
        var records = input.ToArray();
        status = ValidateFeatures(records, lookupCount, at, true, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        int cursor = LayoutTagListLayout.HeaderLength + LayoutTagListLayout.RecordStride * records.Length;
        if (!budget.Output(cursor, at, out diagnostic)) return ReadStatus.ResourceLimit;
        var targets = new int[records.Length];
        var leaves = new List<FeatureBlock>();
        var shared = new Dictionary<LayoutFeature, FeatureBlock>(FeatureContent);
        for (int i = 0; i < records.Length; i++)
        {
            var feature = records[i];
            if (!budget.Work(2L * (feature.LookupIndices.Length +
                (feature.ParamsPayload?.Length ?? 0)) + 2, feature.Location,
                out diagnostic)) return ReadStatus.ResourceLimit;
            if (shared.TryGetValue(feature.PayloadOwner, out var existing))
            { targets[i] = existing.Position; continue; }
            if (cursor > ushort.MaxValue)
                return Width(cursor, feature.RecordLocation.AtLocal(DataField.Offset,
                    LayoutTagListLayout.RecordOffsetField, feature.RecordLocation.RecordOrdinal), out diagnostic);
            if (!budget.Buffer(16, feature.Location, out diagnostic)) return ReadStatus.ResourceLimit;
            var block = new FeatureBlock(feature, cursor); targets[i] = cursor;
            status = Advance(ref cursor, FeatureLength(feature), feature.Location, budget, out diagnostic);
            if (status != ReadStatus.Done) return status;
            leaves.Add(block); shared.Add(feature.PayloadOwner, block);
        }
        var paramsBlocks = new List<ParamsBlock>();
        var placed = new Dictionary<byte[], int>(ParameterContent);
        foreach (var block in leaves)
        {
            if (block.Model.ParamsPayload is not { } payload) continue;
            if (!budget.Work(5L * payload.Length + 1, block.Model.Location,
                out diagnostic)) return ReadStatus.ResourceLimit;
            if (!placed.TryGetValue(payload, out var paramsPosition))
            {
                if (cursor > ushort.MaxValue)
                    return Width(cursor, block.Model.Location.AtLocal(DataField.Offset, 0), out diagnostic);
                paramsPosition = cursor;
                status = Advance(ref cursor, payload.Length, block.Model.Location, budget, out diagnostic);
                if (status != ReadStatus.Done) return status;
                placed.Add(payload, paramsPosition);
                paramsBlocks.Add(new(payload, paramsPosition));
            }
        }
        foreach (var block in leaves)
        {
            if (block.Model.ParamsPayload is not { } payload) continue;
            int distance = placed[payload] - block.Position;
            if (distance > ushort.MaxValue)
                return Width(distance, block.Model.Location.AtLocal(DataField.Offset, 0), out diagnostic);
        }
        if (!budget.Buffer(8L * leaves.Count, at, out diagnostic)) return ReadStatus.ResourceLimit;
        plan = new(records, targets, leaves.ToArray(), cursor, at, paramsBlocks.ToArray());
        diagnostic = default; return ReadStatus.Done;
    }

    internal static void WriteFeatures(FeaturePlan plan, Span<byte> destination)
    {
        CoverageTableCompiler.RequireWritten(LayoutTagListView.TryWriteCount(destination, (ushort)plan.Records.Length, out _, plan.Location));
        for (int i = 0; i < plan.Records.Length; i++)
            CoverageTableCompiler.RequireWritten(LayoutTagListView.TryWriteTagRecord(destination,
                (ulong)plan.Records.Length,
                new LayoutTagListView.TagRecordData(i, plan.Records[i].Tag, (ushort)plan.Targets[i]), out _, plan.Location));
        var paramsAt = new Dictionary<byte[], int>(ParameterContent);
        foreach (var block in plan.Params) paramsAt.Add(block.Payload, block.Position);
        foreach (var block in plan.Blocks)
        {
            Span<byte> bytes = destination.Slice(block.Position);
            ushort paramsOffset = block.Model.ParamsPayload is { } payload
                ? (ushort)(paramsAt[payload] - block.Position)
                : (ushort)0;
            CoverageTableCompiler.RequireWritten(LayoutFeatureView.TryWriteParamsOffsetRaw(bytes, paramsOffset, out _, block.Model.Location));
            CoverageTableCompiler.RequireWritten(LayoutFeatureView.TryWriteLookupCount(bytes, (ushort)block.Model.LookupIndices.Length, out _, block.Model.Location));
            for (int i = 0; i < block.Model.LookupIndices.Length; i++)
                CoverageTableCompiler.RequireWritten(LayoutFeatureView.TryWriteLookupIndexAt(bytes,
                    LayoutFeatureLayout.HeaderLength + (ulong)i * LayoutFeatureLayout.IndexStride,
                    new LayoutFeatureView.LookupIndexData(i, block.Model.LookupIndices[i]), out _, block.Model.Location));
        }
        foreach (var paramsBlock in plan.Params)
            paramsBlock.Payload.CopyTo(destination.Slice(paramsBlock.Position));
    }

    private static int ScriptHeaderLength(LayoutScript model) =>
        LayoutScriptLayout.HeaderLength + LayoutTagListLayout.RecordStride * model.Languages.Length;
    private static int LanguageLength(LayoutLangSys model) =>
        LayoutLangSysLayout.HeaderLength + LayoutLangSysLayout.IndexStride * model.FeatureIndices.Length;
    private static int FeatureLength(LayoutFeature model) =>
        LayoutFeatureLayout.HeaderLength + LayoutFeatureLayout.IndexStride * model.LookupIndices.Length;

    private static ReadStatus Advance(ref int cursor, int length, DataLocation location,
        BuildBudget budget, out FontDiagnostic diagnostic)
    {
        if (!budget.Output((long)cursor + length, location, out diagnostic)) return ReadStatus.ResourceLimit;
        cursor += length; return ReadStatus.Done;
    }

    private static ReadStatus Relative(int owner, int target, DataLocation location, int field,
        int ordinal, out FontDiagnostic diagnostic)
    {
        if (target <= owner || target - owner > ushort.MaxValue)
            return Width(target < owner ? 0 : target - owner,
                location.AtLocal(DataField.Offset, (ulong)field, ordinal), out diagnostic);
        diagnostic = default; return ReadStatus.Done;
    }

    private static ReadStatus Width(int value, DataLocation location, out FontDiagnostic diagnostic) =>
        ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
            location, out diagnostic, (ulong)value, ushort.MaxValue);

    private static ReadStatus Destination(Span<byte> destination, int length, DataLocation location,
        out FontDiagnostic diagnostic)
    {
        if (destination.Length < length)
            return ReadWire.Fail(ReadStatus.DestinationTooSmall, DiagnosticCode.DestinationTooSmall,
                location.AtLocal(DataField.OutputBytes, 0), out diagnostic, (ulong)destination.Length, (ulong)length);
        diagnostic = default; return ReadStatus.Done;
    }
}
