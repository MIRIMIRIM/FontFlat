using FontWeave.Containers;
using FontWeave.Core;
using FontWeave.Tables;

namespace FontWeave.Operations.Subset;

/// <summary>COLR v1 paint-graph reference closure (compact only).
///
/// The rebuild's walk refuses a paint-graph glyph reference the frozen map does not
/// carry (the RE3 face), so every PaintGlyph/PaintColrGlyph target reachable from a
/// surviving BaseGlyphList root must already sit in the retained set before the map
/// freeze — a codepoint-seeded request never selects those outlines through the cmap
/// (the 2026-10-18 noto spot check: asterisk's root paint is itself a PaintGlyph
/// whose target GID no codepoint seed retains, so the rebuild refused the very
/// reference its own walk created). This stage is the retained-set half of that same
/// walk: it materializes the table once here (single-parse discipline, S1c card
/// §1.2) and parks the model for the payload rebuild, then extends the retained set
/// at the fixed point of <see cref="SubsetOperation.VisitColrSubgraph"/> — the
/// closure and the rebuild answer the identical edge dispatch, so the closure cannot
/// miss an edge the rebuild would walk (the dual-implementation drift the shared
/// walker exists to foreclose, SESSION-RULES writing discipline 2). It runs after
/// the layout and seac families, so their additions' color graphs close too, and
/// before the composite closure, whose worklist consumes this stage's additions; a
/// glyph a LATER family adds keeps the rebuild's RE3 face as its backstop. A
/// PaintGlyph target that itself carries a BaseGlyphList row joins the walk's root
/// queue — PaintGlyph addresses an outline, not a paint node, so that glyph's own
/// color graph is otherwise unreachable. Every newly added GID charges one work
/// unit (the closure budget's injection point, S0-BF-001); CPAL has no GID domain
/// and stays RETAIN_RAW, its validated read riding along because the COLR read
/// chain re-answers the palette domain (SE card §2.7).</summary>
public static partial class SubsetOperation
{
    private static ReadStatus CloseColrReferences(SfntDirectoryView directory,
        MaxpTable maxp, PrepareState state, BuildBudget budget,
        out FontDiagnostic diagnostic)
    {
        ReadStatus status = ReadTableBytes(directory, Colr, out var colrAt,
            out var colrSource, out diagnostic);
        if (status is ReadStatus.NotFound or ReadStatus.MissingTable)
        {
            // No COLR: nothing to close and nothing to park (the payload rebuild is
            // decided by the record's absence as well).
            diagnostic = default;
            return ReadStatus.Done;
        }
        if (status != ReadStatus.Done) return status;
        status = TryReadCpalPaletteEntries(directory, budget, out int? cpalEntries,
            out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = ColrView.TryOpen(colrSource, out var colrView, out diagnostic, colrAt);
        if (status != ReadStatus.Done) return status;
        status = colrView.TryReadTable(maxp.NumGlyphs, cpalEntries, out var model,
            out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        state.Colr = model;
        state.CpalPaletteEntries = cpalEntries;
        // v0 carries no paint graph: its rebuild prunes the two record arrays
        // without a glyph-reference refusal face.
        if (model!.Version != 1) return ReadStatus.Done;
        var rootOfGlyph = new Dictionary<ushort, int>(model.BaseGlyphPaintCount);
        foreach (var row in model.BaseGlyphPaints) rootOfGlyph[row.GlyphId] = row.PaintNodeId;
        // The walk roots: every surviving BGL row at this moment, plus any root the
        // retaining face activates mid-walk (the worklist fixed point, the S1a
        // CloseComposites shape).
        var queue = new Queue<int>();
        foreach (var row in model.BaseGlyphPaints)
            if (state.Retained[row.GlyphId]) queue.Enqueue(row.PaintNodeId);
        var retained = state.Retained;
        var visited = new HashSet<int>(model.PaintCount);
        var order = new List<int>();
        ColrGlyphRefFace retainRef = RetainColrGlyphRef;
        while (queue.Count > 0)
        {
            status = VisitColrSubgraph(model, queue.Dequeue(), 0, retainRef,
                visited, order, out diagnostic);
            if (status != ReadStatus.Done) return status;
        }
        return ReadStatus.Done;

        ReadStatus RetainColrGlyphRef(int nodeOrdinal, ushort glyphId,
            out FontDiagnostic face)
        {
            if (retained[glyphId])
            {
                face = default;
                return ReadStatus.Done;
            }
            if (!budget.Work(1, model.Location.AtLocal(DataField.GlyphId,
                    model.PaintSourcePositions[nodeOrdinal], nodeOrdinal), out face))
                return ReadStatus.ResourceLimit;
            retained[glyphId] = true;
            if (rootOfGlyph.TryGetValue(glyphId, out int root)) queue.Enqueue(root);
            return ReadStatus.Done;
        }
    }

    private static ReadStatus TryReadCpalPaletteEntries(SfntDirectoryView directory,
        BuildBudget budget, out int? entries, out FontDiagnostic diagnostic)
    {
        entries = null;
        ReadStatus status = directory.TryFindUniqueTable(Cpal, out int ordinal,
            out diagnostic);
        if (status is ReadStatus.NotFound or ReadStatus.MissingTable)
        {
            // No CPAL: the read side and the factory skip the palette domain (the
            // container-level COLR->CPAL requirement is the joint validator's).
            diagnostic = default;
            return ReadStatus.Done;
        }
        if (status != ReadStatus.Done) return status;
        status = directory.TryGetTableBytes(ordinal, out var cpalBytes, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = CpalView.TryOpen(cpalBytes, out var cpalView, out diagnostic,
            directory.TableLocation(ordinal));
        if (status != ReadStatus.Done) return status;
        status = cpalView.TryReadTable(out var cpal, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        entries = cpal!.NumPaletteEntries;
        return ReadStatus.Done;
    }
}
