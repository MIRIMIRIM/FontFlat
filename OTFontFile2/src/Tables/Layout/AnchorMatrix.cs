using OTFontFile2.SourceGen;

namespace OTFontFile2.Tables;

/// <summary>
/// GPOS AnchorMatrix table used by mark attachment subtables.
/// Offsets in the matrix are relative to the beginning of the AnchorMatrix table.
/// </summary>
[OtSubTable(2, GenerateTryCreate = false, GenerateStorage = false)]
[OtField("RowCount", OtFieldKind.UInt16, 0)]
public readonly partial struct AnchorMatrix
{
    private readonly TableSlice _table;
    private readonly int _offset;
    private readonly ushort _cols;

    private AnchorMatrix(TableSlice table, int offset, ushort cols)
    {
        _table = table;
        _offset = offset;
        _cols = cols;
    }

    public static bool TryCreate(TableSlice table, int offset, ushort cols, out AnchorMatrix matrix)
    {
        matrix = default;

        if (cols == 0)
            return false;

        if ((uint)offset > (uint)table.Length - 2)
            return false;

        ushort rows = BigEndian.ReadUInt16(table.Span, offset);
        long countLong = (long)rows * cols;
        if (countLong > int.MaxValue)
            return false;

        long bytesLong = 2L + (countLong * 2L);
        if (bytesLong > int.MaxValue)
            return false;

        int bytes = (int)bytesLong;
        if ((uint)offset > (uint)table.Length - (uint)bytes)
            return false;

        matrix = new AnchorMatrix(table, offset, cols);
        return true;
    }

    public ushort ColumnCount => _cols;

    public bool TryGetAnchorOffset(int row, int col, out ushort anchorOffset)
    {
        anchorOffset = 0;

        ushort rows = RowCount;
        if ((uint)row >= rows || (uint)col >= _cols)
            return false;

        int index = checked((row * _cols) + col);
        int o = checked(_offset + 2 + (index * 2));
        if ((uint)o > (uint)_table.Length - 2)
            return false;

        anchorOffset = BigEndian.ReadUInt16(_table.Span, o);
        return true;
    }

    public bool TryGetAnchorTable(int row, int col, out bool present, out AnchorTable anchor)
    {
        anchor = default;

        if (!TryGetAnchorOffset(row, col, out ushort rel))
        {
            present = false;
            return false;
        }

        if (rel == 0)
        {
            present = false;
            return true;
        }

        int abs = checked(_offset + rel);
        present = AnchorTable.TryCreate(_table, abs, out anchor);
        return present;
    }
}
