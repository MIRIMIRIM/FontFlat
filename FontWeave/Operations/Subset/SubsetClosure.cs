using FontWeave.Containers;
using FontWeave.Core;
using FontWeave.Tables;

namespace FontWeave.Operations.Subset;

/// <summary>Composite dependency closure (S1a card §3).
///
/// The retained set is the request root, GID 0, and — transitively — every glyph any
/// retained composite references. The input face is materialized through the existing
/// composite-aware read chain first, so a cyclic, over-deep or over-wide component graph
/// propagates the reader's own diagnostics before any closure work starts; the closure
/// itself only walks references of an already-verified graph and cannot invent a
/// violation. Each newly added GID charges one work unit, which is the closure budget's
/// injection point (S0-BF-001).</summary>
public static partial class SubsetOperation
{
    /// <summary>Reads loca/glyf through the typed chain and extends the retained bitmap
    /// to the closure fixed point. Returns the models so the rebuild stage reuses them.</summary>
    private static ReadStatus CloseComposites(SfntDirectoryView directory, ProfileScan profile,
        HeadTable head, MaxpTable maxp, BuildBudget budget, bool[] retained,
        out LocaTable? loca, out GlyfTable? glyf, out List<ushort> added,
        out FontDiagnostic diagnostic)
    {
        loca = null;
        glyf = null;
        added = [];
        ReadStatus status = directory.TryGetTableBytes(profile.LocaOrdinal, out var locaBytes,
            out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = directory.TryGetTableBytes(profile.GlyfOrdinal, out var glyfBytes,
            out diagnostic);
        if (status != ReadStatus.Done) return status;
        DataLocation locaAt = directory.TableLocation(profile.LocaOrdinal);
        status = LocaContext.TryCreate(head, maxp, locaAt, out var locaContext, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = LocaView.TryOpen(locaBytes, out var locaView, out diagnostic, locaAt);
        if (status != ReadStatus.Done) return status;
        status = locaView.Validate(locaContext!, glyfBytes.Length, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = locaView.TryRead(locaContext!, out loca, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        status = GlyfView.TryOpen(glyfBytes, out var glyfView, out diagnostic,
            directory.TableLocation(profile.GlyfOrdinal));
        if (status != ReadStatus.Done) return status;
        // The graph is verified inside this call before any model is published, so the
        // walk below sees an acyclic, budgeted reference structure or nothing at all.
        status = glyfView.TryReadComposites(loca!, out glyf, out diagnostic, budget, true);
        if (status != ReadStatus.Done) return status;

        return CloseCompositeReferences(glyf!, retained, budget, out added, out diagnostic);
    }

    private static ReadStatus CloseCompositeReferences(GlyfTable glyf, bool[] retained,
        BuildBudget budget, out List<ushort> added, out FontDiagnostic diagnostic)
    {
        added = [];

        // Closure work over the verified graph: one queued visit per retained glyph and
        // one work unit per newly pulled-in GID (OQ-S1a-7 lean). The queue's worst case
        // is one entry per glyph.
        DataLocation glyfAt = glyf!.Location;
        if (!budget.Buffer(4L * retained.Length, glyfAt, out diagnostic) ||
            !budget.Work(retained.Length, glyfAt, out diagnostic))
            return ReadStatus.ResourceLimit;
        var queue = new Queue<int>();
        for (int gid = 0; gid < retained.Length; gid++)
            if (retained[gid]) queue.Enqueue(gid);
        while (queue.Count > 0)
        {
            int gid = queue.Dequeue();
            var composite = glyf!.CompositeOf(gid);
            if (composite is null) continue;
            for (int i = 0; i < composite.ComponentCount; i++)
            {
                int child = composite[i].GlyphIndex;
                if (retained[child]) continue;
                if (!budget.Work(1, glyfAt.At(DataField.GlyphId, 0, child), out diagnostic))
                    return ReadStatus.ResourceLimit;
                retained[child] = true;
                added.Add((ushort)child);
                queue.Enqueue(child);
            }
        }
        // Report additions ascending, excluding the root and the implicit GID 0.
        added.Sort();
        return ReadStatus.Done;
    }
}
