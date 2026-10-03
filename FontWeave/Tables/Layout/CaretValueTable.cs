using FontWeave.Core;

namespace FontWeave.Tables.Layout;

/// <summary>Owned format-3 caret coordinate and its required static Device correction.</summary>
public sealed class CaretValueTable
{
    internal CaretValueTable(short coordinate, StaticDeviceTable device, DataLocation location, int consumedLength)
    {
        Coordinate = coordinate;
        Device = device;
        Location = location;
        ConsumedLength = consumedLength;
    }

    public short Coordinate { get; }
    public StaticDeviceTable Device { get; }
    public DataLocation Location { get; }
    /// <summary>Source leaf extent; canonical emission may relocate the child.</summary>
    public int ConsumedLength { get; }

    public ReadStatus TryWith(short coordinate, StaticDeviceTable device, out CaretValueTable? updated,
        out FontDiagnostic diagnostic, BuildLimits? limits = null) =>
        CaretValueTableCompiler.TryCreate(coordinate, device, out updated, out diagnostic, limits, Location);
}
