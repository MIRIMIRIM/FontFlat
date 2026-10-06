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
        int length = AttachListLayout.HeaderLength + AttachListLayout.PointStride * model.Count;
        var seen = new List<ushort[]>();
        for (int i = 0; i < model.Count; i++)
        {
            var points = model.Points(i);
            bool shared = false;
            foreach (var candidate in seen)
            {
                if (candidate.AsSpan().SequenceEqual(points)) { shared = true; break; }
            }
            if (!shared)
            {
                seen.Add(points.ToArray());
                length += AttachPointLayout.HeaderLength + AttachPointLayout.PointStride * points.Length;
            }
        }
        return length + CoverageTableCompiler.Measure(model.Coverage);
    }

    // Only immutable, validated models reach this path; the GDEF root preflighted the
    // destination and every offset before calling in.
    internal static void WritePlanned(AttachListTable model, Span<byte> destination)
    {
        var location = model.Location;
        int coverageAt = AttachListLayout.HeaderLength + AttachListLayout.PointStride * model.Count;
        var placed = new List<(ushort[] Points, int At)>();
        int cursor = coverageAt;
        var offsets = new int[model.Count];
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
        CoverageTableCompiler.RequireWritten(AttachListView.TryWriteCoverageOffset(destination,
            (ushort)cursor, out _, location));
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
        CoverageTableCompiler.WritePlanned(model.Coverage, destination.Slice(cursor));
    }
}
