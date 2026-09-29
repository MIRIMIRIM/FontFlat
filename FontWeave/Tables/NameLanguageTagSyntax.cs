namespace FontWeave.Tables;

/// <summary>RFC 5646 well-formedness, not IANA registration, preferred values or likely subtags.</summary>
public static class NameLanguageTagSyntax
{
    public static bool IsWellFormed(string tag)
    {
        ArgumentNullException.ThrowIfNull(tag);
        string[] parts = tag.Split('-');
        foreach (string part in parts)
            if (part.Length is < 1 or > 8 || !Alphanumeric(part)) return false;
        if (IsGrandfathered(tag)) return true;
        int i = 0;
        if (parts[0].Equals("x", StringComparison.OrdinalIgnoreCase))
            return parts.Length > 1;
        if (parts[0].Length is < 2 or > 8 || !Letters(parts[0])) return false;
        i++;
        if (parts[0].Length <= 3)
            for (int n = 0; n < 3 && i < parts.Length && parts[i].Length == 3 && Letters(parts[i]); n++) i++;
        if (i < parts.Length && parts[i].Length == 4 && Letters(parts[i])) i++;
        if (i < parts.Length && (parts[i].Length == 2 && Letters(parts[i]) ||
            parts[i].Length == 3 && Digits(parts[i]))) i++;
        var variants = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (i < parts.Length && (parts[i].Length >= 5 ||
            parts[i].Length == 4 && char.IsAsciiDigit(parts[i][0])))
            if (!variants.Add(parts[i++])) return false;
        ulong singletons = 0;
        while (i < parts.Length && parts[i].Length == 1 &&
            !parts[i].Equals("x", StringComparison.OrdinalIgnoreCase))
        {
            char c = char.ToLowerInvariant(parts[i++][0]);
            int index = c <= '9' ? c - '0' : c - 'a' + 10;
            ulong bit = 1UL << index;
            if ((singletons & bit) != 0) return false;
            singletons |= bit;
            int start = i;
            while (i < parts.Length && parts[i].Length >= 2) i++;
            if (start == i) return false;
        }
        if (i < parts.Length && parts[i].Equals("x", StringComparison.OrdinalIgnoreCase))
            return i + 1 < parts.Length;
        return i == parts.Length;
    }

    private static bool IsGrandfathered(string tag) => tag.ToLowerInvariant() is
        "en-gb-oed" or "i-ami" or "i-bnn" or "i-default" or "i-enochian" or
        "i-hak" or "i-klingon" or "i-lux" or "i-mingo" or "i-navajo" or
        "i-pwn" or "i-tao" or "i-tay" or "i-tsu" or "sgn-be-fr" or
        "sgn-be-nl" or "sgn-ch-de" or "art-lojban" or "cel-gaulish" or
        "no-bok" or "no-nyn" or "zh-guoyu" or "zh-hakka" or "zh-min" or
        "zh-min-nan" or "zh-xiang";

    private static bool Letters(string s)
    {
        foreach (char c in s) if (!char.IsAsciiLetter(c)) return false;
        return true;
    }

    private static bool Digits(string s)
    {
        foreach (char c in s) if (!char.IsAsciiDigit(c)) return false;
        return true;
    }

    private static bool Alphanumeric(string s)
    {
        foreach (char c in s) if (!char.IsAsciiLetterOrDigit(c)) return false;
        return true;
    }
}
