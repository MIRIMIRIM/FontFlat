using FontWeave.Core;

namespace FontWeave.Tables;

public readonly ref partial struct VheaView
{
    public ReadStatus Validate(out FontDiagnostic diagnostic)
    {
        if (!IsBound)
            return ReadWire.Fail(ReadStatus.Uninitialized, DiagnosticCode.UnboundView,
                Location, out diagnostic);
        ReadStatus status = TryRequireKnownVersion(out _, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (_data.Length != VheaLayout.Length)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.UnexpectedTrailingData,
                Location.At(DataField.Length, VheaLayout.Length), out diagnostic,
                (ulong)_data.Length, VheaLayout.Length);
        return ValidateValues(ReadValues(), Location, out diagnostic);
    }

    public ReadStatus TryRead(out VheaTable? model, out FontDiagnostic diagnostic,
        BuildLimits? limits = null)
    {
        model = null;
        var budget = new BuildBudget(limits);
        if (!budget.Input(_data.Length, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        return TryRead(out model, out diagnostic, budget);
    }

    internal ReadStatus TryRead(out VheaTable? model, out FontDiagnostic diagnostic, BuildBudget budget)
    {
        model = null;
        if (!budget.Work(_data.Length, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        ReadStatus status = Validate(out diagnostic);
        if (status != ReadStatus.Done) return status;
        model = new(ReadValues(), Location);
        return ReadStatus.Done;
    }

    private VheaValues ReadValues() => new(VersionRaw, Ascender, Descender,
        LineGap, AdvanceHeightMax, MinTopSideBearing, MinBottomSideBearing, YMaxExtent,
        CaretSlopeRise, CaretSlopeRun, CaretOffset, Reserved1, Reserved2, Reserved3,
        Reserved4, MetricDataFormat, NumOfLongVerMetrics);

    internal static ReadStatus ValidateValues(VheaValues value, DataLocation at,
        out FontDiagnostic diagnostic)
    {
        if (value.VersionRaw != VheaLayout.Version10 && value.VersionRaw != VheaLayout.Version11)
            return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedVersion,
                at.At(DataField.Version, 0), out diagnostic, value.VersionRaw);
        int bad = value.Reserved1 != 0 ? 24 : value.Reserved2 != 0 ? 26 :
            value.Reserved3 != 0 ? 28 : value.Reserved4 != 0 ? 30 :
            value.MetricDataFormat != 0 ? 32 : value.NumOfLongVerMetrics == 0 ? 34 :
            value.VersionRaw == VheaLayout.Version10 && value.LineGap != 0 ? 8 : -1;
        if (bad >= 0)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                at.At(bad == 34 ? DataField.Count : DataField.Payload, (ulong)bad), out diagnostic);
        diagnostic = default;
        return ReadStatus.Done;
    }
}
