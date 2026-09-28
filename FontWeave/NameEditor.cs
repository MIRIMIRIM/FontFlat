using System.Buffers.Binary;

namespace FontWeave;

/// <summary>One-shot, exact-ordinal edits; unchanged records retain bytes and relative order.</summary>
public static class NameEditor
{
    public static byte[] Replace(NameTable source, int ordinal, ReadOnlySpan<byte> payload)
    {
        ArgumentNullException.ThrowIfNull(source);
        ValidateSource(source);
        if (ordinal < 0 || ordinal >= source.Records.Count)
            throw new FontDataException(FontError.InvalidIndex, "name record ordinal is out of range.");
        byte[] copy = CopyPayload(payload, source.Limits);
        ValidatePayload(source.Records[ordinal].PlatformId, source.Records[ordinal].EncodingId, copy);
        var entries = ExistingEntries(source);
        entries[ordinal] = new Entry(source.Records[ordinal], copy);
        return Build(source, entries);
    }

    public static byte[] Add(NameTable source, ushort platformId, ushort encodingId,
        ushort languageId, ushort nameId, ReadOnlySpan<byte> payload)
    {
        ArgumentNullException.ThrowIfNull(source);
        ValidateSource(source);
        if (source.Records.Count == ushort.MaxValue ||
            source.Records.Count + source.LanguageTags.Count >= source.Limits.MaxNameRecords)
            throw new FontDataException(FontError.ResourceLimit, "name record count exceeds the budget.");
        if (languageId >= 0x8000 &&
            ((source.Format == 0 && (platformId < 240 || platformId > 255)) ||
             (source.Format == 1 && languageId - 0x8000 >= source.LanguageTags.Count)))
            throw new FontDataException(FontError.InvalidIndex, "new name record has an invalid language-tag reference.");
        byte[] copy = CopyPayload(payload, source.Limits);
        ValidatePayload(platformId, encodingId, copy);
        var entries = ExistingEntries(source);
        int index = 0;
        // Insert after equal keys. Duplicate identities remain distinct and stable.
        while (index < entries.Count && NameTable.CompareKeys(
            entries[index].PlatformId, entries[index].EncodingId,
            entries[index].LanguageId, entries[index].NameId,
            platformId, encodingId, languageId, nameId) <= 0)
            index++;
        entries.Insert(index, new Entry(platformId, encodingId, languageId, nameId, copy));
        return Build(source, entries);
    }

    public static byte[] Delete(NameTable source, int ordinal)
    {
        ArgumentNullException.ThrowIfNull(source);
        ValidateSource(source);
        if (ordinal < 0 || ordinal >= source.Records.Count)
            throw new FontDataException(FontError.InvalidIndex, "name record ordinal is out of range.");
        var entries = ExistingEntries(source);
        entries.RemoveAt(ordinal);
        // Format-1 tags are kept even when unused, so every remaining reference is unchanged.
        return Build(source, entries);
    }

    private static void ValidateSource(NameTable source)
    {
        if (source.StructureError is FontError structureError)
            throw new FontDataException(structureError, "name storage offset is invalid.");
        for (int i = 0; i < source.Records.Count; i++)
        {
            NameRecord record = source.Records[i];
            if (record.PayloadError is FontError payloadError)
                throw new FontDataException(payloadError, $"name record {i} has an invalid payload range.");
            if (record.DecodeError is FontError.InvalidEncoding or FontError.InvalidIndex)
                throw new FontDataException(record.DecodeError.Value, $"name record {i} is malformed.");
            if (i > 0 && NameTable.CompareKeys(source.Records[i - 1], record) > 0)
                throw new FontDataException(FontError.InvalidName, "source name records are not sorted.");
        }
        for (int i = 0; i < source.LanguageTags.Count; i++)
        {
            NameLanguageTag tag = source.LanguageTags[i];
            if (tag.PayloadError is FontError payloadError)
                throw new FontDataException(payloadError, $"language tag {i} has an invalid payload range.");
            if (tag.DecodeError is FontError decodeError)
                throw new FontDataException(decodeError, $"language tag {i} has invalid UTF-16BE.");
            if (tag.Text!.Length == 0)
                throw new FontDataException(FontError.InvalidName, $"language tag {i} is empty.");
        }
    }

    private static void ValidatePayload(ushort platformId, ushort encodingId, ReadOnlySpan<byte> payload)
    {
        if (NameTable.IsUtf16(platformId, encodingId) &&
            NameTable.ValidateUtf16(payload) is FontError error)
            throw new FontDataException(error, "new name payload is not valid UTF-16BE.");
    }

    private static byte[] CopyPayload(ReadOnlySpan<byte> payload, FontLimits limits)
    {
        if (payload.Length > ushort.MaxValue)
            throw new FontDataException(FontError.Unrepresentable, "name payload length exceeds UInt16.");
        if (payload.Length > limits.MaxNameBytes || payload.Length > limits.MaxOutputBytes)
            throw new FontDataException(FontError.ResourceLimit, "name payload exceeds the output budget.");
        return payload.ToArray();
    }

    private static List<Entry> ExistingEntries(NameTable source)
    {
        var entries = new List<Entry>(source.Records.Count + 1);
        foreach (NameRecord record in source.Records)
            entries.Add(new Entry(record, null));
        return entries;
    }

    private static byte[] Build(NameTable source, List<Entry> entries)
    {
        int count = entries.Count;
        int tagCount = source.LanguageTags.Count;
        int headerSize = checked(6 + count * 12 + (source.Format == 1 ? 2 + tagCount * 4 : 0));
        if (headerSize > ushort.MaxValue)
            throw new FontDataException(FontError.Unrepresentable, "name storageOffset exceeds UInt16.");

        long storageSize = 0;
        foreach (Entry entry in entries)
        {
            int length = entry.Length;
            if (length > 0 && storageSize > ushort.MaxValue)
                throw new FontDataException(FontError.Unrepresentable, "name payload offset exceeds UInt16.");
            storageSize += length;
        }
        foreach (NameLanguageTag tag in source.LanguageTags)
        {
            if (tag.Length > 0 && storageSize > ushort.MaxValue)
                throw new FontDataException(FontError.Unrepresentable, "language-tag payload offset exceeds UInt16.");
            storageSize += tag.Length;
        }
        long total = headerSize + storageSize;
        if (total > source.Limits.MaxNameBytes || total > source.Limits.MaxOutputBytes)
            throw new FontDataException(FontError.ResourceLimit, "rebuilt name table exceeds the output budget.");
        if (total > int.MaxValue)
            throw new FontDataException(FontError.Unrepresentable, "rebuilt name table is too large.");

        byte[] output = new byte[(int)total];
        BinaryPrimitives.WriteUInt16BigEndian(output, source.Format);
        BinaryPrimitives.WriteUInt16BigEndian(output.AsSpan(2), (ushort)count);
        BinaryPrimitives.WriteUInt16BigEndian(output.AsSpan(4), (ushort)headerSize);
        if (source.Format == 1)
            BinaryPrimitives.WriteUInt16BigEndian(output.AsSpan(6 + count * 12), (ushort)tagCount);
        int at = headerSize;
        for (int i = 0; i < count; i++)
        {
            Entry entry = entries[i];
            ReadOnlySpan<byte> payload = entry.Payload;
            Span<byte> row = output.AsSpan(6 + i * 12, 12);
            BinaryPrimitives.WriteUInt16BigEndian(row, entry.PlatformId);
            BinaryPrimitives.WriteUInt16BigEndian(row[2..], entry.EncodingId);
            BinaryPrimitives.WriteUInt16BigEndian(row[4..], entry.LanguageId);
            BinaryPrimitives.WriteUInt16BigEndian(row[6..], entry.NameId);
            BinaryPrimitives.WriteUInt16BigEndian(row[8..], (ushort)payload.Length);
            BinaryPrimitives.WriteUInt16BigEndian(row[10..], payload.IsEmpty ? (ushort)0 : (ushort)(at - headerSize));
            payload.CopyTo(output.AsSpan(at));
            at += payload.Length;
        }
        for (int i = 0; i < tagCount; i++)
        {
            NameLanguageTag tag = source.LanguageTags[i];
            ReadOnlySpan<byte> payload = tag.Payload;
            Span<byte> row = output.AsSpan(6 + count * 12 + 2 + i * 4, 4);
            BinaryPrimitives.WriteUInt16BigEndian(row, (ushort)payload.Length);
            BinaryPrimitives.WriteUInt16BigEndian(row[2..], payload.IsEmpty ? (ushort)0 : (ushort)(at - headerSize));
            payload.CopyTo(output.AsSpan(at));
            at += payload.Length;
        }
        return output;
    }

    private readonly struct Entry
    {
        private readonly NameRecord? _original;
        private readonly byte[]? _newPayload;

        public Entry(NameRecord original, byte[]? newPayload)
        {
            _original = original;
            _newPayload = newPayload;
            PlatformId = original.PlatformId;
            EncodingId = original.EncodingId;
            LanguageId = original.LanguageId;
            NameId = original.NameId;
        }

        public Entry(ushort platformId, ushort encodingId, ushort languageId, ushort nameId, byte[] payload)
        {
            _original = null;
            _newPayload = payload;
            PlatformId = platformId;
            EncodingId = encodingId;
            LanguageId = languageId;
            NameId = nameId;
        }

        public ushort PlatformId { get; }
        public ushort EncodingId { get; }
        public ushort LanguageId { get; }
        public ushort NameId { get; }
        public ReadOnlySpan<byte> Payload => _newPayload is not null ? _newPayload : _original!.Payload;
        public int Length => _newPayload?.Length ?? _original!.Length;
    }
}
