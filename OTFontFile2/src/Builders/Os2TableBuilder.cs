using OTFontFile2.SourceGen;

namespace OTFontFile2.Tables;

/// <summary>
/// Mutable builder for the OpenType <c>OS/2</c> table.
/// </summary>
[OtTableBuilder("OS/2")]
public sealed partial class Os2TableBuilder : ISfntTableSource
{
    private ushort _version = 4;
    private short _xAvgCharWidth;
    private ushort _usWeightClass;
    private ushort _usWidthClass;
    private ushort _fsType;
    private short _ySubscriptXSize;
    private short _ySubscriptYSize;
    private short _ySubscriptXOffset;
    private short _ySubscriptYOffset;
    private short _ySuperscriptXSize;
    private short _ySuperscriptYSize;
    private short _ySuperscriptXOffset;
    private short _ySuperscriptYOffset;
    private short _yStrikeoutSize;
    private short _yStrikeoutPosition;
    private short _sFamilyClass;
    private readonly byte[] _panose = new byte[10];
    private uint _ulUnicodeRange1;
    private uint _ulUnicodeRange2;
    private uint _ulUnicodeRange3;
    private uint _ulUnicodeRange4;
    private Tag _achVendId;
    private ushort _fsSelection;
    private ushort _usFirstCharIndex;
    private ushort _usLastCharIndex;
    private short _sTypoAscender;
    private short _sTypoDescender;
    private short _sTypoLineGap;
    private ushort _usWinAscent;
    private ushort _usWinDescent;

    private uint _ulCodePageRange1;
    private uint _ulCodePageRange2;

    private short _sxHeight;
    private short _sCapHeight;
    private ushort _usDefaultChar;
    private ushort _usBreakChar;
    private ushort _usMaxContext;

    private ushort _usLowerOpticalPointSize;
    private ushort _usUpperOpticalPointSize;

    public ushort Version
    {
        get => _version;
        set
        {
            if (value > 5)
                throw new ArgumentOutOfRangeException(nameof(value), "OS/2 version must be in the range 0..5.");

            if (value == _version)
                return;

            _version = value;
            MarkDirty();
        }
    }

    public short XAvgCharWidth
    {
        get => _xAvgCharWidth;
        set
        {
            if (value == _xAvgCharWidth)
                return;

            _xAvgCharWidth = value;
            MarkDirty();
        }
    }

    public ushort UsWeightClass
    {
        get => _usWeightClass;
        set
        {
            if (value == _usWeightClass)
                return;

            _usWeightClass = value;
            MarkDirty();
        }
    }

    public ushort UsWidthClass
    {
        get => _usWidthClass;
        set
        {
            if (value == _usWidthClass)
                return;

            _usWidthClass = value;
            MarkDirty();
        }
    }

    public ushort FsType
    {
        get => _fsType;
        set
        {
            if (value == _fsType)
                return;

            _fsType = value;
            MarkDirty();
        }
    }

    public short YSubscriptXSize
    {
        get => _ySubscriptXSize;
        set
        {
            if (value == _ySubscriptXSize)
                return;

            _ySubscriptXSize = value;
            MarkDirty();
        }
    }

    public short YSubscriptYSize
    {
        get => _ySubscriptYSize;
        set
        {
            if (value == _ySubscriptYSize)
                return;

            _ySubscriptYSize = value;
            MarkDirty();
        }
    }

    public short YSubscriptXOffset
    {
        get => _ySubscriptXOffset;
        set
        {
            if (value == _ySubscriptXOffset)
                return;

            _ySubscriptXOffset = value;
            MarkDirty();
        }
    }

    public short YSubscriptYOffset
    {
        get => _ySubscriptYOffset;
        set
        {
            if (value == _ySubscriptYOffset)
                return;

            _ySubscriptYOffset = value;
            MarkDirty();
        }
    }

    public short YSuperscriptXSize
    {
        get => _ySuperscriptXSize;
        set
        {
            if (value == _ySuperscriptXSize)
                return;

            _ySuperscriptXSize = value;
            MarkDirty();
        }
    }

    public short YSuperscriptYSize
    {
        get => _ySuperscriptYSize;
        set
        {
            if (value == _ySuperscriptYSize)
                return;

            _ySuperscriptYSize = value;
            MarkDirty();
        }
    }

    public short YSuperscriptXOffset
    {
        get => _ySuperscriptXOffset;
        set
        {
            if (value == _ySuperscriptXOffset)
                return;

            _ySuperscriptXOffset = value;
            MarkDirty();
        }
    }

    public short YSuperscriptYOffset
    {
        get => _ySuperscriptYOffset;
        set
        {
            if (value == _ySuperscriptYOffset)
                return;

            _ySuperscriptYOffset = value;
            MarkDirty();
        }
    }

    public short YStrikeoutSize
    {
        get => _yStrikeoutSize;
        set
        {
            if (value == _yStrikeoutSize)
                return;

            _yStrikeoutSize = value;
            MarkDirty();
        }
    }

    public short YStrikeoutPosition
    {
        get => _yStrikeoutPosition;
        set
        {
            if (value == _yStrikeoutPosition)
                return;

            _yStrikeoutPosition = value;
            MarkDirty();
        }
    }

    public short SFamilyClass
    {
        get => _sFamilyClass;
        set
        {
            if (value == _sFamilyClass)
                return;

            _sFamilyClass = value;
            MarkDirty();
        }
    }

    public Tag AchVendId
    {
        get => _achVendId;
        set
        {
            if (value == _achVendId)
                return;

            _achVendId = value;
            MarkDirty();
        }
    }

    public uint UlUnicodeRange1
    {
        get => _ulUnicodeRange1;
        set
        {
            if (value == _ulUnicodeRange1)
                return;

            _ulUnicodeRange1 = value;
            MarkDirty();
        }
    }

    public uint UlUnicodeRange2
    {
        get => _ulUnicodeRange2;
        set
        {
            if (value == _ulUnicodeRange2)
                return;

            _ulUnicodeRange2 = value;
            MarkDirty();
        }
    }

    public uint UlUnicodeRange3
    {
        get => _ulUnicodeRange3;
        set
        {
            if (value == _ulUnicodeRange3)
                return;

            _ulUnicodeRange3 = value;
            MarkDirty();
        }
    }

    public uint UlUnicodeRange4
    {
        get => _ulUnicodeRange4;
        set
        {
            if (value == _ulUnicodeRange4)
                return;

            _ulUnicodeRange4 = value;
            MarkDirty();
        }
    }

    public ushort FsSelection
    {
        get => _fsSelection;
        set
        {
            if (value == _fsSelection)
                return;

            _fsSelection = value;
            MarkDirty();
        }
    }

    public ushort UsFirstCharIndex
    {
        get => _usFirstCharIndex;
        set
        {
            if (value == _usFirstCharIndex)
                return;

            _usFirstCharIndex = value;
            MarkDirty();
        }
    }

    public ushort UsLastCharIndex
    {
        get => _usLastCharIndex;
        set
        {
            if (value == _usLastCharIndex)
                return;

            _usLastCharIndex = value;
            MarkDirty();
        }
    }

    public short STypoAscender
    {
        get => _sTypoAscender;
        set
        {
            if (value == _sTypoAscender)
                return;

            _sTypoAscender = value;
            MarkDirty();
        }
    }

    public short STypoDescender
    {
        get => _sTypoDescender;
        set
        {
            if (value == _sTypoDescender)
                return;

            _sTypoDescender = value;
            MarkDirty();
        }
    }

    public short STypoLineGap
    {
        get => _sTypoLineGap;
        set
        {
            if (value == _sTypoLineGap)
                return;

            _sTypoLineGap = value;
            MarkDirty();
        }
    }

    public ushort UsWinAscent
    {
        get => _usWinAscent;
        set
        {
            if (value == _usWinAscent)
                return;

            _usWinAscent = value;
            MarkDirty();
        }
    }

    public ushort UsWinDescent
    {
        get => _usWinDescent;
        set
        {
            if (value == _usWinDescent)
                return;

            _usWinDescent = value;
            MarkDirty();
        }
    }

    public uint UlCodePageRange1
    {
        get => _ulCodePageRange1;
        set
        {
            if (value == _ulCodePageRange1)
                return;

            _ulCodePageRange1 = value;
            MarkDirty();
        }
    }

    public uint UlCodePageRange2
    {
        get => _ulCodePageRange2;
        set
        {
            if (value == _ulCodePageRange2)
                return;

            _ulCodePageRange2 = value;
            MarkDirty();
        }
    }

    public short SxHeight
    {
        get => _sxHeight;
        set
        {
            if (value == _sxHeight)
                return;

            _sxHeight = value;
            MarkDirty();
        }
    }

    public short SCapHeight
    {
        get => _sCapHeight;
        set
        {
            if (value == _sCapHeight)
                return;

            _sCapHeight = value;
            MarkDirty();
        }
    }

    public ushort UsDefaultChar
    {
        get => _usDefaultChar;
        set
        {
            if (value == _usDefaultChar)
                return;

            _usDefaultChar = value;
            MarkDirty();
        }
    }

    public ushort UsBreakChar
    {
        get => _usBreakChar;
        set
        {
            if (value == _usBreakChar)
                return;

            _usBreakChar = value;
            MarkDirty();
        }
    }

    public ushort UsMaxContext
    {
        get => _usMaxContext;
        set
        {
            if (value == _usMaxContext)
                return;

            _usMaxContext = value;
            MarkDirty();
        }
    }

    public ushort UsLowerOpticalPointSize
    {
        get => _usLowerOpticalPointSize;
        set
        {
            if (value == _usLowerOpticalPointSize)
                return;

            _usLowerOpticalPointSize = value;
            MarkDirty();
        }
    }

    public ushort UsUpperOpticalPointSize
    {
        get => _usUpperOpticalPointSize;
        set
        {
            if (value == _usUpperOpticalPointSize)
                return;

            _usUpperOpticalPointSize = value;
            MarkDirty();
        }
    }

    public void SetPanose(ReadOnlySpan<byte> panose)
    {
        if (panose.Length != 10)
            throw new ArgumentOutOfRangeException(nameof(panose), "Panose must be exactly 10 bytes.");

        panose.CopyTo(_panose);
        MarkDirty();
    }

    public byte GetPanoseByte(int index)
    {
        if ((uint)index >= 10u)
            throw new ArgumentOutOfRangeException(nameof(index));

        return _panose[index];
    }

    public void SetPanoseByte(int index, byte value)
    {
        if ((uint)index >= 10u)
            throw new ArgumentOutOfRangeException(nameof(index));

        if (_panose[index] == value)
            return;

        _panose[index] = value;
        MarkDirty();
    }

    public static bool TryFrom(Os2Table os2, out Os2TableBuilder builder)
    {
        builder = null!;

        ushort version = os2.Version;
        if (version > 5)
            return false;

        int requiredLen = GetExpectedLength(version);
        if (os2.Table.Length < requiredLen)
            return false;

        var b = new Os2TableBuilder
        {
            Version = version,
            XAvgCharWidth = os2.XAvgCharWidth,
            UsWeightClass = os2.UsWeightClass,
            UsWidthClass = os2.UsWidthClass,
            FsType = os2.FsType,
            YSubscriptXSize = os2.YSubscriptXSize,
            YSubscriptYSize = os2.YSubscriptYSize,
            YSubscriptXOffset = os2.YSubscriptXOffset,
            YSubscriptYOffset = os2.YSubscriptYOffset,
            YSuperscriptXSize = os2.YSuperscriptXSize,
            YSuperscriptYSize = os2.YSuperscriptYSize,
            YSuperscriptXOffset = os2.YSuperscriptXOffset,
            YSuperscriptYOffset = os2.YSuperscriptYOffset,
            YStrikeoutSize = os2.YStrikeoutSize,
            YStrikeoutPosition = os2.YStrikeoutPosition,
            SFamilyClass = os2.SFamilyClass,
            UlUnicodeRange1 = os2.UlUnicodeRange1,
            UlUnicodeRange2 = os2.UlUnicodeRange2,
            UlUnicodeRange3 = os2.UlUnicodeRange3,
            UlUnicodeRange4 = os2.UlUnicodeRange4,
            AchVendId = os2.AchVendId,
            FsSelection = os2.FsSelection,
            UsFirstCharIndex = os2.UsFirstCharIndex,
            UsLastCharIndex = os2.UsLastCharIndex,
            STypoAscender = os2.STypoAscender,
            STypoDescender = os2.STypoDescender,
            STypoLineGap = os2.STypoLineGap,
            UsWinAscent = os2.UsWinAscent,
            UsWinDescent = os2.UsWinDescent
        };

        b._panose[0] = os2.Panose1;
        b._panose[1] = os2.Panose2;
        b._panose[2] = os2.Panose3;
        b._panose[3] = os2.Panose4;
        b._panose[4] = os2.Panose5;
        b._panose[5] = os2.Panose6;
        b._panose[6] = os2.Panose7;
        b._panose[7] = os2.Panose8;
        b._panose[8] = os2.Panose9;
        b._panose[9] = os2.Panose10;

        if (version >= 1)
        {
            if (!os2.TryGetCodePageRanges(out uint r1, out uint r2))
                return false;

            b.UlCodePageRange1 = r1;
            b.UlCodePageRange2 = r2;
        }

        if (version >= 2)
        {
            if (!os2.TryGetVersion2Fields(out short sxHeight, out short sCapHeight, out ushort usDefaultChar, out ushort usBreakChar, out ushort usMaxContext))
                return false;

            b.SxHeight = sxHeight;
            b.SCapHeight = sCapHeight;
            b.UsDefaultChar = usDefaultChar;
            b.UsBreakChar = usBreakChar;
            b.UsMaxContext = usMaxContext;
        }

        if (version >= 5)
        {
            var span = os2.Table.Span;
            b.UsLowerOpticalPointSize = BigEndian.ReadUInt16(span, 96);
            b.UsUpperOpticalPointSize = BigEndian.ReadUInt16(span, 98);
        }

        b.MarkDirty();
        builder = b;
        return true;
    }

    private byte[] BuildTable()
    {
        int length = GetExpectedLength(Version);
        byte[] table = new byte[length];
        var span = table.AsSpan();

        BigEndian.WriteUInt16(span, 0, Version);
        BigEndian.WriteInt16(span, 2, XAvgCharWidth);
        BigEndian.WriteUInt16(span, 4, UsWeightClass);
        BigEndian.WriteUInt16(span, 6, UsWidthClass);
        BigEndian.WriteUInt16(span, 8, FsType);
        BigEndian.WriteInt16(span, 10, YSubscriptXSize);
        BigEndian.WriteInt16(span, 12, YSubscriptYSize);
        BigEndian.WriteInt16(span, 14, YSubscriptXOffset);
        BigEndian.WriteInt16(span, 16, YSubscriptYOffset);
        BigEndian.WriteInt16(span, 18, YSuperscriptXSize);
        BigEndian.WriteInt16(span, 20, YSuperscriptYSize);
        BigEndian.WriteInt16(span, 22, YSuperscriptXOffset);
        BigEndian.WriteInt16(span, 24, YSuperscriptYOffset);
        BigEndian.WriteInt16(span, 26, YStrikeoutSize);
        BigEndian.WriteInt16(span, 28, YStrikeoutPosition);
        BigEndian.WriteInt16(span, 30, SFamilyClass);
        _panose.AsSpan().CopyTo(span.Slice(32, 10));
        BigEndian.WriteUInt32(span, 42, UlUnicodeRange1);
        BigEndian.WriteUInt32(span, 46, UlUnicodeRange2);
        BigEndian.WriteUInt32(span, 50, UlUnicodeRange3);
        BigEndian.WriteUInt32(span, 54, UlUnicodeRange4);
        BigEndian.WriteUInt32(span, 58, AchVendId.Value);
        BigEndian.WriteUInt16(span, 62, FsSelection);
        BigEndian.WriteUInt16(span, 64, UsFirstCharIndex);
        BigEndian.WriteUInt16(span, 66, UsLastCharIndex);
        BigEndian.WriteInt16(span, 68, STypoAscender);
        BigEndian.WriteInt16(span, 70, STypoDescender);
        BigEndian.WriteInt16(span, 72, STypoLineGap);
        BigEndian.WriteUInt16(span, 74, UsWinAscent);
        BigEndian.WriteUInt16(span, 76, UsWinDescent);

        if (Version >= 1)
        {
            BigEndian.WriteUInt32(span, 78, UlCodePageRange1);
            BigEndian.WriteUInt32(span, 82, UlCodePageRange2);
        }

        if (Version >= 2)
        {
            BigEndian.WriteInt16(span, 86, SxHeight);
            BigEndian.WriteInt16(span, 88, SCapHeight);
            BigEndian.WriteUInt16(span, 90, UsDefaultChar);
            BigEndian.WriteUInt16(span, 92, UsBreakChar);
            BigEndian.WriteUInt16(span, 94, UsMaxContext);
        }

        if (Version >= 5)
        {
            BigEndian.WriteUInt16(span, 96, UsLowerOpticalPointSize);
            BigEndian.WriteUInt16(span, 98, UsUpperOpticalPointSize);
        }

        return table;
    }

    private static int GetExpectedLength(ushort version)
    {
        return version switch
        {
            0 => 78,
            1 => 86,
            2 or 3 or 4 => 96,
            5 => 100,
            _ => throw new ArgumentOutOfRangeException(nameof(version), "OS/2 version must be in the range 0..5.")
        };
    }
}
