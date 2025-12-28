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

// Get GSUB table
var gsub = subset.GetTable("GSUB");
if (gsub != null)
{
    var buf = gsub.GetBuffer();
    var bytes = new byte[buf!.GetLength()];
    for (uint i = 0; i < buf.GetLength(); i++)
        bytes[i] = buf.GetByte(i);
    
    Console.WriteLine($"Our GSUB: {bytes.Length} bytes");
    Console.WriteLine("\nHex dump:");
    for (int i = 0; i < bytes.Length; i++)
    {
        Console.Write($"{bytes[i]:X2} ");
        if ((i + 1) % 16 == 0) Console.WriteLine();
    }
    Console.WriteLine();
    
    // Parse header
    ushort majorVer = (ushort)((bytes[0] << 8) | bytes[1]);
    ushort minorVer = (ushort)((bytes[2] << 8) | bytes[3]);
    ushort scriptListOffset = (ushort)((bytes[4] << 8) | bytes[5]);
    ushort featureListOffset = (ushort)((bytes[6] << 8) | bytes[7]);
    ushort lookupListOffset = (ushort)((bytes[8] << 8) | bytes[9]);
    
    Console.WriteLine($"\nHeader: v{majorVer}.{minorVer}");
    Console.WriteLine($"  ScriptListOffset: {scriptListOffset}");
    Console.WriteLine($"  FeatureListOffset: {featureListOffset}");
    Console.WriteLine($"  LookupListOffset: {lookupListOffset}");
    
    // Parse ScriptList
    if (scriptListOffset > 0 && scriptListOffset < bytes.Length)
    {
        int pos = scriptListOffset;
        ushort scriptCount = (ushort)((bytes[pos] << 8) | bytes[pos + 1]);
        Console.WriteLine($"\nScriptList at {pos}: {scriptCount} scripts");
        
        pos += 2;
        for (int i = 0; i < scriptCount && pos + 6 <= bytes.Length; i++)
        {
            string tag = System.Text.Encoding.ASCII.GetString(bytes, pos, 4);
            ushort offset = (ushort)((bytes[pos + 4] << 8) | bytes[pos + 5]);
            Console.WriteLine($"  Script '{tag}' at offset {offset}");
            pos += 6;
        }
    }
}
