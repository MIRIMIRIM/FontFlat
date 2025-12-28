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

// Save our output
var ourPath = Path.Combine(Path.GetTempPath(), "our_cff.otf");
using (var fs = new FileStream(ourPath, FileMode.Create))
{
    OTFile.WriteSfntFile(fs, subset);
}

// Read raw bytes
var ourBytes = File.ReadAllBytes(ourPath);
Console.WriteLine($"Our file size: {ourBytes.Length} bytes");

// Analyze file header
Console.WriteLine("\n=== File Header ===");
uint sfntVersion = (uint)((ourBytes[0] << 24) | (ourBytes[1] << 16) | (ourBytes[2] << 8) | ourBytes[3]);
ushort numTables = (ushort)((ourBytes[4] << 8) | ourBytes[5]);
ushort searchRange = (ushort)((ourBytes[6] << 8) | ourBytes[7]);
ushort entrySelector = (ushort)((ourBytes[8] << 8) | ourBytes[9]);
ushort rangeShift = (ushort)((ourBytes[10] << 8) | ourBytes[11]);

Console.WriteLine($"sfntVersion: 0x{sfntVersion:X8}");
Console.WriteLine($"numTables: {numTables}");
Console.WriteLine($"searchRange: {searchRange}");
Console.WriteLine($"entrySelector: {entrySelector}");
Console.WriteLine($"rangeShift: {rangeShift}");

// Analyze table directory
Console.WriteLine("\n=== Table Directory (16 bytes each) ===");
int tableOffset = 12;
uint totalTableSize = 0;
for (int i = 0; i < numTables; i++)
{
    string tag = System.Text.Encoding.ASCII.GetString(ourBytes, tableOffset, 4);
    uint checksum = (uint)((ourBytes[tableOffset + 4] << 24) | (ourBytes[tableOffset + 5] << 16) | 
                           (ourBytes[tableOffset + 6] << 8) | ourBytes[tableOffset + 7]);
    uint offset = (uint)((ourBytes[tableOffset + 8] << 24) | (ourBytes[tableOffset + 9] << 16) | 
                         (ourBytes[tableOffset + 10] << 8) | ourBytes[tableOffset + 11]);
    uint length = (uint)((ourBytes[tableOffset + 12] << 24) | (ourBytes[tableOffset + 13] << 16) | 
                         (ourBytes[tableOffset + 14] << 8) | ourBytes[tableOffset + 15]);
    
    Console.WriteLine($"  {tag}: offset={offset}, length={length}");
    totalTableSize += length;
    tableOffset += 16;
}

Console.WriteLine($"\nHeader + Directory: 12 + {numTables}*16 = {12 + numTables * 16} bytes");
Console.WriteLine($"Total table data: {totalTableSize} bytes");
Console.WriteLine($"File size: {ourBytes.Length} bytes");
Console.WriteLine($"Padding/overhead: {ourBytes.Length - (12 + numTables * 16) - totalTableSize} bytes");
