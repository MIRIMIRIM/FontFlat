using FontWeave.Core;
using FontWeave.Tables.Variation;

namespace FontWeave.Tables;

/// <summary>The version 1 value-level edit family (card T6-02 §4.1, T6-03 §4):
/// solid alpha, single color stops, the Affine2x3 tuple, the transform scalar
/// families, the composite mode and the clip box - each accepting its var twin
/// as the same value-level write (scalar words, varIndexBase across the full
/// u32 domain with the sentinel through, var stops, var affines). All are
/// value-level clone-on-write over the stable node ids; none rewires the
/// graph, and no edit flips a node's static/var face. Structural changes -
/// node additions, removals, child changes, sharing changes, version
/// conversion, static↔var conversion, var slot add/remove, map/IVS add/remove -
/// stay finite refusals outside the surface (OQ-T6-02-14, OQ-T6-03-10);
/// consumers re-root through ColrTableCompiler.TryCreate. The scalar words are
/// raw wire values, so the only domain re-answered beyond each family's own is
/// the color stop's cross-table palette entry (0xFFFF sentinel through, the
/// T6-01 contract).</summary>
public sealed partial class ColrTable
{
    private enum ColrPaintFamily : byte
    {
        Solid = 0, Gradient = 1, Transform = 2, Scalar = 3, Composite = 4,
        VarBase = 5,
    }

    /// <summary>Atomically moves one PaintSolid's alpha (the raw F2DOT14 word;
    /// out-of-range values are the renderer's clamp, an observation -
    /// OQ-T6-02-8). The var solid (format 3) answers the same face: the alpha
    /// word is its variable slot and the edit keeps its varIndexBase verbatim.</summary>
    public ReadStatus TryWithPaintSolidAlpha(int nodeId, ushort alpha,
        out ColrTable? updated, out FontDiagnostic diagnostic)
    {
        updated = null;
        ReadStatus status = RequirePaintNode(nodeId, ColrPaintFamily.Solid, out var node,
            out diagnostic);
        if (status != ReadStatus.Done) return status;
        var paints = (ColrPaint[])_paints.Clone();
        paints[nodeId] = node switch
        {
            ColrPaintVarSolid varSolid => new ColrPaintVarSolid(nodeId,
                varSolid.PaletteIndex, alpha, varSolid.VarIndexBase),
            _ => new ColrPaintSolid(nodeId, ((ColrPaintSolid)node).PaletteIndex, alpha),
        };
        updated = WithPaints(paints);
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>Atomically replaces one ColorStop on a gradient's color line;
    /// the cross-table palette domain re-answers the caller's explicit
    /// numPaletteEntries with the 0xFFFF foreground sentinel through (the
    /// read-side OQ-T6-01-4 line). The stop's raw stopOffset keeps wire order
    /// semantics - no order is enforced (L833, OQ-T6-02-8).</summary>
    public ReadStatus TryWithColorStop(int nodeId, int stopOrdinal, ColrColorStop stop,
        int? cpalNumPaletteEntries, out ColrTable? updated, out FontDiagnostic diagnostic)
    {
        updated = null;
        ReadStatus status = RequirePaintNode(nodeId, ColrPaintFamily.Gradient,
            out var node, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (node is not (ColrPaintLinearGradient or ColrPaintRadialGradient
            or ColrPaintSweepGradient))
            return RefuseStaticVarFace(nodeId, node, out diagnostic);
        var gradient = (ColrPaintLinearGradient)node;
        var stops = gradient.ColorLine.StopArray;
        if ((uint)stopOrdinal >= (uint)stops.Length)
            return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                Location.AtLocal(DataField.Payload, ColorLineSourcePosition(nodeId) + 3,
                    stopOrdinal), out diagnostic, unchecked((ulong)(long)stopOrdinal),
                (ulong)stops.Length);
        if (cpalNumPaletteEntries is int entries &&
            stop.PaletteIndex != ColrLayout.ForegroundPaletteIndex &&
            stop.PaletteIndex >= (uint)entries)
            return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                Location.AtLocal(DataField.Payload,
                    ColorLineSourcePosition(nodeId) + 5UL + 6UL * (ulong)stopOrdinal,
                    stopOrdinal),
                out diagnostic, stop.PaletteIndex, unchecked((ulong)(long)entries));
        var newStops = (ColrColorStop[])stops.Clone();
        newStops[stopOrdinal] = stop;
        var colorLine = new ColrColorLine(gradient.ColorLine.Extend, newStops);
        var paints = (ColrPaint[])_paints.Clone();
        paints[nodeId] = node switch
        {
            ColrPaintLinearGradient linear => linear.WithColorLine(colorLine),
            ColrPaintRadialGradient radial => radial.WithColorLine(colorLine),
            _ => ((ColrPaintSweepGradient)node).WithColorLine(colorLine),
        };
        updated = WithPaints(paints);
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>Atomically replaces one ColorStop on a var gradient's
    /// VarColorLine (T6-03 §4): the palette entry re-answers the cross-table
    /// domain exactly like the static stop's; the stop's own varIndexBase rides
    /// the full u32 domain with the sentinel through.</summary>
    public ReadStatus TryWithColorStop(int nodeId, int stopOrdinal, ColrVarColorStop stop,
        int? cpalNumPaletteEntries, out ColrTable? updated, out FontDiagnostic diagnostic)
    {
        updated = null;
        ReadStatus status = RequirePaintNode(nodeId, ColrPaintFamily.Gradient,
            out var node, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (node is not (ColrPaintVarLinearGradient or ColrPaintVarRadialGradient
            or ColrPaintVarSweepGradient))
            return RefuseStaticVarFace(nodeId, node, out diagnostic);
        var line = node switch
        {
            ColrPaintVarLinearGradient linear => linear.ColorLine,
            ColrPaintVarRadialGradient radial => radial.ColorLine,
            _ => ((ColrPaintVarSweepGradient)node).ColorLine,
        };
        var stops = line.StopArray;
        if ((uint)stopOrdinal >= (uint)stops.Length)
            return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                Location.AtLocal(DataField.Payload, ColorLineSourcePosition(nodeId) + 3,
                    stopOrdinal), out diagnostic, unchecked((ulong)(long)stopOrdinal),
                (ulong)stops.Length);
        if (cpalNumPaletteEntries is int entries &&
            stop.PaletteIndex != ColrLayout.ForegroundPaletteIndex &&
            stop.PaletteIndex >= (uint)entries)
            return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                Location.AtLocal(DataField.Payload,
                    ColorLineSourcePosition(nodeId) + 5UL + 10UL * (ulong)stopOrdinal,
                    stopOrdinal),
                out diagnostic, stop.PaletteIndex, unchecked((ulong)(long)entries));
        var newStops = (ColrVarColorStop[])stops.Clone();
        newStops[stopOrdinal] = stop;
        var colorLine = new ColrVarColorLine(line.Extend, newStops);
        var paints = (ColrPaint[])_paints.Clone();
        paints[nodeId] = node switch
        {
            ColrPaintVarLinearGradient linear => linear.WithVarColorLine(colorLine),
            ColrPaintVarRadialGradient radial => radial.WithVarColorLine(colorLine),
            _ => ((ColrPaintVarSweepGradient)node).WithVarColorLine(colorLine),
        };
        updated = WithPaints(paints);
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>Atomically replaces one PaintTransform's Affine2x3 (six raw
    /// Fixed words, bit-faithful).</summary>
    public ReadStatus TryWithTransform(int nodeId, ColrAffine2x3 transform,
        out ColrTable? updated, out FontDiagnostic diagnostic)
    {
        updated = null;
        ReadStatus status = RequirePaintNode(nodeId, ColrPaintFamily.Transform, out var node,
            out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (node is not ColrPaintTransform)
            return RefuseStaticVarFace(nodeId, node, out diagnostic);
        var transformNode = (ColrPaintTransform)node;
        var paints = (ColrPaint[])_paints.Clone();
        paints[nodeId] = new ColrPaintTransform(nodeId, transformNode.PaintNodeId, transform);
        updated = WithPaints(paints);
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>Atomically replaces one PaintVarTransform's VarAffine2x3
    /// (T6-03 §4): the six raw Fixed words plus the node family's only
    /// varIndexBase, bit-faithful across the full u32 domain.</summary>
    public ReadStatus TryWithTransform(int nodeId, ColrVarAffine2x3 transform,
        out ColrTable? updated, out FontDiagnostic diagnostic)
    {
        updated = null;
        ReadStatus status = RequirePaintNode(nodeId, ColrPaintFamily.Transform, out var node,
            out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (node is not ColrPaintVarTransform)
            return RefuseStaticVarFace(nodeId, node, out diagnostic);
        var transformNode = (ColrPaintVarTransform)node;
        var paints = (ColrPaint[])_paints.Clone();
        paints[nodeId] = new ColrPaintVarTransform(nodeId, transformNode.PaintNodeId,
            transform);
        updated = WithPaints(paints);
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>Atomically moves one translate/scale/rotate/skew node's
    /// scalar words, in wire order and bit-faithful - the var twins answer the
    /// same face (T6-03 §4) and keep their varIndexBase verbatim. The word
    /// count must match the node's format exactly (translate/scale/skew 2,
    /// uniform/rotate 1, around-center 3, scale/skew-around-center 4; the var
    /// formats likewise); any other count is a structural refusal.</summary>
    public ReadStatus TryWithPaintScalar(int nodeId, IReadOnlyList<ushort> scalars,
        out ColrTable? updated, out FontDiagnostic diagnostic)
    {
        updated = null;
        ArgumentNullException.ThrowIfNull(scalars);
        ReadStatus status = RequirePaintNode(nodeId, ColrPaintFamily.Scalar, out var node,
            out diagnostic);
        if (status != ReadStatus.Done) return status;
        int expected = node.Format switch
        {
            14 or 16 or 28 => 2,
            15 or 17 or 29 => 2,
            18 or 22 or 23 or 26 or 27 or 30 => 3,
            19 or 31 => 4,
            _ => 1, // 20, 21, 24, 25
        };
        if (scalars.Count != expected)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.AtLocal(DataField.Payload, PaintSourcePosition(nodeId) + 4, nodeId),
                out diagnostic, (uint)scalars.Count, (uint)expected);
        var paints = (ColrPaint[])_paints.Clone();
        paints[nodeId] = node.WithScalars([.. scalars]);
        updated = WithPaints(paints);
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>Atomically moves one PaintComposite's mode inside the declared
    /// 0..27 enumeration. The read side observes out-of-domain values as the
    /// renderer's CLEAR fallback (OQ-T6-02-8), but the edit surface only writes
    /// declared ones - the T6-01 TryWithPaletteType line.</summary>
    public ReadStatus TryWithCompositeMode(int nodeId, byte compositeMode,
        out ColrTable? updated, out FontDiagnostic diagnostic)
    {
        updated = null;
        ReadStatus status = RequirePaintNode(nodeId, ColrPaintFamily.Composite, out var node,
            out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (compositeMode > ColrLayout.CompositeModeDomainMax)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.AtLocal(DataField.Payload, PaintSourcePosition(nodeId) + 4, nodeId),
                out diagnostic, compositeMode, (ulong)ColrLayout.CompositeModeDomainMax);
        var compositeNode = (ColrPaintComposite)node;
        var paints = (ColrPaint[])_paints.Clone();
        paints[nodeId] = new ColrPaintComposite(nodeId, compositeNode.SourceNodeId,
            compositeMode, compositeNode.BackdropNodeId);
        updated = WithPaints(paints);
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>Atomically replaces one clip's box - format 1 or format 2 (the
    /// 13-byte face with its varIndexBase verbatim, T6-03 §4). The four FWORDs
    /// carry no order relation between them (the spec carries none). A format
    /// flip is this family's declared value-level write: the box is a
    /// projection record, not a paint graph member, so its format moves with
    /// the written value (OQ-T6-03-3's observation face never binds the edit).</summary>
    public ReadStatus TryWithClipBox(int clipOrdinal, ColrClipBox box,
        out ColrTable? updated, out FontDiagnostic diagnostic)
    {
        updated = null;
        if (Version != 1)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.UnsupportedOperation,
                Location.AtLocal(DataField.None, 0), out diagnostic, (uint)Version, 1UL);
        if ((uint)clipOrdinal >= (uint)_clips.Length)
            return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                Location.AtLocal(DataField.GlyphId,
                    ClipListOffset + 5 + 7UL * (ulong)clipOrdinal, clipOrdinal),
                out diagnostic, unchecked((ulong)(long)clipOrdinal), (ulong)_clips.Length);
        var clips = (ColrClip[])_clips.Clone();
        clips[clipOrdinal] = new ColrClip(clips[clipOrdinal].StartGlyphId,
            clips[clipOrdinal].EndGlyphId, box);
        updated = new((ColrBaseGlyphRecord[])_baseGlyphs.Clone(),
            (ColrLayerRecord[])_layers.Clone(),
            (ColrBaseGlyphPaint[])_baseGlyphPaints.Clone(),
            (int[])_layerPaintNodeIds.Clone(), clips, _paints, NumGlyphs,
            BaseGlyphRecordsOffset, LayerRecordsOffset, VarIndexMapOffset,
            ItemVariationStoreOffset, ClipListOffset, VarIndexMap,
            ItemVariationStore, PaintSourcePositions, ColorLineSourcePositions,
            LayerSourceOffsets, Location, ConsumedLength);
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>Atomically moves one var node's own varIndexBase across the
    /// full u32 domain with the 0xFFFFFFFF sentinel through (T6-03 §4, the
    /// OQ-T6-03-10 value-level face). PaintVarTransform carries no own word -
    /// its slots ride the VarAffine2x3 and answer through the var transform
    /// edit; a static node is the family-mismatch refusal.</summary>
    public ReadStatus TryWithVarIndexBase(int nodeId, uint varIndexBase,
        out ColrTable? updated, out FontDiagnostic diagnostic)
    {
        updated = null;
        ReadStatus status = RequirePaintNode(nodeId, ColrPaintFamily.VarBase,
            out var node, out diagnostic);
        if (status != ReadStatus.Done) return status;
        var paints = (ColrPaint[])_paints.Clone();
        paints[nodeId] = node.WithVarIndexBase(varIndexBase);
        updated = WithPaints(paints);
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>Atomically swaps the variation index map for an equal-presence
    /// model (T6-03 §4's re-root: the map edit re-roots through
    /// DeltaSetIndexMapTableCompiler.TryWithMapping, then lands here). Adding
    /// or removing the slot is a structural refusal (OQ-T6-03-10).</summary>
    public ReadStatus TryWithVarIndexMap(DeltaSetIndexMapTable? varIndexMap,
        out ColrTable? updated, out FontDiagnostic diagnostic)
    {
        updated = null;
        if (Version != 1)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.UnsupportedOperation,
                Location.AtLocal(DataField.None, 0), out diagnostic, (uint)Version, 1UL);
        if ((VarIndexMap is null) != (varIndexMap is null))
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.UnsupportedOperation,
                Location.AtLocal(DataField.Offset, 26), out diagnostic,
                VarIndexMap is null ? 0UL : 1UL, VarIndexMap is null ? 1UL : 0UL);
        updated = new((ColrBaseGlyphRecord[])_baseGlyphs.Clone(),
            (ColrLayerRecord[])_layers.Clone(),
            (ColrBaseGlyphPaint[])_baseGlyphPaints.Clone(),
            (int[])_layerPaintNodeIds.Clone(), (ColrClip[])_clips.Clone(), _paints,
            NumGlyphs, BaseGlyphRecordsOffset, LayerRecordsOffset, VarIndexMapOffset,
            ItemVariationStoreOffset, ClipListOffset, varIndexMap,
            ItemVariationStore, PaintSourcePositions, ColorLineSourcePositions,
            LayerSourceOffsets, Location, ConsumedLength);
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>Atomically swaps the ItemVariationStore for an equal-presence
    /// model (T6-03 §4's re-root: the store's region/delta edits re-root
    /// through VariationStoreTableCompiler's own faces, then land here).
    /// Adding or removing the slot is a structural refusal (OQ-T6-03-10).</summary>
    public ReadStatus TryWithItemVariationStore(VariationStoreTable? itemVariationStore,
        out ColrTable? updated, out FontDiagnostic diagnostic)
    {
        updated = null;
        if (Version != 1)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.UnsupportedOperation,
                Location.AtLocal(DataField.None, 0), out diagnostic, (uint)Version, 1UL);
        if ((ItemVariationStore is null) != (itemVariationStore is null))
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.UnsupportedOperation,
                Location.AtLocal(DataField.Offset, 30), out diagnostic,
                ItemVariationStore is null ? 0UL : 1UL, ItemVariationStore is null ? 1UL : 0UL);
        updated = new((ColrBaseGlyphRecord[])_baseGlyphs.Clone(),
            (ColrLayerRecord[])_layers.Clone(),
            (ColrBaseGlyphPaint[])_baseGlyphPaints.Clone(),
            (int[])_layerPaintNodeIds.Clone(), (ColrClip[])_clips.Clone(), _paints,
            NumGlyphs, BaseGlyphRecordsOffset, LayerRecordsOffset, VarIndexMapOffset,
            ItemVariationStoreOffset, ClipListOffset, VarIndexMap,
            itemVariationStore, PaintSourcePositions, ColorLineSourcePositions,
            LayerSourceOffsets, Location, ConsumedLength);
        diagnostic = default;
        return ReadStatus.Done;
    }

    private ReadStatus RequirePaintNode(int nodeId, ColrPaintFamily family,
        out ColrPaint node, out FontDiagnostic diagnostic)
    {
        node = null!;
        if (Version != 1)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.UnsupportedOperation,
                Location.AtLocal(DataField.None, 0), out diagnostic, (uint)Version, 1UL);
        if ((uint)nodeId >= (uint)_paints.Length)
            return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                Location.AtLocal(DataField.Payload, 0, nodeId), out diagnostic,
                unchecked((ulong)(long)nodeId), (ulong)_paints.Length);
        node = _paints[nodeId];
        bool matches = family switch
        {
            ColrPaintFamily.Solid => node is ColrPaintSolid or ColrPaintVarSolid,
            ColrPaintFamily.Gradient => node is ColrPaintLinearGradient
                or ColrPaintRadialGradient or ColrPaintSweepGradient
                or ColrPaintVarLinearGradient or ColrPaintVarRadialGradient
                or ColrPaintVarSweepGradient,
            ColrPaintFamily.Transform => node is ColrPaintTransform
                or ColrPaintVarTransform,
            ColrPaintFamily.Scalar => node.Format is 14 or 16 or 18 or 20 or 22 or 24
                or 26 or 28 or 30 or 15 or 17 or 19 or 21 or 23 or 25 or 27 or 29 or 31,
            ColrPaintFamily.VarBase => node.Format is 3 or 5 or 7 or 9 or 15 or 17
                or 19 or 21 or 23 or 25 or 27 or 29 or 31,
            _ => node is ColrPaintComposite,
        };
        if (!matches)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.AtLocal(DataField.Payload, PaintSourcePosition(nodeId), nodeId),
                out diagnostic, node.Format, (uint)family);
        diagnostic = default;
        return ReadStatus.Done;
    }

    private ColrTable WithPaints(ColrPaint[] paints) =>
        new((ColrBaseGlyphRecord[])_baseGlyphs.Clone(), (ColrLayerRecord[])_layers.Clone(),
            (ColrBaseGlyphPaint[])_baseGlyphPaints.Clone(),
            (int[])_layerPaintNodeIds.Clone(), (ColrClip[])_clips.Clone(), paints,
            NumGlyphs, BaseGlyphRecordsOffset, LayerRecordsOffset, VarIndexMapOffset,
            ItemVariationStoreOffset, ClipListOffset, VarIndexMap,
            ItemVariationStore, PaintSourcePositions, ColorLineSourcePositions,
            LayerSourceOffsets, Location, ConsumedLength);

    // The source coordinates the edit diagnostics name: read models carry the
    // wire positions they were read from; factory models carry zeros and
    // address by node id alone.
    private ulong PaintSourcePosition(int nodeId) => PaintSourcePositions[nodeId];
    private ulong ColorLineSourcePosition(int nodeId) => ColorLineSourcePositions[nodeId];

    /// <summary>The static↔var conversion refusal (OQ-T6-03-10): a value-level
    /// edit never flips a node's face - the var twin's write answers through
    /// its own overload, and a format rewrite stays outside the surface.</summary>
    private ReadStatus RefuseStaticVarFace(int nodeId, ColrPaint node,
        out FontDiagnostic diagnostic) =>
        ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
            Location.AtLocal(DataField.Payload, PaintSourcePosition(nodeId), nodeId),
            out diagnostic, node.Format, 0UL);
}
