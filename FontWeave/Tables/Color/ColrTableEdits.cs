using FontWeave.Core;

namespace FontWeave.Tables;

/// <summary>The version 1 value-level edit family (card T6-02 §4.1): solid
/// alpha, single color stops, the Affine2x3 tuple, the transform scalar
/// families, the composite mode and the clip box. All six are value-level
/// clone-on-write over the stable node ids; none rewires the graph. Structural
/// changes - node additions, removals, child changes, sharing changes, version
/// conversion - stay finite refusals outside the surface (OQ-T6-02-14);
/// consumers re-root through ColrTableCompiler.TryCreate. The scalar words are
/// raw wire values, so the only domain re-answered beyond each family's own is
/// the color stop's cross-table palette entry (0xFFFF sentinel through, the
/// T6-01 contract).</summary>
public sealed partial class ColrTable
{
    private enum ColrPaintFamily : byte
    {
        Solid = 0, Gradient = 1, Transform = 2, Scalar = 3, Composite = 4,
    }

    /// <summary>Atomically moves one PaintSolid's alpha (the raw F2DOT14 word;
    /// out-of-range values are the renderer's clamp, an observation -
    /// OQ-T6-02-8).</summary>
    public ReadStatus TryWithPaintSolidAlpha(int nodeId, ushort alpha,
        out ColrTable? updated, out FontDiagnostic diagnostic)
    {
        updated = null;
        ReadStatus status = RequirePaintNode(nodeId, ColrPaintFamily.Solid, out var node,
            out diagnostic);
        if (status != ReadStatus.Done) return status;
        var solid = (ColrPaintSolid)node;
        var paints = (ColrPaint[])_paints.Clone();
        paints[nodeId] = new ColrPaintSolid(nodeId, solid.PaletteIndex, alpha);
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

    /// <summary>Atomically replaces one PaintTransform's Affine2x3 (six raw
    /// Fixed words, bit-faithful).</summary>
    public ReadStatus TryWithTransform(int nodeId, ColrAffine2x3 transform,
        out ColrTable? updated, out FontDiagnostic diagnostic)
    {
        updated = null;
        ReadStatus status = RequirePaintNode(nodeId, ColrPaintFamily.Transform, out var node,
            out diagnostic);
        if (status != ReadStatus.Done) return status;
        var transformNode = (ColrPaintTransform)node;
        var paints = (ColrPaint[])_paints.Clone();
        paints[nodeId] = new ColrPaintTransform(nodeId, transformNode.PaintNodeId, transform);
        updated = WithPaints(paints);
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>Atomically replaces one translate/scale/rotate/skew node's
    /// scalar words, in wire order and bit-faithful. The word count must match
    /// the node's format exactly (translate/scale/skew 2, uniform/rotate 1,
    /// around-center 3, scale/skew-around-center 4); any other count is a
    /// structural refusal.</summary>
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
            20 or 24 => 1,
            22 or 26 => 3,
            18 or 30 => 4,
            _ => 2, // 14, 16, 28
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

    /// <summary>Atomically replaces one clip's format-1 box (four raw FWORDs,
    /// no order relation between them - the spec carries none).</summary>
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
            ColrPaintFamily.Solid => node is ColrPaintSolid,
            ColrPaintFamily.Gradient => node is ColrPaintLinearGradient
                or ColrPaintRadialGradient or ColrPaintSweepGradient,
            ColrPaintFamily.Transform => node is ColrPaintTransform,
            ColrPaintFamily.Scalar => node.Format is 14 or 16 or 18 or 20 or 22 or 24
                or 26 or 28 or 30,
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
}
