using OTFontFile2.SourceGen;

namespace OTFontFile2.Tables;

/// <summary>
/// bdat (Apple bitmap data) table.
/// Layout-compatible with EBDT at the header level; this type is a thin wrapper over <see cref="EbdtTable"/>.
/// </summary>
[OtTable("bdat", 4, GenerateTryCreate = false, GenerateStorage = false)]
public readonly partial struct BdatTable
{
    private readonly TableSlice _table;
    private readonly EbdtTable _ebdt;

    private BdatTable(TableSlice table, EbdtTable ebdt)
    {
        _table = table;
        _ebdt = ebdt;
    }

    public static bool TryCreate(TableSlice table, out BdatTable bdat)
    {
        if (!EbdtTable.TryCreate(table, out var ebdt))
        {
            bdat = default;
            return false;
        }

        bdat = new BdatTable(table, ebdt);
        return true;
    }

    public Fixed1616 Version => _ebdt.Version;

    public bool TryGetGlyphSpan(int offset, int length, out ReadOnlySpan<byte> glyphData)
        => _ebdt.TryGetGlyphSpan(offset, length, out glyphData);
}
