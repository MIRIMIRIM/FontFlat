using System.Buffers.Binary;
using System.Text;

namespace FontWeave;

/// <summary>A single, ordinal-identified name record. The encoded bytes are never normalized.</summary>
public sealed class NameRecord
{
    private readonly byte[] _source;
    private readonly int _start;

    internal NameRecord(int ordinal, ushort platformId, ushort encodingId, ushort languageId,
        ushort nameId, ushort length, ushort offset, byte[] source, int start,
        FontError? payloadError, FontError? decodeError)
    {
        Ordinal = ordinal;
        PlatformId = platformId;
        EncodingId = encodingId;
        LanguageId = languageId;
        NameId = nameId;
        Length = length;
        Offset = offset;
        _source = source;
        _start = start;
        PayloadError = payloadError;
        DecodeError = decodeError;
    }

    public int Ordinal { get; }
    public ushort PlatformId { get; }
    public ushort EncodingId { get; }
    public ushort LanguageId { get; }
    public ushort NameId { get; }
    public ushort Length { get; }
    public ushort Offset { get; }
    public ReadOnlySpan<byte> Payload => PayloadError is null ? _source.AsSpan(_start, Length) : [];
    public FontError? PayloadError { get; }
    public string? Text => DecodeError is null ? Encoding.BigEndianUnicode.GetString(Payload) : null;
    public FontError? DecodeError { get; }
}

/// <summary>A format-1 language tag, retaining its original index and bytes.</summary>
public sealed class NameLanguageTag
{
    private readonly byte[] _source;
    private readonly int _start;

    internal NameLanguageTag(int index, ushort length, ushort offset, byte[] source, int start,
        FontError? payloadError, FontError? decodeError)
    {
        Index = index;
        Length = length;
        Offset = offset;
        _source = source;
        _start = start;
        PayloadError = payloadError;
        DecodeError = decodeError;
    }

    public int Index { get; }
    public ushort Length { get; }
    public ushort Offset { get; }
    public ReadOnlySpan<byte> Payload => PayloadError is null ? _source.AsSpan(_start, Length) : [];
    public FontError? PayloadError { get; }
    public string? Text => DecodeError is null ? Encoding.BigEndianUnicode.GetString(Payload) : null;
    public FontError? DecodeError { get; }
}

/// <summary>An owned snapshot of a name table. Inspection keeps malformed records visible.</summary>
public sealed class NameTable
{
    private NameTable(ushort format, ushort storageOffset, NameRecord[] records,
        NameLanguageTag[] tags, string[] diagnostics, FontError? structureError, FontLimits limits)
    {
        Format = format;
        StorageOffset = storageOffset;
        Records = Array.AsReadOnly(records);
        LanguageTags = Array.AsReadOnly(tags);
        Diagnostics = Array.AsReadOnly(diagnostics);
        StructureError = structureError;
        Limits = limits;
    }

    public ushort Format { get; }
    public ushort StorageOffset { get; }
    public IReadOnlyList<NameRecord> Records { get; }
    public IReadOnlyList<NameLanguageTag> LanguageTags { get; }
    public IReadOnlyList<string> Diagnostics { get; }
    internal FontError? StructureError { get; }
    internal FontLimits Limits { get; }

    public static NameTable Parse(ReadOnlySpan<byte> bytes, FontLimits? limits = null)
    {
        limits ??= new FontLimits();
        limits.Validate();
        if (bytes.Length > limits.MaxNameBytes || bytes.Length > limits.MaxInputBytes)
            throw new FontDataException(FontError.ResourceLimit, "name table exceeds the input budget.");
        if (bytes.Length < 6)
            throw new FontDataException(FontError.Truncated, "name table header is truncated.");

        ushort format = BinaryPrimitives.ReadUInt16BigEndian(bytes);
        if (format is not (0 or 1))
            throw new FontDataException(FontError.UnsupportedFormat, $"name table format {format} is unsupported.");
        int count = BinaryPrimitives.ReadUInt16BigEndian(bytes[2..]);
        ushort storageOffset = BinaryPrimitives.ReadUInt16BigEndian(bytes[4..]);
        if (count > limits.MaxNameRecords)
            throw new FontDataException(FontError.ResourceLimit, "name record count exceeds the budget.");
        int recordsEnd = checked(6 + count * 12);
        if (recordsEnd > bytes.Length)
            throw new FontDataException(FontError.Truncated, "declared name record array is truncated.");

        int tagCount = 0;
        int tagsStart = recordsEnd;
        int headerEnd = recordsEnd;
        if (format == 1)
        {
            if (bytes.Length - recordsEnd < 2)
                throw new FontDataException(FontError.Truncated, "format-1 language-tag count is truncated.");
            tagCount = BinaryPrimitives.ReadUInt16BigEndian(bytes[recordsEnd..]);
            if (tagCount > limits.MaxNameRecords - count)
                throw new FontDataException(FontError.ResourceLimit, "combined name and language-tag count exceeds the budget.");
            tagsStart = recordsEnd + 2;
            headerEnd = checked(tagsStart + tagCount * 4);
            if (headerEnd > bytes.Length)
                throw new FontDataException(FontError.Truncated, "declared language-tag record array is truncated.");
        }

        // No caller-owned array or writable collection is retained.
        byte[] source = bytes.ToArray();
        var diagnostics = new List<string>();
        FontError? structureError = null;
        if (storageOffset < headerEnd || storageOffset > source.Length)
        {
            structureError = FontError.InvalidOffset;
            diagnostics.Add($"storageOffset {storageOffset} does not follow the complete header within the table.");
        }

        var records = new NameRecord[count];
        for (int i = 0; i < count; i++)
        {
            ReadOnlySpan<byte> row = source.AsSpan(6 + i * 12, 12);
            ushort platform = BinaryPrimitives.ReadUInt16BigEndian(row);
            ushort encoding = BinaryPrimitives.ReadUInt16BigEndian(row[2..]);
            ushort language = BinaryPrimitives.ReadUInt16BigEndian(row[4..]);
            ushort nameId = BinaryPrimitives.ReadUInt16BigEndian(row[6..]);
            ushort length = BinaryPrimitives.ReadUInt16BigEndian(row[8..]);
            ushort offset = BinaryPrimitives.ReadUInt16BigEndian(row[10..]);
            FontError? payloadError = PayloadRange(source.Length, storageOffset, headerEnd,
                length, offset, out int start);
            FontError? decodeError = payloadError;
            if (payloadError is null)
            {
                if (IsUtf16(platform, encoding))
                    decodeError = ValidateUtf16(source.AsSpan(start, length));
                else
                    decodeError = FontError.UnsupportedFormat;
            }
            if ((format == 0 && language >= 0x8000 && (platform < 240 || platform > 255)) ||
                (format == 1 && language >= 0x8000 && language - 0x8000 >= tagCount))
            {
                diagnostics.Add($"name record {i} has an invalid language-tag reference {language}.");
                decodeError = FontError.InvalidIndex;
            }
            if (payloadError is not null)
                diagnostics.Add($"name record {i} payload: {payloadError}.");
            else if (decodeError == FontError.InvalidEncoding)
                diagnostics.Add($"name record {i} has invalid UTF-16BE.");
            records[i] = new NameRecord(i, platform, encoding, language, nameId,
                length, offset, source, start, payloadError, decodeError);
        }

        var tags = new NameLanguageTag[tagCount];
        for (int i = 0; i < tagCount; i++)
        {
            ReadOnlySpan<byte> row = source.AsSpan(tagsStart + i * 4, 4);
            ushort length = BinaryPrimitives.ReadUInt16BigEndian(row);
            ushort offset = BinaryPrimitives.ReadUInt16BigEndian(row[2..]);
            FontError? payloadError = PayloadRange(source.Length, storageOffset, headerEnd,
                length, offset, out int start);
            FontError? decodeError = payloadError;
            if (payloadError is null)
                decodeError = ValidateUtf16(source.AsSpan(start, length));
            if (decodeError is not null)
                diagnostics.Add($"language tag {i}: {decodeError}.");
            tags[i] = new NameLanguageTag(i, length, offset, source, start,
                payloadError, decodeError);
        }

        for (int i = 1; i < records.Length; i++)
        {
            if (CompareKeys(records[i - 1], records[i]) > 0)
            {
                diagnostics.Add($"name records {i - 1} and {i} are not sorted by record key.");
                break;
            }
        }
        return new NameTable(format, storageOffset, records, tags, diagnostics.ToArray(), structureError, limits);
    }

    internal static bool IsUtf16(ushort platform, ushort encoding) =>
        platform == 0 || (platform == 3 && (encoding is 0 or 1 or 10));

    internal static FontError? ValidateUtf16(ReadOnlySpan<byte> bytes)
    {
        if ((bytes.Length & 1) != 0)
            return FontError.InvalidEncoding;
        for (int i = 0; i < bytes.Length; i += 2)
        {
            ushort c = BinaryPrimitives.ReadUInt16BigEndian(bytes[i..]);
            if (c is >= 0xD800 and <= 0xDBFF)
            {
                if (bytes.Length - i < 4)
                    return FontError.InvalidEncoding;
                ushort next = BinaryPrimitives.ReadUInt16BigEndian(bytes[(i + 2)..]);
                if (next is < 0xDC00 or > 0xDFFF)
                    return FontError.InvalidEncoding;
                i += 2;
            }
            else if (c is >= 0xDC00 and <= 0xDFFF)
                return FontError.InvalidEncoding;
        }
        return null;
    }

    private static FontError? PayloadRange(int total, int storageOffset, int headerEnd,
        int length, int offset, out int start)
    {
        start = 0;
        if (storageOffset < headerEnd || storageOffset > total)
            return FontError.InvalidOffset;
        long absolute = (long)storageOffset + offset;
        if (absolute > total || length > total - absolute)
            return FontError.InvalidOffset;
        start = (int)absolute;
        return null;
    }

    internal static int CompareKeys(NameRecord a, NameRecord b) =>
        CompareKeys(a.PlatformId, a.EncodingId, a.LanguageId, a.NameId,
            b.PlatformId, b.EncodingId, b.LanguageId, b.NameId);

    internal static int CompareKeys(ushort ap, ushort ae, ushort al, ushort an,
        ushort bp, ushort be, ushort bl, ushort bn)
    {
        int c = ap.CompareTo(bp);
        if (c != 0) return c;
        c = ae.CompareTo(be);
        if (c != 0) return c;
        c = al.CompareTo(bl);
        return c != 0 ? c : an.CompareTo(bn);
    }
}
