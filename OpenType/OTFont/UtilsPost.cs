using CommunityToolkit.Diagnostics;
using FontFlat.OpenType.FontTables;
using System.Text;

namespace FontFlat.OpenType;

public partial class OTFont
{
    public Dictionary<uint, string> PostVer20ParseStringData()
    {
        Dictionary<uint, string> postNameMap = [];
        TableRecord record;
        if (Post is null) { ParseTablePost(out record); }
        else { record = GetTableRecord("post"u8); }

        var postTable = (Table_post)Post!;
        if (postTable.version != Const.ver20) { ThrowHelper.ThrowArgumentException($"Table post version {postTable.version} not have string data"); }

        var tableLength = record.length;
        var numGlyphs = (ushort)postTable.numGlyphs!;
        var stringOffset = record.offset + 34 + numGlyphs * 2;
        reader.BaseStream.Seek(stringOffset, SeekOrigin.Begin);

        ushort pendingNameIndex = 0;
        var pendingNames = new Dictionary<ushort, string>();

        while (pendingNameIndex <= 0xFFFF && stringOffset < tableLength)
        {
            // the first byte is record string length (the length byte is not included)
            var strLength = reader.ReadByte();
            string str;
            stringOffset += 1;
            if (strLength > 0)
            {
                // glyph name strings are encoded in ASCII
                str = Encoding.ASCII.GetString(reader.ReadBytes(strLength).AsSpan());
                pendingNames[pendingNameIndex] = str;
            }
            else
            {
                pendingNames[pendingNameIndex] = string.Empty;
            }
            pendingNameIndex += 1;
            stringOffset += strLength;
        }

        foreach (var idx in postTable.glyphNameIndex!)
        {
            string glyphName;
            if (idx >= 258)
            {
                glyphName = pendingNames[(ushort)(idx - 258)];
            }
            else
            {
                glyphName = standardMacintoshGlyphs[idx];
            }
            postNameMap.Add(idx, glyphName);
        }

        return postNameMap;
    }

    // https://developer.apple.com/fonts/TrueType-Reference-Manual/RM06/Chap6html
    internal static readonly string[] standardMacintoshGlyphs =
    {
        ".notdef",
        ".null", "nonmarkingreturn", "space", "exclam", "quotedbl", "numbersign", "dollar",
        "percent", "ampersand", "quotesingle", "parenleft", "parenright",
        "asterisk", "plus", "comma", "hyphen", "period", "slash",
        "zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine",
        "colon", "semicolon", "less", "equal", "greater", "question", "at",
        "A", "B", "C", "D", "E", "F", "G", "H", "I", "J", "K", "L", "M", "N", "O", "P", "Q", "R", "S", "T", "U", "V", "W", "X", "Y", "Z",
        "bracketleft", "backslash", "bracketright", "asciicircum", "underscore", "grave",
        "a", "b", "c", "d", "e", "f", "g", "h", "i", "j", "k", "l", "m", "n", "o", "p", "q", "r", "s", "t", "u", "v", "w", "x", "y", "z",
        "braceleft", "bar", "braceright", "asciitilde",
        "Adieresis", "Aring", "Ccedilla", "Eacute", "Ntilde", "Odieresis", "Udieresis", "aacute",
        "agrave", "acircumflex", "adieresis", "atilde", "aring", "ccedilla", "eacute", "egrave",
        "ecircumflex", "edieresis", "iacute", "igrave", "icircumflex", "idieresis", "ntilde", "oacute",
        "ograve", "ocircumflex", "odieresis", "otilde", "uacute", "ugrave", "ucircumflex", "udieresis",
        "dagger", "degree", "cent", "sterling", "section", "bullet", "paragraph", "germandbls",
        "registered", "copyright", "trademark", "acute", "dieresis", "notequal", "AE", "Oslash",
        "infinity", "plusminus", "lessequal", "greaterequal", "yen", "mu", "partialdiff", "summation",
        "product", "pi", "integral", "ordfeminine", "ordmasculine", "Omega", "ae", "oslash",
        "questiondown", "exclamdown", "logicalnot", "radical", "florin", "approxequal", "Delta",
        "guillemotleft", "guillemotright", "ellipsis", "nonbreakingspace", "Agrave", "Atilde",
        "Otilde", "OE", "oe", "endash", "emdash", "quotedblleft", "quotedblright", "quoteleft",
        "quoteright", "divide", "lozenge", "ydieresis", "Ydieresis", "fraction", "currency",
        "guilsinglleft", "guilsinglright", "fi", "fl", "daggerdbl", "periodcentered", "quotesinglbase",
        "quotedblbase", "perthousand", "Acircumflex", "Ecircumflex", "Aacute", "Edieresis", "Egrave",
        "Iacute", "Icircumflex", "Idieresis", "Igrave", "Oacute", "Ocircumflex", "apple", "Ograve",
        "Uacute", "Ucircumflex", "Ugrave", "dotlessi", "circumflex", "tilde", "macron", "breve",
        "dotaccent", "ring", "cedilla", "hungarumlaut", "ogonek", "caron", "Lslash", "lslash",
        "Scaron", "scaron", "Zcaron", "zcaron", "brokenbar", "Eth", "eth", "Yacute", "yacute", "Thorn",
        "thorn", "minus", "multiply", "onesuperior", "twosuperior", "threesuperior", "onehalf",
        "onequarter", "threequarters", "franc", "Gbreve", "gbreve", "Idotaccent", "Scedilla",
        "scedilla", "Cacute", "cacute", "Ccaron", "ccaron", "dcroat"
    };
}
