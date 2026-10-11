using FontWeave.Core;

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
/// order relation between them (the spec carries none, card §3).</summary>
public readonly record struct ColrClipBox(short XMin, short YMin, short XMax, short YMax);

/// <summary>One Clip: the inclusive glyph range and the box that bounds it. The
/// range is a projection key, not a paint graph member (spec L729: every color
/// glyph may or may not carry a clip).</summary>
public readonly record struct ColrClip(ushort StartGlyphId, ushort EndGlyphId, ColrClipBox Box);

/// <summary>One BaseGlyphPaintRecord: the base glyph and its root paint node.</summary>
public readonly record struct ColrBaseGlyphPaint(ushort GlyphId, int PaintNodeId);
