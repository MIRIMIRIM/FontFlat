using FontWeave.Core;

namespace FontWeave.Tables.Layout;

/// <summary>Owned static ppem corrections. VariationIndex data is not represented here.</summary>
public sealed class StaticDeviceTable
{
    private readonly sbyte[] _deltas;

    internal StaticDeviceTable(ushort startSize, ushort endSize, ushort deltaFormat, sbyte[] deltas,
        DataLocation location, int consumedLength)
    {
        StartSize = startSize;
        EndSize = endSize;
        DeltaFormat = deltaFormat;
        _deltas = deltas;
        Location = location;
        ConsumedLength = consumedLength;
    }

    public ushort StartSize { get; }
    public ushort EndSize { get; }
    public ushort DeltaFormat { get; }
    public ReadOnlySpan<sbyte> Deltas => _deltas;
    public DataLocation Location { get; }
    /// <summary>The declared leaf length, excluding any enclosing-window suffix.</summary>
    public int ConsumedLength { get; }

    public ReadStatus TryWithDeltas(ReadOnlySpan<sbyte> deltas, out StaticDeviceTable? updated,
        out FontDiagnostic diagnostic, BuildLimits? limits = null) =>
        StaticDeviceTableCompiler.TryCreate(StartSize, EndSize, DeltaFormat, deltas,
            out updated, out diagnostic, limits, Location);
}
