using OTFontFile2.SourceGen;

namespace OTFontFile2.Tables;

/// <summary>
/// Adobe Glyphlets package table (<c>GPKG</c>).
/// </summary>
[OtTable("GPKG", 8)]
[OtField("Version", OtFieldKind.UInt16, 0)]
[OtField("Flags", OtFieldKind.UInt16, 2)]
[OtField("GmapCount", OtFieldKind.UInt16, 4)]
[OtField("GlyphletCount", OtFieldKind.UInt16, 6)]
[OtUInt32Array("GmapOffset", 8, CountPropertyName = nameof(GmapCount), CountAdjustment = 1)]
[OtUInt32Array("GlyphletOffset", 0, ValuesOffsetExpression = "8 + (GmapCount + 1) * 4", CountPropertyName = nameof(GlyphletCount), CountAdjustment = 1)]
public readonly partial struct GpkgTable
{
    public bool TryGetGmapData(int index, out ReadOnlySpan<byte> data)
    {
        data = default;

        ushort count = GmapCount;
        if ((uint)index >= (uint)count)
            return false;

        if (!TryGetGmapOffset(index, out uint startU) || !TryGetGmapOffset(index + 1, out uint endU))
            return false;

        if (startU > int.MaxValue || endU > int.MaxValue)
            return false;

        int start = (int)startU;
        int end = (int)endU;
        if (end < start)
            return false;

        int length = end - start;
        if ((uint)start > (uint)_table.Length)
            return false;
        if ((uint)length > (uint)(_table.Length - start))
            return false;

        data = _table.Span.Slice(start, length);
        return true;
    }

    public bool TryGetGlyphletData(int index, out ReadOnlySpan<byte> data)
    {
        data = default;

        ushort count = GlyphletCount;
        if ((uint)index >= (uint)count)
            return false;

        if (!TryGetGlyphletOffset(index, out uint startU) || !TryGetGlyphletOffset(index + 1, out uint endU))
            return false;

        if (startU > int.MaxValue || endU > int.MaxValue)
            return false;

        int start = (int)startU;
        int end = (int)endU;
        if (end < start)
            return false;

        int length = end - start;
        if ((uint)start > (uint)_table.Length)
            return false;
        if ((uint)length > (uint)(_table.Length - start))
            return false;

        data = _table.Span.Slice(start, length);
        return true;
    }
}

