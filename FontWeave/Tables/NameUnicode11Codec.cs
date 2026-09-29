using System.Buffers.Binary;
using System.Text;
using FontWeave.Core;

namespace FontWeave.Tables;

/// <summary>Unicode 1.1 / ISO 10646-1:1993 character identities, not historical rendering.
/// This historical-text profile leaves private-use, noncharacters and positions outside
/// the published repertoire raw; it does not impose that policy on other Unicode codecs.</summary>
internal static class NameUnicode11Codec
{
    private const int OldFirst = 0x3400, OldCount = 6656;
    private const int ModernFirst = 0xAC00, ModernCount = 11172;
    private const int BitmapAt = 8, HangulAt = BitmapAt + 8192;
    private static readonly byte[] Data = Load();
    private static readonly ushort[] ReverseHangul = BuildReverse();

    // Callers validate the complete UTF-16 wire first, so an earlier unmapped position
    // never hides later malformed input. A valid surrogate pair is outside this BMP repertoire.
    internal static bool CanDecode(ReadOnlySpan<byte> payload) =>
        FindUnmapped(payload, out _) < 0;

    internal static ReadStatus TryDecode(ReadOnlySpan<byte> payload, out string? text,
        out FontDiagnostic diagnostic, DataLocation location)
    {
        text = null;
        if (!NameView.IsValidUtf16BigEndian(payload))
            return ReadWire.Fail(ReadStatus.InvalidEncoding, DiagnosticCode.InvalidUtf16,
                location with { Field = DataField.Payload }, out diagnostic);
        int offset = FindUnmapped(payload, out ushort code);
        if (offset >= 0)
            return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
                location with { Field = DataField.Payload, Offset = location.Offset + (uint)offset },
                out diagnostic, code);
        var builder = new StringBuilder(payload.Length / 2);
        for (int at = 0; at < payload.Length; at += 2)
        {
            _ = Forward(ReadWire.U16(payload, at), out char value);
            builder.Append(value);
        }
        text = builder.ToString();
        diagnostic = default;
        return ReadStatus.Done;
    }

    // Input UTF-16 syntax is checked by the public codec or the legacy edit compiler.
    internal static ReadStatus ValidateText(string text, DataLocation location,
        out FontDiagnostic diagnostic)
    {
        for (int at = 0; at < text.Length; at++)
            if (!Reverse(text[at], out _))
                return ReadWire.Fail(ReadStatus.InvalidEncoding, DiagnosticCode.InvalidValue,
                    location with { Field = DataField.Payload, Offset = location.Offset + (uint)at },
                    out diagnostic, text[at]);
        diagnostic = default;
        return ReadStatus.Done;
    }

    internal static void Write(string text, Span<byte> destination)
    {
        for (int i = 0; i < text.Length; i++)
        {
            _ = Reverse(text[i], out ushort code);
            BinaryPrimitives.WriteUInt16BigEndian(destination.Slice(i * 2, 2), code);
        }
    }

    private static int FindUnmapped(ReadOnlySpan<byte> payload, out ushort code)
    {
        for (int at = 0; at < payload.Length; at += 2)
        {
            code = ReadWire.U16(payload, at);
            if (!Forward(code, out _)) return at;
        }
        code = 0;
        return -1;
    }

    private static bool Forward(ushort code, out char mapped)
    {
        if (code is >= OldFirst and < OldFirst + OldCount)
        {
            mapped = (char)ReadWire.U16(Data, HangulAt + (code - OldFirst) * 2);
            return true;
        }
        mapped = (char)code;
        return IsIdentity(code);
    }

    private static bool Reverse(char value, out ushort code)
    {
        if (value is >= (char)ModernFirst and < (char)(ModernFirst + ModernCount))
        {
            code = ReverseHangul[value - ModernFirst];
            return code != 0;
        }
        code = value;
        return IsIdentity(value);
    }

    private static bool IsIdentity(int code) =>
        (Data[BitmapAt + (code >> 3)] & (1 << (code & 7))) != 0;

    private static byte[] Load()
    {
        using Stream stream = typeof(NameUnicode11Codec).Assembly.GetManifestResourceStream(
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
