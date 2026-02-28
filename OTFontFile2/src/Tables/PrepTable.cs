using OTFontFile2.SourceGen;

namespace OTFontFile2.Tables;

[OtTable("prep", 0)]
public readonly partial struct PrepTable
{
    public int Length => _table.Length;

    public ReadOnlySpan<byte> Program => _table.Span;

    public bool TryGetByte(int index, out byte value)
    {
        value = 0;

        if ((uint)index >= (uint)_table.Length)
            return false;

        value = _table.Span[index];
        return true;
    }
}
