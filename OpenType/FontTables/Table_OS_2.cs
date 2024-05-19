using FontFlat.OpenType.DataTypes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FontFlat.OpenType.FontTables;

public record struct Table_OS_2
{
    public ushort version;
    public short xAvgCharWidth;
    public ushort usWeightClass;
    public ushort usWidthClass;
    public ushort fsType;               // font embedding licensing
    public ushort ySubscriptXSize;
    public ushort ySubscriptYSize;
    public ushort ySubscriptXOffset;
    public ushort ySubscriptYOffset;
    public ushort ySupscriptXSize;
    public ushort ySupscriptYSize;
    public ushort ySupscriptXOffset;
    public ushort ySupscriptYOffset;
    public ushort yStrikeoutSize;
    public ushort yStrikeoutPosition;
    public short sFamilyClass;
    public byte[] panose;               // https://monotype.github.io/panose/pan1.htm
    public uint ulUnicodeRange1;
    public uint ulUnicodeRange2;
    public uint ulUnicodeRange3;
    public uint ulUnicodeRange4;
    public Tag achVendID;
    public ushort fsSelection;
    public ushort usFirstCharIndex;
    public ushort usLastCharIndex;
    
    public short? sTypoAscender;
    public short? sTypoDescender;
    public short? sTypoLineGap;
    public ushort? usWinAscent;
    public ushort? usWinDescent;
    
    public uint? ulCodePageRange1;
    public uint? ulCodePageRange2;
    
    public short? sxHeight;
    public short? sCapHeight;
    public ushort? usDefaultChar;
    public ushort? usBreakChar;
    public ushort? usMaxContext;
    
    public ushort? usLowerOpticalPointSize;
    public ushort? usUpperOpticalPointSize;
}
