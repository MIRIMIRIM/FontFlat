namespace FontWeave.Tables;

/// <summary>
/// Explicit Apple legacy mapping and variant, not a Macintosh script ID or an OS code page.
/// The selected mapping uses the published modern Unicode values and transcoding hints.
/// </summary>
public enum NameLegacyEncoding
{
    MacRoman,
    MacRomanCurrency,
    MacCeltic,
    MacCelticCurrency,
    MacCentralEuropean,
    MacCroatian,
    MacCroatianCurrency,
    MacCyrillic,
    MacCyrillicCurrency,
    MacUkrainianCurrency,
    MacDingbats,
    MacGaelic,
    MacGaelicCurrency,
    MacGaelicPerMille,
    MacGaelicCurrencyPerMille,
    MacGreek,
    MacGreekNoEuro,
    MacIcelandic,
    MacIcelandicCurrency,
    MacIcelandicTrueType,
    MacIcelandicTrueTypeCurrency,
    MacInuit,
    MacKeyboard,
    MacRomanian,
    MacRomanianCurrency,
    MacSymbol,
    MacSymbolNoEuro,
    MacThai,
    MacTurkish,
    MacJapanese,
    MacJapaneseBasic,
    MacChineseTraditional,
    MacChineseSimplified,
    MacKorean,
    MacDevanagari,
    MacGujarati,
    MacGurmukhi,
    MacArabic,
    MacArabicTrueType,
    MacArabicThuluth,
    MacArabicAlBayan,
    MacFarsi,
    MacFarsiTrueType,
    MacHebrew,
    MacHebrewTrueType
}

/// <summary>Assign a published mapping to one original name-record ordinal. Never inferred from locale.</summary>
public readonly record struct NameLegacyEncodingSelection(int Ordinal, NameLegacyEncoding Encoding);

/// <summary>
/// Select an original encoded byte at a UTF-16 text offset. The byte must map to the exact
/// requested text at an encoded-token boundary. Choices are strictly increasing by offset.
/// For RTL mappings the offset points to the first mapped character, not its LRO/RLO opener;
/// all controls earlier in the input count toward the UTF-16 offset. A choice inside an
/// explicit override run must match that run's published direction.
/// </summary>
public readonly record struct NameLegacyByteChoice(int TextOffset, byte Value);
