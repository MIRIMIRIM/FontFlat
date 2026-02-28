namespace OTFontFile2;

public sealed class SfntBuilder
{
    private readonly List<ISfntTableSource> _tables = new();
    private readonly Dictionary<Tag, int> _indexByTag = new();

    public uint SfntVersion { get; set; } = 0x00010000; // TrueType

    public int TableCount => _tables.Count;

    public IEnumerable<ISfntTableSource> Tables => _tables;

    public void SetTable(ISfntTableSource table)
    {
        if (_indexByTag.TryGetValue(table.Tag, out int index))
        {
            _tables[index] = table;
            return;
        }

        _indexByTag.Add(table.Tag, _tables.Count);
        _tables.Add(table);
    }

    public void SetTable(Tag tag, ReadOnlyMemory<byte> data)
        => SetTable(new MemoryTableSource(tag, data));

    public bool RemoveTable(Tag tag)
    {
        if (!_indexByTag.TryGetValue(tag, out int index))
            return false;

        _tables.RemoveAt(index);
        _indexByTag.Remove(tag);

        // Fix indices after removal.
        for (int i = index; i < _tables.Count; i++)
        {
            _indexByTag[_tables[i].Tag] = i;
        }

        return true;
    }

    public bool TryGetTable(Tag tag, out ISfntTableSource table)
    {
        if (_indexByTag.TryGetValue(tag, out int index))
        {
            table = _tables[index];
            return true;
        }

        table = null!;
        return false;
    }

    public void WriteTo(Stream destination, SfntWriteOptions? options = null)
        => SfntWriter.Write(destination, SfntVersion, _tables, options);

    public byte[] ToArray(SfntWriteOptions? options = null)
    {
        using var ms = new MemoryStream();
        WriteTo(ms, options);
        return ms.ToArray();
    }
}

