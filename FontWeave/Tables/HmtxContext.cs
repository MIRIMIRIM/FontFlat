using FontWeave.Core;

namespace FontWeave.Tables;

/// <summary>Explicit G/N and provenance, not a reader and not an owner of table bytes.</summary>
public sealed class HmtxContext
{
    private HmtxContext(ushort numGlyphs, ushort numberOfHMetrics, DataLocation location,
        DataLocation hheaLocation, DataLocation maxpLocation)
    {
        NumGlyphs = numGlyphs; NumberOfHMetrics = numberOfHMetrics; Location = location;
        HheaLocation = hheaLocation; MaxpLocation = maxpLocation;
    }

    public ushort NumGlyphs { get; }
    public ushort NumberOfHMetrics { get; }
    public DataLocation Location { get; }
    public DataLocation HheaLocation { get; }
    public DataLocation MaxpLocation { get; }

    /// <summary>Anonymous wire counts. Source-bound reads must use the model overload.</summary>
    public static ReadStatus TryCreate(int numGlyphs, int numberOfHMetrics,
        out HmtxContext? context, out FontDiagnostic diagnostic, DataLocation location = default)
    {
        context = null;
        location = location == default ? DataLocation.Anonymous(HmtxLayout.Tag) : location;
        if (location.SourceId != 0)
            return ReadWire.Fail(ReadStatus.InvalidSource, DiagnosticCode.WrongSourceFace,
                location, out diagnostic);
        return Create(numGlyphs, numberOfHMetrics, location, default, default, out context, out diagnostic);
    }

    /// <summary>Counts must originate in the same immutable source face as hmtx.</summary>
    public static ReadStatus TryCreate(HheaTable hhea, MaxpTable maxp, DataLocation location,
        out HmtxContext? context, out FontDiagnostic diagnostic)
    {
        ArgumentNullException.ThrowIfNull(hhea);
        ArgumentNullException.ThrowIfNull(maxp);
        context = null;
        if (!hhea.HasOriginalMetricCount || !maxp.HasOriginalGlyphCount ||
            location.SourceId == 0 || location.TableTag != HmtxLayout.Tag ||
            hhea.Location.TableTag != HheaLayout.Tag || maxp.Location.TableTag != 0x6D617870 ||
            !SameFace(location, hhea.Location) || !SameFace(location, maxp.Location))
            return ReadWire.Fail(ReadStatus.InvalidSource, DiagnosticCode.WrongSourceFace,
                location, out diagnostic);
        return Create(maxp.NumGlyphs, hhea.Values.NumberOfHMetrics, location,
            hhea.Location, maxp.Location, out context, out diagnostic);
    }

    private static ReadStatus Create(int g, int n, DataLocation location,
        DataLocation hheaLocation, DataLocation maxpLocation, out HmtxContext? context,
        out FontDiagnostic diagnostic)
    {
        context = null;
        if (g is < 1 or > ushort.MaxValue || n < 1 || n > g)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                location.At(DataField.Count, 0), out diagnostic,
                unchecked((ulong)(long)n), unchecked((ulong)(long)g));
        context = new((ushort)g, (ushort)n, location, hheaLocation, maxpLocation);
        diagnostic = default;
        return ReadStatus.Done;
    }

    internal static bool SameFace(DataLocation left, DataLocation right) =>
        left.SourceId == right.SourceId && left.FaceIndex == right.FaceIndex;
}
