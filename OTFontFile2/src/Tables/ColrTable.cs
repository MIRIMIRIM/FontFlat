using OTFontFile2.SourceGen;

namespace OTFontFile2.Tables;

[OtTable("COLR", 14, GenerateTryCreate = false, GenerateStorage = false)]
public readonly partial struct ColrTable
{
    private readonly TableSlice _table;
    private readonly ushort _version;

    // COLR v0 (also present in v1 for backward compatibility)
    private readonly ushort _numBaseGlyphRecords;
    private readonly int _baseGlyphRecordsOffset;
    private readonly int _layerRecordsOffset;
    private readonly ushort _numLayerRecords;

    // COLR v1
    private readonly int _baseGlyphListOffset;
    private readonly int _layerListOffset;
    private readonly int _clipListOffset;
    private readonly int _varIndexMapOffset;
    private readonly int _itemVariationStoreOffset;

    private ColrTable(
        TableSlice table,
        ushort version,
        ushort numBaseGlyphRecords,
        int baseGlyphRecordsOffset,
        int layerRecordsOffset,
        ushort numLayerRecords,
        int baseGlyphListOffset,
        int layerListOffset,
        int clipListOffset,
        int varIndexMapOffset,
        int itemVariationStoreOffset)
    {
        _table = table;
        _version = version;

        _numBaseGlyphRecords = numBaseGlyphRecords;
        _baseGlyphRecordsOffset = baseGlyphRecordsOffset;
        _layerRecordsOffset = layerRecordsOffset;
        _numLayerRecords = numLayerRecords;

        _baseGlyphListOffset = baseGlyphListOffset;
        _layerListOffset = layerListOffset;
        _clipListOffset = clipListOffset;
        _varIndexMapOffset = varIndexMapOffset;
        _itemVariationStoreOffset = itemVariationStoreOffset;
    }

    public static bool TryCreate(TableSlice table, out ColrTable colr)
    {
        colr = default;

        // v0 header is 14 bytes; v1 header is 34 bytes.
        if (table.Length < 14)
            return false;

        var data = table.Span;
        ushort version = BigEndian.ReadUInt16(data, 0);

        if (version == 0)
        {
            if (!TryReadV0Header(data, table.Length, out ushort numBaseGlyphRecords, out int baseGlyphRecordsOffset, out int layerRecordsOffset, out ushort numLayerRecords))
                return false;

            colr = new ColrTable(
                table,
                version,
                numBaseGlyphRecords,
                baseGlyphRecordsOffset,
                layerRecordsOffset,
                numLayerRecords,
                baseGlyphListOffset: 0,
                layerListOffset: 0,
                clipListOffset: 0,
                varIndexMapOffset: 0,
                itemVariationStoreOffset: 0);
            return true;
        }

        if (version == 1)
        {
            if (table.Length < 34)
                return false;

            if (!TryReadV0HeaderOptional(data, table.Length, out ushort numBaseGlyphRecords, out int baseGlyphRecordsOffset, out int layerRecordsOffset, out ushort numLayerRecords))
                return false;

            uint baseGlyphListOffsetU = BigEndian.ReadUInt32(data, 14);
            uint layerListOffsetU = BigEndian.ReadUInt32(data, 18);
            uint clipListOffsetU = BigEndian.ReadUInt32(data, 22);
            uint varIndexMapOffsetU = BigEndian.ReadUInt32(data, 26);
            uint itemVariationStoreOffsetU = BigEndian.ReadUInt32(data, 30);

            if (baseGlyphListOffsetU == 0 || layerListOffsetU == 0)
                return false;

            if (baseGlyphListOffsetU > int.MaxValue || layerListOffsetU > int.MaxValue || clipListOffsetU > int.MaxValue || varIndexMapOffsetU > int.MaxValue || itemVariationStoreOffsetU > int.MaxValue)
                return false;

            int baseGlyphListOffset = (int)baseGlyphListOffsetU;
            int layerListOffset = (int)layerListOffsetU;
            int clipListOffset = (int)clipListOffsetU;
            int varIndexMapOffset = (int)varIndexMapOffsetU;
            int itemVariationStoreOffset = (int)itemVariationStoreOffsetU;

            if (!ValidateBaseGlyphList(data, table.Length, baseGlyphListOffset))
                return false;

            if (!ValidateLayerList(data, table.Length, layerListOffset))
                return false;

            if (clipListOffset != 0 && (uint)clipListOffset > (uint)table.Length - 4)
                return false;

            // Optional offsets: treat invalid as absent.
            if (varIndexMapOffset != 0 && !DeltaSetIndexMap.TryCreate(table, varIndexMapOffset, out _))
                varIndexMapOffset = 0;

            if (itemVariationStoreOffset != 0 && !ItemVariationStore.TryCreate(table, itemVariationStoreOffset, out _))
                itemVariationStoreOffset = 0;

            colr = new ColrTable(
                table,
                version,
                numBaseGlyphRecords,
                baseGlyphRecordsOffset,
                layerRecordsOffset,
                numLayerRecords,
                baseGlyphListOffset,
                layerListOffset,
                clipListOffset,
                varIndexMapOffset,
                itemVariationStoreOffset);
            return true;
        }

        // Unsupported versions are still materialized so callers can read Version and raw bytes.
        colr = new ColrTable(
            table,
            version,
            numBaseGlyphRecords: 0,
            baseGlyphRecordsOffset: 0,
            layerRecordsOffset: 0,
            numLayerRecords: 0,
            baseGlyphListOffset: 0,
            layerListOffset: 0,
            clipListOffset: 0,
            varIndexMapOffset: 0,
            itemVariationStoreOffset: 0);
        return true;
    }

    public ushort Version => _version;
    public bool IsVersion0 => _version == 0;
    public bool IsVersion1 => _version == 1;

    public ushort BaseGlyphRecordCount => _numBaseGlyphRecords;
    public ushort LayerRecordCount => _numLayerRecords;

    public readonly struct BaseGlyphRecord
    {
        public ushort BaseGlyphId { get; }
        public ushort FirstLayerIndex { get; }
        public ushort NumLayers { get; }

        public BaseGlyphRecord(ushort baseGlyphId, ushort firstLayerIndex, ushort numLayers)
        {
            BaseGlyphId = baseGlyphId;
            FirstLayerIndex = firstLayerIndex;
            NumLayers = numLayers;
        }
    }

    public readonly struct LayerRecord
    {
        public ushort LayerGlyphId { get; }
        public ushort PaletteIndex { get; }

        public LayerRecord(ushort layerGlyphId, ushort paletteIndex)
        {
            LayerGlyphId = layerGlyphId;
            PaletteIndex = paletteIndex;
        }

        public bool UsesForegroundColor => PaletteIndex == 0xFFFF;
    }

    public bool TryGetBaseGlyphRecord(int index, out BaseGlyphRecord record)
    {
        record = default;

        if (_version is not (0 or 1))
            return false;

        if ((uint)index >= _numBaseGlyphRecords)
            return false;

        int offset = checked(_baseGlyphRecordsOffset + (index * 6));
        if ((uint)offset > (uint)_table.Length - 6)
            return false;

        var data = _table.Span;
        ushort baseGlyphId = BigEndian.ReadUInt16(data, offset + 0);
        ushort firstLayerIndex = BigEndian.ReadUInt16(data, offset + 2);
        ushort numLayers = BigEndian.ReadUInt16(data, offset + 4);

        uint end = (uint)firstLayerIndex + numLayers;
        if (end > _numLayerRecords)
            return false;

        record = new BaseGlyphRecord(baseGlyphId, firstLayerIndex, numLayers);
        return true;
    }

    public bool TryFindBaseGlyphRecord(ushort baseGlyphId, out BaseGlyphRecord record)
    {
        record = default;

        if (_version is not (0 or 1))
            return false;

        int count = _numBaseGlyphRecords;
        if (count == 0)
            return false;

        int lo = 0;
        int hi = count - 1;
        while (lo <= hi)
        {
            int mid = (lo + hi) >> 1;
            if (!TryGetBaseGlyphRecord(mid, out var midRecord))
                return false;

            if (midRecord.BaseGlyphId == baseGlyphId)
            {
                record = midRecord;
                return true;
            }

            if (midRecord.BaseGlyphId < baseGlyphId)
                lo = mid + 1;
            else
                hi = mid - 1;
        }

        return false;
    }

    public bool TryGetLayerRecord(int index, out LayerRecord record)
    {
        record = default;

        if (_version is not (0 or 1))
            return false;

        if ((uint)index >= _numLayerRecords)
            return false;

        int offset = checked(_layerRecordsOffset + (index * 4));
        if ((uint)offset > (uint)_table.Length - 4)
            return false;

        var data = _table.Span;
        record = new LayerRecord(
            layerGlyphId: BigEndian.ReadUInt16(data, offset + 0),
            paletteIndex: BigEndian.ReadUInt16(data, offset + 2));
        return true;
    }

    public LayerEnumerator EnumerateLayers(BaseGlyphRecord baseGlyphRecord)
        => new(_table, _layerRecordsOffset, _numLayerRecords, baseGlyphRecord.FirstLayerIndex, baseGlyphRecord.NumLayers);

    public ref struct LayerEnumerator
    {
        private readonly TableSlice _colr;
        private readonly int _layerRecordsOffset;
        private readonly int _layerRecordCount;
        private int _index;
        private int _remaining;

        internal LayerEnumerator(TableSlice colr, int layerRecordsOffset, ushort layerRecordCount, ushort firstLayerIndex, ushort numLayers)
        {
            _colr = colr;
            _layerRecordsOffset = layerRecordsOffset;
            _layerRecordCount = layerRecordCount;
            _index = firstLayerIndex;
            _remaining = numLayers;
            Current = default;
        }

        public LayerRecord Current { get; private set; }

        public bool MoveNext()
        {
            if (_remaining <= 0)
                return false;

            if ((uint)_index >= (uint)_layerRecordCount)
            {
                _remaining = 0;
                return false;
            }

            int offset = checked(_layerRecordsOffset + (_index * 4));
            if ((uint)offset > (uint)_colr.Length - 4)
            {
                _remaining = 0;
                return false;
            }

            var data = _colr.Span;
            Current = new LayerRecord(
                layerGlyphId: BigEndian.ReadUInt16(data, offset + 0),
                paletteIndex: BigEndian.ReadUInt16(data, offset + 2));

            _index++;
            _remaining--;
            return true;
        }
    }

    public int BaseGlyphListOffset => _baseGlyphListOffset;
    public int LayerListOffset => _layerListOffset;
    public int ClipListOffset => _clipListOffset;
    public int VarIndexMapOffset => _varIndexMapOffset;
    public int ItemVariationStoreOffset => _itemVariationStoreOffset;

    public bool TryGetBaseGlyphList(out BaseGlyphList baseGlyphList)
    {
        baseGlyphList = default;

        if (!IsVersion1)
            return false;

        return BaseGlyphList.TryCreate(_table, _baseGlyphListOffset, out baseGlyphList);
    }

    public bool TryGetLayerList(out LayerList layerList)
    {
        layerList = default;

        if (!IsVersion1)
            return false;

        return LayerList.TryCreate(_table, _layerListOffset, out layerList);
    }

    public bool TryGetClipList(out ClipList clipList)
    {
        clipList = default;

        if (!IsVersion1)
            return false;

        return _clipListOffset != 0 && ClipList.TryCreate(_table, _clipListOffset, out clipList);
    }

    public bool TryGetVarIndexMap(out DeltaSetIndexMap map)
    {
        map = default;

        if (!IsVersion1)
            return false;

        return _varIndexMapOffset != 0 && DeltaSetIndexMap.TryCreate(_table, _varIndexMapOffset, out map);
    }

    public bool TryGetItemVariationStore(out ItemVariationStore store)
    {
        store = default;

        if (!IsVersion1)
            return false;

        return _itemVariationStoreOffset != 0 && ItemVariationStore.TryCreate(_table, _itemVariationStoreOffset, out store);
    }

    public bool TryGetBaseGlyphPaint(ushort baseGlyphId, out Paint paint)
    {
        paint = default;

        if (!TryGetBaseGlyphList(out var baseGlyphList))
            return false;

        if (!baseGlyphList.TryFindBaseGlyphPaintRecord(baseGlyphId, out var record))
            return false;

        return record.TryGetPaint(out paint);
    }

    [OtSubTable(4, GenerateTryCreate = false, GenerateStorage = false)]
    [OtSequentialRecordArray("BaseGlyphPaintRecord", 4, 6, OutParameterName = "record")]
    public readonly partial struct BaseGlyphList
    {
        private readonly TableSlice _table;
        private readonly int _offset;
        private readonly uint _recordCount;

        private BaseGlyphList(TableSlice colr, int offset, uint recordCount)
        {
            _table = colr;
            _offset = offset;
            _recordCount = recordCount;
        }

        public static bool TryCreate(TableSlice colr, int offset, out BaseGlyphList baseGlyphList)
        {
            baseGlyphList = default;

            if ((uint)offset > (uint)colr.Length - 4)
                return false;

            uint count = BigEndian.ReadUInt32(colr.Span, offset);

            long bytesLong = 4L + (count * 6L);
            if (bytesLong > int.MaxValue)
                return false;

            int bytes = (int)bytesLong;
            if ((uint)offset > (uint)colr.Length - (uint)bytes)
                return false;

            baseGlyphList = new BaseGlyphList(colr, offset, count);
            return true;
        }

        public uint BaseGlyphPaintRecordCount => _recordCount;

        public bool TryFindBaseGlyphPaintRecord(ushort baseGlyphId, out BaseGlyphPaintRecord record)
        {
            record = default;

            if (_recordCount == 0)
                return false;

            if (_recordCount > int.MaxValue)
                return false;

            int count = (int)_recordCount;
            int lo = 0;
            int hi = count - 1;
            while (lo <= hi)
            {
                int mid = (lo + hi) >> 1;
                if (!TryGetBaseGlyphPaintRecord(mid, out var midRecord))
                    return false;

                if (midRecord.BaseGlyphId == baseGlyphId)
                {
                    record = midRecord;
                    return true;
                }

                if (midRecord.BaseGlyphId < baseGlyphId)
                    lo = mid + 1;
                else
                    hi = mid - 1;
            }

            return false;
        }
    }

    [OtSubTable(5, GenerateTryCreate = false)]
    [OtField("Format", OtFieldKind.Byte, 0)]
    [OtField("ClipRecordCount", OtFieldKind.UInt32, 1)]
    [OtSequentialRecordArray("ClipRecord", 5, 7, CountPropertyName = "ClipRecordCount")]
    public readonly partial struct ClipList
    {
        public static bool TryCreate(TableSlice colr, int offset, out ClipList clipList)
        {
            clipList = default;

            // format(1) + numClips(4)
            if ((uint)offset > (uint)colr.Length - 5)
                return false;

            var data = colr.Span;
            if (data[offset + 0] != 1)
                return false;

            uint count = BigEndian.ReadUInt32(data, offset + 1);

            long clipRecordsLong = 5L + (count * 7L);
            if (clipRecordsLong > int.MaxValue)
                return false;

            int clipRecordsLen = (int)clipRecordsLong;
            if ((uint)offset > (uint)colr.Length - (uint)clipRecordsLen)
                return false;

            clipList = new ClipList(colr, offset);
            return true;
        }

        public bool TryGetClipBox(in ClipRecord record, out ClipBox clipBox)
        {
            clipBox = default;

            int abs = checked(_offset + (int)record.ClipBoxOffset);
            if ((uint)abs > (uint)_table.Length - 1)
                return false;

            var data = _table.Span;
            byte format = data[abs + 0];
            if (format == 1)
            {
                // format(1) + xMin(2) + yMin(2) + xMax(2) + yMax(2)
                if ((uint)abs > (uint)_table.Length - 9)
                    return false;

                clipBox = new ClipBox(
                    format: 1,
                    xMin: BigEndian.ReadInt16(data, abs + 1),
                    yMin: BigEndian.ReadInt16(data, abs + 3),
                    xMax: BigEndian.ReadInt16(data, abs + 5),
                    yMax: BigEndian.ReadInt16(data, abs + 7),
                    varIndexBase: 0);
                return true;
            }

            if (format == 2)
            {
                // format(1) + xMin(2) + yMin(2) + xMax(2) + yMax(2) + varIndexBase(4)
                if ((uint)abs > (uint)_table.Length - 13)
                    return false;

                clipBox = new ClipBox(
                    format: 2,
                    xMin: BigEndian.ReadInt16(data, abs + 1),
                    yMin: BigEndian.ReadInt16(data, abs + 3),
                    xMax: BigEndian.ReadInt16(data, abs + 5),
                    yMax: BigEndian.ReadInt16(data, abs + 7),
                    varIndexBase: BigEndian.ReadUInt32(data, abs + 9));
                return true;
            }

            return false;
        }

        public bool TryFindClipBoxForGlyph(ushort glyphId, out ClipBox clipBox)
        {
            clipBox = default;

            uint count = ClipRecordCount;
            if (count == 0)
                return false;

            if (count > int.MaxValue)
                return false;

            int lo = 0;
            int hi = (int)count - 1;
            while (lo <= hi)
            {
                int mid = lo + ((hi - lo) >> 1);
                if (!TryGetClipRecord(mid, out var r))
                    return false;

                if (glyphId < r.StartGlyphId)
                {
                    hi = mid - 1;
                    continue;
                }

                if (glyphId > r.EndGlyphId)
                {
                    lo = mid + 1;
                    continue;
                }

                return TryGetClipBox(r, out clipBox);
            }

            return false;
        }
    }

    public readonly struct ClipRecord
    {
        public ushort StartGlyphId { get; }
        public ushort EndGlyphId { get; }
        public uint ClipBoxOffset { get; }

        public ClipRecord(ushort startGlyphId, ushort endGlyphId, [OtRecordField(OtFieldKind.UInt24)] uint clipBoxOffset)
        {
            StartGlyphId = startGlyphId;
            EndGlyphId = endGlyphId;
            ClipBoxOffset = clipBoxOffset;
        }
    }

    public readonly struct ClipBox
    {
        public byte Format { get; }
        public short XMin { get; }
        public short YMin { get; }
        public short XMax { get; }
        public short YMax { get; }
        public uint VarIndexBase { get; }

        public ClipBox(byte format, short xMin, short yMin, short xMax, short yMax, uint varIndexBase)
        {
            Format = format;
            XMin = xMin;
            YMin = yMin;
            XMax = xMax;
            YMax = yMax;
            VarIndexBase = varIndexBase;
        }

        public bool IsVariable => Format == 2;
    }

    public readonly struct BaseGlyphPaintRecord
    {
        private readonly TableSlice _colr;
        private readonly int _baseGlyphListOffset;

        public ushort BaseGlyphId { get; }
        public uint PaintOffset { get; }

        internal BaseGlyphPaintRecord([OtRecordContext("_table")] TableSlice colr, [OtRecordContext("_offset")] int baseGlyphListOffset, ushort baseGlyphId, uint paintOffset)
        {
            _colr = colr;
            _baseGlyphListOffset = baseGlyphListOffset;
            BaseGlyphId = baseGlyphId;
            PaintOffset = paintOffset;
        }

        public bool TryGetPaint(out Paint paint)
        {
            paint = default;

            if (PaintOffset > int.MaxValue)
                return false;

            int abs = checked(_baseGlyphListOffset + (int)PaintOffset);
            return Paint.TryCreate(_colr, abs, out paint);
        }
    }

    [OtSubTable(4, GenerateTryCreate = false)]
    [OtField("LayerCount", OtFieldKind.UInt32, 0)]
    [OtUInt32Array("PaintOffset", 4, CountPropertyName = "LayerCount")]
    public readonly partial struct LayerList
    {
        public static bool TryCreate(TableSlice colr, int offset, out LayerList layerList)
        {
            layerList = default;

            if ((uint)offset > (uint)colr.Length - 4)
                return false;

            uint count = BigEndian.ReadUInt32(colr.Span, offset);

            long bytesLong = 4L + (count * 4L);
            if (bytesLong > int.MaxValue)
                return false;

            int bytes = (int)bytesLong;
            if ((uint)offset > (uint)colr.Length - (uint)bytes)
                return false;

            layerList = new LayerList(colr, offset);
            return true;
        }

        public bool TryGetPaint(int index, out Paint paint)
        {
            paint = default;

            if (!TryGetPaintOffset(index, out uint rel))
                return false;

            if (rel > int.MaxValue)
                return false;

            int abs = checked(_offset + (int)rel);
            return Paint.TryCreate(_table, abs, out paint);
        }
    }

    [OtSubTable(1, GenerateTryCreate = false, GenerateStorage = false)]
    [OtField("Format", OtFieldKind.Byte, 0)]
    [OtDiscriminant(nameof(Format))]
    [OtCase(1, typeof(Paint.PaintColrLayersTable))]
    [OtCase(2, typeof(Paint.PaintSolidTable))]
    [OtCase(3, typeof(Paint.PaintVarSolidTable))]
    [OtCase(4, typeof(Paint.PaintLinearGradientTable))]
    [OtCase(5, typeof(Paint.PaintVarLinearGradientTable))]
    [OtCase(6, typeof(Paint.PaintRadialGradientTable))]
    [OtCase(7, typeof(Paint.PaintVarRadialGradientTable))]
    [OtCase(8, typeof(Paint.PaintSweepGradientTable))]
    [OtCase(9, typeof(Paint.PaintVarSweepGradientTable))]
    [OtCase(10, typeof(Paint.PaintGlyphTable))]
    [OtCase(11, typeof(Paint.PaintColrGlyphTable))]
    [OtCase(12, typeof(Paint.PaintTransformTable))]
    [OtCase(13, typeof(Paint.PaintVarTransformTable))]
    [OtCase(14, typeof(Paint.PaintTranslateTable))]
    [OtCase(15, typeof(Paint.PaintVarTranslateTable))]
    [OtCase(16, typeof(Paint.PaintScaleTable))]
    [OtCase(17, typeof(Paint.PaintVarScaleTable))]
    [OtCase(18, typeof(Paint.PaintScaleAroundCenterTable))]
    [OtCase(19, typeof(Paint.PaintVarScaleAroundCenterTable))]
    [OtCase(20, typeof(Paint.PaintScaleUniformTable))]
    [OtCase(21, typeof(Paint.PaintVarScaleUniformTable))]
    [OtCase(22, typeof(Paint.PaintScaleUniformAroundCenterTable))]
    [OtCase(23, typeof(Paint.PaintVarScaleUniformAroundCenterTable))]
    [OtCase(24, typeof(Paint.PaintRotateTable))]
    [OtCase(25, typeof(Paint.PaintVarRotateTable))]
    [OtCase(26, typeof(Paint.PaintRotateAroundCenterTable))]
    [OtCase(27, typeof(Paint.PaintVarRotateAroundCenterTable))]
    [OtCase(28, typeof(Paint.PaintSkewTable))]
    [OtCase(29, typeof(Paint.PaintVarSkewTable))]
    [OtCase(30, typeof(Paint.PaintSkewAroundCenterTable))]
    [OtCase(31, typeof(Paint.PaintVarSkewAroundCenterTable))]
    [OtCase(32, typeof(Paint.PaintCompositeTable))]
    public readonly partial struct Paint
    {
        private readonly TableSlice _table;
        private readonly int _offset;

        private Paint(TableSlice colr, int offset)
        {
            _table = colr;
            _offset = offset;
        }

        public static bool TryCreate(TableSlice colr, int offset, out Paint paint)
        {
            paint = default;

            if ((uint)offset >= (uint)colr.Length)
                return false;

            paint = new Paint(colr, offset);
            return true;
        }

        public bool TryGetPaintColrLayers(out PaintColrLayers layers)
        {
            layers = default;

            if (!TryGetPaintColrLayersTable(out var t))
                return false;

            layers = new PaintColrLayers(_table, t.NumLayers, t.FirstLayerIndex);
            return true;
        }

        public bool TryGetPaintSolid(out PaintSolid solid)
        {
            solid = default;

            if (!TryGetPaintSolidTable(out var t))
                return false;

            solid = new PaintSolid(t.PaletteIndex, t.Alpha);
            return true;
        }

        public bool TryGetPaintVarSolid(out PaintVarSolid solid)
        {
            solid = default;

            if (!TryGetPaintVarSolidTable(out var t))
                return false;

            solid = new PaintVarSolid(t.PaletteIndex, t.Alpha, t.VarIndexBase);
            return true;
        }

        public bool TryGetPaintLinearGradient(out PaintLinearGradient gradient)
        {
            gradient = default;

            if (!TryGetPaintLinearGradientTable(out var t))
                return false;

            gradient = new PaintLinearGradient(_table, _offset, (int)t.ColorLineOffset, t.X0, t.Y0, t.X1, t.Y1, t.X2, t.Y2);
            return true;
        }

        public bool TryGetPaintVarLinearGradient(out PaintVarLinearGradient gradient)
        {
            gradient = default;

            if (!TryGetPaintVarLinearGradientTable(out var t))
                return false;

            gradient = new PaintVarLinearGradient(_table, _offset, (int)t.ColorLineOffset, t.X0, t.Y0, t.X1, t.Y1, t.X2, t.Y2, t.VarIndexBase);
            return true;
        }

        public bool TryGetPaintRadialGradient(out PaintRadialGradient gradient)
        {
            gradient = default;

            if (!TryGetPaintRadialGradientTable(out var t))
                return false;

            gradient = new PaintRadialGradient(_table, _offset, (int)t.ColorLineOffset, t.X0, t.Y0, t.Radius0, t.X1, t.Y1, t.Radius1);
            return true;
        }

        public bool TryGetPaintVarRadialGradient(out PaintVarRadialGradient gradient)
        {
            gradient = default;

            if (!TryGetPaintVarRadialGradientTable(out var t))
                return false;

            gradient = new PaintVarRadialGradient(_table, _offset, (int)t.ColorLineOffset, t.X0, t.Y0, t.Radius0, t.X1, t.Y1, t.Radius1, t.VarIndexBase);
            return true;
        }

        public bool TryGetPaintSweepGradient(out PaintSweepGradient gradient)
        {
            gradient = default;

            if (!TryGetPaintSweepGradientTable(out var t))
                return false;

            gradient = new PaintSweepGradient(_table, _offset, (int)t.ColorLineOffset, t.CenterX, t.CenterY, t.StartAngle, t.EndAngle);
            return true;
        }

        public bool TryGetPaintVarSweepGradient(out PaintVarSweepGradient gradient)
        {
            gradient = default;

            if (!TryGetPaintVarSweepGradientTable(out var t))
                return false;

            gradient = new PaintVarSweepGradient(_table, _offset, (int)t.ColorLineOffset, t.CenterX, t.CenterY, t.StartAngle, t.EndAngle, t.VarIndexBase);
            return true;
        }

        public bool TryGetPaintGlyph(out PaintGlyph glyph)
        {
            glyph = default;

            if (!TryGetPaintGlyphTable(out var t))
                return false;

            glyph = new PaintGlyph(_table, _offset, (int)t.PaintOffset, t.GlyphId);
            return true;
        }

        public bool TryGetPaintColrGlyph(out PaintColrGlyph glyph)
        {
            glyph = default;

            if (!TryGetPaintColrGlyphTable(out var t))
                return false;

            glyph = new PaintColrGlyph(t.GlyphId);
            return true;
        }

        public bool TryGetPaintTransform(out PaintTransform transform)
        {
            transform = default;

            if (!TryGetPaintTransformTable(out var t))
                return false;

            transform = new PaintTransform(_table, _offset, (int)t.PaintOffset, (int)t.TransformOffset);
            return true;
        }

        public bool TryGetPaintVarTransform(out PaintVarTransform transform)
        {
            transform = default;

            if (!TryGetPaintVarTransformTable(out var t))
                return false;

            transform = new PaintVarTransform(_table, _offset, (int)t.PaintOffset, (int)t.TransformOffset);
            return true;
        }

        public bool TryGetPaintTranslate(out PaintTranslate translate)
        {
            translate = default;

            if (!TryGetPaintTranslateTable(out var t))
                return false;

            translate = new PaintTranslate(_table, _offset, (int)t.PaintOffset, t.Dx, t.Dy);
            return true;
        }

        public bool TryGetPaintVarTranslate(out PaintVarTranslate translate)
        {
            translate = default;

            if (!TryGetPaintVarTranslateTable(out var t))
                return false;

            translate = new PaintVarTranslate(_table, _offset, (int)t.PaintOffset, t.Dx, t.Dy, t.VarIndexBase);
            return true;
        }

        public bool TryGetPaintScale(out PaintScale scale)
        {
            scale = default;

            if (!TryGetPaintScaleTable(out var t))
                return false;

            scale = new PaintScale(_table, _offset, (int)t.PaintOffset, t.ScaleX, t.ScaleY);
            return true;
        }

        public bool TryGetPaintVarScale(out PaintVarScale scale)
        {
            scale = default;

            if (!TryGetPaintVarScaleTable(out var t))
                return false;

            scale = new PaintVarScale(_table, _offset, (int)t.PaintOffset, t.ScaleX, t.ScaleY, t.VarIndexBase);
            return true;
        }

        public bool TryGetPaintScaleAroundCenter(out PaintScaleAroundCenter scale)
        {
            scale = default;

            if (!TryGetPaintScaleAroundCenterTable(out var t))
                return false;

            scale = new PaintScaleAroundCenter(_table, _offset, (int)t.PaintOffset, t.ScaleX, t.ScaleY, t.CenterX, t.CenterY);
            return true;
        }

        public bool TryGetPaintVarScaleAroundCenter(out PaintVarScaleAroundCenter scale)
        {
            scale = default;

            if (!TryGetPaintVarScaleAroundCenterTable(out var t))
                return false;

            scale = new PaintVarScaleAroundCenter(_table, _offset, (int)t.PaintOffset, t.ScaleX, t.ScaleY, t.CenterX, t.CenterY, t.VarIndexBase);
            return true;
        }

        public bool TryGetPaintScaleUniform(out PaintScaleUniform scale)
        {
            scale = default;

            if (!TryGetPaintScaleUniformTable(out var t))
                return false;

            scale = new PaintScaleUniform(_table, _offset, (int)t.PaintOffset, t.Scale);
            return true;
        }

        public bool TryGetPaintVarScaleUniform(out PaintVarScaleUniform scale)
        {
            scale = default;

            if (!TryGetPaintVarScaleUniformTable(out var t))
                return false;

            scale = new PaintVarScaleUniform(_table, _offset, (int)t.PaintOffset, t.Scale, t.VarIndexBase);
            return true;
        }

        public bool TryGetPaintScaleUniformAroundCenter(out PaintScaleUniformAroundCenter scale)
        {
            scale = default;

            if (!TryGetPaintScaleUniformAroundCenterTable(out var t))
                return false;

            scale = new PaintScaleUniformAroundCenter(_table, _offset, (int)t.PaintOffset, t.Scale, t.CenterX, t.CenterY);
            return true;
        }

        public bool TryGetPaintVarScaleUniformAroundCenter(out PaintVarScaleUniformAroundCenter scale)
        {
            scale = default;

            if (!TryGetPaintVarScaleUniformAroundCenterTable(out var t))
                return false;

            scale = new PaintVarScaleUniformAroundCenter(_table, _offset, (int)t.PaintOffset, t.Scale, t.CenterX, t.CenterY, t.VarIndexBase);
            return true;
        }

        public bool TryGetPaintRotate(out PaintRotate rotate)
        {
            rotate = default;

            if (!TryGetPaintRotateTable(out var t))
                return false;

            rotate = new PaintRotate(_table, _offset, (int)t.PaintOffset, t.Angle);
            return true;
        }

        public bool TryGetPaintVarRotate(out PaintVarRotate rotate)
        {
            rotate = default;

            if (!TryGetPaintVarRotateTable(out var t))
                return false;

            rotate = new PaintVarRotate(_table, _offset, (int)t.PaintOffset, t.Angle, t.VarIndexBase);
            return true;
        }

        public bool TryGetPaintRotateAroundCenter(out PaintRotateAroundCenter rotate)
        {
            rotate = default;

            if (!TryGetPaintRotateAroundCenterTable(out var t))
                return false;

            rotate = new PaintRotateAroundCenter(_table, _offset, (int)t.PaintOffset, t.Angle, t.CenterX, t.CenterY);
            return true;
        }

        public bool TryGetPaintVarRotateAroundCenter(out PaintVarRotateAroundCenter rotate)
        {
            rotate = default;

            if (!TryGetPaintVarRotateAroundCenterTable(out var t))
                return false;

            rotate = new PaintVarRotateAroundCenter(_table, _offset, (int)t.PaintOffset, t.Angle, t.CenterX, t.CenterY, t.VarIndexBase);
            return true;
        }

        public bool TryGetPaintSkew(out PaintSkew skew)
        {
            skew = default;

            if (!TryGetPaintSkewTable(out var t))
                return false;

            skew = new PaintSkew(_table, _offset, (int)t.PaintOffset, t.XSkewAngle, t.YSkewAngle);
            return true;
        }

        public bool TryGetPaintVarSkew(out PaintVarSkew skew)
        {
            skew = default;

            if (!TryGetPaintVarSkewTable(out var t))
                return false;

            skew = new PaintVarSkew(_table, _offset, (int)t.PaintOffset, t.XSkewAngle, t.YSkewAngle, t.VarIndexBase);
            return true;
        }

        public bool TryGetPaintSkewAroundCenter(out PaintSkewAroundCenter skew)
        {
            skew = default;

            if (!TryGetPaintSkewAroundCenterTable(out var t))
                return false;

            skew = new PaintSkewAroundCenter(_table, _offset, (int)t.PaintOffset, t.XSkewAngle, t.YSkewAngle, t.CenterX, t.CenterY);
            return true;
        }

        public bool TryGetPaintVarSkewAroundCenter(out PaintVarSkewAroundCenter skew)
        {
            skew = default;

            if (!TryGetPaintVarSkewAroundCenterTable(out var t))
                return false;

            skew = new PaintVarSkewAroundCenter(_table, _offset, (int)t.PaintOffset, t.XSkewAngle, t.YSkewAngle, t.CenterX, t.CenterY, t.VarIndexBase);
            return true;
        }

        public bool TryGetPaintComposite(out PaintComposite composite)
        {
            composite = default;

            if (!TryGetPaintCompositeTable(out var t))
                return false;

            composite = new PaintComposite(_table, _offset, (int)t.SourcePaintOffset, t.CompositeMode, (int)t.BackdropPaintOffset);
            return true;
        }

        [OtSubTable(6)]
        [OtField("NumLayers", OtFieldKind.Byte, 1)]
        [OtField("FirstLayerIndex", OtFieldKind.UInt32, 2)]
        public readonly partial struct PaintColrLayersTable
        {
        }

        [OtSubTable(5)]
        [OtField("PaletteIndex", OtFieldKind.UInt16, 1)]
        [OtField("AlphaRaw", OtFieldKind.Int16, 3)]
        public readonly partial struct PaintSolidTable
        {
            public F2Dot14 Alpha => new(AlphaRaw);
        }

        [OtSubTable(9)]
        [OtField("PaletteIndex", OtFieldKind.UInt16, 1)]
        [OtField("AlphaRaw", OtFieldKind.Int16, 3)]
        [OtField("VarIndexBase", OtFieldKind.UInt32, 5)]
        public readonly partial struct PaintVarSolidTable
        {
            public F2Dot14 Alpha => new(AlphaRaw);
        }

        [OtSubTable(16)]
        [OtField("ColorLineOffset", OtFieldKind.UInt24, 1)]
        [OtField("X0", OtFieldKind.Int16, 4)]
        [OtField("Y0", OtFieldKind.Int16, 6)]
        [OtField("X1", OtFieldKind.Int16, 8)]
        [OtField("Y1", OtFieldKind.Int16, 10)]
        [OtField("X2", OtFieldKind.Int16, 12)]
        [OtField("Y2", OtFieldKind.Int16, 14)]
        public readonly partial struct PaintLinearGradientTable
        {
        }

        [OtSubTable(20)]
        [OtField("ColorLineOffset", OtFieldKind.UInt24, 1)]
        [OtField("X0", OtFieldKind.Int16, 4)]
        [OtField("Y0", OtFieldKind.Int16, 6)]
        [OtField("X1", OtFieldKind.Int16, 8)]
        [OtField("Y1", OtFieldKind.Int16, 10)]
        [OtField("X2", OtFieldKind.Int16, 12)]
        [OtField("Y2", OtFieldKind.Int16, 14)]
        [OtField("VarIndexBase", OtFieldKind.UInt32, 16)]
        public readonly partial struct PaintVarLinearGradientTable
        {
        }

        [OtSubTable(16)]
        [OtField("ColorLineOffset", OtFieldKind.UInt24, 1)]
        [OtField("X0", OtFieldKind.Int16, 4)]
        [OtField("Y0", OtFieldKind.Int16, 6)]
        [OtField("Radius0", OtFieldKind.UInt16, 8)]
        [OtField("X1", OtFieldKind.Int16, 10)]
        [OtField("Y1", OtFieldKind.Int16, 12)]
        [OtField("Radius1", OtFieldKind.UInt16, 14)]
        public readonly partial struct PaintRadialGradientTable
        {
        }

        [OtSubTable(20)]
        [OtField("ColorLineOffset", OtFieldKind.UInt24, 1)]
        [OtField("X0", OtFieldKind.Int16, 4)]
        [OtField("Y0", OtFieldKind.Int16, 6)]
        [OtField("Radius0", OtFieldKind.UInt16, 8)]
        [OtField("X1", OtFieldKind.Int16, 10)]
        [OtField("Y1", OtFieldKind.Int16, 12)]
        [OtField("Radius1", OtFieldKind.UInt16, 14)]
        [OtField("VarIndexBase", OtFieldKind.UInt32, 16)]
        public readonly partial struct PaintVarRadialGradientTable
        {
        }

        [OtSubTable(12)]
        [OtField("ColorLineOffset", OtFieldKind.UInt24, 1)]
        [OtField("CenterX", OtFieldKind.Int16, 4)]
        [OtField("CenterY", OtFieldKind.Int16, 6)]
        [OtField("StartAngleRaw", OtFieldKind.Int16, 8)]
        [OtField("EndAngleRaw", OtFieldKind.Int16, 10)]
        public readonly partial struct PaintSweepGradientTable
        {
            public F2Dot14 StartAngle => new(StartAngleRaw);
            public F2Dot14 EndAngle => new(EndAngleRaw);
        }

        [OtSubTable(16)]
        [OtField("ColorLineOffset", OtFieldKind.UInt24, 1)]
        [OtField("CenterX", OtFieldKind.Int16, 4)]
        [OtField("CenterY", OtFieldKind.Int16, 6)]
        [OtField("StartAngleRaw", OtFieldKind.Int16, 8)]
        [OtField("EndAngleRaw", OtFieldKind.Int16, 10)]
        [OtField("VarIndexBase", OtFieldKind.UInt32, 12)]
        public readonly partial struct PaintVarSweepGradientTable
        {
            public F2Dot14 StartAngle => new(StartAngleRaw);
            public F2Dot14 EndAngle => new(EndAngleRaw);
        }

        [OtSubTable(6)]
        [OtField("PaintOffset", OtFieldKind.UInt24, 1)]
        [OtField("GlyphId", OtFieldKind.UInt16, 4)]
        public readonly partial struct PaintGlyphTable
        {
        }

        [OtSubTable(3)]
        [OtField("GlyphId", OtFieldKind.UInt16, 1)]
        public readonly partial struct PaintColrGlyphTable
        {
        }

        [OtSubTable(7)]
        [OtField("PaintOffset", OtFieldKind.UInt24, 1)]
        [OtField("TransformOffset", OtFieldKind.UInt24, 4)]
        public readonly partial struct PaintTransformTable
        {
        }

        [OtSubTable(7)]
        [OtField("PaintOffset", OtFieldKind.UInt24, 1)]
        [OtField("TransformOffset", OtFieldKind.UInt24, 4)]
        public readonly partial struct PaintVarTransformTable
        {
        }

        [OtSubTable(8)]
        [OtField("PaintOffset", OtFieldKind.UInt24, 1)]
        [OtField("Dx", OtFieldKind.Int16, 4)]
        [OtField("Dy", OtFieldKind.Int16, 6)]
        public readonly partial struct PaintTranslateTable
        {
        }

        [OtSubTable(12)]
        [OtField("PaintOffset", OtFieldKind.UInt24, 1)]
        [OtField("Dx", OtFieldKind.Int16, 4)]
        [OtField("Dy", OtFieldKind.Int16, 6)]
        [OtField("VarIndexBase", OtFieldKind.UInt32, 8)]
        public readonly partial struct PaintVarTranslateTable
        {
        }

        [OtSubTable(8)]
        [OtField("PaintOffset", OtFieldKind.UInt24, 1)]
        [OtField("ScaleXRaw", OtFieldKind.Int16, 4)]
        [OtField("ScaleYRaw", OtFieldKind.Int16, 6)]
        public readonly partial struct PaintScaleTable
        {
            public F2Dot14 ScaleX => new(ScaleXRaw);
            public F2Dot14 ScaleY => new(ScaleYRaw);
        }

        [OtSubTable(12)]
        [OtField("PaintOffset", OtFieldKind.UInt24, 1)]
        [OtField("ScaleXRaw", OtFieldKind.Int16, 4)]
        [OtField("ScaleYRaw", OtFieldKind.Int16, 6)]
        [OtField("VarIndexBase", OtFieldKind.UInt32, 8)]
        public readonly partial struct PaintVarScaleTable
        {
            public F2Dot14 ScaleX => new(ScaleXRaw);
            public F2Dot14 ScaleY => new(ScaleYRaw);
        }

        [OtSubTable(12)]
        [OtField("PaintOffset", OtFieldKind.UInt24, 1)]
        [OtField("ScaleXRaw", OtFieldKind.Int16, 4)]
        [OtField("ScaleYRaw", OtFieldKind.Int16, 6)]
        [OtField("CenterX", OtFieldKind.Int16, 8)]
        [OtField("CenterY", OtFieldKind.Int16, 10)]
        public readonly partial struct PaintScaleAroundCenterTable
        {
            public F2Dot14 ScaleX => new(ScaleXRaw);
            public F2Dot14 ScaleY => new(ScaleYRaw);
        }

        [OtSubTable(16)]
        [OtField("PaintOffset", OtFieldKind.UInt24, 1)]
        [OtField("ScaleXRaw", OtFieldKind.Int16, 4)]
        [OtField("ScaleYRaw", OtFieldKind.Int16, 6)]
        [OtField("CenterX", OtFieldKind.Int16, 8)]
        [OtField("CenterY", OtFieldKind.Int16, 10)]
        [OtField("VarIndexBase", OtFieldKind.UInt32, 12)]
        public readonly partial struct PaintVarScaleAroundCenterTable
        {
            public F2Dot14 ScaleX => new(ScaleXRaw);
            public F2Dot14 ScaleY => new(ScaleYRaw);
        }

        [OtSubTable(6)]
        [OtField("PaintOffset", OtFieldKind.UInt24, 1)]
        [OtField("ScaleRaw", OtFieldKind.Int16, 4)]
        public readonly partial struct PaintScaleUniformTable
        {
            public F2Dot14 Scale => new(ScaleRaw);
        }

        [OtSubTable(10)]
        [OtField("PaintOffset", OtFieldKind.UInt24, 1)]
        [OtField("ScaleRaw", OtFieldKind.Int16, 4)]
        [OtField("VarIndexBase", OtFieldKind.UInt32, 6)]
        public readonly partial struct PaintVarScaleUniformTable
        {
            public F2Dot14 Scale => new(ScaleRaw);
        }

        [OtSubTable(10)]
        [OtField("PaintOffset", OtFieldKind.UInt24, 1)]
        [OtField("ScaleRaw", OtFieldKind.Int16, 4)]
        [OtField("CenterX", OtFieldKind.Int16, 6)]
        [OtField("CenterY", OtFieldKind.Int16, 8)]
        public readonly partial struct PaintScaleUniformAroundCenterTable
        {
            public F2Dot14 Scale => new(ScaleRaw);
        }

        [OtSubTable(14)]
        [OtField("PaintOffset", OtFieldKind.UInt24, 1)]
        [OtField("ScaleRaw", OtFieldKind.Int16, 4)]
        [OtField("CenterX", OtFieldKind.Int16, 6)]
        [OtField("CenterY", OtFieldKind.Int16, 8)]
        [OtField("VarIndexBase", OtFieldKind.UInt32, 10)]
        public readonly partial struct PaintVarScaleUniformAroundCenterTable
        {
            public F2Dot14 Scale => new(ScaleRaw);
        }

        [OtSubTable(6)]
        [OtField("PaintOffset", OtFieldKind.UInt24, 1)]
        [OtField("AngleRaw", OtFieldKind.Int16, 4)]
        public readonly partial struct PaintRotateTable
        {
            public F2Dot14 Angle => new(AngleRaw);
        }

        [OtSubTable(10)]
        [OtField("PaintOffset", OtFieldKind.UInt24, 1)]
        [OtField("AngleRaw", OtFieldKind.Int16, 4)]
        [OtField("VarIndexBase", OtFieldKind.UInt32, 6)]
        public readonly partial struct PaintVarRotateTable
        {
            public F2Dot14 Angle => new(AngleRaw);
        }

        [OtSubTable(10)]
        [OtField("PaintOffset", OtFieldKind.UInt24, 1)]
        [OtField("AngleRaw", OtFieldKind.Int16, 4)]
        [OtField("CenterX", OtFieldKind.Int16, 6)]
        [OtField("CenterY", OtFieldKind.Int16, 8)]
        public readonly partial struct PaintRotateAroundCenterTable
        {
            public F2Dot14 Angle => new(AngleRaw);
        }

        [OtSubTable(14)]
        [OtField("PaintOffset", OtFieldKind.UInt24, 1)]
        [OtField("AngleRaw", OtFieldKind.Int16, 4)]
        [OtField("CenterX", OtFieldKind.Int16, 6)]
        [OtField("CenterY", OtFieldKind.Int16, 8)]
        [OtField("VarIndexBase", OtFieldKind.UInt32, 10)]
        public readonly partial struct PaintVarRotateAroundCenterTable
        {
            public F2Dot14 Angle => new(AngleRaw);
        }

        [OtSubTable(8)]
        [OtField("PaintOffset", OtFieldKind.UInt24, 1)]
        [OtField("XSkewAngleRaw", OtFieldKind.Int16, 4)]
        [OtField("YSkewAngleRaw", OtFieldKind.Int16, 6)]
        public readonly partial struct PaintSkewTable
        {
            public F2Dot14 XSkewAngle => new(XSkewAngleRaw);
            public F2Dot14 YSkewAngle => new(YSkewAngleRaw);
        }

        [OtSubTable(12)]
        [OtField("PaintOffset", OtFieldKind.UInt24, 1)]
        [OtField("XSkewAngleRaw", OtFieldKind.Int16, 4)]
        [OtField("YSkewAngleRaw", OtFieldKind.Int16, 6)]
        [OtField("VarIndexBase", OtFieldKind.UInt32, 8)]
        public readonly partial struct PaintVarSkewTable
        {
            public F2Dot14 XSkewAngle => new(XSkewAngleRaw);
            public F2Dot14 YSkewAngle => new(YSkewAngleRaw);
        }

        [OtSubTable(12)]
        [OtField("PaintOffset", OtFieldKind.UInt24, 1)]
        [OtField("XSkewAngleRaw", OtFieldKind.Int16, 4)]
        [OtField("YSkewAngleRaw", OtFieldKind.Int16, 6)]
        [OtField("CenterX", OtFieldKind.Int16, 8)]
        [OtField("CenterY", OtFieldKind.Int16, 10)]
        public readonly partial struct PaintSkewAroundCenterTable
        {
            public F2Dot14 XSkewAngle => new(XSkewAngleRaw);
            public F2Dot14 YSkewAngle => new(YSkewAngleRaw);
        }

        [OtSubTable(16)]
        [OtField("PaintOffset", OtFieldKind.UInt24, 1)]
        [OtField("XSkewAngleRaw", OtFieldKind.Int16, 4)]
        [OtField("YSkewAngleRaw", OtFieldKind.Int16, 6)]
        [OtField("CenterX", OtFieldKind.Int16, 8)]
        [OtField("CenterY", OtFieldKind.Int16, 10)]
        [OtField("VarIndexBase", OtFieldKind.UInt32, 12)]
        public readonly partial struct PaintVarSkewAroundCenterTable
        {
            public F2Dot14 XSkewAngle => new(XSkewAngleRaw);
            public F2Dot14 YSkewAngle => new(YSkewAngleRaw);
        }

        [OtSubTable(8)]
        [OtField("SourcePaintOffset", OtFieldKind.UInt24, 1)]
        [OtField("CompositeMode", OtFieldKind.Byte, 4)]
        [OtField("BackdropPaintOffset", OtFieldKind.UInt24, 5)]
        public readonly partial struct PaintCompositeTable
        {
        }

    }

    public readonly struct PaintColrLayers
    {
        private readonly TableSlice _colr;

        public byte NumLayers { get; }
        public uint FirstLayerIndex { get; }

        internal PaintColrLayers(TableSlice colr, byte numLayers, uint firstLayerIndex)
        {
            _colr = colr;
            NumLayers = numLayers;
            FirstLayerIndex = firstLayerIndex;
        }

        public bool TryGetLayerPaint(int layerListOffset, int layerIndex, out Paint paint)
        {
            paint = default;

            if ((uint)layerIndex >= NumLayers)
                return false;

            if (!LayerList.TryCreate(_colr, layerListOffset, out var list))
                return false;

            uint index = FirstLayerIndex + (uint)layerIndex;
            if (index > int.MaxValue)
                return false;

            return list.TryGetPaint((int)index, out paint);
        }
    }

    public readonly struct PaintSolid
    {
        public ushort PaletteIndex { get; }
        public F2Dot14 Alpha { get; }

        public PaintSolid(ushort paletteIndex, F2Dot14 alpha)
        {
            PaletteIndex = paletteIndex;
            Alpha = alpha;
        }
    }

    public readonly struct PaintVarSolid
    {
        public ushort PaletteIndex { get; }
        public F2Dot14 Alpha { get; }
        public uint VarIndexBase { get; }

        public PaintVarSolid(ushort paletteIndex, F2Dot14 alpha, uint varIndexBase)
        {
            PaletteIndex = paletteIndex;
            Alpha = alpha;
            VarIndexBase = varIndexBase;
        }
    }

    public readonly struct PaintGlyph
    {
        private readonly TableSlice _colr;
        private readonly int _offset;

        public int PaintOffset { get; }
        public ushort GlyphId { get; }

        internal PaintGlyph(TableSlice colr, int offset, int paintOffset, ushort glyphId)
        {
            _colr = colr;
            _offset = offset;
            PaintOffset = paintOffset;
            GlyphId = glyphId;
        }

        public bool TryGetPaint(out Paint paint)
        {
            paint = default;

            int abs = checked(_offset + PaintOffset);
            return Paint.TryCreate(_colr, abs, out paint);
        }
    }

    public readonly struct PaintColrGlyph
    {
        public ushort GlyphId { get; }

        public PaintColrGlyph(ushort glyphId) => GlyphId = glyphId;
    }

    public readonly struct ColorStop
    {
        public F2Dot14 StopOffset { get; }
        public ushort PaletteIndex { get; }
        public F2Dot14 Alpha { get; }

        public ColorStop(F2Dot14 stopOffset, ushort paletteIndex, F2Dot14 alpha)
        {
            StopOffset = stopOffset;
            PaletteIndex = paletteIndex;
            Alpha = alpha;
        }
    }

    public readonly struct VarColorStop
    {
        public F2Dot14 StopOffset { get; }
        public ushort PaletteIndex { get; }
        public F2Dot14 Alpha { get; }
        public uint VarIndexBase { get; }

        public VarColorStop(F2Dot14 stopOffset, ushort paletteIndex, F2Dot14 alpha, uint varIndexBase)
        {
            StopOffset = stopOffset;
            PaletteIndex = paletteIndex;
            Alpha = alpha;
            VarIndexBase = varIndexBase;
        }
    }

    [OtSubTable(3, GenerateTryCreate = false)]
    [OtField("Extend", OtFieldKind.Byte, 0)]
    [OtField("StopCount", OtFieldKind.UInt16, 1)]
    [OtSequentialRecordArray("Stop", 3, 6, CountPropertyName = "StopCount", RecordTypeName = "ColorStop")]
    public readonly partial struct ColorLine
    {
        public static bool TryCreate(TableSlice colr, int offset, out ColorLine line)
        {
            line = default;

            // extend(1) + numStops(2)
            if ((uint)offset > (uint)colr.Length - 3)
                return false;

            ushort numStops = BigEndian.ReadUInt16(colr.Span, offset + 1);

            long stopsBytesLong = (long)numStops * 6L;
            if (stopsBytesLong > int.MaxValue)
                return false;

            int stopsBytes = (int)stopsBytesLong;
            if ((uint)offset > (uint)colr.Length - (uint)(3 + stopsBytes))
                return false;

            line = new ColorLine(colr, offset);
            return true;
        }
    }

    [OtSubTable(3, GenerateTryCreate = false)]
    [OtField("Extend", OtFieldKind.Byte, 0)]
    [OtField("StopCount", OtFieldKind.UInt16, 1)]
    [OtSequentialRecordArray("Stop", 3, 10, CountPropertyName = "StopCount", RecordTypeName = "VarColorStop")]
    public readonly partial struct VarColorLine
    {
        public static bool TryCreate(TableSlice colr, int offset, out VarColorLine line)
        {
            line = default;

            // extend(1) + numStops(2)
            if ((uint)offset > (uint)colr.Length - 3)
                return false;

            ushort numStops = BigEndian.ReadUInt16(colr.Span, offset + 1);

            long stopsBytesLong = (long)numStops * 10L;
            if (stopsBytesLong > int.MaxValue)
                return false;

            int stopsBytes = (int)stopsBytesLong;
            if ((uint)offset > (uint)colr.Length - (uint)(3 + stopsBytes))
                return false;

            line = new VarColorLine(colr, offset);
            return true;
        }
    }

    public readonly struct PaintLinearGradient
    {
        private readonly TableSlice _colr;
        private readonly int _paintOffset;
        public int ColorLineOffset { get; }
        public short X0 { get; }
        public short Y0 { get; }
        public short X1 { get; }
        public short Y1 { get; }
        public short X2 { get; }
        public short Y2 { get; }

        internal PaintLinearGradient(TableSlice colr, int paintOffset, int colorLineOffset, short x0, short y0, short x1, short y1, short x2, short y2)
        {
            _colr = colr;
            _paintOffset = paintOffset;
            ColorLineOffset = colorLineOffset;
            X0 = x0;
            Y0 = y0;
            X1 = x1;
            Y1 = y1;
            X2 = x2;
            Y2 = y2;
        }

        public bool TryGetColorLine(out ColorLine line)
        {
            line = default;
            int abs = checked(_paintOffset + ColorLineOffset);
            return ColorLine.TryCreate(_colr, abs, out line);
        }
    }

    public readonly struct PaintVarLinearGradient
    {
        private readonly TableSlice _colr;
        private readonly int _paintOffset;
        public int ColorLineOffset { get; }
        public short X0 { get; }
        public short Y0 { get; }
        public short X1 { get; }
        public short Y1 { get; }
        public short X2 { get; }
        public short Y2 { get; }
        public uint VarIndexBase { get; }

        internal PaintVarLinearGradient(TableSlice colr, int paintOffset, int colorLineOffset, short x0, short y0, short x1, short y1, short x2, short y2, uint varIndexBase)
        {
            _colr = colr;
            _paintOffset = paintOffset;
            ColorLineOffset = colorLineOffset;
            X0 = x0;
            Y0 = y0;
            X1 = x1;
            Y1 = y1;
            X2 = x2;
            Y2 = y2;
            VarIndexBase = varIndexBase;
        }

        public bool TryGetColorLine(out VarColorLine line)
        {
            line = default;
            int abs = checked(_paintOffset + ColorLineOffset);
            return VarColorLine.TryCreate(_colr, abs, out line);
        }
    }

    public readonly struct PaintRadialGradient
    {
        private readonly TableSlice _colr;
        private readonly int _paintOffset;
        public int ColorLineOffset { get; }
        public short X0 { get; }
        public short Y0 { get; }
        public ushort Radius0 { get; }
        public short X1 { get; }
        public short Y1 { get; }
        public ushort Radius1 { get; }

        internal PaintRadialGradient(TableSlice colr, int paintOffset, int colorLineOffset, short x0, short y0, ushort radius0, short x1, short y1, ushort radius1)
        {
            _colr = colr;
            _paintOffset = paintOffset;
            ColorLineOffset = colorLineOffset;
            X0 = x0;
            Y0 = y0;
            Radius0 = radius0;
            X1 = x1;
            Y1 = y1;
            Radius1 = radius1;
        }

        public bool TryGetColorLine(out ColorLine line)
        {
            line = default;
            int abs = checked(_paintOffset + ColorLineOffset);
            return ColorLine.TryCreate(_colr, abs, out line);
        }
    }

    public readonly struct PaintVarRadialGradient
    {
        private readonly TableSlice _colr;
        private readonly int _paintOffset;
        public int ColorLineOffset { get; }
        public short X0 { get; }
        public short Y0 { get; }
        public ushort Radius0 { get; }
        public short X1 { get; }
        public short Y1 { get; }
        public ushort Radius1 { get; }
        public uint VarIndexBase { get; }

        internal PaintVarRadialGradient(TableSlice colr, int paintOffset, int colorLineOffset, short x0, short y0, ushort radius0, short x1, short y1, ushort radius1, uint varIndexBase)
        {
            _colr = colr;
            _paintOffset = paintOffset;
            ColorLineOffset = colorLineOffset;
            X0 = x0;
            Y0 = y0;
            Radius0 = radius0;
            X1 = x1;
            Y1 = y1;
            Radius1 = radius1;
            VarIndexBase = varIndexBase;
        }

        public bool TryGetColorLine(out VarColorLine line)
        {
            line = default;
            int abs = checked(_paintOffset + ColorLineOffset);
            return VarColorLine.TryCreate(_colr, abs, out line);
        }
    }

    public readonly struct PaintSweepGradient
    {
        private readonly TableSlice _colr;
        private readonly int _paintOffset;
        public int ColorLineOffset { get; }
        public short CenterX { get; }
        public short CenterY { get; }
        public F2Dot14 StartAngle { get; }
        public F2Dot14 EndAngle { get; }

        internal PaintSweepGradient(TableSlice colr, int paintOffset, int colorLineOffset, short centerX, short centerY, F2Dot14 startAngle, F2Dot14 endAngle)
        {
            _colr = colr;
            _paintOffset = paintOffset;
            ColorLineOffset = colorLineOffset;
            CenterX = centerX;
            CenterY = centerY;
            StartAngle = startAngle;
            EndAngle = endAngle;
        }

        public bool TryGetColorLine(out ColorLine line)
        {
            line = default;
            int abs = checked(_paintOffset + ColorLineOffset);
            return ColorLine.TryCreate(_colr, abs, out line);
        }
    }

    public readonly struct PaintVarSweepGradient
    {
        private readonly TableSlice _colr;
        private readonly int _paintOffset;
        public int ColorLineOffset { get; }
        public short CenterX { get; }
        public short CenterY { get; }
        public F2Dot14 StartAngle { get; }
        public F2Dot14 EndAngle { get; }
        public uint VarIndexBase { get; }

        internal PaintVarSweepGradient(TableSlice colr, int paintOffset, int colorLineOffset, short centerX, short centerY, F2Dot14 startAngle, F2Dot14 endAngle, uint varIndexBase)
        {
            _colr = colr;
            _paintOffset = paintOffset;
            ColorLineOffset = colorLineOffset;
            CenterX = centerX;
            CenterY = centerY;
            StartAngle = startAngle;
            EndAngle = endAngle;
            VarIndexBase = varIndexBase;
        }

        public bool TryGetColorLine(out VarColorLine line)
        {
            line = default;
            int abs = checked(_paintOffset + ColorLineOffset);
            return VarColorLine.TryCreate(_colr, abs, out line);
        }
    }

    public readonly struct PaintTransform
    {
        private readonly TableSlice _colr;
        private readonly int _paintOffset;

        public int PaintOffset { get; }
        public int TransformOffset { get; }

        internal PaintTransform(TableSlice colr, int paintOffset, int childOffset, int transformOffset)
        {
            _colr = colr;
            _paintOffset = paintOffset;
            PaintOffset = childOffset;
            TransformOffset = transformOffset;
        }

        public bool TryGetPaint(out Paint paint)
        {
            paint = default;
            int abs = checked(_paintOffset + PaintOffset);
            return Paint.TryCreate(_colr, abs, out paint);
        }

        public bool TryGetTransform(out Affine2x3 transform)
        {
            transform = default;

            if (_colr.Length < 24)
                return false;

            int abs = checked(_paintOffset + TransformOffset);
            if ((uint)abs > (uint)_colr.Length - 24)
                return false;

            var data = _colr.Span;
            transform = new Affine2x3(
                xx: new Fixed1616(BigEndian.ReadUInt32(data, abs + 0)),
                yx: new Fixed1616(BigEndian.ReadUInt32(data, abs + 4)),
                xy: new Fixed1616(BigEndian.ReadUInt32(data, abs + 8)),
                yy: new Fixed1616(BigEndian.ReadUInt32(data, abs + 12)),
                dx: new Fixed1616(BigEndian.ReadUInt32(data, abs + 16)),
                dy: new Fixed1616(BigEndian.ReadUInt32(data, abs + 20)));
            return true;
        }
    }

    public readonly struct PaintVarTransform
    {
        private readonly TableSlice _colr;
        private readonly int _paintOffset;

        public int PaintOffset { get; }
        public int TransformOffset { get; }

        internal PaintVarTransform(TableSlice colr, int paintOffset, int childOffset, int transformOffset)
        {
            _colr = colr;
            _paintOffset = paintOffset;
            PaintOffset = childOffset;
            TransformOffset = transformOffset;
        }

        public bool TryGetPaint(out Paint paint)
        {
            paint = default;
            int abs = checked(_paintOffset + PaintOffset);
            return Paint.TryCreate(_colr, abs, out paint);
        }

        public bool TryGetTransform(out Affine2x3 transform, out uint varIndexBase)
        {
            transform = default;
            varIndexBase = 0;

            if (_colr.Length < 28)
                return false;

            int abs = checked(_paintOffset + TransformOffset);
            if ((uint)abs > (uint)_colr.Length - 28)
                return false;

            var data = _colr.Span;
            transform = new Affine2x3(
                xx: new Fixed1616(BigEndian.ReadUInt32(data, abs + 0)),
                yx: new Fixed1616(BigEndian.ReadUInt32(data, abs + 4)),
                xy: new Fixed1616(BigEndian.ReadUInt32(data, abs + 8)),
                yy: new Fixed1616(BigEndian.ReadUInt32(data, abs + 12)),
                dx: new Fixed1616(BigEndian.ReadUInt32(data, abs + 16)),
                dy: new Fixed1616(BigEndian.ReadUInt32(data, abs + 20)));
            varIndexBase = BigEndian.ReadUInt32(data, abs + 24);
            return true;
        }
    }

    public readonly struct PaintTranslate
    {
        private readonly TableSlice _colr;
        private readonly int _paintOffset;

        public int PaintOffset { get; }
        public short Dx { get; }
        public short Dy { get; }

        internal PaintTranslate(TableSlice colr, int paintOffset, int childOffset, short dx, short dy)
        {
            _colr = colr;
            _paintOffset = paintOffset;
            PaintOffset = childOffset;
            Dx = dx;
            Dy = dy;
        }

        public bool TryGetPaint(out Paint paint)
        {
            paint = default;
            int abs = checked(_paintOffset + PaintOffset);
            return Paint.TryCreate(_colr, abs, out paint);
        }
    }

    public readonly struct PaintVarTranslate
    {
        private readonly TableSlice _colr;
        private readonly int _paintOffset;

        public int PaintOffset { get; }
        public short Dx { get; }
        public short Dy { get; }
        public uint VarIndexBase { get; }

        internal PaintVarTranslate(TableSlice colr, int paintOffset, int childOffset, short dx, short dy, uint varIndexBase)
        {
            _colr = colr;
            _paintOffset = paintOffset;
            PaintOffset = childOffset;
            Dx = dx;
            Dy = dy;
            VarIndexBase = varIndexBase;
        }

        public bool TryGetPaint(out Paint paint)
        {
            paint = default;
            int abs = checked(_paintOffset + PaintOffset);
            return Paint.TryCreate(_colr, abs, out paint);
        }
    }

    public readonly struct PaintScale
    {
        private readonly TableSlice _colr;
        private readonly int _paintOffset;

        public int PaintOffset { get; }
        public F2Dot14 ScaleX { get; }
        public F2Dot14 ScaleY { get; }

        internal PaintScale(TableSlice colr, int paintOffset, int childOffset, F2Dot14 scaleX, F2Dot14 scaleY)
        {
            _colr = colr;
            _paintOffset = paintOffset;
            PaintOffset = childOffset;
            ScaleX = scaleX;
            ScaleY = scaleY;
        }

        public bool TryGetPaint(out Paint paint)
        {
            paint = default;
            int abs = checked(_paintOffset + PaintOffset);
            return Paint.TryCreate(_colr, abs, out paint);
        }
    }

    public readonly struct PaintVarScale
    {
        private readonly TableSlice _colr;
        private readonly int _paintOffset;

        public int PaintOffset { get; }
        public F2Dot14 ScaleX { get; }
        public F2Dot14 ScaleY { get; }
        public uint VarIndexBase { get; }

        internal PaintVarScale(TableSlice colr, int paintOffset, int childOffset, F2Dot14 scaleX, F2Dot14 scaleY, uint varIndexBase)
        {
            _colr = colr;
            _paintOffset = paintOffset;
            PaintOffset = childOffset;
            ScaleX = scaleX;
            ScaleY = scaleY;
            VarIndexBase = varIndexBase;
        }

        public bool TryGetPaint(out Paint paint)
        {
            paint = default;
            int abs = checked(_paintOffset + PaintOffset);
            return Paint.TryCreate(_colr, abs, out paint);
        }
    }

    public readonly struct PaintScaleAroundCenter
    {
        private readonly TableSlice _colr;
        private readonly int _paintOffset;

        public int PaintOffset { get; }
        public F2Dot14 ScaleX { get; }
        public F2Dot14 ScaleY { get; }
        public short CenterX { get; }
        public short CenterY { get; }

        internal PaintScaleAroundCenter(TableSlice colr, int paintOffset, int childOffset, F2Dot14 scaleX, F2Dot14 scaleY, short centerX, short centerY)
        {
            _colr = colr;
            _paintOffset = paintOffset;
            PaintOffset = childOffset;
            ScaleX = scaleX;
            ScaleY = scaleY;
            CenterX = centerX;
            CenterY = centerY;
        }

        public bool TryGetPaint(out Paint paint)
        {
            paint = default;
            int abs = checked(_paintOffset + PaintOffset);
            return Paint.TryCreate(_colr, abs, out paint);
        }
    }

    public readonly struct PaintVarScaleAroundCenter
    {
        private readonly TableSlice _colr;
        private readonly int _paintOffset;

        public int PaintOffset { get; }
        public F2Dot14 ScaleX { get; }
        public F2Dot14 ScaleY { get; }
        public short CenterX { get; }
        public short CenterY { get; }
        public uint VarIndexBase { get; }

        internal PaintVarScaleAroundCenter(TableSlice colr, int paintOffset, int childOffset, F2Dot14 scaleX, F2Dot14 scaleY, short centerX, short centerY, uint varIndexBase)
        {
            _colr = colr;
            _paintOffset = paintOffset;
            PaintOffset = childOffset;
            ScaleX = scaleX;
            ScaleY = scaleY;
            CenterX = centerX;
            CenterY = centerY;
            VarIndexBase = varIndexBase;
        }

        public bool TryGetPaint(out Paint paint)
        {
            paint = default;
            int abs = checked(_paintOffset + PaintOffset);
            return Paint.TryCreate(_colr, abs, out paint);
        }
    }

    public readonly struct PaintScaleUniform
    {
        private readonly TableSlice _colr;
        private readonly int _paintOffset;

        public int PaintOffset { get; }
        public F2Dot14 Scale { get; }

        internal PaintScaleUniform(TableSlice colr, int paintOffset, int childOffset, F2Dot14 scale)
        {
            _colr = colr;
            _paintOffset = paintOffset;
            PaintOffset = childOffset;
            Scale = scale;
        }

        public bool TryGetPaint(out Paint paint)
        {
            paint = default;
            int abs = checked(_paintOffset + PaintOffset);
            return Paint.TryCreate(_colr, abs, out paint);
        }
    }

    public readonly struct PaintVarScaleUniform
    {
        private readonly TableSlice _colr;
        private readonly int _paintOffset;

        public int PaintOffset { get; }
        public F2Dot14 Scale { get; }
        public uint VarIndexBase { get; }

        internal PaintVarScaleUniform(TableSlice colr, int paintOffset, int childOffset, F2Dot14 scale, uint varIndexBase)
        {
            _colr = colr;
            _paintOffset = paintOffset;
            PaintOffset = childOffset;
            Scale = scale;
            VarIndexBase = varIndexBase;
        }

        public bool TryGetPaint(out Paint paint)
        {
            paint = default;
            int abs = checked(_paintOffset + PaintOffset);
            return Paint.TryCreate(_colr, abs, out paint);
        }
    }

    public readonly struct PaintScaleUniformAroundCenter
    {
        private readonly TableSlice _colr;
        private readonly int _paintOffset;

        public int PaintOffset { get; }
        public F2Dot14 Scale { get; }
        public short CenterX { get; }
        public short CenterY { get; }

        internal PaintScaleUniformAroundCenter(TableSlice colr, int paintOffset, int childOffset, F2Dot14 scale, short centerX, short centerY)
        {
            _colr = colr;
            _paintOffset = paintOffset;
            PaintOffset = childOffset;
            Scale = scale;
            CenterX = centerX;
            CenterY = centerY;
        }

        public bool TryGetPaint(out Paint paint)
        {
            paint = default;
            int abs = checked(_paintOffset + PaintOffset);
            return Paint.TryCreate(_colr, abs, out paint);
        }
    }

    public readonly struct PaintVarScaleUniformAroundCenter
    {
        private readonly TableSlice _colr;
        private readonly int _paintOffset;

        public int PaintOffset { get; }
        public F2Dot14 Scale { get; }
        public short CenterX { get; }
        public short CenterY { get; }
        public uint VarIndexBase { get; }

        internal PaintVarScaleUniformAroundCenter(TableSlice colr, int paintOffset, int childOffset, F2Dot14 scale, short centerX, short centerY, uint varIndexBase)
        {
            _colr = colr;
            _paintOffset = paintOffset;
            PaintOffset = childOffset;
            Scale = scale;
            CenterX = centerX;
            CenterY = centerY;
            VarIndexBase = varIndexBase;
        }

        public bool TryGetPaint(out Paint paint)
        {
            paint = default;
            int abs = checked(_paintOffset + PaintOffset);
            return Paint.TryCreate(_colr, abs, out paint);
        }
    }

    public readonly struct PaintRotate
    {
        private readonly TableSlice _colr;
        private readonly int _paintOffset;

        public int PaintOffset { get; }
        public F2Dot14 Angle { get; }

        internal PaintRotate(TableSlice colr, int paintOffset, int childOffset, F2Dot14 angle)
        {
            _colr = colr;
            _paintOffset = paintOffset;
            PaintOffset = childOffset;
            Angle = angle;
        }

        public bool TryGetPaint(out Paint paint)
        {
            paint = default;
            int abs = checked(_paintOffset + PaintOffset);
            return Paint.TryCreate(_colr, abs, out paint);
        }
    }

    public readonly struct PaintVarRotate
    {
        private readonly TableSlice _colr;
        private readonly int _paintOffset;

        public int PaintOffset { get; }
        public F2Dot14 Angle { get; }
        public uint VarIndexBase { get; }

        internal PaintVarRotate(TableSlice colr, int paintOffset, int childOffset, F2Dot14 angle, uint varIndexBase)
        {
            _colr = colr;
            _paintOffset = paintOffset;
            PaintOffset = childOffset;
            Angle = angle;
            VarIndexBase = varIndexBase;
        }

        public bool TryGetPaint(out Paint paint)
        {
            paint = default;
            int abs = checked(_paintOffset + PaintOffset);
            return Paint.TryCreate(_colr, abs, out paint);
        }
    }

    public readonly struct PaintRotateAroundCenter
    {
        private readonly TableSlice _colr;
        private readonly int _paintOffset;

        public int PaintOffset { get; }
        public F2Dot14 Angle { get; }
        public short CenterX { get; }
        public short CenterY { get; }

        internal PaintRotateAroundCenter(TableSlice colr, int paintOffset, int childOffset, F2Dot14 angle, short centerX, short centerY)
        {
            _colr = colr;
            _paintOffset = paintOffset;
            PaintOffset = childOffset;
            Angle = angle;
            CenterX = centerX;
            CenterY = centerY;
        }

        public bool TryGetPaint(out Paint paint)
        {
            paint = default;
            int abs = checked(_paintOffset + PaintOffset);
            return Paint.TryCreate(_colr, abs, out paint);
        }
    }

    public readonly struct PaintVarRotateAroundCenter
    {
        private readonly TableSlice _colr;
        private readonly int _paintOffset;

        public int PaintOffset { get; }
        public F2Dot14 Angle { get; }
        public short CenterX { get; }
        public short CenterY { get; }
        public uint VarIndexBase { get; }

        internal PaintVarRotateAroundCenter(TableSlice colr, int paintOffset, int childOffset, F2Dot14 angle, short centerX, short centerY, uint varIndexBase)
        {
            _colr = colr;
            _paintOffset = paintOffset;
            PaintOffset = childOffset;
            Angle = angle;
            CenterX = centerX;
            CenterY = centerY;
            VarIndexBase = varIndexBase;
        }

        public bool TryGetPaint(out Paint paint)
        {
            paint = default;
            int abs = checked(_paintOffset + PaintOffset);
            return Paint.TryCreate(_colr, abs, out paint);
        }
    }

    public readonly struct PaintSkew
    {
        private readonly TableSlice _colr;
        private readonly int _paintOffset;

        public int PaintOffset { get; }
        public F2Dot14 XSkewAngle { get; }
        public F2Dot14 YSkewAngle { get; }

        internal PaintSkew(TableSlice colr, int paintOffset, int childOffset, F2Dot14 xSkewAngle, F2Dot14 ySkewAngle)
        {
            _colr = colr;
            _paintOffset = paintOffset;
            PaintOffset = childOffset;
            XSkewAngle = xSkewAngle;
            YSkewAngle = ySkewAngle;
        }

        public bool TryGetPaint(out Paint paint)
        {
            paint = default;
            int abs = checked(_paintOffset + PaintOffset);
            return Paint.TryCreate(_colr, abs, out paint);
        }
    }

    public readonly struct PaintVarSkew
    {
        private readonly TableSlice _colr;
        private readonly int _paintOffset;

        public int PaintOffset { get; }
        public F2Dot14 XSkewAngle { get; }
        public F2Dot14 YSkewAngle { get; }
        public uint VarIndexBase { get; }

        internal PaintVarSkew(TableSlice colr, int paintOffset, int childOffset, F2Dot14 xSkewAngle, F2Dot14 ySkewAngle, uint varIndexBase)
        {
            _colr = colr;
            _paintOffset = paintOffset;
            PaintOffset = childOffset;
            XSkewAngle = xSkewAngle;
            YSkewAngle = ySkewAngle;
            VarIndexBase = varIndexBase;
        }

        public bool TryGetPaint(out Paint paint)
        {
            paint = default;
            int abs = checked(_paintOffset + PaintOffset);
            return Paint.TryCreate(_colr, abs, out paint);
        }
    }

    public readonly struct PaintSkewAroundCenter
    {
        private readonly TableSlice _colr;
        private readonly int _paintOffset;

        public int PaintOffset { get; }
        public F2Dot14 XSkewAngle { get; }
        public F2Dot14 YSkewAngle { get; }
        public short CenterX { get; }
        public short CenterY { get; }

        internal PaintSkewAroundCenter(TableSlice colr, int paintOffset, int childOffset, F2Dot14 xSkewAngle, F2Dot14 ySkewAngle, short centerX, short centerY)
        {
            _colr = colr;
            _paintOffset = paintOffset;
            PaintOffset = childOffset;
            XSkewAngle = xSkewAngle;
            YSkewAngle = ySkewAngle;
            CenterX = centerX;
            CenterY = centerY;
        }

        public bool TryGetPaint(out Paint paint)
        {
            paint = default;
            int abs = checked(_paintOffset + PaintOffset);
            return Paint.TryCreate(_colr, abs, out paint);
        }
    }

    public readonly struct PaintVarSkewAroundCenter
    {
        private readonly TableSlice _colr;
        private readonly int _paintOffset;

        public int PaintOffset { get; }
        public F2Dot14 XSkewAngle { get; }
        public F2Dot14 YSkewAngle { get; }
        public short CenterX { get; }
        public short CenterY { get; }
        public uint VarIndexBase { get; }

        internal PaintVarSkewAroundCenter(TableSlice colr, int paintOffset, int childOffset, F2Dot14 xSkewAngle, F2Dot14 ySkewAngle, short centerX, short centerY, uint varIndexBase)
        {
            _colr = colr;
            _paintOffset = paintOffset;
            PaintOffset = childOffset;
            XSkewAngle = xSkewAngle;
            YSkewAngle = ySkewAngle;
            CenterX = centerX;
            CenterY = centerY;
            VarIndexBase = varIndexBase;
        }

        public bool TryGetPaint(out Paint paint)
        {
            paint = default;
            int abs = checked(_paintOffset + PaintOffset);
            return Paint.TryCreate(_colr, abs, out paint);
        }
    }

    public readonly struct PaintComposite
    {
        private readonly TableSlice _colr;
        private readonly int _paintOffset;

        public int SourcePaintOffset { get; }
        public byte CompositeMode { get; }
        public int BackdropPaintOffset { get; }

        internal PaintComposite(TableSlice colr, int paintOffset, int sourcePaintOffset, byte compositeMode, int backdropPaintOffset)
        {
            _colr = colr;
            _paintOffset = paintOffset;
            SourcePaintOffset = sourcePaintOffset;
            CompositeMode = compositeMode;
            BackdropPaintOffset = backdropPaintOffset;
        }

        public bool TryGetSourcePaint(out Paint paint)
        {
            paint = default;
            int abs = checked(_paintOffset + SourcePaintOffset);
            return Paint.TryCreate(_colr, abs, out paint);
        }

        public bool TryGetBackdropPaint(out Paint paint)
        {
            paint = default;
            int abs = checked(_paintOffset + BackdropPaintOffset);
            return Paint.TryCreate(_colr, abs, out paint);
        }
    }

    private static bool TryReadV0Header(ReadOnlySpan<byte> colr, int length, out ushort numBaseGlyphRecords, out int baseGlyphRecordsOffset, out int layerRecordsOffset, out ushort numLayerRecords)
    {
        numBaseGlyphRecords = BigEndian.ReadUInt16(colr, 2);
        numLayerRecords = BigEndian.ReadUInt16(colr, 12);
        baseGlyphRecordsOffset = 0;
        layerRecordsOffset = 0;

        uint baseGlyphRecordsOffsetU = BigEndian.ReadUInt32(colr, 4);
        uint layerRecordsOffsetU = BigEndian.ReadUInt32(colr, 8);
        if (baseGlyphRecordsOffsetU > int.MaxValue || layerRecordsOffsetU > int.MaxValue)
            return false;

        baseGlyphRecordsOffset = (int)baseGlyphRecordsOffsetU;
        layerRecordsOffset = (int)layerRecordsOffsetU;

        long baseBytesLong = (long)numBaseGlyphRecords * 6;
        long layerBytesLong = (long)numLayerRecords * 4;
        if (baseBytesLong > int.MaxValue || layerBytesLong > int.MaxValue)
            return false;

        int baseBytes = (int)baseBytesLong;
        int layerBytes = (int)layerBytesLong;

        if ((uint)baseGlyphRecordsOffset > (uint)length - (uint)baseBytes)
            return false;
        if ((uint)layerRecordsOffset > (uint)length - (uint)layerBytes)
            return false;

        return true;
    }

    private static bool TryReadV0HeaderOptional(ReadOnlySpan<byte> colr, int length, out ushort numBaseGlyphRecords, out int baseGlyphRecordsOffset, out int layerRecordsOffset, out ushort numLayerRecords)
    {
        numBaseGlyphRecords = BigEndian.ReadUInt16(colr, 2);
        numLayerRecords = BigEndian.ReadUInt16(colr, 12);
        baseGlyphRecordsOffset = 0;
        layerRecordsOffset = 0;

        uint baseGlyphRecordsOffsetU = BigEndian.ReadUInt32(colr, 4);
        uint layerRecordsOffsetU = BigEndian.ReadUInt32(colr, 8);
        if (baseGlyphRecordsOffsetU > int.MaxValue || layerRecordsOffsetU > int.MaxValue)
            return false;

        baseGlyphRecordsOffset = (int)baseGlyphRecordsOffsetU;
        layerRecordsOffset = (int)layerRecordsOffsetU;

        long baseBytesLong = (long)numBaseGlyphRecords * 6;
        long layerBytesLong = (long)numLayerRecords * 4;
        if (baseBytesLong > int.MaxValue || layerBytesLong > int.MaxValue)
            return false;

        int baseBytes = (int)baseBytesLong;
        int layerBytes = (int)layerBytesLong;

        if (numBaseGlyphRecords != 0 && (uint)baseGlyphRecordsOffset > (uint)length - (uint)baseBytes)
            return false;

        if (numLayerRecords != 0 && (uint)layerRecordsOffset > (uint)length - (uint)layerBytes)
            return false;

        return true;
    }

    private static bool ValidateBaseGlyphList(ReadOnlySpan<byte> colr, int length, int offset)
    {
        if ((uint)offset > (uint)length - 4)
            return false;

        uint count = BigEndian.ReadUInt32(colr, offset);
        long bytesLong = 4L + (count * 6L);
        if (bytesLong > int.MaxValue)
            return false;

        int bytes = (int)bytesLong;
        return (uint)offset <= (uint)length - (uint)bytes;
    }

    private static bool ValidateLayerList(ReadOnlySpan<byte> colr, int length, int offset)
    {
        if ((uint)offset > (uint)length - 4)
            return false;

        uint count = BigEndian.ReadUInt32(colr, offset);
        long bytesLong = 4L + (count * 4L);
        if (bytesLong > int.MaxValue)
            return false;

        int bytes = (int)bytesLong;
        return (uint)offset <= (uint)length - (uint)bytes;
    }
}
