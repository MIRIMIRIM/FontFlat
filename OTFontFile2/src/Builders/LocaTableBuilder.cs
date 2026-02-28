using OTFontFile2.SourceGen;

namespace OTFontFile2.Tables;

/// <summary>
/// Mutable builder for the TrueType <c>loca</c> table.
/// In linked mode, this table is rebuilt from a linked <see cref="GlyfTableBuilder"/>.
/// </summary>
[OtTableBuilder("loca", Mode = OtTableBuilderMode.Streaming)]
public sealed partial class LocaTableBuilder : ISfntTableSource
{
    private enum StorageKind
    {
        RawBytes,
        DerivedFromGlyf
    }

    private StorageKind _kind;

    // Raw-bytes mode (manual override).
    private ReadOnlyMemory<byte> _data;

    // Derived mode.
    private readonly GlyfTableBuilder? _glyf;

    public LocaTableBuilder()
    {
        _kind = StorageKind.RawBytes;
        _data = new byte[] { 0, 0 };
    }

    internal LocaTableBuilder(GlyfTableBuilder glyf)
    {
        _kind = StorageKind.DerivedFromGlyf;
        _glyf = glyf ?? throw new ArgumentNullException(nameof(glyf));
        _data = default;
        glyf.RegisterDerivedLoca(this);
    }

    public bool IsDerivedFromGlyf => _kind == StorageKind.DerivedFromGlyf;

    public void SetTableData(ReadOnlyMemory<byte> data)
    {
        if (data.Length == 0)
            throw new ArgumentException("loca table must be non-empty.", nameof(data));

        _kind = StorageKind.RawBytes;
        _data = data;
        MarkDirty();
    }

    public static bool TryFrom(LocaTable loca, out LocaTableBuilder builder)
    {
        // Fallback: raw-bytes copy.
        var b = new LocaTableBuilder();
        b.SetTableData(loca.Table.Span.ToArray());
        builder = b;
        return true;
    }

    internal void MarkDirtyFromGlyf() => MarkDirty();

    private int ComputeLength()
    {
        if (_kind == StorageKind.DerivedFromGlyf)
            return _glyf!.GetRebuiltLocaLength();

        return _data.Length;
    }

    private uint ComputeDirectoryChecksum()
    {
        if (_kind == StorageKind.DerivedFromGlyf)
        {
            if (!_glyf!.HasGlyphOverrides)
                return _glyf.BaseLocaSlice.DirectoryChecksum;

            return _glyf.ComputeRebuiltLocaChecksum();
        }

        return OpenTypeChecksum.Compute(_data.Span);
    }

    private void WriteTable(Stream destination, uint headCheckSumAdjustment)
    {
        if (_kind == StorageKind.DerivedFromGlyf)
        {
            if (!_glyf!.HasGlyphOverrides)
            {
                destination.Write(_glyf.BaseLocaSlice.Span);
                return;
            }

            _glyf.WriteRebuiltLoca(destination);
            return;
        }

        destination.Write(_data.Span);
    }
}
