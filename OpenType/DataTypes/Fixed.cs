using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FontFlat.OpenType.DataTypes;

// 32-bit signed fixed-point number (16.16)
public struct Fixed
{
    private uint _value;

    public ushort High;
    public ushort Low;
}
