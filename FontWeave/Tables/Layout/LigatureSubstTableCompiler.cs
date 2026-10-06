using FontWeave.Core;

namespace FontWeave.Tables.Layout;

// The three-level LigatureSubst compiler: subtable -> sets -> ligature rules. The
// layout is deterministic (subtable header, set offsets, set bodies, coverage last;
// inside a set: count, ligature offsets, rule bodies), every offset family goes
// through its generated fixup helper, and a too-narrow target is the registered
// finite refusal (OQ-T4-03-6: no automatic split). Coverage index i corresponds to
// set i on the write side by construction.
internal sealed record LigaturePartPlan(int Length, ushort CoverageFormat,
    int CoverageRangeCount, ushort CoverageOffset);

public static class LigatureSubstTableCompiler
{
    public static ReadStatus TryCreate(ReadOnlySpan<ushort> glyphs, ReadOnlySpan<LigatureSetTable> sets,
        int numGlyphs, out LigatureSubstTable? model, out FontDiagnostic diagnostic,
        BuildLimits? limits = null, DataLocation location = default, ushort coverageFormat = 1) =>
        TryCreate(glyphs, sets, numGlyphs, out model, out diagnostic, new BuildBudget(limits),
            location, coverageFormat);

    internal static ReadStatus TryCreate(ReadOnlySpan<ushort> glyphs, ReadOnlySpan<LigatureSetTable> sets,
        int numGlyphs, out LigatureSubstTable? model, out FontDiagnostic diagnostic,
        BuildBudget budget, DataLocation location = default, ushort coverageFormat = 1)
    {
        model = null;
        ReadStatus status = CoverageTableCompiler.CheckNumGlyphs(numGlyphs, location, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (glyphs.Length != sets.Length)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                location.AtLocal(DataField.Count, LigatureSubstLayout.CountAt),
                out diagnostic, (ulong)sets.Length, (ulong)glyphs.Length);
        if (glyphs.Length > ushort.MaxValue)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                location.AtLocal(DataField.Count, LigatureSubstLayout.CountAt),
                out diagnostic, (ulong)glyphs.Length, ushort.MaxValue);
        long rules = 0, components = 0;
        for (int i = 0; i < sets.Length; i++)
        {
            rules += sets[i].LigatureCount;
            foreach (var ligature in sets[i].Ligatures) components += ligature.Components.Length;
        }
        if (!budget.Work(glyphs.Length + rules + components + 1, location, out diagnostic)) return ReadStatus.ResourceLimit;
        for (int i = 0; i < sets.Length; i++)
        {
            LigatureSetTable set = sets[i];
            if (set.LigatureCount > ushort.MaxValue)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    location.AtLocal(DataField.Count, LigatureSetLayout.CountAt, i),
                    out diagnostic, (ulong)set.LigatureCount, (ulong)ushort.MaxValue);
            foreach (var ligature in set.Ligatures)
            {
                if (ligature.LigatureGlyph >= numGlyphs)
                    return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                        ligature.Location.AtLocal(DataField.GlyphId, LigatureLayout.LigatureGlyphAt),
                        out diagnostic, ligature.LigatureGlyph, (ulong)numGlyphs);
                for (int j = 0; j < ligature.Components.Length; j++)
                    if (ligature.Components[j] >= numGlyphs)
                        return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                            ligature.Location.AtLocal(DataField.GlyphId,
                                LigatureLayout.HeaderLength + (ulong)j * LigatureLayout.ComponentStride, j),
                            out diagnostic, ligature.Components[j], (ulong)numGlyphs);
            }
        }
        status = CoverageTableCompiler.TryCreate(glyphs, numGlyphs, coverageFormat, out var coverage,
            out diagnostic, budget, location);
        if (status != ReadStatus.Done) return status;
        if (!budget.Buffer(rules * 8L + components * 2L + 8L * sets.Length, location, out diagnostic)) return ReadStatus.ResourceLimit;
        var copy = new LigatureSetTable[sets.Length];
        long consumed = LigatureSubstLayout.MinimumHeader + LigatureSubstLayout.OffsetStride * (long)sets.Length;
        for (int i = 0; i < sets.Length; i++)
        {
            // Set wire length: count + offsets + rule bodies; the consumed length is
            // recomputed here so a caller-built set cannot smuggle a stale one in.
            long setLength = LigatureSetLayout.HeaderLength + LigatureSetLayout.OffsetStride * sets[i].LigatureCount;
            foreach (var ligature in sets[i].Ligatures)
                setLength += LigatureLayout.HeaderLength + 2L * ligature.Components.Length;
            copy[i] = new(sets[i].Ligatures.ToArray(), sets[i].Location, (int)setLength);
            consumed += setLength;
        }
        consumed += CoverageTableCompiler.Measure(coverage!);
        model = new(coverage!, copy, location, (int)consumed);
        diagnostic = default;
        return ReadStatus.Done;
    }

    public static ReadStatus TryCompile(LigatureSubstTable model, out byte[]? output,
        out FontDiagnostic diagnostic, BuildLimits? limits = null) =>
        TryCompile(model, out output, out diagnostic, new BuildBudget(limits));

    internal static ReadStatus TryCompile(LigatureSubstTable model, out byte[]? output,
        out FontDiagnostic diagnostic, BuildBudget budget)
    {
        ArgumentNullException.ThrowIfNull(model);
        output = null;
        ReadStatus status = TryPlanSubtables(model, out var plans, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        int total = plans![0].Length;
        if (!budget.Buffer(total, model.Location, out diagnostic)) return ReadStatus.ResourceLimit;
        byte[] bytes = new byte[total];
        WritePlanned(model, plans[0], bytes);
        budget.RecordCompiledTable(bytes.Length);
        output = bytes;
        diagnostic = default;
        return ReadStatus.Done;
    }

    public static ReadStatus TryWrite(LigatureSubstTable model, Span<byte> destination,
        out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        var budget = new BuildBudget(limits);
        ReadStatus status = TryPlanSubtables(model, out var plans, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        if (destination.Length < plans![0].Length)
            return ReadWire.Fail(ReadStatus.DestinationTooSmall, DiagnosticCode.DestinationTooSmall,
                model.Location.AtLocal(DataField.OutputBytes, 0), out diagnostic,
                (ulong)destination.Length, (ulong)plans[0].Length);
        WritePlanned(model, plans[0], destination);
        diagnostic = default;
        return ReadStatus.Done;
    }

    internal static ReadStatus TryPlanSubtables(LigatureSubstTable model, out LigaturePartPlan[]? plans,
        out FontDiagnostic diagnostic, BuildBudget budget)
    {
        plans = null;
        int setCount = model.SetCount;
        if (!budget.Work(32L * setCount + 1, model.Location, out diagnostic)) return ReadStatus.ResourceLimit;
        long setBodies = 0;
        for (int i = 0; i < setCount; i++) setBodies += SetLength(model.GetSet(i));
        int coverageAt = LigatureSubstLayout.MinimumHeader +
            LigatureSubstLayout.OffsetStride * setCount + (int)setBodies;
        ReadStatus status = LigatureSubstView.TryGetCoverageOffsetValue(present: true, origin: 0UL,
            target: (ulong)coverageAt, out ushort coverageOffset, out diagnostic,
            model.Location.AtLocal(DataField.Offset, LigatureSubstLayout.CoverageOffsetField));
        if (status != ReadStatus.Done) return status;
        long cursor = LigatureSubstLayout.MinimumHeader + LigatureSubstLayout.OffsetStride * setCount;
        for (int i = 0; i < setCount; i++)
        {
            status = LigatureSubstView.TryGetLigatureSetOffsetValue(present: true, origin: 0UL,
                target: (ulong)cursor, out _, out diagnostic,
                model.Location.AtLocal(DataField.Offset,
                    LigatureSubstLayout.MinimumHeader + (ulong)i * LigatureSubstLayout.OffsetStride, i));
            if (status != ReadStatus.Done) return status;
            status = PlanSet(model.GetSet(i), model.Location, budget, out diagnostic);
            if (status != ReadStatus.Done) return status;
            cursor += SetLength(model.GetSet(i));
        }
        var plan = new LigaturePartPlan(coverageAt + CoverageTableCompiler.Measure(model.Coverage),
            model.Coverage.Format, model.Coverage.RangeCount, coverageOffset);
        if (!budget.Work(EmissionWork(plan), model.Location, out diagnostic) ||
            !budget.Output(plan.Length, model.Location, out diagnostic)) return ReadStatus.ResourceLimit;
        plans = [plan];
        diagnostic = default;
        return ReadStatus.Done;
    }

    // One set's internal layout: ligature offsets relative to the SET start, rule
    // bodies after the offset array. Each target is a set-local Offset16, so an
    // oversized set refuses through the generated helpers at its own field.
    private static ReadStatus PlanSet(LigatureSetTable set, DataLocation ownerLocation,
        BuildBudget budget, out FontDiagnostic diagnostic)
    {
        int ruleCount = set.LigatureCount;
        long rulesEnd = LigatureSetLayout.HeaderLength + LigatureSetLayout.OffsetStride * ruleCount;
        long cursor = rulesEnd;
        for (int i = 0; i < ruleCount; i++)
        {
            ReadStatus status = LigatureSetView.TryGetLigatureOffsetValue(present: true, origin: 0UL,
                target: (ulong)cursor, out _, out diagnostic,
                set.Location.AtLocal(DataField.Offset,
                    LigatureSetLayout.HeaderLength + (ulong)i * LigatureSetLayout.OffsetStride, i));
            if (status != ReadStatus.Done) return status;
            cursor += LigatureLayout.HeaderLength + 2L * set.Ligatures[i].Components.Length;
        }
        return Work(budget, set, cursor, out diagnostic);
    }

    private static long SetLength(LigatureSetTable set)
    {
        long length = LigatureSetLayout.HeaderLength + LigatureSetLayout.OffsetStride * set.LigatureCount;
        foreach (var ligature in set.Ligatures)
            length += LigatureLayout.HeaderLength + 2L * ligature.Components.Length;
        return length;
    }

    private static ReadStatus Work(BuildBudget budget, LigatureSetTable set, long length, out FontDiagnostic diagnostic) =>
        budget.Work(length, set.Location, out diagnostic) ? ReadStatus.Done : ReadStatus.ResourceLimit;

    internal static void WritePlanned(LigatureSubstTable model, LigaturePartPlan plan, Span<byte> destination)
    {
        int setCount = model.SetCount;
        CoverageTableCompiler.RequireWritten(LigatureSubstView.TryWriteSubstFormat(destination, 1, out _, model.Location));
        CoverageTableCompiler.RequireWritten(LigatureSubstView.TryWriteCoverageOffset(destination,
            plan.CoverageOffset, out _, model.Location));
        CoverageTableCompiler.RequireWritten(LigatureSubstView.TryWriteLigatureSetCount(destination,
            (ushort)setCount, out _, model.Location));
        long cursor = LigatureSubstLayout.MinimumHeader + LigatureSubstLayout.OffsetStride * setCount;
        for (int i = 0; i < setCount; i++)
        {
            CoverageTableCompiler.RequireWritten(LigatureSubstView.TryWriteLigatureSetOffsetRecordAt(destination,
                LigatureSubstLayout.MinimumHeader + (ulong)i * LigatureSubstLayout.OffsetStride,
                new LigatureSubstView.LigatureSetOffsetRecordData(i, (ushort)cursor), out _, model.Location));
            WriteSet(model.GetSet(i), destination.Slice((int)cursor), model.Location);
            cursor += SetLength(model.GetSet(i));
        }
        CoverageTableCompiler.WritePlanned(model.Coverage.Glyphs, plan.CoverageFormat,
            plan.CoverageRangeCount, destination.Slice(plan.CoverageOffset), model.Location);
    }

    private static void WriteSet(LigatureSetTable set, Span<byte> destination, DataLocation ownerLocation)
    {
        int ruleCount = set.LigatureCount;
        CoverageTableCompiler.RequireWritten(LigatureSetView.TryWriteLigatureCount(destination,
            (ushort)ruleCount, out _, set.Location));
        long cursor = LigatureSetLayout.HeaderLength + LigatureSetLayout.OffsetStride * ruleCount;
        for (int i = 0; i < ruleCount; i++)
        {
            CoverageTableCompiler.RequireWritten(LigatureSetView.TryWriteLigatureOffsetRecordAt(destination,
                LigatureSetLayout.HeaderLength + (ulong)i * LigatureSetLayout.OffsetStride,
                new LigatureSetView.LigatureOffsetRecordData(i, (ushort)cursor), out _, set.Location));
            LigatureTable ligature = set.Ligatures[i];
            Span<byte> body = destination.Slice((int)cursor);
            CoverageTableCompiler.RequireWritten(LigatureView.TryWriteLigatureGlyph(body,
                ligature.LigatureGlyph, out _, ligature.Location));
            CoverageTableCompiler.RequireWritten(LigatureView.TryWriteComponentCount(body,
                (ushort)ligature.ComponentCount, out _, ligature.Location));
            for (int j = 0; j < ligature.Components.Length; j++)
                CoverageTableCompiler.RequireWritten(LigatureView.TryWriteComponentRecordAt(body,
                    LigatureLayout.HeaderLength + (ulong)j * LigatureLayout.ComponentStride,
                    new LigatureView.ComponentRecordData(j, ligature.Components[j]), out _, ligature.Location));
            cursor += LigatureLayout.HeaderLength + 2L * ligature.Components.Length;
        }
    }

    private static long EmissionWork(LigaturePartPlan plan) => plan.Length +
        (plan.CoverageFormat == 2 ? (long)plan.CoverageRangeCount * 2 : 0);
}
