using OTFontFile2.SourceGen;

namespace OTFontFile2.Tables;

[OtTable("BASE", 8)]
[OtField("Version", OtFieldKind.Fixed1616, 0)]
[OtField("HorizAxisOffset", OtFieldKind.UInt16, 4)]
[OtField("VertAxisOffset", OtFieldKind.UInt16, 6)]
[OtSubTableOffset("HorizAxis", nameof(HorizAxisOffset), typeof(Axis), OutParameterName = "axis")]
[OtSubTableOffset("VertAxis", nameof(VertAxisOffset), typeof(Axis), OutParameterName = "axis")]
public readonly partial struct BaseTable
{
    [OtSubTable(4)]
    [OtField("BaseTagListOffset", OtFieldKind.UInt16, 0)]
    [OtField("BaseScriptListOffset", OtFieldKind.UInt16, 2)]
    [OtSubTableOffset("BaseTagList", nameof(BaseTagListOffset), typeof(BaseTagList), OutParameterName = "tagList")]
    [OtSubTableOffset("BaseScriptList", nameof(BaseScriptListOffset), typeof(BaseScriptList), OutParameterName = "scriptList")]
    public readonly partial struct Axis
    {
    }

    [OtSubTable(2)]
    [OtField("BaseTagCount", OtFieldKind.UInt16, 0)]
    public readonly partial struct BaseTagList
    {
        public bool TryGetBaselineTag(int index, out Tag tag)
        {
            tag = default;

            ushort count = BaseTagCount;
            if ((uint)index >= (uint)count)
                return false;

            int offset = _offset + 2 + (index * 4);
            if ((uint)offset > (uint)_table.Length - 4)
                return false;

            tag = new Tag(BigEndian.ReadUInt32(_table.Span, offset));
            return true;
        }
    }

    [OtSubTable(2)]
    [OtField("BaseScriptCount", OtFieldKind.UInt16, 0)]
    [OtTagOffsetRecordArray("BaseScript", 2, SubTableType = typeof(BaseScript), OutParameterName = "script")]
    public readonly partial struct BaseScriptList
    {
    }

    [OtSubTable(6)]
    [OtField("BaseValuesOffset", OtFieldKind.UInt16, 0)]
    [OtField("DefaultMinMaxOffset", OtFieldKind.UInt16, 2)]
    [OtField("BaseLangSysCount", OtFieldKind.UInt16, 4)]
    [OtTagOffsetRecordArray("BaseLangSys", 6)]
    [OtSubTableOffset("BaseValues", nameof(BaseValuesOffset), typeof(BaseValues), OutParameterName = "values")]
    [OtSubTableOffset("DefaultMinMax", nameof(DefaultMinMaxOffset), typeof(MinMax), OutParameterName = "minMax")]
    public readonly partial struct BaseScript
    {
        public bool TryGetMinMax(BaseLangSysRecord record, out MinMax minMax)
        {
            minMax = default;

            int rel = record.BaseLangSysOffset;
            if (rel == 0)
                return false;

            int offset = _offset + rel;
            return MinMax.TryCreate(_table, offset, out minMax);
        }
    }

    [OtSubTable(4)]
    [OtField("DefaultIndex", OtFieldKind.UInt16, 0)]
    [OtField("BaseCoordCount", OtFieldKind.UInt16, 2)]
    [OtUInt16Array("BaseCoordOffset", 4, CountPropertyName = "BaseCoordCount")]
    [OtSubTableOffsetArray("BaseCoord", "BaseCoordOffset", typeof(BaseCoord), OutParameterName = "coord")]
    public readonly partial struct BaseValues
    {
    }

    [OtSubTable(6)]
    [OtField("MinCoordOffset", OtFieldKind.UInt16, 0)]
    [OtField("MaxCoordOffset", OtFieldKind.UInt16, 2)]
    [OtField("FeatMinMaxCount", OtFieldKind.UInt16, 4)]
    [OtSequentialRecordArray("FeatMinMaxRecord", 6, 8, CountPropertyName = "FeatMinMaxCount")]
    public readonly partial struct MinMax
    {
        public readonly struct FeatMinMaxRecord
        {
            public Tag FeatureTableTag { get; }
            public ushort MinCoordOffset { get; }
            public ushort MaxCoordOffset { get; }

            public FeatMinMaxRecord(Tag featureTableTag, ushort minCoordOffset, ushort maxCoordOffset)
            {
                FeatureTableTag = featureTableTag;
                MinCoordOffset = minCoordOffset;
                MaxCoordOffset = maxCoordOffset;
            }
        }

        public bool TryGetMinCoord(out BaseCoord coord) => TryGetCoord(MinCoordOffset, out coord);
        public bool TryGetMaxCoord(out BaseCoord coord) => TryGetCoord(MaxCoordOffset, out coord);

        public bool TryGetFeatMinCoord(FeatMinMaxRecord record, out BaseCoord coord) => TryGetCoord(record.MinCoordOffset, out coord);
        public bool TryGetFeatMaxCoord(FeatMinMaxRecord record, out BaseCoord coord) => TryGetCoord(record.MaxCoordOffset, out coord);

        private bool TryGetCoord(ushort rel, out BaseCoord coord)
        {
            coord = default;

            int coordRel = rel;
            if (coordRel == 0)
                return false;

            int offset = _offset + coordRel;
            return BaseCoord.TryCreate(_table, offset, out coord);
        }
    }

    [OtSubTable(4)]
    [OtField("BaseCoordFormat", OtFieldKind.UInt16, 0)]
    [OtField("Coordinate", OtFieldKind.Int16, 2)]
    public readonly partial struct BaseCoord
    {
        public bool TryGetReferenceGlyphAndPoint(out ushort referenceGlyph, out ushort baseCoordPoint)
        {
            referenceGlyph = 0;
            baseCoordPoint = 0;

            if (BaseCoordFormat != 2)
                return false;
            if ((uint)_offset > (uint)_table.Length - 8)
                return false;

            var data = _table.Span;
            referenceGlyph = BigEndian.ReadUInt16(data, _offset + 4);
            baseCoordPoint = BigEndian.ReadUInt16(data, _offset + 6);
            return true;
        }

        public bool TryGetDeviceTableOffset(out ushort deviceTableOffset)
        {
            deviceTableOffset = 0;

            if (BaseCoordFormat != 3)
                return false;
            if ((uint)_offset > (uint)_table.Length - 6)
                return false;

            deviceTableOffset = BigEndian.ReadUInt16(_table.Span, _offset + 4);
            return true;
        }
    }
}
