using OTFontFile2.SourceGen;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace OTFontFile2.Tables;

/// <summary>
/// Mutable builder for the OpenType <c>sbix</c> table.
/// </summary>
/// <remarks>
/// The <c>sbix</c> table is highly structured and depends on <c>maxp.numGlyphs</c> for interpretation.
/// Unsupported or malformed tables can be preserved via <see cref="SetTableData"/> (raw mode).
/// </remarks>
[OtTableBuilder("sbix")]
public sealed partial class SbixTableBuilder : ISfntTableSource
{
    private ushort _version = 1;
    private ushort _flags;
    private ushort _numGlyphs;
    private bool _hasNumGlyphs;
    private readonly List<StrikeBuilder> _strikes = new();

    private bool _isRaw;
    private ReadOnlyMemory<byte> _rawData;

    public SbixTableBuilder()
    {
        Clear();
    }

    public bool IsRaw => _isRaw;

    public ushort Version
    {
        get
        {
            if (_isRaw)
            {
                var span = _rawData.Span;
                if (span.Length >= 2)
                    return BigEndian.ReadUInt16(span, 0);
                return 0;
            }

            return _version;
        }
        set
        {
            if (_isRaw)
            {
                var bytes = _rawData.ToArray();
                if (bytes.Length < 2)
                    throw new InvalidOperationException("sbix raw table data is too short.");
                BigEndian.WriteUInt16(bytes, 0, value);
                _rawData = bytes;
                MarkDirty();
                return;
            }

            if (value == _version)
                return;
            _version = value;
            MarkDirty();
        }
    }

    public ushort Flags
    {
        get
        {
            if (_isRaw)
            {
                var span = _rawData.Span;
                if (span.Length >= 4)
                    return BigEndian.ReadUInt16(span, 2);
                return 0;
            }

            return _flags;
        }
        set
        {
            if (_isRaw)
            {
                var bytes = _rawData.ToArray();
                if (bytes.Length < 4)
                    throw new InvalidOperationException("sbix raw table data is too short.");
                BigEndian.WriteUInt16(bytes, 2, value);
                _rawData = bytes;
                MarkDirty();
                return;
            }

            if (value == _flags)
                return;
            _flags = value;
            MarkDirty();
        }
    }

    public ushort NumGlyphs => _numGlyphs;

    public bool HasNumGlyphs => _hasNumGlyphs;

    public int StrikeCount => _strikes.Count;

    public IReadOnlyList<StrikeBuilder> Strikes => _strikes;

    public ReadOnlyMemory<byte> DataBytes => EnsureBuilt();

    public void Clear()
    {
        _isRaw = false;
        _rawData = ReadOnlyMemory<byte>.Empty;

        _version = 1;
        _flags = 0;
        _numGlyphs = 0;
        _hasNumGlyphs = false;
        _strikes.Clear();

        MarkDirty();
    }

    public void SetNumGlyphs(ushort numGlyphs)
    {
        EnsureStructured();

        if (_hasNumGlyphs && numGlyphs == _numGlyphs)
            return;

        _numGlyphs = numGlyphs;
        _hasNumGlyphs = true;
        MarkDirty();
    }

    public StrikeBuilder AddStrike(ushort ppem, ushort resolution)
    {
        EnsureStructured();

        if (!_hasNumGlyphs)
            throw new InvalidOperationException("sbix.NumGlyphs must be set (from maxp) before adding strikes.");

        var strike = new StrikeBuilder(owner: this, ppem, resolution);
        _strikes.Add(strike);
        MarkDirty();
        return strike;
    }

    public void ClearStrikes()
    {
        EnsureStructured();

        if (_strikes.Count == 0)
            return;

        _strikes.Clear();
        MarkDirty();
    }

    public bool RemoveStrikeAt(int index)
    {
        EnsureStructured();

        if ((uint)index >= (uint)_strikes.Count)
            return false;

        _strikes.RemoveAt(index);
        MarkDirty();
        return true;
    }

    public void SetTableData(ReadOnlyMemory<byte> data)
    {
        if (data.Length < 8)
            throw new ArgumentException("sbix table must be at least 8 bytes.", nameof(data));

        _isRaw = true;
        _rawData = data;
        MarkDirty();
    }

    public bool TryConvertRawToStructured(ushort numGlyphs)
    {
        if (!_isRaw)
            return true;

        if (!TryParseStructured(_rawData, numGlyphs, out ushort version, out ushort flags, out var strikes))
            return false;

        _isRaw = false;
        _rawData = ReadOnlyMemory<byte>.Empty;

        _version = version;
        _flags = flags;
        _numGlyphs = numGlyphs;
        _hasNumGlyphs = true;
        _strikes.Clear();
        _strikes.AddRange(strikes);
        AttachStrikes();

        MarkDirty();
        return true;
    }

    public static bool TryFrom(SbixTable sbix, out SbixTableBuilder builder)
    {
        builder = new SbixTableBuilder();
        builder.SetTableData(sbix.Table.Span.ToArray());
        return true;
    }

    public static bool TryFrom(SbixTable sbix, ushort numGlyphs, out SbixTableBuilder builder)
    {
        builder = null!;

        // Copy once so glyph payloads can reference stable memory.
        ReadOnlyMemory<byte> data = sbix.Table.Span.ToArray();

        if (!TryParseStructured(data, numGlyphs, out ushort version, out ushort flags, out var strikes))
            return false;

        var b = new SbixTableBuilder
        {
            _version = version,
            _flags = flags,
            _numGlyphs = numGlyphs,
            _hasNumGlyphs = true,
        };
        b._strikes.AddRange(strikes);
        b.AttachStrikes();
        b.MarkDirty();
        builder = b;
        return true;
    }

    private void EnsureStructured()
    {
        if (_isRaw)
            throw new InvalidOperationException("sbix builder is in raw mode; call TryConvertRawToStructured(maxpNumGlyphs) or Clear() first.");
    }

    private void AttachStrikes()
    {
        for (int i = 0; i < _strikes.Count; i++)
            _strikes[i].AttachOwner(this);
    }

    private byte[] BuildTable()
    {
        if (_isRaw)
            return GetRawBytes();

        return BuildStructured();
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

    private byte[] BuildStructured()
    {
        if (_strikes.Count != 0 && !_hasNumGlyphs)
            throw new InvalidOperationException("sbix.NumGlyphs must be set (from maxp) before building strikes.");

        int strikeCount = _strikes.Count;

        int headerSize = checked(8 + (strikeCount * 4));

        int totalLength = headerSize;
        for (int i = 0; i < strikeCount; i++)
        {
            totalLength = checked(totalLength + ComputeStrikeLength(_strikes[i], _numGlyphs));
        }

        byte[] table = new byte[totalLength];
        var span = table.AsSpan();

        BigEndian.WriteUInt16(span, 0, _version);
        BigEndian.WriteUInt16(span, 2, _flags);
        BigEndian.WriteUInt32(span, 4, checked((uint)strikeCount));

        int strikeOffset = headerSize;
        for (int strikeIndex = 0; strikeIndex < strikeCount; strikeIndex++)
        {
            var strike = _strikes[strikeIndex];

            BigEndian.WriteUInt32(span, 8 + (strikeIndex * 4), checked((uint)strikeOffset));

            int strikeLength = WriteStrike(span, strikeOffset, strike, _numGlyphs);
            strikeOffset = checked(strikeOffset + strikeLength);
        }

        Debug.Assert(strikeOffset == totalLength);
        return table;
    }

    private static int ComputeStrikeLength(StrikeBuilder strike, ushort numGlyphs)
    {
        // strikeHeader(4) + glyphDataOffsets[(numGlyphs+1)] (4 each)
        int offsetsLen = checked(4 + (((int)numGlyphs + 1) * 4));
        int length = offsetsLen;

        for (ushort glyphId = 0; glyphId < numGlyphs; glyphId++)
        {
            if (!strike.TryGetGlyph(glyphId, out var record))
                continue;

            length = checked(length + checked(8 + record.Payload.Length));
        }

        return length;
    }

    private static int WriteStrike(Span<byte> table, int strikeOffset, StrikeBuilder strike, ushort numGlyphs)
    {
        // strike header
        BigEndian.WriteUInt16(table, strikeOffset + 0, strike.Ppem);
        BigEndian.WriteUInt16(table, strikeOffset + 2, strike.Resolution);

        int offsetsBase = strikeOffset + 4;
        int glyphDataStartRel = checked(4 + (((int)numGlyphs + 1) * 4));
        int cursorRel = glyphDataStartRel;

        for (ushort glyphId = 0; glyphId < numGlyphs; glyphId++)
        {
            BigEndian.WriteUInt32(table, offsetsBase + (glyphId * 4), checked((uint)cursorRel));

            if (!strike.TryGetGlyph(glyphId, out var record))
                continue;

            int payloadLen = record.Payload.Length;
            int recordLen = checked(8 + payloadLen);
            int recordOffsetAbs = checked(strikeOffset + cursorRel);

            BigEndian.WriteInt16(table, recordOffsetAbs + 0, record.OriginOffsetX);
            BigEndian.WriteInt16(table, recordOffsetAbs + 2, record.OriginOffsetY);
            BigEndian.WriteUInt32(table, recordOffsetAbs + 4, record.GraphicType.Value);

            if (payloadLen != 0)
                record.Payload.Span.CopyTo(table.Slice(recordOffsetAbs + 8, payloadLen));

            cursorRel = checked(cursorRel + recordLen);
        }

        BigEndian.WriteUInt32(table, offsetsBase + (numGlyphs * 4), checked((uint)cursorRel));
        return cursorRel;
    }

    private static bool TryParseStructured(
        ReadOnlyMemory<byte> data,
        ushort numGlyphs,
        out ushort version,
        out ushort flags,
        out List<StrikeBuilder> strikes)
    {
        version = 0;
        flags = 0;
        strikes = null!;

        var span = data.Span;

        // version(2) + flags(2) + numStrikes(4)
        if (span.Length < 8)
            return false;

        version = BigEndian.ReadUInt16(span, 0);
        flags = BigEndian.ReadUInt16(span, 2);
        uint strikeCountU = BigEndian.ReadUInt32(span, 4);

        long strikeOffsetsLenLong = 8L + (strikeCountU * 4L);
        if (strikeOffsetsLenLong > int.MaxValue)
            return false;
        if (strikeOffsetsLenLong > span.Length)
            return false;

        int strikeCount = (int)strikeCountU;
        var list = new List<StrikeBuilder>(strikeCount);

        for (int strikeIndex = 0; strikeIndex < strikeCount; strikeIndex++)
        {
            int strikeOffsetEntry = 8 + (strikeIndex * 4);
            uint strikeOffsetU = BigEndian.ReadUInt32(span, strikeOffsetEntry);
            if (strikeOffsetU > int.MaxValue)
                return false;

            int strikeOffset = (int)strikeOffsetU;
            int strikeEnd;

            if ((uint)(strikeIndex + 1) < strikeCountU)
            {
                uint nextU = BigEndian.ReadUInt32(span, strikeOffsetEntry + 4);
                if (nextU > int.MaxValue)
                    return false;
                strikeEnd = (int)nextU;
            }
            else
            {
                strikeEnd = span.Length;
            }

            if (strikeEnd < strikeOffset || strikeEnd > span.Length)
                return false;

            int strikeLength = strikeEnd - strikeOffset;

            // strikeHeader(4) + glyphDataOffsets[(numGlyphs+1)] (4 each)
            long offsetsLenLong = 4L + ((long)numGlyphs + 1) * 4L;
            if (offsetsLenLong > int.MaxValue)
                return false;
            if (offsetsLenLong > strikeLength)
                return false;

            ushort ppem = BigEndian.ReadUInt16(span, strikeOffset + 0);
            ushort resolution = BigEndian.ReadUInt16(span, strikeOffset + 2);

            var strike = new StrikeBuilder(owner: null, ppem, resolution);

            int offsetsBase = strikeOffset + 4;
            int glyphDataStartRel = (int)offsetsLenLong;

            for (ushort glyphId = 0; glyphId < numGlyphs; glyphId++)
            {
                int entryOffset = offsetsBase + (glyphId * 4);
                if ((uint)entryOffset > (uint)span.Length - 8)
                    return false;

                uint startRelU = BigEndian.ReadUInt32(span, entryOffset);
                uint endRelU = BigEndian.ReadUInt32(span, entryOffset + 4);
                if (endRelU < startRelU)
                    return false;

                if (startRelU > int.MaxValue || endRelU > int.MaxValue)
                    return false;

                int startRel = (int)startRelU;
                int endRel = (int)endRelU;

                if (startRel < glyphDataStartRel || endRel < glyphDataStartRel)
                    return false;
                if (endRel > strikeLength)
                    return false;

                int recordLen = endRel - startRel;
                if (recordLen == 0)
                    continue;

                int recordOffsetAbs = checked(strikeOffset + startRel);
                if ((uint)recordOffsetAbs > (uint)span.Length)
                    return false;
                if ((uint)recordLen > (uint)(span.Length - recordOffsetAbs))
                    return false;

                if (recordLen < 8)
                    return false;

                short originX = BigEndian.ReadInt16(span, recordOffsetAbs + 0);
                short originY = BigEndian.ReadInt16(span, recordOffsetAbs + 2);
                var graphicType = new Tag(BigEndian.ReadUInt32(span, recordOffsetAbs + 4));

                var payload = data.Slice(recordOffsetAbs + 8, recordLen - 8);
                strike.SetGlyph(glyphId, new GlyphRecord(originX, originY, graphicType, payload));
            }

            list.Add(strike);
        }

        strikes = list;
        return true;
    }

    public readonly struct GlyphRecord
    {
        public short OriginOffsetX { get; }
        public short OriginOffsetY { get; }
        public Tag GraphicType { get; }
        public ReadOnlyMemory<byte> Payload { get; }

        public GlyphRecord(short originOffsetX, short originOffsetY, Tag graphicType, ReadOnlyMemory<byte> payload)
        {
            OriginOffsetX = originOffsetX;
            OriginOffsetY = originOffsetY;
            GraphicType = graphicType;
            Payload = payload;
        }
    }

    public sealed class StrikeBuilder
    {
        private SbixTableBuilder? _owner;
        private readonly Dictionary<ushort, GlyphRecord> _glyphs = new();

        private ushort _ppem;
        private ushort _resolution;

        internal StrikeBuilder(SbixTableBuilder? owner, ushort ppem, ushort resolution)
        {
            _owner = owner;
            _ppem = ppem;
            _resolution = resolution;
        }

        internal void AttachOwner(SbixTableBuilder owner) => _owner = owner;

        public ushort Ppem
        {
            get => _ppem;
            set
            {
                if (value == _ppem)
                    return;
                _ppem = value;
                _owner?.MarkDirty();
            }
        }

        public ushort Resolution
        {
            get => _resolution;
            set
            {
                if (value == _resolution)
                    return;
                _resolution = value;
                _owner?.MarkDirty();
            }
        }

        public int GlyphRecordCount => _glyphs.Count;

        public bool TryGetGlyph(ushort glyphId, out GlyphRecord record) => _glyphs.TryGetValue(glyphId, out record);

        public void SetGlyph(ushort glyphId, GlyphRecord record)
        {
            _glyphs[glyphId] = record;
            _owner?.MarkDirty();
        }

        public bool RemoveGlyph(ushort glyphId)
        {
            bool removed = _glyphs.Remove(glyphId);
            if (removed)
                _owner?.MarkDirty();
            return removed;
        }

        public void ClearGlyphs()
        {
            if (_glyphs.Count == 0)
                return;

            _glyphs.Clear();
            _owner?.MarkDirty();
        }
    }
}
