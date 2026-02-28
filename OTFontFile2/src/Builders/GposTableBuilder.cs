using OTFontFile2.SourceGen;
using System.Runtime.InteropServices;

namespace OTFontFile2.Tables;

/// <summary>
/// Mutable builder for the OpenType <c>GPOS</c> table.
/// </summary>
[OtTableBuilder("GPOS")]
public sealed partial class GposTableBuilder : ISfntTableSource
{
    private readonly OtlLayoutTableBuilder _layout;

    private bool _isRaw;
    private ReadOnlyMemory<byte> _rawData;

    public GposTableBuilder()
    {
        _layout = new OtlLayoutTableBuilder(MarkDirty, OtlLayoutKind.Gpos);
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
            throw new ArgumentException("GPOS table must be at least 10 bytes.", nameof(data));

        _isRaw = true;
        _rawData = data;
        MarkDirty();
    }

    public static bool TryFrom(GposTable gpos, out GposTableBuilder builder)
    {
        var b = new GposTableBuilder();
        b.SetTableData(gpos.Table.Span.ToArray());
        builder = b;
        return true;
    }

    private void EnsureStructured()
    {
        if (!_isRaw)
            return;

        throw new InvalidOperationException("GPOS is in raw-bytes mode. Call Clear() to switch to structured editing.");
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
