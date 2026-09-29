using FontWeave.Core;

namespace FontWeave.Tables;

/// <summary>Owned per-GID thresholds, not a claim about executed TrueType instructions.</summary>
public sealed class LtshTable
{
    private readonly byte[] _yPixels;

    internal LtshTable(byte[] yPixels, DataLocation location, DataLocation maxpLocation)
    { _yPixels = yPixels; Location = location; MaxpLocation = maxpLocation; }

    public ushort Version => 0;
    public ushort NumGlyphs => (ushort)_yPixels.Length;
    public ReadOnlySpan<byte> YPixels => _yPixels;
    public DataLocation Location { get; }
    public DataLocation MaxpLocation { get; }
}

public readonly record struct LtshThresholdEdit(int GlyphId, int YPixels);

public sealed record LtshEditPlan
{
    public IReadOnlyList<LtshThresholdEdit> Thresholds { get; init; } = [];
}
