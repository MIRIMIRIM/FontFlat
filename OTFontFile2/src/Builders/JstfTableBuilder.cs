using System.Runtime.InteropServices;
using OTFontFile2.SourceGen;

namespace OTFontFile2.Tables;

/// <summary>
/// Mutable builder for the OpenType <c>JSTF</c> table.
/// </summary>
[OtTableBuilder("JSTF")]
public sealed partial class JstfTableBuilder : ISfntTableSource
{
    private ReadOnlyMemory<byte> _data;

    public JstfTableBuilder()
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
        if (data.Length < 6)
            throw new ArgumentException("JSTF table must be at least 6 bytes.", nameof(data));

        _data = data;
        MarkDirty();
    }

    public static bool TryFrom(JstfTable jstf, out JstfTableBuilder builder)
    {
        var b = new JstfTableBuilder();
        b.SetTableData(jstf.Table.Span.ToArray());
        builder = b;
        return true;
    }

    private static byte[] BuildMinimalTable(uint versionRaw)
    {
        byte[] bytes = new byte[6];
        var span = bytes.AsSpan();
        BigEndian.WriteUInt32(span, 0, versionRaw);
        BigEndian.WriteUInt16(span, 4, 0); // scriptCount
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
