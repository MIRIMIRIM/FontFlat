using FontWeave.Core;

namespace FontWeave.Tables;

public readonly ref partial struct LtshView
{
    /// <summary>Checks stored counts and bounds, not the behavior of glyph instructions.</summary>
    public ReadStatus Validate(LegacyMetricsContext context, out FontDiagnostic diagnostic,
        BuildLimits? limits = null)
    {
        var budget = new BuildBudget(limits);
        ReadStatus status = CheckLayout(context, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (!budget.Input(_data.Length, Location, out diagnostic) ||
            !budget.Work(_data.Length, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        diagnostic = default;
        return ReadStatus.Done;
    }

    public ReadStatus TryRead(LegacyMetricsContext context, out LtshTable? model,
        out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        model = null;
        var budget = new BuildBudget(limits);
        ReadStatus status = CheckLayout(context, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (!budget.Input(_data.Length, Location, out diagnostic) ||
            !budget.Work(_data.Length, Location, out diagnostic) ||
            !budget.Buffer(NumGlyphs, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        var thresholds = new byte[NumGlyphs];
        for (int gid = 0; gid < thresholds.Length; gid++)
        {
            _ = TryGetThreshold(gid, out var record, out _);
            thresholds[gid] = record.YPixels;
        }
        model = new(thresholds, Location, context.MaxpLocation);
        diagnostic = default;
        return ReadStatus.Done;
    }

    private ReadStatus CheckLayout(LegacyMetricsContext context, out FontDiagnostic diagnostic)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!IsBound)
            return ReadWire.Fail(ReadStatus.Uninitialized, DiagnosticCode.UnboundView, Location, out diagnostic);
        if (Location != context.Location)
            return ReadWire.Fail(ReadStatus.InvalidSource, DiagnosticCode.WrongSourceFace, Location, out diagnostic);
        ReadStatus status = TryRequireKnownVersion(out _, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (NumGlyphs != context.NumGlyphs)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.At(DataField.Count, 2), out diagnostic, NumGlyphs, context.NumGlyphs);
        ulong expected = LtshLayout.HeaderLength + (ulong)NumGlyphs;
        if ((ulong)_data.Length < expected)
            return ReadWire.Fail(ReadStatus.Truncated, DiagnosticCode.TruncatedData,
                Location.At(DataField.Length, (ulong)_data.Length), out diagnostic, (ulong)_data.Length, expected);
        if ((ulong)_data.Length > expected)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.UnexpectedTrailingData,
                Location.At(DataField.Length, expected), out diagnostic, (ulong)_data.Length, expected);
        diagnostic = default;
        return ReadStatus.Done;
    }
}
