using System.Buffers.Binary;
using System.Text;

namespace FontWeave.Tables;

/// <summary>Fixed generated package data, not a parser for caller-supplied font bytes.</summary>
internal static class NameCjkData
{
    internal static readonly string?[][] Tables = Load();

    private static string?[][] Load()
    {
        using Stream stream = typeof(NameCjkData).Assembly.GetManifestResourceStream("FontWeave.NameCjkMappings")
            ?? throw new InvalidOperationException("Embedded Apple CJK mappings are missing.");
        Span<byte> header = stackalloc byte[4];
        stream.ReadExactly(header);
        if (!header.SequenceEqual("NMC1"u8)) throw new InvalidDataException("Unknown Apple mapping resource.");
        var result = new string?[4][];
        var utf16 = new UnicodeEncoding(true, false, true);
        Span<byte> encoded = stackalloc byte[510];
        for (int table = 0; table < result.Length; table++)
        {
            stream.ReadExactly(header);
            uint count = BinaryPrimitives.ReadUInt32BigEndian(header);
            if (count > 65536) throw new InvalidDataException("Invalid Apple mapping count.");
            var values = new string?[65536];
            for (uint row = 0; row < count; row++)
            {
                stream.ReadExactly(header[..3]);
                ushort code = BinaryPrimitives.ReadUInt16BigEndian(header);
                int length = header[2] * 2;
                if (length == 0 || values[code] is not null)
                    throw new InvalidDataException("Invalid Apple mapping entry.");
                stream.ReadExactly(encoded[..length]);
                values[code] = utf16.GetString(encoded[..length]);
            }
            result[table] = values;
        }
        if (stream.ReadByte() != -1) throw new InvalidDataException("Trailing Apple mapping data.");
        return result;
    }

    internal static string?[] JapaneseBasic()
    {
        var result = (string?[])Tables[0].Clone();
        // JAPANESE.TXT §2b/§3a: exclude precisely the260 Apple symbols and53 ku+84 forms.
        Array.Clear(result, 0x8540, 0x886D - 0x8540 + 1);
        Array.Clear(result, 0xEB41, 0xED96 - 0xEB41 + 1);
        // Missing glyphs at EAA3/EAA4 are not a Unicode remapping. PUA is not a §2b addition.
        return result;
    }
}
