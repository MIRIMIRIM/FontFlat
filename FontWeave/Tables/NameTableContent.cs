namespace FontWeave.Tables;

/// <summary>A complete record. Unknown encodings remain explicit bytes, never guessed text.</summary>
public readonly record struct NameRecordValue(ushort PlatformId, ushort EncodingId,
    ushort LanguageId, ushort NameId, ReadOnlyMemory<byte> Payload)
{
    /// <summary>Caller-selected Apple mapping; null never guesses a legacy variant.</summary>
    public NameLegacyEncoding? LegacyEncoding { get; init; }
}

/// <summary>
/// The complete desired table, not an ordinal patch list. Records are stably key-sorted by
/// the compiler; language tags retain caller order, and their references are caller-assigned.
/// Remove, insert or replace any item to edit it. Format conversion is always explicit.
/// </summary>
public sealed record NameTableContent
{
    /// <summary>Unicode semantics used for validation; strict modern UTF-16BE by default.</summary>
    public NameUnicodeInterpretation UnicodeInterpretation { get; init; } = NameUnicodeInterpretation.Utf16;
    public ushort Format { get; init; }
    public IReadOnlyList<NameRecordValue> Records { get; init; } = Array.Empty<NameRecordValue>();
    public IReadOnlyList<string> LanguageTags { get; init; } = Array.Empty<string>();
    /// <summary>Opaque source gaps, holes and tail bytes, retained in source order after storage.</summary>
    public ReadOnlyMemory<byte> UnreferencedBytes { get; init; }
}
