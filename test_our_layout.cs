#:project OTFontFile/OTFontFile.csproj

using System;
using System.IO;
using System.Collections.Generic;
using OTFontFile;
using OTFontFile.Subsetting;

var fontPath = @"F:\GitHub\FontFlat\OTFontFile.Performance.Tests\TestResources\SampleFonts\SourceHanSansCN-Regular.otf";
var tempDir = Path.GetTempPath();

void TestSubset(string name, HashSet<string>? layoutFeatures, HashSet<string>? layoutScripts)
{
    using var file = new OTFile();
    file.open(fontPath);
    var font = file.GetFont(0)!;
    
    var options = new SubsetOptions().AddText("中文");
    options.LayoutFeatures = layoutFeatures;
    options.LayoutScripts = layoutScripts;
    
    var subsetter = new Subsetter(options);
    var subset = subsetter.Subset(font);
    
    var path = Path.Combine(tempDir, $"our_{name}.otf");
    using (var fs = new FileStream(path, FileMode.Create))
        OTFile.WriteSfntFile(fs, subset);
    
    var fi = new FileInfo(path);
    var gsub = subset.GetTable("GSUB");
    var gpos = subset.GetTable("GPOS");
    
    var gsubSize = gsub?.GetBuffer()?.GetLength() ?? 0;
    var gposSize = gpos?.GetBuffer()?.GetLength() ?? 0;
    
    Console.WriteLine($"{name}:");
    Console.WriteLine($"  Total: {fi.Length} bytes");
    Console.WriteLine($"  GSUB: {gsubSize} bytes");
    Console.WriteLine($"  GPOS: {gposSize} bytes");
    Console.WriteLine();
}

Console.WriteLine("=== Our Layout Options Testing ===\n");

// Default (uses DefaultLayoutFeatures)
TestSubset("default", null, null);

// Keep all features
TestSubset("all_features", new() { "*" }, null);

// Kern only
TestSubset("kern_only", new() { "kern" }, null);

// Empty = drop all
TestSubset("no_features", new(), null);

// All scripts
TestSubset("all_scripts", new() { "*" }, new() { "*" });

// Han script only
TestSubset("han_only", new() { "*" }, new() { "hani" });
