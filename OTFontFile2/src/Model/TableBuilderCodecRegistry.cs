namespace OTFontFile2;

public interface ITableBuilderCodec
{
    Tag Tag { get; }
    Type BuilderType { get; }

    bool TryCreateBuilder(FontModel model, out ISfntTableSource builder);
}

public abstract class TableBuilderCodec<TBuilder> : ITableBuilderCodec
    where TBuilder : class, ISfntTableSource
{
    public abstract Tag Tag { get; }

    public Type BuilderType => typeof(TBuilder);

    bool ITableBuilderCodec.TryCreateBuilder(FontModel model, out ISfntTableSource builder)
    {
        if (TryCreateBuilder(model, out var typed))
        {
            builder = typed;
            return true;
        }

        builder = null!;
        return false;
    }

    public abstract bool TryCreateBuilder(FontModel model, out TBuilder builder);
}

public sealed partial class TableBuilderCodecRegistry
{
    private readonly Dictionary<Tag, ITableBuilderCodec> _byTag;
    private readonly Dictionary<Type, ITableBuilderCodec> _byBuilderType;

    public TableBuilderCodecRegistry()
    {
        _byTag = new Dictionary<Tag, ITableBuilderCodec>();
        _byBuilderType = new Dictionary<Type, ITableBuilderCodec>();
    }

    private TableBuilderCodecRegistry(int tagCapacity, int typeCapacity)
    {
        _byTag = new Dictionary<Tag, ITableBuilderCodec>(tagCapacity);
        _byBuilderType = new Dictionary<Type, ITableBuilderCodec>(typeCapacity);
    }

    public int Count => _byTag.Count;

    public void Register(ITableBuilderCodec codec)
    {
        if (codec is null) throw new ArgumentNullException(nameof(codec));

        _byTag[codec.Tag] = codec;
        _byBuilderType[codec.BuilderType] = codec;
    }

    public bool TryGetByTag(Tag tag, out ITableBuilderCodec codec)
        => _byTag.TryGetValue(tag, out codec!);

    public bool TryGet<TBuilder>(out TableBuilderCodec<TBuilder> codec)
        where TBuilder : class, ISfntTableSource
    {
        if (_byBuilderType.TryGetValue(typeof(TBuilder), out var c) && c is TableBuilderCodec<TBuilder> typed)
        {
            codec = typed;
            return true;
        }

        codec = null!;
        return false;
    }

    public TableBuilderCodecRegistry Clone()
    {
        var clone = new TableBuilderCodecRegistry(_byTag.Count, _byBuilderType.Count);

        foreach (var kv in _byTag)
            clone._byTag.Add(kv.Key, kv.Value);

        foreach (var kv in _byBuilderType)
            clone._byBuilderType.Add(kv.Key, kv.Value);

        return clone;
    }

    public static TableBuilderCodecRegistry CreateDefault()
    {
        var r = new TableBuilderCodecRegistry(tagCapacity: 60, typeCapacity: 60);

        RegisterGenerated(r);

        return r;
    }
}
