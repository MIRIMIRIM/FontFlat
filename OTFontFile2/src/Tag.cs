namespace OTFontFile2;

/// <summary>
/// 4-byte OpenType tag (Big Endian).
/// </summary>
public readonly struct Tag : IEquatable<Tag>, IComparable<Tag>
{
    private readonly uint _value;

    public Tag(uint value) => _value = value;

    public uint Value => _value;

    public override string ToString()
    {
        Span<char> chars = stackalloc char[4];
        chars[0] = (char)((_value >> 24) & 0xFF);
        chars[1] = (char)((_value >> 16) & 0xFF);
        chars[2] = (char)((_value >> 8) & 0xFF);
        chars[3] = (char)(_value & 0xFF);
        return new string(chars);
    }

    public bool Equals(Tag other) => _value == other._value;
    public override bool Equals(object? obj) => obj is Tag other && Equals(other);
    public override int GetHashCode() => (int)_value;
    public int CompareTo(Tag other) => _value.CompareTo(other._value);

    public static bool operator ==(Tag left, Tag right) => left.Equals(right);
    public static bool operator !=(Tag left, Tag right) => !left.Equals(right);

    public static bool TryParse(ReadOnlySpan<char> value, out Tag tag)
    {
        if (value.Length != 4)
        {
            tag = default;
            return false;
        }

        tag = new Tag((uint)value[0] << 24 | (uint)value[1] << 16 | (uint)value[2] << 8 | value[3]);
        return true;
    }
}

