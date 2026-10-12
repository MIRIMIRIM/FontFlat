using FontWeave.Core;
using FontWeave.Schema;

namespace FontWeave.Tables;

internal static class ColrLayout
{
    internal const uint Tag = 0x434F4C52;
    internal const ushort Version0 = 0, Version1 = 1;
    internal const int HeaderLength = 14;
    // The v1 header is a fixed 34-byte face: the v0 words sit in place and five
    // Offset32 slots ride at the constant positions 14/18/22/26/30 (spec L620-633)
    // - a header-constant extension, unlike CPAL v1's runtime-positioned tail.
    internal const int V1HeaderLength = 34;
    internal const int BaseGlyphRecordSize = 6, LayerRecordSize = 4;
    // The v1 array faces: one BaseGlyphPaintRecord is glyphID + Offset32 paint
    // offset (from the BaseGlyphList start, spec L701-706); a LayerList entry is
    // a bare Offset32 paint offset (from the LayerList start, spec L718-723); a
    // Clip is startGlyphID + endGlyphID + Offset24 clipBoxOffset (from the
    // ClipList start, spec L739-745); a format-1 ClipBox is four FWORDs (L751-760).
    internal const int BaseGlyphPaintRecordSize = 6, LayerPaintOffsetStride = 4;
    internal const int ClipRecordSize = 7, ClipBoxFormat1Size = 9;
    // The 18 static paint formats' packed lengths (format byte included, no
    // alignment padding; spec L859-1393).
    internal const int PaintColrLayersSize = 6, PaintSolidSize = 5,
        PaintLinearGradientSize = 16, PaintRadialGradientSize = 16,
        PaintSweepGradientSize = 12, PaintGlyphSize = 6, PaintColrGlyphSize = 3,
        PaintTransformSize = 7, PaintTranslateSize = 8, PaintScaleSize = 8,
        PaintScaleAroundCenterSize = 12, PaintScaleUniformSize = 6,
        PaintScaleUniformAroundCenterSize = 10, PaintRotateSize = 6,
        PaintRotateAroundCenterSize = 10, PaintSkewSize = 8,
        PaintSkewAroundCenterSize = 12, PaintCompositeSize = 8;
    // The fourteen var paint formats (3/5/7/9/13/15/17/19/21/23/25/27/29/31,
    // T6-03 §0's size table): each is its static twin plus a trailing u32
    // varIndexBase (packed, no padding); PaintVarTransform carries two Offset24s
    // and no own varIndexBase (it rides the VarAffine2x3).
    internal const int PaintVarSolidSize = 9, PaintVarLinearGradientSize = 20,
        PaintVarRadialGradientSize = 20, PaintVarSweepGradientSize = 16,
        PaintVarTransformSize = 11, PaintVarTranslateSize = 12,
        PaintVarScaleSize = 12, PaintVarScaleAroundCenterSize = 16,
        PaintVarScaleUniformSize = 10, PaintVarScaleUniformAroundCenterSize = 14,
        PaintVarRotateSize = 10, PaintVarRotateAroundCenterSize = 14,
        PaintVarSkewSize = 12, PaintVarSkewAroundCenterSize = 16;
    // The var sub-structures: one VarColorStop is stopOffset + paletteIndex
    // (immutable, no slot) + alpha + u32 varIndexBase = 10 bytes (L802-809); a
    // VarColorLine is the 3-byte ColorLine head plus 10-byte stops (L811-815,
    // no row-level varIndexBase); a VarAffine2x3 is six Fixed words plus u32
    // varIndexBase = 28 bytes (L1071-1081); a format-2 ClipBox is four FWORDs
    // plus u32 varIndexBase = 13 bytes (L761-770).
    internal const int VarColorStopSize = 10, VarAffine2x3Size = 28,
        ClipBoxFormat2Size = 13;
    // The variation-word contract (L1544-1556): 0xFFFFFFFF marks "no variable
    // data" for a whole table or record; the per-node slot count is capped at
    // six (the gradient twins and the VarAffine2x3).
    internal const uint VarIndexBaseNoData = 0xFFFFFFFFu;
    internal const int MaxVarSlots = 6;
    // The paint sub-structures: a ColorLine is extend + numStops + the stops
    // (3-byte head, spec L817-823), one ColorStop is 6 bytes (L794-800), and an
    // Affine2x3 is six Fixed words = 24 bytes (L1060-1069).
    internal const int ColorLineHeaderSize = 3, ColorStopSize = 6, Affine2x3Size = 24;
    // The Extend enumeration domain 0..2 (spec L837-845): an out-of-domain value
    // is the renderer's should-use-EXTEND_PAD fallback, an observation here
    // (OQ-T6-02-8), never a refusal.
    internal const byte ExtendPad = 0, ExtendRepeat = 1, ExtendReflect = 2,
        ExtendDomainMax = 2;
    // The CompositeMode enumeration domain 0..27 (Porter-Duff 0-12, separable
    // blend 13-23, non-separable 24-27, spec L1397-1431): above 27 the renderer
    // must fall back to COMPOSITE_CLEAR - the same observation face (OQ-T6-02-8).
    internal const byte CompositeModeDomainMax = 27;
    // The layer paletteIndex sentinel: 0xFFFF paints with the text foreground
    // color (spec L688) and is exempt from the cross-table palette-entry domain
    // (OQ-T6-01-4/OQ-T6-01-7; the CPAL labels' 0xFFFF "no string" is a separate
    // contract that happens to share the value). The same sentinel rides v1's
    // PaintSolid/ColorStop paletteIndex words (spec L782-786).
    internal const ushort ForegroundPaletteIndex = 0xFFFF;
}

// COLR v0/v1 (cards T6-01 §2 and T6-02 §2, OQ-T6-01-10/OQ-T6-02-2): one View for
// both versions of the tag, the GDEF single-View line. The v0 face is the
// 14-byte header and the two record arrays, both addressed by their explicit
// Offset32 fields rather than a fixed start - the corpus carries both placements
// (layer-records-first 41/52 and base-records-first 11/52), so the declaration
// walks the offsets and assumes no order.
//
// The v1 face extends the same header in place: the five Offset32 slots are
// declared at their constant positions with WhenVersion={1} (the GDEF
// version-gated scalar line plus T6-01's three CPAL Offset32 precedent), so a
// v0 table reads 0 ("no subtable") for every one of them without any truncation
// face and a v1 wire slices the fixed 14..34 window. baseGlyphListOffset is the
// only one of the five without a "(may be NULL)" note in the spec (L629 versus
// L630-633) - the v1 requirement stays a ValidateTable hard face (OQ-T6-02-1),
// not a declaration-level ZeroAllowed, because zero remains the legal absent
// face on v0.
//
// The two version-1 array faces hang off derived anchors at the offset fields:
// BaseGlyphList is "u32 count + BaseGlyphPaintRecord[count]" (spec L698-706, the
// u32 count is the CmapFormat12/13 NumGroups domain) and LayerList is
// "u32 count + Offset32[count]" (spec L718-723). The generator's one rule the
// precheck surfaced (FWG005): an array cannot combine WhenVersions with a
// WhenVersion-conditioned count - so the version gate lives on the counts alone
// (Otherwise=0 on v0, read before any position is evaluated) and the arrays stay
// unconditioned: on v0 both counts are zero, the row accessors refuse any
// ordinal as InvalidIndex and the extents are empty. The v1 semantics are
// identical to an array-level gate without a second face to answer. ClipList is
// NOT declared: its Offset24 element has no wire type and the graph it anchors
// is the paint codec's walking domain (the T5-05 INDEX/DICT boundary) - the
// partial owns it from T6-02 commit 2.
[BinaryTable(ColrLayout.Tag, ColrLayout.HeaderLength, ContractVersion = 1, VersionField = "Version",
    SupportedVersions = new uint[] { ColrLayout.Version0, ColrLayout.Version1 })]
[BinaryScalar("Version", WireType.UInt16, 0, DataField.Version)]
[BinaryScalar("NumBaseGlyphRecords", WireType.UInt16, 2, DataField.Count, Semantic = NumberSemantic.Count)]
[BinaryScalar("BaseGlyphRecordsOffset", WireType.UInt32, 4, DataField.Offset,
    Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table)]
[BinaryScalar("LayerRecordsOffset", WireType.UInt32, 8, DataField.Offset,
    Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table)]
[BinaryScalar("NumLayerRecords", WireType.UInt16, 12, DataField.Count, Semantic = NumberSemantic.Count)]
[BinaryArray("BaseGlyphRecord", "NumBaseGlyphRecords", 0, ColrLayout.BaseGlyphRecordSize,
    Start = "BaseGlyphRecordsOffset")]
[BinaryElement("BaseGlyphRecord", "GlyphId", WireType.UInt16, 0, DataField.GlyphId)]
[BinaryElement("BaseGlyphRecord", "FirstLayerIndex", WireType.UInt16, 2, DataField.Payload)]
[BinaryElement("BaseGlyphRecord", "NumLayers", WireType.UInt16, 4, DataField.Count)]
[BinaryArray("LayerRecord", "NumLayerRecords", 0, ColrLayout.LayerRecordSize,
    Start = "LayerRecordsOffset")]
[BinaryElement("LayerRecord", "GlyphId", WireType.UInt16, 0, DataField.GlyphId)]
[BinaryElement("LayerRecord", "PaletteIndex", WireType.UInt16, 2, DataField.Payload)]
[BinaryScalar("BaseGlyphListOffset", WireType.UInt32, 14, DataField.Offset,
    Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, WhenVersion = ColrLayout.Version1)]
[BinaryScalar("LayerListOffset", WireType.UInt32, 18, DataField.Offset,
    Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, WhenVersion = ColrLayout.Version1)]
[BinaryScalar("ClipListOffset", WireType.UInt32, 22, DataField.Offset,
    Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, WhenVersion = ColrLayout.Version1)]
[BinaryScalar("VarIndexMapOffset", WireType.UInt32, 26, DataField.Offset,
    Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, WhenVersion = ColrLayout.Version1)]
[BinaryScalar("ItemVariationStoreOffset", WireType.UInt32, 30, DataField.Offset,
    Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, WhenVersion = ColrLayout.Version1)]
[BinaryDerived("BaseGlyphListAt", Base = "BaseGlyphListOffset")]
[BinaryScalar("NumBaseGlyphPaintRecords", WireType.UInt32, 0, DataField.Count,
    Semantic = NumberSemantic.Count, Position = "BaseGlyphListAt", WhenVersion = ColrLayout.Version1)]
[BinaryArray("BaseGlyphPaintRecord", "NumBaseGlyphPaintRecords", 4, ColrLayout.BaseGlyphPaintRecordSize,
    Start = "BaseGlyphListAt")]
[BinaryElement("BaseGlyphPaintRecord", "GlyphId", WireType.UInt16, 0, DataField.GlyphId)]
[BinaryElement("BaseGlyphPaintRecord", "PaintOffset", WireType.UInt32, 2, DataField.Offset,
    Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table)]
[BinaryDerived("LayerListAt", Base = "LayerListOffset")]
[BinaryScalar("NumLayerPaints", WireType.UInt32, 0, DataField.Count,
    Semantic = NumberSemantic.Count, Position = "LayerListAt", WhenVersion = ColrLayout.Version1)]
[BinaryArray("LayerPaintOffset", "NumLayerPaints", 4, ColrLayout.LayerPaintOffsetStride,
    Start = "LayerListAt")]
[BinaryElement("LayerPaintOffset", "PaintOffset", WireType.UInt32, 0, DataField.Offset,
    Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table)]
public readonly ref partial struct ColrView { }
