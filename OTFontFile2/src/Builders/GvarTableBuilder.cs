using System.Buffers;
using OTFontFile2.SourceGen;

namespace OTFontFile2.Tables;

/// <summary>
/// Mutable builder for the OpenType <c>gvar</c> table.
/// </summary>
/// <remarks>
/// Supports a raw-bytes mode and a linked-base mode with per-glyph GlyphVariationData record overrides.
/// </remarks>
[OtTableBuilder("gvar", Mode = OtTableBuilderMode.Streaming)]
public sealed partial class GvarTableBuilder : ISfntTableSource
{
    private enum StorageKind : byte
    {
        RawBytes = 0,
        LinkedBaseFont = 1,
    }

    private StorageKind _kind;

    // Raw-bytes mode.
    private ReadOnlyMemory<byte> _data;

    // Linked-base mode.
    private TableSlice _baseTable;
    private GvarTable _baseGvar;
    private ReadOnlyMemory<byte> _baseSharedTuplesBytes;

    private bool _hasGlyfContext;
    private GlyfTable _baseGlyf;
    private LocaTable _baseLoca;
    private short _baseIndexToLocFormat;
    private ushort _baseNumGlyphs;

    private Dictionary<ushort, ReadOnlyMemory<byte>>? _glyphRecordOverrides;

    private byte[]? _built;

    public GvarTableBuilder()
    {
        _kind = StorageKind.RawBytes;
        _data = BuildMinimalTable();
    }

    public bool IsLinkedBaseFont => _kind == StorageKind.LinkedBaseFont;

    public ushort AxisCount
    {
        get
        {
            EnsureLinked();
            return _baseGvar.AxisCount;
        }
    }

    public ushort GlyphCount
    {
        get
        {
            EnsureLinked();
            return _baseGvar.GlyphCount;
        }
    }

    public ReadOnlyMemory<byte> DataBytes
    {
        get
        {
            if (_kind == StorageKind.RawBytes)
                return _data;

            if (_kind == StorageKind.LinkedBaseFont && !HasOverrides)
                return _baseTable.Span.ToArray();

            return EnsureBuiltLinked();
        }
    }

    private int ComputeLength()
    {
        if (_kind == StorageKind.RawBytes)
            return _data.Length;

        if (_kind == StorageKind.LinkedBaseFont && !HasOverrides)
            return _baseTable.Length;

        return EnsureBuiltLinked().Length;
    }

    private uint ComputeDirectoryChecksum()
    {
        if (_kind == StorageKind.RawBytes)
            return OpenTypeChecksum.Compute(_data.Span);

        if (_kind == StorageKind.LinkedBaseFont && !HasOverrides)
            return _baseTable.DirectoryChecksum;

        var built = EnsureBuiltLinked();
        return OpenTypeChecksum.Compute(built);
    }

    private void WriteTable(Stream destination, uint headCheckSumAdjustment)
    {
        if (_kind == StorageKind.RawBytes)
        {
            destination.Write(_data.Span);
            return;
        }

        if (_kind == StorageKind.LinkedBaseFont && !HasOverrides)
        {
            destination.Write(_baseTable.Span);
            return;
        }

        destination.Write(EnsureBuiltLinked());
    }

    partial void OnMarkDirty()
    {
        _built = null;
    }

    public void Clear()
    {
        _kind = StorageKind.RawBytes;
        _data = BuildMinimalTable();
        _glyphRecordOverrides = null;
        MarkDirty();
    }

    public void SetTableData(ReadOnlyMemory<byte> data)
    {
        if (data.Length < 20)
            throw new ArgumentException("gvar table must be at least 20 bytes.", nameof(data));

        _kind = StorageKind.RawBytes;
        _data = data;
        _glyphRecordOverrides = null;
        MarkDirty();
    }

    public static bool TryFrom(GvarTable gvar, out GvarTableBuilder builder)
    {
        var b = new GvarTableBuilder();
        b.SetTableData(gvar.Table.Span.ToArray());
        builder = b;
        return true;
    }

    public static bool TryFrom(SfntFont font, out GvarTableBuilder builder)
    {
        builder = null!;

        if (!font.TryGetGvar(out var gvar))
        {
            builder = new GvarTableBuilder();
            return true;
        }

        if (!TryCreateLinked(font, gvar, out var linked))
        {
            var raw = new GvarTableBuilder();
            raw.SetTableData(gvar.Table.Span.ToArray());
            builder = raw;
            return true;
        }

        builder = linked;
        return true;
    }

    private static bool TryCreateLinked(SfntFont font, GvarTable gvar, out GvarTableBuilder builder)
    {
        builder = null!;

        if (gvar.Table.Length < 20)
            return false;

        ushort axisCount = gvar.AxisCount;
        if (axisCount == 0)
            return false;

        ReadOnlyMemory<byte> sharedBytes = ReadOnlyMemory<byte>.Empty;
        if (gvar.SharedTupleCount != 0)
        {
            int tupleBytes = checked(gvar.SharedTupleCount * axisCount * 2);
            int off = gvar.SharedTuplesOffset;
            if ((uint)off > (uint)gvar.Table.Length - (uint)tupleBytes)
                return false;
            sharedBytes = gvar.Table.Span.Slice(off, tupleBytes).ToArray();
        }

        var b = new GvarTableBuilder
        {
            _kind = StorageKind.LinkedBaseFont,
            _baseTable = gvar.Table,
            _baseGvar = gvar,
            _baseSharedTuplesBytes = sharedBytes,
        };

        if (font.TryGetGlyf(out var glyf)
            && font.TryGetLoca(out var loca)
            && font.TryGetHead(out var head)
            && font.TryGetMaxp(out var maxp))
        {
            b._hasGlyfContext = true;
            b._baseGlyf = glyf;
            b._baseLoca = loca;
            b._baseIndexToLocFormat = head.IndexToLocFormat;
            b._baseNumGlyphs = maxp.NumGlyphs;
        }

        b.MarkDirty();
        builder = b;
        return true;
    }

    public void SetGlyphVariationDataRecord(ushort glyphId, ReadOnlyMemory<byte> glyphVariationDataRecord)
    {
        EnsureLinked();

        if (glyphId >= _baseGvar.GlyphCount)
            throw new ArgumentOutOfRangeException(nameof(glyphId));

        _glyphRecordOverrides ??= new Dictionary<ushort, ReadOnlyMemory<byte>>();
        _glyphRecordOverrides[glyphId] = glyphVariationDataRecord;
        MarkDirty();
    }

    public bool ClearGlyphVariationDataRecord(ushort glyphId)
    {
        EnsureLinked();

        if (_glyphRecordOverrides is null)
            return false;

        bool removed = _glyphRecordOverrides.Remove(glyphId);
        if (removed)
            MarkDirty();
        return removed;
    }

    public bool TryGetGlyphVariationDataRecord(ushort glyphId, out ReadOnlySpan<byte> record)
    {
        record = default;

        EnsureLinked();

        if (glyphId >= _baseGvar.GlyphCount)
            return false;

        if (_glyphRecordOverrides is not null && _glyphRecordOverrides.TryGetValue(glyphId, out var over))
        {
            record = over.Span;
            return true;
        }

        if (!_baseGvar.TryGetGlyphVariationDataBounds(glyphId, out int offset, out int length))
            return false;

        if (length == 0)
        {
            record = ReadOnlySpan<byte>.Empty;
            return true;
        }

        if ((uint)offset > (uint)_baseTable.Length - (uint)length)
            return false;

        record = _baseTable.Span.Slice(offset, length);
        return true;
    }

    public bool TryGetGlyphPointCountWithPhantoms(ushort glyphId, out int pointCountWithPhantoms)
    {
        pointCountWithPhantoms = 0;

        EnsureLinked();
        if (!_hasGlyfContext)
            return false;

        if (glyphId >= _baseNumGlyphs)
            return false;

        if (!TryGetGlyphPointCount(glyphId, out int pointCount))
            return false;

        pointCountWithPhantoms = checked(pointCount + 4);
        return true;
    }

    private bool TryGetGlyphPointCount(ushort glyphId, out int pointCount)
    {
        pointCount = 0;

        if (!_hasGlyfContext)
            return false;

        ushort numGlyphs = _baseNumGlyphs;
        if (numGlyphs == 0)
            return false;

        byte[] state = ArrayPool<byte>.Shared.Rent(numGlyphs); // 0=unvisited, 1=visiting, 2=done
        ushort[] points = ArrayPool<ushort>.Shared.Rent(numGlyphs);

        state.AsSpan(0, numGlyphs).Clear();
        points.AsSpan(0, numGlyphs).Clear();

        try
        {
            if (!TryComputeGlyphPointCount(glyphId, state, points))
                return false;

            pointCount = points[glyphId];
            return true;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(state);
            ArrayPool<ushort>.Shared.Return(points);
        }
    }

    private bool TryComputeGlyphPointCount(ushort glyphId, byte[] state, ushort[] points)
    {
        if (state[glyphId] == 2)
            return true;

        if (state[glyphId] == 1)
            return false; // cycle

        state[glyphId] = 1;

        if (!_baseGlyf.TryGetGlyphData(glyphId, _baseLoca, _baseIndexToLocFormat, _baseNumGlyphs, out var glyphData))
            return false;

        if (glyphData.Length == 0)
        {
            points[glyphId] = 0;
            state[glyphId] = 2;
            return true;
        }

        if (!GlyfTable.TryReadGlyphHeader(glyphData, out var header))
            return false;

        if (!header.IsComposite)
        {
            if (!GlyfTable.TryCreateSimpleGlyphContourEnumerator(glyphData, out var contours))
                return false;

            points[glyphId] = contours.PointCount;
            state[glyphId] = 2;
            return true;
        }

        if (!GlyfTable.TryCreateCompositeGlyphComponentEnumerator(glyphData, out var e))
            return false;

        int total = 0;
        while (e.MoveNext())
        {
            ushort child = e.Current.GlyphIndex;
            if (child >= _baseNumGlyphs)
                return false;

            if (!TryComputeGlyphPointCount(child, state, points))
                return false;

            total += points[child];
            if (total > ushort.MaxValue)
                total = ushort.MaxValue;
        }

        if (!e.IsValid)
            return false;

        points[glyphId] = (ushort)total;
        state[glyphId] = 2;
        return true;
    }

    public bool TryGetGlyphVariations(ushort glyphId, out GlyphVariationData variations)
    {
        variations = null!;

        EnsureLinked();
        if (!_hasGlyfContext)
            return false;

        if (!TryGetGlyphPointCountWithPhantoms(glyphId, out int pointCount))
            return false;

        if (!TryGetGlyphVariationDataRecord(glyphId, out var recordBytes))
            return false;

        return GlyphVariationData.TryParse(
            axisCount: _baseGvar.AxisCount,
            pointCountWithPhantoms: pointCount,
            glyphVariationDataRecord: recordBytes,
            sharedTupleCount: _baseGvar.SharedTupleCount,
            sharedTuplesBytes: _baseSharedTuplesBytes.Span,
            out variations);
    }

    public void SetGlyphVariations(ushort glyphId, GlyphVariationData variations)
    {
        if (variations is null) throw new ArgumentNullException(nameof(variations));
        EnsureLinked();

        if (glyphId >= _baseGvar.GlyphCount)
            throw new ArgumentOutOfRangeException(nameof(glyphId));

        if (variations.AxisCount != _baseGvar.AxisCount)
            throw new InvalidOperationException("gvar variation axisCount must match the base gvar axisCount.");

        if (_hasGlyfContext && TryGetGlyphPointCountWithPhantoms(glyphId, out int expectedPointCount) && variations.PointCountWithPhantoms != expectedPointCount)
            throw new InvalidOperationException("gvar variation pointCount does not match the glyph point count (including phantom points).");

        byte[] record = variations.BuildGlyphVariationDataRecord();
        SetGlyphVariationDataRecord(glyphId, record);
    }

    private bool HasOverrides => _glyphRecordOverrides is { Count: > 0 };

    private void EnsureLinked()
    {
        if (_kind != StorageKind.LinkedBaseFont)
            throw new InvalidOperationException("This operation requires a linked-base gvar builder.");
    }

    private byte[] EnsureBuiltLinked()
    {
        if (_built is not null)
            return _built;

        byte[] built = BuildLinked();
        _built = built;
        return built;
    }

    private byte[] BuildLinked()
    {
        ushort axisCount = _baseGvar.AxisCount;
        ushort glyphCount = _baseGvar.GlyphCount;
        ushort sharedTupleCount = _baseGvar.SharedTupleCount;

        int recordCount = glyphCount;
        var recordLengths = new int[recordCount];

        var baseSpan = _baseTable.Span;

        for (ushort gid = 0; gid < glyphCount; gid++)
        {
            int len;
            if (_glyphRecordOverrides is not null && _glyphRecordOverrides.TryGetValue(gid, out var over))
            {
                len = over.Length;
            }
            else
            {
                if (!_baseGvar.TryGetGlyphVariationDataBounds(gid, out int off, out len))
                    throw new InvalidOperationException("Invalid base gvar offsets.");

                if (len != 0 && (uint)off > (uint)_baseTable.Length - (uint)len)
                    throw new InvalidOperationException("Invalid base gvar record bounds.");
            }

            recordLengths[gid] = Align2(len);
        }

        int dataArrayLen = 0;
        for (int i = 0; i < recordLengths.Length; i++)
            dataArrayLen = checked(dataArrayLen + recordLengths[i]);

        bool offsetsAreLong = (dataArrayLen / 2) > ushort.MaxValue;
        int offsetEntrySize = offsetsAreLong ? 4 : 2;

        int headerLen = 20;
        int offsetsArrayOffset = headerLen;
        int offsetsArrayBytes = checked((glyphCount + 1) * offsetEntrySize);

        int sharedTuplesOffset = 0;
        int pos = checked(offsetsArrayOffset + offsetsArrayBytes);

        if (sharedTupleCount != 0)
        {
            pos = Align2(pos);
            sharedTuplesOffset = pos;
            pos = checked(pos + _baseSharedTuplesBytes.Length);
        }

        pos = Align2(pos);
        int glyphVariationDataArrayOffset = pos;
        pos = checked(pos + dataArrayLen);

        byte[] bytesOut = new byte[pos];
        var span = bytesOut.AsSpan();

        BigEndian.WriteUInt32(span, 0, _baseGvar.Version.RawValue);
        BigEndian.WriteUInt16(span, 4, axisCount);
        BigEndian.WriteUInt16(span, 6, sharedTupleCount);
        BigEndian.WriteUInt32(span, 8, (uint)sharedTuplesOffset);
        BigEndian.WriteUInt16(span, 12, glyphCount);
        BigEndian.WriteUInt16(span, 14, offsetsAreLong ? (ushort)1 : (ushort)0);
        BigEndian.WriteUInt32(span, 16, (uint)glyphVariationDataArrayOffset);

        int rel = 0;
        for (int i = 0; i < glyphCount + 1; i++)
        {
            if (i != 0)
                rel = checked(rel + recordLengths[i - 1]);

            int entryOffset = checked(offsetsArrayOffset + (i * offsetEntrySize));
            if (offsetsAreLong)
            {
                BigEndian.WriteUInt32(span, entryOffset, (uint)rel);
            }
            else
            {
                BigEndian.WriteUInt16(span, entryOffset, checked((ushort)(rel >> 1)));
            }
        }

        if (sharedTuplesOffset != 0 && !_baseSharedTuplesBytes.IsEmpty)
            _baseSharedTuplesBytes.Span.CopyTo(span.Slice(sharedTuplesOffset, _baseSharedTuplesBytes.Length));

        int p = glyphVariationDataArrayOffset;
        for (int i = 0; i < glyphCount; i++)
        {
            int recLen;
            if (_glyphRecordOverrides is not null && _glyphRecordOverrides.TryGetValue((ushort)i, out var over))
            {
                recLen = over.Length;
                if (recLen != 0)
                {
                    over.Span.CopyTo(span.Slice(p, recLen));
                    p = checked(p + recLen);
                }
            }
            else
            {
                if (!_baseGvar.TryGetGlyphVariationDataBounds((ushort)i, out int off, out recLen))
                    throw new InvalidOperationException("Invalid base gvar offsets.");

                if (recLen != 0)
                {
                    baseSpan.Slice(off, recLen).CopyTo(span.Slice(p, recLen));
                    p = checked(p + recLen);
                }
            }

            int pad = recordLengths[i] - recLen;
            if (pad != 0)
            {
                span.Slice(p, pad).Clear();
                p = checked(p + pad);
            }
        }

        return bytesOut;
    }

    private static int Align2(int offset) => (offset + 1) & ~1;

    private static byte[] BuildMinimalTable()
    {
        byte[] bytes = new byte[22];
        var span = bytes.AsSpan();

        BigEndian.WriteUInt32(span, 0, 0x00010000u);
        BigEndian.WriteUInt16(span, 4, 0); // axisCount
        BigEndian.WriteUInt16(span, 6, 0); // sharedTupleCount
        BigEndian.WriteUInt32(span, 8, 0u); // sharedTuplesOffset
        BigEndian.WriteUInt16(span, 12, 0); // glyphCount
        BigEndian.WriteUInt16(span, 14, 0); // flags (short offsets)
        BigEndian.WriteUInt32(span, 16, 22u); // glyphVariationDataArrayOffset
        BigEndian.WriteUInt16(span, 20, 0); // offsets[0] (words)

        return bytes;
    }
}
