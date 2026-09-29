using FontWeave.Core;

namespace FontWeave.Tables;

public enum NameLanguageKind { Unspecified, LanguageTag, PlatformSpecific }

/// <summary>
/// A language identity without an ambient OS locale or an inferred registry mapping.
/// Numeric identifiers are meaningful only within their platform.
/// </summary>
public readonly record struct NameLanguageIdentity(NameLanguageKind Kind, ushort PlatformId,
    ushort LanguageId, string? Tag)
{
    public static NameLanguageIdentity Unspecified => default;
    public static NameLanguageIdentity ForPlatform(ushort platformId, ushort languageId) =>
        new(NameLanguageKind.PlatformSpecific, platformId, languageId, null);
    public static NameLanguageIdentity ForTag(string tag)
    {
        ArgumentNullException.ThrowIfNull(tag);
        if (!NameLanguageTagSyntax.IsWellFormed(tag))
            throw new ArgumentException("The language tag must be well-formed RFC 5646.", nameof(tag));
        return new(NameLanguageKind.LanguageTag, 0, 0, tag);
    }
}

/// <summary>Deterministic lookup with explicit language preferences, never CurrentCulture.</summary>
public static class NameResolver
{
    /// <summary>
    /// Try languages in caller order. Tag preferences use RFC 4647 lookup truncation;
    /// numeric preferences match exactly. An empty preference list permits any language.
    /// Within a language choose Unicode, Windows, Macintosh, then other platforms; ties
    /// use encoding, language ID and original ordinal. Unsupported text is not a name.
    /// Typographic IDs 16/17 fall back to 1/2 only after the requested ID has no match.
    /// </summary>
    public static ReadStatus TryResolve(NameTableModel table, ushort nameId,
        IReadOnlyList<NameLanguageIdentity> languages, out NameRecordModel? record,
        out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(languages);
        record = null;
        var budget = new BuildBudget(limits);
        bool unsupported = false;
        for (int pass = 0; pass < (nameId is 16 or 17 ? 2 : 1); pass++)
        {
            ushort requested = pass == 0 ? nameId : (ushort)(nameId == 16 ? 1 : 2);
            for (int preference = 0; preference < Math.Max(1, languages.Count); preference++)
            {
                NameLanguageIdentity language = languages.Count == 0 ? default : languages[preference];
                if (!Enum.IsDefined(language.Kind)) throw new ArgumentOutOfRangeException(nameof(languages));
                if (language.Kind == NameLanguageKind.LanguageTag &&
                    (language.Tag is null || !NameLanguageTagSyntax.IsWellFormed(language.Tag)))
                    throw new ArgumentException("Language preferences require well-formed tags.", nameof(languages));
                string? range = language.Tag;
                do
                {
                    if (!budget.Work(table.Records.Count, table.Location, out diagnostic))
                        return ReadStatus.ResourceLimit;
                    foreach (NameRecordModel candidate in table.Records)
                    {
                        if (candidate.NameId != requested || languages.Count != 0 &&
                            !Matches(table.GetLanguage(candidate.Ordinal), language, range)) continue;
                        if (candidate.DecodeState != NameDecodeState.Decoded)
                        { unsupported = true; continue; }
                        if (record is null || Compare(candidate, record) < 0) record = candidate;
                    }
                    if (record is not null) { diagnostic = default; return ReadStatus.Done; }
                    if (language.Kind != NameLanguageKind.LanguageTag) break;
                    range = TruncateRange(range!);
                } while (range is not null);
            }
        }
        return ReadWire.Fail(unsupported ? ReadStatus.UnsupportedFormat : ReadStatus.NotFound,
            unsupported ? DiagnosticCode.UnsupportedOperation : DiagnosticCode.InvalidValue,
            table.Location.At(DataField.Payload, 0), out diagnostic, nameId);
    }

    private static bool Matches(NameLanguageIdentity actual, NameLanguageIdentity preference, string? range)
    {
        if (actual.Kind != preference.Kind) return false;
        return actual.Kind switch
        {
            NameLanguageKind.Unspecified => true,
            NameLanguageKind.PlatformSpecific => actual.PlatformId == preference.PlatformId &&
                actual.LanguageId == preference.LanguageId,
            // RFC 4647 lookup matches the complete requested range; more specific available
            // tags may also match, with a subtag boundary (en must not match enochian).
            NameLanguageKind.LanguageTag => actual.Tag!.Equals(range, StringComparison.OrdinalIgnoreCase) ||
                actual.Tag.Length > range!.Length && actual.Tag[range.Length] == '-' &&
                actual.Tag.StartsWith(range, StringComparison.OrdinalIgnoreCase),
            _ => false
        };
    }

    private static string? TruncateRange(string range)
    {
        int end = range.LastIndexOf('-');
        if (end <= 1) return null;
        // A singleton left at the end must be removed with its separator.
        if (end >= 2 && range[end - 2] == '-') end -= 2;
        return end <= 0 ? null : range[..end];
    }

    private static int Compare(NameRecordModel a, NameRecordModel b)
    {
        int result = Rank(a.PlatformId).CompareTo(Rank(b.PlatformId));
        if (result != 0) return result;
        result = a.PlatformId.CompareTo(b.PlatformId);
        if (result != 0) return result;
        result = a.EncodingId.CompareTo(b.EncodingId);
        if (result != 0) return result;
        result = a.LanguageId.CompareTo(b.LanguageId);
        return result != 0 ? result : a.Ordinal.CompareTo(b.Ordinal);
    }

    private static int Rank(ushort platform) => platform switch { 0 => 0, 3 => 1, 1 => 2, _ => 3 };
}
