using FontWeave.Core;

namespace FontWeave.Tables.Layout;

/// <summary>One discriminated CaretValue (OQ-T4-02-3): format 1 carries a coordinate,
/// format 2 an opaque contour-point index (its existence domain is the owning glyph's
/// outline, which GDEF does not check), format 3 a coordinate plus its required static
/// Device correction. The format-3-only <see cref="CaretValueTable"/> keeps its public
/// surface untouched; ligature caret lists carry every format through this record.</summary>
public sealed class CaretValueEntry
{
    private CaretValueEntry(ushort format, short? coordinate, ushort? pointIndex,
        StaticDeviceTable? device, DataLocation location)
    {
        Format = format;
        Coordinate = coordinate;
        PointIndex = pointIndex;
        Device = device;
        Location = location;
    }

    public ushort Format { get; }
    public short? Coordinate { get; }
    public ushort? PointIndex { get; }
    public StaticDeviceTable? Device { get; }
    public DataLocation Location { get; }

    internal static CaretValueEntry ForCoordinate(short value, DataLocation location) =>
        new(1, value, null, null, location);

    internal static CaretValueEntry ForPointIndex(ushort value, DataLocation location) =>
        new(2, null, value, null, location);

    internal static CaretValueEntry ForDevice(short coordinate, StaticDeviceTable device,
        DataLocation location) => new(3, coordinate, null, device, location);
}

/// <summary>One ligated glyph's caret positions in declared order; the sequence
 /// satisfies the statically decidable increasing-coordinate rule.</summary>
public sealed class LigGlyphTable
{
    private readonly CaretValueEntry[] _carets;

    internal LigGlyphTable(CaretValueEntry[] carets, DataLocation location, int consumedLength)
    {
        _carets = carets;
        Location = location;
        ConsumedLength = consumedLength;
    }

    public int Count => _carets.Length;
    public CaretValueEntry this[int index] => _carets[index];
    public DataLocation Location { get; }
    public int ConsumedLength { get; }
}

/// <summary>Owned LigCaretList: the required Coverage and one LigGlyph entry per
/// covered ligated glyph, in Coverage index order. An empty list (zero count over an
/// empty Coverage) is a legal shape and round-trips unchanged (OQ-T4-02-6).</summary>
public sealed class LigCaretListTable
{
    private readonly LigGlyphTable[] _ligGlyphs;
    private readonly uint[] _sourceOffsets;

    internal LigCaretListTable(CoverageTable coverage, LigGlyphTable[] ligGlyphs, int numGlyphs,
        DataLocation location, int consumedLength, uint[] sourceOffsets)
    {
        Coverage = coverage;
        _ligGlyphs = ligGlyphs;
        NumGlyphs = numGlyphs;
        Location = location;
        ConsumedLength = consumedLength;
        _sourceOffsets = sourceOffsets;
    }

    public CoverageTable Coverage { get; }
    public IReadOnlyList<LigGlyphTable> LigGlyphs => _ligGlyphs;
    public int NumGlyphs { get; }
    public DataLocation Location { get; }
    public int ConsumedLength { get; }
    /// <summary>Original parent-relative wire references; empty for caller-created models.</summary>
    public ReadOnlySpan<uint> SourceOffsets => _sourceOffsets;
}
