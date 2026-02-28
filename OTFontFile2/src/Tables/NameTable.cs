using System.Text;
using OTFontFile2.SourceGen;

namespace OTFontFile2.Tables;

[OtTable("name", 6)]
[OtField("Format", OtFieldKind.UInt16, 0)]
[OtField("Count", OtFieldKind.UInt16, 2)]
[OtField("StringOffset", OtFieldKind.UInt16, 4)]
[OtSequentialRecordArray("Record", 6, 12, CountPropertyName = "Count", RecordTypeName = "NameRecord")]
public readonly partial struct NameTable
{
    public readonly struct NameRecord
    {
        public ushort PlatformId { get; }
        public ushort EncodingId { get; }
        public ushort LanguageId { get; }
        public ushort NameId { get; }
        public ushort Length { get; }
        public ushort Offset { get; }

        public NameRecord(ushort platformId, ushort encodingId, ushort languageId, ushort nameId, ushort length, ushort offset)
        {
            PlatformId = platformId;
            EncodingId = encodingId;
            LanguageId = languageId;
            NameId = nameId;
            Length = length;
            Offset = offset;
        }
    }

    public enum PlatformId : ushort
    {
        Unicode = 0,
        Macintosh = 1,
        ISO = 2,
        Windows = 3,
        Custom = 4
    }

    public enum NameId : ushort
    {
        Copyright = 0,
        FamilyName = 1,
        SubfamilyName = 2,
        UniqueSubfamilyIdentifier = 3,
        FullName = 4,
        VersionString = 5,
        PostScriptName = 6,
        Trademark = 7,
        ManufacturerName = 8,
        Designer = 9,
        Description = 10,
        VendorUri = 11,
        DesignerUri = 12,
        LicenseDescription = 13,
        LicenseInfoUri = 14,
        TypographicFamilyName = 16,
        TypographicSubfamilyName = 17,
        CompatibleFullName = 18,
        SampleText = 19
    }

    public bool TryGetEncodedStringBytes(NameRecord record, out ReadOnlySpan<byte> bytes)
    {
        bytes = default;

        int storageOffset = StringOffset + record.Offset;
        int length = record.Length;

        if ((uint)storageOffset > (uint)_table.Length)
            return false;
        if ((uint)length > (uint)(_table.Length - storageOffset))
            return false;

        bytes = _table.Span.Slice(storageOffset, length);
        return true;
    }

    public string? DecodeString(NameRecord record)
    {
        if (!TryGetEncodedStringBytes(record, out var bytes))
            return null;

        return DecodeString(record.PlatformId, record.EncodingId, record.LanguageId, bytes);
    }

    public string? GetGeneralStringByNameId(NameId nameId, bool validateSurrogates)
        => GetGeneralStringByNameId((ushort)nameId, validateSurrogates);

    public string? GetGeneralStringByNameId(ushort nameId, bool validateSurrogates)
    {
        // Priority: Windows English, Windows any, Mac Roman English.
        string? s = null;

        s ??= GetString((ushort)PlatformId.Windows, encId: 0xFFFF, langId: 0x0409, nameId); // en-US
        s ??= GetString((ushort)PlatformId.Windows, encId: 0xFFFF, langId: 0xFFFF, nameId);
        s ??= GetString((ushort)PlatformId.Macintosh, encId: 0, langId: 0, nameId);

        if (s is not null && validateSurrogates)
        {
            var span = s.AsSpan();
            for (int i = 0; i < span.Length - 1; i++)
            {
                if ((char.IsHighSurrogate(span[i]) && !char.IsLowSurrogate(span[i + 1]))
                    || (!char.IsHighSurrogate(span[i]) && char.IsLowSurrogate(span[i + 1])))
                {
                    return null;
                }
            }
        }

        return s;
    }

    public string? GetFullNameString() => GetGeneralStringByNameId(NameId.FullName, validateSurrogates: true);
    public string? GetVersionString() => GetGeneralStringByNameId(NameId.VersionString, validateSurrogates: true);
    public string? GetPostScriptNameString() => GetGeneralStringByNameId(NameId.PostScriptName, validateSurrogates: true);

    public string? GetString(ushort platformId, ushort encId, ushort langId, ushort nameId)
    {
        int count = Count;
        for (int i = 0; i < count; i++)
        {
            if (!TryGetRecord(i, out var r))
                continue;

            if ((platformId == 0xFFFF || r.PlatformId == platformId) &&
                (encId == 0xFFFF || r.EncodingId == encId) &&
                (langId == 0xFFFF || r.LanguageId == langId) &&
                r.NameId == nameId)
            {
                var decoded = DecodeString(r);
                if (decoded is null)
                    continue;

                // Some old fonts have embedded NULs.
                if (decoded.IndexOf('\0') >= 0)
                    decoded = decoded.Replace("\0", "");

                return decoded;
            }
        }

        return null;
    }

    public static string? DecodeString(ushort platformId, ushort encId, ushort langId, ReadOnlySpan<byte> encoded)
    {
        if (platformId == (ushort)PlatformId.Unicode)
        {
            var ue = new UnicodeEncoding(bigEndian: true, byteOrderMark: false);
            return ue.GetString(encoded);
        }

        if (platformId == (ushort)PlatformId.Windows)
        {
            if (encId == 0 || encId == 1 || encId == 10)
            {
                var ue = new UnicodeEncoding(bigEndian: true, byteOrderMark: false);
                return ue.GetString(encoded);
            }

            int codePage = encId switch
            {
                2 => 932,  // ShiftJIS
                3 => 936,  // PRC
                4 => 950,  // Big5
                5 => 949,  // Wansung
                6 => 1361, // Johab
                _ => -1
            };

            if (codePage != -1)
            {
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                return Encoding.GetEncoding(codePage).GetString(encoded);
            }

            return null;
        }

        if (platformId == (ushort)PlatformId.Macintosh)
        {
            int codePage = MacEncodingToCodePage(encId, langId);
            if (codePage == -1)
                return null;

            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(codePage).GetString(encoded);
        }

        return null;
    }

    private static int MacEncodingToCodePage(ushort encId, ushort langId)
    {
        // If encId == 0, some legacy fonts use language to pick code page.
        if (encId == 0)
        {
            return langId switch
            {
                11 => 10001, // Japanese
                19 => 10008, // Simplified Chinese
                33 => 10002, // Traditional Chinese
                _ => 10000   // Roman
            };
        }

        return encId switch
        {
            0 => 10000, // Roman
            1 => 10001, // Japanese
            2 => 10002, // Traditional Chinese
            3 => 10003, // Korean
            25 => 10008, // Simplified Chinese
            _ => 10000
        };
    }
}
