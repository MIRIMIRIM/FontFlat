using System.Runtime.InteropServices;
using System.Text;
using OTFontFile2.SourceGen;

namespace OTFontFile2.Tables;

/// <summary>
/// Mutable builder for the <c>TTFA</c> table (ASCII payload).
/// </summary>
[OtTableBuilder("TTFA")]
public sealed partial class TtfaTableBuilder : ISfntTableSource
{
    private ReadOnlyMemory<byte> _data;

    public ReadOnlyMemory<byte> DataBytes => _data;

    public void Clear()
    {
        _data = ReadOnlyMemory<byte>.Empty;
        MarkDirty();
    }

    public void SetData(ReadOnlyMemory<byte> data)
    {
        _data = data;
        MarkDirty();
    }

    public void SetAsciiString(string value)
    {
        if (value is null) throw new ArgumentNullException(nameof(value));
        _data = Encoding.ASCII.GetBytes(value);
        MarkDirty();
    }

    public static bool TryFrom(TtfaTable ttfa, out TtfaTableBuilder builder)
    {
        var b = new TtfaTableBuilder();
        b.SetData(ttfa.Table.Span.ToArray());
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

