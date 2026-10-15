using FontWeave.Containers;
using FontWeave.Core;
using FontWeave.Tables;
using FontWeave.Tables.Layout;

namespace FontWeave.Operations.Subset;

/// <summary>Compact-remap MERG rebuild over the frozen map (SE card §2.6).
///
/// Every distinct ClassDef body re-answers over the output domain in its own
/// format: per output glyph o the class is the input glyph's class (the map's
/// frozen order), the maximal same-class runs recompose, and the format is
/// preserved — format 1 re-answers as the dense [minNonZero, maxNonZero] span
/// with interior class-zero runs explicit (leading and trailing zeros ride the
/// format's implicit outside), format 2 keeps only the non-zero runs (the
/// uncovered outputs read as class 0). Class values never renumber and the n²
/// merge matrix travels byte-verbatim: it is keyed by merge class, not by GID,
/// so the remap does not touch its semantics (OQ-SE-4); classes that lose every
/// member stay as dead rows/columns, never queried. The factory re-answers the
/// whole admission matrix — body numGlyphs against the output domain, the
/// reserved ridge, the square's side and the content-equal body dedupe (two
/// bodies that prune to the same content re-emit once, keeping the shared-body
/// fact). Any factory refusal — the format-1 span belt included — refuses the
/// operation with nothing published; MERG itself never drops (its zero form is
/// a legal emission and no row of it prunes away).</summary>
public static partial class SubsetOperation
{
    private static ReadStatus RebuildMerg(SfntDirectoryView directory, MaxpTable maxp,
        GlyphRemap map, BuildBudget budget, out byte[]? mergBytes,
        out FontDiagnostic diagnostic)
    {
        mergBytes = null;
        ReadStatus status = ReadTableBytes(directory, Merg, out var mergAt,
            out var mergSource, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = MergView.TryOpen(mergSource, out var mergView, out diagnostic,
            mergAt);
        if (status != ReadStatus.Done) return status;
        status = mergView.TryReadTable(maxp.NumGlyphs, out var merg, out diagnostic,
            budget.Limits);
        if (status != ReadStatus.Done) return status;
        var bodies = merg!.ClassDefSlots;
        // One rebuilt body per distinct input body (shared wire bodies share the
        // materialized instance, so the walk runs once per instance and the slot
        // sequence keeps its sharing shape).
        var rebuiltOf = new Dictionary<ClassDefTable, ClassDefTable>(
            ReferenceEqualityComparer.Instance);
        var slots = new ClassDefTable[bodies.Count];
        for (int i = 0; i < bodies.Count; i++)
        {
            if (!rebuiltOf.TryGetValue(bodies[i], out var rebuilt))
            {
                status = RebuildMergClassDef(bodies[i], map, mergAt, out rebuilt,
                    out diagnostic);
                if (status != ReadStatus.Done) return status;
                rebuiltOf[bodies[i]] = rebuilt!;
            }
            slots[i] = rebuilt!;
        }
        if (!budget.Work(merg.MatrixLength, mergAt, out diagnostic))
            return ReadStatus.ResourceLimit;
        status = MergTableCompiler.TryCreate(slots, merg.MergeClassCount,
            merg.Matrix, map.OutputGlyphCount, out var newMerg, out diagnostic,
            location: mergAt);
        if (status != ReadStatus.Done) return status;
        status = MergTableCompiler.TryCompile(newMerg!, out var bytes,
            out diagnostic);
        if (status != ReadStatus.Done) return status;
        return ChargeCompiled(budget, mergAt, bytes!, out mergBytes,
            out diagnostic);
    }

    /// <summary>One body's output-domain re-answer: the per-output class walk,
    /// then the format's own recomposition (the dense span vs the sparse
    /// non-zero runs). The format-1 span belt is the factory's: a span past
    /// 65535 refuses there (RE4) — under a monotone map the span never inflates
    /// past its input's, so the face is the write-side guard, not a pipeline
    /// outcome.</summary>
    private static ReadStatus RebuildMergClassDef(ClassDefTable body, GlyphRemap map,
        DataLocation mergAt, out ClassDefTable? rebuilt, out FontDiagnostic diagnostic)
    {
        rebuilt = null;
        var order = map.OutputToInput;
        int count = order.Length;
        var classes = new ushort[count];
        bool anyNonZero = false;
        for (int o = 0; o < count; o++)
        {
            classes[o] = body.ClassOf(order[o]);
            if (classes[o] != 0) anyNonZero = true;
        }
        ushort format = body.Format == 1 ? (ushort)1 : (ushort)2;
        var ranges = new List<ClassDefRange>();
        if (anyNonZero && format == 1)
        {
            // The dense span [firstNonZero, lastNonZero] with every interior run
            // explicit (the class-zero runs included — format 1 has no gaps).
            int firstNonZero = 0;
            while (classes[firstNonZero] == 0) firstNonZero++;
            int lastNonZero = count - 1;
            while (classes[lastNonZero] == 0) lastNonZero--;
            int runStart = firstNonZero;
            for (int o = firstNonZero + 1; o <= lastNonZero + 1; o++)
            {
                if (o <= lastNonZero && classes[o] == classes[runStart]) continue;
                ranges.Add(new((ushort)runStart, (ushort)(o - 1), classes[runStart]));
                runStart = o;
            }
        }
        else if (anyNonZero)
        {
            // The sparse form keeps the maximal non-zero runs only.
            int runStart = -1;
            for (int o = 0; o <= count; o++)
            {
                bool inside = o < count && classes[o] != 0;
                if (inside && runStart < 0) runStart = o;
                if (runStart >= 0 && (!inside || classes[o] != classes[runStart]))
                {
                    ranges.Add(new((ushort)runStart, (ushort)(o - 1),
                        classes[runStart]));
                    runStart = inside ? o : -1;
                }
            }
        }
        return ClassDefTableCompiler.TryCreate(ranges.ToArray(), count, format,
            out rebuilt, out diagnostic, location: mergAt);
    }
}
