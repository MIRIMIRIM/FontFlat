using OTFontFile2.SourceGen;
using System.Runtime.InteropServices;

namespace OTFontFile2.Tables;

/// <summary>
/// Mutable builder for the OpenType <c>GSUB</c> table.
/// </summary>
[OtTableBuilder("GSUB")]
public sealed partial class GsubTableBuilder : ISfntTableSource
{
    private readonly OtlLayoutTableBuilder _layout;

    private bool _isRaw;
    private ReadOnlyMemory<byte> _rawData;

    public GsubTableBuilder()
    {
        _layout = new OtlLayoutTableBuilder(MarkDirty, OtlLayoutKind.Gsub);
        Clear();
    }

    public bool IsRaw => _isRaw;

    public OtlLayoutTableBuilder Layout
    {
        get
        {
            EnsureStructured();
            return _layout;
        }
    }

    public ReadOnlyMemory<byte> DataBytes => EnsureBuilt();

    public void Clear()
    {
        _isRaw = false;
        _rawData = ReadOnlyMemory<byte>.Empty;
        _layout.Clear();
        MarkDirty();
    }

    public void SetTableData(ReadOnlyMemory<byte> data)
    {
        if (data.Length < 10)
            throw new ArgumentException("GSUB table must be at least 10 bytes.", nameof(data));

        _isRaw = true;
        _rawData = data;
        MarkDirty();
    }

    public static bool TryFrom(GsubTable gsub, out GsubTableBuilder builder)
    {
        var b = new GsubTableBuilder();
        b.SetTableData(gsub.Table.Span.ToArray());
        builder = b;
        return true;
    }

    private void EnsureStructured()
    {
        if (!_isRaw)
            return;

        throw new InvalidOperationException("GSUB is in raw-bytes mode. Call Clear() to switch to structured editing.");
    }

    private byte[] BuildTable()
    {
        if (_isRaw)
            return GetRawBytes();

        return _layout.BuildBytes();
    }

    private byte[] GetRawBytes()
    {
        if (MemoryMarshal.TryGetArray(_rawData, out ArraySegment<byte> segment) &&
            segment.Array is not null &&
            segment.Offset == 0 &&
            segment.Count == segment.Array.Length)
        {
            return segment.Array;
        }

        return _rawData.ToArray();
    }
}
