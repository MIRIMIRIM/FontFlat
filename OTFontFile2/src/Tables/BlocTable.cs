using OTFontFile2.SourceGen;

namespace OTFontFile2.Tables;

/// <summary>
/// bloc (Apple bitmap location) table.
/// Layout-compatible with EBLC; this type is a thin wrapper over <see cref="EblcTable"/>.
/// </summary>
[OtTable("bloc", 8, GenerateTryCreate = false, GenerateStorage = false)]
public readonly partial struct BlocTable
{
    private readonly TableSlice _table;
    private readonly EblcTable _eblc;

    private BlocTable(TableSlice table, EblcTable eblc)
    {
        _table = table;
        _eblc = eblc;
    }

    public static bool TryCreate(TableSlice table, out BlocTable bloc)
    {
        if (!EblcTable.TryCreate(table, out var eblc))
        {
            bloc = default;
            return false;
        }

        bloc = new BlocTable(table, eblc);
        return true;
    }

    public Fixed1616 Version => _eblc.Version;

    public uint BitmapSizeTableCount => _eblc.BitmapSizeTableCount;

    public bool TryGetBitmapSizeTable(int index, out EblcTable.BitmapSizeTable table)
        => _eblc.TryGetBitmapSizeTable(index, out table);
}
