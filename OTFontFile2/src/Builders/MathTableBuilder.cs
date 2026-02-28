using System.Runtime.InteropServices;
using OTFontFile2.SourceGen;

namespace OTFontFile2.Tables;

/// <summary>
/// Mutable builder for the OpenType <c>MATH</c> table.
/// </summary>
[OtTableBuilder("MATH")]
public sealed partial class MathTableBuilder : ISfntTableSource
{
    private ReadOnlyMemory<byte> _data;

    public MathTableBuilder()
    {
        _data = BuildMinimalTable(versionRaw: 0x00010000u);
    }

    public ReadOnlyMemory<byte> DataBytes => _data;

    public void Clear()
    {
        _data = BuildMinimalTable(versionRaw: 0x00010000u);
        MarkDirty();
    }

    public void SetTableData(ReadOnlyMemory<byte> data)
    {
        if (data.Length < 10)
            throw new ArgumentException("MATH table must be at least 10 bytes.", nameof(data));

        _data = data;
        MarkDirty();
    }

    public static bool TryFrom(MathTable math, out MathTableBuilder builder)
    {
        var b = new MathTableBuilder();
        b.SetTableData(math.Table.Span.ToArray());
        builder = b;
        return true;
    }

    private static byte[] BuildMinimalTable(uint versionRaw)
    {
        byte[] bytes = new byte[10];
        var span = bytes.AsSpan();
        BigEndian.WriteUInt32(span, 0, versionRaw);
        BigEndian.WriteUInt16(span, 4, 0); // MathConstantsOffset
        BigEndian.WriteUInt16(span, 6, 0); // MathGlyphInfoOffset
        BigEndian.WriteUInt16(span, 8, 0); // MathVariantsOffset
        return bytes;
    }

    private byte[] BuildTable()
    {
        if (_data.Length == 0)
            return Array.Empty<byte>();

        if (MemoryMarshal.TryGetArray(_data, out ArraySegment<byte> segment) &&
            segment.Array is not null &&
            segment.Offset == 0 &&
            segment.Count == segment.Array.Length)
        {
            return segment.Array;
        }

        return _data.ToArray();
    }
}

