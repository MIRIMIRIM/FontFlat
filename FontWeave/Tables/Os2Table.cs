using FontWeave.Core;

namespace FontWeave.Tables;

/// <summary>All OS/2 wire values. Null tail members mean that the version has no such field.</summary>
public sealed record Os2TableValues(
    short XAvgCharWidth, ushort WeightClass, ushort WidthClass, ushort FsType,
    short YSubscriptXSize, short YSubscriptYSize, short YSubscriptXOffset,
    short YSubscriptYOffset, short YSuperscriptXSize, short YSuperscriptYSize,
    short YSuperscriptXOffset, short YSuperscriptYOffset, short YStrikeoutSize,
    short YStrikeoutPosition, short SFamilyClass,
    byte Panose0, byte Panose1, byte Panose2, byte Panose3, byte Panose4,
    byte Panose5, byte Panose6, byte Panose7, byte Panose8, byte Panose9,
    uint UnicodeRange1, uint UnicodeRange2, uint UnicodeRange3, uint UnicodeRange4,
    uint VendorId, ushort FsSelection, ushort FirstCharIndex, ushort LastCharIndex,
    short? TypoAscender, short? TypoDescender, short? TypoLineGap,
    ushort? WinAscent, ushort? WinDescent,
    uint? CodePageRange1, uint? CodePageRange2,
    short? XHeight, short? CapHeight, ushort? DefaultChar,
    ushort? BreakChar, ushort? MaxContext,
    ushort? LowerOpticalPointSize, ushort? UpperOpticalPointSize)
{
    public byte[] Panose => new byte[] { Panose0, Panose1, Panose2, Panose3, Panose4,
        Panose5, Panose6, Panose7, Panose8, Panose9 };

    public byte[] VendorBytes => new byte[] { (byte)(VendorId >> 24),
        (byte)((VendorId >> 16) & 0xFF), (byte)((VendorId >> 8) & 0xFF),
        (byte)(VendorId & 0xFF) };
}

/// <summary>Strictly validated OS/2 0–5 layout, including the read-only short version 0.</summary>
public sealed class Os2Table
{
    internal Os2Table(ushort version, int length, Os2TableValues values, DataLocation location)
    {
        Version = version;
        Length = length;
        Values = values;
        Location = location;
    }

    public ushort Version { get; }
    public int Length { get; }
    public Os2TableValues Values { get; }
    public DataLocation Location { get; }

    public ReadStatus TryWith(Os2TableValues values, out Os2Table? model,
        out FontDiagnostic diagnostic)
    {
        model = null;
        ReadStatus status = Os2View.ValidateValues(Version, Length, values, Location, out diagnostic);
        if (status != ReadStatus.Done) return status;
        model = new Os2Table(Version, Length, values, Location);
        return ReadStatus.Done;
    }
}
