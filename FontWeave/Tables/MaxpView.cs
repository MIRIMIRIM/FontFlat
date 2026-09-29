using FontWeave.Core;

namespace FontWeave.Tables;

/// <summary>Version-specific maxp semantics over the sole schema-generated borrowed reader.</summary>
public readonly ref partial struct MaxpView
{
    public ReadStatus Validate(out FontDiagnostic diagnostic)
    {
        if (!IsBound)
            return ReadWire.Fail(ReadStatus.Uninitialized, DiagnosticCode.UnboundView,
                Location.At(DataField.None, 0), out diagnostic);
        int length = Version switch
        {
            MaxpLayout.Version05 => MaxpLayout.Length05,
            MaxpLayout.Version10 => MaxpLayout.Length10,
            _ => 0
        };
        if (length == 0)
            return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedVersion,
                Location.At(DataField.Version, MaxpLayout.VersionAt), out diagnostic, Version);
        if (_data.Length < length)
            return FirstIncomplete(out diagnostic);
        if (_data.Length > length)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.UnexpectedTrailingData,
                Location.At(DataField.Length, (ulong)length), out diagnostic,
                (ulong)_data.Length, (ulong)length);
        if (Version == MaxpLayout.Version10)
            return ValidateValues(ReadValues10(), Location, out diagnostic);
        diagnostic = default;
        return ReadStatus.Done;
    }

    public ReadStatus TryRead(out MaxpTable? model, out FontDiagnostic diagnostic,
        BuildLimits? limits = null)
    {
        model = null;
        var budget = new BuildBudget(limits);
        if (!budget.Input(_data.Length, Location, out diagnostic))
            return ReadStatus.ResourceLimit;
        return TryRead(out model, out diagnostic, budget);
    }

    internal ReadStatus TryRead(out MaxpTable? model, out FontDiagnostic diagnostic,
        BuildBudget budget)
    {
        model = null;
        if (!budget.Work(_data.Length, Location, out diagnostic))
            return ReadStatus.ResourceLimit;
        ReadStatus status = Validate(out diagnostic);
        if (status != ReadStatus.Done) return status;
        _ = TryGetNumGlyphs(out ushort numGlyphs, out _);
        model = Version == MaxpLayout.Version05
            ? new Maxp05Table(numGlyphs, Location)
            : new Maxp10Table(ReadValues10(), Location);
        diagnostic = default;
        return ReadStatus.Done;
    }

    private Maxp10Values ReadValues10()
    {
        _ = TryGetNumGlyphs(out ushort numGlyphs, out _);
        _ = TryGetMaxPoints(out ushort maxPoints, out _);
        _ = TryGetMaxContours(out ushort maxContours, out _);
        _ = TryGetMaxCompositePoints(out ushort maxCompositePoints, out _);
        _ = TryGetMaxCompositeContours(out ushort maxCompositeContours, out _);
        _ = TryGetMaxZones(out ushort maxZones, out _);
        _ = TryGetMaxTwilightPoints(out ushort maxTwilightPoints, out _);
        _ = TryGetMaxStorage(out ushort maxStorage, out _);
        _ = TryGetMaxFunctionDefs(out ushort maxFunctionDefs, out _);
        _ = TryGetMaxInstructionDefs(out ushort maxInstructionDefs, out _);
        _ = TryGetMaxStackElements(out ushort maxStackElements, out _);
        _ = TryGetMaxSizeOfInstructions(out ushort maxSizeOfInstructions, out _);
        _ = TryGetMaxComponentElements(out ushort maxComponentElements, out _);
        _ = TryGetMaxComponentDepth(out ushort maxComponentDepth, out _);
        return new(numGlyphs, maxPoints, maxContours, maxCompositePoints,
            maxCompositeContours, maxZones, maxTwilightPoints, maxStorage,
            maxFunctionDefs, maxInstructionDefs, maxStackElements,
            maxSizeOfInstructions, maxComponentElements, maxComponentDepth);
    }

    private ReadStatus FirstIncomplete(out FontDiagnostic diagnostic)
    {
        ReadStatus status = TryGetNumGlyphs(out _, out diagnostic);
        if (status != ReadStatus.Done || Version == MaxpLayout.Version05) return status;
        if ((status = TryGetMaxPoints(out _, out diagnostic)) != ReadStatus.Done) return status;
        if ((status = TryGetMaxContours(out _, out diagnostic)) != ReadStatus.Done) return status;
        if ((status = TryGetMaxCompositePoints(out _, out diagnostic)) != ReadStatus.Done) return status;
        if ((status = TryGetMaxCompositeContours(out _, out diagnostic)) != ReadStatus.Done) return status;
        if ((status = TryGetMaxZones(out _, out diagnostic)) != ReadStatus.Done) return status;
        if ((status = TryGetMaxTwilightPoints(out _, out diagnostic)) != ReadStatus.Done) return status;
        if ((status = TryGetMaxStorage(out _, out diagnostic)) != ReadStatus.Done) return status;
        if ((status = TryGetMaxFunctionDefs(out _, out diagnostic)) != ReadStatus.Done) return status;
        if ((status = TryGetMaxInstructionDefs(out _, out diagnostic)) != ReadStatus.Done) return status;
        if ((status = TryGetMaxStackElements(out _, out diagnostic)) != ReadStatus.Done) return status;
        if ((status = TryGetMaxSizeOfInstructions(out _, out diagnostic)) != ReadStatus.Done) return status;
        if ((status = TryGetMaxComponentElements(out _, out diagnostic)) != ReadStatus.Done) return status;
        return TryGetMaxComponentDepth(out _, out diagnostic);
    }

    internal static ReadStatus ValidateValues(Maxp10Values values, DataLocation location,
        out FontDiagnostic diagnostic)
    {
        if (values.MaxZones is not (1 or 2))
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                location.At(DataField.MaxZones, MaxpLayout.MaxZonesAt), out diagnostic,
                values.MaxZones, 2);
        diagnostic = default;
        return ReadStatus.Done;
    }

}
