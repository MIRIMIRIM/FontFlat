using OTFontFile2.SourceGen;

namespace OTFontFile2.Tables;

/// <summary>
/// GDEF LigCaretList table.
/// </summary>
[OtSubTable(4)]
[OtField("CoverageOffset", OtFieldKind.UInt16, 0)]
[OtField("LigGlyphCount", OtFieldKind.UInt16, 2)]
[OtUInt16Array("LigGlyphOffset", 4, CountPropertyName = "LigGlyphCount")]
[OtSubTableOffset("Coverage", nameof(CoverageOffset), typeof(CoverageTable))]
[OtSubTableOffsetArray("LigGlyphTable", "LigGlyphOffset", typeof(LigGlyphTable), OutParameterName = "ligGlyph")]
public readonly partial struct GdefLigCaretListTable
{
    public bool TryGetLigGlyphTableForGlyph(ushort glyphId, out bool covered, out LigGlyphTable ligGlyph)
    {
        covered = false;
        ligGlyph = default;

        if (!TryGetCoverage(out var coverageTable))
            return false;

        if (!coverageTable.TryGetCoverage(glyphId, out covered, out ushort index))
            return false;

        if (!covered)
            return true;

        return TryGetLigGlyphTable(index, out ligGlyph);
    }

    [OtSubTable(2)]
    [OtField("CaretCount", OtFieldKind.UInt16, 0)]
    [OtUInt16Array("CaretValueOffset", 2, CountPropertyName = "CaretCount")]
    [OtSubTableOffsetArray("CaretValueTable", "CaretValueOffset", typeof(CaretValueTable), OutParameterName = "caretValue")]
    public readonly partial struct LigGlyphTable
    {
    }

    [OtSubTable(4)]
    [OtField("CaretValueFormat", OtFieldKind.UInt16, 0)]
    public readonly partial struct CaretValueTable
    {
        public bool TryGetCoordinate(out short coordinate)
        {
            coordinate = 0;

            ushort format = CaretValueFormat;
            if (format != 1 && format != 3)
                return false;

            if ((uint)_offset > (uint)_table.Length - 4)
                return false;

            coordinate = BigEndian.ReadInt16(_table.Span, _offset + 2);
            return true;
        }

        public bool TryGetCaretValuePoint(out ushort pointIndex)
        {
            pointIndex = 0;

            if (CaretValueFormat != 2)
                return false;

            if ((uint)_offset > (uint)_table.Length - 4)
                return false;

            pointIndex = BigEndian.ReadUInt16(_table.Span, _offset + 2);
            return true;
        }

        public bool TryGetDeviceTableOffset(out ushort deviceTableOffset)
        {
            deviceTableOffset = 0;

            if (CaretValueFormat != 3)
                return false;

            // format(2) + coordinate(2) + deviceOffset(2)
            if ((uint)_offset > (uint)_table.Length - 6)
                return false;

            deviceTableOffset = BigEndian.ReadUInt16(_table.Span, _offset + 4);
            return true;
        }

        public bool TryGetDeviceTableAbsoluteOffset(out int absoluteOffset)
        {
            absoluteOffset = 0;

            if (!TryGetDeviceTableOffset(out ushort rel) || rel == 0)
                return false;

            int offset = _offset + rel;
            if ((uint)offset >= (uint)_table.Length)
                return false;

            absoluteOffset = offset;
            return true;
        }
    }
}
