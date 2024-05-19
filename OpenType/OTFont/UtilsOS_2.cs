using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OpenType.OTFont;

public partial class OTFont
{
    public enum FontWeight    // windows.h
    {
        Thin = 100,
        ExtraLight = 200,    // Ultra-light
        Light = 300,
        Normal = 400,        // Regular
        Medium = 500,
        SemiBold = 600,      // Demi-bold
        Bold = 700,
        ExtraBold = 800,     // Ultra-bold
        Black = 900,         // Heavy
    }
    public enum FontWidth
    {
        UltraCondensed = 1,
        ExtraCondensed = 2,
        Condensed = 3,
        SemiCondensed = 4,
        Normal = 5,         // Regular
        SemiExpanded = 6,
        Expanded = 7,
        ExtraExpanded = 8,
        UltraExpanded = 9,
    };
}
