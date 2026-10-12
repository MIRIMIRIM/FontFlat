using FontWeave.Core;
using FontWeave.Tables.Variation;

namespace FontWeave.Tables;

/// <summary>One owned paint graph node (card T6-02 §2, the L476 order): the
/// stable id is the topological ordinal assigned at read time - every parent
/// sorts before every child (a re-used paint table occurs later in the file
/// than any of the paint tables that use it), so the compiler emits in ordinal
/// order with forward offsets and every edit keeps its address. All scalar words are byte-faithful raw wire values (FWORD/F2DOT14 as
/// raw UInt16, Fixed as raw UInt32) - no numeric interpretation, the renderer's
/// domain (card §11). Shared sub-graphs are the same node object referenced by
/// several parents, so sharing survives edits without any copying.</summary>
public abstract class ColrPaint
{
    internal ColrPaint(int id, byte format)
    {
        Id = id;
        Format = format;
    }

    /// <summary>The node's canonical ordinal (parents first, children later -
    /// the L476 emission order).</summary>
    public int Id { get; internal set; }

    /// <summary>Replaces one translate/scale/rotate/skew node's scalar words in
    /// wire order; the per-class overrides carry the arity.</summary>
    internal ColrPaint WithScalars(ushort[] scalars) => this switch
    {
        ColrPaintTranslate n => n.WithScalarWords(scalars),
        ColrPaintScale n => n.WithScalarWords(scalars),
        ColrPaintScaleAroundCenter n => n.WithScalarWords(scalars),
        ColrPaintScaleUniform n => n.WithScalarWords(scalars),
        ColrPaintScaleUniformAroundCenter n => n.WithScalarWords(scalars),
        ColrPaintRotate n => n.WithScalarWords(scalars),
        ColrPaintRotateAroundCenter n => n.WithScalarWords(scalars),
        ColrPaintSkew n => n.WithScalarWords(scalars),
        ColrPaintSkewAroundCenter n => n.WithScalarWords(scalars),
        _ => throw new System.InvalidOperationException(
            "A non-scalar paint has no scalar words."),
    };

    /// <summary>The wire paint format byte.</summary>
    public byte Format { get; }

    /// <summary>The node's minimum depth from any root (the deterministic
    /// tie-break's first key, OQ-T6-02-14).</summary>
    public int Depth { get; internal set; }
}

/// <summary>PaintColrLayers (format 1): one slice of the LayerList. The node ids
/// address the slice's paint entries in bottom-up z order (spec L725).</summary>
public sealed class ColrPaintLayers : ColrPaint
{
    public ColrPaintLayers(int id, uint firstLayerIndex, int[] layerNodeIds) :
        base(id, 1)
    {
        FirstLayerIndex = firstLayerIndex;
        LayerNodeIdArray = layerNodeIds;
    }

    public uint FirstLayerIndex { get; }
    public IReadOnlyList<int> LayerNodeIds => LayerNodeIdArray;
    internal readonly int[] LayerNodeIdArray;
}

/// <summary>PaintSolid (format 2): the palette entry (0xFFFF is the foreground
/// sentinel) and the raw F2DOT14 alpha.</summary>
public sealed class ColrPaintSolid : ColrPaint
{
    public ColrPaintSolid(int id, ushort paletteIndex, ushort alpha) : base(id, 2)
    {
        PaletteIndex = paletteIndex;
        Alpha = alpha;
    }

    public ushort PaletteIndex { get; internal set; }
    public ushort Alpha { get; internal set; }
}

/// <summary>PaintLinearGradient (format 4): three raw FWORD point pairs and the
/// static ColorLine the colorLineOffset addresses (from the paint's own start).</summary>
public sealed class ColrPaintLinearGradient : ColrPaint
{
    public ColrPaintLinearGradient(int id, short x0, short y0, short x1, short y1,
        short x2, short y2, ColrColorLine colorLine) : base(id, 4)
    {
        X0 = x0; Y0 = y0; X1 = x1; Y1 = y1; X2 = x2; Y2 = y2;
        ColorLine = colorLine;
    }

    public short X0 { get; internal set; }
    public short Y0 { get; internal set; }
    public short X1 { get; internal set; }
    public short Y1 { get; internal set; }
    public short X2 { get; internal set; }
    public short Y2 { get; internal set; }
    public ColrColorLine ColorLine { get; internal set; }

    internal ColrPaint WithColorLine(ColrColorLine colorLine) =>
        new ColrPaintLinearGradient(Id, X0, Y0, X1, Y1, X2, Y2, colorLine);
}

/// <summary>PaintRadialGradient (format 6): raw FWORD centers and UFWORD radii
/// plus the static ColorLine.</summary>
public sealed class ColrPaintRadialGradient : ColrPaint
{
    public ColrPaintRadialGradient(int id, short x0, short y0, ushort radius0,
        short x1, short y1, ushort radius1, ColrColorLine colorLine) : base(id, 6)
    {
        X0 = x0; Y0 = y0; Radius0 = radius0; X1 = x1; Y1 = y1; Radius1 = radius1;
        ColorLine = colorLine;
    }

    public short X0 { get; internal set; }
    public short Y0 { get; internal set; }
    public ushort Radius0 { get; internal set; }
    public short X1 { get; internal set; }
    public short Y1 { get; internal set; }
    public ushort Radius1 { get; internal set; }
    public ColrColorLine ColorLine { get; internal set; }

    internal ColrPaint WithColorLine(ColrColorLine colorLine) =>
        new ColrPaintRadialGradient(Id, X0, Y0, Radius0, X1, Y1, Radius1, colorLine);
}

/// <summary>PaintSweepGradient (format 8): raw FWORD center and F2DOT14 angles
/// plus the static ColorLine.</summary>
public sealed class ColrPaintSweepGradient : ColrPaint
{
    public ColrPaintSweepGradient(int id, short centerX, short centerY,
        ushort startAngle, ushort endAngle, ColrColorLine colorLine) : base(id, 8)
    {
        CenterX = centerX; CenterY = centerY;
        StartAngle = startAngle; EndAngle = endAngle;
        ColorLine = colorLine;
    }

    public short CenterX { get; internal set; }
    public short CenterY { get; internal set; }
    public ushort StartAngle { get; internal set; }
    public ushort EndAngle { get; internal set; }
    public ColrColorLine ColorLine { get; internal set; }

    internal ColrPaint WithColorLine(ColrColorLine colorLine) =>
        new ColrPaintSweepGradient(Id, CenterX, CenterY, StartAngle, EndAngle, colorLine);
}

/// <summary>PaintGlyph (format 10): the child paint that fills this glyph's
/// outline and the glyph id (the outline itself is never read here, the T6-01
/// line).</summary>
public sealed class ColrPaintGlyph : ColrPaint
{
    public ColrPaintGlyph(int id, int paintNodeId, ushort glyphId) : base(id, 10)
    {
        PaintNodeId = paintNodeId;
        GlyphId = glyphId;
    }

    public int PaintNodeId { get; internal set; }
    public ushort GlyphId { get; internal set; }
}

/// <summary>PaintColrGlyph (format 11): the indirect reference to another base
/// glyph's color graph, which joins this graph as a child sub-graph (spec
/// L1032). The target is the referenced glyph's BGL root node.</summary>
public sealed class ColrPaintColrGlyph : ColrPaint
{
    public ColrPaintColrGlyph(int id, ushort glyphId, int targetNodeId) : base(id, 11)
    {
        GlyphId = glyphId;
        TargetNodeId = targetNodeId;
    }

    public ushort GlyphId { get; internal set; }
    public int TargetNodeId { get; internal set; }
}

/// <summary>PaintTransform (format 12): the child paint and the static
/// Affine2x3 the transformOffset addresses, six raw Fixed words.</summary>
public sealed class ColrPaintTransform : ColrPaint
{
    public ColrPaintTransform(int id, int paintNodeId, ColrAffine2x3 transform) :
        base(id, 12)
    {
        PaintNodeId = paintNodeId;
        Transform = transform;
    }

    public int PaintNodeId { get; internal set; }
    public ColrAffine2x3 Transform { get; internal set; }
}

/// <summary>PaintTranslate (format 14): raw FWORD offsets.</summary>
public sealed class ColrPaintTranslate : ColrPaint
{
    public ColrPaintTranslate(int id, int paintNodeId, short dx, short dy) :
        base(id, 14)
    {
        PaintNodeId = paintNodeId;
        Dx = dx; Dy = dy;
    }

    public int PaintNodeId { get; internal set; }
    public short Dx { get; internal set; }
    public short Dy { get; internal set; }

    internal ColrPaint WithScalarWords(ushort[] scalars) =>
        new ColrPaintTranslate(Id, PaintNodeId, unchecked((short)scalars[0]),
            unchecked((short)scalars[1]));
}

/// <summary>PaintScale (format 16): raw F2DOT14 factors.</summary>
public sealed class ColrPaintScale : ColrPaint
{
    public ColrPaintScale(int id, int paintNodeId, ushort scaleX, ushort scaleY) :
        base(id, 16)
    {
        PaintNodeId = paintNodeId;
        ScaleX = scaleX; ScaleY = scaleY;
    }

    public int PaintNodeId { get; internal set; }
    public ushort ScaleX { get; internal set; }
    public ushort ScaleY { get; internal set; }

    internal ColrPaint WithScalarWords(ushort[] scalars) =>
        new ColrPaintScale(Id, PaintNodeId, scalars[0], scalars[1]);
}

/// <summary>PaintScaleAroundCenter (format 18): raw F2DOT14 factors and FWORD
/// center.</summary>
public sealed class ColrPaintScaleAroundCenter : ColrPaint
{
    public ColrPaintScaleAroundCenter(int id, int paintNodeId, ushort scaleX,
        ushort scaleY, short centerX, short centerY) : base(id, 18)
    {
        PaintNodeId = paintNodeId;
        ScaleX = scaleX; ScaleY = scaleY; CenterX = centerX; CenterY = centerY;
    }

    public int PaintNodeId { get; internal set; }
    public ushort ScaleX { get; internal set; }
    public ushort ScaleY { get; internal set; }
    public short CenterX { get; internal set; }
    public short CenterY { get; internal set; }

    internal ColrPaint WithScalarWords(ushort[] scalars) =>
        new ColrPaintScaleAroundCenter(Id, PaintNodeId, scalars[0], scalars[1],
            unchecked((short)scalars[2]), unchecked((short)scalars[3]));
}

/// <summary>PaintScaleUniform (format 20): one raw F2DOT14 factor.</summary>
public sealed class ColrPaintScaleUniform : ColrPaint
{
    public ColrPaintScaleUniform(int id, int paintNodeId, ushort scale) :
        base(id, 20)
    {
        PaintNodeId = paintNodeId;
        Scale = scale;
    }

    public int PaintNodeId { get; internal set; }
    public ushort Scale { get; internal set; }

    internal ColrPaint WithScalarWords(ushort[] scalars) =>
        new ColrPaintScaleUniform(Id, PaintNodeId, scalars[0]);
}

/// <summary>PaintScaleUniformAroundCenter (format 22).</summary>
public sealed class ColrPaintScaleUniformAroundCenter : ColrPaint
{
    public ColrPaintScaleUniformAroundCenter(int id, int paintNodeId, ushort scale,
        short centerX, short centerY) : base(id, 22)
    {
        PaintNodeId = paintNodeId;
        Scale = scale; CenterX = centerX; CenterY = centerY;
    }

    public int PaintNodeId { get; internal set; }
    public ushort Scale { get; internal set; }
    public short CenterX { get; internal set; }
    public short CenterY { get; internal set; }

    internal ColrPaint WithScalarWords(ushort[] scalars) =>
        new ColrPaintScaleUniformAroundCenter(Id, PaintNodeId, scalars[0],
            unchecked((short)scalars[1]), unchecked((short)scalars[2]));
}

/// <summary>PaintRotate (format 24): one raw F2DOT14 angle.</summary>
public sealed class ColrPaintRotate : ColrPaint
{
    public ColrPaintRotate(int id, int paintNodeId, ushort angle) : base(id, 24)
    {
        PaintNodeId = paintNodeId;
        Angle = angle;
    }

    public int PaintNodeId { get; internal set; }
    public ushort Angle { get; internal set; }

    internal ColrPaint WithScalarWords(ushort[] scalars) =>
        new ColrPaintRotate(Id, PaintNodeId, scalars[0]);
}

/// <summary>PaintRotateAroundCenter (format 26).</summary>
public sealed class ColrPaintRotateAroundCenter : ColrPaint
{
    public ColrPaintRotateAroundCenter(int id, int paintNodeId, ushort angle,
        short centerX, short centerY) : base(id, 26)
    {
        PaintNodeId = paintNodeId;
        Angle = angle; CenterX = centerX; CenterY = centerY;
    }

    public int PaintNodeId { get; internal set; }
    public ushort Angle { get; internal set; }
    public short CenterX { get; internal set; }
    public short CenterY { get; internal set; }

    internal ColrPaint WithScalarWords(ushort[] scalars) =>
        new ColrPaintRotateAroundCenter(Id, PaintNodeId, scalars[0],
            unchecked((short)scalars[1]), unchecked((short)scalars[2]));
}

/// <summary>PaintSkew (format 28): two raw F2DOT14 angles.</summary>
public sealed class ColrPaintSkew : ColrPaint
{
    public ColrPaintSkew(int id, int paintNodeId, ushort xSkewAngle,
        ushort ySkewAngle) : base(id, 28)
    {
        PaintNodeId = paintNodeId;
        XSkewAngle = xSkewAngle; YSkewAngle = ySkewAngle;
    }

    public int PaintNodeId { get; internal set; }
    public ushort XSkewAngle { get; internal set; }
    public ushort YSkewAngle { get; internal set; }

    internal ColrPaint WithScalarWords(ushort[] scalars) =>
        new ColrPaintSkew(Id, PaintNodeId, scalars[0], scalars[1]);
}

/// <summary>PaintSkewAroundCenter (format 30).</summary>
public sealed class ColrPaintSkewAroundCenter : ColrPaint
{
    public ColrPaintSkewAroundCenter(int id, int paintNodeId, ushort xSkewAngle,
        ushort ySkewAngle, short centerX, short centerY) : base(id, 30)
    {
        PaintNodeId = paintNodeId;
        XSkewAngle = xSkewAngle; YSkewAngle = ySkewAngle;
        CenterX = centerX; CenterY = centerY;
    }

    public int PaintNodeId { get; internal set; }
    public ushort XSkewAngle { get; internal set; }
    public ushort YSkewAngle { get; internal set; }
    public short CenterX { get; internal set; }
    public short CenterY { get; internal set; }

    internal ColrPaint WithScalarWords(ushort[] scalars) =>
        new ColrPaintSkewAroundCenter(Id, PaintNodeId, scalars[0], scalars[1],
            unchecked((short)scalars[2]), unchecked((short)scalars[3]));
}

/// <summary>PaintComposite (format 32): the source paint over the backdrop paint
/// in one CompositeMode. Backdrop renders first, source second (the z-order
/// structural note, spec L1453+; nothing is composited here).</summary>
public sealed class ColrPaintComposite : ColrPaint
{
    internal ColrPaintComposite(int id, int sourceNodeId, byte compositeMode,
        int backdropNodeId) : base(id, 32)
    {
        SourceNodeId = sourceNodeId;
        CompositeMode = compositeMode;
        BackdropNodeId = backdropNodeId;
    }

    public int SourceNodeId { get; internal set; }
    public byte CompositeMode { get; internal set; }
    public int BackdropNodeId { get; internal set; }
}

/// <summary>A static ColorLine: the raw Extend byte (0..2 declared; an
/// out-of-domain value is the renderer's EXTEND_PAD fallback, an observation -
/// OQ-T6-02-8) and the stops in wire order. The wire's stopOffset order is NOT
/// the render order (spec L833 leaves sorting to the application and variation
/// can change it), so the wire order is preserved verbatim and never checked
/// (OQ-T6-02-8's correction).</summary>
public sealed class ColrColorLine
{
    public ColrColorLine(byte extend, ColrColorStop[] stops)
    {
        Extend = extend;
        StopArray = stops;
    }

    public byte Extend { get; internal set; }
    public IReadOnlyList<ColrColorStop> Stops => StopArray;
    internal readonly ColrColorStop[] StopArray;
}

/// <summary>One ColorStop: raw F2DOT14 stopOffset, the palette entry (the
/// 0xFFFF foreground sentinel passes the cross-table domain) and the raw
/// F2DOT14 alpha (out-of-range values are reserved wording with a renderer-side
/// clamp - an observation, OQ-T6-02-8).</summary>
public readonly record struct ColrColorStop(short StopOffset, ushort PaletteIndex, ushort Alpha);

/// <summary>An Affine2x3: six raw Fixed words in wire order
/// (xx, yx, xy, yy, dx, dy - spec L1060-1069).</summary>
public readonly record struct ColrAffine2x3(uint Xx, uint Yx, uint Xy, uint Yy, uint Dx, uint Dy);

/// <summary>A format-1 ClipBox: four raw FWORDs, preserved verbatim with no
/// order relation between them (the spec carries none, card §3). Format 2
/// (T6-03 §3, OQ-T6-03-3) adds the u32 varIndexBase and degrades to this
/// static face when the table carries no ItemVariationStore (L1554: the word
/// is ignored and should be zero - kept verbatim as an observation).</summary>
public readonly record struct ColrClipBox(short XMin, short YMin, short XMax, short YMax,
    byte Format = 1, uint VarIndexBase = 0);

/// <summary>One Clip: the inclusive glyph range and the box that bounds it. The
/// range is a projection key, not a paint graph member (spec L729: every color
/// glyph may or may not carry a clip).</summary>
public readonly record struct ColrClip(ushort StartGlyphId, ushort EndGlyphId, ColrClipBox Box);

/// <summary>One BaseGlyphPaintRecord: the base glyph and its root paint node.</summary>
public readonly record struct ColrBaseGlyphPaint(ushort GlyphId, int PaintNodeId);

// ---- the version 1 variable paint family (card T6-03 §2; the L1531-1562
// contract). Every node carries its scalar words exactly like its static twin
// plus the raw u32 varIndexBase (0xFFFFFFFF = the no-variable-data sentinel,
// L1550); the palette entries stay immutable (no var slot, the cross-table
// domain is the static family's). No delta evaluation happens here - the
// resolution of (varIndexBase, slot) onto the variation store is the read
// chain's domain check, and the numeric application is the renderer's (L1560,
// oracle-only family discipline).

/// <summary>PaintVarSolid (format 3): the alpha slot is variable (+0), the
/// palette entry is not.</summary>
public sealed class ColrPaintVarSolid : ColrPaint
{
    public ColrPaintVarSolid(int id, ushort paletteIndex, ushort alpha,
        uint varIndexBase) : base(id, 3)
    {
        PaletteIndex = paletteIndex;
        Alpha = alpha;
        VarIndexBase = varIndexBase;
    }

    public ushort PaletteIndex { get; internal set; }
    public ushort Alpha { get; internal set; }
    public uint VarIndexBase { get; internal set; }

    internal ColrPaint WithScalarWords(ushort[] scalars) =>
        new ColrPaintVarSolid(Id, PaletteIndex, scalars[0], VarIndexBase);

    internal ColrPaint WithVarIndexBase(uint varIndexBase) =>
        new ColrPaintVarSolid(Id, PaletteIndex, Alpha, varIndexBase);
}

/// <summary>A variable ColorStop: stopOffset (+0) and alpha (+1) are the
/// variable slots; the palette entry is immutable (L802-809).</summary>
public readonly record struct ColrVarColorStop(short StopOffset, ushort PaletteIndex,
    ushort Alpha, uint VarIndexBase);

/// <summary>A variable ColorLine: the same 3-byte head and wire-ordered stops
/// as the static face, each stop carrying its own varIndexBase (L811-815 -
/// the line itself carries none).</summary>
public sealed class ColrVarColorLine
{
    public ColrVarColorLine(byte extend, ColrVarColorStop[] stops)
    {
        Extend = extend;
        StopArray = stops;
    }

    public byte Extend { get; internal set; }
    public IReadOnlyList<ColrVarColorStop> Stops => StopArray;
    internal readonly ColrVarColorStop[] StopArray;
}

/// <summary>A variable Affine2x3: six raw Fixed words (+0..+5, the L1546
/// example's slot order) plus the u32 varIndexBase - 28 bytes (L1071-1081).</summary>
public readonly record struct ColrVarAffine2x3(uint Xx, uint Yx, uint Xy, uint Yy,
    uint Dx, uint Dy, uint VarIndexBase);

/// <summary>PaintVarLinearGradient (format 5): six variable FWORD slots
/// (+0..+5) and the VarColorLine the colorLineOffset addresses.</summary>
public sealed class ColrPaintVarLinearGradient : ColrPaint
{
    public ColrPaintVarLinearGradient(int id, short x0, short y0, short x1, short y1,
        short x2, short y2, ColrVarColorLine colorLine, uint varIndexBase) : base(id, 5)
    {
        X0 = x0; Y0 = y0; X1 = x1; Y1 = y1; X2 = x2; Y2 = y2;
        ColorLine = colorLine;
        VarIndexBase = varIndexBase;
    }

    public short X0 { get; internal set; }
    public short Y0 { get; internal set; }
    public short X1 { get; internal set; }
    public short Y1 { get; internal set; }
    public short X2 { get; internal set; }
    public short Y2 { get; internal set; }
    public ColrVarColorLine ColorLine { get; internal set; }
    public uint VarIndexBase { get; internal set; }

    internal ColrPaint WithVarColorLine(ColrVarColorLine colorLine) =>
        new ColrPaintVarLinearGradient(Id, X0, Y0, X1, Y1, X2, Y2, colorLine, VarIndexBase);

    internal ColrPaint WithScalarWords(ushort[] scalars) =>
        new ColrPaintVarLinearGradient(Id, unchecked((short)scalars[0]),
            unchecked((short)scalars[1]), unchecked((short)scalars[2]),
            unchecked((short)scalars[3]), unchecked((short)scalars[4]),
            unchecked((short)scalars[5]), ColorLine, VarIndexBase);
}

/// <summary>PaintVarRadialGradient (format 7): six variable slots (+0..+5:
/// x0, y0, radius0, x1, y1, radius1).</summary>
public sealed class ColrPaintVarRadialGradient : ColrPaint
{
    public ColrPaintVarRadialGradient(int id, short x0, short y0, ushort radius0,
        short x1, short y1, ushort radius1, ColrVarColorLine colorLine,
        uint varIndexBase) : base(id, 7)
    {
        X0 = x0; Y0 = y0; Radius0 = radius0; X1 = x1; Y1 = y1; Radius1 = radius1;
        ColorLine = colorLine;
        VarIndexBase = varIndexBase;
    }

    public short X0 { get; internal set; }
    public short Y0 { get; internal set; }
    public ushort Radius0 { get; internal set; }
    public short X1 { get; internal set; }
    public short Y1 { get; internal set; }
    public ushort Radius1 { get; internal set; }
    public ColrVarColorLine ColorLine { get; internal set; }
    public uint VarIndexBase { get; internal set; }

    internal ColrPaint WithVarColorLine(ColrVarColorLine colorLine) =>
        new ColrPaintVarRadialGradient(Id, X0, Y0, Radius0, X1, Y1, Radius1,
            colorLine, VarIndexBase);

    internal ColrPaint WithScalarWords(ushort[] scalars) =>
        new ColrPaintVarRadialGradient(Id, unchecked((short)scalars[0]),
            unchecked((short)scalars[1]), scalars[2], unchecked((short)scalars[3]),
            unchecked((short)scalars[4]), scalars[5], ColorLine, VarIndexBase);
}

/// <summary>PaintVarSweepGradient (format 9): four variable slots (+0..+3:
/// centerX, centerY, startAngle, endAngle).</summary>
public sealed class ColrPaintVarSweepGradient : ColrPaint
{
    public ColrPaintVarSweepGradient(int id, short centerX, short centerY,
        ushort startAngle, ushort endAngle, ColrVarColorLine colorLine,
        uint varIndexBase) : base(id, 9)
    {
        CenterX = centerX; CenterY = centerY;
        StartAngle = startAngle; EndAngle = endAngle;
        ColorLine = colorLine;
        VarIndexBase = varIndexBase;
    }

    public short CenterX { get; internal set; }
    public short CenterY { get; internal set; }
    public ushort StartAngle { get; internal set; }
    public ushort EndAngle { get; internal set; }
    public ColrVarColorLine ColorLine { get; internal set; }
    public uint VarIndexBase { get; internal set; }

    internal ColrPaint WithVarColorLine(ColrVarColorLine colorLine) =>
        new ColrPaintVarSweepGradient(Id, CenterX, CenterY, StartAngle, EndAngle,
            colorLine, VarIndexBase);

    internal ColrPaint WithScalarWords(ushort[] scalars) =>
        new ColrPaintVarSweepGradient(Id, unchecked((short)scalars[0]),
            unchecked((short)scalars[1]), scalars[2], scalars[3], ColorLine,
            VarIndexBase);
}

/// <summary>PaintVarTransform (format 13): the child paint and the
/// VarAffine2x3 the transformOffset addresses. The format itself carries no
/// varIndexBase - the slots live in the 28-byte VarAffine2x3 (L1050-1056).</summary>
public sealed class ColrPaintVarTransform : ColrPaint
{
    public ColrPaintVarTransform(int id, int paintNodeId, ColrVarAffine2x3 transform) :
        base(id, 13)
    {
        PaintNodeId = paintNodeId;
        Transform = transform;
    }

    public int PaintNodeId { get; internal set; }
    public ColrVarAffine2x3 Transform { get; internal set; }
}

/// <summary>PaintVarTranslate (format 15): dx (+0), dy (+1).</summary>
public sealed class ColrPaintVarTranslate : ColrPaint
{
    public ColrPaintVarTranslate(int id, int paintNodeId, short dx, short dy,
        uint varIndexBase) : base(id, 15)
    {
        PaintNodeId = paintNodeId;
        Dx = dx; Dy = dy;
        VarIndexBase = varIndexBase;
    }

    public int PaintNodeId { get; internal set; }
    public short Dx { get; internal set; }
    public short Dy { get; internal set; }
    public uint VarIndexBase { get; internal set; }

    internal ColrPaint WithScalarWords(ushort[] scalars) =>
        new ColrPaintVarTranslate(Id, PaintNodeId, unchecked((short)scalars[0]),
            unchecked((short)scalars[1]), VarIndexBase);
}

/// <summary>PaintVarScale (format 17): scaleX (+0), scaleY (+1).</summary>
public sealed class ColrPaintVarScale : ColrPaint
{
    public ColrPaintVarScale(int id, int paintNodeId, ushort scaleX, ushort scaleY,
        uint varIndexBase) : base(id, 17)
    {
        PaintNodeId = paintNodeId;
        ScaleX = scaleX; ScaleY = scaleY;
        VarIndexBase = varIndexBase;
    }

    public int PaintNodeId { get; internal set; }
    public ushort ScaleX { get; internal set; }
    public ushort ScaleY { get; internal set; }
    public uint VarIndexBase { get; internal set; }

    internal ColrPaint WithScalarWords(ushort[] scalars) =>
        new ColrPaintVarScale(Id, PaintNodeId, scalars[0], scalars[1], VarIndexBase);
}

/// <summary>PaintVarScaleAroundCenter (format 19): scaleX/Y (+0/+1),
/// centerX/Y (+2/+3).</summary>
public sealed class ColrPaintVarScaleAroundCenter : ColrPaint
{
    public ColrPaintVarScaleAroundCenter(int id, int paintNodeId, ushort scaleX,
        ushort scaleY, short centerX, short centerY, uint varIndexBase) : base(id, 19)
    {
        PaintNodeId = paintNodeId;
        ScaleX = scaleX; ScaleY = scaleY; CenterX = centerX; CenterY = centerY;
        VarIndexBase = varIndexBase;
    }

    public int PaintNodeId { get; internal set; }
    public ushort ScaleX { get; internal set; }
    public ushort ScaleY { get; internal set; }
    public short CenterX { get; internal set; }
    public short CenterY { get; internal set; }
    public uint VarIndexBase { get; internal set; }

    internal ColrPaint WithScalarWords(ushort[] scalars) =>
        new ColrPaintVarScaleAroundCenter(Id, PaintNodeId, scalars[0], scalars[1],
            unchecked((short)scalars[2]), unchecked((short)scalars[3]), VarIndexBase);
}

/// <summary>PaintVarScaleUniform (format 21): scale (+0).</summary>
public sealed class ColrPaintVarScaleUniform : ColrPaint
{
    public ColrPaintVarScaleUniform(int id, int paintNodeId, ushort scale,
        uint varIndexBase) : base(id, 21)
    {
        PaintNodeId = paintNodeId;
        Scale = scale;
        VarIndexBase = varIndexBase;
    }

    public int PaintNodeId { get; internal set; }
    public ushort Scale { get; internal set; }
    public uint VarIndexBase { get; internal set; }

    internal ColrPaint WithScalarWords(ushort[] scalars) =>
        new ColrPaintVarScaleUniform(Id, PaintNodeId, scalars[0], VarIndexBase);
}

/// <summary>PaintVarScaleUniformAroundCenter (format 23): scale (+0),
/// centerX/Y (+1/+2).</summary>
public sealed class ColrPaintVarScaleUniformAroundCenter : ColrPaint
{
    public ColrPaintVarScaleUniformAroundCenter(int id, int paintNodeId, ushort scale,
        short centerX, short centerY, uint varIndexBase) : base(id, 23)
    {
        PaintNodeId = paintNodeId;
        Scale = scale; CenterX = centerX; CenterY = centerY;
        VarIndexBase = varIndexBase;
    }

    public int PaintNodeId { get; internal set; }
    public ushort Scale { get; internal set; }
    public short CenterX { get; internal set; }
    public short CenterY { get; internal set; }
    public uint VarIndexBase { get; internal set; }

    internal ColrPaint WithScalarWords(ushort[] scalars) =>
        new ColrPaintVarScaleUniformAroundCenter(Id, PaintNodeId, scalars[0],
            unchecked((short)scalars[1]), unchecked((short)scalars[2]), VarIndexBase);
}

/// <summary>PaintVarRotate (format 25): angle (+0).</summary>
public sealed class ColrPaintVarRotate : ColrPaint
{
    public ColrPaintVarRotate(int id, int paintNodeId, ushort angle,
        uint varIndexBase) : base(id, 25)
    {
        PaintNodeId = paintNodeId;
        Angle = angle;
        VarIndexBase = varIndexBase;
    }

    public int PaintNodeId { get; internal set; }
    public ushort Angle { get; internal set; }
    public uint VarIndexBase { get; internal set; }

    internal ColrPaint WithScalarWords(ushort[] scalars) =>
        new ColrPaintVarRotate(Id, PaintNodeId, scalars[0], VarIndexBase);
}

/// <summary>PaintVarRotateAroundCenter (format 27): angle (+0), centerX/Y
/// (+1/+2).</summary>
public sealed class ColrPaintVarRotateAroundCenter : ColrPaint
{
    public ColrPaintVarRotateAroundCenter(int id, int paintNodeId, ushort angle,
        short centerX, short centerY, uint varIndexBase) : base(id, 27)
    {
        PaintNodeId = paintNodeId;
        Angle = angle; CenterX = centerX; CenterY = centerY;
        VarIndexBase = varIndexBase;
    }

    public int PaintNodeId { get; internal set; }
    public ushort Angle { get; internal set; }
    public short CenterX { get; internal set; }
    public short CenterY { get; internal set; }
    public uint VarIndexBase { get; internal set; }

    internal ColrPaint WithScalarWords(ushort[] scalars) =>
        new ColrPaintVarRotateAroundCenter(Id, PaintNodeId, scalars[0],
            unchecked((short)scalars[1]), unchecked((short)scalars[2]), VarIndexBase);
}

/// <summary>PaintVarSkew (format 29): xSkewAngle (+0), ySkewAngle (+1).</summary>
public sealed class ColrPaintVarSkew : ColrPaint
{
    public ColrPaintVarSkew(int id, int paintNodeId, ushort xSkewAngle,
        ushort ySkewAngle, uint varIndexBase) : base(id, 29)
    {
        PaintNodeId = paintNodeId;
        XSkewAngle = xSkewAngle; YSkewAngle = ySkewAngle;
        VarIndexBase = varIndexBase;
    }

    public int PaintNodeId { get; internal set; }
    public ushort XSkewAngle { get; internal set; }
    public ushort YSkewAngle { get; internal set; }
    public uint VarIndexBase { get; internal set; }

    internal ColrPaint WithScalarWords(ushort[] scalars) =>
        new ColrPaintVarSkew(Id, PaintNodeId, scalars[0], scalars[1], VarIndexBase);
}

/// <summary>PaintVarSkewAroundCenter (format 31): xSkew/ySkew (+0/+1),
/// centerX/Y (+2/+3).</summary>
public sealed class ColrPaintVarSkewAroundCenter : ColrPaint
{
    public ColrPaintVarSkewAroundCenter(int id, int paintNodeId, ushort xSkewAngle,
        ushort ySkewAngle, short centerX, short centerY, uint varIndexBase) :
        base(id, 31)
    {
        PaintNodeId = paintNodeId;
        XSkewAngle = xSkewAngle; YSkewAngle = ySkewAngle;
        CenterX = centerX; CenterY = centerY;
        VarIndexBase = varIndexBase;
    }

    public int PaintNodeId { get; internal set; }
    public ushort XSkewAngle { get; internal set; }
    public ushort YSkewAngle { get; internal set; }
    public short CenterX { get; internal set; }
    public short CenterY { get; internal set; }
    public uint VarIndexBase { get; internal set; }

    internal ColrPaint WithScalarWords(ushort[] scalars) =>
        new ColrPaintVarSkewAroundCenter(Id, PaintNodeId, scalars[0], scalars[1],
            unchecked((short)scalars[2]), unchecked((short)scalars[3]), VarIndexBase);
}
