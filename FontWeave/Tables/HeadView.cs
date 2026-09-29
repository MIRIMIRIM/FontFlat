using FontWeave.Core;

namespace FontWeave.Tables;

/// <summary>Head 1.0 semantics over the sole schema-generated borrowed reader.</summary>
public readonly ref partial struct HeadView
{
    private const uint RequiredMagicNumber = 0x5F0F3CF5;

    public ReadStatus Validate(out FontDiagnostic diagnostic)
    {
        if (!IsBound)
            return ReadWire.Fail(ReadStatus.Uninitialized, DiagnosticCode.UnboundView,
                Location.At(DataField.None, 0), out diagnostic);
        if (Version != HeadLayout.Version)
            return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedVersion,
                Location.At(DataField.Version, HeadLayout.VersionAt), out diagnostic,
                Version, HeadLayout.Version);
        if (_data.Length < HeadLayout.Length)
            return FirstIncomplete(out diagnostic);
        if (_data.Length > HeadLayout.Length)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.UnexpectedTrailingData,
                Location.At(DataField.Length, HeadLayout.Length), out diagnostic,
                (ulong)_data.Length, HeadLayout.Length);

        var values = ReadValues();
        return ValidateValues(values, Location, out diagnostic);
    }

    public ReadStatus TryRead(out HeadTable? model, out FontDiagnostic diagnostic,
        BuildLimits? limits = null)
    {
        model = null;
        var budget = new BuildBudget(limits);
        if (!budget.Input(_data.Length, Location, out diagnostic))
            return ReadStatus.ResourceLimit;
        return TryRead(out model, out diagnostic, budget);
    }

    internal ReadStatus TryRead(out HeadTable? model, out FontDiagnostic diagnostic,
        BuildBudget budget)
    {
        model = null;
        if (!budget.Work(_data.Length, Location, out diagnostic))
            return ReadStatus.ResourceLimit;
        ReadStatus status = Validate(out diagnostic);
        if (status != ReadStatus.Done) return status;
        model = new HeadTable(ReadValues(), Location);
        diagnostic = default;
        return ReadStatus.Done;
    }

    internal HeadTableValues ReadValues() => new(
        GetFontRevision(), GetChecksumAdjustment(), GetMagicNumber(), GetFlags(),
        GetUnitsPerEm(), GetCreated(), GetModified(), GetXMin(), GetYMin(),
        GetXMax(), GetYMax(), GetMacStyle(), GetLowestRecPpem(),
        GetFontDirectionHint(), GetIndexToLocFormat(), GetGlyphDataFormat());

    private int GetFontRevision() { _ = TryGetFontRevision(out int v, out _); return v; }
    private uint GetChecksumAdjustment() { _ = TryGetChecksumAdjustment(out uint v, out _); return v; }
    private uint GetMagicNumber() { _ = TryGetMagicNumber(out uint v, out _); return v; }
    private ushort GetFlags() { _ = TryGetFlags(out ushort v, out _); return v; }
    private ushort GetUnitsPerEm() { _ = TryGetUnitsPerEm(out ushort v, out _); return v; }
    private long GetCreated() { _ = TryGetCreated(out long v, out _); return v; }
    private long GetModified() { _ = TryGetModified(out long v, out _); return v; }
    private short GetXMin() { _ = TryGetXMin(out short v, out _); return v; }
    private short GetYMin() { _ = TryGetYMin(out short v, out _); return v; }
    private short GetXMax() { _ = TryGetXMax(out short v, out _); return v; }
    private short GetYMax() { _ = TryGetYMax(out short v, out _); return v; }
    private ushort GetMacStyle() { _ = TryGetMacStyle(out ushort v, out _); return v; }
    private ushort GetLowestRecPpem() { _ = TryGetLowestRecPpem(out ushort v, out _); return v; }
    private short GetFontDirectionHint() { _ = TryGetFontDirectionHint(out short v, out _); return v; }
    private short GetIndexToLocFormat() { _ = TryGetIndexToLocFormat(out short v, out _); return v; }
    private short GetGlyphDataFormat() { _ = TryGetGlyphDataFormat(out short v, out _); return v; }

    private ReadStatus FirstIncomplete(out FontDiagnostic diagnostic)
    {
        ReadStatus status;
        if ((status = TryGetFontRevision(out _, out diagnostic)) != ReadStatus.Done) return status;
        if ((status = TryGetChecksumAdjustment(out _, out diagnostic)) != ReadStatus.Done) return status;
        if ((status = TryGetMagicNumber(out _, out diagnostic)) != ReadStatus.Done) return status;
        if ((status = TryGetFlags(out _, out diagnostic)) != ReadStatus.Done) return status;
        if ((status = TryGetUnitsPerEm(out _, out diagnostic)) != ReadStatus.Done) return status;
        if ((status = TryGetCreated(out _, out diagnostic)) != ReadStatus.Done) return status;
        if ((status = TryGetModified(out _, out diagnostic)) != ReadStatus.Done) return status;
        if ((status = TryGetXMin(out _, out diagnostic)) != ReadStatus.Done) return status;
        if ((status = TryGetYMin(out _, out diagnostic)) != ReadStatus.Done) return status;
        if ((status = TryGetXMax(out _, out diagnostic)) != ReadStatus.Done) return status;
        if ((status = TryGetYMax(out _, out diagnostic)) != ReadStatus.Done) return status;
        if ((status = TryGetMacStyle(out _, out diagnostic)) != ReadStatus.Done) return status;
        if ((status = TryGetLowestRecPpem(out _, out diagnostic)) != ReadStatus.Done) return status;
        if ((status = TryGetFontDirectionHint(out _, out diagnostic)) != ReadStatus.Done) return status;
        if ((status = TryGetIndexToLocFormat(out _, out diagnostic)) != ReadStatus.Done) return status;
        return TryGetGlyphDataFormat(out _, out diagnostic);
    }

    internal static ReadStatus ValidateValues(HeadTableValues value, DataLocation location,
        out FontDiagnostic diagnostic)
    {
        if (value.MagicNumber != RequiredMagicNumber)
            return Invalid(DataField.MagicNumber, HeadLayout.MagicNumberAt, value.MagicNumber, RequiredMagicNumber, location, out diagnostic);
        if ((value.Flags & 0x87E0) != 0)
            return Invalid(DataField.Flags, HeadLayout.FlagsAt, value.Flags, 0x781F, location, out diagnostic);
        if (value.UnitsPerEm is < 16 or > 16384)
            return Invalid(DataField.UnitsPerEm, HeadLayout.UnitsPerEmAt, value.UnitsPerEm, 16384, location, out diagnostic);
        if ((value.MacStyle & 0xFF80) != 0)
            return Invalid(DataField.MacStyle, HeadLayout.MacStyleAt, value.MacStyle, 0x007F, location, out diagnostic);
        if (value.FontDirectionHint is < -2 or > 2)
            return Invalid(DataField.FontDirectionHint, HeadLayout.FontDirectionHintAt,
                unchecked((ushort)value.FontDirectionHint), 2, location, out diagnostic);
        if (value.IndexToLocFormat is not (0 or 1))
            return Invalid(DataField.IndexToLocFormat, HeadLayout.IndexToLocFormatAt,
                unchecked((ushort)value.IndexToLocFormat), 1, location, out diagnostic);
        if (value.GlyphDataFormat != 0)
            return Invalid(DataField.GlyphDataFormat, HeadLayout.GlyphDataFormatAt,
                unchecked((ushort)value.GlyphDataFormat), 0, location, out diagnostic);
        diagnostic = default;
        return ReadStatus.Done;
    }

    private static ReadStatus Invalid(DataField field, ulong offset, ulong actual,
        ulong expected, DataLocation location, out FontDiagnostic diagnostic) =>
        ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
            location.At(field, offset), out diagnostic, actual, expected);

}
