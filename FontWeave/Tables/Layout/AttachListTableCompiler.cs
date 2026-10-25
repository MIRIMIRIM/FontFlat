using FontWeave.Core;

namespace FontWeave.Tables.Layout;

/// <summary>Compile-side planning for owned AttachLists. Identical point lists share
/// one AttachPoint body by content identity (the compiled-model analog of the source
/// offset sharing, provably compatible); an edited list therefore splits naturally.</summary>
public static class AttachListTableCompiler
{
    /// <summary>Replaces one glyph's contour-point list. The glyph inventory, Coverage,
    /// source context and numGlyphs are preserved; the replacement must stay strictly
    /// increasing (the read-side rule).</summary>
    public static ReadStatus TryWithPoints(AttachListTable model, int glyphIndex, ReadOnlySpan<ushort> points,
        out AttachListTable? updated, out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        updated = null;
        if (glyphIndex < 0 || glyphIndex >= model.Count)
            return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                model.Location.AtLocal(DataField.Count, AttachListLayout.GlyphCountAt, glyphIndex),
                out diagnostic, unchecked((ulong)(long)glyphIndex), (ulong)model.Count);
        var budget = new BuildBudget(limits);
        if (!budget.Work(points.Length + 1L, model.Location, out diagnostic)) return ReadStatus.ResourceLimit;
        for (int i = 0; i < points.Length; i++)
        {
            if (i > 0 && points[i] <= points[i - 1])
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    model.Location.AtLocal(DataField.Payload, AttachPointLayout.HeaderLength + (ulong)i, i),
                    out diagnostic, points[i], points[i - 1]);
        }
        var replaced = new ushort[model.Count][];
        for (int i = 0; i < model.Count; i++)
            replaced[i] = i == glyphIndex ? points.ToArray() : model.Points(i).ToArray();
        updated = new(model.Coverage, replaced, model.NumGlyphs, model.Location, model.ConsumedLength, []);
        diagnostic = default;
        return ReadStatus.Done;
    }

    internal static int Measure(AttachListTable model)
    {
        Place(model, out int coverageAt, out _, out _);
        return coverageAt + CoverageTableCompiler.Measure(model.Coverage);
    }

    /// <summary>One child block's placement: the Coverage start (after every
    /// AttachPoint body), the per-glyph AttachPoint targets and the deduplicated
    /// bodies in emission order. Shared by the size, the preflight and the writer so
    /// the three cannot disagree.</summary>
    private static void Place(AttachListTable model, out int coverageAt,
        out int[] offsets, out List<(ushort[] Points, int At)> placed)
    {
        int cursor = AttachListLayout.HeaderLength + AttachListLayout.PointStride * model.Count;
        placed = [];
        offsets = new int[model.Count];
        for (int i = 0; i < model.Count; i++)
        {
            var points = model.Points(i);
            int at = -1;
            foreach (var (candidate, position) in placed)
            {
                if (candidate.AsSpan().SequenceEqual(points)) { at = position; break; }
            }
            if (at < 0)
            {
                at = cursor;
                cursor += AttachPointLayout.HeaderLength + AttachPointLayout.PointStride * points.Length;
                placed.Add((points.ToArray(), at));
            }
            offsets[i] = at;
        }
        coverageAt = cursor;
    }

    /// <summary>Plan-time admission of every Offset16 word this child owns: the
    /// Coverage target and each AttachPoint record, plus the two count words. The
    /// writer's narrowing casts then cannot throw or truncate, so an over-wide list
    /// refuses as the finite too-narrow face instead of publishing a bad table.</summary>
    internal static ReadStatus TryPlan(AttachListTable model, BuildBudget budget,
        out FontDiagnostic diagnostic)
    {
        if (!budget.Work(2L * model.Count + 1, model.Location, out diagnostic))
            return ReadStatus.ResourceLimit;
        if (model.Count > ushort.MaxValue)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                model.Location.AtLocal(DataField.Count, AttachListLayout.GlyphCountAt),
                out diagnostic, (ulong)model.Count, (ulong)ushort.MaxValue);
        Place(model, out int coverageAt, out var offsets, out var placed);
        ReadStatus status = CheckTarget(model.Location, coverageAt,
            AttachListLayout.CoverageOffsetAt, out diagnostic);
        if (status != ReadStatus.Done) return status;
        for (int i = 0; i < offsets.Length; i++)
        {
            status = CheckTarget(model.Location, offsets[i],
                AttachListLayout.HeaderLength + (ulong)i * AttachListLayout.PointStride, out diagnostic);
            if (status != ReadStatus.Done) return status;
        }
        foreach (var (points, _) in placed)
            if (points.Length > ushort.MaxValue)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    model.Location.AtLocal(DataField.Count, AttachPointLayout.CountAt),
                    out diagnostic, (ulong)points.Length, (ulong)ushort.MaxValue);
        diagnostic = default;
        return ReadStatus.Done;
    }

    private static ReadStatus CheckTarget(DataLocation location, int target, ulong fieldAt,
        out FontDiagnostic diagnostic)
    {
        if (target <= ushort.MaxValue)
        {
            diagnostic = default;
            return ReadStatus.Done;
        }
        return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
            location.AtLocal(DataField.Offset, fieldAt), out diagnostic, (ulong)target,
            (ulong)ushort.MaxValue);
    }

    // Only immutable, preflighted models reach this path (GdefTableCompiler.TryPlan
    // runs TryPlan for every child before any byte exists).
    internal static void WritePlanned(AttachListTable model, Span<byte> destination)
    {
        var location = model.Location;
        Place(model, out int coverageAt, out var offsets, out var placed);
        CoverageTableCompiler.RequireWritten(AttachListView.TryWriteCoverageOffset(destination,
            (ushort)coverageAt, out _, location));
        CoverageTableCompiler.RequireWritten(AttachListView.TryWriteGlyphCount(destination,
            (ushort)model.Count, out _, location));
        for (int i = 0; i < model.Count; i++)
            CoverageTableCompiler.RequireWritten(AttachListView.TryWriteAttachPointOffsetRecordAt(destination,
                AttachListLayout.HeaderLength + (ulong)i * AttachListLayout.PointStride,
                new AttachListView.AttachPointOffsetRecordData(i, (ushort)offsets[i]), out _, location));
        foreach (var (points, at) in placed)
        {
            var body = destination.Slice(at);
            CoverageTableCompiler.RequireWritten(AttachPointView.TryWritePointCount(body,
                (ushort)points.Length, out _, location));
            for (int i = 0; i < points.Length; i++)
                CoverageTableCompiler.RequireWritten(AttachPointView.TryWritePointIndexAt(body,
                    AttachPointLayout.HeaderLength + (ulong)i * AttachPointLayout.PointStride,
                    new AttachPointView.PointIndexData(i, points[i]), out _, location));
        }
        CoverageTableCompiler.WritePlanned(model.Coverage, destination.Slice(coverageAt));
    }
}
