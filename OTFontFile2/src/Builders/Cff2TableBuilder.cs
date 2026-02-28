using System.Buffers;
using OTFontFile2.Tables.Cff;
using OTFontFile2.SourceGen;

namespace OTFontFile2.Tables;

/// <summary>
/// Mutable builder for the OpenType <c>CFF2</c> table.
/// Supports a linked-base mode with per-glyph CharString overrides and deterministic rebuild.
/// </summary>
[OtTableBuilder("CFF2", Mode = OtTableBuilderMode.Streaming)]
public sealed partial class Cff2TableBuilder : ISfntTableSource
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
    private Cff2Table _baseCff2;
    private CffDictModel _baseTopDictModel = null!;

    private Cff2Index _baseGlobalSubrsIndex;
    private Cff2Index _baseCharStringsIndex;

    private bool _hasFdArray;
    private Cff2Index _baseFdArrayIndex;
    private bool _hasFdSelect;
    private CffFdSelect _baseFdSelect;

    private List<CffDictModel>? _baseFontDictModels;
    private List<(int privateSize, int privateOffset, int privateBlockLength)>? _baseFontDictPrivateBlocks;

    private bool _hasVarStore;
    private int _varStoreOffset;
    private int _varStoreLength;

    private Dictionary<int, ReadOnlyMemory<byte>>? _charStringOverrides;

    private byte[]? _built;

    public Cff2TableBuilder()
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
            return _baseCharStringsIndex.Count > int.MaxValue ? int.MaxValue : (int)_baseCharStringsIndex.Count;
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
        if (data.Length < 5)
            throw new ArgumentException("CFF2 table must be at least 5 bytes.", nameof(data));

        _kind = StorageKind.RawBytes;
        _data = data;
        _charStringOverrides = null;
        MarkDirty();
    }

    public void SetGlyphCharString(int glyphId, ReadOnlyMemory<byte> charString)
    {
        EnsureLinked();

        int glyphCount = GlyphCount;
        if ((uint)glyphId >= (uint)glyphCount)
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

        int glyphCount = GlyphCount;
        if ((uint)glyphId >= (uint)glyphCount)
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

    public static bool TryFrom(Cff2Table cff2, out Cff2TableBuilder builder)
    {
        builder = null!;

        if (!TryCreateLinked(cff2, out var linked))
        {
            var b = new Cff2TableBuilder();
            b.SetTableData(cff2.Table.Span.ToArray());
            builder = b;
            return true;
        }

        builder = linked;
        return true;
    }

    private static bool TryCreateLinked(Cff2Table cff2, out Cff2TableBuilder builder)
    {
        builder = null!;

        if (!cff2.TryGetTopDict(out var topDictView))
            return false;

        int topDictOffset = cff2.HeaderSize;
        int topDictLength = cff2.TopDictLength;
        if ((uint)topDictOffset > (uint)cff2.Table.Length - (uint)topDictLength)
            return false;

        ReadOnlySpan<byte> topDictBytes = cff2.Table.Span.Slice(topDictOffset, topDictLength);
        if (!CffDictModel.TryParse(topDictBytes, out var topDictModel))
            return false;

        if (!cff2.TryGetGlobalSubrIndex(out var gsubrs))
            return false;

        if (!cff2.TryGetCharStringsIndex(out var charStrings))
            return false;

        var b = new Cff2TableBuilder
        {
            _kind = StorageKind.LinkedBaseFont,
            _baseTable = cff2.Table,
            _baseCff2 = cff2,
            _baseTopDictModel = topDictModel,
            _baseGlobalSubrsIndex = gsubrs,
            _baseCharStringsIndex = charStrings
        };

        b._hasFdArray = topDictView.FdArrayOffset > 0 && cff2.TryGetFdArrayIndex(out b._baseFdArrayIndex);
        b._hasFdSelect = topDictView.FdSelectOffset > 0 && cff2.TryGetFdSelect(out b._baseFdSelect);

        if (topDictView.HasVarStore)
        {
            b._hasVarStore = true;
            b._varStoreOffset = topDictView.VarStoreOffset;

            if (!cff2.TryGetVarStore(out var store))
                return false;
            if (!store.TryGetByteLength(out int len))
                return false;

            b._varStoreLength = len;
        }

        if (b._hasFdArray)
        {
            int fdCount = (int)b._baseFdArrayIndex.Count;
            b._baseFontDictModels = new List<CffDictModel>(fdCount);
            b._baseFontDictPrivateBlocks = new List<(int privateSize, int privateOffset, int privateBlockLength)>(fdCount);

            for (int i = 0; i < fdCount; i++)
            {
                if (!b._baseFdArrayIndex.TryGetObjectSpan(i, out var fdDictBytes))
                    return false;
                if (!CffDictModel.TryParse(fdDictBytes, out var fdModel))
                    return false;

                if (!cff2.TryGetFontDict(i, out var fdView))
                    return false;

                int pSize = fdView.PrivateSize;
                int pOff = fdView.PrivateOffset;
                int pBlockLen = ComputePrivateBlockLengthCff2(cff2.Table, fdView, pSize, pOff);
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

    private static int ComputePrivateBlockLengthCff2(TableSlice cff2, CffFontDict fdView, int privateSize, int privateOffset)
    {
        if (privateSize <= 0 || privateOffset <= 0)
            return 0;
        if ((uint)privateOffset > (uint)cff2.Length - (uint)privateSize)
            return 0;

        int maxEnd = checked(privateOffset + privateSize);

        if (fdView.TryGetPrivateDictCff2(out var priv) && priv.SubrsOffset > 0 && priv.TryGetSubrsIndex(out var subrs))
        {
            int end = checked(privateOffset + priv.SubrsOffset + subrs.ByteLength);
            if (end > maxEnd)
                maxEnd = end;
        }

        return maxEnd - privateOffset;
    }

    private bool HasOverrides => _charStringOverrides is { Count: > 0 };

    private void EnsureLinked()
    {
        if (_kind != StorageKind.LinkedBaseFont)
            throw new InvalidOperationException("This operation requires a linked-base CFF2 builder.");
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

        ReadOnlySpan<byte> headerBytes = baseData.Slice(0, _baseCff2.HeaderSize);
        ReadOnlySpan<byte> globalSubrsBytes = baseData.Slice(_baseGlobalSubrsIndex.Offset, _baseGlobalSubrsIndex.ByteLength);

        int glyphCount = GlyphCount;
        byte[] charStringsIndexBytes = Cff2IndexWriter.Build((uint)glyphCount, new OverridesCff2IndexSource(_baseCharStringsIndex, glyphCount, _charStringOverrides));

        ReadOnlySpan<byte> fdSelectBytes = ReadOnlySpan<byte>.Empty;
        if (_hasFdSelect)
        {
            if (!_baseFdSelect.TryGetByteLength(out int len))
                throw new InvalidOperationException("Invalid base FDSelect.");
            fdSelectBytes = baseData.Slice(_baseFdSelect.Offset, len);
        }

        ReadOnlySpan<byte> varStoreBytes = ReadOnlySpan<byte>.Empty;
        if (_hasVarStore)
            varStoreBytes = baseData.Slice(_varStoreOffset, _varStoreLength);

        int fontDictCount = _hasFdArray ? _baseFontDictPrivateBlocks!.Count : 0;

        byte[] topDictBytes = _baseTopDictModel.BuildDeterministic();
        byte[] fdArrayBytes = _hasFdArray
            ? Cff2IndexWriter.Build((uint)fontDictCount, new EmptyCff2IndexSource(fontDictCount))
            : Array.Empty<byte>();

        int totalLength = 0;
        byte[] headerOut = headerBytes.ToArray();

        for (int iter = 0; iter < 12; iter++)
        {
            ushort topDictLength = (ushort)topDictBytes.Length;
            BigEndian.WriteUInt16(headerOut, 3, topDictLength);

            int prefix = checked(_baseCff2.HeaderSize + topDictLength + globalSubrsBytes.Length);
            int pos = prefix;

            int fdSelectOffset = _hasFdSelect ? pos : 0;
            if (_hasFdSelect) pos = checked(pos + fdSelectBytes.Length);

            int fdArrayOffset = 0;
            if (_hasFdArray)
            {
                fdArrayOffset = pos;
                pos = checked(pos + fdArrayBytes.Length);
            }

            int[] privOffsets = Array.Empty<int>();
            if (_hasFdArray)
            {
                privOffsets = new int[fontDictCount];
                for (int i = 0; i < fontDictCount; i++)
                {
                    privOffsets[i] = pos;
                    pos = checked(pos + _baseFontDictPrivateBlocks![i].privateBlockLength);
                }
            }

            int charStringsOffset = pos;
            pos = checked(pos + charStringsIndexBytes.Length);

            int varStoreOffset = _hasVarStore ? pos : 0;
            if (_hasVarStore) pos = checked(pos + varStoreBytes.Length);

            byte[] newFdArrayBytes = fdArrayBytes;
            if (_hasFdArray)
            {
                var dicts = new ReadOnlyMemory<byte>[fontDictCount];
                for (int i = 0; i < fontDictCount; i++)
                {
                    var m = _baseFontDictModels![i];
                    int pSize = _baseFontDictPrivateBlocks![i].privateSize;
                    m.SetInt2(18, pSize, privOffsets[i]);
                    dicts[i] = m.BuildDeterministic();
                }

                newFdArrayBytes = Cff2IndexWriter.Build((uint)fontDictCount, new MemoryArrayCff2IndexSource(dicts));
            }

            _baseTopDictModel.SetInt(17, charStringsOffset);
            if (_hasFdArray)
            {
                _baseTopDictModel.SetInt(0x0C24, fdArrayOffset);
                _baseTopDictModel.SetInt(0x0C25, fdSelectOffset);
            }

            if (_hasVarStore)
                _baseTopDictModel.SetInt(24, varStoreOffset);

            byte[] newTopDictBytes = _baseTopDictModel.BuildDeterministic();

            bool stable = newTopDictBytes.Length == topDictBytes.Length
                && (!_hasFdArray || newFdArrayBytes.Length == fdArrayBytes.Length);

            topDictBytes = newTopDictBytes;
            fdArrayBytes = newFdArrayBytes;
            totalLength = pos;

            if (stable)
                break;
        }

        ushort finalTopDictLen = (ushort)topDictBytes.Length;
        BigEndian.WriteUInt16(headerOut, 3, finalTopDictLen);

        var w = new ArrayBufferWriter<byte>(totalLength);
        headerOut.CopyTo(w.GetSpan(headerOut.Length));
        w.Advance(headerOut.Length);
        topDictBytes.CopyTo(w.GetSpan(topDictBytes.Length));
        w.Advance(topDictBytes.Length);
        globalSubrsBytes.CopyTo(w.GetSpan(globalSubrsBytes.Length));
        w.Advance(globalSubrsBytes.Length);

        if (_hasFdSelect)
        {
            fdSelectBytes.CopyTo(w.GetSpan(fdSelectBytes.Length));
            w.Advance(fdSelectBytes.Length);
        }

        if (_hasFdArray)
        {
            fdArrayBytes.CopyTo(w.GetSpan(fdArrayBytes.Length));
            w.Advance(fdArrayBytes.Length);
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

        if (_hasVarStore)
        {
            varStoreBytes.CopyTo(w.GetSpan(varStoreBytes.Length));
            w.Advance(varStoreBytes.Length);
        }

        return w.WrittenSpan.ToArray();
    }

    private static byte[] BuildMinimalTable()
    {
        byte[] bytes = new byte[5];
        bytes[0] = 2;
        bytes[1] = 0;
        bytes[2] = 5;
        bytes[3] = 0;
        bytes[4] = 0;
        return bytes;
    }

    private readonly struct EmptyCff2IndexSource : ICff2IndexObjectSource
    {
        private readonly int _count;
        public EmptyCff2IndexSource(int count) => _count = count;
        public int Count => _count;
        public int GetLength(int index) => 0;
        public void CopyObject(int index, Span<byte> destination) { }
    }

    private readonly struct MemoryArrayCff2IndexSource : ICff2IndexObjectSource
    {
        private readonly ReadOnlyMemory<byte>[] _objects;
        public MemoryArrayCff2IndexSource(ReadOnlyMemory<byte>[] objects) => _objects = objects;
        public int Count => _objects.Length;
        public int GetLength(int index) => _objects[index].Length;
        public void CopyObject(int index, Span<byte> destination) => _objects[index].Span.CopyTo(destination);
    }

    private readonly struct OverridesCff2IndexSource : ICff2IndexObjectSource
    {
        private readonly Cff2Index _baseIndex;
        private readonly int _count;
        private readonly Dictionary<int, ReadOnlyMemory<byte>>? _overrides;

        public OverridesCff2IndexSource(Cff2Index baseIndex, int count, Dictionary<int, ReadOnlyMemory<byte>>? overrides)
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
                throw new InvalidOperationException("Invalid base CFF2 INDEX.");

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
                throw new InvalidOperationException("Invalid base CFF2 INDEX.");

            span.CopyTo(destination);
        }
    }
}
