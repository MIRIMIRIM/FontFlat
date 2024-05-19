using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FontFlat.OpenType.DataTypes;

public struct Offset16(ushort value)
{
    private readonly ushort value = value;
    public static implicit operator Offset16(ushort value) => new Offset16(value);
    public static implicit operator ushort(Offset16 offset) => offset.value;
    public static implicit operator uint(Offset16 offset) => offset.value;
    public static implicit operator int(Offset16 offset) => offset.value;
    public override readonly string ToString() => value.ToString();
}
public struct Offset24
{
}

public struct Offset32(uint value)
{
    private readonly uint value = value;
    public static implicit operator Offset32(uint value) => new Offset32(value);
    public static implicit operator uint(Offset32 offset) => offset.value;
    public static implicit operator int(Offset32 offset) => (int)offset.value;
    public static implicit operator long(Offset32 offset) => (long)offset.value;
    public override readonly string ToString() => value.ToString();
}
