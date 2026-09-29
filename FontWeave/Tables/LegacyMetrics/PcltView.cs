using FontWeave.Core;

namespace FontWeave.Tables;

/// <summary>Strict PCLT 1.0 semantics over the schema-generated borrowed reader.</summary>
public readonly ref partial struct PcltView
{
    public ReadStatus Validate(out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        var budget = new BuildBudget(limits);
        return ReadValidatedValues(budget, out _, out diagnostic);
    }

    public ReadStatus TryRead(out PcltTable? model, out FontDiagnostic diagnostic,
        BuildLimits? limits = null)
    {
        model = null;
        var budget = new BuildBudget(limits);
        ReadStatus status = ReadValidatedValues(budget, out PcltValues values, out diagnostic);
        if (status != ReadStatus.Done) return status;
        model = new(values, Location);
        diagnostic = default;
        return ReadStatus.Done;
    }

    private ReadStatus ReadValidatedValues(BuildBudget budget, out PcltValues values,
        out FontDiagnostic diagnostic)
    {
        values = default;
        if (!IsBound)
            return ReadWire.Fail(ReadStatus.Uninitialized, DiagnosticCode.UnboundView,
                Location, out diagnostic);
        if (!budget.Input(_data.Length, Location, out diagnostic) ||
            !budget.Work(_data.Length, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        ReadStatus status = TryRequireKnownVersion(out _, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (_data.Length > PcltLayout.Length)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.UnexpectedTrailingData,
                Location.At(DataField.Length, PcltLayout.Length), out diagnostic,
                (ulong)_data.Length, PcltLayout.Length);
        if (Reserved != 0)
            return Invalid(Location, 53, Reserved, out diagnostic);
        // Two UTF-16 strings are the only owned allocations in this read.
        if (!budget.Buffer(2 * (16 + 6), Location, out diagnostic)) return ReadStatus.ResourceLimit;
        values = ReadValues();
        return ValidateValues(values, Location, out diagnostic);
    }

    private PcltValues ReadValues()
    {
        Span<char> typeface = stackalloc char[] {
            (char)Typeface0, (char)Typeface1, (char)Typeface2, (char)Typeface3,
            (char)Typeface4, (char)Typeface5, (char)Typeface6, (char)Typeface7,
            (char)Typeface8, (char)Typeface9, (char)Typeface10, (char)Typeface11,
            (char)Typeface12, (char)Typeface13, (char)Typeface14, (char)Typeface15
        };
        Span<char> fileName = stackalloc char[] {
            (char)FileName0, (char)FileName1, (char)FileName2,
            (char)FileName3, (char)FileName4, (char)FileName5
        };
        return new(FontNumber, Pitch, XHeight, Style, TypeFamily, CapHeight,
            SymbolSet, new string(typeface),
            ((ulong)CharacterComplementHigh << 32) | CharacterComplementLow,
            new string(fileName), StrokeWeight, WidthType, SerifStyle);
    }

    internal static ReadStatus ValidateValues(PcltValues v, DataLocation location,
        out FontDiagnostic diagnostic)
    {
        if ((v.Style & 0xFC00) != 0 || ((v.Style >> 5) & 0x1F) > 17 ||
            ((v.Style >> 2) & 7) == 5 || (v.Style & 3) == 3)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                location.At(DataField.Flags, 12), out diagnostic, v.Style);
        uint vendor = (uint)v.TypeFamily >> 12;
        if (vendor is < 1 or > 7)
            return Invalid(location, 14, v.TypeFamily, out diagnostic);
        if (v.Typeface is null || v.Typeface.Length != 16)
            return Invalid(location, 20, (ulong)(v.Typeface?.Length ?? 0), out diagnostic);
        for (int i = 0; i < v.Typeface.Length; i++)
            if (v.Typeface[i] > 127)
                return InvalidEncoding(location, 20 + (ulong)i, v.Typeface[i], out diagnostic);
        if (v.FileName is null || v.FileName.Length != 6)
            return Invalid(location, 44, (ulong)(v.FileName?.Length ?? 0), out diagnostic);
        for (int i = 0; i < v.FileName.Length; i++)
            if (v.FileName[i] > 127)
                return InvalidEncoding(location, 44 + (ulong)i, v.FileName[i], out diagnostic);
        if (v.SymbolSet == 0 && v.FileName[4] != '0')
            return Invalid(location, 48, v.FileName[4], out diagnostic);
        if (v.SymbolSet == 0 && v.FileName[5] != '0')
            return Invalid(location, 49, v.FileName[5], out diagnostic);
        if (v.StrokeWeight is < -7 or > 7)
            return Invalid(location, 50, unchecked((byte)v.StrokeWeight), out diagnostic);
        if (v.WidthType is < -5 or > 5)
            return Invalid(location, 51, unchecked((byte)v.WidthType), out diagnostic);
        uint serifKind = (uint)v.SerifStyle >> 6;
        if (serifKind is not (1 or 2) || (v.SerifStyle & 0x3F) > 12)
            return Invalid(location, 52, v.SerifStyle, out diagnostic);
        diagnostic = default;
        return ReadStatus.Done;
    }

    private static ReadStatus Invalid(DataLocation location, ulong offset, ulong value,
        out FontDiagnostic diagnostic) =>
        ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
            location.At(DataField.Payload, offset), out diagnostic, value);

    private static ReadStatus InvalidEncoding(DataLocation location, ulong offset, char value,
        out FontDiagnostic diagnostic) =>
        ReadWire.Fail(ReadStatus.InvalidEncoding, DiagnosticCode.InvalidValue,
            location.At(DataField.Payload, offset), out diagnostic, value, 127);
}
