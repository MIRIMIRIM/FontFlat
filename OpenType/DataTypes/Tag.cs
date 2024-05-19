using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FontFlat.OpenType.DataTypes;

public struct Tag(byte[] _value)
{
    private readonly byte[] value = _value;
    public readonly Span<byte> AsSpan() => value.AsSpan();
    public override string ToString() => Encoding.UTF8.GetString(value);
}
