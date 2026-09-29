using FontWeave.Core;

namespace FontWeave.Tables;

/// <summary>Explicit G/N and provenance, not a reader and not an owner of table bytes.</summary>
public sealed class VmtxContext
{
    private VmtxContext(ushort numGlyphs, ushort numOfLongVerMetrics, DataLocation location,
        DataLocation vheaLocation, DataLocation maxpLocation)
    {
        NumGlyphs = numGlyphs; NumOfLongVerMetrics = numOfLongVerMetrics; Location = location;
        VheaLocation = vheaLocation; MaxpLocation = maxpLocation;
    }

    public ushort NumGlyphs { get; }
    public ushort NumOfLongVerMetrics { get; }
    public DataLocation Location { get; }
    public DataLocation VheaLocation { get; }
    public DataLocation MaxpLocation { get; }

    /// <summary>Anonymous wire counts. Source-bound reads must use the model overload.</summary>
    public static ReadStatus TryCreate(int numGlyphs, int numOfLongVerMetrics,
        out VmtxContext? context, out FontDiagnostic diagnostic, DataLocation location = default)
    {
        context = null;
        location = location == default ? DataLocation.Anonymous(VmtxLayout.Tag) : location;
        if (location.SourceId != 0)
            return ReadWire.Fail(ReadStatus.InvalidSource, DiagnosticCode.WrongSourceFace,
                location, out diagnostic);
        return Create(numGlyphs, numOfLongVerMetrics, location, default, default, out context, out diagnostic);
    }

    /// <summary>Counts must originate in the same immutable source face as vmtx.</summary>
    public static ReadStatus TryCreate(VheaTable vhea, MaxpTable maxp, DataLocation location,
        out VmtxContext? context, out FontDiagnostic diagnostic)
    {
        ArgumentNullException.ThrowIfNull(vhea);
        ArgumentNullException.ThrowIfNull(maxp);
        context = null;
        if (!vhea.HasOriginalMetricCount || !maxp.HasOriginalGlyphCount ||
            location.SourceId == 0 || location.TableTag != VmtxLayout.Tag ||
            vhea.Location.TableTag != VheaLayout.Tag || maxp.Location.TableTag != 0x6D617870 ||
            !SameFace(location, vhea.Location) || !SameFace(location, maxp.Location))
            return ReadWire.Fail(ReadStatus.InvalidSource, DiagnosticCode.WrongSourceFace,
                location, out diagnostic);
        return Create(maxp.NumGlyphs, vhea.Values.NumOfLongVerMetrics, location,
            vhea.Location, maxp.Location, out context, out diagnostic);
    }

    private static ReadStatus Create(int g, int n, DataLocation location,
        DataLocation vheaLocation, DataLocation maxpLocation, out VmtxContext? context,
        out FontDiagnostic diagnostic)
    {
        context = null;
        if (g is < 1 or > ushort.MaxValue || n < 1 || n > g)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                location.At(DataField.Count, 0), out diagnostic,
                unchecked((ulong)(long)n), unchecked((ulong)(long)g));
        context = new((ushort)g, (ushort)n, location, vheaLocation, maxpLocation);
        diagnostic = default;
        return ReadStatus.Done;
    }

    internal static bool SameFace(DataLocation left, DataLocation right) =>
        left.SourceId == right.SourceId && left.FaceIndex == right.FaceIndex;
}
