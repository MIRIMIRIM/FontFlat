namespace OTFontFile2;

/// <summary>
/// Copy-on-write editor for building a new sfnt from an existing <see cref="SfntFont"/>,
/// overriding only the tables that have been replaced.
/// </summary>
public sealed class SfntEditor
{
    private readonly SfntFont _font;
    private readonly Dictionary<Tag, ISfntTableSource> _overrides = new();
    private readonly HashSet<Tag> _removed = new();

    public SfntEditor(SfntFont font) => _font = font;

    public SfntFont Font => _font;

    public int OverrideCount => _overrides.Count;

    public void SetTable(ISfntTableSource table)
    {
        if (table is null) throw new ArgumentNullException(nameof(table));

        _removed.Remove(table.Tag);
        _overrides[table.Tag] = table;
    }

    public void SetTable(Tag tag, ReadOnlyMemory<byte> data)
        => SetTable(new MemoryTableSource(tag, data));

    public bool RemoveTable(Tag tag)
    {
        _overrides.Remove(tag);
        return _removed.Add(tag);
    }

    public bool TryGetOverride(Tag tag, out ISfntTableSource table)
        => _overrides.TryGetValue(tag, out table!);

    public void WriteTo(Stream destination, SfntWriteOptions? options = null)
    {
        if (destination is null) throw new ArgumentNullException(nameof(destination));

        SfntWriter.Write(destination, _font.SfntVersion, EnumerateTableSources(), options);
    }

    public byte[] ToArray(SfntWriteOptions? options = null)
    {
        using var ms = new MemoryStream();
        WriteTo(ms, options);
        return ms.ToArray();
    }

    private IEnumerable<ISfntTableSource> EnumerateTableSources()
    {
        var existingTags = new HashSet<Tag>();

        var directory = _font.Directory;
        var buffer = _font.Buffer;

        int count = _font.TableCount;
        for (int i = 0; i < count; i++)
        {
            var record = directory.GetRecord(i);
            existingTags.Add(record.Tag);

            if (_removed.Contains(record.Tag))
                continue;

            if (_overrides.TryGetValue(record.Tag, out var replacement))
            {
                yield return replacement;
                continue;
            }

            if (record.Offset > int.MaxValue || record.Length > int.MaxValue)
                throw new NotSupportedException("Fonts larger than 2GB are not supported by span-based APIs.");

            int offset = (int)record.Offset;
            int length = (int)record.Length;
            if (!buffer.TrySlice(offset, length, out _))
                throw new InvalidDataException($"Table out of bounds: {record.Tag}.");

            yield return new FontSliceTableSource(buffer, record.Tag, offset, length);
        }

        foreach (var (tag, source) in _overrides)
        {
            if (_removed.Contains(tag))
                continue;

            if (!existingTags.Contains(tag))
                yield return source;
        }
    }
}

