using FontWeave.Core;

namespace FontWeave.Tables;

public readonly ref partial struct HheaView
{
    public ReadStatus Validate(out FontDiagnostic diagnostic)
    {
        if (!IsBound)
            return ReadWire.Fail(ReadStatus.Uninitialized, DiagnosticCode.UnboundView,
                Location, out diagnostic);
        ReadStatus status = TryRequireKnownVersion(out _, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (_data.Length != HheaLayout.Length)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.UnexpectedTrailingData,
                Location.At(DataField.Length, HheaLayout.Length), out diagnostic,
                (ulong)_data.Length, HheaLayout.Length);
        return ValidateValues(ReadValues(), Location, out diagnostic);
    }

    public ReadStatus TryRead(out HheaTable? model, out FontDiagnostic diagnostic,
        BuildLimits? limits = null)
    {
        model = null;
        var budget = new BuildBudget(limits);
        if (!budget.Input(_data.Length, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        return TryRead(out model, out diagnostic, budget);
    }

    internal ReadStatus TryRead(out HheaTable? model, out FontDiagnostic diagnostic, BuildBudget budget)
    {
        model = null;
        if (!budget.Work(_data.Length, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        ReadStatus status = Validate(out diagnostic);
        if (status != ReadStatus.Done) return status;
        model = new(ReadValues(), Location);
        return ReadStatus.Done;
    }

    private HheaValues ReadValues() => new(MajorVersion, MinorVersion, Ascender, Descender,
        LineGap, AdvanceWidthMax, MinLeftSideBearing, MinRightSideBearing, XMaxExtent,
        CaretSlopeRise, CaretSlopeRun, CaretOffset, Reserved1, Reserved2, Reserved3,
        Reserved4, MetricDataFormat, NumberOfHMetrics);

    internal static ReadStatus ValidateValues(HheaValues value, DataLocation at,
        out FontDiagnostic diagnostic)
    {
        if (value.MajorVersion != 1 || value.MinorVersion != 0)
            return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedVersion,
                at.At(DataField.Version, value.MajorVersion != 1 ? 0UL : 2UL), out diagnostic,
                value.MajorVersion != 1 ? value.MajorVersion : value.MinorVersion);
        int bad = value.Reserved1 != 0 ? 24 : value.Reserved2 != 0 ? 26 :
            value.Reserved3 != 0 ? 28 : value.Reserved4 != 0 ? 30 :
            value.MetricDataFormat != 0 ? 32 : value.NumberOfHMetrics == 0 ? 34 : -1;
        if (bad >= 0)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                at.At(bad == 34 ? DataField.Count : DataField.Payload, (ulong)bad), out diagnostic);
        diagnostic = default;
        return ReadStatus.Done;
    }
}
