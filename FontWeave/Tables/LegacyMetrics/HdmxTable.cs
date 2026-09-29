using FontWeave.Core;

namespace FontWeave.Tables;

public sealed class HdmxDeviceRecord
{
    private readonly byte[] _widths;

    internal HdmxDeviceRecord(byte pixelSize, byte maxWidth, byte[] widths)
    { PixelSize = pixelSize; MaxWidth = maxWidth; _widths = widths; }

    public byte PixelSize { get; }
    public byte MaxWidth { get; }
    public ReadOnlySpan<byte> Widths => _widths;
}

/// <summary>Owned device widths. Compilation normalizes source stride and padding.</summary>
public sealed class HdmxTable
{
    private readonly HdmxDeviceRecord[] _records;

    internal HdmxTable(ushort numGlyphs, uint sourceSizeDeviceRecord, HdmxDeviceRecord[] records,
        DataLocation location, DataLocation maxpLocation)
    {
        NumGlyphs = numGlyphs; SourceSizeDeviceRecord = sourceSizeDeviceRecord; _records = records;
        Location = location; MaxpLocation = maxpLocation;
    }

    public ushort Version => 0;
    public ushort NumGlyphs { get; }
    public ushort NumRecords => (ushort)_records.Length;
    public uint SourceSizeDeviceRecord { get; }
    public ReadOnlySpan<HdmxDeviceRecord> Records => _records;
    public DataLocation Location { get; }
    public DataLocation MaxpLocation { get; }
}

public readonly record struct HdmxWidthEdit(int RecordOrdinal, int GlyphId, int Width);

public sealed record HdmxEditPlan
{
    public IReadOnlyList<HdmxWidthEdit> Widths { get; init; } = [];
}
