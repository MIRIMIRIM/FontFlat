using FontWeave.Core;

namespace FontWeave.Tables;

/// <summary>The finite decoder's result. Unsupported bytes remain available verbatim.</summary>
public enum NameDecodeState { Decoded, UnsupportedEncoding }

/// <summary>A validated name record, retaining its original ordinal and wire identity.</summary>
public sealed class NameRecordModel
{
    internal NameRecordModel(int ordinal, ushort platformId, ushort encodingId,
        ushort languageId, ushort nameId, ushort length, ushort storageRelativeOffset,
        byte[] payload, NameDecodeState decodeState, string? text,
        DataLocation rowLocation, DataLocation payloadLocation)
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
    public string? Text { get; }
    public DataLocation RowLocation { get; }
    public DataLocation PayloadLocation { get; }
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

/// <summary>Strictly validated, source-independent format 0/1 name table.</summary>
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
}
