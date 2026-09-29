using FontWeave.Core;

namespace FontWeave.Tables;

/// <summary>Versioned strict semantics over the single schema-generated OS/2 reader.</summary>
public readonly ref partial struct Os2View
{
    public ReadStatus Validate(out FontDiagnostic diagnostic)
    {
        if (!IsBound)
            return ReadWire.Fail(ReadStatus.Uninitialized, DiagnosticCode.UnboundView,
                Location.At(DataField.None, 0), out diagnostic);
        int expected = Os2Layout.Length(Version, Version == 0 && _data.Length == Os2Layout.ShortLength);
        if (expected == 0)
            return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedVersion,
                Location.At(DataField.Version, 0), out diagnostic, Version);
        if (_data.Length < expected)
            return ReadWire.Fail(ReadStatus.Truncated, DiagnosticCode.TruncatedData,
                Location.At(DataField.Payload, (ulong)_data.Length), out diagnostic,
                (ulong)_data.Length, (ulong)expected);
        if (_data.Length > expected)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.UnexpectedTrailingData,
                Location.At(DataField.Length, (ulong)expected), out diagnostic,
                (ulong)_data.Length, (ulong)expected);
        return ValidateValues(Version, expected, ReadValues(), Location, out diagnostic);
    }

    public ReadStatus TryRead(out Os2Table? model, out FontDiagnostic diagnostic,
        BuildLimits? limits = null)
    {
        model = null;
        var budget = new BuildBudget(limits);
        if (!budget.Input(_data.Length, Location, out diagnostic))
            return ReadStatus.ResourceLimit;
        return TryRead(out model, out diagnostic, budget);
    }

    internal ReadStatus TryRead(out Os2Table? model, out FontDiagnostic diagnostic,
        BuildBudget budget)
    {
        model = null;
        if (!budget.Work(_data.Length, Location, out diagnostic))
            return ReadStatus.ResourceLimit;
        ReadStatus status = Validate(out diagnostic);
        if (status != ReadStatus.Done) return status;
        model = new Os2Table(Version, _data.Length, ReadValues(), Location);
        diagnostic = default;
        return ReadStatus.Done;
    }

    private Os2TableValues ReadValues()
    {
        _ = TryGetXAvgCharWidth(out short avg, out _);
        _ = TryGetWeightClass(out ushort weight, out _);
        _ = TryGetWidthClass(out ushort width, out _);
        _ = TryGetFsType(out ushort fsType, out _);
        _ = TryGetYSubscriptXSize(out short subXSize, out _);
        _ = TryGetYSubscriptYSize(out short subYSize, out _);
        _ = TryGetYSubscriptXOffset(out short subXOffset, out _);
        _ = TryGetYSubscriptYOffset(out short subYOffset, out _);
        _ = TryGetYSuperscriptXSize(out short supXSize, out _);
        _ = TryGetYSuperscriptYSize(out short supYSize, out _);
        _ = TryGetYSuperscriptXOffset(out short supXOffset, out _);
        _ = TryGetYSuperscriptYOffset(out short supYOffset, out _);
        _ = TryGetYStrikeoutSize(out short strikeSize, out _);
        _ = TryGetYStrikeoutPosition(out short strikePosition, out _);
        _ = TryGetSFamilyClass(out short familyClass, out _);
        _ = TryGetPanose0(out byte p0, out _);
        _ = TryGetPanose1(out byte p1, out _);
        _ = TryGetPanose2(out byte p2, out _);
        _ = TryGetPanose3(out byte p3, out _);
        _ = TryGetPanose4(out byte p4, out _);
        _ = TryGetPanose5(out byte p5, out _);
        _ = TryGetPanose6(out byte p6, out _);
        _ = TryGetPanose7(out byte p7, out _);
        _ = TryGetPanose8(out byte p8, out _);
        _ = TryGetPanose9(out byte p9, out _);
        _ = TryGetUnicodeRange1(out uint unicode1, out _);
        _ = TryGetUnicodeRange2(out uint unicode2, out _);
        _ = TryGetUnicodeRange3(out uint unicode3, out _);
        _ = TryGetUnicodeRange4(out uint unicode4, out _);
        _ = TryGetVendorId(out uint vendor, out _);
        _ = TryGetFsSelection(out ushort selection, out _);
        _ = TryGetFirstCharIndex(out ushort firstChar, out _);
        _ = TryGetLastCharIndex(out ushort lastChar, out _);
        short? asc = null, desc = null, gap = null, xHeight = null, capHeight = null;
        ushort? winAsc = null, winDesc = null, defaultChar = null, breakChar = null;
        ushort? maxContext = null, lower = null, upper = null;
        uint? code1 = null, code2 = null;
        if (_data.Length >= Os2Layout.V0Length)
        {
            _ = TryGetTypoAscender(out short vAsc, out _); asc = vAsc;
            _ = TryGetTypoDescender(out short vDesc, out _); desc = vDesc;
            _ = TryGetTypoLineGap(out short vGap, out _); gap = vGap;
            _ = TryGetWinAscent(out ushort vWinAsc, out _); winAsc = vWinAsc;
            _ = TryGetWinDescent(out ushort vWinDesc, out _); winDesc = vWinDesc;
        }
        if (Version >= 1)
        {
            _ = TryGetCodePageRange1(out uint vCode1, out _); code1 = vCode1;
            _ = TryGetCodePageRange2(out uint vCode2, out _); code2 = vCode2;
        }
        if (Version >= 2)
        {
            _ = TryGetXHeight(out short vXHeight, out _); xHeight = vXHeight;
            _ = TryGetCapHeight(out short vCapHeight, out _); capHeight = vCapHeight;
            _ = TryGetDefaultChar(out ushort vDefault, out _); defaultChar = vDefault;
            _ = TryGetBreakChar(out ushort vBreak, out _); breakChar = vBreak;
            _ = TryGetMaxContext(out ushort vContext, out _); maxContext = vContext;
        }
        if (Version >= 5)
        {
            _ = TryGetLowerOpticalPointSize(out ushort vLower, out _); lower = vLower;
            _ = TryGetUpperOpticalPointSize(out ushort vUpper, out _); upper = vUpper;
        }
        return new(avg, weight, width, fsType,
            subXSize, subYSize, subXOffset, subYOffset,
            supXSize, supYSize, supXOffset, supYOffset, strikeSize, strikePosition,
            familyClass, p0, p1, p2, p3, p4, p5, p6, p7, p8, p9,
            unicode1, unicode2, unicode3, unicode4,
            vendor, selection, firstChar, lastChar, asc, desc, gap, winAsc, winDesc,
            code1, code2, xHeight, capHeight, defaultChar, breakChar, maxContext,
            lower, upper);
    }

    internal static ReadStatus ValidateValues(ushort version, int length, Os2TableValues v,
        DataLocation location, out FontDiagnostic diagnostic)
    {
        if (Os2Layout.Length(version, length == Os2Layout.ShortLength) != length)
            return Invalid(location, DataField.Length, 0, (ulong)length, out diagnostic);
        if (v.WeightClass is < 1 or > 1000)
            return Invalid(location, DataField.Payload, 4, v.WeightClass, out diagnostic);
        if (v.WidthClass is < 1 or > 9)
            return Invalid(location, DataField.Payload, 6, v.WidthClass, out diagnostic);
        if ((v.FsType & 1) != 0 ||
            (version >= 2 && (v.FsType & 0xFCF1) != 0) ||
            (version >= 3 && PermissionCount(v.FsType) > 1))
            return Invalid(location, DataField.Flags, 8, v.FsType, out diagnostic);
        ushort allowedSelection = version < 4 ? (ushort)0x007F : (ushort)0x03FF;
        if ((v.FsSelection & ~allowedSelection) != 0 ||
            ((v.FsSelection & 0x40) != 0 && (v.FsSelection & 0x21) != 0))
            return Invalid(location, DataField.Flags, 62, v.FsSelection, out diagnostic);
        if ((v.UnicodeRange4 & 0xF8000000) != 0)
            return Invalid(location, DataField.Payload, 54, v.UnicodeRange4, out diagnostic);
        if (length == Os2Layout.ShortLength)
        {
            if (v.TypoAscender is not null || v.TypoDescender is not null ||
                v.TypoLineGap is not null || v.WinAscent is not null || v.WinDescent is not null)
                return Invalid(location, DataField.Payload, 68, 1, out diagnostic);
        }
        else if (v.TypoAscender is null || v.TypoDescender is null ||
            v.TypoLineGap is null || v.WinAscent is null || v.WinDescent is null)
            return Invalid(location, DataField.Payload, 68, 0, out diagnostic);
        if (version == 0)
        {
            if (v.CodePageRange1 is not null || v.CodePageRange2 is not null)
                return Invalid(location, DataField.Payload, 78, 1, out diagnostic);
        }
        else
        {
            if (v.CodePageRange1 is null || v.CodePageRange2 is null)
                return Invalid(location, DataField.Payload, 78, 0, out diagnostic);
            uint mask1 = version == 1 ? 0xE03F00FF : 0xE03F01FF;
            if ((v.CodePageRange1.Value & ~mask1) != 0)
                return Invalid(location, DataField.Payload, 78, v.CodePageRange1.Value, out diagnostic);
            if ((v.CodePageRange2.Value & 0x0000FFFF) != 0)
                return Invalid(location, DataField.Payload, 82, v.CodePageRange2.Value, out diagnostic);
        }
        if (version < 2)
        {
            if (v.XHeight is not null || v.CapHeight is not null || v.DefaultChar is not null ||
                v.BreakChar is not null || v.MaxContext is not null)
                return Invalid(location, DataField.Payload, 86, 1, out diagnostic);
        }
        else if (v.XHeight is null || v.CapHeight is null || v.DefaultChar is null ||
            v.BreakChar is null || v.MaxContext is null)
            return Invalid(location, DataField.Payload, 86, 0, out diagnostic);
        if (version < 5)
        {
            if (v.LowerOpticalPointSize is not null || v.UpperOpticalPointSize is not null)
                return Invalid(location, DataField.Payload, 96, 1, out diagnostic);
        }
        else
        {
            if (v.LowerOpticalPointSize is null || v.UpperOpticalPointSize is null)
                return Invalid(location, DataField.Payload, 96, 0, out diagnostic);
            ushort lower = v.LowerOpticalPointSize.Value, upper = v.UpperOpticalPointSize.Value;
            if (lower == ushort.MaxValue || upper < 2 || lower >= upper)
                return Invalid(location, DataField.Payload, 96, lower, out diagnostic);
        }
        diagnostic = default;
        return ReadStatus.Done;
    }

    private static int PermissionCount(ushort fsType) =>
        ((fsType >> 1) & 1) + ((fsType >> 2) & 1) + ((fsType >> 3) & 1);

    private static ReadStatus Invalid(DataLocation location, DataField field, ulong offset,
        ulong value, out FontDiagnostic diagnostic) =>
        ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
            location.At(field, offset), out diagnostic, value);
}
