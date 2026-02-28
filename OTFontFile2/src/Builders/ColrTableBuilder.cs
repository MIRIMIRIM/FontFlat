using OTFontFile2.SourceGen;
using System.Runtime.InteropServices;

namespace OTFontFile2.Tables;

/// <summary>
/// Mutable builder for the OpenType <c>COLR</c> table.
/// </summary>
/// <remarks>
/// This builder supports structured build/edit for COLR v0 and v1.
/// Unsupported versions (and unsupported v1 surface area) fall back to a raw-bytes mode via <see cref="SetTableData"/>.
/// </remarks>
[OtTableBuilder("COLR")]
public sealed partial class ColrTableBuilder : ISfntTableSource
{
    private readonly List<BaseGlyphEntry> _baseGlyphs = new();
    private readonly List<(ushort baseGlyphId, PaintV1 paint)> _v1BaseGlyphs = new();
    private readonly List<(ushort startGlyphId, ushort endGlyphId, ClipBoxV1 box)> _v1Clips = new();
    private ReadOnlyMemory<byte> _v1VarIndexMap;
    private ReadOnlyMemory<byte> _v1ItemVariationStore;

    private ReadOnlyMemory<byte> _data;
    private bool _isRaw;
    private bool _isStructuredV1;

    public ushort Version
    {
        get
        {
            if (_isRaw)
            {
                var span = _data.Span;
                if (span.Length >= 2)
                    return BigEndian.ReadUInt16(span, 0);

                return 0;
            }

            if (_isStructuredV1)
                return 1;

            return 0;
        }
    }

    public int BaseGlyphCount => _isStructuredV1 ? _v1BaseGlyphs.Count : _baseGlyphs.Count;

    public ReadOnlyMemory<byte> DataBytes => EnsureBuilt();

    public void ClearToVersion0()
    {
        _isRaw = false;
        _isStructuredV1 = false;
        _data = default;
        _built = null;
        _baseGlyphs.Clear();
        _v1BaseGlyphs.Clear();
        _v1Clips.Clear();
        _v1VarIndexMap = ReadOnlyMemory<byte>.Empty;
        _v1ItemVariationStore = ReadOnlyMemory<byte>.Empty;
        MarkDirty();
    }

    public void ClearToVersion1()
    {
        _isRaw = false;
        _isStructuredV1 = true;
        _data = default;
        _built = null;
        _baseGlyphs.Clear();
        _v1BaseGlyphs.Clear();
        _v1Clips.Clear();
        _v1VarIndexMap = ReadOnlyMemory<byte>.Empty;
        _v1ItemVariationStore = ReadOnlyMemory<byte>.Empty;
        MarkDirty();
    }

    public void SetTableData(ReadOnlyMemory<byte> data)
    {
        if (data.Length < 14)
            throw new ArgumentException("COLR table must be at least 14 bytes.", nameof(data));

        _isRaw = true;
        _isStructuredV1 = false;
        _data = data;
        _built = null;
        _baseGlyphs.Clear();
        _v1BaseGlyphs.Clear();
        _v1Clips.Clear();
        _v1VarIndexMap = ReadOnlyMemory<byte>.Empty;
        _v1ItemVariationStore = ReadOnlyMemory<byte>.Empty;
        MarkDirty();
    }

    public void AddOrReplaceBaseGlyph(ushort baseGlyphId, ReadOnlySpan<LayerEntry> layers)
    {
        if (_isRaw || _isStructuredV1)
            throw new InvalidOperationException("COLR is not in structured v0 mode. Call ClearToVersion0() to switch to structured v0 editing.");

        if (layers.Length > ushort.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(layers), "Layer count must fit in uint16.");

        int index = FindBaseGlyphIndex(baseGlyphId);
        var copiedLayers = layers.ToArray();
        if (index >= 0)
        {
            _baseGlyphs[index] = new BaseGlyphEntry(baseGlyphId, copiedLayers);
        }
        else
        {
            _baseGlyphs.Add(new BaseGlyphEntry(baseGlyphId, copiedLayers));
        }

        MarkDirty();
    }

    public bool RemoveBaseGlyph(ushort baseGlyphId)
    {
        if (_isRaw || _isStructuredV1)
            throw new InvalidOperationException("COLR is not in structured v0 mode. Call ClearToVersion0() to switch to structured v0 editing.");

        int index = FindBaseGlyphIndex(baseGlyphId);
        if (index < 0)
            return false;

        _baseGlyphs.RemoveAt(index);
        MarkDirty();
        return true;
    }

    private int FindBaseGlyphIndex(ushort baseGlyphId)
    {
        for (int i = 0; i < _baseGlyphs.Count; i++)
        {
            if (_baseGlyphs[i].BaseGlyphId == baseGlyphId)
                return i;
        }

        return -1;
    }

    public static bool TryFrom(ColrTable colr, out ColrTableBuilder builder)
    {
        var b = new ColrTableBuilder();

        if (colr.Version == 0)
        {
            if (!TryImportV0(colr, b))
                b.SetTableData(colr.Table.Span.ToArray());

            builder = b;
            return true;
        }

        if (colr.Version == 1)
        {
            if (!TryImportV1(colr, b))
                b.SetTableData(colr.Table.Span.ToArray());

            builder = b;
            return true;
        }

        // Unknown versions fall back to raw bytes.
        b.SetTableData(colr.Table.Span.ToArray());
        builder = b;
        return true;

        static bool TryImportV0(ColrTable colr, ColrTableBuilder b)
        {
            b.ClearToVersion0();

            int baseCount = colr.BaseGlyphRecordCount;
            for (int i = 0; i < baseCount; i++)
            {
                if (!colr.TryGetBaseGlyphRecord(i, out var baseRec))
                    return false;

                var layers = new LayerEntry[baseRec.NumLayers];
                int pos = 0;
                var e = colr.EnumerateLayers(baseRec);
                while (e.MoveNext())
                {
                    if ((uint)pos >= (uint)layers.Length)
                        return false;

                    layers[pos++] = new LayerEntry(e.Current.LayerGlyphId, e.Current.PaletteIndex);
                }

                if (pos != layers.Length)
                    return false;

                b.AddOrReplaceBaseGlyph(baseRec.BaseGlyphId, layers);
            }

            return true;
        }

        static bool TryImportV1(ColrTable colr, ColrTableBuilder b)
        {
            b.ClearToVersion1();

            // Import optional VarIndexMap / ItemVariationStore bytes (required for var paints to be valid).
            if (colr.TryGetVarIndexMap(out var map) && map.TryGetByteLength(out int mapLen))
            {
                int start = colr.VarIndexMapOffset;
                if ((uint)start > (uint)colr.Table.Length - (uint)mapLen)
                    return false;

                b.SetVarIndexMapData(colr.Table.Span.Slice(start, mapLen).ToArray());
            }

            if (colr.TryGetItemVariationStore(out var store) && store.TryGetByteLength(out int storeLen))
            {
                int start = colr.ItemVariationStoreOffset;
                if ((uint)start > (uint)colr.Table.Length - (uint)storeLen)
                    return false;

                b.SetItemVariationStoreData(colr.Table.Span.Slice(start, storeLen).ToArray());
            }

            // ClipList (format 1 only; variable clip boxes are not supported by the builder yet).
            if (colr.TryGetClipList(out var clipList))
            {
                uint countU = clipList.ClipRecordCount;
                if (countU > int.MaxValue)
                    return false;

                int count = (int)countU;
                for (int i = 0; i < count; i++)
                {
                    if (!clipList.TryGetClipRecord(i, out var rec))
                        return false;

                    if (!clipList.TryGetClipBox(rec, out var box))
                        return false;

                    if (box.Format != 1)
                        return false;

                    b.SetClipBoxRange(
                        startGlyphId: rec.StartGlyphId,
                        endGlyphId: rec.EndGlyphId,
                        clipBox: new ClipBoxV1(box.XMin, box.YMin, box.XMax, box.YMax));
                }
            }

            if (!colr.TryGetLayerList(out var layerList))
                return false;

            if (!colr.TryGetBaseGlyphList(out var baseGlyphList))
                return false;

            uint baseCountU = baseGlyphList.BaseGlyphPaintRecordCount;
            if (baseCountU > int.MaxValue)
                return false;

            var paintCache = new Dictionary<int, PaintV1>();
            var inProgress = new HashSet<int>();
            bool usesVarPaint = false;

            int baseCount = (int)baseCountU;
            for (int i = 0; i < baseCount; i++)
            {
                if (!baseGlyphList.TryGetBaseGlyphPaintRecord(i, out var rec))
                    return false;

                if (!rec.TryGetPaint(out var paint))
                    return false;

                if (!TryConvertPaint(paint, layerList, paintCache, inProgress, ref usesVarPaint, out var converted))
                    return false;

                b.SetBaseGlyphPaint(rec.BaseGlyphId, converted);
            }

            if (usesVarPaint && (b.VarIndexMapBytes.IsEmpty || b.ItemVariationStoreBytes.IsEmpty))
                return false;

            return true;

            static bool TryConvertPaint(ColrTable.Paint paint, ColrTable.LayerList layerList, Dictionary<int, PaintV1> cache, HashSet<int> inProgress, ref bool usesVarPaint, out PaintV1 converted)
            {
                converted = null!;

                int offset = paint.Offset;
                if (cache.TryGetValue(offset, out var existing))
                {
                    converted = existing;
                    return true;
                }

                if (!inProgress.Add(offset))
                    return false;

                byte fmt = paint.Format;
                if (fmt is 3 or 5 or 7 or 9 or 13 or 15 or 17 or 19 or 21 or 23 or 25 or 27 or 29 or 31)
                    usesVarPaint = true;

                PaintV1 created;
                switch (fmt)
                {
                    case 1:
                        if (!paint.TryGetPaintColrLayers(out var layers))
                            return false;

                        var layerPaints = new PaintV1[layers.NumLayers];
                        for (int i = 0; i < layerPaints.Length; i++)
                        {
                            uint indexU = layers.FirstLayerIndex + (uint)i;
                            if (indexU > int.MaxValue)
                                return false;

                            if (!layerList.TryGetPaint((int)indexU, out var layerPaint))
                                return false;

                            if (!TryConvertPaint(layerPaint, layerList, cache, inProgress, ref usesVarPaint, out layerPaints[i]))
                                return false;
                        }

                        created = new PaintColrLayersV1(layerPaints);
                        break;
                    case 2:
                        if (!paint.TryGetPaintSolid(out var solid))
                            return false;
                        created = new PaintSolidV1(solid.PaletteIndex, solid.Alpha);
                        break;
                    case 3:
                        if (!paint.TryGetPaintVarSolid(out var varSolid))
                            return false;
                        created = new PaintVarSolidV1(varSolid.PaletteIndex, varSolid.Alpha, varSolid.VarIndexBase);
                        break;
                    case 4:
                        if (!paint.TryGetPaintLinearGradient(out var lg) || !lg.TryGetColorLine(out var line))
                            return false;
                        var stops = new ColorStopV1[line.StopCount];
                        for (int i = 0; i < stops.Length; i++)
                        {
                            if (!line.TryGetStop(i, out var s))
                                return false;
                            stops[i] = new ColorStopV1(s.StopOffset, s.PaletteIndex, s.Alpha);
                        }

                        created = new PaintLinearGradientV1(line.Extend, lg.X0, lg.Y0, lg.X1, lg.Y1, lg.X2, lg.Y2, stops);
                        break;
                    case 5:
                        if (!paint.TryGetPaintVarLinearGradient(out var vlg) || !vlg.TryGetColorLine(out var vline))
                            return false;
                        var vstops = new VarColorStopV1[vline.StopCount];
                        for (int i = 0; i < vstops.Length; i++)
                        {
                            if (!vline.TryGetStop(i, out var s))
                                return false;
                            vstops[i] = new VarColorStopV1(s.StopOffset, s.PaletteIndex, s.Alpha, s.VarIndexBase);
                        }

                        created = new PaintVarLinearGradientV1(vline.Extend, vlg.X0, vlg.Y0, vlg.X1, vlg.Y1, vlg.X2, vlg.Y2, vlg.VarIndexBase, vstops);
                        break;
                    case 6:
                        if (!paint.TryGetPaintRadialGradient(out var rg) || !rg.TryGetColorLine(out line))
                            return false;
                        stops = new ColorStopV1[line.StopCount];
                        for (int i = 0; i < stops.Length; i++)
                        {
                            if (!line.TryGetStop(i, out var s))
                                return false;
                            stops[i] = new ColorStopV1(s.StopOffset, s.PaletteIndex, s.Alpha);
                        }

                        created = new PaintRadialGradientV1(line.Extend, rg.X0, rg.Y0, rg.Radius0, rg.X1, rg.Y1, rg.Radius1, stops);
                        break;
                    case 7:
                        if (!paint.TryGetPaintVarRadialGradient(out var vrg) || !vrg.TryGetColorLine(out vline))
                            return false;
                        vstops = new VarColorStopV1[vline.StopCount];
                        for (int i = 0; i < vstops.Length; i++)
                        {
                            if (!vline.TryGetStop(i, out var s))
                                return false;
                            vstops[i] = new VarColorStopV1(s.StopOffset, s.PaletteIndex, s.Alpha, s.VarIndexBase);
                        }

                        created = new PaintVarRadialGradientV1(vline.Extend, vrg.X0, vrg.Y0, vrg.Radius0, vrg.X1, vrg.Y1, vrg.Radius1, vrg.VarIndexBase, vstops);
                        break;
                    case 8:
                        if (!paint.TryGetPaintSweepGradient(out var sg) || !sg.TryGetColorLine(out line))
                            return false;
                        stops = new ColorStopV1[line.StopCount];
                        for (int i = 0; i < stops.Length; i++)
                        {
                            if (!line.TryGetStop(i, out var s))
                                return false;
                            stops[i] = new ColorStopV1(s.StopOffset, s.PaletteIndex, s.Alpha);
                        }

                        created = new PaintSweepGradientV1(line.Extend, sg.CenterX, sg.CenterY, sg.StartAngle, sg.EndAngle, stops);
                        break;
                    case 9:
                        if (!paint.TryGetPaintVarSweepGradient(out var vsg) || !vsg.TryGetColorLine(out vline))
                            return false;
                        vstops = new VarColorStopV1[vline.StopCount];
                        for (int i = 0; i < vstops.Length; i++)
                        {
                            if (!vline.TryGetStop(i, out var s))
                                return false;
                            vstops[i] = new VarColorStopV1(s.StopOffset, s.PaletteIndex, s.Alpha, s.VarIndexBase);
                        }

                        created = new PaintVarSweepGradientV1(vline.Extend, vsg.CenterX, vsg.CenterY, vsg.StartAngle, vsg.EndAngle, vsg.VarIndexBase, vstops);
                        break;
                    case 10:
                        if (!paint.TryGetPaintGlyph(out var glyph) || !glyph.TryGetPaint(out var glyphPaint))
                            return false;
                        if (!TryConvertPaint(glyphPaint, layerList, cache, inProgress, ref usesVarPaint, out var childPaint))
                            return false;
                        created = new PaintGlyphV1(childPaint, glyph.GlyphId);
                        break;
                    case 11:
                        if (!paint.TryGetPaintColrGlyph(out var colrGlyph))
                            return false;
                        created = new PaintColrGlyphV1(colrGlyph.GlyphId);
                        break;
                    case 12:
                        if (!paint.TryGetPaintTransform(out var t) || !t.TryGetPaint(out var tPaint) || !t.TryGetTransform(out var affine))
                            return false;
                        if (!TryConvertPaint(tPaint, layerList, cache, inProgress, ref usesVarPaint, out childPaint))
                            return false;
                        created = new PaintTransformV1(childPaint, affine);
                        break;
                    case 13:
                        if (!paint.TryGetPaintVarTransform(out var vt) || !vt.TryGetPaint(out var vtPaint) || !vt.TryGetTransform(out affine, out uint varIndexBase))
                            return false;
                        if (!TryConvertPaint(vtPaint, layerList, cache, inProgress, ref usesVarPaint, out childPaint))
                            return false;
                        created = new PaintVarTransformV1(childPaint, affine, varIndexBase);
                        break;
                    case 14:
                        if (!paint.TryGetPaintTranslate(out var tr) || !tr.TryGetPaint(out var trPaint))
                            return false;
                        if (!TryConvertPaint(trPaint, layerList, cache, inProgress, ref usesVarPaint, out childPaint))
                            return false;
                        created = new PaintTranslateV1(childPaint, tr.Dx, tr.Dy);
                        break;
                    case 15:
                        if (!paint.TryGetPaintVarTranslate(out var vtr) || !vtr.TryGetPaint(out var vtrPaint))
                            return false;
                        if (!TryConvertPaint(vtrPaint, layerList, cache, inProgress, ref usesVarPaint, out childPaint))
                            return false;
                        created = new PaintVarTranslateV1(childPaint, vtr.Dx, vtr.Dy, vtr.VarIndexBase);
                        break;
                    case 16:
                        if (!paint.TryGetPaintScale(out var sc) || !sc.TryGetPaint(out var scPaint))
                            return false;
                        if (!TryConvertPaint(scPaint, layerList, cache, inProgress, ref usesVarPaint, out childPaint))
                            return false;
                        created = new PaintScaleV1(childPaint, sc.ScaleX, sc.ScaleY);
                        break;
                    case 17:
                        if (!paint.TryGetPaintVarScale(out var vsc) || !vsc.TryGetPaint(out var vscPaint))
                            return false;
                        if (!TryConvertPaint(vscPaint, layerList, cache, inProgress, ref usesVarPaint, out childPaint))
                            return false;
                        created = new PaintVarScaleV1(childPaint, vsc.ScaleX, vsc.ScaleY, vsc.VarIndexBase);
                        break;
                    case 18:
                        if (!paint.TryGetPaintScaleAroundCenter(out var sac) || !sac.TryGetPaint(out var sacPaint))
                            return false;
                        if (!TryConvertPaint(sacPaint, layerList, cache, inProgress, ref usesVarPaint, out childPaint))
                            return false;
                        created = new PaintScaleAroundCenterV1(childPaint, sac.ScaleX, sac.ScaleY, sac.CenterX, sac.CenterY);
                        break;
                    case 19:
                        if (!paint.TryGetPaintVarScaleAroundCenter(out var vsac) || !vsac.TryGetPaint(out var vsacPaint))
                            return false;
                        if (!TryConvertPaint(vsacPaint, layerList, cache, inProgress, ref usesVarPaint, out childPaint))
                            return false;
                        created = new PaintVarScaleAroundCenterV1(childPaint, vsac.ScaleX, vsac.ScaleY, vsac.CenterX, vsac.CenterY, vsac.VarIndexBase);
                        break;
                    case 20:
                        if (!paint.TryGetPaintScaleUniform(out var su) || !su.TryGetPaint(out var suPaint))
                            return false;
                        if (!TryConvertPaint(suPaint, layerList, cache, inProgress, ref usesVarPaint, out childPaint))
                            return false;
                        created = new PaintScaleUniformV1(childPaint, su.Scale);
                        break;
                    case 21:
                        if (!paint.TryGetPaintVarScaleUniform(out var vsu) || !vsu.TryGetPaint(out var vsuPaint))
                            return false;
                        if (!TryConvertPaint(vsuPaint, layerList, cache, inProgress, ref usesVarPaint, out childPaint))
                            return false;
                        created = new PaintVarScaleUniformV1(childPaint, vsu.Scale, vsu.VarIndexBase);
                        break;
                    case 22:
                        if (!paint.TryGetPaintScaleUniformAroundCenter(out var suac) || !suac.TryGetPaint(out var suacPaint))
                            return false;
                        if (!TryConvertPaint(suacPaint, layerList, cache, inProgress, ref usesVarPaint, out childPaint))
                            return false;
                        created = new PaintScaleUniformAroundCenterV1(childPaint, suac.Scale, suac.CenterX, suac.CenterY);
                        break;
                    case 23:
                        if (!paint.TryGetPaintVarScaleUniformAroundCenter(out var vsuac) || !vsuac.TryGetPaint(out var vsuacPaint))
                            return false;
                        if (!TryConvertPaint(vsuacPaint, layerList, cache, inProgress, ref usesVarPaint, out childPaint))
                            return false;
                        created = new PaintVarScaleUniformAroundCenterV1(childPaint, vsuac.Scale, vsuac.CenterX, vsuac.CenterY, vsuac.VarIndexBase);
                        break;
                    case 24:
                        if (!paint.TryGetPaintRotate(out var rot) || !rot.TryGetPaint(out var rotPaint))
                            return false;
                        if (!TryConvertPaint(rotPaint, layerList, cache, inProgress, ref usesVarPaint, out childPaint))
                            return false;
                        created = new PaintRotateV1(childPaint, rot.Angle);
                        break;
                    case 25:
                        if (!paint.TryGetPaintVarRotate(out var vrot) || !vrot.TryGetPaint(out var vrotPaint))
                            return false;
                        if (!TryConvertPaint(vrotPaint, layerList, cache, inProgress, ref usesVarPaint, out childPaint))
                            return false;
                        created = new PaintVarRotateV1(childPaint, vrot.Angle, vrot.VarIndexBase);
                        break;
                    case 26:
                        if (!paint.TryGetPaintRotateAroundCenter(out var rac) || !rac.TryGetPaint(out var racPaint))
                            return false;
                        if (!TryConvertPaint(racPaint, layerList, cache, inProgress, ref usesVarPaint, out childPaint))
                            return false;
                        created = new PaintRotateAroundCenterV1(childPaint, rac.Angle, rac.CenterX, rac.CenterY);
                        break;
                    case 27:
                        if (!paint.TryGetPaintVarRotateAroundCenter(out var vrac) || !vrac.TryGetPaint(out var vracPaint))
                            return false;
                        if (!TryConvertPaint(vracPaint, layerList, cache, inProgress, ref usesVarPaint, out childPaint))
                            return false;
                        created = new PaintVarRotateAroundCenterV1(childPaint, vrac.Angle, vrac.CenterX, vrac.CenterY, vrac.VarIndexBase);
                        break;
                    case 28:
                        if (!paint.TryGetPaintSkew(out var skew) || !skew.TryGetPaint(out var skewPaint))
                            return false;
                        if (!TryConvertPaint(skewPaint, layerList, cache, inProgress, ref usesVarPaint, out childPaint))
                            return false;
                        created = new PaintSkewV1(childPaint, skew.XSkewAngle, skew.YSkewAngle);
                        break;
                    case 29:
                        if (!paint.TryGetPaintVarSkew(out var vskew) || !vskew.TryGetPaint(out var vskewPaint))
                            return false;
                        if (!TryConvertPaint(vskewPaint, layerList, cache, inProgress, ref usesVarPaint, out childPaint))
                            return false;
                        created = new PaintVarSkewV1(childPaint, vskew.XSkewAngle, vskew.YSkewAngle, vskew.VarIndexBase);
                        break;
                    case 30:
                        if (!paint.TryGetPaintSkewAroundCenter(out var skewac) || !skewac.TryGetPaint(out var skewacPaint))
                            return false;
                        if (!TryConvertPaint(skewacPaint, layerList, cache, inProgress, ref usesVarPaint, out childPaint))
                            return false;
                        created = new PaintSkewAroundCenterV1(childPaint, skewac.XSkewAngle, skewac.YSkewAngle, skewac.CenterX, skewac.CenterY);
                        break;
                    case 31:
                        if (!paint.TryGetPaintVarSkewAroundCenter(out var vskewac) || !vskewac.TryGetPaint(out var vskewacPaint))
                            return false;
                        if (!TryConvertPaint(vskewacPaint, layerList, cache, inProgress, ref usesVarPaint, out childPaint))
                            return false;
                        created = new PaintVarSkewAroundCenterV1(childPaint, vskewac.XSkewAngle, vskewac.YSkewAngle, vskewac.CenterX, vskewac.CenterY, vskewac.VarIndexBase);
                        break;
                    case 32:
                        if (!paint.TryGetPaintComposite(out var composite) ||
                            !composite.TryGetSourcePaint(out var sourcePaint) ||
                            !composite.TryGetBackdropPaint(out var backdropPaint))
                            return false;
                        if (!TryConvertPaint(sourcePaint, layerList, cache, inProgress, ref usesVarPaint, out var src))
                            return false;
                        if (!TryConvertPaint(backdropPaint, layerList, cache, inProgress, ref usesVarPaint, out var backdrop))
                            return false;
                        created = new PaintCompositeV1(src, composite.CompositeMode, backdrop);
                        break;
                    default:
                        return false;
                }

                inProgress.Remove(offset);
                cache.Add(offset, created);
                converted = created;
                return true;
            }
        }
    }

    private byte[] BuildTable()
    {
        if (_isRaw)
            return GetRawBytes(_data);

        return _isStructuredV1 ? BuildV1Bytes() : BuildV0Bytes();
    }

    private static byte[] GetRawBytes(ReadOnlyMemory<byte> data)
    {
        if (MemoryMarshal.TryGetArray(data, out ArraySegment<byte> segment) &&
            segment.Array is not null &&
            segment.Offset == 0 &&
            segment.Count == segment.Array.Length)
        {
            return segment.Array;
        }

        return data.ToArray();
    }

    private byte[] BuildV0Bytes()
    {
        // COLR v0: header(14) + baseGlyphRecords + layerRecords
        if (_baseGlyphs.Count > ushort.MaxValue)
            throw new InvalidOperationException("COLR baseGlyphRecord count must fit in uint16.");

        var baseGlyphs = _baseGlyphs.ToArray();
        Array.Sort(baseGlyphs, static (a, b) => a.BaseGlyphId.CompareTo(b.BaseGlyphId));

        int baseCount = baseGlyphs.Length;

        int totalLayers = 0;
        for (int i = 0; i < baseCount; i++)
            totalLayers = checked(totalLayers + baseGlyphs[i].Layers.Length);

        if (totalLayers > ushort.MaxValue)
            throw new InvalidOperationException("COLR layerRecord count must fit in uint16.");

        int baseGlyphRecordsOffset = 14;
        int layerRecordsOffset = checked(baseGlyphRecordsOffset + (baseCount * 6));
        int length = checked(layerRecordsOffset + (totalLayers * 4));

        byte[] table = new byte[length];
        var span = table.AsSpan();

        BigEndian.WriteUInt16(span, 0, 0); // version
        BigEndian.WriteUInt16(span, 2, (ushort)baseCount);
        BigEndian.WriteUInt32(span, 4, (uint)baseGlyphRecordsOffset);
        BigEndian.WriteUInt32(span, 8, (uint)layerRecordsOffset);
        BigEndian.WriteUInt16(span, 12, (ushort)totalLayers);

        int basePos = baseGlyphRecordsOffset;
        int layerPos = layerRecordsOffset;
        ushort firstLayerIndex = 0;

        for (int i = 0; i < baseCount; i++)
        {
            var entry = baseGlyphs[i];
            ushort layerCount = checked((ushort)entry.Layers.Length);

            BigEndian.WriteUInt16(span, basePos + 0, entry.BaseGlyphId);
            BigEndian.WriteUInt16(span, basePos + 2, firstLayerIndex);
            BigEndian.WriteUInt16(span, basePos + 4, layerCount);
            basePos += 6;

            for (int j = 0; j < entry.Layers.Length; j++)
            {
                var layer = entry.Layers[j];
                BigEndian.WriteUInt16(span, layerPos + 0, layer.LayerGlyphId);
                BigEndian.WriteUInt16(span, layerPos + 2, layer.PaletteIndex);
                layerPos += 4;
            }

            firstLayerIndex = checked((ushort)(firstLayerIndex + layerCount));
        }

        return table;
    }

    private readonly struct BaseGlyphEntry
    {
        public ushort BaseGlyphId { get; }
        public LayerEntry[] Layers { get; }

        public BaseGlyphEntry(ushort baseGlyphId, LayerEntry[] layers)
        {
            BaseGlyphId = baseGlyphId;
            Layers = layers;
        }
    }

    public readonly struct LayerEntry
    {
        public ushort LayerGlyphId { get; }
        public ushort PaletteIndex { get; }

        public LayerEntry(ushort layerGlyphId, ushort paletteIndex)
        {
            LayerGlyphId = layerGlyphId;
            PaletteIndex = paletteIndex;
        }

        public bool UsesForegroundColor => PaletteIndex == 0xFFFF;
    }
}
