using System.Buffers.Binary;
using System.Text;
using FontWeave.Core;

namespace FontWeave.Tables;

/// <summary>Bounded historical character identities, not full historical Unicode behavior.
/// Unicode 1.0 admits ASCII, Latin1 graphics, 127 European Latin characters,
/// 113 Extended Latin characters, and its published 2350 Hangul mappings;
/// Unicode 1.1 / ISO 1993 also admits the verified identity bitmap.
/// Other positions remain raw, not necessarily unassigned.</summary>
internal static class NameHistoricalUnicodeCodec
{
    private const int OldFirst = 0x3400, OldCount = 6656;
    private const int Unicode10HangulCount = 2350;
    private const int ModernFirst = 0xAC00, ModernCount = 11172;
    private const int BitmapAt = 8, HangulAt = BitmapAt + 8192;
    private static readonly byte[] Data = Load();
    private static readonly ushort[] ReverseHangul = BuildReverse();

    // Callers validate the complete UTF-16 wire first, so an earlier unmapped position
    // never hides later malformed input. A valid surrogate pair is outside this BMP repertoire.
    internal static bool CanDecode(ReadOnlySpan<byte> payload, bool unicode10) =>
        FindUnmapped(payload, unicode10, out _) < 0;

    internal static ReadStatus TryDecode(ReadOnlySpan<byte> payload, bool unicode10, out string? text,
        out FontDiagnostic diagnostic, DataLocation location)
    {
        text = null;
        if (payload.IsEmpty)
        {
            text = string.Empty;
            diagnostic = default;
            return ReadStatus.Done;
        }
        if (!NameView.IsValidUtf16BigEndian(payload))
            return ReadWire.Fail(ReadStatus.InvalidEncoding, DiagnosticCode.InvalidUtf16,
                location with { Field = DataField.Payload }, out diagnostic);
        int offset = FindUnmapped(payload, unicode10, out ushort code);
        if (offset >= 0)
            return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
                location with { Field = DataField.Payload, Offset = location.Offset + (uint)offset },
                out diagnostic, code);
        var builder = new StringBuilder(payload.Length / 2);
        for (int at = 0; at < payload.Length; at += 2)
        {
            _ = Forward(ReadWire.U16(payload, at), unicode10, out char value);
            builder.Append(value);
        }
        text = builder.ToString();
        diagnostic = default;
        return ReadStatus.Done;
    }

    // Input UTF-16 syntax is checked by the public codec or the legacy edit compiler.
    internal static ReadStatus ValidateText(string text, bool unicode10, DataLocation location,
        out FontDiagnostic diagnostic)
    {
        for (int at = 0; at < text.Length; at++)
            if (!Reverse(text[at], unicode10, out _))
                return ReadWire.Fail(ReadStatus.InvalidEncoding, DiagnosticCode.InvalidValue,
                    location with { Field = DataField.Payload, Offset = location.Offset + (uint)at },
                    out diagnostic, text[at]);
        diagnostic = default;
        return ReadStatus.Done;
    }

    internal static void Write(string text, Span<byte> destination, bool unicode10)
    {
        for (int i = 0; i < text.Length; i++)
        {
            _ = Reverse(text[i], unicode10, out ushort code);
            BinaryPrimitives.WriteUInt16BigEndian(destination.Slice(i * 2, 2), code);
        }
    }

    private static int FindUnmapped(ReadOnlySpan<byte> payload, bool unicode10, out ushort code)
    {
        for (int at = 0; at < payload.Length; at += 2)
        {
            code = ReadWire.U16(payload, at);
            if (!Forward(code, unicode10, out _)) return at;
        }
        code = 0;
        return -1;
    }

    private static bool Forward(ushort code, bool unicode10, out char mapped)
    {
        int count = unicode10 ? Unicode10HangulCount : OldCount;
        if (code >= OldFirst && code < OldFirst + count)
        {
            mapped = (char)ReadWire.U16(Data, HangulAt + (code - OldFirst) * 2);
            return true;
        }
        mapped = (char)code;
        return unicode10 ? IsUnicode10Identity(code) : IsIdentity(code);
    }

    private static bool Reverse(char value, bool unicode10, out ushort code)
    {
        if (value is >= (char)ModernFirst and < (char)(ModernFirst + ModernCount))
        {
            code = ReverseHangul[value - ModernFirst];
            return code != 0 && (!unicode10 || code < OldFirst + Unicode10HangulCount);
        }
        code = value;
        return unicode10 ? IsUnicode10Identity(value) : IsIdentity(value);
    }

    // Unicode 1.0 ch.3.1 preserves ASCII identities; ch.3.7 pp.127-128 document
    // Latin1 graphics, European Latin through U+017E, and Extended Latin U+0180..U+01F0.
    // U+017F and U+01F1 remain raw, as does C1 whose meanings are left to external standards.
    private static bool IsUnicode10Identity(int code) =>
        code <= 0x7F || code is >= 0xA0 and <= 0x17E or >= 0x180 and <= 0x1F0;

    private static bool IsIdentity(int code) =>
        (Data[BitmapAt + (code >> 3)] & (1 << (code & 7))) != 0;

    private static byte[] Load()
    {
        // The generator independently verifies every Unicode 1.0 row against the shared
        // lower Hangul segment; its membership is never inferred just from a range label.
        using Stream stream = typeof(NameHistoricalUnicodeCodec).Assembly.GetManifestResourceStream(
            "FontWeave.NameUnicode11Mappings") ??
            throw new InvalidOperationException("Missing Unicode 1.1 name mappings.");
        if (stream.Length != HangulAt + OldCount * 2)
            throw new InvalidOperationException("Invalid Unicode 1.1 name mapping length.");
        var data = new byte[(int)stream.Length];
        stream.ReadExactly(data);
        if (ReadWire.U32(data, 0) != 0x4E553131 || ReadWire.U32(data, 4) != 1)
            throw new InvalidOperationException("Invalid Unicode 1.1 name mapping header.");
        return data;
    }

    private static ushort[] BuildReverse()
    {
        var result = new ushort[ModernCount];
        for (int i = 0; i < OldCount; i++)
        {
            int modern = ReadWire.U16(Data, HangulAt + i * 2);
            if (modern < ModernFirst || modern >= ModernFirst + ModernCount ||
                result[modern - ModernFirst] != 0 || IsIdentity(modern) || IsIdentity(OldFirst + i))
                throw new InvalidOperationException("Ambiguous Unicode 1.1 name mappings.");
            result[modern - ModernFirst] = (ushort)(OldFirst + i);
        }
        return result;
    }
}
