using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FontFlat.OpenType;

public partial class OTFont
{
    /// <summary>
    /// Bit 0-6  : Value when set to 1.
    /// Bits 7–15: Reserved (set to 0).
    /// </summary>
    internal static readonly Dictionary<ushort, string> MacStyleBit = new()
    {
        {0, "Bold"},
        {1, "Italic"},
        {2, "Underline"},
        {3, "Underline"},
        {4, "Shadow"},
        {5, "Condensed"},
        {6, "Extended"},
    };
}
