namespace OTFontFile2.Tables;

using OTFontFile2.SourceGen;
using System.Text;

/// <summary>
/// Mutable builder for the OpenType <c>post</c> table.
/// </summary>
[OtTableBuilder("post")]
public sealed partial class PostTableBuilder : ISfntTableSource
{
    private const uint Version10 = 0x00010000u;
    private const uint Version20 = 0x00020000u;
    private const uint Version30 = 0x00030000u;

    private Fixed1616 _version = new(Version30);
    private Fixed1616 _italicAngle;
    private short _underlinePosition;
    private short _underlineThickness;
    private uint _isFixedPitch;
    private uint _minMemType42;
    private uint _maxMemType42;
    private uint _minMemType1;
    private uint _maxMemType1;

    private ushort _numberOfGlyphs;
    private readonly List<ushort> _glyphNameIndex = new();
    private readonly List<byte[]> _customNames = new();

    public Fixed1616 Version
    {
        get => _version;
        set
        {
            uint raw = value.RawValue;
            if (raw is not (Version10 or Version20 or Version30))
                throw new ArgumentOutOfRangeException(nameof(value), "post version must be 1.0, 2.0, or 3.0.");

            if (value == _version)
                return;

            _version = value;
            MarkDirty();
        }
    }

    public bool IsVersion2 => Version.RawValue == Version20;

    public Fixed1616 ItalicAngle
    {
        get => _italicAngle;
        set
        {
            if (value == _italicAngle)
                return;

            _italicAngle = value;
            MarkDirty();
        }
    }

    public short UnderlinePosition
    {
        get => _underlinePosition;
        set
        {
            if (value == _underlinePosition)
                return;

            _underlinePosition = value;
            MarkDirty();
        }
    }

    public short UnderlineThickness
    {
        get => _underlineThickness;
        set
        {
            if (value == _underlineThickness)
                return;

            _underlineThickness = value;
            MarkDirty();
        }
    }

    public uint IsFixedPitch
    {
        get => _isFixedPitch;
        set
        {
            if (value == _isFixedPitch)
                return;

            _isFixedPitch = value;
            MarkDirty();
        }
    }

    public uint MinMemType42
    {
        get => _minMemType42;
        set
        {
            if (value == _minMemType42)
                return;

            _minMemType42 = value;
            MarkDirty();
        }
    }

    public uint MaxMemType42
    {
        get => _maxMemType42;
        set
        {
            if (value == _maxMemType42)
                return;

            _maxMemType42 = value;
            MarkDirty();
        }
    }

    public uint MinMemType1
    {
        get => _minMemType1;
        set
        {
            if (value == _minMemType1)
                return;

            _minMemType1 = value;
            MarkDirty();
        }
    }

    public uint MaxMemType1
    {
        get => _maxMemType1;
        set
        {
            if (value == _maxMemType1)
                return;

            _maxMemType1 = value;
            MarkDirty();
        }
    }

    public ushort NumberOfGlyphs
    {
        get => _numberOfGlyphs;
        set
        {
            if (value == _numberOfGlyphs)
                return;

            _numberOfGlyphs = value;
            EnsureGlyphNameIndexSize(value);
            MarkDirty();
        }
    }

    public int GlyphNameIndexCount => _glyphNameIndex.Count;
    public int CustomNameCount => _customNames.Count;

    public void ClearGlyphNames()
    {
        _numberOfGlyphs = 0;
        _glyphNameIndex.Clear();
        _customNames.Clear();
        MarkDirty();
    }

    public void SetGlyphNameIndex(ushort glyphId, ushort nameIndex)
    {
        if (!IsVersion2)
            throw new InvalidOperationException("Glyph names are only stored in post version 2.0.");

        if (glyphId >= _numberOfGlyphs)
            throw new ArgumentOutOfRangeException(nameof(glyphId));

        if (_glyphNameIndex[(int)glyphId] == nameIndex)
            return;

        _glyphNameIndex[(int)glyphId] = nameIndex;
        MarkDirty();
    }

    public bool TryGetGlyphNameIndex(ushort glyphId, out ushort nameIndex)
    {
        nameIndex = 0;

        if (!IsVersion2)
            return false;

        if (glyphId >= _numberOfGlyphs)
            return false;

        if ((uint)glyphId >= (uint)_glyphNameIndex.Count)
            return false;

        nameIndex = _glyphNameIndex[(int)glyphId];
        return true;
    }

    public int AddCustomName(string ascii)
    {
        if (ascii is null) throw new ArgumentNullException(nameof(ascii));

        int byteCount = Encoding.ASCII.GetByteCount(ascii);
        if (byteCount > 255)
            throw new ArgumentOutOfRangeException(nameof(ascii), "Custom post names must be <= 255 ASCII bytes.");

        byte[] bytes = Encoding.ASCII.GetBytes(ascii);
        _customNames.Add(bytes);
        MarkDirty();
        return _customNames.Count - 1;
    }

    public void SetCustomNameBytes(int index, ReadOnlySpan<byte> asciiBytes)
    {
        if ((uint)index >= (uint)_customNames.Count)
            throw new ArgumentOutOfRangeException(nameof(index));

        if (asciiBytes.Length > 255)
            throw new ArgumentOutOfRangeException(nameof(asciiBytes), "Custom post names must be <= 255 bytes.");

        byte[] copy = asciiBytes.ToArray();
        _customNames[index] = copy;
        MarkDirty();
    }

    public static bool TryFrom(PostTable post, out PostTableBuilder builder)
    {
        builder = null!;

        uint version = post.Version.RawValue;
        if (version is not (Version10 or Version20 or Version30))
            return false;

        var b = new PostTableBuilder
        {
            Version = post.Version,
            ItalicAngle = post.ItalicAngle,
            UnderlinePosition = post.UnderlinePosition,
            UnderlineThickness = post.UnderlineThickness,
            IsFixedPitch = post.IsFixedPitch,
            MinMemType42 = post.MinMemType42,
            MaxMemType42 = post.MaxMemType42,
            MinMemType1 = post.MinMemType1,
            MaxMemType1 = post.MaxMemType1
        };

        if (version == Version20)
        {
            if (!TryReadVersion2(post, b))
                return false;
        }

        b.MarkDirty();
        builder = b;
        return true;
    }

    private static bool TryReadVersion2(PostTable post, PostTableBuilder builder)
    {
        var span = post.Table.Span;
        if (span.Length < 34)
            return false;

        ushort numGlyphs = BigEndian.ReadUInt16(span, 32);

        int indicesOffset = 34;
        int indicesBytes = checked(numGlyphs * 2);
        if ((uint)indicesOffset > (uint)span.Length - (uint)indicesBytes)
            return false;

        builder._numberOfGlyphs = numGlyphs;
        builder.EnsureGlyphNameIndexSize(numGlyphs);

        int standardCount = PostStandardNames.Values.Length;

        ushort maxNameIndex = 0;
        int pos = indicesOffset;
        for (int i = 0; i < numGlyphs; i++)
        {
            ushort nameIndex = BigEndian.ReadUInt16(span, pos);
            builder._glyphNameIndex[i] = nameIndex;
            if (nameIndex > maxNameIndex)
                maxNameIndex = nameIndex;
            pos += 2;
        }

        int customCount = 0;
        if (maxNameIndex >= standardCount)
            customCount = (maxNameIndex - standardCount) + 1;

        builder._customNames.Clear();
        builder._customNames.Capacity = Math.Max(builder._customNames.Capacity, customCount);

        pos = indicesOffset + indicesBytes;
        for (int i = 0; i < customCount; i++)
        {
            if ((uint)pos >= (uint)span.Length)
                return false;

            int len = span[pos];
            pos++;

            if ((uint)pos > (uint)span.Length - (uint)len)
                return false;

            builder._customNames.Add(span.Slice(pos, len).ToArray());
            pos += len;
        }

        return true;
    }

    private void EnsureGlyphNameIndexSize(ushort numGlyphs)
    {
        int count = _glyphNameIndex.Count;
        int desired = numGlyphs;

        if (count == desired)
            return;

        if (count < desired)
        {
            int add = desired - count;
            _glyphNameIndex.Capacity = Math.Max(_glyphNameIndex.Capacity, desired);
            for (int i = 0; i < add; i++)
                _glyphNameIndex.Add(0);
            return;
        }

        _glyphNameIndex.RemoveRange(desired, count - desired);
    }

    private byte[] BuildTable()
        => Version.RawValue == Version20 ? BuildVersion2Table() : BuildHeaderOnlyTable();

    private byte[] BuildHeaderOnlyTable()
    {
        byte[] table = new byte[32];
        WriteHeader(table.AsSpan());
        return table;
    }

    private byte[] BuildVersion2Table()
    {
        if (_glyphNameIndex.Count != _numberOfGlyphs)
            throw new InvalidOperationException("post glyphNameIndex count must match numberOfGlyphs.");

        int standardCount = PostStandardNames.Values.Length;

        int maxCustomIndex = -1;
        for (int i = 0; i < _glyphNameIndex.Count; i++)
        {
            int nameIndex = _glyphNameIndex[i];
            if (nameIndex >= standardCount)
            {
                int customIndex = nameIndex - standardCount;
                if (customIndex > maxCustomIndex)
                    maxCustomIndex = customIndex;
            }
        }

        if (maxCustomIndex >= _customNames.Count)
            throw new InvalidOperationException("post custom names list does not contain all referenced indices.");

        int stringBytes = 0;
        for (int i = 0; i < _customNames.Count; i++)
        {
            int len = _customNames[i].Length;
            if ((uint)len > 255u)
                throw new InvalidOperationException("post custom name length must fit in a uint8.");

            stringBytes = checked(stringBytes + 1 + len);
        }

        int length = checked(34 + (_numberOfGlyphs * 2) + stringBytes);
        byte[] table = new byte[length];
        var span = table.AsSpan();

        WriteHeader(span);
        BigEndian.WriteUInt16(span, 32, _numberOfGlyphs);

        int pos = 34;
        for (int i = 0; i < _glyphNameIndex.Count; i++)
        {
            BigEndian.WriteUInt16(span, pos, _glyphNameIndex[i]);
            pos += 2;
        }

        for (int i = 0; i < _customNames.Count; i++)
        {
            byte[] bytes = _customNames[i];
            span[pos] = checked((byte)bytes.Length);
            pos++;
            bytes.AsSpan().CopyTo(span.Slice(pos, bytes.Length));
            pos += bytes.Length;
        }

        return table;
    }

    private void WriteHeader(Span<byte> span)
    {
        if (span.Length < 32)
            throw new ArgumentException("post header requires 32 bytes.", nameof(span));

        BigEndian.WriteUInt32(span, 0, Version.RawValue);
        BigEndian.WriteUInt32(span, 4, ItalicAngle.RawValue);
        BigEndian.WriteInt16(span, 8, UnderlinePosition);
        BigEndian.WriteInt16(span, 10, UnderlineThickness);
        BigEndian.WriteUInt32(span, 12, IsFixedPitch);
        BigEndian.WriteUInt32(span, 16, MinMemType42);
        BigEndian.WriteUInt32(span, 20, MaxMemType42);
        BigEndian.WriteUInt32(span, 24, MinMemType1);
        BigEndian.WriteUInt32(span, 28, MaxMemType1);
    }
}
