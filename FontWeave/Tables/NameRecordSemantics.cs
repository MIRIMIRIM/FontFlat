namespace FontWeave.Tables;

/// <summary>Local mandatory name-string constraints; not naming/style or cross-table recommendations.</summary>
internal static class NameRecordSemantics
{
    internal static bool HasTextConstraint(ushort nameId) => nameId is 5 or 6 or 20 or 25;

    internal static bool IsValid(ushort nameId, string text)
    {
        if (nameId == 5) return HasVersionNumber(text);
        if (nameId == 6 && text.Length > 63) return false;
        if (nameId is 6 or 20)
        {
            foreach (char c in text)
                if (c is < (char)33 or > (char)126 or '[' or ']' or '(' or ')' or
                    '{' or '}' or '<' or '>' or '/' or '%') return false;
        }
        if (nameId == 25)
            foreach (char c in text) if (!char.IsAsciiLetterOrDigit(c)) return false;
        return true;
    }

    private static bool HasVersionNumber(string text)
    {
        // "Version " is recommended, not mandatory. The required number may be embedded.
        int at = 0;
        while (at < text.Length)
        {
            if (!char.IsAsciiDigit(text[at])) { at++; continue; }
            int major = Number(text, ref at);
            while (at + 1 < text.Length && text[at] == '.' && char.IsAsciiDigit(text[at + 1]))
            {
                at++;
                int minor = Number(text, ref at);
                if (major < ushort.MaxValue && minor < ushort.MaxValue) return true;
                // An invalid preceding pair must not hide a later, overlapping valid one:
                // in "70000.1.2", 1.2 is a valid version number.
                major = minor;
            }
        }
        return false;
    }

    private static int Number(string text, ref int at)
    {
        int value = 0;
        while (at < text.Length && char.IsAsciiDigit(text[at]))
        {
            value = Math.Min(ushort.MaxValue, value * 10 + text[at] - '0');
            at++;
        }
        return value;
    }
}
