using System.Text;

namespace FontFlat.OpenType.DataTypes;

public readonly struct Tag(byte[] _value)
{
    private readonly byte[] value = _value;
    public readonly Span<byte> AsSpan() => value.AsSpan();
    public override readonly string ToString() => Encoding.UTF8.GetString(value);
}
