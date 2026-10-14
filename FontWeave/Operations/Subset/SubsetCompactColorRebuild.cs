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
/// order, remaps the glyph reference points and recomputes the clip rows' surviving
/// spans; the variation slots travel verbatim (no GID domain). A dangling glyph
/// reference — a PaintGlyph or PaintColrGlyph target the request did not select —
/// is a finite refusal (RE3, OQ-SE-6); the PaintGlyph domain re-answer is this
/// slice's pre-check because the v1 factory's owned walk does not cover it (SE
/// card §7 finding 4). An all-empty rebuild drops the table (OQ-SE-5). The rebuilt
/// model re-answers every structural invariant through the compiler factory; any
/// failure refuses with nothing published. Registered canonical-form fact: the v1
/// factory's owned construction carries the read model's v0 compat records no
/// longer (TryCreate hardcodes the empty compat arrays), so a rebuilt v1 face
/// emits the graph in the factory's canonical compat-less form — the corpus's
/// only v1 host carries no compat records either.</summary>
public static partial class SubsetOperation
{
    private static ReadStatus RebuildColr(SfntDirectoryView directory, MaxpTable maxp,
        GlyphRemap map, BuildBudget budget, out byte[]? colrBytes, out bool dropped,
        out FontDiagnostic diagnostic)
    {
        colrBytes = null;
        dropped = false;
        ReadStatus status = ReadTableBytes(directory, Colr, out var colrAt,
            out var colrSource, out diagnostic);
        if (status != ReadStatus.Done) return status;
        // CPAL has no GID domain and stays RETAIN_RAW; its validated read supplies
        // the palette-entry authority the COLR read and the compiler factory
        // re-answer (SE card §2.7). Absent CPAL skips the domain (the read side's
        // OQ-T6-01-4 line).
        status = TryReadCpalPaletteEntries(directory, budget, out int? cpalEntries,
            out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = ColrView.TryOpen(colrSource, out var colrView, out diagnostic, colrAt);
        if (status != ReadStatus.Done) return status;
        status = colrView.TryReadTable(maxp.NumGlyphs, cpalEntries, out var model,
            out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        byte[]? bytes;
        if (model!.Version == 1)
        {
            status = RebuildColrV1(model, colrAt, map, out var rebuiltModel,
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
                location: colrAt);
            if (status != ReadStatus.Done) return status;
            status = ColrTableCompiler.TryCompile(v0Model!, out bytes, out diagnostic);
        }
        if (status != ReadStatus.Done) return status;
        return ChargeCompiled(budget, colrAt, bytes!, out colrBytes, out diagnostic);
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

    // ---- v0: prune the two record arrays, recompute the slices ----

    private static ReadStatus RebuildColrV0(ColrTable model, GlyphRemap map,
        out List<ColrBaseGlyphRecord> newBase, out List<ColrLayerRecord> newLayers,
        out bool dropped, out FontDiagnostic diagnostic)
    {
        newBase = [];
        newLayers = [];
        dropped = false;
        diagnostic = default;
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
        // Empty shells cannot emit (the factory refuses the empty record set), so an
        // all-empty rebuild drops the table (OQ-SE-5).
        if (newBase.Count == 0 || newLayers.Count == 0) dropped = true;
        return ReadStatus.Done;
    }

    // ---- v1: reachability pruning + renumbering on the owned graph ----

    private static ReadStatus RebuildColrV1(ColrTable model, DataLocation at,
        GlyphRemap map, out ColrTable? rebuilt, out bool dropped,
        out FontDiagnostic diagnostic)
    {
        rebuilt = null;
        dropped = false;
        // Reachability pruning from the surviving roots (OQ-SE-1): the DFS preorder
        // is parents-first (a parent takes its ordinal before any child is first
        // visited), so the direct-edge forward face the factory re-answers holds by
        // construction; dangling glyph references refuse inside the walk (RE3).
        var rootOfGlyph = new Dictionary<ushort, int>(model.BaseGlyphPaintCount);
        foreach (var row in model.BaseGlyphPaints) rootOfGlyph[row.GlyphId] = row.PaintNodeId;
        var visitedOrder = new List<int>(model.PaintCount);
        var visited = new HashSet<int>(model.PaintCount);
        foreach (var row in model.BaseGlyphPaints)
        {
            if (map.OutputOf(row.GlyphId) < 0) continue;
            ReadStatus status = VisitColrSubgraph(model, row.PaintNodeId, 0, map,
                visited, visitedOrder, out diagnostic);
            if (status != ReadStatus.Done) return status;
        }
        // No surviving color graph means no rebuildable content (OQ-SE-5): the
        // factory's canonical v1 form carries no v0 compat arrays to keep a
        // compat-only face alive.
        if (visitedOrder.Count == 0)
        {
            dropped = true;
            diagnostic = default;
            return ReadStatus.Done;
        }
        var newId = new int[model.PaintCount];
        var paints = new ColrPaint[visitedOrder.Count];
        for (int i = 0; i < visitedOrder.Count; i++)
        {
            int old = visitedOrder[i];
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
        // The clip rows recompute over each range's surviving span; an empty span
        // drops the row, and the ascending non-overlap relations hold through the
        // order-preserving map (SE card §2.1 ④).
        var newClips = new List<ColrClip>(model.ClipCount);
        foreach (var clip in model.Clips)
        {
            if (!TrySurvivingSpan(map, clip.StartGlyphId, clip.EndGlyphId,
                    out ushort firstSurvivor, out ushort lastSurvivor))
                continue;
            newClips.Add(new((ushort)map.OutputOf(firstSurvivor),
                (ushort)map.OutputOf(lastSurvivor), clip.Box));
        }
        var newBgl = new List<ColrBaseGlyphPaint>(visitedOrder.Count);
        foreach (var row in model.BaseGlyphPaints)
        {
            if (map.OutputOf(row.GlyphId) < 0) continue;
            newBgl.Add(new((ushort)map.OutputOf(row.GlyphId),
                newId[rootOfGlyph[row.GlyphId]]));
        }
        // The variation slots ride verbatim (no GID domain, OQ-SE-1).
        return ColrTableCompiler.TryCreate(newBgl, newLayerIds, newClips, paints,
            map.OutputGlyphCount, null, out rebuilt, out diagnostic, model.VarIndexMap,
            model.ItemVariationStore, location: at);
    }

    /// <summary>One reachability step (OQ-SE-1): the three edge classes —
    /// PaintColrLayers slices into the LayerList, direct child edges, PaintColrGlyph
    /// → the referenced glyph's surviving BGL root — over the validated input graph.
    /// A dangling PaintGlyph or PaintColrGlyph target refuses here (RE3, OQ-SE-6)
    /// with the referencing paint's source position and the old GID.</summary>
    private static ReadStatus VisitColrSubgraph(ColrTable model, int nodeId, int depth,
        GlyphRemap map, HashSet<int> visited, List<int> visitedOrder,
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
                        map, visited, visitedOrder, out diagnostic);
                    if (status != ReadStatus.Done) return status;
                }
                break;
            case ColrPaintGlyph glyph:
                if (map.OutputOf(glyph.GlyphId) < 0)
                    return RefuseDanglingColrGlyphRef(model, glyph.Id, glyph.GlyphId,
                        out diagnostic);
                status = VisitColrSubgraph(model, glyph.PaintNodeId, depth + 1, map,
                    visited, visitedOrder, out diagnostic);
                if (status != ReadStatus.Done) return status;
                break;
            case ColrPaintColrGlyph colrGlyph:
                if (map.OutputOf(colrGlyph.GlyphId) < 0)
                    return RefuseDanglingColrGlyphRef(model, colrGlyph.Id,
                        colrGlyph.GlyphId, out diagnostic);
                status = VisitColrSubgraph(model, colrGlyph.TargetNodeId, depth + 1,
                    map, visited, visitedOrder, out diagnostic);
                if (status != ReadStatus.Done) return status;
                break;
            default:
                if (HasColrChild(model.Paints[nodeId]))
                {
                    status = VisitColrSubgraph(model, ChildOf(model.Paints[nodeId]),
                        depth + 1, map, visited, visitedOrder, out diagnostic);
                    if (status != ReadStatus.Done) return status;
                }
                break;
        }
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>The RE3 face (SE card §3): a dangling glyph reference refuses with
    /// InvalidValue at the referencing paint's GlyphId, detail = the old GID (the
    /// sbix dangling-dupe precedent's shape).</summary>
    private static ReadStatus RefuseDanglingColrGlyphRef(ColrTable model, int nodeOrdinal,
        ushort oldGlyphId, out FontDiagnostic diagnostic) =>
        ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
            model.Location.AtLocal(DataField.GlyphId,
                model.PaintSourcePositions[nodeOrdinal], nodeOrdinal), out diagnostic,
            oldGlyphId, 0UL);

    private static bool HasColrChild(ColrPaint node) => node is
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

    /// <summary>The first and last retained input GIDs of [start, end], via two
    /// binary searches over the ascending output-to-input order.</summary>
    private static bool TrySurvivingSpan(GlyphRemap map, ushort start, ushort end,
        out ushort firstSurvivor, out ushort lastSurvivor)
    {
        firstSurvivor = 0;
        lastSurvivor = 0;
        var order = map.OutputToInput;
        int low = 0, high = order.Length;
        while (low < high)
        {
            int mid = low + (high - low) / 2;
            if (order[mid] >= start) high = mid;
            else low = mid + 1;
        }
        if (low >= order.Length || order[low] > end) return false;
        firstSurvivor = order[low];
        int inner = low, high2 = order.Length - 1;
        while (inner <= high2)
        {
            int mid = inner + (high2 - inner) / 2;
            if (order[mid] <= end)
            {
                low = mid;
                inner = mid + 1;
            }
            else high2 = mid - 1;
        }
        lastSurvivor = order[low];
        return true;
    }
}
