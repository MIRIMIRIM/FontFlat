using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;

namespace OTFontFile
{
    public class StandardPostNames
    {
        private static string[] macNames = new string[258]
        {
            ".notdef",
            "null",
            "CR",
            "space",
            "exclam",
            "quotedbl",
            "numbersign",
            "dollar",
            "percent",
            "ampersand",
            "quotesingle",
            "parenleft",
            "parenright",
            "asterisk",
            "plus",
            "comma",
            "hyphen",
            "period",
            "slash",
            "zero",
            "one",
            "two",
            "three",
            "four",
            "five",
            "six",
            "seven",
            "eight",
            "nine",
            "colon",
            "semicolon",
            "less",
            "equal",
            "greater",
            "question",
            "at",
            "A",
            "B",
            "C",
            "D",
            "E",
            "F",
            "G",
            "H",
            "I",
            "J",
            "K",
            "L",
            "M",
            "N",
            "O",
            "P",
            "Q",
            "R",
            "S",
            "T",
            "U",
            "V",
            "W",
            "X",
            "Y",
            "Z",
            "bracketleft",
            "backslash",
            "bracketright",
            "asciicircum",
            "underscore",
            "grave",
            "a",
            "b",
            "c",
            "d",
            "e",
            "f",
            "g",
            "h",
            "i",
            "j",
            "k",
            "l",
            "m",
            "n",
            "o",
            "p",
            "q",
            "r",
            "s",
            "t",
            "u",
            "v",
            "w",
            "x",
            "y",
            "z",
            "braceleft",
            "bar",
            "braceright",
            "asciitilde",
            "Adieresis",
            "Aring",
            "Ccedilla",
            "Eacute",
            "Ntilde",
            "Odieresis",
            "Udieresis",
            "aacute",
            "agrave",
            "acircumflex",
            "adieresis",
            "atilde",
            "aring",
            "ccedilla",
            "eacute",
            "egrave",
            "ecircumflex",
            "edieresis",
            "iacute",
            "igrave",
            "icircumflex",
            "idieresis",
            "ntilde",
            "oacute",
            "ograve",
            "ocircumflex",
            "odieresis",
            "otilde",
            "uacute",
            "ugrave",
            "ucircumflex",
            "udieresis",
            "dagger",
            "degree",
            "cent",
            "sterling",
            "section",
            "bullet",
            "paragraph",
            "germandbls",
            "registered",
            "copyright",
            "trademark",
            "acute",
            "dieresis",
            "notequal",
            "AE",
            "Oslash",
            "infinity",
            "plusminus",
            "lessequal",
            "greaterequal",
            "yen",
            "mu",
            "partialdiff",
            "summation",
            "product",
            "pi",
            "integral",
            "ordfeminine",
            "ordmasculine",
            "Omega",
            "ae",
            "oslash",
            "questiondown",
            "exclamdown",
            "logicalnot",
            "radical",
            "florin",
            "approxequal",
            "increment",
            "guillemotleft",
            "guillemotright",
            "elipsis",
            "nbspace",
            "Agrave",
            "Atilde",
            "Otilde",
            "OE",
            "oe",
            "endash",
            "emdash",
            "quotedblleft",
            "quotedblright",
            "quoteleft",
            "quoteright",
            "divide",
            "lozenge",
            "ydieresis",
            "Ydieresis",
            "fraction",
            "currency",
            "guilsinglleft",
            "guilsinglright",
            "fi",
            "fl",
            "vdaggerdbl",
            "middot",
            "quotesinglbase",
            "quotedblbase",
            "perthousand",
            "Acircumflex",
            "Ecircumflex",
            "Aacute",
            "Edieresis",
            "Egrave",
            "Iacute",
            "Icircumflex",
            "Idieresis",
            "Igrave",
            "Oacute",
            "Ocircumflex",
            "apple",
            "Ograve",
            "Uacute",
            "Ucircumflex",
            "Ugrave",
            "dotlessi",
            "circumflex",
            "tilde",
            "overscore",
            "breve",
            "dotaccent",
            "ring",
            "cidilla",
            "hungarumlaut",
            "ogonek",
            "caron",
            "Lslash",
            "lslash",
            "Scaron",
            "scaron",
            "Zcaron",
            "zcaron",
            "brokenbar",
            "Eth",
            "eth",
            "Yacute",
            "yacute",
            "Thorn",
            "thorn",
            "minus",
            "multiply",
            "onesuperior",
            "twosuperior",
            "threesuperior",
            "onehalf",
            "onequarter",
            "threequarters",
            "franc",
            "Gbreve",
            "gbreve",
            "Idot",
            "Scidilla",
            "scedilla",
            "Cacute",
            "cacute",
            "Ccaron",
            "ccaron",
            "dcroat"
        };
        
        private static Hashtable postNames;

        static StandardPostNames()
        {
            postNames = new Hashtable
            {
                { 0x0000, ".notdef" },
                { 0x000D, "CR" },
                { 0x0020, "space" },
                { 0x0021, "exclam" },
                { 0x0022, "quotedbl" },
                { 0x0023, "numbersign" },
                { 0x0024, "dollar" },
                { 0x0025, "percent" },
                { 0x0026, "ampersand" },
                { 0x0027, "quotesingle" },
                { 0x0028, "parenleft" },
                { 0x0029, "parenright" },
                { 0x002a, "asterisk" },
                { 0x002b, "plus" },
                { 0x002c, "comma" },
                { 0x002d, "hyphen" },
                { 0x002e, "period" },
                { 0x002f, "slash" },
                { 0x0030, "zero" },
                { 0x0031, "one" },
                { 0x0032, "two" },
                { 0x0033, "three" },
                { 0x0034, "four" },
                { 0x0035, "five" },
                { 0x0036, "six" },
                { 0x0037, "seven" },
                { 0x0038, "eight" },
                { 0x0039, "nine" },
                { 0x003a, "colon" },
                { 0x003b, "semicolon" },
                { 0x003c, "less" },
                { 0x003d, "equal" },
                { 0x003e, "greater" },
                { 0x003f, "question" },
                { 0x0040, "at" },
                { 0x0041, "A" },
                { 0x0042, "B" },
                { 0x0043, "C" },
                { 0x0044, "D" },
                { 0x0045, "E" },
                { 0x0046, "F" },
                { 0x0047, "G" },
                { 0x0048, "H" },
                { 0x0049, "I" },
                { 0x004a, "J" },
                { 0x004b, "K" },
                { 0x004c, "L" },
                { 0x004d, "M" },
                { 0x004e, "N" },
                { 0x004f, "O" },
                { 0x0050, "P" },
                { 0x0051, "Q" },
                { 0x0052, "R" },
                { 0x0053, "S" },
                { 0x0054, "T" },
                { 0x0055, "U" },
                { 0x0056, "V" },
                { 0x0057, "W" },
                { 0x0058, "X" },
                { 0x0059, "Y" },
                { 0x005a, "Z" },
                { 0x005b, "bracketleft" },
                { 0x005c, "backslash" },
                { 0x005d, "bracketright" },
                { 0x005e, "asciicircum" },
                { 0x005f, "underscore" },
                { 0x0060, "grave" },
                { 0x0061, "a" },
                { 0x0062, "b" },
                { 0x0063, "c" },
                { 0x0064, "d" },
                { 0x0065, "e" },
                { 0x0066, "f" },
                { 0x0067, "g" },
                { 0x0068, "h" },
                { 0x0069, "i" },
                { 0x006a, "j" },
                { 0x006b, "k" },
                { 0x006c, "l" },
                { 0x006d, "m" },
                { 0x006e, "n" },
                { 0x006f, "o" },
                { 0x0070, "p" },
                { 0x0071, "q" },
                { 0x0072, "r" },
                { 0x0073, "s" },
                { 0x0074, "t" },
                { 0x0075, "u" },
                { 0x0076, "v" },
                { 0x0077, "w" },
                { 0x0078, "x" },
                { 0x0079, "y" },
                { 0x007a, "z" },
                { 0x007b, "braceleft" },
                { 0x007c, "bar" },
                { 0x007d, "braceright" },
                { 0x007e, "asciitilde" },
                { 0x00a0, "nbspace" },
                { 0x00a1, "exclamdown" },
                { 0x00a2, "cent" },
                { 0x00a3, "sterling" },
                { 0x00a4, "currency" },
                { 0x00a5, "yen" },
                { 0x00a6, "brokenbar" },
                { 0x00a7, "section" },
                { 0x00a8, "dieresis" },
                { 0x00a9, "copyright" },
                { 0x00aa, "ordfeminine" },
                { 0x00ab, "guillemotleft" },
                { 0x00ac, "logicalnot" },
                { 0x00ad, "sfthyphen" },
                { 0x00ae, "registered" },
                { 0x00af, "macron" },
                { 0x00b0, "degree" },
                { 0x00b1, "plusminus" },
                { 0x00b2, "twosuperior" },
                { 0x00b3, "threesuperior" },
                { 0x00b4, "acute" },
                { 0x00b5, "mu" },
                { 0x00b6, "paragraph" },
                { 0x00b7, "periodcentered" },
                { 0x00b8, "cedilla" },
                { 0x00b9, "onesuperior" },
                { 0x00ba, "ordmasculine" },
                { 0x00bb, "guillemotright" },
                { 0x00bc, "onequarter" },
                { 0x00bd, "onehalf" },
                { 0x00be, "threequarters" },
                { 0x00bf, "questiondown" },
                { 0x00c0, "Agrave" },
                { 0x00c1, "Aacute" },
                { 0x00c2, "Acircumflex" },
                { 0x00c3, "Atilde" },
                { 0x00c4, "Adieresis" },
                { 0x00c5, "Aring" },
                { 0x00c6, "AE" },
                { 0x00c7, "Ccedilla" },
                { 0x00c8, "Egrave" },
                { 0x00c9, "Eacute" },
                { 0x00ca, "Ecircumflex" },
                { 0x00cb, "Edieresis" },
                { 0x00cc, "Igrave" },
                { 0x00cd, "Iacute" },
                { 0x00ce, "Icircumflex" },
                { 0x00cf, "Idieresis" },
                { 0x00d0, "Eth" },
                { 0x00d1, "Ntilde" },
                { 0x00d2, "Ograve" },
                { 0x00d3, "Oacute" },
                { 0x00d4, "Ocircumflex" },
                { 0x00d5, "Otilde" },
                { 0x00d6, "Odieresis" },
                { 0x00d7, "multiply" },
                { 0x00d8, "Oslash" },
                { 0x00d9, "Ugrave" },
                { 0x00da, "Uacute" },
                { 0x00db, "Ucircumflex" },
                { 0x00dc, "Udieresis" },
                { 0x00dd, "Yacute" },
                { 0x00de, "Thorn" },
                { 0x00df, "germandbls" },
                { 0x00e0, "agrave" },
                { 0x00e1, "aacute" },
                { 0x00e2, "acircumflex" },
                { 0x00e3, "atilde" },
                { 0x00e4, "adieresis" },
                { 0x00e5, "aring" },
                { 0x00e6, "ae" },
                { 0x00e7, "ccedilla" },
                { 0x00e8, "egrave" },
                { 0x00e9, "eacute" },
                { 0x00ea, "ecircumflex" },
                { 0x00eb, "edieresis" },
                { 0x00ec, "igrave" },
                { 0x00ed, "iacute" },
                { 0x00ee, "icircumflex" },
                { 0x00ef, "idieresis" },
                { 0x00f0, "eth" },
                { 0x00f1, "ntilde" },
                { 0x00f2, "ograve" },
                { 0x00f3, "oacute" },
                { 0x00f4, "ocircumflex" },
                { 0x00f5, "otilde" },
                { 0x00f6, "odieresis" },
                { 0x00f7, "divide" },
                { 0x00f8, "oslash" },
                { 0x00f9, "ugrave" },
                { 0x00fa, "uacute" },
                { 0x00fb, "ucircumflex" },
                { 0x00fc, "udieresis" },
                { 0x00fd, "yacute" },
                { 0x00fe, "thorn" },
                { 0x00ff, "ydieresis" },
                { 0x0100, "Amacron" },
                { 0x0101, "amacron" },
                { 0x0102, "Abreve" },
                { 0x0103, "abreve" },
                { 0x0104, "Aogonek" },
                { 0x0105, "aogonek" },
                { 0x0106, "Cacute" },
                { 0x0107, "cacute" },
                { 0x0108, "Ccircumflex" },
                { 0x0109, "ccircumflex" },
                { 0x010a, "Cdotaccent" },
                { 0x010b, "cdotaccent" },
                { 0x010c, "Ccaron" },
                { 0x010d, "ccaron" },
                { 0x010e, "Dcaron" },
                { 0x010f, "dcaron" },
                { 0x0110, "Dcroat" },
                { 0x0111, "dcroat" },
                { 0x0112, "Emacron" },
                { 0x0113, "emacron" },
                { 0x0114, "Ebreve" },
                { 0x0115, "ebreve" },
                { 0x0116, "Edotaccent" },
                { 0x0117, "edotaccent" },
                { 0x0118, "Eogonek" },
                { 0x0119, "eogonek" },
                { 0x011a, "Ecaron" },
                { 0x011b, "ecaron" },
                { 0x011c, "Gcircumflex" },
                { 0x011d, "gcircumflex" },
                { 0x011e, "Gbreve" },
                { 0x011f, "gbreve" },
                { 0x0120, "Gdotaccent" },
                { 0x0121, "gdotaccent" },
                { 0x0122, "Gcommaaccent" },
                { 0x0123, "gcommaaccent" },
                { 0x0124, "Hcircumflex" },
                { 0x0125, "hcircumflex" },
                { 0x0126, "Hbar" },
                { 0x0127, "hbar" },
                { 0x0128, "Itilde" },
                { 0x0129, "itilde" },
                { 0x012a, "Imacron" },
                { 0x012b, "imacron" },
                { 0x012c, "Ibreve" },
                { 0x012d, "ibreve" },
                { 0x012e, "Iogonek" },
                { 0x012f, "iogonek" },
                { 0x0130, "Idotaccent" },
                { 0x0131, "dotlessi" },
                { 0x0132, "IJ" },
                { 0x0133, "ij" },
                { 0x0134, "Jcircumflex" },
                { 0x0135, "jcircumflex" },
                { 0x0136, "Kcommaaccent" },
                { 0x0137, "kcommaaccent" },
                { 0x0138, "kgreenlandic" },
                { 0x0139, "Lacute" },
                { 0x013a, "lacute" },
                { 0x013b, "Lcommaaccent" },
                { 0x013c, "lcommaaccent" },
                { 0x013d, "Lcaron" },
                { 0x013e, "lcaron" },
                { 0x013f, "Ldot" },
                { 0x0140, "ldot" },
                { 0x0141, "Lslash" },
                { 0x0142, "lslash" },
                { 0x0143, "Nacute" },
                { 0x0144, "nacute" },
                { 0x0145, "Ncommaaccent" },
                { 0x0146, "ncommaaccent" },
                { 0x0147, "Ncaron" },
                { 0x0148, "ncaron" },
                { 0x0149, "napostrophe" },
                { 0x014a, "Eng" },
                { 0x014b, "eng" },
                { 0x014c, "Omacron" },
                { 0x014d, "omacron" },
                { 0x014e, "Obreve" },
                { 0x014f, "obreve" },
                { 0x0150, "Ohungarumlaut" },
                { 0x0151, "ohungarumlaut" },
                { 0x0152, "OE" },
                { 0x0153, "oe" },
                { 0x0154, "Racute" },
                { 0x0155, "racute" },
                { 0x0156, "Rcommaaccent" },
                { 0x0157, "rcommaaccent" },
                { 0x0158, "Rcaron" },
                { 0x0159, "rcaron" },
                { 0x015a, "Sacute" },
                { 0x015b, "sacute" },
                { 0x015c, "Scircumflex" },
                { 0x015d, "scircumflex" },
                { 0x015e, "Scedilla" },
                { 0x015f, "scedilla" },
                { 0x0160, "Scaron" },
                { 0x0161, "scaron" },
                { 0x0162, "Tcommaaccent" },
                { 0x0163, "tcommaaccent" },
                { 0x0164, "Tcaron" },
                { 0x0165, "tcaron" },
                { 0x0166, "Tbar" },
                { 0x0167, "tbar" },
                { 0x0168, "Utilde" },
                { 0x0169, "utilde" },
                { 0x016a, "Umacron" },
                { 0x016b, "umacron" },
                { 0x016c, "Ubreve" },
                { 0x016d, "ubreve" },
                { 0x016e, "Uring" },
                { 0x016f, "uring" },
                { 0x0170, "Uhungarumlaut" },
                { 0x0171, "uhungarumlaut" },
                { 0x0172, "Uogonek" },
                { 0x0173, "uogonek" },
                { 0x0174, "Wcircumflex" },
                { 0x0175, "wcircumflex" },
                { 0x0176, "Ycircumflex" },
                { 0x0177, "ycircumflex" },
                { 0x0178, "Ydieresis" },
                { 0x0179, "Zacute" },
                { 0x017a, "zacute" },
                { 0x017b, "Zdotaccent" },
                { 0x017c, "zdotaccent" },
                { 0x017d, "Zcaron" },
                { 0x017e, "zcaron" },
                { 0x017F, "longs" },
                { 0x0192, "florin" },
                { 0x01fa, "Aringacute" },
                { 0x01fb, "aringacute" },
                { 0x01fc, "AEacute" },
                { 0x01fd, "aeacute" },
                { 0x01fe, "Oslashacute" },
                { 0x01ff, "oslashacute" },
                { 0x02c6, "circumflex" },
                { 0x02c7, "caron" },
                { 0x02c9, "overscore" },
                { 0x02d8, "breve" },
                { 0x02d9, "dotaccent" },
                { 0x02da, "ring" },
                { 0x02db, "ogonek" },
                { 0x02dc, "tilde" },
                { 0x02dd, "hungarumlaut" },
                { 0x0384, "tonos" },
                { 0x0385, "dieresistonos" },
                { 0x0386, "Alphatonos" },
                { 0x0387, "anoteleia" },
                { 0x0388, "Epsilontonos" },
                { 0x0389, "Etatonos" },
                { 0x038a, "Iotatonos" },
                { 0x038c, "Omicrontonos" },
                { 0x038e, "Upsilontonos" },
                { 0x038f, "Omegatonos" },
                { 0x0390, "iotadieresistonos" },
                { 0x0391, "Alpha" },
                { 0x0392, "Beta" },
                { 0x0393, "Gamma" },
                { 0x0394, "Deltagreek" },
                { 0x0395, "Epsilon" },
                { 0x0396, "Zeta" },
                { 0x0397, "Eta" },
                { 0x0398, "Theta" },
                { 0x0399, "Iota" },
                { 0x039a, "Kappa" },
                { 0x039b, "Lambda" },
                { 0x039c, "Mu" },
                { 0x039d, "Nu" },
                { 0x039e, "Xi" },
                { 0x039f, "Omicron" },
                { 0x03a0, "Pi" },
                { 0x03a1, "Rho" },
                { 0x03a3, "Sigma" },
                { 0x03a4, "Tau" },
                { 0x03a5, "Upsilon" },
                { 0x03a6, "Phi" },
                { 0x03a7, "Chi" },
                { 0x03a8, "Psi" },
                { 0x03a9, "Omegagreek" },
                { 0x03aa, "Iotadieresis" },
                { 0x03ab, "Upsilondieresis" },
                { 0x03ac, "alphatonos" },
                { 0x03ad, "epsilontonos" },
                { 0x03ae, "etatonos" },
                { 0x03af, "iotatonos" },
                { 0x03b0, "upsilondieresistonos" },
                { 0x03b1, "alpha" },
                { 0x03b2, "beta" },
                { 0x03b3, "gamma" },
                { 0x03b4, "delta" },
                { 0x03b5, "epsilon" },
                { 0x03b6, "zeta" },
                { 0x03b7, "eta" },
                { 0x03b8, "theta" },
                { 0x03b9, "iota" },
                { 0x03ba, "kappa" },
                { 0x03bb, "lambda" },
                { 0x03bc, "mugreek" },
                { 0x03bd, "nu" },
                { 0x03be, "xi" },
                { 0x03bf, "omicron" },
                { 0x03c0, "pi" },
                { 0x03c1, "rho" },
                { 0x03c2, "sigma1" },
                { 0x03c3, "sigma" },
                { 0x03c4, "tau" },
                { 0x03c5, "upsilon" },
                { 0x03c6, "phi" },
                { 0x03c7, "chi" },
                { 0x03c8, "psi" },
                { 0x03c9, "omega" },
                { 0x03ca, "iotadieresis" },
                { 0x03cb, "upsilondieresis" },
                { 0x03cc, "omicrontonos" },
                { 0x03cd, "upsilontonos" },
                { 0x03ce, "omegatonos" },
                { 0x0401, "Iocyrillic" },
                { 0x0402, "Djecyrillic" },
                { 0x0403, "Gjecyrillic" },
                { 0x0404, "Ecyrillic" },
                { 0x0405, "Dzecyrillic" },
                { 0x0406, "Icyrillic" },
                { 0x0407, "Yicyrillic" },
                { 0x0408, "Jecyrillic" },
                { 0x0409, "Ljecyrillic" },
                { 0x040a, "Njecyrillic" },
                { 0x040b, "Tshecyrillic" },
                { 0x040c, "Kjecyrillic" },
                { 0x040e, "Ushortcyrillic" },
                { 0x040f, "Dzhecyrillic" },
                { 0x0410, "Acyrillic" },
                { 0x0411, "Becyrillic" },
                { 0x0412, "Vecyrillic" },
                { 0x0413, "Gecyrillic" },
                { 0x0414, "Decyrillic" },
                { 0x0415, "Iecyrillic" },
                { 0x0416, "Zhecyrillic" },
                { 0x0417, "Zecyrillic" },
                { 0x0418, "Iicyrillic" },
                { 0x0419, "Iishortcyrillic" },
                { 0x041a, "Kacyrillic" },
                { 0x041b, "Elcyrillic" },
                { 0x041c, "Emcyrillic" },
                { 0x041d, "Encyrillic" },
                { 0x041e, "Ocyrillic" },
                { 0x041f, "Pecyrillic" },
                { 0x0420, "Ercyrillic" },
                { 0x0421, "Escyrillic" },
                { 0x0422, "Tecyrillic" },
                { 0x0423, "Ucyrillic" },
                { 0x0424, "Efcyrillic" },
                { 0x0425, "Khacyrillic" },
                { 0x0426, "Tsecyrillic" },
                { 0x0427, "Checyrillic" },
                { 0x0428, "Shacyrillic" },
                { 0x0429, "Shchacyrillic" },
                { 0x042a, "Hardsigncyrillic" },
                { 0x042b, "Yericyrillic" },
                { 0x042c, "Softsigncyrillic" },
                { 0x042d, "Ereversedcyrillic" },
                { 0x042e, "IUcyrillic" },
                { 0x042f, "IAcyrillic" },
                { 0x0430, "acyrillic" },
                { 0x0431, "becyrillic" },
                { 0x0432, "vecyrillic" },
                { 0x0433, "gecyrillic" },
                { 0x0434, "decyrillic" },
                { 0x0435, "iecyrillic" },
                { 0x0436, "zhecyrillic" },
                { 0x0437, "zecyrillic" },
                { 0x0438, "iicyrillic" },
                { 0x0439, "iishortcyrillic" },
                { 0x043a, "kacyrillic" },
                { 0x043b, "elcyrillic" },
                { 0x043c, "emcyrillic" },
                { 0x043d, "encyrillic" },
                { 0x043e, "ocyrillic" },
                { 0x043f, "pecyrillic" },
                { 0x0440, "ercyrillic" },
                { 0x0441, "escyrillic" },
                { 0x0442, "tecyrillic" },
                { 0x0443, "ucyrillic" },
                { 0x0444, "efcyrillic" },
                { 0x0445, "khacyrillic" },
                { 0x0446, "tsecyrillic" },
                { 0x0447, "checyrillic" },
                { 0x0448, "shacyrillic" },
                { 0x0449, "shchacyrillic" },
                { 0x044a, "hardsigncyrillic" },
                { 0x044b, "yericyrillic" },
                { 0x044c, "softsigncyrillic" },
                { 0x044d, "ereversedcyrillic" },
                { 0x044e, "iucyrillic" },
                { 0x044f, "iacyrillic" },
                { 0x0451, "iocyrillic" },
                { 0x0452, "djecyrillic" },
                { 0x0453, "gjecyrillic" },
                { 0x0454, "ecyrillic" },
                { 0x0455, "dzecyrillic" },
                { 0x0456, "icyrillic" },
                { 0x0457, "yicyrillic" },
                { 0x0458, "jecyrillic" },
                { 0x0459, "ljecyrillic" },
                { 0x045a, "njecyrillic" },
                { 0x045b, "tshecyrillic" },
                { 0x045c, "kjecyrillic" },
                { 0x045e, "ushortcyrillic" },
                { 0x045f, "dzhecyrillic" },
                { 0x0490, "gheupturncyrillic" },
                { 0x0491, "Ghestrokecyrillic" },
                { 0x1e80, "Wgrave" },
                { 0x1e81, "wgrave" },
                { 0x1e82, "Wacute" },
                { 0x1e83, "wacute" },
                { 0x1e84, "Wdieresis" },
                { 0x1e85, "wdieresis" },
                { 0x1ef2, "Ygrave" },
                { 0x1ef3, "ygrave" },
                { 0x2013, "endash" },
                { 0x2014, "emdash" },
                { 0x2015, "horizontalbar" },
                { 0x2017, "underscoredbl" },
                { 0x2018, "quoteleft" },
                { 0x2019, "quoteright" },
                { 0x201a, "quotesinglbase" },
                { 0x201b, "quotereversed" },
                { 0x201c, "quotedblleft" },
                { 0x201d, "quotedblright" },
                { 0x201e, "quotedblbase" },
                { 0x2020, "dagger" },
                { 0x2021, "daggerdbl" },
                { 0x2022, "bullet" },
                { 0x2026, "ellipsis" },
                { 0x2030, "perthousand" },
                { 0x2032, "minute" },
                { 0x2033, "second" },
                { 0x2039, "guilsinglleft" },
                { 0x203a, "guilsinglright" },
                { 0x203c, "exclamdbl" },
                { 0x203e, "overline" },
                { 0x2044, "fraction" },
                { 0x207f, "nsuperior" },
                { 0x20a3, "franc" },
                { 0x20a4, "lira" },
                { 0x20a7, "peseta" },
                { 0x20ac, "Euro" },
                { 0x2105, "careof" },
                { 0x2113, "lsquare" },
                { 0x2116, "numero" },
                { 0x2122, "trademark" },
                { 0x2126, "Omega" },
                { 0x212e, "estimated" },
                { 0x215b, "oneeighth" },
                { 0x215c, "threeeighths" },
                { 0x215d, "fiveeighths" },
                { 0x215e, "seveneighths" },
                { 0x2190, "arrowleft" },
                { 0x2191, "arrowup" },
                { 0x2192, "arrowright" },
                { 0x2193, "arrowdown" },
                { 0x2194, "arrowboth" },
                { 0x2195, "arrowupdn" },
                { 0x21a8, "arrowupdnbse" },
                { 0x2202, "partialdiff" },
                { 0x2206, "Delta" },
                { 0x220f, "product" },
                { 0x2211, "summation" },
                { 0x2212, "minus" },
                { 0x2215, "divisionslash" },
                { 0x2219, "bulletoperator" },
                { 0x221a, "radical" },
                { 0x221e, "infinity" },
                { 0x221f, "orthogonal" },
                { 0x2229, "intersection" },
                { 0x222b, "integral" },
                { 0x2248, "approxequal" },
                { 0x2260, "notequal" },
                { 0x2261, "equivalence" },
                { 0x2264, "lessequal" },
                { 0x2265, "greaterequal" },
                { 0x2302, "house" },
                { 0x2310, "revlogicalnot" },
                { 0x2320, "integraltp" },
                { 0x2321, "integralbt" },
                { 0x2500, "SF100000" },
                { 0x2502, "SF110000" },
                { 0x250c, "SF010000" },
                { 0x2510, "SF030000" },
                { 0x2514, "SF020000" },
                { 0x2518, "SF040000" },
                { 0x251c, "SF080000" },
                { 0x2524, "SF090000" },
                { 0x252c, "SF060000" },
                { 0x2534, "SF070000" },
                { 0x253c, "SF050000" },
                { 0x2550, "SF430000" },
                { 0x2551, "SF240000" },
                { 0x2552, "SF510000" },
                { 0x2553, "SF520000" },
                { 0x2554, "SF390000" },
                { 0x2555, "SF220000" },
                { 0x2556, "SF210000" },
                { 0x2557, "SF250000" },
                { 0x2558, "SF500000" },
                { 0x2559, "SF490000" },
                { 0x255a, "SF380000" },
                { 0x255b, "SF280000" },
                { 0x255c, "SF270000" },
                { 0x255d, "SF260000" },
                { 0x255e, "SF360000" },
                { 0x255f, "SF370000" },
                { 0x2560, "SF420000" },
                { 0x2561, "SF190000" },
                { 0x2562, "SF200000" },
                { 0x2563, "SF230000" },
                { 0x2564, "SF470000" },
                { 0x2565, "SF480000" },
                { 0x2566, "SF410000" },
                { 0x2567, "SF450000" },
                { 0x2568, "SF460000" },
                { 0x2569, "SF400000" },
                { 0x256a, "SF540000" },
                { 0x256b, "SF530000" },
                { 0x256c, "SF440000" },
                { 0x2580, "upblock" },
                { 0x2584, "dnblock" },
                { 0x2588, "block" },
                { 0x258c, "lfblock" },
                { 0x2590, "rtblock" },
                { 0x2591, "ltshade" },
                { 0x2592, "shade" },
                { 0x2593, "dkshade" },
                { 0x25a0, "filledbox" },
                { 0x25a1, "H22073" },
                { 0x25aa, "H18543" },
                { 0x25ab, "H18551" },
                { 0x25ac, "blackrectangle" },
                { 0x25b2, "triagup" },
                { 0x25ba, "triagrt" },
                { 0x25bc, "triagdn" },
                { 0x25c4, "triaglf" },
                { 0x25ca, "lozenge" },
                { 0x25cb, "circle" },
                { 0x25cf, "blackcircle" },
                { 0x25d8, "invbullet" },
                { 0x25d9, "invcircle" },
                { 0x25e6, "openbullet" },
                { 0x263a, "smileface" },
                { 0x263b, "invsmileface" },
                { 0x263c, "sun" },
                { 0x2640, "female" },
                { 0x2642, "male" },
                { 0x2660, "spade" },
                { 0x2663, "club" },
                { 0x2665, "heart" },
                { 0x2666, "diamond" },
                { 0x266a, "musicalnote" },
                { 0x266b, "musicalnotedbl" },
                { 0xfb01, "fi" },
                { 0xfb02, "fl" }
            };
        }

       
        public static string? GetMacName(int index)
        {
            if (index < macNames.Length)
            {
                return macNames[index];
            }

            return null;
        }
        
        public static int MacNameCount
        {
            get { return macNames.Length; }
        }

        public static int GetMacIndex(string name)
        {
            int nIndex = -1;

            for (int i = 0; i < macNames.Length; i++)
            {
                if (name == macNames[i])
                {
                    nIndex = i;
                    break;
                }
            }
            return nIndex;
        }

        public static string GetPostNameFromUnicode(ushort nVal)
        {
            if (postNames.ContainsKey((int)nVal))
            {
                return postNames[(int)nVal]!.ToString()!;
            }
            else
            {
                return "uni" + ((uint)nVal).ToString("X4");
            }
        }

    }

    /// <summary>
    /// Summary description for Table_post.
    /// </summary>
    public class Table_post : OTTable
    {
        /************************
         * constructors
         */
        
        
        public Table_post(OTTag tag, MBOBuffer buf) : base(tag, buf)
        {
            //m_nameOffsets = null;

            if (Version.GetUint() == 0x00020000)
            {
                // make sure we have more than a header
                if (GetLength() < 34)
                    return;

                // count the number of strings
                m_nNumberOfStrings = 0;
                uint offset = (uint)FieldOffsetsVer2.glyphNameIndex + (uint)numberOfGlyphs*2;
                uint length = 0;
                while (offset< GetLength())
                {
                    m_nNumberOfStrings++;
                    length = m_bufTable.GetByte(offset);
                    offset += length + 1;
                }
                // store the offsets
                m_nameOffsets = new uint[m_nNumberOfStrings];
                offset = (uint)FieldOffsetsVer2.glyphNameIndex + (uint)numberOfGlyphs*2;
                for (uint i=0; i<m_nNumberOfStrings; i++)
                {
                    m_nameOffsets[i] = offset;
                    length = m_bufTable.GetByte(offset);
                    offset += length + 1;
                }
            }
        }

        /************************
         * field offset values
         */

        
        public enum FieldOffsets
        {
            Version            = 0, 
            italicAngle        = 4,
            underlinePosition  = 8,
            underlineThickness = 10,
            isFixedPitch       = 12,
            minMemType42       = 16,
            maxMemType42       = 20,
            minMemType1        = 24,
            maxMemType1        = 28
        }

        public enum FieldOffsetsVer2
        {
            numberOfGlyphs     = 32,
            glyphNameIndex     = 34
        }


        /************************
         * property accessors
         */

        public OTFixed Version
        {
            get {return m_bufTable.GetFixed((uint)FieldOffsets.Version);}
        }

        public OTFixed italicAngle
        {
            get {return m_bufTable.GetFixed((uint)FieldOffsets.italicAngle);}
        }

        public short underlinePosition
        {
            get {return m_bufTable.GetShort((uint)FieldOffsets.underlinePosition);}
        }

        public short underlineThickness
        {
            get {return m_bufTable.GetShort((uint)FieldOffsets.underlineThickness);}
        }

        public uint isFixedPitch
        {
            get {return m_bufTable.GetUint((uint)FieldOffsets.isFixedPitch);}
        }

        public uint minMemType42
        {
            get {return m_bufTable.GetUint((uint)FieldOffsets.minMemType42);}
        }

        public uint maxMemType42
        {
            get {return m_bufTable.GetUint((uint)FieldOffsets.maxMemType42);}
        }

        public uint minMemType1
        {
            get {return m_bufTable.GetUint((uint)FieldOffsets.minMemType1);}
        }

        public uint maxMemType1
        {
            get {return m_bufTable.GetUint((uint)FieldOffsets.maxMemType1);}
        }

        public ushort numberOfGlyphs
        {
            get
            {
                if (Version.GetUint() != 0x00020000)
                {
                    throw new InvalidOperationException();
                }
                return m_bufTable.GetUshort((uint)FieldOffsetsVer2.numberOfGlyphs);
            }
        }

        public ushort GetGlyphNameIndex(ushort iGlyph)
        {
            if (Version.GetUint() != 0x00020000)
            {
                throw new InvalidOperationException();
            }
            if (iGlyph >= numberOfGlyphs)
            {
                throw new ArgumentOutOfRangeException();
            }
            return m_bufTable.GetUshort((uint)FieldOffsetsVer2.glyphNameIndex + (uint)iGlyph*2);
        }

        public byte[] GetName(uint i)
        {
            if (Version.GetUint() != 0x00020000)
            {
                throw new InvalidOperationException();
            }
            uint length = m_bufTable.GetByte(m_nameOffsets![i]);
            byte [] buf = new byte[length+1];
            Buffer.BlockCopy(m_bufTable.GetBuffer(), (int)m_nameOffsets[i], buf, 0, (int)length+1);
            return buf;
        }

        public string GetNameString(uint i)
        {
            if (Version.GetUint() != 0x00020000)
            {
                throw new InvalidOperationException();
            }

            if (i >= m_nameOffsets!.Length) return "";

            int length = m_bufTable.GetByte(m_nameOffsets[i]);
            
            if (length == 0) return "";
            
            StringBuilder sb = new StringBuilder(length,length);

            for (uint j = 1; j <= length; j++)
            {
                sb.Append((char)m_bufTable.GetByte(m_nameOffsets[i]+j));
            }

            return sb.ToString();
        }
        
        public string? GetGlyphName(ushort glyphId)
        {
            ushort nameIndex = GetGlyphNameIndex(glyphId);
           
            if (nameIndex < StandardPostNames.MacNameCount)
            {
                return StandardPostNames.GetMacName(nameIndex);
            }
            else 
            {
                return GetNameString((uint)(nameIndex - StandardPostNames.MacNameCount));
            }
        }

        public uint NumberOfStrings{ get{ return m_nNumberOfStrings; }}

        protected uint []? m_nameOffsets;
        protected uint m_nNumberOfStrings;


        /************************
         * DataCache class
         */

        public override DataCache GetCache()
        {
            if (m_cache == null)
            {
                m_cache = new post_cache(this);
            }

            return m_cache;
        }
        
        public class post_cache : DataCache
        {
            protected OTFixed m_Version;
            protected OTFixed m_italicAngle;
            protected short m_underlinePosition;
            protected short m_underlineThickness;
            protected uint m_isFixedPitch;
            protected uint m_minMemType42;
            protected uint m_maxMemType42;
            protected uint m_minMemType1;
            protected uint m_maxMemType1;
            protected ushort m_numberOfGlyphs; // v2.0, v2.5
            protected List<ushort> m_glyphNameIndex = []; // v2.0 ushort[] array
            protected List<string> m_names = []; // v2.0, string[] array, Store in strings but write out byte[] to the buffer
            protected char[]? m_offset; // v2.5 NOTE: We may not need? so not supported yet

            // constructor
            public post_cache(Table_post OwnerTable)
            {
                m_Version = OwnerTable.Version;
                m_italicAngle = OwnerTable.italicAngle;
                m_underlinePosition = OwnerTable.underlinePosition;
                m_underlineThickness = OwnerTable.underlineThickness;
                m_isFixedPitch = OwnerTable.isFixedPitch;
                m_minMemType42 = OwnerTable.minMemType42;
                m_maxMemType42 = OwnerTable.maxMemType42;
                m_minMemType1 = OwnerTable.minMemType1;
                m_maxMemType1 = OwnerTable.maxMemType1;

                // NOTE: what about version 2.5 is that covered with this check?
                // NOTE: Are we not checking because it is deprecated?
                if( m_Version.GetUint() == 0x00020000 )
                {
                    m_numberOfGlyphs = OwnerTable.numberOfGlyphs;
                    m_glyphNameIndex = new (m_numberOfGlyphs);

                    for ( ushort i = 0; i < m_numberOfGlyphs; i++ )
                    {
                        m_glyphNameIndex.Add( OwnerTable.GetGlyphNameIndex( i ));
                    }

                    m_names = new ((int)OwnerTable.NumberOfStrings);

                    // Get the gyph names
                    for ( uint i = 0; i < OwnerTable.NumberOfStrings; i++ )
                    {
                        m_names.Add(OwnerTable.GetNameString(i));
                    }
                }

            }

            // accessors for the cached data
            public void MakeDirty()
            {
                m_bDirty = true;
            }

            public OTFixed Version
            {
                get
                {
                    return m_Version;
                }
                set
                {
                    // NOTE: if the version is changed do we try to fix up the m_names?
                    // For now we will let the user handle this
                    if (value != m_Version)
                    {
                        m_Version = value;
                        m_bDirty = true;
                    }
                }
            }

            public OTFixed italicAngle
            {
                get
                {
                    return m_italicAngle;
                }
                set
                {
                    if (value != m_italicAngle)
                    {
                        m_italicAngle = value;
                        m_bDirty = true;
                    }
                }
            }


            public short underlinePosition
            {
                get
                {
                    return m_underlinePosition;
                }
                set
                {
                    if (value != m_underlinePosition)
                    {
                        m_underlinePosition = value;
                        m_bDirty = true;
                    }
                }
            }

            public short underlineThickness
            {
                get
                {
                    return m_underlineThickness;
                }
                set
                {
                    if (value != m_underlineThickness)
                    {
                        m_underlineThickness = value;
                        m_bDirty = true;
                    }
                }
            }

            public uint isFixedPitch
            {
                get
                {
                    return m_isFixedPitch;
                }
                set
                {
                    if (value != m_isFixedPitch)
                    {
                        m_isFixedPitch = value;
                        m_bDirty = true;
                    }
                }
            }


            public uint minMemType42
            {
                get
                {
                    return m_minMemType42;
                }
                set
                {
                    if (value != m_minMemType42)
                    {
                        m_minMemType42 = value;
                        m_bDirty = true;
                    }
                }
            }

            public uint maxMemType42
            {
                get
                {
                    return m_maxMemType42;
                }
                set
                {
                    if (value != m_maxMemType42)
                    {
                        m_maxMemType42 = value;
                        m_bDirty = true;
                    }
                }
            }

            public uint minMemType1
            {
                get
                {
                    return m_minMemType1;
                }
                set
                {
                    if (value != m_minMemType1)
                    {
                        m_minMemType1 = value;
                        m_bDirty = true;
                    }
                }
            }

            public uint maxMemType1
            {
                get
                {
                    return m_maxMemType1;
                }
                set
                {
                    if (value != m_maxMemType1)
                    {
                        m_maxMemType1 = value;
                        m_bDirty = true;
                    }
                }
            }

            // NOTE: I don't believe we want to allow this to be set here
            // since we automatically adjust this when we add index and name information
            public ushort numberOfGlyphs
            {                
                get
                {
                    if( m_Version.GetUint() != 0x00020000 )
                    {
                        throw new System.InvalidOperationException();
                    }
                    return m_numberOfGlyphs;
                }                
            }

            public ushort getGlyphNameIndex(ushort nGlyphIndex)
            {
                if( nGlyphIndex < m_glyphNameIndex.Count )
                {
                    return (ushort)m_glyphNameIndex[nGlyphIndex];
                }
                return 0;
            }

            public bool setGlyphNameIndex(ushort nGlyphIndex, ushort nValue)
            {
                if ( nGlyphIndex < m_glyphNameIndex.Count)
                {
                    m_glyphNameIndex[nGlyphIndex] = nValue;
                    return true;
                }
                else
                    return false;
            }

            public bool setGlyphNameIndexSize(ushort numGlyphs)
            {
                bool updated = false;
                ushort beginCount = (ushort)m_glyphNameIndex.Count;

                // expand the array size if we need to make it bigger
                for (ushort i = numGlyphs; i > beginCount; i--)
                {
                    // ser are simply adding an entry that is .notdef.
                    m_glyphNameIndex.Add((ushort)0);

                    // increase the number of gyphs since we just added one
                    m_numberOfGlyphs++;                    

                    updated = true;
                    m_bDirty = true;
                }
                return updated;
            }

            public void RebuildNameStringList()
            {
                m_names.Clear();
            }

            public string getNameString(ushort nNameIndex)
            {
                if (nNameIndex < m_names.Count)
                {
                    return m_names[nNameIndex].ToString();
                }
                else
                {
                    return "Out of range";
                }
            }
            
            public string? GetGlyphName(ushort glyphId)
            {
                ushort nameIndex = getGlyphNameIndex(glyphId);
           
                if (nameIndex < StandardPostNames.MacNameCount)
                {
                    return StandardPostNames.GetMacName(nameIndex);
                }
                else 
                {
                    return getNameString((ushort)(nameIndex - StandardPostNames.MacNameCount));
                }
            }

            public int addNameString(string sNameOfGlyph)
            {
                m_bDirty = true;
                m_names.Add(sNameOfGlyph);
                return m_names.Count - 1;
            }

            // If we remove a glyph this lets us remove the index and name
            public bool removeGlyphIndexAndName( ushort nGlyphIndex )
            {
                bool bResult = false;

                if( m_Version.GetUint() != 0x00020000 )
                {                    
                    throw new System.InvalidOperationException();
                }
                else if( (ushort)m_glyphNameIndex[nGlyphIndex] > 257 )
                {
                    // all nems under 258 are Macintosh order and are set so they are not stored here
                    m_names.RemoveAt( (ushort)m_glyphNameIndex[nGlyphIndex] - 258 );                    

                    // Now remove the index entry for this glyph
                    m_glyphNameIndex.RemoveAt( nGlyphIndex );
                    
                    // decrease the number of gyphs since we just removed one
                    m_numberOfGlyphs--;

                    // Fix up the indexes
                    for( int i = 0; i < m_numberOfGlyphs; i++ )
                    {
                        if( (ushort)m_glyphNameIndex[i] >= nGlyphIndex )
                        {
                            m_glyphNameIndex[i] = (ushort)(m_glyphNameIndex[i] - 1);    
                        }
                    }

                    m_bDirty = true;
                    bResult = true;
                }

                return bResult;

            }

            // Use to add a brand new glyph entry
            public bool addNewGlyphIndexAndName( ushort nGlyphIndex, string sNameOfGlyph )
            {
                bool bResult = false;

                if( m_Version.GetUint() != 0x00020000 )
                {                    
                    throw new System.InvalidOperationException();
                }
                else if( (ushort)m_glyphNameIndex[nGlyphIndex] > 257 )
                {
                    // all nems under 258 are Macintosh order and are set so 
                    // they are not stored Add the new name to the end of the list
                    m_names.Add( sNameOfGlyph );                    

                    // Now insert the new index entry for this glyph
                    m_glyphNameIndex.Insert( nGlyphIndex - 258, (ushort)(m_names.Count - 1) );
                    
                    // increase the number of gyphs since we just added one
                    m_numberOfGlyphs++;                    

                    m_bDirty = true;
                    bResult = true;
                }

                return bResult;
            }

            // Use to add a band new glyph entry
            public bool changeGlyphName( ushort nGlyphIndex, string sNameOfGlyph )
            {
                bool bResult = false;

                if( m_Version.GetUint() != 0x00020000 )
                {                    
                    throw new System.InvalidOperationException();
                }
                else if( (ushort)m_glyphNameIndex[nGlyphIndex] > 258 )
                {
                    // all nems under 258 are Macintosh order and are set so 
                    // they are not stored Add the new name to the end of the list
                    m_names[(ushort)m_glyphNameIndex[nGlyphIndex] - 258] = sNameOfGlyph;    
                    
                    m_bDirty = true;
                    bResult = true;
                }

                return bResult;
            }

            public override OTTable GenerateTable()
            {
                // create a Motorola Byte Order buffer for the new table
                MBOBuffer newbuf;
                uint nSizeOfIndexAndNames = 0;                

                if( m_Version.GetUint() == 0x00020000 )
                {
                    uint nSizeOfByteArray = 0;
    
                    // Get what the size of the byte array will be
                    for(int i = 0; i < m_names.Count; i++ )
                    {
                        nSizeOfByteArray += (uint)(((string)m_names[i]).Length + 1); 
                    }                    

                    // Add 2 for the ushort numberOfGlyphs
                    nSizeOfIndexAndNames = (uint)(2 + (m_numberOfGlyphs * 2) + nSizeOfByteArray);                    

                }

                newbuf = new MBOBuffer(32 + nSizeOfIndexAndNames);

                newbuf.SetFixed( m_Version, (uint)Table_post.FieldOffsets.Version );
                newbuf.SetFixed( m_italicAngle, (uint)Table_post.FieldOffsets.italicAngle );
                newbuf.SetShort( m_underlinePosition, (uint)Table_post.FieldOffsets.underlinePosition );
                newbuf.SetShort( m_underlineThickness, (uint)Table_post.FieldOffsets.underlineThickness );
                newbuf.SetUint( m_isFixedPitch, (uint)Table_post.FieldOffsets.isFixedPitch );
                newbuf.SetUint( m_minMemType42, (uint)Table_post.FieldOffsets.minMemType42 );
                newbuf.SetUint( m_maxMemType42, (uint)Table_post.FieldOffsets.maxMemType42 );
                newbuf.SetUint( m_minMemType1, (uint)Table_post.FieldOffsets.minMemType1);
                newbuf.SetUint( m_maxMemType1, (uint)Table_post.FieldOffsets.maxMemType1);                


                if( m_Version.GetUint() == 0x00020000 )
                {
                    newbuf.SetUshort( m_numberOfGlyphs, (uint)Table_post.FieldOffsetsVer2.numberOfGlyphs);    

                    uint nOffset = (uint)Table_post.FieldOffsetsVer2.glyphNameIndex;
                    for( int i = 0; i < m_numberOfGlyphs; i++ )
                    {
                        newbuf.SetUshort( (ushort)m_glyphNameIndex[i], nOffset );
                        nOffset += 2;
                    }

                    // write out the names to the buffer in length followed by character bytes
                    for( int i = 0; i < m_names.Count; i++ )
                    {
                        string sName = (string)m_names[i];
                        newbuf.SetByte( (byte)sName.Length,      nOffset );
                        nOffset++;

                        for( int ii = 0; ii < sName.Length; ii++ )
                        {
                            newbuf.SetByte( (byte)sName[ii],      nOffset );
                            nOffset++;
                        }
                    }                
                }
                
                
                // put the buffer into a Table_maxp object and return it
                Table_post postTable = new Table_post("post", newbuf);

                return postTable;            
            }
        }

    }
}
