using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FontFlat.OpenType.DataTypes;

// Date and time represented in number of seconds since 12:00 midnight, January 1, 1904, UTC. The value is represented as a signed 64-bit integer.
public struct LONGDATETIME(long value)
{
    public long Value = value;
}
