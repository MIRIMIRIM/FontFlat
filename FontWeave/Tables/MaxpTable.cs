using FontWeave.Core;

namespace FontWeave.Tables;

public abstract class MaxpTable
{
    private protected MaxpTable(ushort numGlyphs, DataLocation location, bool hasOriginalGlyphCount = true)
    { NumGlyphs = numGlyphs; Location = location; HasOriginalGlyphCount = hasOriginalGlyphCount; }

    public abstract uint VersionRaw { get; }
    public ushort NumGlyphs { get; }
    public DataLocation Location { get; }
    internal bool HasOriginalGlyphCount { get; }
}

/// <summary>CFF/CFF2 maxp 0.5; no TrueType maxima are present.</summary>
public sealed class Maxp05Table : MaxpTable
{
    internal Maxp05Table(ushort numGlyphs, DataLocation location, bool hasOriginalGlyphCount = true)
        : base(numGlyphs, location, hasOriginalGlyphCount) { }
    public override uint VersionRaw => MaxpLayout.Version05;

    public ReadStatus TryWith(ushort numGlyphs, out Maxp05Table? model,
        out FontDiagnostic diagnostic)
    {
        model = new Maxp05Table(numGlyphs, Location, HasOriginalGlyphCount && numGlyphs == NumGlyphs);
        diagnostic = default;
        return ReadStatus.Done;
    }
}

/// <summary>All thirteen TrueType maxp 1.0 maxima, in wire order.</summary>
public readonly record struct Maxp10Values(
    ushort NumGlyphs, ushort MaxPoints, ushort MaxContours,
    ushort MaxCompositePoints, ushort MaxCompositeContours, ushort MaxZones,
    ushort MaxTwilightPoints, ushort MaxStorage, ushort MaxFunctionDefs,
    ushort MaxInstructionDefs, ushort MaxStackElements,
    ushort MaxSizeOfInstructions, ushort MaxComponentElements,
    ushort MaxComponentDepth);

public sealed class Maxp10Table : MaxpTable
{
    internal Maxp10Table(Maxp10Values values, DataLocation location, bool hasOriginalGlyphCount = true)
        : base(values.NumGlyphs, location, hasOriginalGlyphCount) { Values = values; }
    public override uint VersionRaw => MaxpLayout.Version10;
    public Maxp10Values Values { get; }

    public ReadStatus TryWith(Maxp10Values values, out Maxp10Table? model,
        out FontDiagnostic diagnostic)
    {
        model = null;
        ReadStatus status = MaxpView.ValidateValues(values, Location, out diagnostic);
        if (status != ReadStatus.Done) return status;
        model = new Maxp10Table(values, Location, HasOriginalGlyphCount && values.NumGlyphs == NumGlyphs);
        return ReadStatus.Done;
    }
}
