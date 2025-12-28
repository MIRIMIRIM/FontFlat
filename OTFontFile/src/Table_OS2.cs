using System;
using OTFontFile.Generators;

namespace OTFontFile;

/// <summary>
/// 'OS/2' - OS/2 and Windows Metrics Table.
/// Partial class using source generation for property accessors.
/// </summary>
[OTTable("OS/2")]
[OTCacheable(FixedSize = 96)]  // Version 2+ size
public partial class Table_OS2 : OTTable
{
    // ==================== Version 0 fields (all versions) ====================
    [OTField(0, OTFieldType.UShort)]  private ushort _version;
    [OTField(2, OTFieldType.Short)]   private short _xAvgCharWidth;
    [OTField(4, OTFieldType.UShort)]  private ushort _usWeightClass;
    [OTField(6, OTFieldType.UShort)]  private ushort _usWidthClass;
    [OTField(8, OTFieldType.UShort)]  private ushort _fsType;
    [OTField(10, OTFieldType.Short)]  private short _ySubscriptXSize;
    [OTField(12, OTFieldType.Short)]  private short _ySubscriptYSize;
    [OTField(14, OTFieldType.Short)]  private short _ySubscriptXOffset;
    [OTField(16, OTFieldType.Short)]  private short _ySubscriptYOffset;
    [OTField(18, OTFieldType.Short)]  private short _ySuperscriptXSize;
    [OTField(20, OTFieldType.Short)]  private short _ySuperscriptYSize;
    [OTField(22, OTFieldType.Short)]  private short _ySuperscriptXOffset;
    [OTField(24, OTFieldType.Short)]  private short _ySuperscriptYOffset;
    [OTField(26, OTFieldType.Short)]  private short _yStrikeoutSize;
    [OTField(28, OTFieldType.Short)]  private short _yStrikeoutPosition;
    [OTField(30, OTFieldType.Short)]  private short _sFamilyClass;
    [OTField(32, OTFieldType.Byte)]   private byte _panose_byte1;
    [OTField(33, OTFieldType.Byte)]   private byte _panose_byte2;
    [OTField(34, OTFieldType.Byte)]   private byte _panose_byte3;
    [OTField(35, OTFieldType.Byte)]   private byte _panose_byte4;
    [OTField(36, OTFieldType.Byte)]   private byte _panose_byte5;
    [OTField(37, OTFieldType.Byte)]   private byte _panose_byte6;
    [OTField(38, OTFieldType.Byte)]   private byte _panose_byte7;
    [OTField(39, OTFieldType.Byte)]   private byte _panose_byte8;
    [OTField(40, OTFieldType.Byte)]   private byte _panose_byte9;
    [OTField(41, OTFieldType.Byte)]   private byte _panose_byte10;
    [OTField(42, OTFieldType.UInt)]   private uint _ulUnicodeRange1;
    [OTField(46, OTFieldType.UInt)]   private uint _ulUnicodeRange2;
    [OTField(50, OTFieldType.UInt)]   private uint _ulUnicodeRange3;
    [OTField(54, OTFieldType.UInt)]   private uint _ulUnicodeRange4;
    // achVendID at offset 58 is a 4-byte array - handled manually
    [OTField(62, OTFieldType.UShort)] private ushort _fsSelection;
    [OTField(64, OTFieldType.UShort)] private ushort _usFirstCharIndex;
    [OTField(66, OTFieldType.UShort)] private ushort _usLastCharIndex;
    [OTField(68, OTFieldType.Short)]  private short _sTypoAscender;
    [OTField(70, OTFieldType.Short)]  private short _sTypoDescender;
    [OTField(72, OTFieldType.Short)]  private short _sTypoLineGap;
    [OTField(74, OTFieldType.UShort)] private ushort _usWinAscent;
    [OTField(76, OTFieldType.UShort)] private ushort _usWinDescent;

    // ==================== Version 1+ fields ====================
    [OTField(78, OTFieldType.UInt, MinVersion = 1)]  private uint _ulCodePageRange1;
    [OTField(82, OTFieldType.UInt, MinVersion = 1)]  private uint _ulCodePageRange2;

    // ==================== Version 2+ fields ====================
    [OTField(86, OTFieldType.Short, MinVersion = 2)]  private short _sxHeight;
    [OTField(88, OTFieldType.Short, MinVersion = 2)]  private short _sCapHeight;
    [OTField(90, OTFieldType.UShort, MinVersion = 2)] private ushort _usDefaultChar;
    [OTField(92, OTFieldType.UShort, MinVersion = 2)] private ushort _usBreakChar;
    [OTField(94, OTFieldType.UShort, MinVersion = 2)] private ushort _usMaxContext;

    /// <summary>
    /// Constructor
    /// </summary>
    public Table_OS2(OTTag tag, MBOBuffer buf) : base(tag, buf)
    {
    }

    /************************
     * Manual fields (arrays)
     */

    public byte[] achVendID
    {
        get
        {
            byte[] fourBytes = new byte[4];
            System.Buffer.BlockCopy(m_bufTable.GetBuffer(), 58, fourBytes, 0, 4);
            return fourBytes;
        }
    }

    /************************
     * Utility methods
     */

    public bool isUnicodeRangeBitSet(byte byBit)
    {
        if (byBit > 127)
            throw new ArgumentOutOfRangeException("Valid Bit request are are to 127");

        if (byBit < 32)
            return (ulUnicodeRange1 & 1 << byBit) != 0;
        else if (byBit < 64)
            return (ulUnicodeRange2 & 1 << (byBit - 32)) != 0;
        else if (byBit < 96)
            return (ulUnicodeRange3 & 1 << (byBit - 64)) != 0;
        else
            return (ulUnicodeRange4 & 1 << (byBit - 96)) != 0;
    }

    /************************
     * DataCache class accessor
     */
    public override DataCache GetCache()
    {
        if (m_cache == null)
        {
            m_cache = new OS2_cache(this);
        }
        return m_cache;
    }
}
