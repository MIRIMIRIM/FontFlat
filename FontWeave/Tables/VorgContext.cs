using FontWeave.Core;

namespace FontWeave.Tables;

/// <summary>The glyph-count boundary for VORG, explicitly bound to its source face.</summary>
public sealed class VorgContext
{
    private VorgContext(ushort numGlyphs, DataLocation location, DataLocation maxpLocation)
    { NumGlyphs = numGlyphs; Location = location; MaxpLocation = maxpLocation; }

    public ushort NumGlyphs { get; }
    public DataLocation Location { get; }
    public DataLocation MaxpLocation { get; }

    /// <summary>Anonymous wire fixtures only; source-bound reads require the maxp overload.</summary>
    public static ReadStatus TryCreate(int numGlyphs, out VorgContext? context,
        out FontDiagnostic diagnostic, DataLocation location = default)
    {
        context = null;
        location = location == default ? DataLocation.Anonymous(VorgLayout.Tag) : location;
        if (location.SourceId != 0 || location.TableTag != VorgLayout.Tag)
            return ReadWire.Fail(ReadStatus.InvalidSource, DiagnosticCode.WrongSourceFace,
                location, out diagnostic);
        return Create(numGlyphs, location, default, out context, out diagnostic);
    }

    public static ReadStatus TryCreate(MaxpTable maxp, DataLocation location,
        out VorgContext? context, out FontDiagnostic diagnostic)
    {
        ArgumentNullException.ThrowIfNull(maxp);
        context = null;
        if (!maxp.HasOriginalGlyphCount || location.SourceId == 0 ||
            location.TableTag != VorgLayout.Tag || maxp.Location.TableTag != 0x6D617870 ||
            location.SourceId != maxp.Location.SourceId || location.FaceIndex != maxp.Location.FaceIndex)
            return ReadWire.Fail(ReadStatus.InvalidSource, DiagnosticCode.WrongSourceFace,
                location, out diagnostic);
        return Create(maxp.NumGlyphs, location, maxp.Location, out context, out diagnostic);
    }

    private static ReadStatus Create(int numGlyphs, DataLocation location, DataLocation maxpLocation,
        out VorgContext? context, out FontDiagnostic diagnostic)
    {
        context = null;
        if (numGlyphs is < 1 or > ushort.MaxValue)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                location.At(DataField.Count, 0), out diagnostic, unchecked((ulong)(long)numGlyphs));
        context = new((ushort)numGlyphs, location, maxpLocation);
        diagnostic = default;
        return ReadStatus.Done;
    }
}
