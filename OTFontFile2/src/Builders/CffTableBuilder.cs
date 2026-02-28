using OTFontFile2.Tables.Cff;
using OTFontFile2.SourceGen;
using System.Buffers;

namespace OTFontFile2.Tables;

/// <summary>
/// Mutable builder for the OpenType <c>CFF </c> table.
/// Supports a linked-base mode with per-glyph CharString overrides and deterministic rebuild.
/// </summary>
[OtTableBuilder("CFF ", Mode = OtTableBuilderMode.Streaming)]
public sealed partial class CffTableBuilder : ISfntTableSource
{
    private enum StorageKind
    {
        RawBytes,
        LinkedBaseFont
    }

    private StorageKind _kind;

    // Raw-bytes mode.
    private ReadOnlyMemory<byte> _data;

    // Linked-base mode.
    private TableSlice _baseTable;
    private CffTable _baseCff;
    private CffIndex _baseNameIndex;
    private CffIndex _baseStringIndex;
    private CffIndex _baseGlobalSubrsIndex;
    private CffTopDict _baseTopDictView;
    private CffIndex _baseCharStringsIndex;
    private CffDictModel _baseTopDictModel = null!;

    private bool _hasFdArray;
    private CffIndex _baseFdArrayIndex;
    private bool _hasFdSelect;
    private CffFdSelect _baseFdSelect;

    private List<CffDictModel>? _baseFontDictModels;
    private List<(int privateSize, int privateOffset, int privateBlockLength)>? _baseFontDictPrivateBlocks;

    private int _basePrivateSize;
    private int _basePrivateOffset;
    private int _basePrivateBlockLength;

    private int _customCharsetOffset;
    private int _customCharsetLength;
    private int _customEncodingOffset;
    private int _customEncodingLength;

    private Dictionary<int, ReadOnlyMemory<byte>>? _charStringOverrides;

    private byte[]? _built;

    public CffTableBuilder()
    {
        _kind = StorageKind.RawBytes;
        _data = BuildMinimalTable();
    }

    public bool IsLinkedBaseFont => _kind == StorageKind.LinkedBaseFont;

    public int GlyphCount
    {
        get
        {
            EnsureLinked();
            return _baseCharStringsIndex.Count;
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

            return EnsureBuilt();
        }
    }

    private int ComputeLength()
    {
        if (_kind == StorageKind.RawBytes)
            return _data.Length;

        if (_kind == StorageKind.LinkedBaseFont && !HasOverrides)
            return _baseTable.Length;

        return EnsureBuilt().Length;
    }

    private uint ComputeDirectoryChecksum()
    {
        if (_kind == StorageKind.RawBytes)
            return OpenTypeChecksum.Compute(_data.Span);

        if (_kind == StorageKind.LinkedBaseFont && !HasOverrides)
            return _baseTable.DirectoryChecksum;

        var built = EnsureBuilt();
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

        destination.Write(EnsureBuilt());
    }

    partial void OnMarkDirty()
    {
        _built = null;
    }

    public void Clear()
    {
        _kind = StorageKind.RawBytes;
        _data = BuildMinimalTable();
        _charStringOverrides = null;
        MarkDirty();
    }

    public void SetTableData(ReadOnlyMemory<byte> data)
    {
        if (data.Length < 4)
            throw new ArgumentException("CFF table must be at least 4 bytes.", nameof(data));

        _kind = StorageKind.RawBytes;
        _data = data;
        _charStringOverrides = null;
        MarkDirty();
    }

    public void SetGlyphCharString(int glyphId, ReadOnlyMemory<byte> charString)
    {
        EnsureLinked();

        if ((uint)glyphId >= (uint)_baseCharStringsIndex.Count)
            throw new ArgumentOutOfRangeException(nameof(glyphId));

        _charStringOverrides ??= new Dictionary<int, ReadOnlyMemory<byte>>();
        _charStringOverrides[glyphId] = charString;
        MarkDirty();
    }

    public void SetGlyphCharStringProgram(int glyphId, Type2CharStringProgram program)
    {
        if (program is null) throw new ArgumentNullException(nameof(program));
        SetGlyphCharString(glyphId, program.ToBytes());
    }

    public bool TryGetGlyphCharStringProgram(int glyphId, out Type2CharStringProgram program)
    {
        program = null!;

        EnsureLinked();

        if ((uint)glyphId >= (uint)_baseCharStringsIndex.Count)
            return false;

        if (_charStringOverrides is not null && _charStringOverrides.TryGetValue(glyphId, out var over))
            return Type2CharStringProgram.TryParse(over.Span, out program);

        if (!_baseCharStringsIndex.TryGetObjectSpan(glyphId, out var baseBytes))
            return false;

        return Type2CharStringProgram.TryParse(baseBytes, out program);
    }

    public bool ClearGlyphCharString(int glyphId)
    {
        EnsureLinked();

        if (_charStringOverrides is null)
            return false;

        bool removed = _charStringOverrides.Remove(glyphId);
        if (removed)
            MarkDirty();
        return removed;
    }

    public static bool TryFrom(CffTable cff, out CffTableBuilder builder)
    {
        builder = null!;

        if (!TryCreateLinked(cff, out var linked))
        {
            // Fallback: raw-bytes copy.
            var b = new CffTableBuilder();
            b.SetTableData(cff.Table.Span.ToArray());
            builder = b;
            return true;
        }

        builder = linked;
        return true;
    }

    private static bool TryCreateLinked(CffTable cff, out CffTableBuilder builder)
    {
        builder = null!;

        if (!cff.TryGetNameIndex(out var nameIndex))
            return false;
        if (!cff.TryGetTopDictIndex(out var topDictIndex))
            return false;
        if (!cff.TryGetStringIndex(out var stringIndex))
            return false;
        if (!cff.TryGetGlobalSubrIndex(out var globalSubrs))
            return false;
        if (!cff.TryGetTopDict(out var topDictView))
            return false;
        if (!topDictView.TryGetCharStringsIndex(out var charStrings))
            return false;

        if (!topDictIndex.TryGetObjectSpan(0, out var topDictBytes))
            return false;
        if (!CffDictModel.TryParse(topDictBytes, out var topDictModel))
            return false;

        var b = new CffTableBuilder
        {
            _kind = StorageKind.LinkedBaseFont,
            _baseTable = cff.Table,
            _baseCff = cff,
            _baseNameIndex = nameIndex,
            _baseStringIndex = stringIndex,
            _baseGlobalSubrsIndex = globalSubrs,
            _baseTopDictView = topDictView,
            _baseCharStringsIndex = charStrings,
            _baseTopDictModel = topDictModel,
        };

        b._basePrivateSize = topDictView.PrivateSize;
        b._basePrivateOffset = topDictView.PrivateOffset;
        if (b._basePrivateSize > 0 && b._basePrivateOffset > 0)
        {
            b._basePrivateBlockLength = ComputePrivateBlockLengthCff1(cff.Table, topDictView, b._basePrivateSize, b._basePrivateOffset);
            if (b._basePrivateBlockLength <= 0)
                return false;
        }
        else
        {
            // CID-keyed fonts can omit Top DICT Private in favor of per-FD Private blocks.
            b._basePrivateBlockLength = 0;
        }

        b._hasFdArray = topDictView.FdArrayOffset > 0 && topDictView.TryGetFdArrayIndex(out b._baseFdArrayIndex);
        b._hasFdSelect = topDictView.FdSelectOffset > 0 && topDictView.TryGetFdSelect(out b._baseFdSelect);

        if (topDictView.HasCustomCharset)
        {
            b._customCharsetOffset = topDictView.CharsetIdOrOffset;
            if (!CffCharset.TryGetByteLength(cff.Table.Span, b._customCharsetOffset, charStrings.Count, out b._customCharsetLength))
                return false;
        }

        if (topDictView.HasCustomEncoding)
        {
            b._customEncodingOffset = topDictView.EncodingIdOrOffset;
            if (!CffEncoding.TryGetByteLength(cff.Table.Span, b._customEncodingOffset, out b._customEncodingLength))
                return false;
        }

        if (b._hasFdArray)
        {
            int fdCount = b._baseFdArrayIndex.Count;
            b._baseFontDictModels = new List<CffDictModel>(fdCount);
            b._baseFontDictPrivateBlocks = new List<(int privateSize, int privateOffset, int privateBlockLength)>(fdCount);

            for (int i = 0; i < fdCount; i++)
            {
                if (!b._baseFdArrayIndex.TryGetObjectSpan(i, out var fdDictBytes))
                    return false;
                if (!CffDictModel.TryParse(fdDictBytes, out var fdModel))
                    return false;

                if (!topDictView.TryGetFontDict(i, out var fdView))
                    return false;

                int pSize = fdView.PrivateSize;
                int pOff = fdView.PrivateOffset;
                int pBlockLen = ComputePrivateBlockLengthCff1(cff.Table, fdView, pSize, pOff);
                if (pBlockLen <= 0)
                    return false;

                b._baseFontDictModels.Add(fdModel);
                b._baseFontDictPrivateBlocks.Add((pSize, pOff, pBlockLen));
            }
        }

        b.MarkDirty();
        builder = b;
        return true;
    }

    private static int ComputePrivateBlockLengthCff1(TableSlice cff, CffTopDict topDict, int privateSize, int privateOffset)
    {
        if (privateSize <= 0 || privateOffset <= 0)
            return 0;

        if ((uint)privateOffset > (uint)cff.Length - (uint)privateSize)
            return 0;

        int maxEnd = checked(privateOffset + privateSize);

        if (topDict.TryGetPrivateDict(out var priv) && priv.SubrsOffset > 0 && priv.TryGetSubrsIndex(out var subrs))
        {
            int end = checked(privateOffset + priv.SubrsOffset + subrs.ByteLength);
            if (end > maxEnd)
                maxEnd = end;
        }

        int length = maxEnd - privateOffset;
        return length > 0 ? length : 0;
    }

    private static int ComputePrivateBlockLengthCff1(TableSlice cff, CffFontDict fontDict, int privateSize, int privateOffset)
    {
        if (privateSize <= 0 || privateOffset <= 0)
            return 0;

        if ((uint)privateOffset > (uint)cff.Length - (uint)privateSize)
            return 0;

        int maxEnd = checked(privateOffset + privateSize);

        if (fontDict.TryGetPrivateDict(out var priv) && priv.SubrsOffset > 0 && priv.TryGetSubrsIndex(out var subrs))
        {
            int end = checked(privateOffset + priv.SubrsOffset + subrs.ByteLength);
            if (end > maxEnd)
                maxEnd = end;
        }

        int length = maxEnd - privateOffset;
        return length > 0 ? length : 0;
    }

    private bool HasOverrides => _charStringOverrides is { Count: > 0 };

    private void EnsureLinked()
    {
        if (_kind != StorageKind.LinkedBaseFont)
            throw new InvalidOperationException("This operation requires a linked-base CFF builder.");
    }

    private byte[] EnsureBuilt()
    {
        if (_kind == StorageKind.RawBytes)
            return _data.ToArray();

        if (_built is not null)
            return _built;

        byte[] built = BuildLinked();
        _built = built;
        return built;
    }

    private byte[] BuildLinked()
    {
        var baseData = _baseTable.Span;

        ReadOnlySpan<byte> headerBytes = baseData.Slice(0, _baseCff.HeaderSize);
        ReadOnlySpan<byte> nameIndexBytes = baseData.Slice(_baseNameIndex.Offset, _baseNameIndex.ByteLength);
        ReadOnlySpan<byte> stringIndexBytes = baseData.Slice(_baseStringIndex.Offset, _baseStringIndex.ByteLength);
        ReadOnlySpan<byte> globalSubrsBytes = baseData.Slice(_baseGlobalSubrsIndex.Offset, _baseGlobalSubrsIndex.ByteLength);

        int glyphCount = _baseCharStringsIndex.Count;
        byte[] charStringsIndexBytes = CffIndexWriter.Build((ushort)glyphCount, new OverridesCffIndexSource(_baseCharStringsIndex, glyphCount, _charStringOverrides));

        ReadOnlySpan<byte> charsetBytes = _customCharsetLength != 0
            ? baseData.Slice(_customCharsetOffset, _customCharsetLength)
            : ReadOnlySpan<byte>.Empty;

        ReadOnlySpan<byte> encodingBytes = _customEncodingLength != 0
            ? baseData.Slice(_customEncodingOffset, _customEncodingLength)
            : ReadOnlySpan<byte>.Empty;

        ReadOnlySpan<byte> fdSelectBytes = ReadOnlySpan<byte>.Empty;
        if (_hasFdSelect)
        {
            if (!_baseFdSelect.TryGetByteLength(out int len))
                throw new InvalidOperationException("Invalid base FDSelect.");
            fdSelectBytes = baseData.Slice(_baseFdSelect.Offset, len);
        }

        bool hasTopPrivate = _basePrivateSize > 0 && _basePrivateOffset > 0 && _basePrivateBlockLength > 0;
        ReadOnlySpan<byte> topPrivateBytes = hasTopPrivate
            ? baseData.Slice(_basePrivateOffset, _basePrivateBlockLength)
            : ReadOnlySpan<byte>.Empty;
        int fontDictCount = _hasFdArray ? _baseFdArrayIndex.Count : 0;

        // Iteratively stabilize Top DICT INDEX length and FDArray length (DICT numeric encoding depends on offset magnitudes).
        byte[] topDictBytes = _baseTopDictModel.BuildDeterministic();
        byte[] topDictIndexBytes = CffIndexWriter.Build(1, new SingleCffIndexSource(topDictBytes));
        byte[] fdArrayBytes = _hasFdArray
            ? CffIndexWriter.Build((ushort)fontDictCount, new EmptyCffIndexSource(fontDictCount))
            : Array.Empty<byte>();

        for (int iter = 0; iter < 10; iter++)
        {
            int prefixLen = checked(headerBytes.Length + nameIndexBytes.Length + topDictIndexBytes.Length + stringIndexBytes.Length + globalSubrsBytes.Length);
            int pos = prefixLen;

            int charsetOffset = _customCharsetLength != 0 ? pos : _baseTopDictView.CharsetIdOrOffset;
            if (_customCharsetLength != 0) pos = checked(pos + charsetBytes.Length);

            int encodingOffset = _customEncodingLength != 0 ? pos : _baseTopDictView.EncodingIdOrOffset;
            if (_customEncodingLength != 0) pos = checked(pos + encodingBytes.Length);

            int fdSelectOffset = _hasFdSelect ? pos : 0;
            if (_hasFdSelect) pos = checked(pos + fdSelectBytes.Length);

            int fdArrayOffset = 0;
            if (_hasFdArray)
            {
                fdArrayOffset = pos;
                pos = checked(pos + fdArrayBytes.Length);
            }

            int topPrivateOffset = pos;
            pos = checked(pos + topPrivateBytes.Length);

            int[] fontDictPrivateOffsets = Array.Empty<int>();
            if (_hasFdArray)
            {
                fontDictPrivateOffsets = new int[fontDictCount];
                for (int i = 0; i < fontDictCount; i++)
                {
                    fontDictPrivateOffsets[i] = pos;
                    pos = checked(pos + _baseFontDictPrivateBlocks![i].privateBlockLength);
                }
            }

            int charStringsOffset = pos;
            pos = checked(pos + charStringsIndexBytes.Length);

            // Rebuild FDArray with updated private offsets.
            byte[] newFdArrayBytes = Array.Empty<byte>();
            if (_hasFdArray)
            {
                var dicts = new ReadOnlyMemory<byte>[fontDictCount];
                for (int i = 0; i < fontDictCount; i++)
                {
                    var m = _baseFontDictModels![i];
                    int privateSize = _baseFontDictPrivateBlocks![i].privateSize;
                    m.SetInt2(18, privateSize, fontDictPrivateOffsets[i]);
                    dicts[i] = m.BuildDeterministic();
                }

                newFdArrayBytes = CffIndexWriter.Build((ushort)fontDictCount, new MemoryArrayCffIndexSource(dicts));
            }

            // Update Top DICT offsets.
            _baseTopDictModel.SetInt(15, charsetOffset);
            _baseTopDictModel.SetInt(16, encodingOffset);
            _baseTopDictModel.SetInt(17, charStringsOffset);
            if (hasTopPrivate)
                _baseTopDictModel.SetInt2(18, _basePrivateSize, topPrivateOffset);
            else
                _baseTopDictModel.RemoveOperator(18);

            if (_hasFdArray)
            {
                _baseTopDictModel.SetInt(0x0C24, fdArrayOffset);
                _baseTopDictModel.SetInt(0x0C25, fdSelectOffset);
            }

            byte[] newTopDictBytes = _baseTopDictModel.BuildDeterministic();
            byte[] newTopDictIndexBytes = CffIndexWriter.Build(1, new SingleCffIndexSource(newTopDictBytes));

            bool stable = newTopDictIndexBytes.Length == topDictIndexBytes.Length
                && (!_hasFdArray || newFdArrayBytes.Length == fdArrayBytes.Length);

            topDictBytes = newTopDictBytes;
            topDictIndexBytes = newTopDictIndexBytes;
            fdArrayBytes = newFdArrayBytes;

            if (stable)
                break;
        }

        // Final layout with stabilized bytes.
        int prefix = checked(headerBytes.Length + nameIndexBytes.Length + topDictIndexBytes.Length + stringIndexBytes.Length + globalSubrsBytes.Length);
        int cur = prefix;

        int charsetAbs = _customCharsetLength != 0 ? cur : _baseTopDictView.CharsetIdOrOffset;
        if (_customCharsetLength != 0) cur = checked(cur + charsetBytes.Length);

        int encodingAbs = _customEncodingLength != 0 ? cur : _baseTopDictView.EncodingIdOrOffset;
        if (_customEncodingLength != 0) cur = checked(cur + encodingBytes.Length);

        int fdSelectAbs = _hasFdSelect ? cur : 0;
        if (_hasFdSelect) cur = checked(cur + fdSelectBytes.Length);

        int fdArrayAbs = 0;
        if (_hasFdArray)
        {
            fdArrayAbs = cur;
            cur = checked(cur + fdArrayBytes.Length);
        }

        int topPrivateAbs = cur;
        cur = checked(cur + topPrivateBytes.Length);

        int[] fontPrivAbs = Array.Empty<int>();
        if (_hasFdArray)
        {
            fontPrivAbs = new int[fontDictCount];
            for (int i = 0; i < fontDictCount; i++)
            {
                fontPrivAbs[i] = cur;
                cur = checked(cur + _baseFontDictPrivateBlocks![i].privateBlockLength);
            }
        }

        int charStringsAbs = cur;
        cur = checked(cur + charStringsIndexBytes.Length);

        // Rebuild bytes once more with final offsets (guaranteed stable now).
        if (_hasFdArray)
        {
            var dicts = new ReadOnlyMemory<byte>[fontDictCount];
            for (int i = 0; i < fontDictCount; i++)
            {
                var m = _baseFontDictModels![i];
                int privateSize = _baseFontDictPrivateBlocks![i].privateSize;
                m.SetInt2(18, privateSize, fontPrivAbs[i]);
                dicts[i] = m.BuildDeterministic();
            }

            fdArrayBytes = CffIndexWriter.Build((ushort)fontDictCount, new MemoryArrayCffIndexSource(dicts));
        }

        _baseTopDictModel.SetInt(15, charsetAbs);
        _baseTopDictModel.SetInt(16, encodingAbs);
        _baseTopDictModel.SetInt(17, charStringsAbs);
        if (hasTopPrivate)
            _baseTopDictModel.SetInt2(18, _basePrivateSize, topPrivateAbs);
        else
            _baseTopDictModel.RemoveOperator(18);
        if (_hasFdArray)
        {
            _baseTopDictModel.SetInt(0x0C24, fdArrayAbs);
            _baseTopDictModel.SetInt(0x0C25, fdSelectAbs);
        }

        topDictBytes = _baseTopDictModel.BuildDeterministic();
        topDictIndexBytes = CffIndexWriter.Build(1, new SingleCffIndexSource(topDictBytes));

        var w = new ArrayBufferWriter<byte>(cur);

        headerBytes.CopyTo(w.GetSpan(headerBytes.Length));
        w.Advance(headerBytes.Length);
        nameIndexBytes.CopyTo(w.GetSpan(nameIndexBytes.Length));
        w.Advance(nameIndexBytes.Length);
        topDictIndexBytes.CopyTo(w.GetSpan(topDictIndexBytes.Length));
        w.Advance(topDictIndexBytes.Length);
        stringIndexBytes.CopyTo(w.GetSpan(stringIndexBytes.Length));
        w.Advance(stringIndexBytes.Length);
        globalSubrsBytes.CopyTo(w.GetSpan(globalSubrsBytes.Length));
        w.Advance(globalSubrsBytes.Length);

        if (_customCharsetLength != 0)
        {
            charsetBytes.CopyTo(w.GetSpan(charsetBytes.Length));
            w.Advance(charsetBytes.Length);
        }

        if (_customEncodingLength != 0)
        {
            encodingBytes.CopyTo(w.GetSpan(encodingBytes.Length));
            w.Advance(encodingBytes.Length);
        }

        if (_hasFdSelect)
        {
            fdSelectBytes.CopyTo(w.GetSpan(fdSelectBytes.Length));
            w.Advance(fdSelectBytes.Length);
        }

        if (_hasFdArray)
        {
            fdArrayBytes.CopyTo(w.GetSpan(fdArrayBytes.Length));
            w.Advance(fdArrayBytes.Length);
        }

        topPrivateBytes.CopyTo(w.GetSpan(topPrivateBytes.Length));
        w.Advance(topPrivateBytes.Length);

        if (_hasFdArray)
        {
            for (int i = 0; i < fontDictCount; i++)
            {
                var info = _baseFontDictPrivateBlocks![i];
                var b = baseData.Slice(info.privateOffset, info.privateBlockLength);
                b.CopyTo(w.GetSpan(b.Length));
                w.Advance(b.Length);
            }
        }

        charStringsIndexBytes.CopyTo(w.GetSpan(charStringsIndexBytes.Length));
        w.Advance(charStringsIndexBytes.Length);

        return w.WrittenSpan.ToArray();
    }

    private static byte[] BuildMinimalTable()
    {
        byte[] bytes = new byte[12];
        bytes[0] = 1;
        bytes[1] = 0;
        bytes[2] = 4;
        bytes[3] = 1;
        return bytes;
    }

    private readonly struct EmptyCffIndexSource : ICffIndexObjectSource
    {
        private readonly int _count;

        public EmptyCffIndexSource(int count) => _count = count;

        public int Count => _count;
        public int GetLength(int index) => 0;
        public void CopyObject(int index, Span<byte> destination) { }
    }

    private readonly struct SingleCffIndexSource : ICffIndexObjectSource
    {
        private readonly ReadOnlyMemory<byte> _obj;

        public SingleCffIndexSource(ReadOnlyMemory<byte> obj) => _obj = obj;
        public int Count => 1;
        public int GetLength(int index) => _obj.Length;
        public void CopyObject(int index, Span<byte> destination) => _obj.Span.CopyTo(destination);
    }

    private readonly struct MemoryArrayCffIndexSource : ICffIndexObjectSource
    {
        private readonly ReadOnlyMemory<byte>[] _objects;

        public MemoryArrayCffIndexSource(ReadOnlyMemory<byte>[] objects) => _objects = objects;

        public int Count => _objects.Length;
        public int GetLength(int index) => _objects[index].Length;
        public void CopyObject(int index, Span<byte> destination) => _objects[index].Span.CopyTo(destination);
    }

    private readonly struct OverridesCffIndexSource : ICffIndexObjectSource
    {
        private readonly CffIndex _baseIndex;
        private readonly int _count;
        private readonly Dictionary<int, ReadOnlyMemory<byte>>? _overrides;

        public OverridesCffIndexSource(CffIndex baseIndex, int count, Dictionary<int, ReadOnlyMemory<byte>>? overrides)
        {
            _baseIndex = baseIndex;
            _count = count;
            _overrides = overrides;
        }

        public int Count => _count;

        public int GetLength(int index)
        {
            if (_overrides is not null && _overrides.TryGetValue(index, out var o))
                return o.Length;

            if (!_baseIndex.TryGetObjectBounds(index, out _, out int length))
                throw new InvalidOperationException("Invalid base CFF INDEX.");

            return length;
        }

        public void CopyObject(int index, Span<byte> destination)
        {
            if (_overrides is not null && _overrides.TryGetValue(index, out var o))
            {
                o.Span.CopyTo(destination);
                return;
            }

            if (!_baseIndex.TryGetObjectSpan(index, out var span))
                throw new InvalidOperationException("Invalid base CFF INDEX.");

            span.CopyTo(destination);
        }
    }
}
