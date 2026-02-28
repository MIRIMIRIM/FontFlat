using System.Runtime.InteropServices;
using System.Text;
using OTFontFile2.SourceGen;

namespace OTFontFile2.Tables;

/// <summary>
/// Mutable builder for the <c>Debg</c> table (UTF-8 JSON payload).
/// </summary>
[OtTableBuilder("Debg")]
public sealed partial class DebgTableBuilder : ISfntTableSource
{
    private ReadOnlyMemory<byte> _data;

    public ReadOnlyMemory<byte> JsonUtf8 => _data;

    public void Clear()
    {
        _data = ReadOnlyMemory<byte>.Empty;
        MarkDirty();
    }

    public void SetJsonUtf8(ReadOnlyMemory<byte> utf8JsonBytes)
    {
        _data = utf8JsonBytes;
        MarkDirty();
    }

    public void SetJsonString(string json)
    {
        if (json is null) throw new ArgumentNullException(nameof(json));
        _data = Encoding.UTF8.GetBytes(json);
        MarkDirty();
    }

    public static bool TryFrom(DebgTable debg, out DebgTableBuilder builder)
    {
        var b = new DebgTableBuilder();
        b.SetJsonUtf8(debg.Table.Span.ToArray());
        builder = b;
        return true;
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

