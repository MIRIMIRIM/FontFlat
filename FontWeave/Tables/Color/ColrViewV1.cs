using FontWeave.Core;

namespace FontWeave.Tables;

/// <summary>Strict whole-table validation and materialization for COLR v1 (card
/// T6-02 §3). The v1 face rides the same ColrView: after the shared version gate
/// accepts version 1, this half re-answers every structural face - the v0
/// compatibility records under their v1 grace, the BaseGlyphList/LayerList/
/// ClipList array domains, and the whole paint graph (format gates, packed
/// reads, the five offset bases, glyph/palette/slice domains, the path-set cycle
/// refusal and the depth/node budgets). Error priority follows the T4-01 line:
/// presence, bounds, relations, value domains, graph domain - the graph walk
/// only runs once every array it touches is legal. Pure validation materializes
/// nothing; the walk's scratch state is never returned.</summary>
public readonly ref partial struct ColrView
{
    /// <summary>The paint graph's product guards (OQ-T6-02-12 lean): the spec
    /// sets no numeric bounds ("arbitrary depth ... to the limits inherent in
    /// the formats", L553), so these caps are ResourceLimit faces - a different
    /// diagnostic tier from every spec refusal, and orders of magnitude above
    /// the corpus peaks (depth 7, 87,012 unique nodes).</summary>
    internal const int MaxPaintGraphDepth = 128;
    internal const int MaxPaintGraphNodes = 1 << 20;

    internal ReadStatus ValidateTableV1(int numGlyphs, int? cpalNumPaletteEntries,
        out FontDiagnostic diagnostic, BuildBudget budget)
    {
        return ReadTableV1(numGlyphs, cpalNumPaletteEntries, false, out _,
            out diagnostic, budget);
    }

    /// <summary>The one v1 read walk: validate-only for the pure gate,
    /// materializing for TryReadTable. Everything refuses before anything is
    /// returned, so a materialized model exists only for fully validated
    /// wires.</summary>
    internal ReadStatus ReadTableV1(int numGlyphs, int? cpalNumPaletteEntries,
        bool materialize, out ColrTable? model, out FontDiagnostic diagnostic,
        BuildBudget budget)
    {
        model = null;
        ReadStatus status = TryGetBaseGlyphListOffset(out uint baseGlyphList, out diagnostic);
        if (status != ReadStatus.Done) return status;
        // The one v1 header offset without a "(may be NULL)" note (spec L629
        // against L630-633): a v1 table without a BaseGlyphList has no v1 color
        // definitions at all, so the text asymmetry is the hard face (OQ-T6-02-1;
        // corpus 175/175 nonzero). layerList/clipList stay legal at zero (L637/
        // L639); the variation slots get only their window receipt below
        // (OQ-T6-02-5 - refusing nonzero would reject 29 legal variable hosts at
        // the structural layer, and the contents are T6-03's to open).
        if (baseGlyphList == 0)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.AtLocal(DataField.Offset, 14), out diagnostic, 0UL, 1UL);
        status = TryGetLayerListOffset(out uint layerList, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = TryGetClipListOffset(out uint clipList, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = TryGetVarIndexMapOffset(out uint varIndexMap, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = TryGetItemVariationStoreOffset(out uint store, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (varIndexMap >= (ulong)_data.Length)
            return ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.RangeOutsideSource,
                Location.AtLocal(DataField.Offset, 26), out diagnostic,
                varIndexMap, (ulong)_data.Length);
        if (store >= (ulong)_data.Length)
            return ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.RangeOutsideSource,
                Location.AtLocal(DataField.Offset, 30), out diagnostic,
                store, (ulong)_data.Length);
        status = TryGetNumBaseGlyphPaintRecords(out uint paintCount, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = TryGetNumLayerPaints(out uint layerPaintCount, out diagnostic);
        if (status != ReadStatus.Done) return status;
        // An absent LayerList carries no entries - the count word physically
        // cannot exist, so the accessor's position-0 probe never stands.
        if (layerList == 0)
            layerPaintCount = 0;
        if (!budget.Work((long)_data.Length +
                6L * (long)NumBaseGlyphRecords + 4L * (long)NumLayerRecords +
                6L * paintCount + 4L * layerPaintCount, Location, out diagnostic))
            return ReadStatus.ResourceLimit;
        // The compatibility records re-answer every T6-01 rule, under the v1
        // grace: the "may be 0"/"(may be NULL)" wordings hang off the v1 lines
        // (spec L625-628), so a zero count is legal here (148/175 corpus hosts)
        // where the v0 walk refuses it. A count with a null offset still
        // refuses; a zero count with a dangling offset is the observation
        // shape (OQ-T6-02-2's attachment) and passes.
        status = ValidateCompatRecords(numGlyphs, cpalNumPaletteEntries, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = TryGetBaseGlyphRecordsOffset(out uint compatBaseOffset, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = TryGetLayerRecordsOffset(out uint compatLayerOffset, out diagnostic);
        if (status != ReadStatus.Done) return status;
        // BaseGlyphList: the generated extent require owns the window (the first
        // incomplete record's field provenance), then the row walk answers the
        // glyph domain, the strict ascent (L710) and the direct-link forward+
        // window pair (L476/L564(1), OQ-T6-02-7).
        status = TryRequireBaseGlyphPaintRecordExtent(out diagnostic);
        if (status != ReadStatus.Done) return status;
        var baseRoots = new Dictionary<ushort, uint>(
            (int)Math.Min(paintCount, 1u << 16));
        var rootOrder = new List<uint>((int)Math.Min(paintCount, 1u << 16));
        uint previousGlyph = 0;
        for (uint i = 0; i < paintCount; i++)
        {
            status = TryGetBaseGlyphPaintRecord(unchecked((int)i), out var record, out diagnostic);
            if (status != ReadStatus.Done) return status;
            ulong rowPosition = baseGlyphList + 4 + (ulong)i * ColrLayout.BaseGlyphPaintRecordSize;
            var at = Location.AtLocal(DataField.GlyphId, rowPosition, unchecked((int)i));
            if (record.GlyphId >= (ushort)numGlyphs)
                return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                    at, out diagnostic, record.GlyphId, (ulong)numGlyphs);
            if (i > 0 && record.GlyphId <= previousGlyph)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    at, out diagnostic, record.GlyphId, previousGlyph);
            previousGlyph = record.GlyphId;
            // The paint offset addresses from the BaseGlyphList start (the
            // first nested base, spec L701-706).
            ulong root = baseGlyphList + record.PaintOffset;
            if (root >= (ulong)_data.Length)
                return ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.RangeOutsideSource,
                    Location.AtLocal(DataField.Offset, rowPosition + 2, unchecked((int)i)),
                    out diagnostic, root, (ulong)_data.Length);
            if (root <= rowPosition)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    Location.AtLocal(DataField.Offset, rowPosition + 2, unchecked((int)i)),
                    out diagnostic, root, rowPosition);
            baseRoots[record.GlyphId] = (uint)root;
            rootOrder.Add((uint)root);
        }
        // LayerList: the generated extent require plus each entry's window - the
        // dead entries' only mechanical face (OQ-T6-02-11 keeps their sub-graphs
        // out of the deep walk: the spec's graph is the root-reachable set,
        // L547).
        var layerTargets = new uint[layerPaintCount];
        var layerReferenced = new bool[layerPaintCount];
        if (layerList != 0)
        {
            status = TryRequireLayerPaintOffsetExtent(out diagnostic);
            if (status != ReadStatus.Done) return status;
            for (uint i = 0; i < layerPaintCount; i++)
            {
                status = TryGetLayerPaintOffset(unchecked((int)i), out var entry, out diagnostic);
                if (status != ReadStatus.Done) return status;
                ulong target = layerList + entry.PaintOffset;
                if (target >= (ulong)_data.Length)
                    return ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.RangeOutsideSource,
                        Location.AtLocal(DataField.Offset,
                            layerList + 4 + (ulong)i * ColrLayout.LayerPaintOffsetStride,
                            unchecked((int)i)), out diagnostic, target, (ulong)_data.Length);
                layerTargets[i] = (uint)target;
            }
        }
        // ClipList: the format gate, the record window, the per-row relations
        // and the ClipBox windows and format gates (OQ-T6-02-4/9/10).
        status = ValidateClipList(clipList, numGlyphs, materialize, out var clips,
            out diagnostic);
        if (status != ReadStatus.Done) return status;
        // The paint graph walk (OQ-T6-02-6/7/8/11/12): roots are the BGL records
        // in glyph order - the spec's lookup preference order (L653) is also the
        // deterministic walk order.
        var walk = new PaintWalk(numGlyphs, cpalNumPaletteEntries, layerList,
            layerTargets, layerReferenced, baseRoots, materialize);
        walk.MaxEnd = Math.Max(baseGlyphList + 4 + 6UL * paintCount,
            layerList + 4 + 4UL * layerPaintCount);
        foreach (uint root in rootOrder)
        {
            status = VisitPaint(root, 0, walk, budget, out diagnostic);
            if (status != ReadStatus.Done) return status;
        }
        if (!materialize)
        {
            diagnostic = default;
            return ReadStatus.Done;
        }
        // Materialization: the canonical order is parents first, children later
        // (L476: "a re-used paint table can only occur later in the file than
        // any of the paint tables that use it"), so the stable ordinal is the
        // topological order - Kahn emits a node the moment every parent has been
        // emitted, with the deterministic tie-break (smaller minimum depth
        // first, then the first-visit order, OQ-T6-02-14). The compiler emits in
        // ordinal order and every child offset then points forward.
        int nodeTotal = walk.Nodes.Count;
        var distinctChildren = new HashSet<int>[nodeTotal];
        var parentsOf = new HashSet<int>[nodeTotal];
        for (int i = 0; i < nodeTotal; i++)
        {
            distinctChildren[i] = [];
            parentsOf[i] = [];
        }
        foreach (var (parent, child) in walk.Edges)
        {
            distinctChildren[parent].Add(child);
            parentsOf[child].Add(parent);
        }
        var pendingParents = new int[nodeTotal];
        var ready = new PriorityQueue<int, (int Depth, int Seq)>();
        for (int i = 0; i < nodeTotal; i++)
        {
            pendingParents[i] = parentsOf[i].Count;
            if (pendingParents[i] == 0)
                ready.Enqueue(i, (walk.Depths[i], walk.Sequences[i]));
        }
        var ordinal = new int[nodeTotal];
        int emitted = 0;
        while (ready.TryDequeue(out int idx, out _))
        {
            ordinal[idx] = emitted++;
            foreach (int child in distinctChildren[idx])
            {
                pendingParents[child]--;
                if (pendingParents[child] == 0)
                    ready.Enqueue(child, (walk.Depths[child], walk.Sequences[child]));
            }
        }
        // The walk refused every cycle, so every node drains.
        for (int i = 0; i < nodeTotal; i++)
        {
            ColrPaint node = walk.Nodes[i];
            node.Id = ordinal[i];
            node.Depth = walk.Depths[i];
            RemapNodeChildren(node, ordinal);
        }
        var orderedPaints = new ColrPaint[nodeTotal];
        for (int i = 0; i < nodeTotal; i++)
            orderedPaints[ordinal[i]] = walk.Nodes[i];
        var baseGlyphPaints = new ColrBaseGlyphPaint[paintCount];
        for (uint i = 0; i < paintCount; i++)
        {
            status = TryGetBaseGlyphPaintRecord(unchecked((int)i), out var record, out diagnostic);
            if (status != ReadStatus.Done) return status;
            baseGlyphPaints[i] = new(record.GlyphId,
                ordinal[walk.Visited[baseRoots[record.GlyphId]]]);
        }
        var layerNodeIds = new int[layerPaintCount];
        for (uint i = 0; i < layerPaintCount; i++)
            layerNodeIds[i] = layerReferenced[i] ? ordinal[walk.Visited[layerTargets[i]]] : -1;
        // The compat rows materialize exactly like the T6-01 read.
        status = TryGetNumBaseGlyphRecords(out ushort numBase, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = TryGetNumLayerRecords(out ushort numLayers, out diagnostic);
        if (status != ReadStatus.Done) return status;
        var compatBase = new ColrBaseGlyphRecord[numBase];
        for (int i = 0; i < numBase; i++)
        {
            status = TryGetBaseGlyphRecord(i, out var row, out diagnostic);
            if (status != ReadStatus.Done) return status;
            compatBase[i] = new(row.GlyphId, row.FirstLayerIndex, row.NumLayers);
        }
        var compatLayers = new ColrLayerRecord[numLayers];
        for (int i = 0; i < numLayers; i++)
        {
            status = TryGetLayerRecord(i, out var row, out diagnostic);
            if (status != ReadStatus.Done) return status;
            compatLayers[i] = new(row.GlyphId, row.PaletteIndex);
        }
        if (!budget.Buffer(6L * paintCount + 4L * layerPaintCount +
                11L * clips.Length + 64L * nodeTotal + 8L * walk.StopCount,
                Location, out diagnostic))
            return ReadStatus.ResourceLimit;
        model = new(compatBase, compatLayers, baseGlyphPaints, layerNodeIds, clips,
            orderedPaints, numGlyphs, compatBaseOffset, compatLayerOffset,
            varIndexMap, store, Location,
            (int)Math.Min(walk.MaxEnd, (ulong)_data.Length));
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>The v0 compatibility records inside a v1 table: the T6-01 rule
    /// set re-answered (strict glyph ascent, both glyph domains, the slice
    /// relation, the cross-table palette domain - diagnostics keyed exactly like
    /// the v0 face), with the v1 zero grace (OQ-T6-02-10). This deliberately
    /// mirrors rather than shares the v0 walk: the frozen T6-01 surface stays
    /// byte-identical and this copy answers only for version 1.</summary>
    private ReadStatus ValidateCompatRecords(int numGlyphs, int? cpalNumPaletteEntries,
        out FontDiagnostic diagnostic)
    {
        diagnostic = default;
        ReadStatus status = TryGetNumBaseGlyphRecords(out ushort numBase, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = TryGetNumLayerRecords(out ushort numLayers, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = TryGetBaseGlyphRecordsOffset(out uint baseOffset, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = TryGetLayerRecordsOffset(out uint layerOffset, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (numBase > 0)
        {
            if (baseOffset == 0)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    Location.AtLocal(DataField.Offset, 4), out diagnostic, 0UL, 1UL);
            status = TryRequireBaseGlyphRecordExtent(out diagnostic);
            if (status != ReadStatus.Done) return status;
            int previous = -1;
            for (int i = 0; i < numBase; i++)
            {
                status = TryGetBaseGlyphRecord(i, out var row, out diagnostic);
                if (status != ReadStatus.Done) return status;
                var at = Location.AtLocal(DataField.GlyphId,
                    baseOffset + (ulong)i * ColrLayout.BaseGlyphRecordSize, i);
                if (row.GlyphId >= (ushort)numGlyphs)
                    return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                        at, out diagnostic, row.GlyphId, (ulong)numGlyphs);
                if (i > 0 && row.GlyphId <= previous)
                    return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                        at, out diagnostic, row.GlyphId, unchecked((ulong)previous));
                previous = row.GlyphId;
                if ((uint)row.FirstLayerIndex + (uint)row.NumLayers > (uint)numLayers)
                    return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                        Location.AtLocal(DataField.Payload,
                            baseOffset + (ulong)i * ColrLayout.BaseGlyphRecordSize + 4, i),
                        out diagnostic, (uint)row.FirstLayerIndex + (uint)row.NumLayers,
                        (ulong)numLayers);
            }
        }
        if (numLayers > 0)
        {
            if (layerOffset == 0)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    Location.AtLocal(DataField.Offset, 8), out diagnostic, 0UL, 1UL);
            status = TryRequireLayerRecordExtent(out diagnostic);
            if (status != ReadStatus.Done) return status;
            for (int i = 0; i < numLayers; i++)
            {
                status = TryGetLayerRecord(i, out var row, out diagnostic);
                if (status != ReadStatus.Done) return status;
                var at = Location.AtLocal(DataField.GlyphId,
                    layerOffset + (ulong)i * ColrLayout.LayerRecordSize, i);
                if (row.GlyphId >= (ushort)numGlyphs)
                    return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                        at, out diagnostic, row.GlyphId, (ulong)numGlyphs);
                if (cpalNumPaletteEntries is int entries &&
                    row.PaletteIndex != ColrLayout.ForegroundPaletteIndex &&
                    row.PaletteIndex >= (uint)entries)
                    return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                        Location.AtLocal(DataField.Payload,
                            layerOffset + (ulong)i * ColrLayout.LayerRecordSize + 2, i),
                        out diagnostic, row.PaletteIndex, unchecked((ulong)(long)entries));
            }
        }
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>The ClipList walk (spec L731-747): the format-1 gate, the record
    /// window, then per row the glyph domain (OQ-T6-02-10's lean), the strict
    /// startGlyphID ascent (L737), the no-overlap relation (L747, equal starts
    /// included), the reversed-range refusal (OQ-T6-02-9's lean - an empty range
    /// has no semantics) and the ClipBox window and format gate (OQ-T6-02-4).
    /// The boxes' four FWORDs are read verbatim with no order relation (the spec
    /// carries none).</summary>
    private ReadStatus ValidateClipList(uint clipList, int numGlyphs, bool materialize,
        out ColrClip[] clips, out FontDiagnostic diagnostic)
    {
        clips = [];
        if (clipList == 0)
        {
            diagnostic = default;
            return ReadStatus.Done;
        }
        if (clipList + 1 > (ulong)_data.Length)
            return ReadWire.Fail(ReadStatus.Truncated, DiagnosticCode.TruncatedData,
                Location.AtLocal(DataField.Version, clipList), out diagnostic,
                (ulong)_data.Length, clipList + 1);
        byte format = _data[(int)clipList];
        if (format != 1)
            return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
                Location.AtLocal(DataField.Version, clipList), out diagnostic,
                format, 1UL);
        if (clipList + 5 > (ulong)_data.Length)
            return ReadWire.Fail(ReadStatus.Truncated, DiagnosticCode.TruncatedData,
                Location.AtLocal(DataField.Count, clipList + 1), out diagnostic,
                (ulong)_data.Length, clipList + 5);
        uint numClips = ReadWire.U32(_data, (int)clipList + 1);
        ulong rowsStart = clipList + 5;
        ulong rowsEnd = rowsStart + 7UL * numClips;
        if (rowsEnd > (ulong)_data.Length)
        {
            // The first incomplete clip row names itself (the T4-10 family line
            // over the 7-byte rows).
            ulong available = (ulong)_data.Length - rowsStart;
            ulong firstIncomplete = rowsStart + available / ColrLayout.ClipRecordSize
                * ColrLayout.ClipRecordSize;
            return ReadWire.Fail(ReadStatus.Truncated, DiagnosticCode.TruncatedData,
                Location.AtLocal(DataField.None, firstIncomplete), out diagnostic,
                (ulong)_data.Length, rowsEnd);
        }
        clips = materialize ? new ColrClip[numClips] : [];
        int previousStart = -1, previousEnd = -1;
        for (uint i = 0; i < numClips; i++)
        {
            ulong row = rowsStart + (ulong)i * ColrLayout.ClipRecordSize;
            ushort start = ReadWire.U16(_data, (int)row);
            ushort end = ReadWire.U16(_data, (int)row + 2);
            uint boxOffset = U24((int)row + 4);
            var at = Location.AtLocal(DataField.GlyphId, row, unchecked((int)i));
            if (start >= (ushort)numGlyphs || end >= (ushort)numGlyphs)
                return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                    at, out diagnostic,
                    start >= (ushort)numGlyphs ? start : end, (ulong)numGlyphs);
            if (previousStart >= 0 && start <= previousStart)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    at, out diagnostic, start, unchecked((uint)previousStart));
            if (previousEnd >= 0 && start <= previousEnd)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.OverlappingRange,
                    at, out diagnostic, start, unchecked((uint)(previousEnd + 1)));
            if (start > end)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    at, out diagnostic, start, end);
            ulong box = clipList + boxOffset;
            if (box >= (ulong)_data.Length)
                return ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.RangeOutsideSource,
                    Location.AtLocal(DataField.Offset, row + 4, unchecked((int)i)),
                    out diagnostic, box, (ulong)_data.Length);
            if (box + ColrLayout.ClipBoxFormat1Size > (ulong)_data.Length)
                return ReadWire.Fail(ReadStatus.Truncated, DiagnosticCode.TruncatedData,
                    Location.AtLocal(DataField.Payload, box), out diagnostic,
                    (ulong)_data.Length, box + ColrLayout.ClipBoxFormat1Size);
            byte boxFormat = _data[(int)box];
            if (boxFormat != 1)
                return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
                    Location.AtLocal(DataField.Version, box), out diagnostic,
                    boxFormat, 1UL);
            if (materialize)
                clips[i] = new ColrClip(start, end, new ColrClipBox(
                    RawFword((int)box + 1),
                    RawFword((int)box + 3),
                    RawFword((int)box + 5),
                    RawFword((int)box + 7)));
            previousStart = start;
            previousEnd = end;
        }
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>One paint node visit (the L571-581 algorithm API-shaped): the
    /// path set decides cycles (OQ-T6-02-6's hard face), the global visited set
    /// converges shared sub-graphs (L553-555 - 98 corpus hosts, 18,723 shared
    /// nodes at peak, zero false refusals by construction), and the depth/node
    /// caps are the OQ-T6-02-12 ResourceLimit tier. Every direct-link edge is
    /// checked at the parent (window, forward, cycle) so the diagnostic names
    /// the offending offset field; the indirect edges (PaintColrLayers slices
    /// and PaintColrGlyph targets, L547) skip only the forward check.</summary>
    private ReadStatus VisitPaint(uint target, int depth, PaintWalk walk,
        BuildBudget budget, out FontDiagnostic diagnostic)
    {
        diagnostic = default;
        if (walk.Visited.TryGetValue(target, out int nodeIdx))
        {
            walk.NoteRevisit(nodeIdx, depth);
            return ReadStatus.Done;
        }
        if (walk.Visited.Count >= MaxPaintGraphNodes)
            return ReadWire.Fail(ReadStatus.ResourceLimit, DiagnosticCode.BudgetExceeded,
                Location.AtLocal(DataField.WorkUnits, target), out diagnostic,
                (ulong)(walk.Visited.Count + 1), (ulong)MaxPaintGraphNodes);
        if (depth > MaxPaintGraphDepth)
            return ReadWire.Fail(ReadStatus.ResourceLimit, DiagnosticCode.BudgetExceeded,
                Location.AtLocal(DataField.WorkUnits, target), out diagnostic,
                (ulong)depth, (ulong)MaxPaintGraphDepth);
        if (!budget.Work(24, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        byte format = _data[(int)target];
        // The format gate (OQ-T6-02-3 lean): unknown formats refuse as
        // UnsupportedFormat - the L590 "should be ignored" wording is the
        // renderer's recovery semantics, not a license to drop declared-domain
        // bytes silently. The fourteen var formats refuse the same way
        // (OQ-T6-02-4: spec-legal v1 faces that are T6-03's declared domain;
        // T6-03 flips them to accepted).
        if (format is < 1 or > 32)
            return RefusePaintFormat(target, format, out diagnostic);
        if (format is 3 or 5 or 7 or 9 or 13 or 15 or 17 or 19 or 21 or 23 or 25 or 27
                or 29 or 31)
            return RefusePaintFormat(target, format, out diagnostic);
        ReadStatus status = RequirePaintHead(target, format, out diagnostic);
        if (status != ReadStatus.Done) return status;
        nodeIdx = walk.Depths.Count;
        walk.Visited.Add(target, nodeIdx);
        walk.Path.Add(target);
        walk.Depths.Add(depth);
        walk.Sequences.Add(walk.Sequence++);
        walk.MaxEnd = Math.Max(walk.MaxEnd, target + (ulong)PaintSize(format));
        // A placeholder holds the visited slot while the children visit; the
        // concrete node replaces it before this frame returns.
        if (walk.Materialize)
            walk.Nodes.Add(_placeholder);
        switch (format)
        {
            case 1:
                status = VisitPaintColrLayers(target, nodeIdx, walk, budget, out diagnostic);
                break;
            case 2:
                status = VisitPaintSolid(target, nodeIdx, walk, out diagnostic);
                break;
            case 4 or 6 or 8:
                status = VisitPaintGradient(target, format, nodeIdx, walk, budget, out diagnostic);
                break;
            case 10:
                status = VisitPaintGlyph(target, nodeIdx, walk, budget, out diagnostic);
                break;
            case 11:
                status = VisitPaintColrGlyph(target, nodeIdx, walk, budget, out diagnostic);
                break;
            case 12:
                status = VisitPaintTransform(target, nodeIdx, walk, budget, out diagnostic);
                break;
            case 14 or 16 or 18 or 20 or 22 or 24 or 26 or 28 or 30:
                status = VisitPaintScalarFamily(target, format, nodeIdx, walk, budget, out diagnostic);
                break;
            default: // 32
                status = VisitPaintComposite(target, nodeIdx, walk, budget, out diagnostic);
                break;
        }
        if (status != ReadStatus.Done) return status;
        walk.Path.Remove(target);
        diagnostic = default;
        return ReadStatus.Done;
    }

    private static readonly ColrPaint _placeholder = new ColrPaintSolid(0, 0, 0);

    private static void StoreNode(PaintWalk walk, int nodeIdx, ColrPaint built)
    {
        // Validate mode keeps the list empty - the walk's scratch state is
        // never returned.
        if (walk.Materialize) walk.Nodes[nodeIdx] = built;
    }

    private ReadStatus VisitPaintColrLayers(uint target, int nodeIdx, PaintWalk walk,
        BuildBudget budget, out FontDiagnostic diagnostic)
    {
        diagnostic = default;
        byte numLayers = _data[(int)target + 1];
        uint firstLayerIndex = ReadWire.U32(_data, (int)target + 2);
        // "If a PaintColrLayers table is present, then a LayerList is also
        // present" (L565) - the absence face lives at the header offset field.
        if (walk.LayerListOffset == 0)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.AtLocal(DataField.Offset, 18), out diagnostic, 0UL, 1UL);
        // The slice domain: first + num within the LayerList (L565's second
        // clause), u32 + u8 wide arithmetic; a zero-layer slice is the legal
        // row-level zero (the T6-01 OQ-6 same-kind observation).
        if ((ulong)firstLayerIndex + numLayers > (ulong)walk.LayerTargets.Length)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.AtLocal(DataField.Payload, target + 2), out diagnostic,
                (ulong)firstLayerIndex + numLayers, (ulong)walk.LayerTargets.Length);
        var built = new ColrPaintLayers(0, firstLayerIndex, new int[numLayers]);
        for (int k = 0; k < numLayers; k++)
        {
            uint layerTarget = walk.LayerTargets[firstLayerIndex + (uint)k];
            walk.LayerReferenced[firstLayerIndex + (uint)k] = true;
            // Indirect edge (L547): the window was answered by the LayerList
            // walk; the cycle check and the recursion run here.
            ReadStatus status = VisitIndirectEdge(target, nodeIdx, layerTarget, walk,
                budget, out diagnostic);
            if (status != ReadStatus.Done) return status;
            built.LayerNodeIdArray[k] = walk.Visited[layerTarget];
        }
        StoreNode(walk, nodeIdx, built);
        return ReadStatus.Done;
    }

    private ReadStatus VisitPaintSolid(uint target, int nodeIdx, PaintWalk walk,
        out FontDiagnostic diagnostic)
    {
        diagnostic = default;
        ushort paletteIndex = ReadWire.U16(_data, (int)target + 1);
        ReadStatus status = RequirePaletteIndex(target + 1, paletteIndex, walk, out diagnostic);
        if (status != ReadStatus.Done) return status;
        // The raw F2DOT14 alpha travels verbatim; out-of-range values are the
        // reserved-wording observation (L786, OQ-T6-02-8), never a refusal.
        StoreNode(walk, nodeIdx, new ColrPaintSolid(0, paletteIndex,
            ReadWire.U16(_data, (int)target + 3)));
        return ReadStatus.Done;
    }

    private ReadStatus VisitPaintGradient(uint target, byte format, int nodeIdx,
        PaintWalk walk, BuildBudget budget, out FontDiagnostic diagnostic)
    {
        diagnostic = default;
        uint colorLineTarget = target + U24((int)target + 1);
        ReadStatus status = ReadColorLine(target, colorLineTarget, walk, budget,
            out var colorLine, out diagnostic);
        if (status != ReadStatus.Done) return status;
        ColrPaint built = format switch
        {
            4 => new ColrPaintLinearGradient(0,
                RawFword((int)target + 4), RawFword((int)target + 6),
                RawFword((int)target + 8), RawFword((int)target + 10),
                RawFword((int)target + 12), RawFword((int)target + 14), colorLine!),
            6 => new ColrPaintRadialGradient(0,
                RawFword((int)target + 4), RawFword((int)target + 6),
                ReadWire.U16(_data, (int)target + 8),
                RawFword((int)target + 10), RawFword((int)target + 12),
                ReadWire.U16(_data, (int)target + 14), colorLine!),
            _ => new ColrPaintSweepGradient(0,
                RawFword((int)target + 4), RawFword((int)target + 6),
                ReadWire.U16(_data, (int)target + 8),
                ReadWire.U16(_data, (int)target + 10), colorLine!),
        };
        StoreNode(walk, nodeIdx, built);
        return ReadStatus.Done;
    }

    private ReadStatus VisitPaintGlyph(uint target, int nodeIdx, PaintWalk walk,
        BuildBudget budget, out FontDiagnostic diagnostic)
    {
        diagnostic = default;
        ushort glyphId = ReadWire.U16(_data, (int)target + 4);
        if (glyphId >= (ushort)walk.NumGlyphs)
            return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                Location.AtLocal(DataField.GlyphId, target + 4), out diagnostic,
                glyphId, (ulong)walk.NumGlyphs);
        uint paintTarget = target + U24((int)target + 1);
        ReadStatus status = VisitDirectEdge(target, nodeIdx, target + 1, paintTarget,
            walk, budget, out diagnostic);
        if (status != ReadStatus.Done) return status;
        StoreNode(walk, nodeIdx, new ColrPaintGlyph(0, walk.Visited[paintTarget], glyphId));
        return ReadStatus.Done;
    }

    private ReadStatus VisitPaintColrGlyph(uint target, int nodeIdx, PaintWalk walk,
        BuildBudget budget, out FontDiagnostic diagnostic)
    {
        diagnostic = default;
        ushort glyphId = ReadWire.U16(_data, (int)target + 1);
        if (glyphId >= (ushort)walk.NumGlyphs)
            return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                Location.AtLocal(DataField.GlyphId, target + 1), out diagnostic,
                glyphId, (ulong)walk.NumGlyphs);
        // "must be a glyphID found in a BaseGlyphPaintRecord" (L1030) with the
        // missing record named "not well formed" (L566(3)/L1032).
        if (!walk.BaseRoots.TryGetValue(glyphId, out uint rootTarget))
            return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.GlyphNotMapped,
                Location.AtLocal(DataField.GlyphId, target + 1), out diagnostic,
                glyphId, 0UL);
        // The indirect edge: the target joins this graph as a child sub-graph
        // (L1032); a cycle back through a root is the path-set's face.
        ReadStatus status = VisitIndirectEdge(target, nodeIdx, rootTarget, walk,
            budget, out diagnostic);
        if (status != ReadStatus.Done) return status;
        StoreNode(walk, nodeIdx, new ColrPaintColrGlyph(0, glyphId,
            walk.Visited[rootTarget]));
        return ReadStatus.Done;
    }

    private ReadStatus VisitPaintTransform(uint target, int nodeIdx, PaintWalk walk,
        BuildBudget budget, out FontDiagnostic diagnostic)
    {
        diagnostic = default;
        uint transformTarget = target + U24((int)target + 4);
        // The Affine2x3 window: 24 bytes of Fixed words (L1060-1069). The corpus
        // carries one host with 52 of these windows running past the table end -
        // the frozen negative witness this face refuses.
        if (transformTarget >= (ulong)_data.Length)
            return ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.RangeOutsideSource,
                Location.AtLocal(DataField.Offset, target + 4), out diagnostic,
                transformTarget, (ulong)_data.Length);
        if (transformTarget + ColrLayout.Affine2x3Size > (ulong)_data.Length)
            return ReadWire.Fail(ReadStatus.Truncated, DiagnosticCode.TruncatedData,
                Location.AtLocal(DataField.Payload, transformTarget), out diagnostic,
                (ulong)_data.Length, transformTarget + (ulong)ColrLayout.Affine2x3Size);
        var transform = new ColrAffine2x3(
            ReadWire.U32(_data, (int)transformTarget),
            ReadWire.U32(_data, (int)transformTarget + 4),
            ReadWire.U32(_data, (int)transformTarget + 8),
            ReadWire.U32(_data, (int)transformTarget + 12),
            ReadWire.U32(_data, (int)transformTarget + 16),
            ReadWire.U32(_data, (int)transformTarget + 20));
        walk.MaxEnd = Math.Max(walk.MaxEnd, transformTarget + (ulong)ColrLayout.Affine2x3Size);
        uint paintTarget = target + U24((int)target + 1);
        ReadStatus status = VisitDirectEdge(target, nodeIdx, target + 1, paintTarget,
            walk, budget, out diagnostic);
        if (status != ReadStatus.Done) return status;
        StoreNode(walk, nodeIdx, new ColrPaintTransform(0, walk.Visited[paintTarget],
            transform));
        return ReadStatus.Done;
    }

    private ReadStatus VisitPaintScalarFamily(uint target, byte format, int nodeIdx,
        PaintWalk walk, BuildBudget budget, out FontDiagnostic diagnostic)
    {
        diagnostic = default;
        uint paintTarget = target + U24((int)target + 1);
        ReadStatus status = VisitDirectEdge(target, nodeIdx, target + 1, paintTarget,
            walk, budget, out diagnostic);
        if (status != ReadStatus.Done) return status;
        int child = walk.Visited[paintTarget];
        ushort w4 = ReadWire.U16(_data, (int)target + 4);
        ushort w6 = ReadWire.U16(_data, (int)target + 6);
        ushort w8 = ReadWire.U16(_data, (int)target + 8);
        ushort w10 = ReadWire.U16(_data, (int)target + 10);
        ColrPaint built = format switch
        {
            14 => new ColrPaintTranslate(0, child, RawFword((int)target + 4),
                RawFword((int)target + 6)),
            16 => new ColrPaintScale(0, child, w4, w6),
            18 => new ColrPaintScaleAroundCenter(0, child, w4, w6,
                RawFword((int)target + 8), RawFword((int)target + 10)),
            20 => new ColrPaintScaleUniform(0, child, w4),
            22 => new ColrPaintScaleUniformAroundCenter(0, child, w4,
                RawFword((int)target + 6), RawFword((int)target + 8)),
            24 => new ColrPaintRotate(0, child, w4),
            26 => new ColrPaintRotateAroundCenter(0, child, w4,
                RawFword((int)target + 6), RawFword((int)target + 8)),
            28 => new ColrPaintSkew(0, child, w4, w6),
            _ => new ColrPaintSkewAroundCenter(0, child, w4, w6,
                RawFword((int)target + 8), RawFword((int)target + 10)),
        };
        StoreNode(walk, nodeIdx, built);
        return ReadStatus.Done;
    }

    private ReadStatus VisitPaintComposite(uint target, int nodeIdx, PaintWalk walk,
        BuildBudget budget, out FontDiagnostic diagnostic)
    {
        diagnostic = default;
        byte compositeMode = _data[(int)target + 4];
        // Above the 0..27 enumeration the renderer must fall back to
        // COMPOSITE_CLEAR (L1395's second sentence) - the observation face
        // (OQ-T6-02-8); the value travels verbatim either way.
        uint sourceTarget = target + U24((int)target + 1);
        ReadStatus status = VisitDirectEdge(target, nodeIdx, target + 1, sourceTarget,
            walk, budget, out diagnostic);
        if (status != ReadStatus.Done) return status;
        int source = walk.Visited[sourceTarget];
        uint backdropTarget = target + U24((int)target + 5);
        status = VisitDirectEdge(target, nodeIdx, target + 5, backdropTarget, walk,
            budget, out diagnostic);
        if (status != ReadStatus.Done) return status;
        StoreNode(walk, nodeIdx, new ColrPaintComposite(0, source, compositeMode,
            walk.Visited[backdropTarget]));
        return ReadStatus.Done;
    }

    /// <summary>The direct-link edge face (L476's "only limitation" plus the
    /// L564(1) window and the path-set cycle check, all answered at the parent's
    /// offset field).</summary>
    private ReadStatus VisitDirectEdge(uint nodeTarget, int nodeIdx, ulong fieldPosition,
        uint childTarget, PaintWalk walk, BuildBudget budget, out FontDiagnostic diagnostic)
    {
        diagnostic = default;
        if (childTarget >= (ulong)_data.Length)
            return ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.RangeOutsideSource,
                Location.AtLocal(DataField.Offset, fieldPosition), out diagnostic,
                childTarget, (ulong)_data.Length);
        if (childTarget <= nodeTarget)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.AtLocal(DataField.Offset, fieldPosition), out diagnostic,
                childTarget, nodeTarget);
        if (walk.Path.Contains(childTarget))
        {
            // The cycle face (L567 necessary; L588 is the renderer's recovery,
            // not a legalization): Detail names the on-path node the edge
            // closes onto.
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.AtLocal(DataField.Offset, fieldPosition), out diagnostic,
                childTarget, 0UL);
        }
        ReadStatus status = VisitPaint(childTarget, walk.Depths[nodeIdx] + 1, walk,
            budget, out diagnostic);
        if (status != ReadStatus.Done) return status;
        walk.Edges.Add((nodeIdx, walk.Visited[childTarget]));
        return ReadStatus.Done;
    }

    /// <summary>The indirect edge (PaintColrLayers slices, PaintColrGlyph
    /// targets): the window is answered upstream (the LayerList walk, the BGL
    /// walk), so only the cycle check and the recursion run here.</summary>
    private ReadStatus VisitIndirectEdge(uint nodeTarget, int nodeIdx, uint childTarget,
        PaintWalk walk, BuildBudget budget, out FontDiagnostic diagnostic)
    {
        diagnostic = default;
        if (walk.Path.Contains(childTarget))
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.AtLocal(DataField.Payload, nodeTarget), out diagnostic,
                childTarget, 0UL);
        ReadStatus status = VisitPaint(childTarget, walk.Depths[nodeIdx] + 1, walk,
            budget, out diagnostic);
        if (status != ReadStatus.Done) return status;
        walk.Edges.Add((nodeIdx, walk.Visited[childTarget]));
        return ReadStatus.Done;
    }

    /// <summary>A static ColorLine read (spec L817-845): the Extend byte travels
    /// verbatim (an out-of-domain value is the renderer's should-use-PAD
    /// fallback, an observation - OQ-T6-02-8), the stops keep wire order (the
    /// stopOffset order is the application's sorting concern, L833 - never
    /// checked, 9 corpus hosts carry genuinely unsorted lines), each palette
    /// entry re-answers the cross-table domain with the 0xFFFF foreground
    /// sentinel passing through (the T6-01 contract), and the raw F2DOT14 alpha
    /// is observation-only.</summary>
    private ReadStatus ReadColorLine(uint paintTarget, uint colorLineTarget,
        PaintWalk walk, BuildBudget budget, out ColrColorLine? colorLine,
        out FontDiagnostic diagnostic)
    {
        colorLine = null;
        if (colorLineTarget >= (ulong)_data.Length)
            return ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.RangeOutsideSource,
                Location.AtLocal(DataField.Offset, paintTarget + 1), out diagnostic,
                colorLineTarget, (ulong)_data.Length);
        if (colorLineTarget + ColrLayout.ColorLineHeaderSize > (ulong)_data.Length)
            return ReadWire.Fail(ReadStatus.Truncated, DiagnosticCode.TruncatedData,
                Location.AtLocal(DataField.Count, colorLineTarget + 1), out diagnostic,
                (ulong)_data.Length, colorLineTarget + (ulong)ColrLayout.ColorLineHeaderSize);
        byte extend = _data[(int)colorLineTarget];
        ushort numStops = ReadWire.U16(_data, (int)colorLineTarget + 1);
        // numStops==0 has no spec clause either way - the empty line is legal
        // (the OQ-T6-02-8 attachment).
        var stops = new ColrColorStop[numStops];
        for (uint i = 0; i < numStops; i++)
        {
            ulong stop = colorLineTarget + 3 + (ulong)i * ColrLayout.ColorStopSize;
            if (stop + ColrLayout.ColorStopSize > (ulong)_data.Length)
            {
                ulong available = (ulong)_data.Length - stop;
                ulong fieldStart = available switch
                {
                    < 2 => stop,
                    < 4 => stop + 2,
                    _ => stop + 4,
                };
                return ReadWire.Fail(ReadStatus.Truncated, DiagnosticCode.TruncatedData,
                    Location.AtLocal(DataField.Payload, fieldStart, unchecked((int)i)),
                    out diagnostic, (ulong)_data.Length, stop + ColrLayout.ColorStopSize);
            }
            ushort paletteIndex = ReadWire.U16(_data, (int)stop + 2);
            ReadStatus status = RequirePaletteIndex(stop + 2, paletteIndex, walk, out diagnostic);
            if (status != ReadStatus.Done) return status;
            stops[i] = new ColrColorStop(RawFword((int)stop),
                paletteIndex, ReadWire.U16(_data, (int)stop + 4));
        }
        if (!budget.Work(4L * numStops, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        walk.StopCount += numStops;
        walk.MaxEnd = Math.Max(walk.MaxEnd, colorLineTarget + 3 + 6UL * numStops);
        colorLine = new ColrColorLine(extend, stops);
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>The cross-table paletteIndex domain (spec L782-786): every value
    /// but the 0xFFFF foreground sentinel must sit below the CPAL table's
    /// numPaletteEntries; a null cpal authority skips the domain (the read-side
    /// OQ-T6-01-4 line).</summary>
    private ReadStatus RequirePaletteIndex(ulong fieldPosition, ushort paletteIndex,
        PaintWalk walk, out FontDiagnostic diagnostic)
    {
        diagnostic = default;
        if (walk.CpalEntries is int entries &&
            paletteIndex != ColrLayout.ForegroundPaletteIndex &&
            paletteIndex >= (uint)entries)
            return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                Location.AtLocal(DataField.Payload, fieldPosition), out diagnostic,
                paletteIndex, unchecked((ulong)(long)entries));
        return ReadStatus.Done;
    }

    /// <summary>The packed paint head window with per-field truncation
    /// provenance (the first incomplete field names itself, the T4-10 family
    /// line).</summary>
    private ReadStatus RequirePaintHead(uint target, byte format, out FontDiagnostic diagnostic)
    {
        ulong length = (ulong)_data.Length;
        switch (format)
        {
            case 1:
                if (target + 2 > length) return TruncateAt(target, 1, 2, DataField.Payload, length, out diagnostic);
                if (target + 6 > length) return TruncateAt(target, 2, 6, DataField.Count, length, out diagnostic);
                break;
            case 2:
                if (target + 3 > length) return TruncateAt(target, 1, 3, DataField.Payload, length, out diagnostic);
                if (target + 5 > length) return TruncateAt(target, 3, 5, DataField.Payload, length, out diagnostic);
                break;
            case 4 or 6:
                if (target + 4 > length) return TruncateAt(target, 1, 4, DataField.Offset, length, out diagnostic);
                if (target + 6 > length) return TruncateAt(target, 4, 6, DataField.Payload, length, out diagnostic);
                if (target + 8 > length) return TruncateAt(target, 6, 8, DataField.Payload, length, out diagnostic);
                if (target + 10 > length) return TruncateAt(target, 8, 10, DataField.Payload, length, out diagnostic);
                if (target + 12 > length) return TruncateAt(target, 10, 12, DataField.Payload, length, out diagnostic);
                if (target + 14 > length) return TruncateAt(target, 12, 14, DataField.Payload, length, out diagnostic);
                if (target + 16 > length) return TruncateAt(target, 14, 16, DataField.Payload, length, out diagnostic);
                break;
            case 8:
                if (target + 4 > length) return TruncateAt(target, 1, 4, DataField.Offset, length, out diagnostic);
                if (target + 6 > length) return TruncateAt(target, 4, 6, DataField.Payload, length, out diagnostic);
                if (target + 8 > length) return TruncateAt(target, 6, 8, DataField.Payload, length, out diagnostic);
                if (target + 10 > length) return TruncateAt(target, 8, 10, DataField.Payload, length, out diagnostic);
                if (target + 12 > length) return TruncateAt(target, 10, 12, DataField.Payload, length, out diagnostic);
                break;
            case 10:
                if (target + 4 > length) return TruncateAt(target, 1, 4, DataField.Offset, length, out diagnostic);
                if (target + 6 > length) return TruncateAt(target, 4, 6, DataField.GlyphId, length, out diagnostic);
                break;
            case 11:
                if (target + 3 > length) return TruncateAt(target, 1, 3, DataField.GlyphId, length, out diagnostic);
                break;
            case 12:
                if (target + 4 > length) return TruncateAt(target, 1, 4, DataField.Offset, length, out diagnostic);
                if (target + 7 > length) return TruncateAt(target, 4, 7, DataField.Offset, length, out diagnostic);
                break;
            case 14 or 16:
                if (target + 4 > length) return TruncateAt(target, 1, 4, DataField.Offset, length, out diagnostic);
                if (target + 6 > length) return TruncateAt(target, 4, 6, DataField.Payload, length, out diagnostic);
                if (target + 8 > length) return TruncateAt(target, 6, 8, DataField.Payload, length, out diagnostic);
                break;
            case 18:
                if (target + 4 > length) return TruncateAt(target, 1, 4, DataField.Offset, length, out diagnostic);
                if (target + 6 > length) return TruncateAt(target, 4, 6, DataField.Payload, length, out diagnostic);
                if (target + 8 > length) return TruncateAt(target, 6, 8, DataField.Payload, length, out diagnostic);
                if (target + 10 > length) return TruncateAt(target, 8, 10, DataField.Payload, length, out diagnostic);
                if (target + 12 > length) return TruncateAt(target, 10, 12, DataField.Payload, length, out diagnostic);
                break;
            case 20 or 24:
                if (target + 4 > length) return TruncateAt(target, 1, 4, DataField.Offset, length, out diagnostic);
                if (target + 6 > length) return TruncateAt(target, 4, 6, DataField.Payload, length, out diagnostic);
                break;
            case 22 or 26:
                if (target + 4 > length) return TruncateAt(target, 1, 4, DataField.Offset, length, out diagnostic);
                if (target + 6 > length) return TruncateAt(target, 4, 6, DataField.Payload, length, out diagnostic);
                if (target + 8 > length) return TruncateAt(target, 6, 8, DataField.Payload, length, out diagnostic);
                if (target + 10 > length) return TruncateAt(target, 8, 10, DataField.Payload, length, out diagnostic);
                break;
            case 28:
                if (target + 4 > length) return TruncateAt(target, 1, 4, DataField.Offset, length, out diagnostic);
                if (target + 6 > length) return TruncateAt(target, 4, 6, DataField.Payload, length, out diagnostic);
                if (target + 8 > length) return TruncateAt(target, 6, 8, DataField.Payload, length, out diagnostic);
                break;
            case 30:
                if (target + 4 > length) return TruncateAt(target, 1, 4, DataField.Offset, length, out diagnostic);
                if (target + 6 > length) return TruncateAt(target, 4, 6, DataField.Payload, length, out diagnostic);
                if (target + 8 > length) return TruncateAt(target, 6, 8, DataField.Payload, length, out diagnostic);
                if (target + 10 > length) return TruncateAt(target, 8, 10, DataField.Payload, length, out diagnostic);
                if (target + 12 > length) return TruncateAt(target, 10, 12, DataField.Payload, length, out diagnostic);
                break;
            default: // 32
                if (target + 4 > length) return TruncateAt(target, 1, 4, DataField.Offset, length, out diagnostic);
                if (target + 5 > length) return TruncateAt(target, 4, 5, DataField.Payload, length, out diagnostic);
                if (target + 8 > length) return TruncateAt(target, 5, 8, DataField.Offset, length, out diagnostic);
                break;
        }
        diagnostic = default;
        return ReadStatus.Done;
    }

    private ReadStatus TruncateAt(uint target, int start, int end, DataField field,
        ulong length, out FontDiagnostic diagnostic)
    {
        diagnostic = new(DiagnosticCode.TruncatedData,
            Location.AtLocal(field, target + (uint)start), length,
            target + (uint)end);
        return ReadStatus.Truncated;
    }

    private ReadStatus RefusePaintFormat(uint target, byte format,
        out FontDiagnostic diagnostic) =>
        ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
            Location.AtLocal(DataField.Version, target), out diagnostic, format, 0UL);

    private static int PaintSize(byte format) => format switch
    {
        1 => ColrLayout.PaintColrLayersSize,
        2 => ColrLayout.PaintSolidSize,
        4 => ColrLayout.PaintLinearGradientSize,
        6 => ColrLayout.PaintRadialGradientSize,
        8 => ColrLayout.PaintSweepGradientSize,
        10 => ColrLayout.PaintGlyphSize,
        11 => ColrLayout.PaintColrGlyphSize,
        12 => ColrLayout.PaintTransformSize,
        14 => ColrLayout.PaintTranslateSize,
        16 => ColrLayout.PaintScaleSize,
        18 => ColrLayout.PaintScaleAroundCenterSize,
        20 => ColrLayout.PaintScaleUniformSize,
        22 => ColrLayout.PaintScaleUniformAroundCenterSize,
        24 => ColrLayout.PaintRotateSize,
        26 => ColrLayout.PaintRotateAroundCenterSize,
        28 => ColrLayout.PaintSkewSize,
        30 => ColrLayout.PaintSkewAroundCenterSize,
        _ => ColrLayout.PaintCompositeSize, // 32
    };

    private static void RemapNodeChildren(ColrPaint node, int[] ordinal)
    {
        switch (node)
        {
            case ColrPaintLayers layers:
                for (int i = 0; i < layers.LayerNodeIdArray.Length; i++)
                    layers.LayerNodeIdArray[i] = ordinal[layers.LayerNodeIdArray[i]];
                break;
            case ColrPaintGlyph glyph:
                glyph.PaintNodeId = ordinal[glyph.PaintNodeId];
                break;
            case ColrPaintColrGlyph colrGlyph:
                colrGlyph.TargetNodeId = ordinal[colrGlyph.TargetNodeId];
                break;
            case ColrPaintTransform transform:
                transform.PaintNodeId = ordinal[transform.PaintNodeId];
                break;
            case ColrPaintTranslate translate:
                translate.PaintNodeId = ordinal[translate.PaintNodeId];
                break;
            case ColrPaintScale scale:
                scale.PaintNodeId = ordinal[scale.PaintNodeId];
                break;
            case ColrPaintScaleAroundCenter scaleAroundCenter:
                scaleAroundCenter.PaintNodeId = ordinal[scaleAroundCenter.PaintNodeId];
                break;
            case ColrPaintScaleUniform scaleUniform:
                scaleUniform.PaintNodeId = ordinal[scaleUniform.PaintNodeId];
                break;
            case ColrPaintScaleUniformAroundCenter scaleUniformAroundCenter:
                scaleUniformAroundCenter.PaintNodeId =
                    ordinal[scaleUniformAroundCenter.PaintNodeId];
                break;
            case ColrPaintRotate rotate:
                rotate.PaintNodeId = ordinal[rotate.PaintNodeId];
                break;
            case ColrPaintRotateAroundCenter rotateAroundCenter:
                rotateAroundCenter.PaintNodeId = ordinal[rotateAroundCenter.PaintNodeId];
                break;
            case ColrPaintSkew skew:
                skew.PaintNodeId = ordinal[skew.PaintNodeId];
                break;
            case ColrPaintSkewAroundCenter skewAroundCenter:
                skewAroundCenter.PaintNodeId = ordinal[skewAroundCenter.PaintNodeId];
                break;
            case ColrPaintComposite composite:
                composite.SourceNodeId = ordinal[composite.SourceNodeId];
                composite.BackdropNodeId = ordinal[composite.BackdropNodeId];
                break;
        }
    }

    private uint U24(int at) => (uint)((_data[at] << 16) | (_data[at + 1] << 8) | _data[at + 2]);

    /// <summary>A raw FWORD bit pattern: the reinterpret is unchecked by
    /// contract (the card's byte-faithful line - negative FWORDs are data).</summary>
    private short RawFword(int at) => unchecked((short)ReadWire.U16(_data, at));

    /// <summary>The v1 paint-graph walk's scratch state: keyed by the wire
    /// offset during the walk, renumbered to canonical topological ordinals
    /// afterwards. Never returned from validation.</summary>
    private sealed class PaintWalk
    {
        internal PaintWalk(int numGlyphs, int? cpalEntries, uint layerListOffset,
            uint[] layerTargets, bool[] layerReferenced,
            Dictionary<ushort, uint> baseRoots, bool materialize)
        {
            NumGlyphs = numGlyphs;
            CpalEntries = cpalEntries;
            LayerListOffset = layerListOffset;
            LayerTargets = layerTargets;
            LayerReferenced = layerReferenced;
            BaseRoots = baseRoots;
            Materialize = materialize;
        }

        internal int NumGlyphs;
        internal int? CpalEntries;
        internal uint LayerListOffset;
        internal uint[] LayerTargets;
        internal bool[] LayerReferenced;
        internal Dictionary<ushort, uint> BaseRoots;
        internal bool Materialize;

        internal readonly Dictionary<uint, int> Visited = new();
        internal readonly HashSet<uint> Path = [];
        // The placeholder list: validate mode never adds; materialize mode holds
        // one slot per visited node until the node's own frame replaces it.
        internal readonly List<ColrPaint> Nodes = [];
        internal readonly List<int> Depths = [];
        internal readonly List<int> Sequences = [];
        internal readonly List<(int Parent, int Child)> Edges = [];
        internal int Sequence;
        internal ulong MaxEnd;
        internal uint StopCount;

        internal void NoteRevisit(int nodeIdx, int depth)
        {
            if (depth < Depths[nodeIdx]) Depths[nodeIdx] = depth;
        }
    }

    /// <summary>Binary search over the BaseGlyphList (spec L710's "intended that
    /// a binary search can be used" as the read-side API). A miss is a normal
    /// outcome, not an error; on a v0 table there is no BaseGlyphList, so every
    /// glyph misses.</summary>
    public ReadStatus TryFindBaseGlyphV1(int glyphId, out int recordOrdinal,
        out FontDiagnostic diagnostic)
    {
        recordOrdinal = -1;
        if (!IsBound)
            return ReadWire.Fail(ReadStatus.Uninitialized, DiagnosticCode.UnboundView,
                Location, out diagnostic);
        if (glyphId < 0 || glyphId > ushort.MaxValue)
            return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                Location.AtLocal(DataField.GlyphId, 0), out diagnostic,
                unchecked((ulong)(long)glyphId), ushort.MaxValue);
        ReadStatus status = TryRequireKnownVersion(out ulong version, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (version != ColrLayout.Version1)
            return ReadWire.Fail(ReadStatus.NotFound, DiagnosticCode.GlyphNotMapped,
                Location.AtLocal(DataField.GlyphId, 0, glyphId), out diagnostic,
                (uint)glyphId);
        status = TryGetBaseGlyphListOffset(out uint baseGlyphList, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (baseGlyphList == 0)
            return ReadWire.Fail(ReadStatus.NotFound, DiagnosticCode.GlyphNotMapped,
                Location.AtLocal(DataField.GlyphId, 0, glyphId), out diagnostic,
                (uint)glyphId);
        status = TryRequireBaseGlyphPaintRecordExtent(out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = TryGetNumBaseGlyphPaintRecords(out uint count, out diagnostic);
        if (status != ReadStatus.Done) return status;
        uint low = 0, high = count;
        while (low < high)
        {
            uint mid = low + (high - low) / 2;
            status = TryGetBaseGlyphPaintRecord(unchecked((int)mid), out var record, out diagnostic);
            if (status != ReadStatus.Done) return status;
            if (record.GlyphId == glyphId)
            {
                recordOrdinal = unchecked((int)mid);
                diagnostic = default;
                return ReadStatus.Done;
            }
            if (record.GlyphId < glyphId) low = mid + 1;
            else high = mid;
        }
        return ReadWire.Fail(ReadStatus.NotFound, DiagnosticCode.GlyphNotMapped,
            Location.AtLocal(DataField.GlyphId, 0, glyphId), out diagnostic, (uint)glyphId);
    }

    /// <summary>The combined lookup in the spec's preference order (L651 "should
    /// give preference" to the v1 definition, L653's structural form: the
    /// BaseGlyphList first, the v0 compatibility records second).</summary>
    public ReadStatus TryFindColorGlyph(int glyphId, out ColrColorGlyphHit hit,
        out FontDiagnostic diagnostic)
    {
        hit = default;
        ReadStatus status = TryFindBaseGlyphV1(glyphId, out int paintOrdinal, out diagnostic);
        if (status == ReadStatus.Done)
        {
            hit = new ColrColorGlyphHit(true, paintOrdinal);
            return status;
        }
        if (status != ReadStatus.NotFound) return status;
        status = TryFindBaseGlyph(glyphId, out int recordOrdinal, out diagnostic);
        if (status == ReadStatus.Done)
        {
            hit = new ColrColorGlyphHit(false, recordOrdinal);
            diagnostic = default;
            return status;
        }
        return status;
    }

    /// <summary>Projects the ClipBox covering one glyph (the range hit is a
    /// projection, not a paint graph member). The clips' sorted, non-overlapping
    /// contract is the read-side assumption (the validation contract), so a
    /// binary search over the starts answers.</summary>
    public ReadStatus TryGetClipBox(int glyphId, out ColrClipBox clipBox,
        out FontDiagnostic diagnostic)
    {
        clipBox = default;
        if (!IsBound)
            return ReadWire.Fail(ReadStatus.Uninitialized, DiagnosticCode.UnboundView,
                Location, out diagnostic);
        if (glyphId < 0 || glyphId > ushort.MaxValue)
            return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                Location.AtLocal(DataField.GlyphId, 0), out diagnostic,
                unchecked((ulong)(long)glyphId), ushort.MaxValue);
        ReadStatus status = TryRequireKnownVersion(out ulong version, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (version != ColrLayout.Version1)
            return ReadWire.Fail(ReadStatus.NotFound, DiagnosticCode.GlyphNotMapped,
                Location.AtLocal(DataField.GlyphId, 0, glyphId), out diagnostic,
                (uint)glyphId);
        status = TryGetClipListOffset(out uint clipList, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (clipList == 0 || clipList + 5 > (ulong)_data.Length ||
            _data[(int)clipList] != 1)
            return ReadWire.Fail(ReadStatus.NotFound, DiagnosticCode.GlyphNotMapped,
                Location.AtLocal(DataField.GlyphId, 0, glyphId), out diagnostic,
                (uint)glyphId);
        uint numClips = ReadWire.U32(_data, (int)clipList + 1);
        uint low = 0, high = numClips;
        while (low < high)
        {
            uint mid = low + (high - low) / 2;
            ulong row = clipList + 5 + (ulong)mid * ColrLayout.ClipRecordSize;
            if (row + ColrLayout.ClipRecordSize > (ulong)_data.Length)
                return ReadWire.Fail(ReadStatus.Truncated, DiagnosticCode.TruncatedData,
                    Location.AtLocal(DataField.None, row), out diagnostic,
                    (ulong)_data.Length, row + ColrLayout.ClipRecordSize);
            ushort start = ReadWire.U16(_data, (int)row);
            if (start <= glyphId) low = mid + 1;
            else high = mid;
        }
        if (low == 0)
            return ReadWire.Fail(ReadStatus.NotFound, DiagnosticCode.GlyphNotMapped,
                Location.AtLocal(DataField.GlyphId, 0, glyphId), out diagnostic,
                (uint)glyphId);
        ulong hitRow = clipList + 5 + (ulong)(low - 1) * ColrLayout.ClipRecordSize;
        ushort hitStart = ReadWire.U16(_data, (int)hitRow);
        ushort hitEnd = ReadWire.U16(_data, (int)hitRow + 2);
        if (glyphId < hitStart || glyphId > hitEnd)
            return ReadWire.Fail(ReadStatus.NotFound, DiagnosticCode.GlyphNotMapped,
                Location.AtLocal(DataField.GlyphId, 0, glyphId), out diagnostic,
                (uint)glyphId);
        uint boxOffset = U24((int)hitRow + 4);
        ulong box = clipList + boxOffset;
        if (box + ColrLayout.ClipBoxFormat1Size > (ulong)_data.Length)
            return ReadWire.Fail(ReadStatus.Truncated, DiagnosticCode.TruncatedData,
                Location.AtLocal(DataField.Payload, box), out diagnostic,
                (ulong)_data.Length, box + ColrLayout.ClipBoxFormat1Size);
        clipBox = new ColrClipBox(
            RawFword((int)box + 1),
            RawFword((int)box + 3),
            RawFword((int)box + 5),
            RawFword((int)box + 7));
        diagnostic = default;
        return ReadStatus.Done;
    }
}

/// <summary>One TryFindColorGlyph hit in the preference order (L651/L653): which
/// face answered and the row ordinal in that face's array (the BaseGlyphList
/// record ordinal when FromPaintGraph, otherwise the v0 base glyph record
/// ordinal).</summary>
public readonly record struct ColrColorGlyphHit(bool FromPaintGraph, int Ordinal);
