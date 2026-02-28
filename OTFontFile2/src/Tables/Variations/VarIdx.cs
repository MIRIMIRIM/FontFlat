namespace OTFontFile2.Tables;

public readonly struct VarIdx : IEquatable<VarIdx>
{
    public VarIdx(ushort outerIndex, ushort innerIndex)
    {
        OuterIndex = outerIndex;
        InnerIndex = innerIndex;
    }

    public ushort OuterIndex { get; }
    public ushort InnerIndex { get; }

    public bool Equals(VarIdx other) => OuterIndex == other.OuterIndex && InnerIndex == other.InnerIndex;
    public override bool Equals(object? obj) => obj is VarIdx other && Equals(other);
    public override int GetHashCode() => (OuterIndex << 16) | InnerIndex;

    public static bool operator ==(VarIdx left, VarIdx right) => left.Equals(right);
    public static bool operator !=(VarIdx left, VarIdx right) => !left.Equals(right);
}

