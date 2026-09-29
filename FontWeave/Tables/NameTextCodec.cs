using System.Text;
using FontWeave.Core;

namespace FontWeave.Tables;

/// <summary>
/// Strict codecs whose mapping is defined by the OpenType name chapter and the explicitly
/// selected Apple catalogue. Unknown encodings are not approximated by an OS code page.
/// Historical Unicode IDs use explicit character-identity profiles. Unicode 1.0 currently
/// supports only its 2350 Hangul mappings; other e0 positions remain raw, not "unassigned".
/// </summary>
public static class NameTextCodec
{
    private const uint Tag = 0x6E616D65;
    private static readonly Encoding Utf16 = new UnicodeEncoding(true, false, true);
    // Ask the provider directly: registration would mutate unrelated callers' global state.
    private static readonly Encoding? Prc = CodePagesEncodingProvider.Instance.GetEncoding(936,
        EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
    private static readonly Encoding? Big5 = CodePagesEncodingProvider.Instance.GetEncoding(950,
        EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
    private static readonly Encoding? Wansung = CodePagesEncodingProvider.Instance.GetEncoding(949,
        EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);

    /// <summary>Decode an explicitly chosen Apple mapping, preserving Unicode sequences/hints.
    /// Keyboard 09 is a defined one-way alias of 61; both decode to U+2423.
    /// RTL mappings preserve published strong-direction tags with maximal flat runs of
    /// U+202D LRO / U+202E RLO, terminated by U+202C PDF. Untagged units remain bare.</summary>
    public static ReadStatus TryDecode(NameLegacyEncoding encoding, ReadOnlySpan<byte> payload,
        out string? text, out FontDiagnostic diagnostic, DataLocation location = default)
    {
        text = null;
        location = location == default ? DataLocation.Anonymous(Tag) : location;
        NameLegacyCodec? codec = NameLegacyCodec.Get(encoding);
        if (codec is null) return UnsupportedLegacy(encoding, location, out diagnostic);
        if (payload.Length > int.MaxValue / codec.MaxSequenceLength)
            return ReadWire.Fail(ReadStatus.ResourceLimit, DiagnosticCode.BudgetExceeded,
                location with { Field = DataField.Length }, out diagnostic,
                (ulong)payload.Length * (uint)codec.MaxSequenceLength, int.MaxValue);
        return codec.TryDecode(payload, out text, out diagnostic, location);
    }

    /// <summary>Encode without best-fit or character normalization. Keyboard U+2423 uses 61.
    /// RTL mappings accept flat, nonnested, nonempty LRO/RLO ... PDF runs of matching tagged
    /// tokens, not general Unicode bidi resolution. Bare text prefers a published untagged
    /// mapping, otherwise accepts a unique tagged mapping; multiple tagged candidates require
    /// an override or an explicit byte choice. Decoding the
    /// result returns the canonical directional spelling, not necessarily the input controls.</summary>
    public static ReadStatus TryEncode(NameLegacyEncoding encoding, string text,
        out byte[]? payload, out FontDiagnostic diagnostic, BuildLimits? limits = null) =>
        TryEncode(encoding, text, [], out payload, out diagnostic, limits);

    /// <summary>Choose exact bytes at specified text-token boundaries, e.g. Keyboard 09 instead
    /// of canonical 61. Other text retains the normal mapping; invalid choices fail atomically.</summary>
    public static ReadStatus TryEncode(NameLegacyEncoding encoding, string text,
        ReadOnlySpan<NameLegacyByteChoice> choices, out byte[]? payload,
        out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        payload = null;
        NameLegacyCodec? codec = NameLegacyCodec.Get(encoding);
        if (codec is null) return UnsupportedLegacy(encoding, DataLocation.Anonymous(Tag), out diagnostic);
        return codec.TryEncode(text, choices, out payload, out diagnostic, new BuildBudget(limits));
    }

    public static ReadStatus TryDecode(ushort platformId, ushort encodingId, ReadOnlySpan<byte> payload,
        out string? text, out FontDiagnostic diagnostic, DataLocation location = default)
    {
        text = null;
        location = location == default ? DataLocation.Anonymous(Tag) : location;
        if (IsHistoricalUnicode(platformId, encodingId))
            return NameHistoricalUnicodeCodec.TryDecode(payload, encodingId == 0, out text, out diagnostic, location);
        Encoding? encoding = GetEncoding(platformId, encodingId);
        if (encoding is null) return Unsupported(encodingId, location, out diagnostic);
        try { text = encoding.GetString(payload); }
        catch (DecoderFallbackException) { return Invalid(platformId, encodingId, location, out diagnostic); }
        diagnostic = default;
        return ReadStatus.Done;
    }

    public static ReadStatus TryEncode(ushort platformId, ushort encodingId, string text,
        out byte[]? payload, out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        payload = null;
        DataLocation request = DataLocation.Anonymous(Tag);
        var budget = new BuildBudget(limits);
        Encoding? encoding = GetEncoding(platformId, encodingId);
        if (encoding is null) return Unsupported(encodingId, request, out diagnostic);
        if ((long)text.Length * 2 > int.MaxValue)
            return ReadWire.Fail(ReadStatus.ResourceLimit, DiagnosticCode.BudgetExceeded,
                request.At(DataField.InputBytes, 0), out diagnostic, (ulong)text.Length * 2, int.MaxValue);
        if (!budget.Input(text.Length * 2, request, out diagnostic) ||
            !budget.Work(text.Length, request, out diagnostic)) return ReadStatus.ResourceLimit;
        int length;
        try { length = encoding.GetByteCount(text); }
        catch (EncoderFallbackException) { return Invalid(platformId, encodingId, request, out diagnostic); }
        if (IsHistoricalUnicode(platformId, encodingId))
        {
            ReadStatus status = NameHistoricalUnicodeCodec.ValidateText(text, encodingId == 0, request, out diagnostic);
            if (status != ReadStatus.Done) return status;
        }
        if (length > ushort.MaxValue)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                request.At(DataField.Length, 0), out diagnostic, (ulong)length, ushort.MaxValue);
        if (!budget.Output(length, request, out diagnostic) ||
            !budget.Buffer(length, request, out diagnostic) ||
            !budget.Work(length, request, out diagnostic)) return ReadStatus.ResourceLimit;
        var result = new byte[length];
        try
        {
            if (IsHistoricalUnicode(platformId, encodingId))
                NameHistoricalUnicodeCodec.Write(text, result, encodingId == 0);
            else _ = encoding.GetBytes(text.AsSpan(), result);
        }
        catch (EncoderFallbackException) { return Invalid(platformId, encodingId, request, out diagnostic); }
        payload = result;
        diagnostic = default;
        return ReadStatus.Done;
    }

    internal static bool IsSupported(ushort platformId, ushort encodingId) =>
        GetEncoding(platformId, encodingId) is not null;

    internal static bool IsHistoricalUnicode(ushort platformId, ushort encodingId) =>
        platformId == 0 && encodingId <= 2;

    internal static ReadStatus ValidateSelection(ushort platformId, ushort encodingId,
        NameLegacyEncoding encoding, DataLocation at, out FontDiagnostic diagnostic)
    {
        NameLegacyCodec? codec = NameLegacyCodec.Get(encoding);
        if (codec is null) return UnsupportedLegacy(encoding, at, out diagnostic);
        if (!codec.IsCompatible(platformId, encodingId))
            return ReadWire.Fail(ReadStatus.InvalidEncoding, DiagnosticCode.InvalidValue,
                at with { Field = DataField.EncodingId }, out diagnostic, encodingId);
        diagnostic = default;
        return ReadStatus.Done;
    }

    internal static NameDecodeState DecodeState(ushort platformId, ushort encodingId,
        ReadOnlySpan<byte> payload, NameLegacyEncoding? legacyEncoding)
    {
        if (legacyEncoding is NameLegacyEncoding legacy)
            return NameLegacyCodec.Get(legacy)!.TryMeasure(payload, out _, out _)
                ? NameDecodeState.Decoded : NameDecodeState.UnmappedCharacter;
        if (IsHistoricalUnicode(platformId, encodingId))
            return NameHistoricalUnicodeCodec.CanDecode(payload, encodingId == 0)
                ? NameDecodeState.Decoded : NameDecodeState.UnmappedCharacter;
        return IsSupported(platformId, encodingId) ? NameDecodeState.Decoded : NameDecodeState.UnsupportedEncoding;
    }

    internal static long AdditionalDecodeBufferBytes(ushort platformId, ushort encodingId,
        int length, NameLegacyEncoding? legacyEncoding) =>
        legacyEncoding is NameLegacyEncoding legacy
            ? (long)length * NameLegacyCodec.Get(legacy)!.MaxSequenceLength * 4
            : IsHistoricalUnicode(platformId, encodingId) ? length * 2L : 0;

    internal static ReadStatus TryDecode(ushort platformId, ushort encodingId, ReadOnlySpan<byte> payload,
        NameLegacyEncoding? legacyEncoding, out string? text, out FontDiagnostic diagnostic,
        DataLocation location) => legacyEncoding is NameLegacyEncoding legacy
            ? TryDecode(legacy, payload, out text, out diagnostic, location)
            : TryDecode(platformId, encodingId, payload, out text, out diagnostic, location);

    // Name-ID constraints apply to the mapped characters, not codec-generated direction
    // metadata. Decode from bytes so literal controls in Unicode records are never stripped.
    internal static ReadStatus TryDecodeSemantic(ushort platformId, ushort encodingId,
        ReadOnlySpan<byte> payload, NameLegacyEncoding? legacyEncoding, out string? text,
        out FontDiagnostic diagnostic, DataLocation location) => legacyEncoding is NameLegacyEncoding legacy
            ? NameLegacyCodec.Get(legacy)!.TryDecode(payload, out text, out diagnostic, location,
                includeDirection: false)
            : TryDecode(platformId, encodingId, payload, out text, out diagnostic, location);

    internal static ReadStatus ValidatePayload(ushort platformId, ushort encodingId,
        ReadOnlySpan<byte> payload, DataLocation location, out FontDiagnostic diagnostic,
        NameLegacyEncoding? legacyEncoding = null)
    {
        if (legacyEncoding is NameLegacyEncoding legacy)
        {
            // Undefined units remain raw; malformed multibyte input is still rejected.
            return NameLegacyCodec.Get(legacy)!.Validate(payload, location, out diagnostic);
        }
        Encoding? encoding = GetEncoding(platformId, encodingId);
        // Unknown bytes are uninterpreted, not damaged text. DecodeState exposes that limit.
        if (encoding is not null)
        {
            try { _ = encoding.GetCharCount(payload); }
            catch (DecoderFallbackException) { return Invalid(platformId, encodingId, location, out diagnostic); }
        }
        diagnostic = default;
        return ReadStatus.Done;
    }

    private static Encoding? GetEncoding(ushort platformId, ushort encodingId) =>
        NameView.IsUtf16(platformId, encodingId) ? Utf16 :
        platformId == 3 ? encodingId switch { 3 => Prc, 4 => Big5, 5 => Wansung, _ => null } : null;

    private static ReadStatus Unsupported(ushort encodingId, DataLocation at, out FontDiagnostic diagnostic) =>
        ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
            at with { Field = DataField.EncodingId }, out diagnostic, encodingId);

    private static ReadStatus UnsupportedLegacy(NameLegacyEncoding encoding, DataLocation at,
        out FontDiagnostic diagnostic) =>
        ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
            at with { Field = DataField.EncodingId }, out diagnostic,
            (int)encoding < 0 ? 0 : (ulong)(int)encoding);

    private static ReadStatus Invalid(ushort platformId, ushort encodingId, DataLocation at,
        out FontDiagnostic diagnostic) => ReadWire.Fail(ReadStatus.InvalidEncoding,
            NameView.IsUtf16(platformId, encodingId) ? DiagnosticCode.InvalidUtf16 : DiagnosticCode.InvalidValue,
            at with { Field = DataField.Payload }, out diagnostic);
}
