using FontWeave.Core;

namespace FontWeave.Tables.Layout;

/// <summary>Owned device corrections. The 0x8000 VariationIndex body carries the
/// outer/inner delta-set indices in StartSize/EndSize with an empty delta array
/// (T4-06 section 2.4); the store-domain validation lives at the consumption chain
/// - the GDEF store is a cross-table input, not a device-table property.</summary>
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
    /// <summary>True when this body is a VariationIndex reference (format 0x8000):
    /// <see cref="Outer"/>/<see cref="Inner"/> name the Start/End words, Deltas is
    /// empty and the delta application stays oracle-only (T5-01 3.2's boundary).</summary>
    public bool IsVariationDevice => DeltaFormat == 0x8000;
    /// <summary>The outer delta-set index; valid when <see cref="IsVariationDevice"/>.</summary>
    public ushort Outer => StartSize;
    /// <summary>The inner delta-set index; valid when <see cref="IsVariationDevice"/>.</summary>
    public ushort Inner => EndSize;
    public ReadOnlySpan<sbyte> Deltas => _deltas;
    public DataLocation Location { get; }
    /// <summary>The declared leaf length, excluding any enclosing-window suffix.</summary>
    public int ConsumedLength { get; }

    public ReadStatus TryWithDeltas(ReadOnlySpan<sbyte> deltas, out StaticDeviceTable? updated,
        out FontDiagnostic diagnostic, BuildLimits? limits = null) =>
        StaticDeviceTableCompiler.TryCreate(StartSize, EndSize, DeltaFormat, deltas,
            out updated, out diagnostic, limits, Location);
}
