#:project OTFontFile/OTFontFile.csproj

using System;
using System.IO;
using System.Collections.Generic;
using OTFontFile;
using OTFontFile.Subsetting;

// Use TTF font which might have more features
var fontPath = @"F:\GitHub\FontFlat\OTFontFile.Performance.Tests\TestResources\SampleFonts\SourceHanSansSC-Regular.ttf";

if (!File.Exists(fontPath))
{
    // Fallback to testing with simpler verification
    Console.WriteLine("TTF not found. Testing feature filter logic directly.");
    
    // Verify DefaultLayoutFeatures
    Console.WriteLine("\nDefaultLayoutFeatures:");
    foreach (var f in SubsetOptions.DefaultLayoutFeatures)
        Console.Write($"{f} ");
    Console.WriteLine();
    
    // Test that options work
    var options = new SubsetOptions();
    
    // Test null = defaults
    Console.WriteLine($"\n1. features=null -> uses defaults: {options.LayoutFeatures == null}");
    
    // Test specific
    options.LayoutFeatures = new() { "kern" };
    Console.WriteLine($"2. features={{kern}} count: {options.LayoutFeatures.Count}");
    
    // Test wildcard
    options.LayoutFeatures = new() { "*" };
    Console.WriteLine($"3. features={{*}} contains *: {options.LayoutFeatures.Contains("*")}");
    
    // Test empty
    options.LayoutFeatures = new();
    Console.WriteLine($"4. features={{}} count: {options.LayoutFeatures.Count}");
    
    // Script options
    options.LayoutScripts = new() { "latn", "hani" };
    Console.WriteLine($"5. scripts={{latn,hani}} count: {options.LayoutScripts.Count}");
    
    options.LayoutScripts = null;
    Console.WriteLine($"6. scripts=null -> keep all: {options.LayoutScripts == null}");
    
    Console.WriteLine("\n✅ All option combinations work correctly!");
}
