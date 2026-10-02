using FontWeave.Core;

namespace FontWeave.Tables;

/// <summary>External glyph count and loca format plus provenance, not a reader and
/// not an owner of table bytes. Both values must originate in the same immutable
/// source face as the loca/glyf bytes being read.</summary>
public sealed class LocaContext
{
    private LocaContext(ushort numGlyphs, bool longFormat, DataLocation location,
        DataLocation headLocation, DataLocation maxpLocation)
    {
        NumGlyphs = numGlyphs; LongFormat = longFormat; Location = location;
        HeadLocation = headLocation; MaxpLocation = maxpLocation;
    }

    public ushort NumGlyphs { get; }
    public bool LongFormat { get; }
    public DataLocation Location { get; }
    public DataLocation HeadLocation { get; }
    public DataLocation MaxpLocation { get; }

    /// <summary>Anonymous wire counts. Source-bound reads must use the model overload.</summary>
    public static ReadStatus TryCreate(int numGlyphs, bool longFormat,
        out LocaContext? context, out FontDiagnostic diagnostic, DataLocation location = default)
    {
        context = null;
        location = location == default ? DataLocation.Anonymous(LocaLayout.Tag) : location;
        if (location.SourceId != 0)
            return ReadWire.Fail(ReadStatus.InvalidSource, DiagnosticCode.WrongSourceFace,
                location, out diagnostic);
        return Create(numGlyphs, longFormat, location, default, default, out context, out diagnostic);
    }

    /// <summary>Count and format must originate in the same immutable source face as loca.</summary>
    public static ReadStatus TryCreate(HeadTable head, MaxpTable maxp, DataLocation location,
        out LocaContext? context, out FontDiagnostic diagnostic)
    {
        ArgumentNullException.ThrowIfNull(head);
        ArgumentNullException.ThrowIfNull(maxp);
        context = null;
        if (!head.HasOriginalIndexToLocFormat || !maxp.HasOriginalGlyphCount ||
            location.SourceId == 0 || location.TableTag != LocaLayout.Tag ||
            head.Location.TableTag != 0x68656164 || maxp.Location.TableTag != 0x6D617870 ||
            !SameFace(location, head.Location) || !SameFace(location, maxp.Location))
            return ReadWire.Fail(ReadStatus.InvalidSource, DiagnosticCode.WrongSourceFace,
                location, out diagnostic);
        return Create(maxp.NumGlyphs, head.IndexToLocFormat != 0, location,
            head.Location, maxp.Location, out context, out diagnostic);
    }

    private static ReadStatus Create(int numGlyphs, bool longFormat, DataLocation location,
        DataLocation headLocation, DataLocation maxpLocation, out LocaContext? context,
        out FontDiagnostic diagnostic)
    {
        context = null;
        if (numGlyphs is < 1 or > ushort.MaxValue)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                location.At(DataField.Count, 0), out diagnostic,
                unchecked((ulong)(long)numGlyphs), ushort.MaxValue);
        context = new((ushort)numGlyphs, longFormat, location, headLocation, maxpLocation);
        diagnostic = default;
        return ReadStatus.Done;
    }

    internal static bool SameFace(DataLocation left, DataLocation right) =>
        left.SourceId == right.SourceId && left.FaceIndex == right.FaceIndex;
}
