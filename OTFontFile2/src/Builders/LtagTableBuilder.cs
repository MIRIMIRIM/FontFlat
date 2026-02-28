using System.Text;
using OTFontFile2.SourceGen;

namespace OTFontFile2.Tables;

/// <summary>
/// Mutable builder for the AAT <c>ltag</c> table.
/// </summary>
[OtTableBuilder("ltag")]
public sealed partial class LtagTableBuilder : ISfntTableSource
{
    private readonly List<string> _tags = new();

    private uint _version = 1;
    private uint _flags;

    public uint Version
    {
        get => _version;
        set
        {
            if (value == _version)
                return;

            _version = value;
            MarkDirty();
        }
    }

    public uint Flags
    {
        get => _flags;
        set
        {
            if (value == _flags)
                return;

            _flags = value;
            MarkDirty();
        }
    }

    public int TagCount => _tags.Count;

    public IReadOnlyList<string> Tags => _tags;

    public void Clear()
    {
        _tags.Clear();
        MarkDirty();
    }

    public int AddOrGetIndex(string tag)
    {
        ValidateLanguageTag(tag);

        for (int i = 0; i < _tags.Count; i++)
        {
            if (string.Equals(_tags[i], tag, StringComparison.Ordinal))
                return i;
        }

        _tags.Add(tag);
        MarkDirty();
        return _tags.Count - 1;
    }

    public bool Remove(string tag)
    {
        if (tag is null) throw new ArgumentNullException(nameof(tag));

        bool removed = false;
        for (int i = _tags.Count - 1; i >= 0; i--)
        {
            if (string.Equals(_tags[i], tag, StringComparison.Ordinal))
            {
                _tags.RemoveAt(i);
                removed = true;
            }
        }

        if (removed)
            MarkDirty();

        return removed;
    }

    public static bool TryFrom(LtagTable ltag, out LtagTableBuilder builder)
    {
        var b = new LtagTableBuilder
        {
            Version = ltag.Version,
            Flags = ltag.Flags
        };

        uint countU = ltag.TagCount;
        if (countU > int.MaxValue)
        {
            builder = null!;
            return false;
        }

        int count = (int)countU;
        for (int i = 0; i < count; i++)
        {
            if (!ltag.TryGetLanguageTagSpan(i, out var tagBytes))
            {
                builder = null!;
                return false;
            }

            for (int bIndex = 0; bIndex < tagBytes.Length; bIndex++)
            {
                if (tagBytes[bIndex] > 0x7Fu)
                {
                    builder = null!;
                    return false;
                }
            }

            b._tags.Add(Encoding.ASCII.GetString(tagBytes));
        }

        b.MarkDirty();
        builder = b;
        return true;
    }

    private byte[] BuildTable()
    {
        int count = _tags.Count;

        int headerSize = checked(12 + (count * 4));
        if (headerSize > ushort.MaxValue)
            throw new InvalidOperationException("ltag header is too large for uint16 offsets.");

        var poolIndexByTag = new Dictionary<string, int>(StringComparer.Ordinal);
        var pool = new List<UniqueTag>(count);

        var tagOffsets = new ushort[count];
        var tagLengths = new ushort[count];

        int poolLength = 0;
        for (int i = 0; i < count; i++)
        {
            string tag = _tags[i];
            ValidateLanguageTag(tag);

            int len = tag.Length;
            if (len > ushort.MaxValue)
                throw new InvalidOperationException("ltag language tag length exceeds uint16.");

            if (!poolIndexByTag.TryGetValue(tag, out int poolOffset))
            {
                poolOffset = poolLength;
                poolIndexByTag.Add(tag, poolOffset);
                pool.Add(new UniqueTag(tag, poolOffset, len));
                poolLength = checked(poolLength + len);
            }

            int absOffset = checked(headerSize + poolOffset);
            if (absOffset > ushort.MaxValue)
                throw new InvalidOperationException("ltag language tag offset exceeds uint16.");

            tagOffsets[i] = (ushort)absOffset;
            tagLengths[i] = (ushort)len;
        }

        int totalLength = checked(headerSize + poolLength);
        if (totalLength > ushort.MaxValue)
            throw new InvalidOperationException("ltag table is too large for uint16 offsets.");

        byte[] bytes = new byte[totalLength];
        var span = bytes.AsSpan();

        BigEndian.WriteUInt32(span, 0, Version);
        BigEndian.WriteUInt32(span, 4, Flags);
        BigEndian.WriteUInt32(span, 8, checked((uint)count));

        int recordPos = 12;
        for (int i = 0; i < count; i++)
        {
            BigEndian.WriteUInt16(span, recordPos + 0, tagOffsets[i]);
            BigEndian.WriteUInt16(span, recordPos + 2, tagLengths[i]);
            recordPos += 4;
        }

        for (int i = 0; i < pool.Count; i++)
        {
            var entry = pool[i];
            int dest = checked(headerSize + entry.PoolOffset);
            Encoding.ASCII.GetBytes(entry.Tag.AsSpan(), span.Slice(dest, entry.Length));
        }

        return bytes;
    }

    private static void ValidateLanguageTag(string tag)
    {
        if (tag is null) throw new ArgumentNullException(nameof(tag));

        for (int i = 0; i < tag.Length; i++)
        {
            if (tag[i] > 0x7F)
                throw new ArgumentOutOfRangeException(nameof(tag), "ltag language tags must be ASCII.");
        }

        if (tag.Length > ushort.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(tag), "ltag language tag length must fit in uint16.");
    }

    private readonly struct UniqueTag
    {
        public UniqueTag(string tag, int poolOffset, int length)
        {
            Tag = tag;
            PoolOffset = poolOffset;
            Length = length;
        }

        public string Tag { get; }
        public int PoolOffset { get; }
        public int Length { get; }
    }
}

