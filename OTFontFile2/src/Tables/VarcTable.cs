using OTFontFile2.SourceGen;

namespace OTFontFile2.Tables;

/// <summary>
/// OpenType <c>VARC</c> table.
/// Variation information for composite glyphs.
/// </summary>
[OtTable("VARC", 24, GenerateTryCreate = false)]
[OtField("Version", OtFieldKind.Fixed1616, 0)]
[OtField("CoverageOffset", OtFieldKind.UInt32, 4)]
[OtField("MultiVarStoreOffset", OtFieldKind.UInt32, 8)]
[OtField("ConditionListOffset", OtFieldKind.UInt32, 12)]
[OtField("AxisIndicesListOffset", OtFieldKind.UInt32, 16)]
[OtField("VarCompositeGlyphsOffset", OtFieldKind.UInt32, 20)]
public readonly partial struct VarcTable
{
    public static bool TryCreate(TableSlice table, out VarcTable varc)
    {
        varc = default;

        // Version(4) + 5 offsets32.
        if (table.Length < 24)
            return false;

        var data = table.Span;
        uint coverageOffsetU = BigEndian.ReadUInt32(data, 4);
        uint multiVarStoreOffsetU = BigEndian.ReadUInt32(data, 8);
        uint conditionListOffsetU = BigEndian.ReadUInt32(data, 12);
        uint axisIndicesListOffsetU = BigEndian.ReadUInt32(data, 16);
        uint varCompositeGlyphsOffsetU = BigEndian.ReadUInt32(data, 20);

        if (coverageOffsetU > int.MaxValue ||
            multiVarStoreOffsetU > int.MaxValue ||
            conditionListOffsetU > int.MaxValue ||
            axisIndicesListOffsetU > int.MaxValue ||
            varCompositeGlyphsOffsetU > int.MaxValue)
        {
            return false;
        }

        int coverageOffset = (int)coverageOffsetU;
        int multiVarStoreOffset = (int)multiVarStoreOffsetU;
        int conditionListOffset = (int)conditionListOffsetU;
        int axisIndicesListOffset = (int)axisIndicesListOffsetU;
        int varCompositeGlyphsOffset = (int)varCompositeGlyphsOffsetU;

        if (coverageOffset != 0 && (uint)coverageOffset > (uint)table.Length - 1)
            return false;
        if (multiVarStoreOffset != 0 && (uint)multiVarStoreOffset > (uint)table.Length - 1)
            return false;
        if (conditionListOffset != 0 && (uint)conditionListOffset > (uint)table.Length - 1)
            return false;
        if (axisIndicesListOffset != 0 && (uint)axisIndicesListOffset > (uint)table.Length - 1)
            return false;
        if (varCompositeGlyphsOffset != 0 && (uint)varCompositeGlyphsOffset > (uint)table.Length - 1)
            return false;

        varc = new VarcTable(table);
        return true;
    }

    public bool TryGetCoverage(out CoverageTable coverage)
    {
        coverage = default;

        uint offsetU = CoverageOffset;
        if (offsetU == 0 || offsetU > int.MaxValue)
            return false;

        return CoverageTable.TryCreate(_table, (int)offsetU, out coverage);
    }
}

