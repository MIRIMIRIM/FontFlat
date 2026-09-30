namespace FontWeave.Tables;

/// <summary>Character interpretation for Unicode-platform name records.</summary>
public enum NameUnicodeInterpretation
{
    /// <summary>Strict UTF-16BE using modern Unicode character identities.</summary>
    Utf16 = 0,
    /// <summary>For platform 0 encoding IDs 0–2 only, restore historical character identities
    /// using the bounded implemented repertoire. Unmapped positions remain raw.</summary>
    Historical = 1
}
