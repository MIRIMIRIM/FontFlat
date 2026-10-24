using FontWeave.Core;
using FontWeave.Tables.Variation;

namespace FontWeave.Tables;

/// <summary>The canonical COLR v1 compiler (card T6-02 §4.1, the L476 order the
/// coordinator's correction pinned): header (34 for v1) → the v0 compatibility
/// arrays (layer records then base records, the T6-01 OQ-T6-01-8 order) →
/// BaseGlyphList → LayerList → ClipList (the ClipBoxes tail-placed after the
/// rows, each in its own format) → the paint node region in canonical ordinal
/// order (parents first, children later - a re-used paint table occurs later in
/// the file than any of the paint tables that use it), each node's auxiliary
/// ColorLine/VarColorLine/Affine2x3/VarAffine2x3 immediately behind its node →
/// VarIndexMap → ItemVariationStore tail-placed (T6-03 §4, OQ-T6-03-9 - the two
/// side structures the graph cannot reach, so the paint region's fixup surface
/// stays untouched). Because the read assigned the ordinals in exactly this
/// order and every plan position is fixed before the first byte lands, the
/// whole table is one forward write pass; every offset value is a single
/// forward emission from its own base. Models without var content keep both
/// header slots at 0 (the T6-02 static hosts' byte-for-byte face). Shared
/// identity holds by construction (a shared node is one object with one
/// ordinal); equal nodes never merge (zero hash-consing, OQ-T6-02-14).</summary>
public static partial class ColrTableCompiler
{
    /// <summary>Explicit v1 root-model factory from owned parts (card §4.1):
    /// the root re-answers every §3 invariant over the owned graph - the BGL
    /// ascent and glyph domains, the clip relations and domains, the slice
    /// bounds, the cross-table palette entries with the 0xFFFF sentinel through,
    /// the PaintGlyph and PaintColrGlyph glyph domains (the latter's record
    /// presence too), the graph's acyclicity, the direct
    /// edges' L476 forward face (a child must sit at a later ordinal than its
    /// referencing node - the canonical emission order demands it) and the
    /// depth/node budgets - through a walk over the owned node set (BGL roots
    /// first in glyph order, then every node the roots cannot reach, so an
    /// orphan cycle refuses too). There are no cross-model children, so the
    /// SameSource guard surface is empty by construction. The paints must
    /// arrive in canonical ordinal order (paints[i].Id == i) - the order is the
    /// model's own addressing, not a plan the compiler picks. The variation
    /// slots (T6-03 §4) ride their own pre-validated models: var paints demand
    /// a store (the OQ-T6-03-2 mirror), a map demands one too (the OQ-8
    /// dangling mirror), while a format-2 clip box needs neither (OQ-3).
    /// The two v0 compatibility arrays are optional (spec L625-628: a v1 table
    /// may carry them, and a v1 renderer queries them for a glyph without a
    /// BaseGlyphList row), so a caller that keeps them passes them through the
    /// same T6-01 rule set under the v1 zero grace; the default is the empty
    /// pair.</summary>
    public static ReadStatus TryCreate(IReadOnlyList<ColrBaseGlyphPaint> baseGlyphPaints,
        IReadOnlyList<int> layerPaintNodeIds, IReadOnlyList<ColrClip> clips,
        IReadOnlyList<ColrPaint> paints, int numGlyphs, int? cpalNumPaletteEntries,
        out ColrTable? model, out FontDiagnostic diagnostic,
        DeltaSetIndexMapTable? varIndexMap = null,
        VariationStoreTable? itemVariationStore = null, BuildLimits? limits = null,
        DataLocation location = default,
        IReadOnlyList<ColrBaseGlyphRecord>? compatBaseGlyphs = null,
        IReadOnlyList<ColrLayerRecord>? compatLayers = null)
    {
        model = null;
        ArgumentNullException.ThrowIfNull(baseGlyphPaints);
        ArgumentNullException.ThrowIfNull(layerPaintNodeIds);
        ArgumentNullException.ThrowIfNull(clips);
        ArgumentNullException.ThrowIfNull(paints);
        location = location == default ? DataLocation.Anonymous(ColrLayout.Tag) : location;
        var budget = new BuildBudget(limits);
        if (numGlyphs is < 0 or > ushort.MaxValue)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                location.AtLocal(DataField.None, 0), out diagnostic,
                unchecked((ulong)(long)numGlyphs), ushort.MaxValue);
        if (!budget.Work(24L * paints.Count + 6L * baseGlyphPaints.Count +
                4L * layerPaintNodeIds.Count + 11L * clips.Count, location, out diagnostic))
            return ReadStatus.ResourceLimit;
        // The compatibility arrays' plan positions (layer records then base records,
        // the T6-01 order the emission below uses); an absent pair declares no
        // records and its offsets stay zero.
        var compatBases = compatBaseGlyphs ?? [];
        var compatLayerRecords = compatLayers ?? [];
        uint compatLayerAt = 0, compatBaseAt = 0;
        long compatLength = 0;
        if (compatLayerRecords.Count > 0)
        {
            compatLayerAt = (uint)(ColrLayout.V1HeaderLength);
            compatLength += 4L * compatLayerRecords.Count;
        }
        if (compatBases.Count > 0)
        {
            compatBaseAt = (uint)(ColrLayout.V1HeaderLength + compatLength);
            compatLength += 6L * compatBases.Count;
        }
        if (compatBases.Count > ushort.MaxValue || compatLayerRecords.Count > ushort.MaxValue)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                location.AtLocal(DataField.Count,
                    compatBases.Count > ushort.MaxValue ? 2UL : 12UL), out diagnostic,
                (ulong)(compatBases.Count > ushort.MaxValue
                    ? compatBases.Count : compatLayerRecords.Count), (ulong)ushort.MaxValue);
        if (compatBases.Count > 0 || compatLayerRecords.Count > 0)
        {
            ReadStatus records = ValidateRecords(compatBases, compatLayerRecords,
                numGlyphs, cpalNumPaletteEntries, compatBaseAt, compatLayerAt, location,
                budget, out diagnostic);
            if (records != ReadStatus.Done) return records;
        }
        // The presence matrix faces (the read side's OQ-2/OQ-8 mirrors): a var
        // paint without a store and a map without a store refuse before the
        // walk; a store alone stays legal (the implicit-identity shape).
        bool hasVarPaint = false;
        foreach (var paint in paints)
        {
            if (paint.Format is 3 or 5 or 7 or 9 or 13 or 15 or 17 or 19 or 21
                or 23 or 25 or 27 or 29 or 31)
            {
                hasVarPaint = true;
                break;
            }
        }
        if ((hasVarPaint || varIndexMap is not null) && itemVariationStore is null)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                location.AtLocal(DataField.Offset, 30), out diagnostic,
                itemVariationStore is null ? 1UL : 0UL, 0UL);
        for (int i = 0; i < paints.Count; i++)
        {
            if (paints[i].Id != i)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    location.AtLocal(DataField.Payload, 0, i), out diagnostic,
                    (uint)paints[i].Id, (uint)i);
        }
        // The BaseGlyphList face: strict ascent, glyph domain, root range.
        int previous = -1;
        for (int i = 0; i < baseGlyphPaints.Count; i++)
        {
            var row = baseGlyphPaints[i];
            var at = location.AtLocal(DataField.GlyphId, 0, i);
            if (row.GlyphId >= (ushort)numGlyphs)
                return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                    at, out diagnostic, row.GlyphId, (ulong)numGlyphs);
            if (i > 0 && row.GlyphId <= previous)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    at, out diagnostic, row.GlyphId, unchecked((uint)previous));
            previous = row.GlyphId;
            if ((uint)row.PaintNodeId >= (uint)paints.Count)
                return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                    location.AtLocal(DataField.Offset, 0, i), out diagnostic,
                    (uint)row.PaintNodeId, (uint)paints.Count);
        }
        // The LayerList face: node ranges; -1 is the dead entry's marker.
        for (int i = 0; i < layerPaintNodeIds.Count; i++)
        {
            int nodeId = layerPaintNodeIds[i];
            if (nodeId < -1 || nodeId >= paints.Count)
                return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                    location.AtLocal(DataField.Offset, 0, i), out diagnostic,
                    unchecked((ulong)(long)nodeId), (uint)paints.Count);
        }
        // The ClipList face: glyph domains, strict ascent, no overlap, no
        // reversed ranges - the read side's relations re-answered (the boxes'
        // FWORDs carry no order relation, so no box domain exists).
        int previousStart = -1, previousEnd = -1;
        for (int i = 0; i < clips.Count; i++)
        {
            var clip = clips[i];
            var at = location.AtLocal(DataField.GlyphId, 0, i);
            if (clip.StartGlyphId >= (ushort)numGlyphs || clip.EndGlyphId >= (ushort)numGlyphs)
                return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                    at, out diagnostic,
                    clip.StartGlyphId >= (ushort)numGlyphs ? clip.StartGlyphId : clip.EndGlyphId,
                    (ulong)numGlyphs);
            if (previousStart >= 0 && clip.StartGlyphId <= previousStart)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    at, out diagnostic, clip.StartGlyphId, unchecked((uint)previousStart));
            if (previousEnd >= 0 && clip.StartGlyphId <= previousEnd)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.OverlappingRange,
                    at, out diagnostic, clip.StartGlyphId, unchecked((uint)(previousEnd + 1)));
            if (clip.StartGlyphId > clip.EndGlyphId)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    at, out diagnostic, clip.StartGlyphId, clip.EndGlyphId);
            previousStart = clip.StartGlyphId;
            previousEnd = clip.EndGlyphId;
        }
        // The paint graph face: the owned walk with the read side's faces.
        var owned = new OwnedWalk(paints, baseGlyphPaints, layerPaintNodeIds, numGlyphs,
            cpalNumPaletteEntries, location);
        for (int i = 0; i < paints.Count; i++)
        {
            ReadStatus status = owned.Visit(i, 0, budget, out diagnostic);
            if (status != ReadStatus.Done) return status;
        }
        model = new([.. compatBases], [.. compatLayerRecords], [.. baseGlyphPaints],
            [.. layerPaintNodeIds], [.. clips], [.. paints], numGlyphs, compatBaseAt,
            compatLayerAt, 0, 0, 0, varIndexMap, itemVariationStore,
            [], [], [], location, 0);
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>The v1 canonical emission (card §4.1): the plan positions every
    /// piece before the first byte lands, then one forward pass writes the
    /// bytes and their (already computable) offsets.</summary>
    public static ReadStatus TryCompileV1(ColrTable model, out byte[]? bytes,
        out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        bytes = null;
        var budget = new BuildBudget(limits);
        long length = ColrLayout.V1HeaderLength;
        uint layerRecordsAt = 0, baseRecordsAt = 0;
        if (model.LayerCount > 0)
        {
            layerRecordsAt = (uint)length;
            length += 4L * model.LayerCount;
        }
        if (model.BaseGlyphCount > 0)
        {
            baseRecordsAt = (uint)length;
            length += 6L * model.BaseGlyphCount;
        }
        uint bglAt = (uint)length;
        length += 4L + 6L * model.BaseGlyphPaintCount;
        uint llAt = 0;
        bool hasLayerList = model.LayerPaintNodeIds.Count > 0;
        if (hasLayerList)
        {
            llAt = (uint)length;
            length += 4L + 4L * model.LayerPaintNodeIds.Count;
        }
        uint clipListAt = 0;
        long clipBoxesLength = 0;
        if (model.ClipCount > 0)
        {
            clipListAt = (uint)length;
            // The rows take 7 bytes each; each box takes its own format's size
            // (9 on format 1, 13 with the trailing varIndexBase on format 2).
            foreach (var clip in model.Clips)
                clipBoxesLength += clip.Box.Format == 2
                    ? ColrLayout.ClipBoxFormat2Size : ColrLayout.ClipBoxFormat1Size;
            length += 5L + 7L * model.ClipCount + clipBoxesLength;
        }
        uint paintRegionAt = (uint)length;
        var nodeAt = new uint[model.PaintCount];
        var colorLineAt = new uint[model.PaintCount];
        var affineAt = new uint[model.PaintCount];
        for (int i = 0; i < model.PaintCount; i++)
        {
            var node = model.Paints[i];
            nodeAt[i] = (uint)length;
            length += PaintNodeSize(node);
            long colorLineLength = ColorLineLengthOf(node);
            if (colorLineLength > 0)
            {
                colorLineAt[i] = (uint)length;
                length += colorLineLength;
            }
            if (node is ColrPaintTransform or ColrPaintVarTransform)
            {
                affineAt[i] = (uint)length;
                length += node is ColrPaintTransform
                    ? ColrLayout.Affine2x3Size : ColrLayout.VarAffine2x3Size;
            }
        }
        // The two variation slots' bodies, tail-placed in canonical order -
        // map then store (OQ-T6-03-9): both are side structures the paint
        // graph cannot reach, so the paint region's fixup surface stays
        // untouched and every offset remains one forward emission.
        uint varIndexMapAt = 0, itemVariationStoreAt = 0;
        if (model.VarIndexMap is { } map)
        {
            varIndexMapAt = (uint)length;
            length += DeltaSetIndexMapTableCompiler.Measure(map);
        }
        if (model.ItemVariationStore is { } store)
        {
            itemVariationStoreAt = (uint)length;
            length += VariationStoreTableCompiler.Measure(store);
        }
        if (length > int.MaxValue)
            return ReadWire.Fail(ReadStatus.ResourceLimit, DiagnosticCode.BudgetExceeded,
                model.Location.AtLocal(DataField.Length, 0), out diagnostic,
                (ulong)length, (ulong)int.MaxValue);
        // The Offset24 domains: every paint-region offset and every clip box
        // offset must fit its 3-byte word (the plan makes them all forward, so
        // the region span is the maximum face).
        if (length - paintRegionAt > 0x1000000 || model.ClipCount > 0 &&
            (ulong)(5L + 7L * model.ClipCount + clipBoxesLength) > 0x1000000)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                model.Location.AtLocal(DataField.Offset, paintRegionAt), out diagnostic,
                (ulong)(length - paintRegionAt), 0x1000000UL);
        if (!budget.Work(length, model.Location, out diagnostic) ||
            !budget.Buffer(length, model.Location, out diagnostic) ||
            !budget.Output(length, model.Location, out diagnostic))
            return ReadStatus.ResourceLimit;
        var output = new byte[(int)length];
        // The header words (the canonical plan's own coordinates; the variation
        // slots stay zero - T6-02 has no var content to write).
        RequireWritten(ColrView.TryWriteVersion(output, model.Version, out _, model.Location));
        RequireWritten(ColrView.TryWriteNumBaseGlyphRecords(output,
            (ushort)model.BaseGlyphCount, out _, model.Location));
        RequireWritten(ColrView.TryWriteBaseGlyphRecordsOffset(output, baseRecordsAt,
            out _, model.Location));
        RequireWritten(ColrView.TryWriteLayerRecordsOffset(output, layerRecordsAt,
            out _, model.Location));
        RequireWritten(ColrView.TryWriteNumLayerRecords(output, (ushort)model.LayerCount,
            out _, model.Location));
        // The five WhenVersion slots: the generator emits no TryWrite for
        // version-conditional scalars (its write family covers unconditional
        // fixed positions only - a registered generation-face observation), so
        // the compiler writes the five fixed 32-bit words directly; the window
        // is the plan's own header, already capacity-checked. The two variation
        // slots emit their plan positions when the model carries the slots and
        // stay 0 otherwise (the T6-02 static hosts' byte-for-byte face).
        WriteU32(output, 14, bglAt);
        WriteU32(output, 18, llAt);
        WriteU32(output, 22, clipListAt);
        WriteU32(output, 26, varIndexMapAt);
        WriteU32(output, 30, itemVariationStoreAt);
        // The compatibility arrays (layer records then base records, the T6-01
        // OQ-T6-01-8 order), rows byte-for-byte in field order.
        for (int i = 0; i < model.LayerCount; i++)
        {
            var row = model.Layers[i];
            RequireWritten(ColrView.TryWriteLayerRecordAt(output,
                layerRecordsAt + (ulong)i * ColrLayout.LayerRecordSize,
                new ColrView.LayerRecordData(i, row.GlyphId, row.PaletteIndex), out _,
                model.Location));
        }
        for (int i = 0; i < model.BaseGlyphCount; i++)
        {
            var row = model.BaseGlyphs[i];
            RequireWritten(ColrView.TryWriteBaseGlyphRecordAt(output,
                baseRecordsAt + (ulong)i * ColrLayout.BaseGlyphRecordSize,
                new ColrView.BaseGlyphRecordData(i, row.GlyphId, row.FirstLayerIndex,
                    row.NumLayers), out _, model.Location));
        }
        // BaseGlyphList: the count word plus the records, each paint offset a
        // forward u32 from the BaseGlyphList start.
        WriteU32(output, (int)bglAt, (uint)model.BaseGlyphPaintCount);
        for (int i = 0; i < model.BaseGlyphPaintCount; i++)
        {
            var row = model.BaseGlyphPaints[i];
            ulong rowAt = bglAt + 4 + (ulong)i * ColrLayout.BaseGlyphPaintRecordSize;
            WriteU16(output, (int)rowAt, row.GlyphId);
            WriteU32(output, (int)rowAt + 2, nodeAt[row.PaintNodeId] - bglAt);
        }
        // LayerList: the count word plus the entry words (live entries recompute
        // from the plan; a dead entry re-emits its source word - its only
        // surviving identity - and the plan must still reach it).
        if (hasLayerList)
        {
            WriteU32(output, (int)llAt, (uint)model.LayerPaintNodeIds.Count);
            for (int i = 0; i < model.LayerPaintNodeIds.Count; i++)
            {
                int nodeId = model.LayerPaintNodeIds[i];
                ulong entryAt = llAt + 4 + (ulong)i * ColrLayout.LayerPaintOffsetStride;
                if (nodeId >= 0)
                {
                    WriteU32(output, (int)entryAt, nodeAt[nodeId] - llAt);
                }
                else
                {
                    uint source = model.LayerSourceOffsets[i];
                    if (llAt + source >= (ulong)length)
                        return ReadWire.Fail(ReadStatus.InvalidFormat,
                            DiagnosticCode.InvalidValue,
                            model.Location.AtLocal(DataField.Offset, entryAt, i),
                            out diagnostic, llAt + source, (ulong)length);
                    WriteU32(output, (int)entryAt, source);
                }
            }
        }
        // ClipList: format 1, the count, the rows (start/end immediate, the
        // boxOffset a forward Offset24), the boxes tail-placed after the rows
        // in each row's own format (1 or 2; the format-2 box carries its
        // varIndexBase verbatim - OQ-T6-03-3).
        if (model.ClipCount > 0)
        {
            output[(int)clipListAt] = 1;
            WriteU32(output, (int)clipListAt + 1, (uint)model.ClipCount);
            long boxCursor = 0;
            for (int i = 0; i < model.ClipCount; i++)
            {
                var clip = model.Clips[i];
                ulong rowAt = clipListAt + 5 + (ulong)i * ColrLayout.ClipRecordSize;
                uint boxAt = clipListAt + 5 + (uint)(7L * model.ClipCount + boxCursor);
                boxCursor += clip.Box.Format == 2
                    ? ColrLayout.ClipBoxFormat2Size : ColrLayout.ClipBoxFormat1Size;
                WriteU16(output, (int)rowAt, clip.StartGlyphId);
                WriteU16(output, (int)rowAt + 2, clip.EndGlyphId);
                WriteU24(output, (int)rowAt + 4, boxAt - clipListAt);
                output[(int)boxAt] = clip.Box.Format;
                WriteS16(output, (int)boxAt + 1, clip.Box.XMin);
                WriteS16(output, (int)boxAt + 3, clip.Box.YMin);
                WriteS16(output, (int)boxAt + 5, clip.Box.XMax);
                WriteS16(output, (int)boxAt + 7, clip.Box.YMax);
                if (clip.Box.Format == 2)
                    WriteU32(output, (int)boxAt + 9, clip.Box.VarIndexBase);
            }
        }
        // The paint region, ordinal order (parents first); the auxiliaries ride
        // right behind their nodes, so their offsets are the node's own size.
        for (int i = 0; i < model.PaintCount; i++)
        {
            var node = model.Paints[i];
            WritePaintNode(output, nodeAt[i], node, nodeAt);
            if (colorLineAt[i] > 0)
            {
                if (node is ColrPaintVarLinearGradient varLinear)
                    WriteVarColorLine(output, colorLineAt[i], varLinear.ColorLine);
                else if (node is ColrPaintVarRadialGradient varRadial)
                    WriteVarColorLine(output, colorLineAt[i], varRadial.ColorLine);
                else if (node is ColrPaintVarSweepGradient varSweep)
                    WriteVarColorLine(output, colorLineAt[i], varSweep.ColorLine);
                else
                    WriteColorLine(output, colorLineAt[i], ColorLineOf(node)!);
            }
            if (affineAt[i] > 0)
            {
                if (node is ColrPaintVarTransform varTransform)
                    WriteVarAffine(output, affineAt[i], varTransform.Transform);
                else
                    WriteAffine(output, affineAt[i], ((ColrPaintTransform)node).Transform);
            }
        }
        // The variation bodies at their tail positions, each through its own
        // compiler (the map's format word is the model's own; the store re-emits
        // its width classes - the LONG_WORDS rows keep their int32 face).
        if (model.VarIndexMap is { } mapModel)
            DeltaSetIndexMapTableCompiler.WritePlanned(mapModel,
                output.AsSpan((int)varIndexMapAt));
        if (model.ItemVariationStore is { } storeModel)
            VariationStoreTableCompiler.WritePlanned(storeModel,
                output.AsSpan((int)itemVariationStoreAt));
        budget.RecordCompiledTable(output.Length);
        bytes = output;
        diagnostic = default;
        return ReadStatus.Done;
    }

    private static ColrColorLine? ColorLineOf(ColrPaint node) => node switch
    {
        ColrPaintLinearGradient linear => linear.ColorLine,
        ColrPaintRadialGradient radial => radial.ColorLine,
        ColrPaintSweepGradient sweep => sweep.ColorLine,
        _ => null,
    };

    private static void WritePaintNode(byte[] output, uint at, ColrPaint node, uint[] nodeAt)
    {
        int p = (int)at;
        output[p] = node.Format;
        switch (node)
        {
            case ColrPaintLayers layers:
                output[p + 1] = (byte)layers.LayerNodeIds.Count;
                WriteU32(output, p + 2, layers.FirstLayerIndex);
                break;
            case ColrPaintSolid solid:
                WriteU16(output, p + 1, solid.PaletteIndex);
                WriteU16(output, p + 3, solid.Alpha);
                break;
            case ColrPaintLinearGradient linear:
                WriteU24(output, p + 1, (uint)PaintNodeSize(node));
                WriteS16(output, p + 4, linear.X0);
                WriteS16(output, p + 6, linear.Y0);
                WriteS16(output, p + 8, linear.X1);
                WriteS16(output, p + 10, linear.Y1);
                WriteS16(output, p + 12, linear.X2);
                WriteS16(output, p + 14, linear.Y2);
                break;
            case ColrPaintRadialGradient radial:
                WriteU24(output, p + 1, (uint)PaintNodeSize(node));
                WriteS16(output, p + 4, radial.X0);
                WriteS16(output, p + 6, radial.Y0);
                WriteU16(output, p + 8, radial.Radius0);
                WriteS16(output, p + 10, radial.X1);
                WriteS16(output, p + 12, radial.Y1);
                WriteU16(output, p + 14, radial.Radius1);
                break;
            case ColrPaintSweepGradient sweep:
                WriteU24(output, p + 1, (uint)PaintNodeSize(node));
                WriteS16(output, p + 4, sweep.CenterX);
                WriteS16(output, p + 6, sweep.CenterY);
                WriteU16(output, p + 8, sweep.StartAngle);
                WriteU16(output, p + 10, sweep.EndAngle);
                break;
            case ColrPaintGlyph glyph:
                WriteU24(output, p + 1, nodeAt[glyph.PaintNodeId] - at);
                WriteU16(output, p + 4, glyph.GlyphId);
                break;
            case ColrPaintColrGlyph colrGlyph:
                WriteU16(output, p + 1, colrGlyph.GlyphId);
                break;
            case ColrPaintTransform transform:
                WriteU24(output, p + 1, nodeAt[transform.PaintNodeId] - at);
                WriteU24(output, p + 4, (uint)PaintNodeSize(node)); // the affine, right behind
                break;
            case ColrPaintTranslate translate:
                WriteU24(output, p + 1, nodeAt[translate.PaintNodeId] - at);
                WriteS16(output, p + 4, translate.Dx);
                WriteS16(output, p + 6, translate.Dy);
                break;
            case ColrPaintScale scale:
                WriteU24(output, p + 1, nodeAt[scale.PaintNodeId] - at);
                WriteU16(output, p + 4, scale.ScaleX);
                WriteU16(output, p + 6, scale.ScaleY);
                break;
            case ColrPaintScaleAroundCenter scaleAroundCenter:
                WriteU24(output, p + 1, nodeAt[scaleAroundCenter.PaintNodeId] - at);
                WriteU16(output, p + 4, scaleAroundCenter.ScaleX);
                WriteU16(output, p + 6, scaleAroundCenter.ScaleY);
                WriteS16(output, p + 8, scaleAroundCenter.CenterX);
                WriteS16(output, p + 10, scaleAroundCenter.CenterY);
                break;
            case ColrPaintScaleUniform scaleUniform:
                WriteU24(output, p + 1, nodeAt[scaleUniform.PaintNodeId] - at);
                WriteU16(output, p + 4, scaleUniform.Scale);
                break;
            case ColrPaintScaleUniformAroundCenter scaleUniformAroundCenter:
                WriteU24(output, p + 1, nodeAt[scaleUniformAroundCenter.PaintNodeId] - at);
                WriteU16(output, p + 4, scaleUniformAroundCenter.Scale);
                WriteS16(output, p + 6, scaleUniformAroundCenter.CenterX);
                WriteS16(output, p + 8, scaleUniformAroundCenter.CenterY);
                break;
            case ColrPaintRotate rotate:
                WriteU24(output, p + 1, nodeAt[rotate.PaintNodeId] - at);
                WriteU16(output, p + 4, rotate.Angle);
                break;
            case ColrPaintRotateAroundCenter rotateAroundCenter:
                WriteU24(output, p + 1, nodeAt[rotateAroundCenter.PaintNodeId] - at);
                WriteU16(output, p + 4, rotateAroundCenter.Angle);
                WriteS16(output, p + 6, rotateAroundCenter.CenterX);
                WriteS16(output, p + 8, rotateAroundCenter.CenterY);
                break;
            case ColrPaintSkew skew:
                WriteU24(output, p + 1, nodeAt[skew.PaintNodeId] - at);
                WriteU16(output, p + 4, skew.XSkewAngle);
                WriteU16(output, p + 6, skew.YSkewAngle);
                break;
            case ColrPaintSkewAroundCenter skewAroundCenter:
                WriteU24(output, p + 1, nodeAt[skewAroundCenter.PaintNodeId] - at);
                WriteU16(output, p + 4, skewAroundCenter.XSkewAngle);
                WriteU16(output, p + 6, skewAroundCenter.YSkewAngle);
                WriteS16(output, p + 8, skewAroundCenter.CenterX);
                WriteS16(output, p + 10, skewAroundCenter.CenterY);
                break;
            // The fourteen var twins (T6-03 §4): the static word order plus the
            // trailing varIndexBase (fmt13's rides the VarAffine2x3 behind the
            // node), the auxiliaries right behind their nodes.
            case ColrPaintVarSolid varSolid:
                WriteU16(output, p + 1, varSolid.PaletteIndex);
                WriteU16(output, p + 3, varSolid.Alpha);
                WriteU32(output, p + 5, varSolid.VarIndexBase);
                break;
            case ColrPaintVarLinearGradient varLinear:
                WriteU24(output, p + 1, (uint)PaintNodeSize(node));
                WriteS16(output, p + 4, varLinear.X0);
                WriteS16(output, p + 6, varLinear.Y0);
                WriteS16(output, p + 8, varLinear.X1);
                WriteS16(output, p + 10, varLinear.Y1);
                WriteS16(output, p + 12, varLinear.X2);
                WriteS16(output, p + 14, varLinear.Y2);
                WriteU32(output, p + 16, varLinear.VarIndexBase);
                break;
            case ColrPaintVarRadialGradient varRadial:
                WriteU24(output, p + 1, (uint)PaintNodeSize(node));
                WriteS16(output, p + 4, varRadial.X0);
                WriteS16(output, p + 6, varRadial.Y0);
                WriteU16(output, p + 8, varRadial.Radius0);
                WriteS16(output, p + 10, varRadial.X1);
                WriteS16(output, p + 12, varRadial.Y1);
                WriteU16(output, p + 14, varRadial.Radius1);
                WriteU32(output, p + 16, varRadial.VarIndexBase);
                break;
            case ColrPaintVarSweepGradient varSweep:
                WriteU24(output, p + 1, (uint)PaintNodeSize(node));
                WriteS16(output, p + 4, varSweep.CenterX);
                WriteS16(output, p + 6, varSweep.CenterY);
                WriteU16(output, p + 8, varSweep.StartAngle);
                WriteU16(output, p + 10, varSweep.EndAngle);
                WriteU32(output, p + 12, varSweep.VarIndexBase);
                break;
            case ColrPaintVarTransform varTransform:
                WriteU24(output, p + 1, nodeAt[varTransform.PaintNodeId] - at);
                WriteU24(output, p + 4, (uint)PaintNodeSize(node)); // the affine, right behind
                break;
            case ColrPaintVarTranslate or ColrPaintVarScale
                or ColrPaintVarScaleAroundCenter or ColrPaintVarScaleUniform
                or ColrPaintVarScaleUniformAroundCenter or ColrPaintVarRotate
                or ColrPaintVarRotateAroundCenter or ColrPaintVarSkew
                or ColrPaintVarSkewAroundCenter:
            {
                var (childNodeId, words, baseOffset) = VarScalarParts(node);
                WriteU24(output, p + 1, nodeAt[childNodeId] - at);
                for (int k = 0; k < words.Length; k++)
                    WriteU16(output, p + 4 + 2 * k, words[k]);
                WriteU32(output, p + baseOffset, node.OwnVarIndexBase);
                break;
            }
            default: // ColrPaintComposite
                var composite = (ColrPaintComposite)node;
                WriteU24(output, p + 1, nodeAt[composite.SourceNodeId] - at);
                output[p + 4] = composite.CompositeMode;
                WriteU24(output, p + 5, nodeAt[composite.BackdropNodeId] - at);
                break;
        }
    }

    private static long PaintNodeSize(ColrPaint node) => node.Format switch
    {
        1 => ColrLayout.PaintColrLayersSize,
        2 => ColrLayout.PaintSolidSize,
        3 => ColrLayout.PaintVarSolidSize,
        4 => ColrLayout.PaintLinearGradientSize,
        5 => ColrLayout.PaintVarLinearGradientSize,
        6 => ColrLayout.PaintRadialGradientSize,
        7 => ColrLayout.PaintVarRadialGradientSize,
        8 => ColrLayout.PaintSweepGradientSize,
        9 => ColrLayout.PaintVarSweepGradientSize,
        10 => ColrLayout.PaintGlyphSize,
        11 => ColrLayout.PaintColrGlyphSize,
        12 => ColrLayout.PaintTransformSize,
        13 => ColrLayout.PaintVarTransformSize,
        14 => ColrLayout.PaintTranslateSize,
        15 => ColrLayout.PaintVarTranslateSize,
        16 => ColrLayout.PaintScaleSize,
        17 => ColrLayout.PaintVarScaleSize,
        18 => ColrLayout.PaintScaleAroundCenterSize,
        19 => ColrLayout.PaintVarScaleAroundCenterSize,
        20 => ColrLayout.PaintScaleUniformSize,
        21 => ColrLayout.PaintVarScaleUniformSize,
        22 => ColrLayout.PaintScaleUniformAroundCenterSize,
        23 => ColrLayout.PaintVarScaleUniformAroundCenterSize,
        24 => ColrLayout.PaintRotateSize,
        25 => ColrLayout.PaintVarRotateSize,
        26 => ColrLayout.PaintRotateAroundCenterSize,
        27 => ColrLayout.PaintVarRotateAroundCenterSize,
        28 => ColrLayout.PaintSkewSize,
        29 => ColrLayout.PaintVarSkewSize,
        30 => ColrLayout.PaintSkewAroundCenterSize,
        31 => ColrLayout.PaintVarSkewAroundCenterSize,
        _ => ColrLayout.PaintCompositeSize,
    };

    /// <summary>A node's color-line auxiliary length in bytes (0 = none): the
    /// static lines' 6-byte stops or the var lines' 10-byte stops (T6-03 §4).</summary>
    private static long ColorLineLengthOf(ColrPaint node) => node switch
    {
        ColrPaintLinearGradient linear => StaticColorLineLength(linear.ColorLine),
        ColrPaintRadialGradient radial => StaticColorLineLength(radial.ColorLine),
        ColrPaintSweepGradient sweep => StaticColorLineLength(sweep.ColorLine),
        ColrPaintVarLinearGradient varLinear => VarColorLineLength(varLinear.ColorLine),
        ColrPaintVarRadialGradient varRadial => VarColorLineLength(varRadial.ColorLine),
        ColrPaintVarSweepGradient varSweep => VarColorLineLength(varSweep.ColorLine),
        _ => 0,
    };

    private static long StaticColorLineLength(ColrColorLine line) =>
        ColrLayout.ColorLineHeaderSize + (long)line.Stops.Count * ColrLayout.ColorStopSize;

    private static long VarColorLineLength(ColrVarColorLine line) =>
        ColrLayout.ColorLineHeaderSize + (long)line.Stops.Count * ColrLayout.VarColorStopSize;

    /// <summary>The var scalar family's wire parts: the child ordinal, the
    /// scalar words in wire order, and the varIndexBase's own offset (§0's
    /// per-format field order).</summary>
    private static (int ChildNodeId, ushort[] Words, int BaseOffset) VarScalarParts(
        ColrPaint node) => node switch
    {
        ColrPaintVarTranslate n => (n.PaintNodeId, [RawWord(n.Dx), RawWord(n.Dy)], 8),
        ColrPaintVarScale n => (n.PaintNodeId, [n.ScaleX, n.ScaleY], 8),
        ColrPaintVarScaleAroundCenter n => (n.PaintNodeId,
            [n.ScaleX, n.ScaleY, RawWord(n.CenterX), RawWord(n.CenterY)], 12),
        ColrPaintVarScaleUniform n => (n.PaintNodeId, [n.Scale], 6),
        ColrPaintVarScaleUniformAroundCenter n => (n.PaintNodeId,
            [n.Scale, RawWord(n.CenterX), RawWord(n.CenterY)], 10),
        ColrPaintVarRotate n => (n.PaintNodeId, [n.Angle], 6),
        ColrPaintVarRotateAroundCenter n => (n.PaintNodeId,
            [n.Angle, RawWord(n.CenterX), RawWord(n.CenterY)], 10),
        ColrPaintVarSkew n => (n.PaintNodeId, [n.XSkewAngle, n.YSkewAngle], 8),
        ColrPaintVarSkewAroundCenter n => (n.PaintNodeId,
            [n.XSkewAngle, n.YSkewAngle, RawWord(n.CenterX), RawWord(n.CenterY)], 12),
        _ => throw new System.InvalidOperationException(
            "A non-scalar var paint has no scalar words."),
    };

    private static ushort RawWord(short value) => unchecked((ushort)value);

    /// <summary>The owned-graph walk: the read walk's face set over node ids
    /// instead of wire offsets. The caller drives the root order (BGL glyph
    /// order, then the unreached remainder, so an orphan cycle refuses too);
    /// each visit answers its node's domains and recurses through the children.
    /// The direct edges carry the L476 forward face (a child ordinal must be
    /// later than the referencing node's - the wire's forward-offset limitation,
    /// which the compile's ordinal-order emission needs to hold); the indirect
    /// edges (PaintColrLayers slices, PaintColrGlyph targets) skip only that
    /// face, exactly like the read walk.</summary>
    private sealed class OwnedWalk
    {
        internal OwnedWalk(IReadOnlyList<ColrPaint> paints,
            IReadOnlyList<ColrBaseGlyphPaint> baseGlyphPaints,
            IReadOnlyList<int> layerPaintNodeIds, int numGlyphs,
            int? cpalNumPaletteEntries, DataLocation location)
        {
            Paints = paints;
            BaseGlyphPaints = baseGlyphPaints;
            LayerPaintNodeIds = layerPaintNodeIds;
            NumGlyphs = numGlyphs;
            CpalEntries = cpalNumPaletteEntries;
            Location = location;
        }

        internal IReadOnlyList<ColrPaint> Paints { get; }
        internal IReadOnlyList<ColrBaseGlyphPaint> BaseGlyphPaints { get; }
        internal IReadOnlyList<int> LayerPaintNodeIds { get; }
        internal int NumGlyphs { get; }
        internal int? CpalEntries { get; }
        internal DataLocation Location { get; }
        internal HashSet<int> Visited { get; } = [];
        internal HashSet<int> Path { get; } = [];

        internal ReadStatus Visit(int nodeId, int depth, BuildBudget budget,
            out FontDiagnostic diagnostic)
        {
            if (Path.Contains(nodeId))
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    Location.AtLocal(DataField.Payload, 0, nodeId), out diagnostic,
                    (uint)nodeId, 0UL);
            if (!Visited.Add(nodeId))
                return Done(out diagnostic);
            if (Visited.Count > ColrView.MaxPaintGraphNodes)
                return ReadWire.Fail(ReadStatus.ResourceLimit, DiagnosticCode.BudgetExceeded,
                    Location.AtLocal(DataField.WorkUnits, 0, nodeId), out diagnostic,
                    (ulong)Visited.Count, (ulong)ColrView.MaxPaintGraphNodes);
            if (depth > ColrView.MaxPaintGraphDepth)
                return ReadWire.Fail(ReadStatus.ResourceLimit, DiagnosticCode.BudgetExceeded,
                    Location.AtLocal(DataField.WorkUnits, 0, nodeId), out diagnostic,
                    (ulong)depth, (ulong)ColrView.MaxPaintGraphDepth);
            if (!budget.Work(24, Location, out diagnostic)) return ReadStatus.ResourceLimit;
            ReadStatus status = Paints[nodeId] switch
            {
                ColrPaintLayers layers => VisitLayers(layers, depth, budget, out diagnostic),
                ColrPaintGlyph glyph => VisitPaintGlyph(glyph, depth, budget,
                    out diagnostic),
                ColrPaintColrGlyph colrGlyph => VisitColrGlyph(colrGlyph, depth, budget,
                    out diagnostic),
                ColrPaintTransform transform => VisitChild(nodeId, transform.PaintNodeId,
                    depth, budget, out diagnostic),
                ColrPaintVarTransform varTransform => VisitChild(nodeId,
                    varTransform.PaintNodeId, depth, budget, out diagnostic),
                ColrPaintTranslate translate => VisitChild(nodeId, translate.PaintNodeId, depth, budget,
                    out diagnostic),
                ColrPaintScale scale => VisitChild(nodeId, scale.PaintNodeId, depth, budget,
                    out diagnostic),
                ColrPaintScaleAroundCenter scaleAroundCenter => VisitChild(nodeId,
                    scaleAroundCenter.PaintNodeId, depth, budget, out diagnostic),
                ColrPaintScaleUniform scaleUniform => VisitChild(nodeId,
                    scaleUniform.PaintNodeId, depth, budget, out diagnostic),
                ColrPaintScaleUniformAroundCenter scaleUniformAroundCenter => VisitChild(
                    nodeId, scaleUniformAroundCenter.PaintNodeId, depth, budget,
                    out diagnostic),
                ColrPaintRotate rotate => VisitChild(nodeId, rotate.PaintNodeId, depth,
                    budget, out diagnostic),
                ColrPaintRotateAroundCenter rotateAroundCenter => VisitChild(nodeId,
                    rotateAroundCenter.PaintNodeId, depth, budget, out diagnostic),
                ColrPaintSkew skew => VisitChild(nodeId, skew.PaintNodeId, depth, budget,
                    out diagnostic),
                ColrPaintSkewAroundCenter skewAroundCenter => VisitChild(nodeId,
                    skewAroundCenter.PaintNodeId, depth, budget, out diagnostic),
                ColrPaintVarTranslate varTranslate => VisitChild(nodeId,
                    varTranslate.PaintNodeId, depth, budget, out diagnostic),
                ColrPaintVarScale varScale => VisitChild(nodeId, varScale.PaintNodeId,
                    depth, budget, out diagnostic),
                ColrPaintVarScaleAroundCenter varScaleAroundCenter => VisitChild(nodeId,
                    varScaleAroundCenter.PaintNodeId, depth, budget, out diagnostic),
                ColrPaintVarScaleUniform varScaleUniform => VisitChild(nodeId,
                    varScaleUniform.PaintNodeId, depth, budget, out diagnostic),
                ColrPaintVarScaleUniformAroundCenter varScaleUniformAroundCenter =>
                    VisitChild(nodeId, varScaleUniformAroundCenter.PaintNodeId, depth,
                        budget, out diagnostic),
                ColrPaintVarRotate varRotate => VisitChild(nodeId, varRotate.PaintNodeId,
                    depth, budget, out diagnostic),
                ColrPaintVarRotateAroundCenter varRotateAroundCenter => VisitChild(nodeId,
                    varRotateAroundCenter.PaintNodeId, depth, budget, out diagnostic),
                ColrPaintVarSkew varSkew => VisitChild(nodeId, varSkew.PaintNodeId, depth,
                    budget, out diagnostic),
                ColrPaintVarSkewAroundCenter varSkewAroundCenter => VisitChild(nodeId,
                    varSkewAroundCenter.PaintNodeId, depth, budget, out diagnostic),
                ColrPaintComposite composite => VisitComposite(composite, depth, budget,
                    out diagnostic),
                _ => VisitDomains(nodeId, out diagnostic), // solids + gradients
            };
            if (status != ReadStatus.Done) return status;
            Path.Remove(nodeId);
            return Done(out diagnostic);
        }

        private static ReadStatus Done(out FontDiagnostic diagnostic)
        {
            diagnostic = default;
            return ReadStatus.Done;
        }

        private ReadStatus VisitLayers(ColrPaintLayers layers, int depth, BuildBudget budget,
            out FontDiagnostic diagnostic)
        {
            if ((ulong)layers.FirstLayerIndex + (ulong)layers.LayerNodeIds.Count >
                (ulong)LayerPaintNodeIds.Count)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    Location.AtLocal(DataField.Payload, 2, layers.Id), out diagnostic,
                    (ulong)layers.FirstLayerIndex + (ulong)layers.LayerNodeIds.Count,
                    (uint)LayerPaintNodeIds.Count);
            foreach (int entry in layers.LayerNodeIds)
            {
                int nodeId = LayerPaintNodeIds[entry];
                if (nodeId < 0)
                    return ReadWire.Fail(ReadStatus.InvalidIndex,
                        DiagnosticCode.IndexOutOfRange,
                        Location.AtLocal(DataField.Payload, 0, entry), out diagnostic,
                        unchecked((ulong)(long)nodeId), 0UL);
                ReadStatus status = Visit(nodeId, depth + 1, budget, out diagnostic);
                if (status != ReadStatus.Done) return status;
            }
            return Done(out diagnostic);
        }

        private ReadStatus VisitChild(int parentOrdinal, int nodeId, int depth,
            BuildBudget budget, out FontDiagnostic diagnostic)
        {
            if ((uint)nodeId >= (uint)Paints.Count)
                return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                    Location.AtLocal(DataField.Offset, 1, parentOrdinal), out diagnostic,
                    unchecked((ulong)(long)nodeId), (uint)Paints.Count);
            if (nodeId <= parentOrdinal)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    Location.AtLocal(DataField.Offset, 1, parentOrdinal), out diagnostic,
                    (uint)nodeId, (uint)parentOrdinal);
            return Visit(nodeId, depth + 1, budget, out diagnostic);
        }

        private ReadStatus VisitPaintGlyph(ColrPaintGlyph glyph, int depth,
            BuildBudget budget, out FontDiagnostic diagnostic)
        {
            // The output glyph domain (spec L1019) re-answered before the
            // child edge - the read side's VisitPaintGlyph face in its order.
            if (glyph.GlyphId >= (ushort)NumGlyphs)
                return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                    Location.AtLocal(DataField.GlyphId, 0, glyph.Id), out diagnostic,
                    glyph.GlyphId, (ulong)NumGlyphs);
            return VisitChild(glyph.Id, glyph.PaintNodeId, depth, budget, out diagnostic);
        }

        private ReadStatus VisitColrGlyph(ColrPaintColrGlyph colrGlyph, int depth,
            BuildBudget budget, out FontDiagnostic diagnostic)
        {
            if (colrGlyph.GlyphId >= (ushort)NumGlyphs)
                return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                    Location.AtLocal(DataField.GlyphId, 0, colrGlyph.Id), out diagnostic,
                    colrGlyph.GlyphId, (ulong)NumGlyphs);
            int? root = null;
            foreach (var row in BaseGlyphPaints)
            {
                if (row.GlyphId == colrGlyph.GlyphId)
                {
                    root = row.PaintNodeId;
                    break;
                }
            }
            if (root is not int nodeId)
                return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.GlyphNotMapped,
                    Location.AtLocal(DataField.GlyphId, 0, colrGlyph.Id), out diagnostic,
                    colrGlyph.GlyphId, 0UL);
            return Visit(nodeId, depth + 1, budget, out diagnostic);
        }

        private ReadStatus VisitComposite(ColrPaintComposite composite, int depth,
            BuildBudget budget, out FontDiagnostic diagnostic)
        {
            ReadStatus status = VisitChild(composite.Id, composite.SourceNodeId, depth,
                budget, out diagnostic);
            if (status != ReadStatus.Done) return status;
            return VisitChild(composite.Id, composite.BackdropNodeId, depth, budget,
                out diagnostic);
        }

        private ReadStatus VisitDomains(int nodeId, out FontDiagnostic diagnostic)
        {
            switch (Paints[nodeId])
            {
                case ColrPaintSolid solid:
                    if (CpalEntries is int entries &&
                        solid.PaletteIndex != ColrLayout.ForegroundPaletteIndex &&
                        solid.PaletteIndex >= (uint)entries)
                        return ReadWire.Fail(ReadStatus.InvalidIndex,
                            DiagnosticCode.IndexOutOfRange,
                            Location.AtLocal(DataField.Payload, 1, nodeId), out diagnostic,
                            solid.PaletteIndex, unchecked((ulong)(long)entries));
                    break;
                case ColrPaintVarSolid varSolid:
                    // The var palette entry is immutable (no var slot) - the
                    // same cross-table domain as the static face.
                    if (CpalEntries is int varEntries &&
                        varSolid.PaletteIndex != ColrLayout.ForegroundPaletteIndex &&
                        varSolid.PaletteIndex >= (uint)varEntries)
                        return ReadWire.Fail(ReadStatus.InvalidIndex,
                            DiagnosticCode.IndexOutOfRange,
                            Location.AtLocal(DataField.Payload, 1, nodeId), out diagnostic,
                            varSolid.PaletteIndex, unchecked((ulong)(long)varEntries));
                    break;
                case ColrPaintLinearGradient linear:
                    return VisitColorLine(linear.ColorLine, nodeId, out diagnostic);
                case ColrPaintRadialGradient radial:
                    return VisitColorLine(radial.ColorLine, nodeId, out diagnostic);
                case ColrPaintSweepGradient sweep:
                    return VisitColorLine(sweep.ColorLine, nodeId, out diagnostic);
                case ColrPaintVarLinearGradient varLinear:
                    return VisitVarColorLine(varLinear.ColorLine, nodeId, out diagnostic);
                case ColrPaintVarRadialGradient varRadial:
                    return VisitVarColorLine(varRadial.ColorLine, nodeId, out diagnostic);
                case ColrPaintVarSweepGradient varSweep:
                    return VisitVarColorLine(varSweep.ColorLine, nodeId, out diagnostic);
            }
            return Done(out diagnostic);
        }

        private ReadStatus VisitColorLine(ColrColorLine colorLine, int nodeId,
            out FontDiagnostic diagnostic)
        {
            for (int i = 0; i < colorLine.Stops.Count; i++)
            {
                var stop = colorLine.Stops[i];
                if (CpalEntries is int entries &&
                    stop.PaletteIndex != ColrLayout.ForegroundPaletteIndex &&
                    stop.PaletteIndex >= (uint)entries)
                    return ReadWire.Fail(ReadStatus.InvalidIndex,
                        DiagnosticCode.IndexOutOfRange,
                        Location.AtLocal(DataField.Payload, 5UL + 6UL * (ulong)i, i),
                        out diagnostic, stop.PaletteIndex, unchecked((ulong)(long)entries));
            }
            return Done(out diagnostic);
        }

        /// <summary>The var line's stop domain: the palette entry re-answers the
        /// cross-table authority (immutable, no slot); the stopOffset and alpha
        /// words are byte-faithful with no value domain of their own.</summary>
        private ReadStatus VisitVarColorLine(ColrVarColorLine colorLine, int nodeId,
            out FontDiagnostic diagnostic)
        {
            for (int i = 0; i < colorLine.Stops.Count; i++)
            {
                var stop = colorLine.Stops[i];
                if (CpalEntries is int entries &&
                    stop.PaletteIndex != ColrLayout.ForegroundPaletteIndex &&
                    stop.PaletteIndex >= (uint)entries)
                    return ReadWire.Fail(ReadStatus.InvalidIndex,
                        DiagnosticCode.IndexOutOfRange,
                        Location.AtLocal(DataField.Payload, 7UL + 10UL * (ulong)i, i),
                        out diagnostic, stop.PaletteIndex, unchecked((ulong)(long)entries));
            }
            return Done(out diagnostic);
        }
    }

    private static void WriteColorLine(byte[] output, uint at, ColrColorLine colorLine)
    {
        output[(int)at] = colorLine.Extend;
        WriteU16(output, (int)at + 1, (uint)colorLine.Stops.Count);
        for (int i = 0; i < colorLine.Stops.Count; i++)
        {
            var stop = colorLine.Stops[i];
            WriteU16(output, (int)at + 3 + 6 * i, unchecked((ushort)stop.StopOffset));
            WriteU16(output, (int)at + 5 + 6 * i, stop.PaletteIndex);
            WriteU16(output, (int)at + 7 + 6 * i, stop.Alpha);
        }
    }

    /// <summary>The variable ColorLine (T6-03 §4): the same 3-byte head, the
    /// 10-byte stops carrying each stop's own varIndexBase verbatim.</summary>
    private static void WriteVarColorLine(byte[] output, uint at, ColrVarColorLine colorLine)
    {
        output[(int)at] = colorLine.Extend;
        WriteU16(output, (int)at + 1, (uint)colorLine.Stops.Count);
        for (int i = 0; i < colorLine.Stops.Count; i++)
        {
            var stop = colorLine.Stops[i];
            WriteU16(output, (int)at + 3 + 10 * i, unchecked((ushort)stop.StopOffset));
            WriteU16(output, (int)at + 5 + 10 * i, stop.PaletteIndex);
            WriteU16(output, (int)at + 7 + 10 * i, stop.Alpha);
            WriteU32(output, (int)at + 9 + 10 * i, stop.VarIndexBase);
        }
    }

    private static void WriteAffine(byte[] output, uint at, ColrAffine2x3 affine)
    {
        WriteU32(output, (int)at, affine.Xx);
        WriteU32(output, (int)at + 4, affine.Yx);
        WriteU32(output, (int)at + 8, affine.Xy);
        WriteU32(output, (int)at + 12, affine.Yy);
        WriteU32(output, (int)at + 16, affine.Dx);
        WriteU32(output, (int)at + 20, affine.Dy);
    }

    /// <summary>The variable Affine2x3 (T6-03 §4): the six Fixed words plus the
    /// trailing varIndexBase - the only var base PaintVarTransform carries.</summary>
    private static void WriteVarAffine(byte[] output, uint at, ColrVarAffine2x3 affine)
    {
        WriteU32(output, (int)at, affine.Xx);
        WriteU32(output, (int)at + 4, affine.Yx);
        WriteU32(output, (int)at + 8, affine.Xy);
        WriteU32(output, (int)at + 12, affine.Yy);
        WriteU32(output, (int)at + 16, affine.Dx);
        WriteU32(output, (int)at + 20, affine.Dy);
        WriteU32(output, (int)at + 24, affine.VarIndexBase);
    }

    private static void WriteU16(byte[] output, int at, uint value)
    {
        output[at] = (byte)((value >> 8) & 0xFF);
        output[at + 1] = (byte)(value & 0xFF);
    }

    private static void WriteS16(byte[] output, int at, short value) =>
        WriteU16(output, at, unchecked((ushort)value));

    private static void WriteU24(byte[] output, int at, uint value)
    {
        output[at] = (byte)((value >> 16) & 0xFF);
        output[at + 1] = (byte)((value >> 8) & 0xFF);
        output[at + 2] = (byte)(value & 0xFF);
    }

    private static void WriteU32(byte[] output, int at, uint value)
    {
        WriteU16(output, at, value >> 16);
        WriteU16(output, at + 2, value & 0xFFFF);
    }
}
