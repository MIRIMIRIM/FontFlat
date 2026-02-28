using System.Text;
using System.Threading;
using OTFontFile2.SourceGen;

namespace OTFontFile2.Tables;

/// <summary>
/// Mutable builder for the OpenType <c>name</c> table.
/// </summary>
[OtTableBuilder("name")]
public sealed partial class NameTableBuilder : ISfntTableSource
{
    private static readonly UnicodeEncoding s_utf16Be = new(bigEndian: true, byteOrderMark: false);
    private static int s_codePagesRegistered;

    private readonly List<RecordEntry> _records = new();
    private readonly List<ReadOnlyMemory<byte>> _langTagStrings = new();

    private ushort _format;

    public NameTableBuilder(ushort format = 0)
    {
        if (format is not (0 or 1))
            throw new ArgumentOutOfRangeException(nameof(format));

        _format = format;
    }

    public ushort Format
    {
        get => _format;
        set
        {
            if (value is not (0 or 1))
                throw new ArgumentOutOfRangeException(nameof(value));

            if (value == _format)
                return;

            _format = value;
            MarkDirty();
        }
    }

    public int RecordCount => _records.Count;
    public int LangTagCount => _langTagStrings.Count;

    public IReadOnlyList<RecordEntry> Records => _records;
    public IReadOnlyList<ReadOnlyMemory<byte>> LangTagStrings => _langTagStrings;

    public void Clear()
    {
        _records.Clear();
        _langTagStrings.Clear();
        MarkDirty();
    }

    public void AddOrReplaceEncodedString(ushort platformId, ushort encodingId, ushort languageId, ushort nameId, ReadOnlyMemory<byte> encoded)
    {
        if (encoded.Length > ushort.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(encoded), "Encoded string length must fit in uint16.");

        for (int i = _records.Count - 1; i >= 0; i--)
        {
            var r = _records[i];
            if (r.PlatformId == platformId && r.EncodingId == encodingId && r.LanguageId == languageId && r.NameId == nameId)
                _records.RemoveAt(i);
        }

        _records.Add(new RecordEntry(platformId, encodingId, languageId, nameId, encoded));
        MarkDirty();
    }

    public void AddOrReplaceString(ushort platformId, ushort encodingId, ushort languageId, ushort nameId, string value)
    {
        if (!TryEncodeString(platformId, encodingId, languageId, value, out byte[] encoded))
            throw new NotSupportedException($"Encoding not supported for platform={platformId}, enc={encodingId}, lang={languageId}.");

        AddOrReplaceEncodedString(platformId, encodingId, languageId, nameId, encoded);
    }

    public bool RemoveRecord(ushort platformId, ushort encodingId, ushort languageId, ushort nameId)
    {
        bool removed = false;
        for (int i = _records.Count - 1; i >= 0; i--)
        {
            var r = _records[i];
            if (r.PlatformId == platformId && r.EncodingId == encodingId && r.LanguageId == languageId && r.NameId == nameId)
            {
                _records.RemoveAt(i);
                removed = true;
            }
        }

        if (removed)
            MarkDirty();

        return removed;
    }

    public void AddLangTagString(ReadOnlyMemory<byte> encodedBcp47Utf16Be)
    {
        _langTagStrings.Add(encodedBcp47Utf16Be);
        MarkDirty();
    }

    public static bool TryFrom(NameTable name, out NameTableBuilder builder)
    {
        builder = null!;

        ushort format = name.Format;
        if (format is not (0 or 1))
            return false;

        var b = new NameTableBuilder(format);

        int count = name.Count;
        for (int i = 0; i < count; i++)
        {
            if (!name.TryGetRecord(i, out var r))
                continue;

            if (!name.TryGetEncodedStringBytes(r, out var bytes))
                continue;

            b._records.Add(new RecordEntry(r.PlatformId, r.EncodingId, r.LanguageId, r.NameId, bytes.ToArray()));
        }

        if (name.Format == 1)
        {
            if (TryReadLangTagRecords(name, out var langTags))
                b._langTagStrings.AddRange(langTags);
        }

        b.MarkDirty();
        builder = b;
        return true;
    }

    private static bool TryReadLangTagRecords(NameTable name, out List<ReadOnlyMemory<byte>> encodedTags)
    {
        encodedTags = new List<ReadOnlyMemory<byte>>();

        var table = name.Table;
        var data = table.Span;

        int count = name.Count;
        int langTagCountOffset = 6 + (count * 12);
        if ((uint)langTagCountOffset > (uint)data.Length - 2)
            return false;

        ushort langTagCount = BigEndian.ReadUInt16(data, langTagCountOffset);
        int recordsOffset = langTagCountOffset + 2;

        int recordsBytes = langTagCount * 4;
        if ((uint)recordsOffset > (uint)data.Length - (uint)recordsBytes)
            return false;

        int storageOffset = name.StringOffset;
        if ((uint)storageOffset > (uint)data.Length)
            return false;

        for (int i = 0; i < langTagCount; i++)
        {
            int o = recordsOffset + (i * 4);
            ushort length = BigEndian.ReadUInt16(data, o);
            ushort offset = BigEndian.ReadUInt16(data, o + 2);

            int abs = storageOffset + offset;
            if ((uint)abs > (uint)data.Length)
                continue;
            if ((uint)length > (uint)(data.Length - abs))
                continue;

            encodedTags.Add(data.Slice(abs, length).ToArray());
        }

        return true;
    }

    private byte[] BuildTable()
    {
        if (Format is not (0 or 1))
            throw new InvalidOperationException("name table format must be 0 or 1.");

        var records = _records.ToArray();
        Array.Sort(records, static (a, b) => a.CompareTo(b));

        int count = records.Length;
        if (count > ushort.MaxValue)
            throw new InvalidOperationException("name record count must fit in uint16.");

        int langTagCount = Format == 1 ? _langTagStrings.Count : 0;
        if (langTagCount > ushort.MaxValue)
            throw new InvalidOperationException("langTag record count must fit in uint16.");

        int langTagHeaderBytes = Format == 1 ? 2 + (langTagCount * 4) : 0;
        int stringOffset = checked(6 + (count * 12) + langTagHeaderBytes);
        if (stringOffset > ushort.MaxValue)
            throw new InvalidOperationException("name stringOffset must fit in uint16.");

        int stringStorageBytes = 0;
        for (int i = 0; i < count; i++)
        {
            int len = records[i].Encoded.Length;
            if (len > ushort.MaxValue)
                throw new InvalidOperationException("Encoded string length must fit in uint16.");

            stringStorageBytes = checked(stringStorageBytes + len);
        }

        if (Format == 1)
        {
            for (int i = 0; i < langTagCount; i++)
            {
                int len = _langTagStrings[i].Length;
                if (len > ushort.MaxValue)
                    throw new InvalidOperationException("LangTag string length must fit in uint16.");

                stringStorageBytes = checked(stringStorageBytes + len);
            }
        }

        int totalLength = checked(stringOffset + stringStorageBytes);
        if (totalLength > ushort.MaxValue)
            throw new InvalidOperationException("name table must fit in uint16-referenced storage (<= 65535 bytes).");

        byte[] table = new byte[totalLength];
        var span = table.AsSpan();

        BigEndian.WriteUInt16(span, 0, Format);
        BigEndian.WriteUInt16(span, 2, (ushort)count);
        BigEndian.WriteUInt16(span, 4, (ushort)stringOffset);

        int recordOffset = 6;
        int storagePos = stringOffset;

        for (int i = 0; i < count; i++)
        {
            ref readonly var r = ref records[i];

            BigEndian.WriteUInt16(span, recordOffset + 0, r.PlatformId);
            BigEndian.WriteUInt16(span, recordOffset + 2, r.EncodingId);
            BigEndian.WriteUInt16(span, recordOffset + 4, r.LanguageId);
            BigEndian.WriteUInt16(span, recordOffset + 6, r.NameId);

            ushort len = checked((ushort)r.Encoded.Length);
            ushort rel = checked((ushort)(storagePos - stringOffset));
            BigEndian.WriteUInt16(span, recordOffset + 8, len);
            BigEndian.WriteUInt16(span, recordOffset + 10, rel);

            r.Encoded.Span.CopyTo(span.Slice(storagePos, len));
            storagePos = checked(storagePos + len);
            recordOffset += 12;
        }

        if (Format == 1)
        {
            int langTagCountOffset = 6 + (count * 12);
            BigEndian.WriteUInt16(span, langTagCountOffset, (ushort)langTagCount);

            int langTagRecordsOffset = langTagCountOffset + 2;
            for (int i = 0; i < langTagCount; i++)
            {
                ReadOnlyMemory<byte> encoded = _langTagStrings[i];

                ushort len = checked((ushort)encoded.Length);
                ushort rel = checked((ushort)(storagePos - stringOffset));

                int o = langTagRecordsOffset + (i * 4);
                BigEndian.WriteUInt16(span, o, len);
                BigEndian.WriteUInt16(span, o + 2, rel);

                encoded.Span.CopyTo(span.Slice(storagePos, len));
                storagePos = checked(storagePos + len);
            }
        }

        return table;
    }

    private static bool TryEncodeString(ushort platformId, ushort encId, ushort langId, string value, out byte[] encoded)
    {
        encoded = Array.Empty<byte>();

        if (platformId == (ushort)NameTable.PlatformId.Unicode)
        {
            encoded = EncodeUtf16Be(value);
            return true;
        }

        if (platformId == (ushort)NameTable.PlatformId.Windows)
        {
            if (encId == 0 || encId == 1 || encId == 10)
            {
                encoded = EncodeUtf16Be(value);
                return true;
            }

            int codePage = encId switch
            {
                2 => 932,  // ShiftJIS
                3 => 936,  // PRC
                4 => 950,  // Big5
                5 => 949,  // Wansung
                6 => 1361, // Johab
                _ => -1
            };

            if (codePage != -1)
            {
                EnsureCodePagesEncodingProvider();
                encoded = Encoding.GetEncoding(codePage).GetBytes(value);
                return true;
            }

            return false;
        }

        if (platformId == (ushort)NameTable.PlatformId.Macintosh)
        {
            int codePage = GetMacCodePage(encId, langId);
            if (codePage == -1)
                return false;

            EnsureCodePagesEncodingProvider();
            encoded = Encoding.GetEncoding(codePage).GetBytes(value);
            return true;
        }

        return false;
    }

    private static void EnsureCodePagesEncodingProvider()
    {
        if (Interlocked.Exchange(ref s_codePagesRegistered, 1) == 0)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        }
    }

    private static byte[] EncodeUtf16Be(string value)
    {
        return s_utf16Be.GetBytes(value);
    }

    private static int GetMacCodePage(ushort encId, ushort langId)
    {
        // If encId == 0, some legacy fonts use language to pick code page.
        if (encId == 0)
        {
            return langId switch
            {
                11 => 10001, // Japanese
                19 => 10008, // Simplified Chinese
                33 => 10002, // Traditional Chinese
                _ => 10000   // Roman
            };
        }

        return encId switch
        {
            0 => 10000, // Roman
            1 => 10001, // Japanese
            2 => 10002, // Traditional Chinese
            3 => 10003, // Korean
            25 => 10008, // Simplified Chinese
            _ => 10000
        };
    }

    public readonly struct RecordEntry : IComparable<RecordEntry>
    {
        public ushort PlatformId { get; }
        public ushort EncodingId { get; }
        public ushort LanguageId { get; }
        public ushort NameId { get; }
        public ReadOnlyMemory<byte> Encoded { get; }

        public RecordEntry(ushort platformId, ushort encodingId, ushort languageId, ushort nameId, ReadOnlyMemory<byte> encoded)
        {
            PlatformId = platformId;
            EncodingId = encodingId;
            LanguageId = languageId;
            NameId = nameId;
            Encoded = encoded;
        }

        public int CompareTo(RecordEntry other)
        {
            int c = PlatformId.CompareTo(other.PlatformId);
            if (c != 0) return c;
            c = EncodingId.CompareTo(other.EncodingId);
            if (c != 0) return c;
            c = LanguageId.CompareTo(other.LanguageId);
            if (c != 0) return c;
            return NameId.CompareTo(other.NameId);
        }
    }
}
