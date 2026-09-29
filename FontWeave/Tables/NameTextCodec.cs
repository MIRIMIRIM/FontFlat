using System.Text;
using FontWeave.Core;

namespace FontWeave.Tables;

/// <summary>
/// Strict codecs whose mapping is defined by the OpenType name chapter. Historical Mac
/// scripts and unknown encodings are unsupported, not approximated by an OS code page.
/// Unicode 1.x IDs are decoded as UTF-16 code units, without historical character remapping.
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

    public static ReadStatus TryDecode(ushort platformId, ushort encodingId, ReadOnlySpan<byte> payload,
        out string? text, out FontDiagnostic diagnostic, DataLocation location = default)
    {
        text = null;
        location = location == default ? DataLocation.Anonymous(Tag) : location;
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
        if (length > ushort.MaxValue)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                request.At(DataField.Length, 0), out diagnostic, (ulong)length, ushort.MaxValue);
        if (!budget.Output(length, request, out diagnostic) ||
            !budget.Buffer(length, request, out diagnostic) ||
            !budget.Work(length, request, out diagnostic)) return ReadStatus.ResourceLimit;
        var result = new byte[length];
        try { _ = encoding.GetBytes(text.AsSpan(), result); }
        catch (EncoderFallbackException) { return Invalid(platformId, encodingId, request, out diagnostic); }
        payload = result;
        diagnostic = default;
        return ReadStatus.Done;
    }

    internal static bool IsSupported(ushort platformId, ushort encodingId) =>
        GetEncoding(platformId, encodingId) is not null;

    internal static ReadStatus ValidatePayload(ushort platformId, ushort encodingId,
        ReadOnlySpan<byte> payload, DataLocation location, out FontDiagnostic diagnostic)
    {
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

    private static ReadStatus Invalid(ushort platformId, ushort encodingId, DataLocation at,
        out FontDiagnostic diagnostic) => ReadWire.Fail(ReadStatus.InvalidEncoding,
            NameView.IsUtf16(platformId, encodingId) ? DiagnosticCode.InvalidUtf16 : DiagnosticCode.InvalidValue,
            at with { Field = DataField.Payload }, out diagnostic);
}
