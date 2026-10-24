using FontWeave.Containers;
using FontWeave.Core;
using FontWeave.Tables;

namespace FontWeave.Operations.Subset;

/// <summary>Compact-remap COLR rebuild over the frozen map (SE card §2.1).
///
/// The typed read chain materializes the whole table once (v0 records or the v1
/// BGL-roots walk), the rebuild prunes and renumbers on the owned model — v0
/// base/layer rows keep only retained glyphs with the slices recomputed over the
/// surviving layer order; v1 walks the surviving BaseGlyphList roots (reachability
/// pruning over the three edge classes, OQ-SE-1), rebuilds the LayerList from the
/// entries the surviving PaintColrLayers nodes reference in their original relative
/// order, remaps the glyph reference points and re-spans the clip rows over the
/// surviving contiguous output runs; the variation slots travel verbatim (no
/// GID domain). The paint-graph glyph references the request did not select have
/// already joined the retained set at the color closure's fixed point (the shared
/// walk below), so a dangling PaintGlyph or PaintColrGlyph target is a backstop
/// refusal (RE3, OQ-SE-6) — unreachable while both consumers ride the one
/// dispatch, kept because the v1 factory's owned walk does not cover the domain
/// (SE card §7 finding 4). A v1 table may also carry the v0 compatibility arrays
/// (spec L625-628), which a v1 renderer queries for a glyph without a BGL row, so
/// the rebuild prunes them by the same rule as a v0 table and re-emits them beside
/// the rebuilt graph; dropping them would silently uncolor every compat-only glyph.
/// An all-empty rebuild drops the table (OQ-SE-5). The rebuilt model re-answers
/// every structural invariant through the compiler factory; any failure refuses
/// with nothing published.</summary>
public static partial class SubsetOperation
{
    private static ReadStatus RebuildColr(ColrTable model, int? cpalEntries,
        GlyphRemap map, BuildBudget budget, out byte[]? colrBytes, out bool dropped,
        out FontDiagnostic diagnostic)
    {
        colrBytes = null;
        dropped = false;
        // The color closure materialized this model and read the CPAL palette-entry
        // authority with it (the parked-model pair); the rebuild never re-opens the
        // table (single-parse discipline, S1c card §1.2).
        DataLocation at = model.Location;
        ReadStatus status;
        byte[]? bytes;
        if (model.Version == 1)
        {
            status = RebuildColrV1(model, at, map, cpalEntries, budget, out var rebuiltModel,
                out dropped, out diagnostic);
            if (status != ReadStatus.Done || dropped) return status;
            status = ColrTableCompiler.TryCompile(rebuiltModel!, out bytes,
                out diagnostic);
        }
        else
        {
            status = RebuildColrV0(model, map, out var newBase, out var newLayers,
                out dropped, out diagnostic);
            if (status != ReadStatus.Done || dropped) return status;
            status = ColrTableCompiler.TryCreate(newBase, newLayers,
                map.OutputGlyphCount, cpalEntries, out var v0Model, out diagnostic,
                location: at);
            if (status != ReadStatus.Done) return status;
            status = ColrTableCompiler.TryCompile(v0Model!, out bytes, out diagnostic);
        }
        if (status != ReadStatus.Done) return status;
        return ChargeCompiled(budget, at, bytes!, out colrBytes, out diagnostic);
    }

    /// <summary>The compilers' budget hooks take limits, not the operation's running
    /// budget, so each SE producer re-answers the compile's own Work/Buffer/Output
    /// formula against the operation budget once the bytes exist — before anything
    /// publishes (SE card §1; the emitter's output charge stays the definitive one
    /// and exhaustion still refuses with no partial product).</summary>
    private static ReadStatus ChargeCompiled(BuildBudget budget, DataLocation at,
        byte[] bytes, out byte[]? charged, out FontDiagnostic diagnostic)
    {
        if (!budget.Work(bytes.Length, at, out diagnostic) ||
            !budget.Buffer(bytes.Length, at, out diagnostic) ||
            !budget.Output(bytes.Length, at, out diagnostic))
        {
            charged = null;
            return ReadStatus.ResourceLimit;
        }
        charged = bytes;
        diagnostic = default;
        return ReadStatus.Done;
    }

    // ---- v0: prune the two record arrays, recompute the slices ----

    private static ReadStatus RebuildColrV0(ColrTable model, GlyphRemap map,
        out List<ColrBaseGlyphRecord> newBase, out List<ColrLayerRecord> newLayers,
        out bool dropped, out FontDiagnostic diagnostic)
    {
        diagnostic = default;
        dropped = false;
        PruneColrRecords(model, map, out newBase, out newLayers);
        // Empty shells cannot emit (the factory refuses the empty record set), so an
        // all-empty rebuild drops the table (OQ-SE-5).
        if (newBase.Count == 0 || newLayers.Count == 0) dropped = true;
        return ReadStatus.Done;
    }

    /// <summary>Prunes both v0 record arrays by the frozen map: layers survive in
    /// their original relative order with their glyph remapped, and a base row
    /// survives when its own glyph does, its slice recomputed over the surviving
    /// layer order. Shared by the v0 rebuild and the v1 table's compatibility
    /// arrays — the two answer one wire shape and one rule set.</summary>
    private static void PruneColrRecords(ColrTable model, GlyphRemap map,
        out List<ColrBaseGlyphRecord> newBase, out List<ColrLayerRecord> newLayers)
    {
        newBase = [];
        newLayers = [];
        // Layers survive in their original relative order, glyphs remapped; the
        // per-layer decision is independent of the base rows (SE card §2.1).
        var newIndexOfLayer = new int[model.LayerCount];
        for (int i = 0; i < model.LayerCount; i++)
        {
            var row = model.Layers[i];
            int output = map.OutputOf(row.GlyphId);
            if (output < 0)
            {
                newIndexOfLayer[i] = -1;
                continue;
            }
            newIndexOfLayer[i] = newLayers.Count;
            newLayers.Add(new((ushort)output, row.PaletteIndex));
        }
        foreach (var row in model.BaseGlyphs)
        {
            // The base record's own glyph and its layers prune independently (the
            // per-row deletion rules, SE card §2.1): an unselected base glyph deletes
            // the record even when some of its slice's layers survive.
            int outputGlyph = map.OutputOf(row.GlyphId);
            if (outputGlyph < 0) continue;
            int first = -1, count = 0;
            for (int i = 0; i < row.NumLayers; i++)
            {
                int index = newIndexOfLayer[row.FirstLayerIndex + i];
                if (index < 0) continue;
                if (first < 0) first = index;
                count++;
            }
            // The survivors of one contiguous source slice occupy one contiguous run
            // of the new layer order (nothing between two in-range survivors can come
            // from outside the slice), so the recomputed window covers exactly this
            // base's surviving layers; shared slices stay shared (SE card §2.1).
            if (count > 0)
                newBase.Add(new((ushort)outputGlyph, (ushort)first, (ushort)count));
        }
        // The base record array is a strict glyphID ascent, and the compact map can
        // invert the input rows (the CFF1 SID/CID freeze), so the rebuilt rows sort
        // by their output glyph before the factory re-answers the ascent; on the
        // monotone orders the sort is the identity.
        newBase.Sort(static (a, b) => a.GlyphId.CompareTo(b.GlyphId));
    }

    // ---- v1: reachability pruning + renumbering on the owned graph ----

    private static ReadStatus RebuildColrV1(ColrTable model, DataLocation at,
        GlyphRemap map, int? cpalEntries, BuildBudget budget, out ColrTable? rebuilt,
        out bool dropped, out FontDiagnostic diagnostic)
    {
        rebuilt = null;
        dropped = false;
        // Reachability pruning from the surviving roots (OQ-SE-1): the DFS preorder
        // is parents-first (a parent takes its ordinal before any child is first
        // visited), so the direct-edge forward face the factory re-answers holds by
        // construction; the walk's glyph-reference face refuses a dangling one (RE3
        // — the color closure's fixed point has already retained every reachable
        // reference, so the face answers Done on every live path).
        ColrGlyphRefFace refuseDangling = RefuseUnretained;
        var rootOfGlyph = new Dictionary<ushort, int>(model.BaseGlyphPaintCount);
        foreach (var row in model.BaseGlyphPaints) rootOfGlyph[row.GlyphId] = row.PaintNodeId;
        var visitedOrder = new List<int>(model.PaintCount);
        var visited = new HashSet<int>(model.PaintCount);
        foreach (var row in model.BaseGlyphPaints)
        {
            if (map.OutputOf(row.GlyphId) < 0) continue;
            ReadStatus status = VisitColrSubgraph(model, row.PaintNodeId, 0,
                refuseDangling, visited, visitedOrder, out diagnostic);
            if (status != ReadStatus.Done) return status;
        }
        // The v0 compatibility arrays prune by the same rule as a v0 table: a v1
        // renderer falls back to them for a glyph without a BGL row, so dropping
        // them would uncolor every compat-only glyph (spec L625-628).
        PruneColrRecords(model, map, out var compatBase, out var compatLayers);
        // No surviving color definition of either form means no rebuildable content
        // (OQ-SE-5); a compat-only face is a live table.
        if (visitedOrder.Count == 0 && compatBase.Count == 0)
        {
            dropped = true;
            diagnostic = default;
            return ReadStatus.Done;
        }
        if (visitedOrder.Count == 0)
        {
            // A compat-only v1 table keeps its version: the v1 header's
            // BaseGlyphList stays present with zero rows (the read side requires the
            // offset, not a row), and the compatibility arrays carry the color.
            return ColrTableCompiler.TryCreate([], [], [], [], map.OutputGlyphCount,
                cpalEntries, out rebuilt, out diagnostic, location: at,
                compatBaseGlyphs: compatBase, compatLayers: compatLayers);
        }
        // The new ordinals follow the read model's own canonical order restricted
        // to the visited set: the read numbers its nodes topologically (parents
        // first, the L476 re-use line), so every model edge - direct and
        // indirect - points from a lower to a higher ordinal, and subsetting
        // with renumbering preserves that. The factory's direct-edge forward
        // face re-answers on these ordinals by construction, while a DFS
        // preorder would let a shared subgraph's early visit invert a later
        // direct edge.
        var newOrder = Enumerable.Range(0, model.PaintCount)
            .Where(visited.Contains).ToList();
        var newId = new int[model.PaintCount];
        var paints = new ColrPaint[newOrder.Count];
        for (int i = 0; i < newOrder.Count; i++)
        {
            int old = newOrder[i];
            newId[old] = i;
            var node = model.Paints[old];
            node.Id = i;
            paints[i] = node;
        }
        // The LayerList rebuild (SE card §2.1 ③): the entries the surviving
        // PaintColrLayers nodes reference, deduplicated in their original relative
        // order; dead entries never enter (they are unreachable by the read
        // contract, OQ-T6-02-11).
        var referenced = new SortedSet<int>();
        foreach (var node in paints)
        {
            if (node is not ColrPaintLayers layers) continue;
            for (int k = 0; k < layers.LayerNodeIds.Count; k++)
                referenced.Add((int)layers.FirstLayerIndex + k);
        }
        var entryNewIndex = new int[model.LayerPaintNodeIds.Count];
        var newLayerIds = new List<int>(referenced.Count);
        foreach (int entry in referenced)
        {
            entryNewIndex[entry] = newLayerIds.Count;
            newLayerIds.Add(newId[model.LayerPaintNodeIds[entry]]);
        }
        foreach (var node in paints)
        {
            switch (node)
            {
                case ColrPaintLayers layers:
                {
                    // FirstLayerIndex has no setter: the surviving slice node is
                    // rebuilt over its renumbered entry run (the input model is the
                    // transient read product; the replacement takes the node's id).
                    // The factory form stores the slice's entry POSITIONS in
                    // LayerNodeIds (the owned walk's own addressing); the read model's
                    // ordinals ride LayerPaintNodeIds alone.
                    int first = entryNewIndex[(int)layers.FirstLayerIndex];
                    var ids = new int[layers.LayerNodeIds.Count];
                    for (int k = 0; k < ids.Length; k++) ids[k] = first + k;
                    paints[layers.Id] = new ColrPaintLayers(layers.Id, (uint)first, ids)
                    {
                        Depth = layers.Depth,
                    };
                    break;
                }
                case ColrPaintGlyph glyph:
                    // The dangling check ran in the walk; every visited reference
                    // resolves (SE card §2.1 ④; the factory does not re-answer this
                    // domain, finding 4).
                    glyph.GlyphId = (ushort)map.OutputOf(glyph.GlyphId);
                    glyph.PaintNodeId = newId[glyph.PaintNodeId];
                    break;
                case ColrPaintColrGlyph colrGlyph:
                    colrGlyph.GlyphId = (ushort)map.OutputOf(colrGlyph.GlyphId);
                    break;
                case ColrPaintTransform transform:
                    transform.PaintNodeId = newId[transform.PaintNodeId];
                    break;
                case ColrPaintVarTransform varTransform:
                    varTransform.PaintNodeId = newId[varTransform.PaintNodeId];
                    break;
                case ColrPaintComposite composite:
                    composite.SourceNodeId = newId[composite.SourceNodeId];
                    composite.BackdropNodeId = newId[composite.BackdropNodeId];
                    break;
                default:
                    if (HasColrChild(node))
                        SetColrChild(node, newId[ChildOf(node)]);
                    break;
            }
        }
        // The clip rows recompute over each range's surviving outputs, one wire row
        // per maximal contiguous output run; an empty range drops the row. The
        // compact map's output order can permute the input order (the CFF1 SID/CID
        // freeze), so the query runs over the ascending input-GID survivor array —
        // on the monotone TrueType order every row's survivors are output-contiguous
        // and one run per row is the endpoints' own image (SE card §2.1 ④).
        if (!budget.Work(map.OutputGlyphCount, at, out diagnostic))
            return ReadStatus.ResourceLimit;
        var survivors = SortedSurvivors(map);
        var outputs = new int[survivors.Length];
        var runs = new List<(ushort Start, ushort End)>();
        var newClips = new List<ColrClip>(model.ClipCount);
        foreach (var clip in model.Clips)
        {
            if (!TrySurvivingRuns(map, survivors, clip.StartGlyphId, clip.EndGlyphId,
                    outputs, runs))
                continue;
            foreach (var (runStart, runEnd) in runs)
                newClips.Add(new(runStart, runEnd, clip.Box));
        }
        // The rows' own order is a wire contract too (the ClipList's strict
        // ascent), and the surviving rows' output order is the map permutation's,
        // not the input rows' — the freeze can invert them. The runs are pairwise
        // disjoint (a row's own runs split on a gap; different rows' outputs are
        // disjoint by the map's injectivity), so the sort by output start cannot
        // produce an overlap before the factory re-answers the ascent; on the
        // monotone orders the sort is the identity and the emission stays
        // byte-identical.
        newClips = newClips.OrderBy(static clip => clip.StartGlyphId).ToList();
        var newBgl = new List<ColrBaseGlyphPaint>(visitedOrder.Count);
        foreach (var row in model.BaseGlyphPaints)
        {
            if (map.OutputOf(row.GlyphId) < 0) continue;
            newBgl.Add(new((ushort)map.OutputOf(row.GlyphId),
                newId[rootOfGlyph[row.GlyphId]]));
        }
        // The BaseGlyphList's own order is a strict glyphID ascent (the v1 factory
        // re-answers it), and the surviving rows' output order is the map
        // permutation's, not the input rows' — the freeze can invert them. Sorting by
        // output glyph before the factory is the same identity the ClipList keeps; on
        // the monotone orders it changes nothing.
        newBgl.Sort(static (a, b) => a.GlyphId.CompareTo(b.GlyphId));
        // The variation slots ride verbatim (no GID domain, OQ-SE-1); the pruned
        // compatibility arrays ride beside the graph (a v1 renderer's fallback) and
        // answer the same palette domain the v0 face answers.
        return ColrTableCompiler.TryCreate(newBgl, newLayerIds, newClips, paints,
            map.OutputGlyphCount, cpalEntries, out rebuilt, out diagnostic, model.VarIndexMap,
            model.ItemVariationStore, location: at, compatBaseGlyphs: compatBase,
            compatLayers: compatLayers);

        // The RE3 face (SE card §3): a paint-graph glyph reference outside the
        // frozen map refuses with InvalidValue at the referencing paint's GlyphId,
        // detail = the old GID (the sbix dangling-dupe precedent's shape); a
        // retained one answers Done. The backstop consumer of the shared walk's
        // face — the color closure retains instead of refusing.
        ReadStatus RefuseUnretained(int nodeOrdinal, ushort glyphId,
            out FontDiagnostic face)
        {
            if (map.OutputOf(glyphId) >= 0)
            {
                face = default;
                return ReadStatus.Done;
            }
            return RefuseDanglingColrGlyphRef(model, nodeOrdinal, glyphId, out face);
        }
    }

    /// <summary>The glyph-reference face of the one paint-graph walk: the color
    /// closure RETAINS the reference (the fixed point that keeps the frozen map
    /// carrying every reachable paint-graph GID), the rebuild refuses an unretained
    /// one (the RE3 backstop). The two faces are the only difference between the
    /// walk's two consumers.</summary>
    private delegate ReadStatus ColrGlyphRefFace(int nodeOrdinal, ushort glyphId,
        out FontDiagnostic diagnostic);

    /// <summary>One reachability step of the ONE paint-graph walk (OQ-SE-1): the
    /// three edge classes — PaintColrLayers slices into the LayerList, direct child
    /// edges, PaintColrGlyph → the referenced glyph's surviving BGL root — over the
    /// validated input graph. The color closure and this rebuild are its only two
    /// consumers and differ only in the glyph-reference face, so a new paint node
    /// or edge class MUST land in this one switch: a class the closure misses but
    /// the rebuild walks re-opens the dual-implementation drift this sharing
    /// forecloses (the SE-era PaintComposite/Transform omissions, SESSION-RULES
    /// writing discipline 2). A dangling glyph reference answers through the face
    /// (RE3, OQ-SE-6) with the referencing paint's source position and the old
    /// GID.</summary>
    private static ReadStatus VisitColrSubgraph(ColrTable model, int nodeId, int depth,
        ColrGlyphRefFace glyphRef, HashSet<int> visited, List<int> visitedOrder,
        out FontDiagnostic diagnostic)
    {
        if (depth > ColrView.MaxPaintGraphDepth)
            return ReadWire.Fail(ReadStatus.ResourceLimit, DiagnosticCode.BudgetExceeded,
                model.Location.AtLocal(DataField.WorkUnits, 0, nodeId), out diagnostic,
                (ulong)depth, (ulong)ColrView.MaxPaintGraphDepth);
        if (!visited.Add(nodeId))
        {
            diagnostic = default;
            return ReadStatus.Done;
        }
        if (visited.Count > ColrView.MaxPaintGraphNodes)
            return ReadWire.Fail(ReadStatus.ResourceLimit, DiagnosticCode.BudgetExceeded,
                model.Location.AtLocal(DataField.WorkUnits, 0, nodeId), out diagnostic,
                (ulong)visited.Count, (ulong)ColrView.MaxPaintGraphNodes);
        visitedOrder.Add(nodeId);
        ReadStatus status;
        switch (model.Paints[nodeId])
        {
            case ColrPaintLayers layers:
                for (int k = 0; k < layers.LayerNodeIds.Count; k++)
                {
                    status = VisitColrSubgraph(model,
                        model.LayerPaintNodeIds[(int)layers.FirstLayerIndex + k], depth + 1,
                        glyphRef, visited, visitedOrder, out diagnostic);
                    if (status != ReadStatus.Done) return status;
                }
                break;
            case ColrPaintGlyph glyph:
                status = glyphRef(glyph.Id, glyph.GlyphId, out diagnostic);
                if (status != ReadStatus.Done) return status;
                status = VisitColrSubgraph(model, glyph.PaintNodeId, depth + 1, glyphRef,
                    visited, visitedOrder, out diagnostic);
                if (status != ReadStatus.Done) return status;
                break;
            case ColrPaintColrGlyph colrGlyph:
                status = glyphRef(colrGlyph.Id, colrGlyph.GlyphId, out diagnostic);
                if (status != ReadStatus.Done) return status;
                status = VisitColrSubgraph(model, colrGlyph.TargetNodeId, depth + 1,
                    glyphRef, visited, visitedOrder, out diagnostic);
                if (status != ReadStatus.Done) return status;
                break;
            case ColrPaintComposite composite:
                // Both children are direct edges the read validated; they must join
                // the visited set or their renumbering collapses onto ordinal 0.
                status = VisitColrSubgraph(model, composite.SourceNodeId, depth + 1,
                    glyphRef, visited, visitedOrder, out diagnostic);
                if (status != ReadStatus.Done) return status;
                status = VisitColrSubgraph(model, composite.BackdropNodeId, depth + 1,
                    glyphRef, visited, visitedOrder, out diagnostic);
                if (status != ReadStatus.Done) return status;
                break;
            default:
                if (HasColrChild(model.Paints[nodeId]))
                {
                    status = VisitColrSubgraph(model, ChildOf(model.Paints[nodeId]),
                        depth + 1, glyphRef, visited, visitedOrder, out diagnostic);
                    if (status != ReadStatus.Done) return status;
                }
                break;
        }
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>The RE3 wire shape (SE card §3): the refusal fires with InvalidValue
    /// at the referencing paint's GlyphId, detail = the old GID (the sbix
    /// dangling-dupe precedent's shape).</summary>
    private static ReadStatus RefuseDanglingColrGlyphRef(ColrTable model, int nodeOrdinal,
        ushort oldGlyphId, out FontDiagnostic diagnostic) =>
        ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
            model.Location.AtLocal(DataField.GlyphId,
                model.PaintSourcePositions[nodeOrdinal], nodeOrdinal), out diagnostic,
            oldGlyphId, 0UL);

    private static bool HasColrChild(ColrPaint node) => node is
        ColrPaintTransform or ColrPaintVarTransform or
        ColrPaintTranslate or ColrPaintScale or ColrPaintScaleAroundCenter
        or ColrPaintScaleUniform or ColrPaintScaleUniformAroundCenter
        or ColrPaintRotate or ColrPaintRotateAroundCenter or ColrPaintSkew
        or ColrPaintSkewAroundCenter or ColrPaintVarTranslate or ColrPaintVarScale
        or ColrPaintVarScaleAroundCenter or ColrPaintVarScaleUniform
        or ColrPaintVarScaleUniformAroundCenter or ColrPaintVarRotate
        or ColrPaintVarRotateAroundCenter or ColrPaintVarSkew
        or ColrPaintVarSkewAroundCenter;

    private static int ChildOf(ColrPaint node) => node switch
    {
        ColrPaintTransform n => n.PaintNodeId,
        ColrPaintVarTransform n => n.PaintNodeId,
        ColrPaintTranslate n => n.PaintNodeId,
        ColrPaintScale n => n.PaintNodeId,
        ColrPaintScaleAroundCenter n => n.PaintNodeId,
        ColrPaintScaleUniform n => n.PaintNodeId,
        ColrPaintScaleUniformAroundCenter n => n.PaintNodeId,
        ColrPaintRotate n => n.PaintNodeId,
        ColrPaintRotateAroundCenter n => n.PaintNodeId,
        ColrPaintSkew n => n.PaintNodeId,
        ColrPaintSkewAroundCenter n => n.PaintNodeId,
        ColrPaintVarTranslate n => n.PaintNodeId,
        ColrPaintVarScale n => n.PaintNodeId,
        ColrPaintVarScaleAroundCenter n => n.PaintNodeId,
        ColrPaintVarScaleUniform n => n.PaintNodeId,
        ColrPaintVarScaleUniformAroundCenter n => n.PaintNodeId,
        ColrPaintVarRotate n => n.PaintNodeId,
        ColrPaintVarRotateAroundCenter n => n.PaintNodeId,
        ColrPaintVarSkew n => n.PaintNodeId,
        ColrPaintVarSkewAroundCenter n => n.PaintNodeId,
        _ => throw new InvalidOperationException("A paint without a child edge."),
    };

    private static void SetColrChild(ColrPaint node, int childId)
    {
        switch (node)
        {
            case ColrPaintTranslate n: n.PaintNodeId = childId; break;
            case ColrPaintScale n: n.PaintNodeId = childId; break;
            case ColrPaintScaleAroundCenter n: n.PaintNodeId = childId; break;
            case ColrPaintScaleUniform n: n.PaintNodeId = childId; break;
            case ColrPaintScaleUniformAroundCenter n: n.PaintNodeId = childId; break;
            case ColrPaintRotate n: n.PaintNodeId = childId; break;
            case ColrPaintRotateAroundCenter n: n.PaintNodeId = childId; break;
            case ColrPaintSkew n: n.PaintNodeId = childId; break;
            case ColrPaintSkewAroundCenter n: n.PaintNodeId = childId; break;
            case ColrPaintVarTranslate n: n.PaintNodeId = childId; break;
            case ColrPaintVarScale n: n.PaintNodeId = childId; break;
            case ColrPaintVarScaleAroundCenter n: n.PaintNodeId = childId; break;
            case ColrPaintVarScaleUniform n: n.PaintNodeId = childId; break;
            case ColrPaintVarScaleUniformAroundCenter n: n.PaintNodeId = childId; break;
            case ColrPaintVarRotate n: n.PaintNodeId = childId; break;
            case ColrPaintVarRotateAroundCenter n: n.PaintNodeId = childId; break;
            case ColrPaintVarSkew n: n.PaintNodeId = childId; break;
            case ColrPaintVarSkewAroundCenter n: n.PaintNodeId = childId; break;
            default: throw new InvalidOperationException("A paint without a child edge.");
        }
    }

    /// <summary>The retained set in ascending input-GID order — the domain every
    /// surviving-span query answers over. The compact map's output order is the
    /// SID/CID identity order on a CFF1 face and can permute the input order, so
    /// the output-to-input order itself must never answer a first/last-survivor
    /// query; the copy is built once per table rebuild.</summary>
    private static ushort[] SortedSurvivors(GlyphRemap map)
    {
        var sorted = map.OutputToInput.ToArray();
        Array.Sort(sorted);
        return sorted;
    }

    /// <summary>One input range's surviving outputs as maximal contiguous output
    /// runs, ascending: adjacent output GIDs share a run, a gap — a dead glyph or
    /// another row's survivor — closes it. The survivors come off the ascending
    /// input-GID array (an empty range returns false), the row's outputs arrive in
    /// the map permutation's order, so the run split sorts them first. The runs are
    /// pairwise disjoint across a whole rebuild: a row's own runs split on a gap by
    /// construction, and different rows' outputs are disjoint by the map's
    /// injectivity. This is the wire row's interval semantics made explicit — a wire
    /// row covers one contiguous output interval, so a row whose survivors straddle
    /// a gap must emit one row per run where the output extrema would claim the
    /// in-between glyphs; the extrema endpoints are this split's run-count-one
    /// special case, which every monotone order and every non-interleaving
    /// permutation yields.</summary>
    private static bool TrySurvivingRuns(GlyphRemap map, ushort[] survivors,
        ushort start, ushort end, int[] outputs, List<(ushort Start, ushort End)> runs)
    {
        runs.Clear();
        int low = 0, high = survivors.Length;
        while (low < high)
        {
            int mid = low + (high - low) / 2;
            if (survivors[mid] >= start) high = mid;
            else low = mid + 1;
        }
        int count = 0;
        for (int i = low; i < survivors.Length && survivors[i] <= end; i++)
            outputs[count++] = map.OutputOf(survivors[i]);
        if (count == 0) return false;
        Array.Sort(outputs, 0, count);
        runs.Add(((ushort)outputs[0], (ushort)outputs[0]));
        for (int i = 1; i < count; i++)
        {
            int output = outputs[i];
            if (output == runs[^1].End + 1)
                runs[^1] = (runs[^1].Start, (ushort)output);
            else
                runs.Add(((ushort)output, (ushort)output));
        }
        return true;
    }
}
