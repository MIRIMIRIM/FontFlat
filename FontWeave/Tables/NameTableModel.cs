using FontWeave.Core;

namespace FontWeave.Tables;

/// <summary>The finite decoder's result. Unsupported bytes remain available verbatim.</summary>
public enum NameDecodeState { Decoded, UnsupportedEncoding, UnmappedCharacter }

/// <summary>A validated name record, retaining its original ordinal and wire identity.</summary>
public sealed class NameRecordModel
{
    internal NameRecordModel(int ordinal, ushort platformId, ushort encodingId,
        ushort languageId, ushort nameId, ushort length, ushort storageRelativeOffset,
        byte[] payload, NameDecodeState decodeState, string? text,
        DataLocation rowLocation, DataLocation payloadLocation, NameLegacyEncoding? legacyEncoding = null)
    {
        Ordinal = ordinal;
        PlatformId = platformId;
        EncodingId = encodingId;
        LanguageId = languageId;
        NameId = nameId;
        Length = length;
        StorageRelativeOffset = storageRelativeOffset;
        Payload = payload;
        DecodeState = decodeState;
        Text = text;
        RowLocation = rowLocation;
        PayloadLocation = payloadLocation;
        LegacyEncoding = legacyEncoding;
    }

    public int Ordinal { get; }
    public ushort PlatformId { get; }
    public ushort EncodingId { get; }
    public ushort LanguageId { get; }
    public ushort NameId { get; }
    public ushort Length { get; }
    public ushort StorageRelativeOffset { get; }
    public ReadOnlyMemory<byte> Payload { get; }
    public NameDecodeState DecodeState { get; }
    /// <summary>Decoded characters and transcoding hints. Selected Apple RTL mappings include
    /// canonical LRO/RLO ... PDF runs; this is not an undecorated name-ID validation value.
    /// Supported historical Unicode positions expose modern identities, not raw code units;
    /// Unicode 1.0 currently covers ASCII and its 2350 Hangul positions.
    /// Unsupported or unmapped historical positions leave Text null and preserve Payload.</summary>
    public string? Text { get; }
    public DataLocation RowLocation { get; }
    public DataLocation PayloadLocation { get; }
    public NameLegacyEncoding? LegacyEncoding { get; }
}

/// <summary>A validated format 1 language-tag record.</summary>
public sealed class NameLanguageTagModel
{
    internal NameLanguageTagModel(int ordinal, ushort length, ushort storageRelativeOffset,
        byte[] payload, string text, DataLocation rowLocation, DataLocation payloadLocation)
    {
        Ordinal = ordinal;
        Length = length;
        StorageRelativeOffset = storageRelativeOffset;
        Payload = payload;
        Text = text;
        RowLocation = rowLocation;
        PayloadLocation = payloadLocation;
    }

    public int Ordinal { get; }
    public ushort Length { get; }
    public ushort StorageRelativeOffset { get; }
    public ReadOnlyMemory<byte> Payload { get; }
    public string Text { get; }
    public DataLocation RowLocation { get; }
    public DataLocation PayloadLocation { get; }
}

/// <summary>A safely read, source-independent format 0/1 name table. Source order is retained.</summary>
public sealed class NameTableModel
{
    internal NameTableModel(ushort format, ushort storageOffset, byte[] originalBytes,
        NameRecordModel[] records, NameLanguageTagModel[] languageTags, DataLocation location)
    {
        Format = format;
        StorageOffset = storageOffset;
        OriginalBytes = originalBytes;
        Records = Array.AsReadOnly(records);
        LanguageTags = Array.AsReadOnly(languageTags);
        Location = location;
    }

    public ushort Format { get; }
    public ushort StorageOffset { get; }
    public ReadOnlyMemory<byte> OriginalBytes { get; }
    public IReadOnlyList<NameRecordModel> Records { get; }
    public IReadOnlyList<NameLanguageTagModel> LanguageTags { get; }
    public DataLocation Location { get; }

    public NameLanguageIdentity GetLanguage(int ordinal)
    {
        NameRecordModel record = Records[ordinal];
        if (Format == 1 && record.LanguageId >= 0x8000)
            return new(NameLanguageKind.LanguageTag, record.PlatformId, record.LanguageId,
                LanguageTags[record.LanguageId - 0x8000].Text);
        if (record.PlatformId == 0 && record.LanguageId == 0)
            return NameLanguageIdentity.Unspecified;
        return NameLanguageIdentity.ForPlatform(record.PlatformId, record.LanguageId);
    }

    /// <summary>
    /// Materialize a complete editable value, preserving unreferenced bytes. Tag array edits
    /// never implicitly remap language IDs: the caller supplies the final references.
    /// </summary>
    public NameTableContent ToContent()
    {
        var records = new NameRecordValue[Records.Count];
        var tags = new string[LanguageTags.Count];
        int metadataEnd = 6 + Records.Count * 12 + (Format == 1 ? 2 + LanguageTags.Count * 4 : 0);
        var referenced = new bool[OriginalBytes.Length - metadataEnd];
        for (int i = 0; i < records.Length; i++)
        {
            NameRecordModel record = Records[i];
            records[i] = new(record.PlatformId, record.EncodingId, record.LanguageId,
                record.NameId, record.Payload) { LegacyEncoding = record.LegacyEncoding };
            referenced.AsSpan(StorageOffset + record.StorageRelativeOffset - metadataEnd,
                record.Length).Fill(true);
        }
        for (int i = 0; i < tags.Length; i++)
        {
            NameLanguageTagModel tag = LanguageTags[i];
            tags[i] = tag.Text;
            referenced.AsSpan(StorageOffset + tag.StorageRelativeOffset - metadataEnd,
                tag.Length).Fill(true);
        }
        int opaqueCount = 0;
        foreach (bool value in referenced) if (!value) opaqueCount++;
        var opaque = new byte[opaqueCount];
        int at = 0;
        for (int i = 0; i < referenced.Length; i++)
            if (!referenced[i]) opaque[at++] = OriginalBytes.Span[metadataEnd + i];
        return new() { Format = Format, Records = records, LanguageTags = tags, UnreferencedBytes = opaque };
    }
}
