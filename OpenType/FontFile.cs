using FontFlat.OpenType.Helper;
using FontFlat.OpenType.FontTables;
using CommunityToolkit.Diagnostics;

namespace FontFlat.OpenType;

public class FontFile : IDisposable
{
    private readonly Stream stream;
    public readonly BigEndianBinaryReader reader;
    public FontType fontType;
    public int fontCount = 1;
    public bool isCollection = false;
    public OTFont[]? fonts;
    public CollectionHeader? ttcHeader;
    public byte[]? dsigData;


    public FontFile(string filePath)
    {
        stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        reader = new BigEndianBinaryReader(stream);
        GetFontType();
    }
    public FontFile(FileInfo fileInfo) : this(fileInfo.FullName) { }

    public void Dispose()
    {
        reader.Dispose();
        stream.Dispose();
    }

    public void Parse(ReaderFlag flag)
    {
        if (isCollection)
        {
            ParseTTC(flag);
        }
        else
        {
            var font = new OTFont(reader, 0, flag);
            font.ReadPackets();
            fonts = new OTFont[1];
            fonts[0] = font;
        }
    }
    public OTFont GetFont(int index)
    {
        if (index > fontCount) { ThrowHelper.ThrowArgumentOutOfRangeException("index"); }
        if (fonts is null) { ThrowHelper.ThrowArgumentNullException("You must Parse() before GetFont()"); }
        return fonts[index];
    }


    private void GetFontType()
    {
        fontType = (FontType)reader.ReadUInt32();

        switch (fontType)
        {
            case FontType.TrueType:
            case FontType.Otto:
            case FontType.True:
            case FontType.Typ1:
                fontCount = 1;
                break;
            case FontType.Collection:
                isCollection = true;
                break;
            case FontType.Woff:
            case FontType.Woff2:
            case FontType.Invalid:
                fontCount = 0;
                break;
        }
    }
    private void ParseTTC(ReaderFlag flag)
    {
        ttcHeader = FontTables.Read.ReadCollectionHeader(reader);
        var header = (CollectionHeader)ttcHeader;
        if (header.majorVersion == 2)
        {
            reader.BaseStream.Seek((long)header.dsigOffset!, SeekOrigin.Begin);
            dsigData = reader.ReadBytes((int)header.dsigLength!);
        }

        fontCount = (int)header.numFonts;
        fonts = new OTFont[fontCount];
        for (var i = 0; i < fontCount; i++)
        {
            var font = new OTFont(reader, (int)header.tableDirectoryOffsets[i], flag);
            font.ReadPackets();
            fonts[i] = font;
        }
    }
}

public enum FontType
{
    Invalid = 0x00000000,    // Unknown
    TrueType = 0x00010000,   // ttf
    Collection = 0x74746366, // 'ttcf', ttc or otc
    Otto = 0x4F54544F,       // 'OTTO', otf
    True = 0x74727565,       // 'true', OS X or iOS Only ttf
    Typ1 = 0x74797031,       // 'typ1', old style of PostScript font housed in a sfnt wrapper
    Woff = 0x774F4646,       // 'wOFF'
    Woff2 = 0x774F4632,      // 'wOF2'
}

public enum ReaderFlag
{
    Full = 0,
}