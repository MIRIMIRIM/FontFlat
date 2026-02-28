using OTFontFile2.SourceGen;

namespace OTFontFile2.Tables;

/// <summary>
/// CBLC (Color Bitmap Location) table.
/// Layout-compatible with EBLC; this type is a thin wrapper over <see cref="EblcTable"/>.
/// </summary>
[OtTable("CBLC", 8, GenerateTryCreate = false, GenerateStorage = false)]
public readonly partial struct CblcTable
{
    private readonly TableSlice _table;
    private readonly EblcTable _eblc;

    private CblcTable(TableSlice table, EblcTable eblc)
    {
        _table = table;
        _eblc = eblc;
    }

    public static bool TryCreate(TableSlice table, out CblcTable cblc)
    {
        if (!EblcTable.TryCreate(table, out var eblc))
        {
            cblc = default;
            return false;
        }

        cblc = new CblcTable(table, eblc);
        return true;
    }

    public Fixed1616 Version => _eblc.Version;

    public uint BitmapSizeTableCount => _eblc.BitmapSizeTableCount;

    public bool TryGetBitmapSizeTable(int index, out EblcTable.BitmapSizeTable table)
        => _eblc.TryGetBitmapSizeTable(index, out table);
}
