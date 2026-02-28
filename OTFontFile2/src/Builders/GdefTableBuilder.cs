using OTFontFile2.SourceGen;
using System.Runtime.InteropServices;

namespace OTFontFile2.Tables;

/// <summary>
/// Mutable builder for the OpenType <c>GDEF</c> table.
/// </summary>
[OtTableBuilder("GDEF")]
public sealed partial class GdefTableBuilder : ISfntTableSource
{
    private Fixed1616 _version = new(0x00010000u);

    private ReadOnlyMemory<byte> _glyphClassDef = ReadOnlyMemory<byte>.Empty;
    private ReadOnlyMemory<byte> _attachList = ReadOnlyMemory<byte>.Empty;
    private ReadOnlyMemory<byte> _ligCaretList = ReadOnlyMemory<byte>.Empty;
    private ReadOnlyMemory<byte> _markAttachClassDef = ReadOnlyMemory<byte>.Empty;
    private ReadOnlyMemory<byte> _markGlyphSetsDef = ReadOnlyMemory<byte>.Empty;
    private ReadOnlyMemory<byte> _itemVariationStore = ReadOnlyMemory<byte>.Empty;

    private bool _isRaw;
    private ReadOnlyMemory<byte> _rawData;

    public GdefTableBuilder()
    {
        Clear();
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
                    throw new InvalidOperationException("GDEF raw table data is too short.");

                BigEndian.WriteUInt32(bytes, 0, value.RawValue);
                _rawData = bytes;
                _dirty = true;
                return;
            }

            if (value == _version)
                return;

            _version = value;
            MarkDirty();
        }
    }

    public bool IsRaw => _isRaw;

    public bool HasGlyphClassDef => !_glyphClassDef.IsEmpty;
    public bool HasMarkAttachClassDef => !_markAttachClassDef.IsEmpty;

    public ReadOnlyMemory<byte> GlyphClassDefBytes => _glyphClassDef;
    public ReadOnlyMemory<byte> MarkAttachClassDefBytes => _markAttachClassDef;

    public ReadOnlyMemory<byte> AttachListBytes => _attachList;
    public ReadOnlyMemory<byte> LigCaretListBytes => _ligCaretList;
    public ReadOnlyMemory<byte> MarkGlyphSetsDefBytes => _markGlyphSetsDef;
    public ReadOnlyMemory<byte> ItemVariationStoreBytes => _itemVariationStore;

    public ReadOnlyMemory<byte> DataBytes => EnsureBuilt();

    public void Clear()
    {
        _isRaw = false;
        _rawData = ReadOnlyMemory<byte>.Empty;

        _version = new Fixed1616(0x00010000u);
        _glyphClassDef = ReadOnlyMemory<byte>.Empty;
        _attachList = ReadOnlyMemory<byte>.Empty;
        _ligCaretList = ReadOnlyMemory<byte>.Empty;
        _markAttachClassDef = ReadOnlyMemory<byte>.Empty;
        _markGlyphSetsDef = ReadOnlyMemory<byte>.Empty;
        _itemVariationStore = ReadOnlyMemory<byte>.Empty;

        MarkDirty();
    }

    public void SetTableData(ReadOnlyMemory<byte> data)
    {
        if (data.Length < 12)
            throw new ArgumentException("GDEF table must be at least 12 bytes.", nameof(data));

        _isRaw = true;
        _rawData = data;
        _built = null;
        _dirty = true;
    }

    public void SetGlyphClassDef(ClassDefTableBuilder classDef)
    {
        if (classDef is null) throw new ArgumentNullException(nameof(classDef));

        EnsureStructured();
        _glyphClassDef = classDef.ToMemory();
        MarkDirty();
    }

    public void ClearGlyphClassDef()
    {
        EnsureStructured();
        if (_glyphClassDef.IsEmpty)
            return;

        _glyphClassDef = ReadOnlyMemory<byte>.Empty;
        MarkDirty();
    }

    public void SetMarkAttachClassDef(ClassDefTableBuilder classDef)
    {
        if (classDef is null) throw new ArgumentNullException(nameof(classDef));

        EnsureStructured();
        _markAttachClassDef = classDef.ToMemory();
        MarkDirty();
    }

    public void ClearMarkAttachClassDef()
    {
        EnsureStructured();
        if (_markAttachClassDef.IsEmpty)
            return;

        _markAttachClassDef = ReadOnlyMemory<byte>.Empty;
        MarkDirty();
    }

    public void SetAttachListData(ReadOnlyMemory<byte> attachListBytes)
    {
        EnsureStructured();
        _attachList = attachListBytes;
        MarkDirty();
    }

    public void SetAttachList(GdefAttachListBuilder attachList)
    {
        if (attachList is null) throw new ArgumentNullException(nameof(attachList));

        EnsureStructured();
        _attachList = attachList.ToMemory();
        MarkDirty();
    }

    public void ClearAttachList()
    {
        EnsureStructured();
        if (_attachList.IsEmpty)
            return;

        _attachList = ReadOnlyMemory<byte>.Empty;
        MarkDirty();
    }

    public void SetLigCaretListData(ReadOnlyMemory<byte> ligCaretListBytes)
    {
        EnsureStructured();
        _ligCaretList = ligCaretListBytes;
        MarkDirty();
    }

    public void SetLigCaretList(GdefLigCaretListBuilder ligCaretList)
    {
        if (ligCaretList is null) throw new ArgumentNullException(nameof(ligCaretList));

        EnsureStructured();
        _ligCaretList = ligCaretList.ToMemory();
        MarkDirty();
    }

    public void ClearLigCaretList()
    {
        EnsureStructured();
        if (_ligCaretList.IsEmpty)
            return;

        _ligCaretList = ReadOnlyMemory<byte>.Empty;
        MarkDirty();
    }

    public void SetMarkGlyphSetsDefData(ReadOnlyMemory<byte> markGlyphSetsDefBytes)
    {
        EnsureStructured();
        _markGlyphSetsDef = markGlyphSetsDefBytes;
        MarkDirty();
    }

    public void SetMarkGlyphSetsDef(GdefMarkGlyphSetsDefBuilder markGlyphSetsDef)
    {
        if (markGlyphSetsDef is null) throw new ArgumentNullException(nameof(markGlyphSetsDef));

        EnsureStructured();
        _markGlyphSetsDef = markGlyphSetsDef.ToMemory();
        MarkDirty();
    }

    public void ClearMarkGlyphSetsDef()
    {
        EnsureStructured();
        if (_markGlyphSetsDef.IsEmpty)
            return;

        _markGlyphSetsDef = ReadOnlyMemory<byte>.Empty;
        MarkDirty();
    }

    public void SetItemVariationStoreData(ReadOnlyMemory<byte> itemVariationStoreBytes)
    {
        EnsureStructured();
        _itemVariationStore = itemVariationStoreBytes;
        MarkDirty();
    }

    public void ClearItemVariationStore()
    {
        EnsureStructured();
        if (_itemVariationStore.IsEmpty)
            return;

        _itemVariationStore = ReadOnlyMemory<byte>.Empty;
        MarkDirty();
    }

    public static bool TryFrom(GdefTable gdef, out GdefTableBuilder builder)
    {
        builder = null!;

        var span = gdef.Table.Span;
        int length = span.Length;
        if (length < 12)
            return false;

        var b = new GdefTableBuilder
        {
            _isRaw = false,
            _rawData = ReadOnlyMemory<byte>.Empty,
            _version = gdef.Version
        };

        int glyphClassDefOffset = gdef.GlyphClassDefOffset;
        int attachListOffset = gdef.AttachListOffset;
        int ligCaretListOffset = gdef.LigCaretListOffset;
        int markAttachClassDefOffset = gdef.MarkAttachClassDefOffset;
        int markGlyphSetsDefOffset = gdef.MarkGlyphSetsDefOffset;

        uint itemVarStoreOffsetU = gdef.ItemVarStoreOffset;
        int itemVarStoreOffset = 0;
        if (itemVarStoreOffsetU != 0)
        {
            if (itemVarStoreOffsetU > int.MaxValue)
                return false;
            itemVarStoreOffset = (int)itemVarStoreOffsetU;
        }

        Span<(int offset, int kind)> sections = stackalloc (int, int)[6];
        int count = 0;
        if (glyphClassDefOffset != 0) sections[count++] = (glyphClassDefOffset, 0);
        if (attachListOffset != 0) sections[count++] = (attachListOffset, 1);
        if (ligCaretListOffset != 0) sections[count++] = (ligCaretListOffset, 2);
        if (markAttachClassDefOffset != 0) sections[count++] = (markAttachClassDefOffset, 3);
        if (markGlyphSetsDefOffset != 0) sections[count++] = (markGlyphSetsDefOffset, 4);
        if (itemVarStoreOffset != 0) sections[count++] = (itemVarStoreOffset, 5);

        for (int i = 0; i < count; i++)
        {
            int start = sections[i].offset;
            if ((uint)start > (uint)length)
                return false;
        }

        sections.Slice(0, count).Sort(static (a, b) => a.offset.CompareTo(b.offset));

        for (int i = 0; i < count; i++)
        {
            int start = sections[i].offset;
            int end = i + 1 < count ? sections[i + 1].offset : length;
            if (end < start || (uint)end > (uint)length)
                return false;

            var bytes = span.Slice(start, end - start).ToArray();
            switch (sections[i].kind)
            {
                case 0:
                    b._glyphClassDef = bytes;
                    break;
                case 1:
                    b._attachList = bytes;
                    break;
                case 2:
                    b._ligCaretList = bytes;
                    break;
                case 3:
                    b._markAttachClassDef = bytes;
                    break;
                case 4:
                    b._markGlyphSetsDef = bytes;
                    break;
                case 5:
                    b._itemVariationStore = bytes;
                    break;
            }
        }

        b.MarkDirty();
        builder = b;
        return true;
    }

    private void EnsureStructured()
    {
        if (!_isRaw)
            return;

        // Best-effort import of known offsets from the raw table; if it fails, we drop the raw bytes.
        if (TryImportRaw(_rawData.Span))
            return;

        _isRaw = false;
        _rawData = ReadOnlyMemory<byte>.Empty;
        _glyphClassDef = ReadOnlyMemory<byte>.Empty;
        _attachList = ReadOnlyMemory<byte>.Empty;
        _ligCaretList = ReadOnlyMemory<byte>.Empty;
        _markAttachClassDef = ReadOnlyMemory<byte>.Empty;
        _markGlyphSetsDef = ReadOnlyMemory<byte>.Empty;
        _itemVariationStore = ReadOnlyMemory<byte>.Empty;
        _version = new Fixed1616(0x00010000u);
        MarkDirty();
    }

    private bool TryImportRaw(ReadOnlySpan<byte> data)
    {
        if (data.Length < 12)
            return false;

        uint versionRaw = BigEndian.ReadUInt32(data, 0);

        int glyphClassDefOffset = BigEndian.ReadUInt16(data, 4);
        int attachListOffset = BigEndian.ReadUInt16(data, 6);
        int ligCaretListOffset = BigEndian.ReadUInt16(data, 8);
        int markAttachClassDefOffset = BigEndian.ReadUInt16(data, 10);

        int markGlyphSetsDefOffset = 0;
        if (versionRaw > 0x00010000u && data.Length >= 14)
            markGlyphSetsDefOffset = BigEndian.ReadUInt16(data, 12);

        int itemVarStoreOffset = 0;
        if (versionRaw >= 0x00010003u && data.Length >= 18)
        {
            uint u = BigEndian.ReadUInt32(data, 14);
            if (u > int.MaxValue)
                return false;
            itemVarStoreOffset = (int)u;
        }

        Span<(int offset, int kind)> sections = stackalloc (int, int)[6];
        int count = 0;
        if (glyphClassDefOffset != 0) sections[count++] = (glyphClassDefOffset, 0);
        if (attachListOffset != 0) sections[count++] = (attachListOffset, 1);
        if (ligCaretListOffset != 0) sections[count++] = (ligCaretListOffset, 2);
        if (markAttachClassDefOffset != 0) sections[count++] = (markAttachClassDefOffset, 3);
        if (markGlyphSetsDefOffset != 0) sections[count++] = (markGlyphSetsDefOffset, 4);
        if (itemVarStoreOffset != 0) sections[count++] = (itemVarStoreOffset, 5);

        for (int i = 0; i < count; i++)
        {
            int start = sections[i].offset;
            if ((uint)start > (uint)data.Length)
                return false;
        }

        sections.Slice(0, count).Sort(static (a, b) => a.offset.CompareTo(b.offset));

        _version = new Fixed1616(versionRaw);
        _glyphClassDef = ReadOnlyMemory<byte>.Empty;
        _attachList = ReadOnlyMemory<byte>.Empty;
        _ligCaretList = ReadOnlyMemory<byte>.Empty;
        _markAttachClassDef = ReadOnlyMemory<byte>.Empty;
        _markGlyphSetsDef = ReadOnlyMemory<byte>.Empty;
        _itemVariationStore = ReadOnlyMemory<byte>.Empty;

        for (int i = 0; i < count; i++)
        {
            int start = sections[i].offset;
            int end = i + 1 < count ? sections[i + 1].offset : data.Length;
            if (end < start)
                return false;

            byte[] bytes = data.Slice(start, end - start).ToArray();
            switch (sections[i].kind)
            {
                case 0:
                    _glyphClassDef = bytes;
                    break;
                case 1:
                    _attachList = bytes;
                    break;
                case 2:
                    _ligCaretList = bytes;
                    break;
                case 3:
                    _markAttachClassDef = bytes;
                    break;
                case 4:
                    _markGlyphSetsDef = bytes;
                    break;
                case 5:
                    _itemVariationStore = bytes;
                    break;
            }
        }

        _isRaw = false;
        _rawData = ReadOnlyMemory<byte>.Empty;
        _built = null;
        _dirty = true;
        return true;
    }

    private byte[] BuildTable()
    {
        if (_isRaw)
            return GetRawBytes();

        return BuildStructuredTable();
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

    private byte[] BuildStructuredTable()
    {
        uint versionRaw = _version.RawValue;

        if (!_itemVariationStore.IsEmpty && versionRaw < 0x00010003u)
            versionRaw = 0x00010003u;

        if (_itemVariationStore.IsEmpty && !_markGlyphSetsDef.IsEmpty && versionRaw <= 0x00010000u)
            versionRaw = 0x00010002u;

        _version = new Fixed1616(versionRaw);
        int headerLen = versionRaw >= 0x00010003u ? 18 : (versionRaw > 0x00010000u ? 14 : 12);

        int pos = headerLen;

        int glyphClassDefOffset = 0;
        if (!_glyphClassDef.IsEmpty)
        {
            pos = Align2(pos);
            glyphClassDefOffset = pos;
            pos = checked(pos + _glyphClassDef.Length);
        }

        int attachListOffset = 0;
        if (!_attachList.IsEmpty)
        {
            pos = Align2(pos);
            attachListOffset = pos;
            pos = checked(pos + _attachList.Length);
        }

        int ligCaretListOffset = 0;
        if (!_ligCaretList.IsEmpty)
        {
            pos = Align2(pos);
            ligCaretListOffset = pos;
            pos = checked(pos + _ligCaretList.Length);
        }

        int markAttachClassDefOffset = 0;
        if (!_markAttachClassDef.IsEmpty)
        {
            pos = Align2(pos);
            markAttachClassDefOffset = pos;
            pos = checked(pos + _markAttachClassDef.Length);
        }

        int markGlyphSetsDefOffset = 0;
        if (!_markGlyphSetsDef.IsEmpty)
        {
            pos = Align2(pos);
            markGlyphSetsDefOffset = pos;
            pos = checked(pos + _markGlyphSetsDef.Length);
        }

        int itemVarStoreOffset = 0;
        if (!_itemVariationStore.IsEmpty)
        {
            pos = Align2(pos);
            itemVarStoreOffset = pos;
            pos = checked(pos + _itemVariationStore.Length);
        }

        if (glyphClassDefOffset > ushort.MaxValue || attachListOffset > ushort.MaxValue || ligCaretListOffset > ushort.MaxValue || markAttachClassDefOffset > ushort.MaxValue || markGlyphSetsDefOffset > ushort.MaxValue)
            throw new InvalidOperationException("GDEF offset must fit in uint16.");

        byte[] bytes = new byte[pos];
        var span = bytes.AsSpan();

        BigEndian.WriteUInt32(span, 0, versionRaw);
        BigEndian.WriteUInt16(span, 4, (ushort)glyphClassDefOffset);
        BigEndian.WriteUInt16(span, 6, (ushort)attachListOffset);
        BigEndian.WriteUInt16(span, 8, (ushort)ligCaretListOffset);
        BigEndian.WriteUInt16(span, 10, (ushort)markAttachClassDefOffset);

        if (headerLen >= 14)
            BigEndian.WriteUInt16(span, 12, (ushort)markGlyphSetsDefOffset);

        if (headerLen >= 18)
            BigEndian.WriteUInt32(span, 14, (uint)itemVarStoreOffset);

        if (glyphClassDefOffset != 0)
            _glyphClassDef.Span.CopyTo(span.Slice(glyphClassDefOffset));
        if (attachListOffset != 0)
            _attachList.Span.CopyTo(span.Slice(attachListOffset));
        if (ligCaretListOffset != 0)
            _ligCaretList.Span.CopyTo(span.Slice(ligCaretListOffset));
        if (markAttachClassDefOffset != 0)
            _markAttachClassDef.Span.CopyTo(span.Slice(markAttachClassDefOffset));
        if (markGlyphSetsDefOffset != 0)
            _markGlyphSetsDef.Span.CopyTo(span.Slice(markGlyphSetsDefOffset));
        if (itemVarStoreOffset != 0)
            _itemVariationStore.Span.CopyTo(span.Slice(itemVarStoreOffset));

        return bytes;
    }

    private static int Align2(int offset) => (offset + 1) & ~1;

    private static byte[] BuildMinimalTable(uint versionRaw)
    {
        byte[] bytes = new byte[12];
        var span = bytes.AsSpan();
        BigEndian.WriteUInt32(span, 0, versionRaw);
        // offsets left as zero
        return bytes;
    }
}
