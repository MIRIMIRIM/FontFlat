#:project OTFontFile/OTFontFile.csproj

using System;
using System.IO;
using OTFontFile;
using OTFontFile.Subsetting;

var fontPath = @"F:\GitHub\FontFlat\OTFontFile.Performance.Tests\TestResources\SampleFonts\SourceHanSansCN-Regular.otf";

using var file = new OTFile();
file.open(fontPath);
var font = file.GetFont(0)!;

// Subset
var options = new SubsetOptions().AddText("中文");
var subsetter = new Subsetter(options);
var subset = subsetter.Subset(font);

// Get CFF table
var cffTable = subset.GetTable("CFF ");
if (cffTable == null) { Console.WriteLine("No CFF table!"); return; }

var buf = cffTable.GetBuffer();
var cffData = new byte[buf!.GetLength()];
for (uint i = 0; i < buf.GetLength(); i++)
    cffData[i] = buf.GetByte(i);

Console.WriteLine($"Our CFF table size: {cffData.Length} bytes");
Console.WriteLine($"\n=== CFF Structure ===");
Console.WriteLine($"Header: major={cffData[0]}, minor={cffData[1]}, hdrSize={cffData[2]}, offSize={cffData[3]}");

// Parse INDEX function
(List<byte[]> items, int end) ReadIndex(byte[] data, int pos)
{
    int count = (data[pos] << 8) | data[pos + 1];
    if (count == 0) return (new List<byte[]>(), pos + 2);
    int offSize = data[pos + 2];
    var offsets = new List<int>();
    for (int i = 0; i <= count; i++)
    {
        int off = 0;
        for (int j = 0; j < offSize; j++)
            off = (off << 8) | data[pos + 3 + i * offSize + j];
        offsets.Add(off);
    }
    int dataStart = pos + 3 + (count + 1) * offSize;
    var items = new List<byte[]>();
    for (int i = 0; i < count; i++)
    {
        var item = new byte[offsets[i + 1] - offsets[i]];
        Array.Copy(data, dataStart + offsets[i] - 1, item, 0, item.Length);
        items.Add(item);
    }
    return (items, dataStart + offsets[count] - 1);
}

int pos = cffData[2]; // hdrSize

var (nameIndex, nameEnd) = ReadIndex(cffData, pos);
Console.WriteLine($"Name INDEX: {nameIndex.Count} names at end pos {nameEnd}");
foreach (var name in nameIndex)
    Console.WriteLine($"  {System.Text.Encoding.ASCII.GetString(name)}");

var (topDictIndex, topDictEnd) = ReadIndex(cffData, nameEnd);
Console.WriteLine($"TopDICT INDEX: {topDictIndex.Count} at end pos {topDictEnd}");

var (stringIndex, stringEnd) = ReadIndex(cffData, topDictEnd);
Console.WriteLine($"String INDEX: {stringIndex.Count} strings at end pos {stringEnd}");
foreach (var s in stringIndex)
    Console.WriteLine($"  \"{System.Text.Encoding.ASCII.GetString(s)}\"");

var (globalSubrIndex, globalSubrEnd) = ReadIndex(cffData, stringEnd);
Console.WriteLine($"GlobalSubr INDEX: {globalSubrIndex.Count} subrs at end pos {globalSubrEnd}");
