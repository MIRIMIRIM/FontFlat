using FontWeave.Core;

namespace FontWeave.Tables;

/// <summary>Explicit glyph count and provenance for hdmx/LTSH, not an owner of table bytes.</summary>
public sealed class LegacyMetricsContext
{
    private LegacyMetricsContext(ushort numGlyphs, DataLocation location, DataLocation maxpLocation)
    { NumGlyphs = numGlyphs; Location = location; MaxpLocation = maxpLocation; }

    public ushort NumGlyphs { get; }
    public DataLocation Location { get; }
    public DataLocation MaxpLocation { get; }

    /// <summary>Anonymous wire counts. Source-bound reads must use the maxp overload.</summary>
    public static ReadStatus TryCreate(int numGlyphs, DataLocation location,
        out LegacyMetricsContext? context, out FontDiagnostic diagnostic)
    {
        context = null;
        if (location.SourceId != 0 || !IsMetricsTag(location.TableTag))
            return ReadWire.Fail(ReadStatus.InvalidSource, DiagnosticCode.WrongSourceFace,
                location, out diagnostic);
        return Create(numGlyphs, location, default, out context, out diagnostic);
    }

    public static ReadStatus TryCreate(MaxpTable maxp, DataLocation location,
        out LegacyMetricsContext? context, out FontDiagnostic diagnostic)
    {
        ArgumentNullException.ThrowIfNull(maxp);
        context = null;
        if (!maxp.HasOriginalGlyphCount || location.SourceId == 0 ||
            !IsMetricsTag(location.TableTag) || maxp.Location.TableTag != 0x6D617870 ||
            location.SourceId != maxp.Location.SourceId || location.FaceIndex != maxp.Location.FaceIndex)
            return ReadWire.Fail(ReadStatus.InvalidSource, DiagnosticCode.WrongSourceFace,
                location, out diagnostic);
        return Create(maxp.NumGlyphs, location, maxp.Location, out context, out diagnostic);
    }

    private static bool IsMetricsTag(uint tag) => tag is HdmxLayout.Tag or LtshLayout.Tag;

    private static ReadStatus Create(int count, DataLocation location, DataLocation maxpLocation,
        out LegacyMetricsContext? context, out FontDiagnostic diagnostic)
    {
        context = null;
        if (count is < 0 or > ushort.MaxValue)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                location.At(DataField.Count, 0), out diagnostic, unchecked((ulong)(long)count));
        context = new((ushort)count, location, maxpLocation);
        diagnostic = default;
        return ReadStatus.Done;
    }
}
