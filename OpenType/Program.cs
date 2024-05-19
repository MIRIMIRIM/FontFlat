using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using FontFlat.OpenType.FontTables;
using OTFontFile;

namespace FontFlat.OpenType;

internal class Program
{
    static void Main(string[] args)
    {
        //var file = @"F:\GitHub\sub_backup\Totokami\Fonts\FZLanTingHei-R-GBK.TTF";
        //var file = @"C:\Windows\Fonts\msyh.ttc";
        //var file = new FileInfo(@"C:\Windows\Fonts\華康飾藝體W7 & 華康飾藝體W7(P).ttc");
        var file = new FileInfo(@"G:\Typeface\超级字体整合包 XZ\Japanese\視覚デザイン研究所\old\TrueMegaMaru-U.ttf");

        //var file = new FileInfo(@"C:\Windows\Fonts\SourceHanSansCN-Regular.otf");
        //var file = new FileInfo(@"C:\Windows\Fonts\华康POP1体W5 & 华康POP1体W5(P).ttc");
        //var file = new FileInfo(@"C:\Windows\Fonts\Seguiemj.ttf");
        //var file = new FileInfo(@"G:\Typeface\超级字体整合包 XZ\Chinese\方正 Founder Type\方正×筑紫_buy\FZLongZhaoJ.OTF");

        //BenchmarkRunner.Run<GetNames>();

        //GetNames.DecodeAllNames(file);

        var fontFile = new FontFile(file);
        fontFile.Parse(ReaderFlag.Full);
        var face = fontFile.GetFont(0);
        var t = face.GetTableOS_2();
    }
}

[MemoryDiagnoser]
public class GetNames
{
    public static void DecodeAllNames(FileInfo f)
    {
        var fontFile = new FontFile(f);
        fontFile.Parse(ReaderFlag.Full);
        var face = fontFile.GetFont(0);
        FontTables.Table_name nameT = (FontTables.Table_name)face.GetTableName()!;

        //var offset = 19517096 + nameT.storageOffset;
        //var str = Read.ReadName(fontFile.reader, nameT.nameRecords[2], offset);

        List<string> names = new List<string>();
        for (var i = 0; i < nameT.nameRecords.Count(); i++)
        {
            names.Add(face.NameRecordDecodeString(nameT.nameRecords[i]));
        }
    }

    [Benchmark]
    public void DecodeAllNames() => DecodeAllNames(new FileInfo(@"G:\Typeface\超级字体整合包 XZ\Japanese\視覚デザイン研究所\old\TrueMegaMaru-U.ttf"));

    [Benchmark]
    public void DecodeAllNamesByOTF()
    {
        var f = new FileInfo(@"G:\Typeface\超级字体整合包 XZ\Japanese\視覚デザイン研究所\old\TrueMegaMaru-U.ttf");

        var otf = new OTFile();
        otf.open(f);
        var face = otf.GetFont(0);
        var nameTable = (OTFontFile.Table_name)face!.GetTable("name")!;

        List<string> names = new List<string>();
        for (uint i = 0; i < nameTable.NumberNameRecords; i++)
        {
            var rec = nameTable.GetNameRecord(i)!;

            names.Add(nameTable.GetString(rec.PlatformID, rec.EncodingID, rec.LanguageID, rec.NameID)!);
        }


    }
}
