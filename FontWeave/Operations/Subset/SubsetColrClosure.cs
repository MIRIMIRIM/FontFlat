using FontWeave.Containers;
using FontWeave.Core;
using FontWeave.Tables;

namespace FontWeave.Operations.Subset;

/// <summary>COLR glyph-reference closure.
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
/// chain re-answers the palette domain (SE card §2.7).
///
/// Both subset modes run it. Compact renumbers the paint-graph references, so a
/// target outside the retained set cannot be emitted; retain-GIDs keeps the table
/// raw, but the same stage clears every unretained glyph's outline, so keeping the
/// GID is not keeping the content a reference draws. The v0 base→layer slices close
/// in both modes (on a v1 table's compat arrays too): retain keeps the records raw and
/// every layer a surviving base row paints must keep its outline; compact prunes the
/// records by the frozen map, so a layer glyph outside the retained set would delete
/// its record and empty the base's slice — the same blank color glyph the paint-graph
/// face exists to prevent.
/// Retain's raw pass-through contract is untouched: a COLR the typed chain cannot
/// read is skipped there (nothing to retain), while compact's rebuild refusal stands.</summary>
public static partial class SubsetOperation
{
    private static ReadStatus CloseColrReferences(SfntDirectoryView directory,
        MaxpTable maxp, PrepareState state, BuildBudget budget, bool compact,
        out FontDiagnostic diagnostic)
    {
        ReadStatus status = CloseColrReferencesCore(directory, maxp, state, budget, compact,
            out diagnostic);
        // Retain-GIDs keeps the table raw, so a COLR whose typed read refuses is not this
        // stage's refusal there - the raw pass-through contract is unchanged, and no
        // reference could be retained anyway. Compact must read it to rebuild, so its
        // refusal stands. The budget ceiling still propagates in both modes.
        if (!compact && status is not (ReadStatus.Done or ReadStatus.ResourceLimit))
        {
            diagnostic = default;
            return ReadStatus.Done;
        }
        return status;
    }

    private static ReadStatus CloseColrReferencesCore(SfntDirectoryView directory,
        MaxpTable maxp, PrepareState state, BuildBudget budget, bool compact,
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
        // The store⟡fvar signature coupling is symmetric: an ItemVariationStore must be
        // read against the fvar axis count, and only a table that declares one may pass
        // it. A static v1 table and every v0 table therefore stay on the null face; the
        // accessor itself requires a known version and answers zero for v0.
        status = colrView.TryGetItemVariationStoreOffset(out uint storeOffset, out diagnostic);
        if (status != ReadStatus.Done) return status;
        int? fvarAxisCount = null;
        // A store without fvar leaves the axis face null: the reader's own coupling
        // refusal is the finite answer for that input.
        if (storeOffset != 0 && state.Fvar is { } fvar) fvarAxisCount = fvar.AxisCount;
        status = colrView.TryReadTable(maxp.NumGlyphs, cpalEntries, fvarAxisCount,
            out var model, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        state.Colr = model;
        state.CpalPaletteEntries = cpalEntries;
        // The table can carry both definitions: a v1 table keeps the v0 base/layer
        // arrays for the glyphs without a paint graph, and a v1 renderer queries them.
        // Both modes close the base→layer references on both table versions: retain
        // keeps the records raw (an unretained layer glyph loses its outline), and
        // compact keeps the records of a retained base (a dropped layer glyph would
        // otherwise empty the slice and delete the record, turning a color glyph
        // blank). A layer glyph can itself be a base row, so this is a fixed point.
        status = CloseColrV0Layers(model!, state.Retained, budget, out diagnostic);
        if (status != ReadStatus.Done) return status;
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

    // The v0 base→layer face of both modes: a retained base row keeps its layer
    // records, so every layer glyph has to keep its outline (retain) or its slot
    // (compact). A layer glyph that is itself a base row re-opens that row's slice,
    // so the worklist runs to the fixed point (the composite closure's shape).
    private static ReadStatus CloseColrV0Layers(ColrTable model, bool[] retained,
        BuildBudget budget, out FontDiagnostic diagnostic)
    {
        var rowOfGlyph = new Dictionary<ushort, int>(model.BaseGlyphCount);
        for (int row = 0; row < model.BaseGlyphCount; row++)
            rowOfGlyph[model.BaseGlyphs[row].GlyphId] = row;
        var queue = new Queue<int>();
        for (int row = 0; row < model.BaseGlyphCount; row++)
            if (retained[model.BaseGlyphs[row].GlyphId]) queue.Enqueue(row);
        while (queue.Count > 0)
        {
            var baseGlyph = model.BaseGlyphs[queue.Dequeue()];
            for (int i = 0; i < baseGlyph.NumLayers; i++)
            {
                int layerIndex = baseGlyph.FirstLayerIndex + i;
                ushort glyphId = model.Layers[layerIndex].GlyphId;
                if (retained[glyphId]) continue;
                if (!budget.Work(1, model.Location.AtLocal(DataField.GlyphId,
                        model.LayerRowPosition(layerIndex), layerIndex), out diagnostic))
                    return ReadStatus.ResourceLimit;
                retained[glyphId] = true;
                if (rowOfGlyph.TryGetValue(glyphId, out int reopened)) queue.Enqueue(reopened);
            }
        }
        diagnostic = default;
        return ReadStatus.Done;
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
