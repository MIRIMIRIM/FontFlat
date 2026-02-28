namespace OTFontFile2.Tables.Cff;

public readonly struct CffSubrProvider : IType2SubrProvider
{
    private readonly CffIndex _index;

    public CffSubrProvider(CffIndex index) => _index = index;

    public int Count => _index.Count;

    public bool TryGetSubrBytes(int index, out ReadOnlySpan<byte> bytes)
        => _index.TryGetObjectSpan(index, out bytes);
}

public readonly struct Cff2SubrProvider : IType2SubrProvider
{
    private readonly Cff2Index _index;

    public Cff2SubrProvider(Cff2Index index) => _index = index;

    public int Count => _index.Count > int.MaxValue ? int.MaxValue : (int)_index.Count;

    public bool TryGetSubrBytes(int index, out ReadOnlySpan<byte> bytes)
        => _index.TryGetObjectSpan(index, out bytes);
}

public readonly struct EmptySubrProvider : IType2SubrProvider
{
    public int Count => 0;
    public bool TryGetSubrBytes(int index, out ReadOnlySpan<byte> bytes)
    {
        bytes = default;
        return false;
    }
}

