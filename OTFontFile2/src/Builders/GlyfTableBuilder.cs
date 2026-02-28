using System.Buffers;
using OTFontFile2.SourceGen;
using OTFontFile2.Writing;
using OTFontFile2.Tables.Glyf;

namespace OTFontFile2.Tables;

/// <summary>
/// Mutable builder for the TrueType <c>glyf</c> table.
/// Supports a zero-copy base font mode with per-glyph byte overrides, plus a raw-bytes fallback mode.
/// </summary>
[OtTableBuilder("glyf", Mode = OtTableBuilderMode.Streaming)]
public sealed partial class GlyfTableBuilder : ISfntTableSource
{
    private const int Format0MaxGlyfLength = 2 * ushort.MaxValue;

    private enum StorageKind
    {
        RawBytes,
        LinkedBaseFont
    }

    private StorageKind _kind;

    // Raw-bytes mode (fallback for new fonts / manual overrides).
    private ReadOnlyMemory<byte> _rawData;

    // Linked-base mode.
    private TableSlice _baseGlyf;
    private TableSlice _baseLocaSlice;
    private LocaTable _baseLoca;
    private short _baseIndexToLocFormat;
    private ushort _numGlyphs;
    private Dictionary<ushort, ReadOnlyMemory<byte>>? _glyphOverrides;
    private LocaTableBuilder? _derivedLoca;

    private short _requiredIndexToLocFormat;

    public GlyfTableBuilder()
    {
        _kind = StorageKind.RawBytes;
        _rawData = new byte[] { 0 };
    }

    private GlyfTableBuilder(TableSlice baseGlyf, TableSlice baseLocaSlice, LocaTable baseLoca, short indexToLocFormat, ushort numGlyphs)
    {
        _kind = StorageKind.LinkedBaseFont;
        _baseGlyf = baseGlyf;
        _baseLocaSlice = baseLocaSlice;
        _baseLoca = baseLoca;
        _baseIndexToLocFormat = indexToLocFormat;
        _numGlyphs = numGlyphs;
        _rawData = default;
    }

    public bool IsLinkedBaseFont => _kind == StorageKind.LinkedBaseFont;

    public ushort NumGlyphs
    {
        get
        {
            if (_kind != StorageKind.LinkedBaseFont)
                throw new InvalidOperationException("NumGlyphs is only available in linked-base mode.");
            return _numGlyphs;
        }
    }

    /// <summary>
    /// The indexToLocFormat required by the current glyph data (0 or 1).
    /// In raw-bytes mode this always returns 0.
    /// </summary>
    public short RequiredIndexToLocFormat
    {
        get
        {
            EnsureComputed();
            return _requiredIndexToLocFormat;
        }
    }

    public void SetTableData(ReadOnlyMemory<byte> data)
    {
        if (data.Length == 0)
            throw new ArgumentException("glyf table must be non-empty.", nameof(data));

        _kind = StorageKind.RawBytes;
        _rawData = data;
        _glyphOverrides = null;
        MarkDirty();
    }

    public void SetGlyphData(ushort glyphId, ReadOnlyMemory<byte> data)
    {
        EnsureLinked();

        if (glyphId >= _numGlyphs)
            throw new ArgumentOutOfRangeException(nameof(glyphId));

        _glyphOverrides ??= new Dictionary<ushort, ReadOnlyMemory<byte>>();
        _glyphOverrides[glyphId] = data;
        MarkDirty();
    }

    public void SetGlyph(ushort glyphId, GlyfSimpleGlyphBuilder glyph)
    {
        if (glyph is null) throw new ArgumentNullException(nameof(glyph));
        SetGlyphData(glyphId, glyph.Build());
    }

    public void SetGlyph(ushort glyphId, GlyfCompositeGlyphBuilder glyph)
    {
        if (glyph is null) throw new ArgumentNullException(nameof(glyph));
        SetGlyphData(glyphId, glyph.Build());
    }

    public bool ClearGlyphData(ushort glyphId)
    {
        EnsureLinked();

        if (_glyphOverrides is null)
            return false;

        bool removed = _glyphOverrides.Remove(glyphId);
        if (removed)
            MarkDirty();
        return removed;
    }

    internal bool HasGlyphOverrides => _glyphOverrides is { Count: > 0 };

    internal bool TryGetGlyphData(ushort glyphId, out ReadOnlySpan<byte> glyphData)
    {
        glyphData = default;

        if (_kind != StorageKind.LinkedBaseFont)
            return false;

        if (glyphId >= _numGlyphs)
            return false;

        glyphData = GetGlyphDataSpan(glyphId);
        return true;
    }

    internal TableSlice BaseLocaSlice
    {
        get
        {
            EnsureLinked();
            return _baseLocaSlice;
        }
    }

    internal short BaseIndexToLocFormat
    {
        get
        {
            EnsureLinked();
            return _baseIndexToLocFormat;
        }
    }

    internal int GetRebuiltLocaLength()
    {
        EnsureLinked();
        EnsureComputed();

        int entryCount = _numGlyphs + 1;
        return _requiredIndexToLocFormat == 0 ? entryCount * 2 : entryCount * 4;
    }

    internal uint ComputeRebuiltLocaChecksum()
    {
        EnsureLinked();
        EnsureComputed();

        int entryCount = _numGlyphs + 1;
        int offset = 0;

        unchecked
        {
            if (_requiredIndexToLocFormat == 0)
            {
                uint sum = 0;
                int i = 0;
                while (i < entryCount)
                {
                    ushort w0 = checked((ushort)(offset >> 1));
                    if (i != _numGlyphs)
                        offset = checked(offset + GetGlyphPaddedLength((ushort)i));
                    i++;

                    if (i >= entryCount)
                    {
                        sum += (uint)w0 << 16;
                        break;
                    }

                    ushort w1 = checked((ushort)(offset >> 1));
                    if (i != _numGlyphs)
                        offset = checked(offset + GetGlyphPaddedLength((ushort)i));
                    i++;

                    sum += ((uint)w0 << 16) | w1;
                }

                return sum;
            }
            else
            {
                uint sum = 0;
                for (int i = 0; i < entryCount; i++)
                {
                    sum += (uint)offset;
                    if (i != _numGlyphs)
                        offset = checked(offset + GetGlyphPaddedLength((ushort)i));
                }

                return sum;
            }
        }
    }

    internal void WriteRebuiltLoca(Stream destination)
    {
        if (destination is null) throw new ArgumentNullException(nameof(destination));

        EnsureLinked();
        EnsureComputed();

        int entryCount = _numGlyphs + 1;

        byte[] buffer = ArrayPool<byte>.Shared.Rent(8192);
        try
        {
            int offset = 0;
            int pos = 0;

            if (_requiredIndexToLocFormat == 0)
            {
                for (int i = 0; i < entryCount; i++)
                {
                    if (pos > buffer.Length - 2)
                    {
                        destination.Write(buffer, 0, pos);
                        pos = 0;
                    }

                    ushort wordOffset = checked((ushort)(offset >> 1));
                    buffer[pos++] = (byte)(wordOffset >> 8);
                    buffer[pos++] = (byte)wordOffset;

                    if (i != _numGlyphs)
                        offset = checked(offset + GetGlyphPaddedLength((ushort)i));
                }
            }
            else
            {
                for (int i = 0; i < entryCount; i++)
                {
                    if (pos > buffer.Length - 4)
                    {
                        destination.Write(buffer, 0, pos);
                        pos = 0;
                    }

                    uint off = (uint)offset;
                    buffer[pos++] = (byte)(off >> 24);
                    buffer[pos++] = (byte)(off >> 16);
                    buffer[pos++] = (byte)(off >> 8);
                    buffer[pos++] = (byte)off;

                    if (i != _numGlyphs)
                        offset = checked(offset + GetGlyphPaddedLength((ushort)i));
                }
            }

            if (pos != 0)
                destination.Write(buffer, 0, pos);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    public static bool TryFrom(SfntFont font, out GlyfTableBuilder builder)
    {
        builder = null!;

        if (!font.TryGetGlyf(out var glyf))
            return false;
        if (!font.TryGetLoca(out var loca))
            return false;
        if (!font.TryGetHead(out var head))
            return false;
        if (!font.TryGetMaxp(out var maxp))
            return false;

        short indexToLocFormat = head.IndexToLocFormat;
        if (indexToLocFormat is not (0 or 1))
            return false;

        builder = new GlyfTableBuilder(glyf.Table, loca.Table, loca, indexToLocFormat, maxp.NumGlyphs);
        return true;
    }

    public static bool TryFrom(GlyfTable glyf, out GlyfTableBuilder builder)
    {
        // Fallback: raw-bytes copy.
        var b = new GlyfTableBuilder();
        b.SetTableData(glyf.Table.Span.ToArray());
        builder = b;
        return true;
    }

    private void EnsureLinked()
    {
        if (_kind != StorageKind.LinkedBaseFont)
            throw new InvalidOperationException("This operation requires a linked-base glyf builder created from a TrueType font.");
    }

    private int ComputeLength()
    {
        if (_kind == StorageKind.RawBytes)
        {
            _requiredIndexToLocFormat = 0;
            _checksum = OpenTypeChecksum.Compute(_rawData.Span);
            return _rawData.Length;
        }

        _requiredIndexToLocFormat = _baseIndexToLocFormat;

        if (!HasGlyphOverrides)
        {
            _checksum = _baseGlyf.DirectoryChecksum;
            return _baseGlyf.Length;
        }

        var acc = new OpenTypeChecksumAccumulator();
        int total = 0;
        for (ushort gid = 0; gid < _numGlyphs; gid++)
        {
            ReadOnlySpan<byte> glyphData = GetGlyphDataSpan(gid);
            acc.Append(glyphData);
            if ((glyphData.Length & 1) != 0)
                acc.AppendByte(0);

            total = checked(total + Align2(glyphData.Length));
        }

        _checksum = acc.FinalizeChecksum();

        if (_baseIndexToLocFormat == 0 && total > Format0MaxGlyfLength)
            _requiredIndexToLocFormat = 1;

        return total;
    }

    private uint ComputeDirectoryChecksum() => _checksum;

    private void WriteTable(Stream destination, uint headCheckSumAdjustment)
    {
        if (_kind == StorageKind.RawBytes)
        {
            destination.Write(_rawData.Span);
            return;
        }

        if (!HasGlyphOverrides)
        {
            destination.Write(_baseGlyf.Span);
            return;
        }

        WriteRebuiltGlyf(destination);
    }

    private void WriteRebuiltGlyf(Stream destination)
    {
        for (ushort gid = 0; gid < _numGlyphs; gid++)
        {
            ReadOnlySpan<byte> glyphData = GetGlyphDataSpan(gid);
            destination.Write(glyphData);
            if ((glyphData.Length & 1) != 0)
                destination.WriteByte(0);
        }
    }

    private int GetGlyphPaddedLength(ushort glyphId)
        => Align2(GetGlyphUnpaddedLength(glyphId));

    internal void RegisterDerivedLoca(LocaTableBuilder loca) => _derivedLoca = loca;

    partial void OnMarkDirty()
    {
        _derivedLoca?.MarkDirtyFromGlyf();
    }

    private int GetGlyphUnpaddedLength(ushort glyphId)
    {
        if (_glyphOverrides is not null && _glyphOverrides.TryGetValue(glyphId, out var overridden))
            return overridden.Length;

        if (!_baseLoca.TryGetGlyphOffsetLength(glyphId, _baseIndexToLocFormat, _numGlyphs, out _, out int length))
            throw new InvalidOperationException($"Invalid loca/glyf: unable to read glyph length for glyph {glyphId}.");

        return length;
    }

    private ReadOnlySpan<byte> GetGlyphDataSpan(ushort glyphId)
    {
        if (_glyphOverrides is not null && _glyphOverrides.TryGetValue(glyphId, out var overridden))
            return overridden.Span;

        if (!_baseLoca.TryGetGlyphOffsetLength(glyphId, _baseIndexToLocFormat, _numGlyphs, out int offset, out int length))
            throw new InvalidOperationException($"Invalid loca/glyf: unable to read glyph slice for glyph {glyphId}.");

        var baseSpan = _baseGlyf.Span;
        if ((uint)offset > (uint)baseSpan.Length || (uint)length > (uint)(baseSpan.Length - offset))
            throw new InvalidOperationException($"Invalid loca/glyf: glyph {glyphId} out of bounds.");

        return baseSpan.Slice(offset, length);
    }

    private static int Align2(int value) => (value + 1) & ~1;
}
