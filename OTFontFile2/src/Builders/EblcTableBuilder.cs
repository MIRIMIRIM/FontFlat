using OTFontFile2.SourceGen;

namespace OTFontFile2.Tables;

/// <summary>
/// Mutable builder for the OpenType <c>EBLC</c> table.
/// </summary>
[OtTableBuilder("EBLC", Mode = OtTableBuilderMode.Streaming)]
public sealed partial class EblcTableBuilder : ISfntTableSource
{
    private Fixed1616 _version = new(0x00020000u);
    private uint _bitmapSizeTableCount;
    private ReadOnlyMemory<byte> _body = ReadOnlyMemory<byte>.Empty;

    private Fixed1616 _ebdtVersion = new(0x00020000u);

    private bool _isRaw;
    private ReadOnlyMemory<byte> _rawData;

    private bool _isStructured;
    private readonly List<StrikeBuilder> _strikes = new();

    private byte[]? _built;
    private byte[]? _builtEbdtPayload;

    public bool IsRaw => _isRaw;

    public bool IsStructured => _isStructured;

    public Fixed1616 EbdtVersion
    {
        get => _ebdtVersion;
        set
        {
            EnsureNotRaw();

            if (value == _ebdtVersion)
                return;

            _ebdtVersion = value;
            if (_isStructured)
                MarkDirty();
        }
    }

    public Fixed1616 Version
    {
        get
        {
            if (_isRaw)
            {
                var span = _rawData.Span;
                if (span.Length >= 4)
                    return new Fixed1616(BigEndian.ReadUInt32(span, 0));
                return default;
            }

            return _version;
        }
        set
        {
            if (_isRaw)
            {
                var bytes = _rawData.ToArray();
                if (bytes.Length < 4)
                    throw new InvalidOperationException("EBLC raw table data is too short.");

                BigEndian.WriteUInt32(bytes, 0, value.RawValue);
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

    public uint BitmapSizeTableCount
    {
        get
        {
            if (_isRaw)
            {
                var span = _rawData.Span;
                if (span.Length >= 8)
                    return BigEndian.ReadUInt32(span, 4);
                return 0;
            }

            if (_isStructured)
                return (uint)_strikes.Count;

            return _bitmapSizeTableCount;
        }
        set
        {
            EnsureNotRaw();
            EnsureNotStructured();

            if (value == _bitmapSizeTableCount)
                return;

            _bitmapSizeTableCount = value;
            MarkDirty();
        }
    }

    public ReadOnlyMemory<byte> BodyBytes => _body;

    public IReadOnlyList<StrikeBuilder> Strikes => _strikes;

    private int ComputeLength()
    {
        if (_isRaw)
            return _rawData.Length;

        if (_isStructured)
            return EnsureBuilt().Length;

        return checked(8 + _body.Length);
    }

    private uint ComputeDirectoryChecksum()
    {
        if (_isRaw)
            return OpenTypeChecksum.Compute(_rawData.Span);

        if (_isStructured)
        {
            var built = EnsureBuilt();
            return OpenTypeChecksum.Compute(built);
        }

        unchecked
        {
            return Version.RawValue + BitmapSizeTableCount + OpenTypeChecksum.Compute(_body.Span);
        }
    }

    private void WriteTable(Stream destination, uint headCheckSumAdjustment)
    {
        if (_isRaw)
        {
            destination.Write(_rawData.Span);
            return;
        }

        if (_isStructured)
        {
            destination.Write(EnsureBuilt());
            return;
        }

        Span<byte> header = stackalloc byte[8];
        BigEndian.WriteUInt32(header, 0, Version.RawValue);
        BigEndian.WriteUInt32(header, 4, BitmapSizeTableCount);
        destination.Write(header);
        destination.Write(_body.Span);
    }

    public void ClearBody()
    {
        EnsureNotRaw();
        EnsureNotStructured();

        if (_body.IsEmpty)
            return;

        _body = ReadOnlyMemory<byte>.Empty;
        MarkDirty();
    }

    public void SetBody(uint bitmapSizeTableCount, ReadOnlyMemory<byte> bodyBytes)
    {
        EnsureNotRaw();
        EnsureNotStructured();

        _bitmapSizeTableCount = bitmapSizeTableCount;
        _body = bodyBytes;
        MarkDirty();
    }

    public void SetTableData(ReadOnlyMemory<byte> data)
    {
        if (data.Length < 8)
            throw new ArgumentException("EBLC table must be at least 8 bytes.", nameof(data));

        _isRaw = true;
        _rawData = data;
        _isStructured = false;
        _strikes.Clear();
        MarkDirty();
    }

    public void EnableStructured()
    {
        EnsureNotRaw();

        _isStructured = true;
        _strikes.Clear();
        _bitmapSizeTableCount = 0;
        _body = ReadOnlyMemory<byte>.Empty;
        MarkDirty();
    }

    public StrikeBuilder AddStrike(in LineMetrics hori, in LineMetrics vert, byte ppemX, byte ppemY, byte bitDepth, sbyte flags, uint colorRef = 0)
    {
        EnsureNotRaw();
        EnsureStructured();

        var strike = new StrikeBuilder(this, hori, vert, ppemX, ppemY, bitDepth, flags, colorRef);
        _strikes.Add(strike);
        MarkDirty();
        return strike;
    }

    public StrikeBuilder AddStrike(byte ppemX, byte ppemY, byte bitDepth = 1, sbyte flags = 0, uint colorRef = 0)
        => AddStrike(hori: default, vert: default, ppemX, ppemY, bitDepth, flags, colorRef);

    public bool TryBuildDerivedEbdt(out EbdtTableBuilder builder)
    {
        builder = null!;

        if (_isRaw || !_isStructured)
            return false;

        EnsureBuiltStructuredPair();

        builder = new EbdtTableBuilder
        {
            Version = _ebdtVersion
        };
        builder.SetPayload(_builtEbdtPayload!);
        return true;
    }

    public static bool TryFrom(EblcTable eblc, out EblcTableBuilder builder)
    {
        var b = new EblcTableBuilder
        {
            Version = eblc.Version,
            BitmapSizeTableCount = eblc.BitmapSizeTableCount
        };

        var span = eblc.Table.Span;
        b._body = span.Length == 8 ? ReadOnlyMemory<byte>.Empty : span.Slice(8).ToArray();
        builder = b;
        return true;
    }

    public static bool TryFrom(EblcTable eblc, EbdtTable ebdt, out EblcTableBuilder builder)
    {
        builder = null!;

        if (eblc.BitmapSizeTableCount == 0)
            return false;

        if (!TryReadAllBytes(ebdt.Table, out byte[] ebdtBytes))
            return false;

        var b = new EblcTableBuilder
        {
            _version = eblc.Version,
            _ebdtVersion = ebdt.Version,
            _isStructured = true
        };

        uint sizeCountU = eblc.BitmapSizeTableCount;
        if (sizeCountU > int.MaxValue)
            return false;

        int sizeCount = (int)sizeCountU;
        b._strikes.Capacity = sizeCount;

        for (int sizeIndex = 0; sizeIndex < sizeCount; sizeIndex++)
        {
            if (!eblc.TryGetBitmapSizeTable(sizeIndex, out var size))
                return false;

            var strike = new StrikeBuilder(
                owner: b,
                hori: LineMetrics.From(size.Hori),
                vert: LineMetrics.From(size.Vert),
                ppemX: size.PpemX,
                ppemY: size.PpemY,
                bitDepth: size.BitDepth,
                flags: size.Flags,
                colorRef: size.ColorRef);

            uint subCountU = size.NumberOfIndexSubTables;
            if (subCountU > int.MaxValue)
                return false;

            int subCount = (int)subCountU;
            for (int subIndex = 0; subIndex < subCount; subIndex++)
            {
                if (!size.TryGetIndexSubTableArray(subIndex, out var array))
                    return false;

                if (!size.TryGetIndexSubTable(array, out var subTable))
                    return false;

                var st = strike.AddIndexSubTable(
                    firstGlyphIndex: subTable.FirstGlyphIndex,
                    lastGlyphIndex: subTable.LastGlyphIndex,
                    imageFormat: subTable.ImageFormat);

                ushort first = subTable.FirstGlyphIndex;
                ushort last = subTable.LastGlyphIndex;
                for (uint glyphIdU = first; glyphIdU <= last; glyphIdU++)
                {
                    ushort glyphId = (ushort)glyphIdU;
                    if (!subTable.TryGetGlyphImageBounds(glyphId, out int ebdtOffset, out int length))
                        return false;

                    if (length == 0)
                        continue;

                    if (!ebdt.TryGetGlyphSpan(ebdtOffset, length, out _))
                        return false;

                    st.SetGlyphData(glyphId, new ReadOnlyMemory<byte>(ebdtBytes, ebdtOffset, length));
                }
            }

            b._strikes.Add(strike);
        }

        b.MarkDirty();
        builder = b;
        return true;
    }

    private static bool TryReadAllBytes(TableSlice table, out byte[] bytes)
    {
        bytes = null!;

        if (!table.TryGetSpan(out var span))
            return false;

        bytes = span.ToArray();
        return true;
    }

    private void EnsureNotRaw()
    {
        if (_isRaw)
            throw new InvalidOperationException("EBLC builder is in raw mode; call EnableStructured() or use a new builder instance.");
    }

    private void EnsureStructured()
    {
        if (!_isStructured)
            throw new InvalidOperationException("EBLC builder is not in structured mode; call EnableStructured() first.");
    }

    private void EnsureNotStructured()
    {
        if (_isStructured)
            throw new InvalidOperationException("EBLC builder is in structured mode; use Strikes APIs instead of BodyBytes/SetBody.");
    }

    partial void OnMarkDirty()
    {
        _built = null;
        _builtEbdtPayload = null;
    }

    private byte[] EnsureBuilt()
    {
        EnsureBuiltStructuredPair();
        return _built!;
    }

    private void EnsureBuiltStructuredPair()
    {
        if (!_isStructured)
            throw new InvalidOperationException("EBLC builder is not in structured mode.");

        if (_built is not null && _builtEbdtPayload is not null)
            return;

        BuildStructuredPair(out _built, out _builtEbdtPayload);
    }

    private void BuildStructuredPair(out byte[] eblcTable, out byte[] ebdtPayload)
    {
        int strikeCount = _strikes.Count;
        if (strikeCount == 0)
            throw new InvalidOperationException("EBLC structured builder must contain at least one strike.");

        int headerLen = checked(8 + (strikeCount * 48));

        int eblcLen = headerLen;
        int ebdtPayloadLen = 0;

        for (int i = 0; i < strikeCount; i++)
        {
            var strike = _strikes[i];
            int subCount = strike.SubTableCount;
            if (subCount == 0)
                throw new InvalidOperationException("EBLC strike must contain at least one index subtable.");

            int arrayLen = checked(subCount * 8);
            int strikeTablesLen = arrayLen;

            for (int j = 0; j < subCount; j++)
            {
                var st = strike.GetSubTable(j);
                int glyphCount = checked(st.LastGlyphIndex - st.FirstGlyphIndex + 1);
                int subTableLen = checked(8 + ((glyphCount + 1) * 4));
                strikeTablesLen = checked(strikeTablesLen + subTableLen);

                int dataLen = st.ComputeDataBlockLength();
                ebdtPayloadLen = checked(ebdtPayloadLen + dataLen);
            }

            eblcLen = checked(eblcLen + strikeTablesLen);
        }

        eblcTable = new byte[eblcLen];
        ebdtPayload = new byte[ebdtPayloadLen];

        var eblc = eblcTable.AsSpan();
        BigEndian.WriteUInt32(eblc, 0, _version.RawValue);
        BigEndian.WriteUInt32(eblc, 4, checked((uint)strikeCount));

        int eblcCursor = headerLen;
        int ebdtCursor = 0;

        for (int strikeIndex = 0; strikeIndex < strikeCount; strikeIndex++)
        {
            var strike = _strikes[strikeIndex];
            int subCount = strike.SubTableCount;

            int arrayLen = checked(subCount * 8);

            int sizeOffset = 8 + (strikeIndex * 48);
            BigEndian.WriteUInt32(eblc, sizeOffset + 0, checked((uint)eblcCursor)); // indexSubTableArrayOffset

            int strikeTablesLen = arrayLen;
            for (int j = 0; j < subCount; j++)
            {
                var st = strike.GetSubTable(j);
                int glyphCount = checked(st.LastGlyphIndex - st.FirstGlyphIndex + 1);
                int subTableLen = checked(8 + ((glyphCount + 1) * 4));
                strikeTablesLen = checked(strikeTablesLen + subTableLen);
            }

            BigEndian.WriteUInt32(eblc, sizeOffset + 4, checked((uint)strikeTablesLen));
            BigEndian.WriteUInt32(eblc, sizeOffset + 8, checked((uint)subCount));
            BigEndian.WriteUInt32(eblc, sizeOffset + 12, strike.ColorRef);

            strike.Hori.WriteTo(eblc, sizeOffset + 16);
            strike.Vert.WriteTo(eblc, sizeOffset + 28);

            strike.ComputeGlyphRange(out ushort startGlyph, out ushort endGlyph);
            BigEndian.WriteUInt16(eblc, sizeOffset + 40, startGlyph);
            BigEndian.WriteUInt16(eblc, sizeOffset + 42, endGlyph);

            eblc[sizeOffset + 44] = strike.PpemX;
            eblc[sizeOffset + 45] = strike.PpemY;
            eblc[sizeOffset + 46] = strike.BitDepth;
            eblc[sizeOffset + 47] = unchecked((byte)strike.Flags);

            int arrayOffset = eblcCursor;
            int subTableCursor = checked(arrayOffset + arrayLen);

            for (int subIndex = 0; subIndex < subCount; subIndex++)
            {
                var st = strike.GetSubTable(subIndex);

                int arrayEntryOffset = arrayOffset + (subIndex * 8);
                BigEndian.WriteUInt16(eblc, arrayEntryOffset + 0, st.FirstGlyphIndex);
                BigEndian.WriteUInt16(eblc, arrayEntryOffset + 2, st.LastGlyphIndex);

                uint additional = checked((uint)(subTableCursor - arrayOffset));
                BigEndian.WriteUInt32(eblc, arrayEntryOffset + 4, additional);

                int glyphCount = checked(st.LastGlyphIndex - st.FirstGlyphIndex + 1);
                int subTableLen = checked(8 + ((glyphCount + 1) * 4));

                BigEndian.WriteUInt16(eblc, subTableCursor + 0, 1);
                BigEndian.WriteUInt16(eblc, subTableCursor + 2, st.ImageFormat);
                BigEndian.WriteUInt32(eblc, subTableCursor + 4, checked((uint)(4 + ebdtCursor)));

                int offsetsOffset = subTableCursor + 8;
                int blockStart = ebdtCursor;
                int rel = 0;

                for (uint glyphIdU = st.FirstGlyphIndex; glyphIdU <= st.LastGlyphIndex; glyphIdU++)
                {
                    ushort glyphId = (ushort)glyphIdU;
                    int glyphIndex = glyphId - st.FirstGlyphIndex;

                    BigEndian.WriteUInt32(eblc, offsetsOffset + (glyphIndex * 4), checked((uint)rel));

                    if (st.TryGetGlyphData(glyphId, out var data))
                    {
                        int n = data.Length;
                        if (n != 0)
                        {
                            data.Span.CopyTo(ebdtPayload.AsSpan(ebdtCursor, n));
                            ebdtCursor = checked(ebdtCursor + n);
                            rel = checked(ebdtCursor - blockStart);
                        }
                    }
                }

                BigEndian.WriteUInt32(eblc, offsetsOffset + (glyphCount * 4), checked((uint)rel));

                subTableCursor = checked(subTableCursor + subTableLen);
            }

            eblcCursor = subTableCursor;
        }

        if (eblcCursor != eblcLen)
            throw new InvalidOperationException("EBLC builder internal length mismatch.");
        if (ebdtCursor != ebdtPayloadLen)
            throw new InvalidOperationException("EBDT builder internal length mismatch.");
    }

    public readonly struct LineMetrics
    {
        public readonly sbyte Ascender;
        public readonly sbyte Descender;
        public readonly byte WidthMax;
        public readonly sbyte CaretSlopeNumerator;
        public readonly sbyte CaretSlopeDenominator;
        public readonly sbyte CaretOffset;
        public readonly sbyte MinOriginSb;
        public readonly sbyte MinAdvanceSb;
        public readonly sbyte MaxBeforeBl;
        public readonly sbyte MinAfterBl;
        public readonly sbyte Pad1;
        public readonly sbyte Pad2;

        public LineMetrics(
            sbyte ascender,
            sbyte descender,
            byte widthMax,
            sbyte caretSlopeNumerator,
            sbyte caretSlopeDenominator,
            sbyte caretOffset,
            sbyte minOriginSb,
            sbyte minAdvanceSb,
            sbyte maxBeforeBl,
            sbyte minAfterBl,
            sbyte pad1,
            sbyte pad2)
        {
            Ascender = ascender;
            Descender = descender;
            WidthMax = widthMax;
            CaretSlopeNumerator = caretSlopeNumerator;
            CaretSlopeDenominator = caretSlopeDenominator;
            CaretOffset = caretOffset;
            MinOriginSb = minOriginSb;
            MinAdvanceSb = minAdvanceSb;
            MaxBeforeBl = maxBeforeBl;
            MinAfterBl = minAfterBl;
            Pad1 = pad1;
            Pad2 = pad2;
        }

        public static LineMetrics From(SbitLineMetrics m)
            => new(
                ascender: m.Ascender,
                descender: m.Descender,
                widthMax: m.WidthMax,
                caretSlopeNumerator: m.CaretSlopeNumerator,
                caretSlopeDenominator: m.CaretSlopeDenominator,
                caretOffset: m.CaretOffset,
                minOriginSb: m.MinOriginSb,
                minAdvanceSb: m.MinAdvanceSb,
                maxBeforeBl: m.MaxBeforeBl,
                minAfterBl: m.MinAfterBl,
                pad1: m.Pad1,
                pad2: m.Pad2);

        internal void WriteTo(Span<byte> dst, int offset)
        {
            dst[offset + 0] = unchecked((byte)Ascender);
            dst[offset + 1] = unchecked((byte)Descender);
            dst[offset + 2] = WidthMax;
            dst[offset + 3] = unchecked((byte)CaretSlopeNumerator);
            dst[offset + 4] = unchecked((byte)CaretSlopeDenominator);
            dst[offset + 5] = unchecked((byte)CaretOffset);
            dst[offset + 6] = unchecked((byte)MinOriginSb);
            dst[offset + 7] = unchecked((byte)MinAdvanceSb);
            dst[offset + 8] = unchecked((byte)MaxBeforeBl);
            dst[offset + 9] = unchecked((byte)MinAfterBl);
            dst[offset + 10] = unchecked((byte)Pad1);
            dst[offset + 11] = unchecked((byte)Pad2);
        }
    }

    public sealed class StrikeBuilder
    {
        private readonly EblcTableBuilder _owner;
        private LineMetrics _hori;
        private LineMetrics _vert;
        private byte _ppemX;
        private byte _ppemY;
        private byte _bitDepth;
        private sbyte _flags;
        private uint _colorRef;

        private readonly List<IndexSubTableBuilder> _subTables = new();

        internal StrikeBuilder(EblcTableBuilder owner, in LineMetrics hori, in LineMetrics vert, byte ppemX, byte ppemY, byte bitDepth, sbyte flags, uint colorRef)
        {
            _owner = owner;
            _hori = hori;
            _vert = vert;
            _ppemX = ppemX;
            _ppemY = ppemY;
            _bitDepth = bitDepth;
            _flags = flags;
            _colorRef = colorRef;
        }

        internal uint ColorRef => _colorRef;
        internal LineMetrics Hori => _hori;
        internal LineMetrics Vert => _vert;
        internal byte PpemX => _ppemX;
        internal byte PpemY => _ppemY;
        internal byte BitDepth => _bitDepth;
        internal sbyte Flags => _flags;

        public int SubTableCount => _subTables.Count;

        public IReadOnlyList<IndexSubTableBuilder> IndexSubTables => _subTables;

        internal IndexSubTableBuilder GetSubTable(int index) => _subTables[index];

        public IndexSubTableBuilder AddIndexSubTable(ushort firstGlyphIndex, ushort lastGlyphIndex, ushort imageFormat)
        {
            if (lastGlyphIndex < firstGlyphIndex)
                throw new ArgumentOutOfRangeException(nameof(lastGlyphIndex));

            var st = new IndexSubTableBuilder(_owner, firstGlyphIndex, lastGlyphIndex, imageFormat);
            _subTables.Add(st);
            _owner.MarkDirty();
            return st;
        }

        internal void ComputeGlyphRange(out ushort startGlyphIndex, out ushort endGlyphIndex)
        {
            if (_subTables.Count == 0)
                throw new InvalidOperationException("Strike has no index subtables.");

            ushort min = _subTables[0].FirstGlyphIndex;
            ushort max = _subTables[0].LastGlyphIndex;

            for (int i = 1; i < _subTables.Count; i++)
            {
                ushort f = _subTables[i].FirstGlyphIndex;
                ushort l = _subTables[i].LastGlyphIndex;
                if (f < min) min = f;
                if (l > max) max = l;
            }

            startGlyphIndex = min;
            endGlyphIndex = max;
        }
    }

    public sealed class IndexSubTableBuilder
    {
        private readonly EblcTableBuilder _owner;
        private readonly ushort _firstGlyphIndex;
        private readonly ushort _lastGlyphIndex;
        private ushort _imageFormat;

        private readonly Dictionary<ushort, ReadOnlyMemory<byte>> _glyphData = new();

        internal IndexSubTableBuilder(EblcTableBuilder owner, ushort firstGlyphIndex, ushort lastGlyphIndex, ushort imageFormat)
        {
            _owner = owner;
            _firstGlyphIndex = firstGlyphIndex;
            _lastGlyphIndex = lastGlyphIndex;
            _imageFormat = imageFormat;
        }

        public ushort FirstGlyphIndex => _firstGlyphIndex;
        public ushort LastGlyphIndex => _lastGlyphIndex;

        public ushort ImageFormat
        {
            get => _imageFormat;
            set
            {
                if (value == _imageFormat)
                    return;
                _imageFormat = value;
                _owner.MarkDirty();
            }
        }

        public bool TryGetGlyphData(ushort glyphId, out ReadOnlyMemory<byte> data) => _glyphData.TryGetValue(glyphId, out data);

        public void SetGlyphData(ushort glyphId, ReadOnlyMemory<byte> data)
        {
            if (glyphId < _firstGlyphIndex || glyphId > _lastGlyphIndex)
                throw new ArgumentOutOfRangeException(nameof(glyphId));

            _glyphData[glyphId] = data;
            _owner.MarkDirty();
        }

        internal int ComputeDataBlockLength()
        {
            int sum = 0;
            for (uint glyphIdU = _firstGlyphIndex; glyphIdU <= _lastGlyphIndex; glyphIdU++)
            {
                ushort glyphId = (ushort)glyphIdU;
                if (_glyphData.TryGetValue(glyphId, out var data))
                    sum = checked(sum + data.Length);
            }

            return sum;
        }
    }
}
