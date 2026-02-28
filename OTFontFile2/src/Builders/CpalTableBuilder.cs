using OTFontFile2.SourceGen;

namespace OTFontFile2.Tables;

/// <summary>
/// Mutable builder for the OpenType <c>CPAL</c> table.
/// </summary>
[OtTableBuilder("CPAL")]
public sealed partial class CpalTableBuilder : ISfntTableSource
{
    private ushort _version;
    private ushort _paletteEntryCount;
    private ushort _paletteCount;

    // Always stored as a dense paletteCount*paletteEntryCount array (palette-major).
    private CpalTable.ColorRecord[] _colors = Array.Empty<CpalTable.ColorRecord>();

    // CPAL v1 optional arrays.
    private uint[]? _paletteTypes;
    private ushort[]? _paletteLabelNameIds;
    private ushort[]? _paletteEntryLabelNameIds;

    public CpalTableBuilder(ushort version = 0)
    {
        if (version > 1)
            throw new ArgumentOutOfRangeException(nameof(version));

        _version = version;
    }

    public ushort Version
    {
        get => _version;
        set
        {
            if (value > 1)
                throw new ArgumentOutOfRangeException(nameof(value), "CPAL version must be 0 or 1.");

            if (value == _version)
                return;

            _version = value;
            if (value == 0)
            {
                _paletteTypes = null;
                _paletteLabelNameIds = null;
                _paletteEntryLabelNameIds = null;
            }

            MarkDirty();
        }
    }

    public ushort PaletteEntryCount => _paletteEntryCount;
    public ushort PaletteCount => _paletteCount;

    public bool HasPaletteTypes => _paletteTypes is not null;
    public bool HasPaletteLabels => _paletteLabelNameIds is not null;
    public bool HasPaletteEntryLabels => _paletteEntryLabelNameIds is not null;

    public int ColorRecordCount => checked(_paletteCount * (int)_paletteEntryCount);

    public void Clear()
    {
        _version = 0;
        _paletteEntryCount = 0;
        _paletteCount = 0;
        _colors = Array.Empty<CpalTable.ColorRecord>();
        _paletteTypes = null;
        _paletteLabelNameIds = null;
        _paletteEntryLabelNameIds = null;
        MarkDirty();
    }

    public void Resize(ushort paletteEntryCount, ushort paletteCount)
    {
        int totalColorRecords = checked(paletteCount * (int)paletteEntryCount);
        if (totalColorRecords > ushort.MaxValue)
            throw new InvalidOperationException("CPAL numColorRecords must fit in uint16.");

        var newColors = totalColorRecords == 0 ? Array.Empty<CpalTable.ColorRecord>() : new CpalTable.ColorRecord[totalColorRecords];
        if (_colors.Length != 0 && newColors.Length != 0)
        {
            int toCopy = Math.Min(_colors.Length, newColors.Length);
            Array.Copy(_colors, 0, newColors, 0, toCopy);
        }

        _colors = newColors;
        _paletteEntryCount = paletteEntryCount;
        _paletteCount = paletteCount;

        if (_paletteTypes is not null)
            _paletteTypes = ResizeArray(_paletteTypes, paletteCount);

        if (_paletteLabelNameIds is not null)
            _paletteLabelNameIds = ResizeU16Array(_paletteLabelNameIds, paletteCount, fill: CpalTable.NoNameId);

        if (_paletteEntryLabelNameIds is not null)
            _paletteEntryLabelNameIds = ResizeU16Array(_paletteEntryLabelNameIds, paletteEntryCount, fill: CpalTable.NoNameId);

        MarkDirty();
    }

    public bool TryGetPaletteColor(int paletteIndex, int entryIndex, out CpalTable.ColorRecord record)
    {
        record = default;

        if ((uint)paletteIndex >= _paletteCount)
            return false;
        if ((uint)entryIndex >= _paletteEntryCount)
            return false;

        int idx = checked((paletteIndex * (int)_paletteEntryCount) + entryIndex);
        record = _colors[idx];
        return true;
    }

    public void SetPaletteColor(int paletteIndex, int entryIndex, CpalTable.ColorRecord record)
    {
        if ((uint)paletteIndex >= _paletteCount)
            throw new ArgumentOutOfRangeException(nameof(paletteIndex));
        if ((uint)entryIndex >= _paletteEntryCount)
            throw new ArgumentOutOfRangeException(nameof(entryIndex));

        int idx = checked((paletteIndex * (int)_paletteEntryCount) + entryIndex);
        _colors[idx] = record;
        MarkDirty();
    }

    public void SetPaletteColors(int paletteIndex, ReadOnlySpan<CpalTable.ColorRecord> colors)
    {
        if ((uint)paletteIndex >= _paletteCount)
            throw new ArgumentOutOfRangeException(nameof(paletteIndex));

        if (colors.Length != _paletteEntryCount)
            throw new ArgumentOutOfRangeException(nameof(colors), $"Palette colors length must be exactly {PaletteEntryCount}.");

        int start = checked(paletteIndex * (int)_paletteEntryCount);
        colors.CopyTo(_colors.AsSpan(start, _paletteEntryCount));
        MarkDirty();
    }

    public bool TryGetPaletteType(int paletteIndex, out uint paletteType)
    {
        paletteType = 0;

        if ((uint)paletteIndex >= _paletteCount)
            return false;

        if (_paletteTypes is null)
            return true;

        paletteType = _paletteTypes[paletteIndex];
        return true;
    }

    public void SetPaletteType(int paletteIndex, uint paletteType)
    {
        if ((uint)paletteIndex >= _paletteCount)
            throw new ArgumentOutOfRangeException(nameof(paletteIndex));

        EnsurePaletteTypes();
        _paletteTypes![paletteIndex] = paletteType;
        MarkDirty();
    }

    public bool TryGetPaletteLabelNameId(int paletteIndex, out ushort nameId)
    {
        nameId = CpalTable.NoNameId;

        if ((uint)paletteIndex >= _paletteCount)
            return false;

        if (_paletteLabelNameIds is null)
            return true;

        nameId = _paletteLabelNameIds[paletteIndex];
        return true;
    }

    public void SetPaletteLabelNameId(int paletteIndex, ushort nameId)
    {
        if ((uint)paletteIndex >= _paletteCount)
            throw new ArgumentOutOfRangeException(nameof(paletteIndex));

        EnsurePaletteLabels();
        _paletteLabelNameIds![paletteIndex] = nameId;
        MarkDirty();
    }

    public bool TryGetPaletteEntryLabelNameId(int entryIndex, out ushort nameId)
    {
        nameId = CpalTable.NoNameId;

        if ((uint)entryIndex >= _paletteEntryCount)
            return false;

        if (_paletteEntryLabelNameIds is null)
            return true;

        nameId = _paletteEntryLabelNameIds[entryIndex];
        return true;
    }

    public void SetPaletteEntryLabelNameId(int entryIndex, ushort nameId)
    {
        if ((uint)entryIndex >= _paletteEntryCount)
            throw new ArgumentOutOfRangeException(nameof(entryIndex));

        EnsurePaletteEntryLabels();
        _paletteEntryLabelNameIds![entryIndex] = nameId;
        MarkDirty();
    }

    public void DisablePaletteTypes()
    {
        if (_paletteTypes is null)
            return;

        _paletteTypes = null;
        MarkDirty();
    }

    public void DisablePaletteLabels()
    {
        if (_paletteLabelNameIds is null)
            return;

        _paletteLabelNameIds = null;
        MarkDirty();
    }

    public void DisablePaletteEntryLabels()
    {
        if (_paletteEntryLabelNameIds is null)
            return;

        _paletteEntryLabelNameIds = null;
        MarkDirty();
    }

    private void EnsurePaletteTypes()
    {
        if (_paletteTypes is not null)
            return;

        Version = 1;
        _paletteTypes = _paletteCount == 0 ? Array.Empty<uint>() : new uint[_paletteCount];
    }

    private void EnsurePaletteLabels()
    {
        if (_paletteLabelNameIds is not null)
            return;

        Version = 1;
        _paletteLabelNameIds = _paletteCount == 0 ? Array.Empty<ushort>() : new ushort[_paletteCount];
        if (_paletteLabelNameIds.Length != 0)
            Array.Fill(_paletteLabelNameIds, CpalTable.NoNameId);
    }

    private void EnsurePaletteEntryLabels()
    {
        if (_paletteEntryLabelNameIds is not null)
            return;

        Version = 1;
        _paletteEntryLabelNameIds = _paletteEntryCount == 0 ? Array.Empty<ushort>() : new ushort[_paletteEntryCount];
        if (_paletteEntryLabelNameIds.Length != 0)
            Array.Fill(_paletteEntryLabelNameIds, CpalTable.NoNameId);
    }

    public static bool TryFrom(CpalTable cpal, out CpalTableBuilder builder)
    {
        builder = null!;

        ushort version = cpal.Version;
        if (version > 1)
            return false;

        var b = new CpalTableBuilder(version);
        b.Resize(cpal.PaletteEntryCount, cpal.PaletteCount);

        for (int p = 0; p < b._paletteCount; p++)
        {
            for (int e = 0; e < b._paletteEntryCount; e++)
            {
                if (!cpal.TryGetPaletteColor(p, e, out var record))
                    return false;

                int idx = checked((p * (int)b._paletteEntryCount) + e);
                b._colors[idx] = record;
            }
        }

        if (version == 1)
        {
            var span = cpal.Table.Span;
            int length = span.Length;

            int afterIndices = 12 + (b._paletteCount * 2);
            if ((uint)afterIndices > (uint)length - 12)
                return false;

            uint paletteTypeOffsetU = BigEndian.ReadUInt32(span, afterIndices + 0);
            uint paletteLabelOffsetU = BigEndian.ReadUInt32(span, afterIndices + 4);
            uint entryLabelOffsetU = BigEndian.ReadUInt32(span, afterIndices + 8);

            if (paletteTypeOffsetU != 0)
            {
                if (paletteTypeOffsetU > int.MaxValue)
                    return false;

                int o = (int)paletteTypeOffsetU;
                if ((uint)o > (uint)length - (uint)(b._paletteCount * 4))
                    return false;

                b._paletteTypes = b._paletteCount == 0 ? Array.Empty<uint>() : new uint[b._paletteCount];
                for (int i = 0; i < b._paletteCount; i++)
                    b._paletteTypes[i] = BigEndian.ReadUInt32(span, o + (i * 4));
            }

            if (paletteLabelOffsetU != 0)
            {
                if (paletteLabelOffsetU > int.MaxValue)
                    return false;

                int o = (int)paletteLabelOffsetU;
                if ((uint)o > (uint)length - (uint)(b._paletteCount * 2))
                    return false;

                b._paletteLabelNameIds = b._paletteCount == 0 ? Array.Empty<ushort>() : new ushort[b._paletteCount];
                for (int i = 0; i < b._paletteCount; i++)
                    b._paletteLabelNameIds[i] = BigEndian.ReadUInt16(span, o + (i * 2));
            }

            if (entryLabelOffsetU != 0)
            {
                if (entryLabelOffsetU > int.MaxValue)
                    return false;

                int o = (int)entryLabelOffsetU;
                if ((uint)o > (uint)length - (uint)(b._paletteEntryCount * 2))
                    return false;

                b._paletteEntryLabelNameIds = b._paletteEntryCount == 0 ? Array.Empty<ushort>() : new ushort[b._paletteEntryCount];
                for (int i = 0; i < b._paletteEntryCount; i++)
                    b._paletteEntryLabelNameIds[i] = BigEndian.ReadUInt16(span, o + (i * 2));
            }
        }

        b.MarkDirty();
        builder = b;
        return true;
    }

    private byte[] BuildTable()
    {
        ushort version = Version;
        if (version > 1)
            throw new InvalidOperationException("CPAL version must be 0 or 1.");

        ushort numPaletteEntries = _paletteEntryCount;
        ushort numPalettes = _paletteCount;

        int numColorRecordsInt = checked(numPalettes * (int)numPaletteEntries);
        if (numColorRecordsInt > ushort.MaxValue)
            throw new InvalidOperationException("CPAL numColorRecords must fit in uint16.");

        ushort numColorRecords = (ushort)numColorRecordsInt;
        if (_colors.Length != numColorRecordsInt)
            throw new InvalidOperationException("CPAL color records array length mismatch.");

        int indicesOffset = 12;
        int afterIndices = checked(indicesOffset + (numPalettes * 2));

        int pos = afterIndices;
        int paletteTypeArrayOffset = 0;
        int paletteLabelArrayOffset = 0;
        int paletteEntryLabelArrayOffset = 0;

        if (version == 1)
        {
            pos = checked(pos + 12);

            if (_paletteTypes is not null)
            {
                if (_paletteTypes.Length != numPalettes)
                    throw new InvalidOperationException("CPAL paletteType array length mismatch.");

                paletteTypeArrayOffset = pos;
                pos = checked(pos + (numPalettes * 4));
            }

            if (_paletteLabelNameIds is not null)
            {
                if (_paletteLabelNameIds.Length != numPalettes)
                    throw new InvalidOperationException("CPAL paletteLabel array length mismatch.");

                paletteLabelArrayOffset = pos;
                pos = checked(pos + (numPalettes * 2));
            }

            if (_paletteEntryLabelNameIds is not null)
            {
                if (_paletteEntryLabelNameIds.Length != numPaletteEntries)
                    throw new InvalidOperationException("CPAL paletteEntryLabel array length mismatch.");

                paletteEntryLabelArrayOffset = pos;
                pos = checked(pos + (numPaletteEntries * 2));
            }
        }

        int firstColorRecordOffset = pos;
        int colorRecordsBytes = checked(numColorRecordsInt * 4);
        pos = checked(pos + colorRecordsBytes);

        byte[] table = new byte[pos];
        var span = table.AsSpan();

        BigEndian.WriteUInt16(span, 0, version);
        BigEndian.WriteUInt16(span, 2, numPaletteEntries);
        BigEndian.WriteUInt16(span, 4, numPalettes);
        BigEndian.WriteUInt16(span, 6, numColorRecords);
        BigEndian.WriteUInt32(span, 8, (uint)firstColorRecordOffset);

        for (int i = 0; i < numPalettes; i++)
        {
            int startIndex = checked(i * numPaletteEntries);
            BigEndian.WriteUInt16(span, indicesOffset + (i * 2), (ushort)startIndex);
        }

        if (version == 1)
        {
            BigEndian.WriteUInt32(span, afterIndices + 0, (uint)paletteTypeArrayOffset);
            BigEndian.WriteUInt32(span, afterIndices + 4, (uint)paletteLabelArrayOffset);
            BigEndian.WriteUInt32(span, afterIndices + 8, (uint)paletteEntryLabelArrayOffset);

            if (paletteTypeArrayOffset != 0)
            {
                int o = paletteTypeArrayOffset;
                for (int i = 0; i < numPalettes; i++)
                    BigEndian.WriteUInt32(span, o + (i * 4), _paletteTypes![i]);
            }

            if (paletteLabelArrayOffset != 0)
            {
                int o = paletteLabelArrayOffset;
                for (int i = 0; i < numPalettes; i++)
                    BigEndian.WriteUInt16(span, o + (i * 2), _paletteLabelNameIds![i]);
            }

            if (paletteEntryLabelArrayOffset != 0)
            {
                int o = paletteEntryLabelArrayOffset;
                for (int i = 0; i < numPaletteEntries; i++)
                    BigEndian.WriteUInt16(span, o + (i * 2), _paletteEntryLabelNameIds![i]);
            }
        }

        int recPos = firstColorRecordOffset;
        for (int i = 0; i < numColorRecordsInt; i++)
        {
            var r = _colors[i];
            span[recPos + 0] = r.Blue;
            span[recPos + 1] = r.Green;
            span[recPos + 2] = r.Red;
            span[recPos + 3] = r.Alpha;
            recPos += 4;
        }

        return table;
    }

    private static uint[] ResizeArray(uint[] existing, int newLength)
    {
        if (newLength == existing.Length)
            return existing;

        if (newLength == 0)
            return Array.Empty<uint>();

        var resized = new uint[newLength];
        int toCopy = Math.Min(existing.Length, newLength);
        if (toCopy != 0)
            Array.Copy(existing, 0, resized, 0, toCopy);
        return resized;
    }

    private static ushort[] ResizeU16Array(ushort[] existing, int newLength, ushort fill)
    {
        if (newLength == existing.Length)
            return existing;

        if (newLength == 0)
            return Array.Empty<ushort>();

        var resized = new ushort[newLength];
        int toCopy = Math.Min(existing.Length, newLength);
        if (toCopy != 0)
            Array.Copy(existing, 0, resized, 0, toCopy);

        if (newLength > toCopy)
            Array.Fill(resized, fill, toCopy, newLength - toCopy);

        return resized;
    }
}
