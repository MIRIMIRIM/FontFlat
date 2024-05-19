using CommunityToolkit.Diagnostics;
using FontFlat.OpenType.FontTables;
using System.Diagnostics;
using System.Text;

namespace FontFlat.OpenType;

public partial class OTFont
{
    private byte[] NameRecordGetBytes(NameRecord rec)
    {
        Guard.IsNotNull(Name);
        int offset = Records.First(x => x.tableTag.AsSpan().SequenceEqual("name"u8)).offset;
        offset += ((Table_name)Name).storageOffset;
        offset += rec.stringOffset;
        if (reader.BaseStream.Position != offset) { reader.BaseStream.Seek(offset, SeekOrigin.Begin); }
        return reader.ReadBytes(rec.length);
    }
    public string NameRecordDecodeString(NameRecord rec)
    {
        var buf = NameRecordGetBytes(rec);

        var name = rec.platformID switch
        {
            0 => NameRecordDecodeUnicode(buf),
            1 => NameRecordDecodeMacintosh(buf, rec.encodingID, rec.languageID), // codes from OTFontFile
            3 => NameRecordDecodeWindows(buf, rec.encodingID), // codes from OTFontFile
            _ => Convert.ToBase64String(buf),
        };
        Debug.WriteLine($"language: {rec.languageID}; encoding: {rec.encodingID}; platform: {rec.platformID}; {name}");

        // Some old japanese fonts maybe have weird character in namestring
        if (name.Contains('\0'))
        {
            name = name.Replace("\0", "");
        }

        return name;
    }


    private static string NameRecordDecodeUnicode(byte[] buf)
    {
        var utf16be = Encoding.BigEndianUnicode;
        return utf16be.GetString(buf);
    }
    private static string NameRecordDecodeWindows(byte[] buf, ushort encId)
    {
        var utf16be = Encoding.BigEndianUnicode;
        if (encId == 0 || // symbol - strings identified as symbol encoded strings 
                          // aren't symbol encoded, they're unicode encoded!!!
                    encId == 1 || // unicode
                    encId == 10) // unicode with surrogate support for UCS-4
        {
            return utf16be.GetString(buf);
        }
        else if (encId >= 2 && encId <= 6)
        {
            int nCodePage = WinGetCodepageByEncId(encId);
            return GetUnicodeStrFromCodePageBuf(buf, nCodePage);
        }
        else
        {
            //Debug.Assert(false, "unsupported text encoding");
            return Convert.ToBase64String(buf);
        }


    }
    private static string NameRecordDecodeMacintosh(byte[] buf, ushort encId, ushort langId)
    {
        // Some old fonts maybe use encoding = 0 encode cjk characters. Maybe can use UTF-Unknown. such as FZLongZhaoS-R-GB
        int nMacCodePage;
        if (encId == 0)
        {
            // old japanese fonts
            nMacCodePage = MacGetCodepageByLangId(langId);
        }
        else
        {
            nMacCodePage = MacGetCodepageByEncId(encId);
        }

        return nMacCodePage != -1 ? GetUnicodeStrFromCodePageBuf(buf, nMacCodePage) : Convert.ToBase64String(buf);
    }
    private static string GetUnicodeStrFromCodePageBuf(byte[] buf, int codepage)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Encoding enc = Encoding.GetEncoding(codepage);
        return enc.GetString(buf);
    }
    private static int WinGetCodepageByEncId(ushort MSEncID)
    {
        int nCodePage = -1;

        switch (MSEncID)
        {
            case 2: // ShiftJIS
                nCodePage = 932;
                break;
            case 3: // PRC
                nCodePage = 936;
                break;
            case 4: // Big5
                nCodePage = 950;
                break;
            case 5: // Wansung
                nCodePage = 949;
                break;
            case 6: // Johab
                nCodePage = 1361;
                break;
        }

        return nCodePage;
    }
    private static Encoding WinGetEncodingByLangId(ushort langID)
    {
        if (!LanguageIDWindows.TryGetValue(langID, out var lang)) { lang = "Unknown"; }
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return lang[..2] switch
        {
            "ja" => Encoding.GetEncoding("SHIFT-JIS"),
            "zh" => lang.Contains("Hant") ? Encoding.GetEncoding("BIG5") : Encoding.GetEncoding("GB18030"),
            _ => Encoding.UTF8
        };
    }
    private static int MacGetCodepageByLangId(ushort MacLanguageID)
    {
        return MacLanguageID switch
        {
            (ushort)LanguageIDMacintosh.ja => 10001,
            (ushort)LanguageIDMacintosh.zh_Hans => 10008,
            (ushort)LanguageIDMacintosh.zh_Hant => 10002,
            _ => 10000,
        };
    }
    private static int MacGetCodepageByEncId(ushort MacEncodingID)
    {
        /*
            Q187858 INFO: Macintosh Code Pages Supported Under Windows NT

            10000 (MAC - Roman)
            10001 (MAC - Japanese)
            10002 (MAC - Traditional Chinese Big5)
            10003 (MAC - Korean)
            10004 (MAC - Arabic)
            10005 (MAC - Hebrew)
            10006 (MAC - Greek I)
            10007 (MAC - Cyrillic)
            10008 (MAC - Simplified Chinese GB 2312)
            10010 (MAC - Romania)
            10017 (MAC - Ukraine)
            10029 (MAC - Latin II)
            10079 (MAC - Icelandic)
            10081 (MAC - Turkish)
            10082 (MAC - Croatia) 
        */
        // NOTE: code pages 10010 through 10082
        // don't seem to map to Encoding IDs in the OT spec

        return MacEncodingID switch
        {
            // Roman
            0 => 10000,
            // Japanese
            1 => 10001,
            // Chinese (Traditional)
            2 => 10002,
            // Korean
            3 => 10003,
            // Arabic
            4 => 10004,
            // Hebrew
            5 => 10005,
            // Greek
            6 => 10006,
            // Russian
            7 => 10007,
            // Chinese (Simplified)
            25 => 10008,
            // unsupported text encoding
            _ => -1,
        };
    }

    public enum PlatformID : ushort
    {
        Unicode = 0,
        Macintosh = 1,  // Discouraged
        ISO = 2,        // Deprecated
        Windows = 3,
        Custom = 4,     // should not be used in new fonts
    }
    // Must include: Family (or Preferred Family), Style (or Preferred Style), Full, PostScript
    public enum NameID : ushort
    {
        copyright = 0,
        familyName = 1,
        subfamilyName = 2,
        uniqueSubfamilyIdentifier = 3,
        fullName = 4,
        versionString = 5,
        postScriptName = 6,
        trademark = 7,
        manufacturerName = 8,
        designer = 9,
        description = 10,
        vendorUri = 11,
        designerUri = 12,
        licenseDescription = 13,
        licenseInfoUri = 14,
        typographicFamilyName = 16, // Preferred Family
        typographicSubfamilyName = 17,  // Preferred Subfamily
        compatibleFullName = 18,    // MacOS Only
        sampleText = 19,
        postScriptCIDFindfontName = 20,
        wwsFamilyName = 21,
        wwsSubfamilyName = 22,
        lightBackgroundPalette = 23,
        darkBackgroundPalette = 24,
        variationsPostScriptNamePrefix = 25,
    }
    public enum EncodingIDMacintosh : ushort
    {
        Roman = 0,
        Japanese = 1,
        Traditional_Chinese = 2,
        Korean = 3,
        Arabic = 4,
        Hebrew = 5,
        Greek = 6,
        Russian = 7,
        RSymbol = 8,
        Devanagari = 9,
        Gurmukhi = 10,
        Gujarati = 11,
        Oriya = 12,
        Bengali = 13,
        Tamil = 14,
        Telugu = 15,
        Kannada = 16,
        Malayalam = 17,
        Sinhalese = 18,
        Burmese = 19,
        Khmer = 20,
        Thai = 21,
        Laotian = 22,
        Georgian = 23,
        Armenian = 24,
        Simplified_Chinese = 25,
        Tibetan = 26,
        Mongolian = 27,
        Geez = 28,
        Slavic = 29,
        Vietnamese = 30,
        Sindhi = 31,
        Uninterpreted = 32
    };
    public enum EncodingIDWindows
    {
        Symbol = 0,
        Unicode_BMP = 1,
        ShiftJIS = 2,
        PRC = 3,
        Big5 = 4,
        Wansung = 5,
        Johab = 6,
        Reserved = 7,   // 7-9 is Reserved
        Unicode_full_repertoire = 10
    }
    public static readonly Dictionary<uint, string> LanguageIDWindows = new()
        {
            // https://referencesource.microsoft.com/#mscorlib/system/globalization/regioninfo.cs,171
            // https://www.iana.org/assignments/language-subtag-registry/language-subtag-registry
            // https://fonttools.readthedocs.io/en/latest/_modules/fontTools/ttLib/tables/_n_a_m_e.html

            { 0x0403, "ca-ES"},         // Catalan (Catalan)
            { 0x0404, "zh-Hant-TW"},    // Chinese (Taiwan)
            { 0x0405, "cs-CZ"},         // Czech (Czech Republic)
            { 0x0406, "da-DK"},         // Danish (Denmark)
            { 0x0407, "de-DE"},         // German (Germany)
            { 0x0408, "el-GR"},         // Greek (Greece)
            { 0x0409, "en-US"},         // English (United States)
            { 0x040A, "es-ES"},         // Spanish (Traditional Sort) (Spain)   es-u-co-trad?
            { 0x040B, "fi-FI"},         // Finnish (Finland)
            { 0x040C, "fr-FR"},         // French (France)
            { 0x040E, "hu-HU"},         // Hungarian (Hungary)
            { 0x0410, "it-IT"},         // Italian (Italy)
            { 0x0411, "ja-JP"},         // Japanese (Japan)
            { 0x0413, "nl-NL"},         // Dutch (Netherlands)
            { 0x0414, "nb-NO"},         // Norwegian (Bokm?) (Norway)
            { 0x0415, "pl-PL"},         // Polish (Poland)
            { 0x0416, "pt-BR"},         // Portuguese (Brazil)
            { 0x0419, "ru-RU"},         // Russian (Russia)
            { 0x041B, "sk-SK"},         // Slovak (Slovakia)
            { 0x041D, "sv-SE"},         // Swedish (Sweden)
            { 0x0424, "sl-SI"},         // Slovenian (Slovenia)
            { 0x042D, "eu-ES"},         // Basque (Basque)
            { 0x0804, "zh-Hans-CN"},    // Chinese (People's Republic of China)
            { 0x080A, "es-MX"},         // Spanish (Mexico)
            { 0x0816, "pt-PT"},         // Portuguese (Portugal)
            { 0x0C04, "zh-Hant-HK"},    // Chinese (Hong Kong S.A.R.)
            { 0x0C0C, "fr-CA"},         // French (Canada)
            { 0x1404, "zh-Hant-MO"},    // Chinese (Macau S.A.R.)
        };
    public enum LanguageIDMacintosh : ushort
    {
        en = 0,
        ja = 11,
        zh_Hans = 19,
        zh_Hant = 33
    }
}
