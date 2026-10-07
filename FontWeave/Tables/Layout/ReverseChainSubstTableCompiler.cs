using System.Buffers.Binary;
using FontWeave.Core;

namespace FontWeave.Tables.Layout;

// Immutable fragment plan: one reverse-chain subtable is one part. Canonical layout:
// header, backtrack offset records, backtrack coverage bodies, then the hand-walked
// lookahead chain (offsets, bodies), then the substitutes. A single subtable over
// 64K has no fallback to save it - an extension wrapper does not shrink the body
// past its own 8-byte header, so the overflow is the registered finite refusal
// (OQ-T4-04-10's wrap-all boundary note).
internal sealed record ReverseChainSubstPartPlan(int Length, ushort CoverageFormat,
    int CoverageRangeCount, ushort CoverageOffset, int BacktrackAt);

public static class ReverseChainSubstTableCompiler
{
    // One reverse-chain subtable: the substitutes/coverage identity is the
    // SingleSubst format-2 shape (one substitute per coverage individual) and every
    // substitute stays inside the glyph domain; empty backtrack/lookahead chains are
    // legal (the gsub8 pin's glyphCount=1 zero-chain subtable).
    public static ReadStatus TryCreate(CoverageTable coverage,
        IReadOnlyList<CoverageTable> backtrackCoverages, IReadOnlyList<CoverageTable> lookaheadCoverages,
        ReadOnlySpan<ushort> substitutes, int numGlyphs, out ReverseChainSubstTable? model,
        out FontDiagnostic diagnostic, BuildLimits? limits = null, DataLocation location = default) =>
        TryCreate(coverage, backtrackCoverages, lookaheadCoverages, substitutes, numGlyphs,
            out model, out diagnostic, new BuildBudget(limits), location);

    internal static ReadStatus TryCreate(CoverageTable coverage,
        IReadOnlyList<CoverageTable> backtrackCoverages, IReadOnlyList<CoverageTable> lookaheadCoverages,
        ReadOnlySpan<ushort> substitutes, int numGlyphs, out ReverseChainSubstTable? model,
        out FontDiagnostic diagnostic, BuildBudget budget, DataLocation location = default)
    {
        model = null;
        ReadStatus status = CoverageTableCompiler.CheckNumGlyphs(numGlyphs, location, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (coverage.NumGlyphs != numGlyphs || coverage.Location.SourceId != location.SourceId ||
            coverage.Location.FaceIndex != location.FaceIndex)
            return ReadWire.Fail(ReadStatus.InvalidSource, DiagnosticCode.WrongSourceFace,
                coverage.Location, out diagnostic);
        if (backtrackCoverages.Count > ushort.MaxValue || lookaheadCoverages.Count > ushort.MaxValue)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                location.AtLocal(DataField.Count, 4), out diagnostic,
                (ulong)Math.Max(backtrackCoverages.Count, lookaheadCoverages.Count),
                (ulong)ushort.MaxValue);
        if (substitutes.Length != coverage.Glyphs.Length)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                location.AtLocal(DataField.Count, 0), out diagnostic,
                (ulong)substitutes.Length, (ulong)coverage.Glyphs.Length);
        if (!budget.Work(backtrackCoverages.Count + lookaheadCoverages.Count + substitutes.Length,
            location, out diagnostic)) return ReadStatus.ResourceLimit;
        for (int i = 0; i < substitutes.Length; i++)
        {
            if (substitutes[i] >= numGlyphs)
                return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                    location.AtLocal(DataField.GlyphId, (ulong)i, i), out diagnostic,
                    substitutes[i], (ulong)numGlyphs);
        }
        var backtrack = CopyCoverages(backtrackCoverages, numGlyphs, location, out status, out diagnostic);
        if (status != ReadStatus.Done) return status;
        var lookahead = CopyCoverages(lookaheadCoverages, numGlyphs, location, out status, out diagnostic);
        if (status != ReadStatus.Done) return status;
        long consumed = ReverseChainSubstLayout.BacktrackArrayAt + 2L * backtrackCoverages.Count;
        foreach (var chain in backtrack) consumed += CoverageTableCompiler.Measure(chain);
        consumed += 2;
        foreach (var chain in lookahead) consumed += 2 + CoverageTableCompiler.Measure(chain);
        consumed += 2 + 2L * substitutes.Length;
        if (!budget.Buffer(8L * backtrackCoverages.Count + 8L * lookaheadCoverages.Count +
            2L * substitutes.Length + 16, location, out diagnostic)) return ReadStatus.ResourceLimit;
        model = new(coverage, backtrack, lookahead, substitutes.ToArray(), numGlyphs,
            location, (int)consumed);
        diagnostic = default;
        return ReadStatus.Done;
    }

    private static CoverageTable[] CopyCoverages(IReadOnlyList<CoverageTable> coverages,
        int numGlyphs, DataLocation location, out ReadStatus status, out FontDiagnostic diagnostic)
    {
        var copy = new CoverageTable[coverages.Count];
        for (int i = 0; i < coverages.Count; i++)
        {
            var coverage = coverages[i];
            if (coverage is null || coverage.NumGlyphs != numGlyphs ||
                coverage.Location.SourceId != location.SourceId || coverage.Location.FaceIndex != location.FaceIndex)
            {
                status = ReadWire.Fail(ReadStatus.InvalidSource, DiagnosticCode.WrongSourceFace,
                    coverage?.Location ?? location, out diagnostic);
                return copy;
            }
            copy[i] = coverage;
        }
        status = ReadStatus.Done;
        diagnostic = default;
        return copy;
    }

    public static ReadStatus TryCompile(ReverseChainSubstTable model, out byte[]? output,
        out FontDiagnostic diagnostic, BuildLimits? limits = null) =>
        TryCompile(model, out output, out diagnostic, new BuildBudget(limits));

    internal static ReadStatus TryCompile(ReverseChainSubstTable model, out byte[]? output,
        out FontDiagnostic diagnostic, BuildBudget budget)
    {
        ArgumentNullException.ThrowIfNull(model);
        output = null;
        ReadStatus status = TryPlanSubtables(model, out var plans, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        if (!budget.Buffer(plans![0].Length, model.Location, out diagnostic)) return ReadStatus.ResourceLimit;
        byte[] bytes = new byte[plans[0].Length];
        WritePlanned(model, plans[0], bytes);
        budget.RecordCompiledTable(bytes.Length);
        output = bytes;
        diagnostic = default;
        return ReadStatus.Done;
    }

    public static ReadStatus TryWrite(ReverseChainSubstTable model, Span<byte> destination,
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

    internal static ReadStatus TryPlanSubtables(ReverseChainSubstTable model,
        out ReverseChainSubstPartPlan[]? plans, out FontDiagnostic diagnostic, BuildBudget budget)
    {
        plans = null;
        int backtrackCount = model.BacktrackCoverages.Count;
        int lookaheadCount = model.LookaheadCoverages.Count;
        long bytes = ReverseChainSubstLayout.BacktrackArrayAt + 2L * backtrackCount;
        foreach (var chain in model.BacktrackCoverages)
            bytes += CoverageTableCompiler.Measure(chain);
        bytes += 2;
        foreach (var chain in model.LookaheadCoverages)
            bytes += 2 + CoverageTableCompiler.Measure(chain);
        bytes += 2 + 2L * model.Substitutes.Length;
        if (bytes > ushort.MaxValue)
            return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
                model.Location.AtLocal(DataField.Offset, ReverseChainSubstLayout.CoverageOffsetField),
                out diagnostic, (ulong)bytes, (ulong)ushort.MaxValue);
        if (!budget.Work(bytes, model.Location, out diagnostic) ||
            !budget.Output(bytes, model.Location, out diagnostic)) return ReadStatus.ResourceLimit;
        // Canonical order: the coverage body is last, after the substitutes.
        int coverageAt = (int)(bytes - CoverageTableCompiler.Measure(model.Coverage));
        plans = [new((int)bytes, model.Coverage.Format, model.Coverage.RangeCount,
            (ushort)coverageAt, ReverseChainSubstLayout.BacktrackArrayAt)];
        diagnostic = default;
        return ReadStatus.Done;
    }

    // The version-branched scalars have no generated writers (OQ-5's hand-written
    // boundary); header words and the hand-walked chain words go through this checked
    // raw writer, every declared offset row through the generated writers.
    private static void WriteWord(Span<byte> destination, int at, ushort value) =>
        BinaryPrimitives.WriteUInt16BigEndian(destination.Slice(at, 2), value);

    internal static void WritePlanned(ReverseChainSubstTable model, ReverseChainSubstPartPlan plan,
        Span<byte> destination)
    {
        var location = model.Location;
        int backtrackCount = model.BacktrackCoverages.Count;
        int lookaheadCount = model.LookaheadCoverages.Count;
        CoverageTableCompiler.RequireWritten(ReverseChainSubstView.TryWriteSubstFormat(destination,
            1, out _, location));
        // The whole declared header is contiguous (format, coverage offset, backtrack
        // count and row, lookahead count and row, glyph count, substitutes) and the
        // coverage bodies follow it - the row targets those positions, and the input
        // coverage body is last (the plan's canonical order).
        WriteWord(destination, 2, plan.CoverageOffset);
        WriteWord(destination, 4, (ushort)backtrackCount);
        long backtrackBase = ReverseChainSubstLayout.BacktrackArrayAt + 2L * backtrackCount + 2L +
            2L * lookaheadCount + 2L + 2L * model.Substitutes.Length;
        long lookaheadBase = backtrackBase;
        foreach (var chain in model.BacktrackCoverages)
            lookaheadBase += CoverageTableCompiler.Measure(chain);
        for (int i = 0; i < backtrackCount; i++)
        {
            long target = backtrackBase;
            for (int j = 0; j < i; j++) target += CoverageTableCompiler.Measure(model.BacktrackCoverages[j]);
            WriteWord(destination, ReverseChainSubstLayout.BacktrackArrayAt + 2 * i, (ushort)target);
        }
        int laCountAt = ReverseChainSubstLayout.BacktrackArrayAt + 2 * backtrackCount;
        WriteWord(destination, laCountAt, (ushort)lookaheadCount);
        for (int i = 0; i < lookaheadCount; i++)
        {
            long target = lookaheadBase;
            for (int j = 0; j < i; j++) target += CoverageTableCompiler.Measure(model.LookaheadCoverages[j]);
            WriteWord(destination, laCountAt + 2 + 2 * i, (ushort)target);
        }
        int glyphCountAt = laCountAt + 2 + 2 * lookaheadCount;
        WriteWord(destination, glyphCountAt, (ushort)model.Substitutes.Length);
        for (int i = 0; i < model.Substitutes.Length; i++)
            WriteWord(destination, glyphCountAt + 2 + 2 * i, model.Substitutes[i]);
        int cursor = (int)backtrackBase;
        foreach (var coverage in model.BacktrackCoverages)
        {
            int size = CoverageTableCompiler.Measure(coverage);
            CoverageTableCompiler.WritePlanned(coverage.Glyphs, coverage.Format, coverage.RangeCount,
                destination.Slice(cursor, size), location);
            cursor += size;
        }
        foreach (var coverage in model.LookaheadCoverages)
        {
            int size = CoverageTableCompiler.Measure(coverage);
            CoverageTableCompiler.WritePlanned(coverage.Glyphs, coverage.Format, coverage.RangeCount,
                destination.Slice(cursor, size), location);
            cursor += size;
        }
        CoverageTableCompiler.WritePlanned(model.Coverage.Glyphs, plan.CoverageFormat,
            plan.CoverageRangeCount, destination.Slice(plan.CoverageOffset,
                CoverageTableCompiler.Measure(model.Coverage)), location);
    }
}
