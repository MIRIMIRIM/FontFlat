#:project OTFontFile.Performance.Tests/OTFontFile.Performance.Tests.csproj

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using OTFontFile;
using OTFontFile.Subsetting;

var fontPath = @"F:\GitHub\FontFlat\OTFontFile.Performance.Tests\TestResources\SampleFonts\SourceHanSansCN-Regular.otf";
var tempDir = Path.GetTempPath();
var text = "中文";

// Our subset
using var file = new OTFile();
file.open(fontPath);
var font = file.GetFont(0)!;
var options = new SubsetOptions().AddText(text);
var subsetter = new Subsetter(options);
var ourFont = subsetter.Subset(font);

var ourPath = Path.Combine(tempDir, "verify_ours.otf");
using (var fs = new FileStream(ourPath, FileMode.Create))
    OTFile.WriteSfntFile(fs, ourFont);

// pyftsubset output
var unicodes = string.Join(",", text.Select(c => $"U+{(int)c:X4}"));
var pyftPath = Path.Combine(tempDir, "verify_pyft.otf");
var pyftsubset = Environment.GetEnvironmentVariable("FONTTOOLS_PATH") ?? "pyftsubset";
var psi = new ProcessStartInfo
{
    FileName = pyftsubset,
    Arguments = $"\"{fontPath}\" --unicodes={unicodes} --desubroutinize --layout-features=* --output-file=\"{pyftPath}\"",
    UseShellExecute = false,
    CreateNoWindow = true
};
Process.Start(psi)?.WaitForExit();

// Load and compare
using var pyftFile = new OTFile();
pyftFile.open(pyftPath);
var pyftFont = pyftFile.GetFont(0)!;

Console.WriteLine("=== Table Size Comparison ===\n");
Console.WriteLine($"{"Table",-8} {"Ours",8} {"pyft",8} {"Ratio",10}");
Console.WriteLine(new string('-', 40));

for (uint i = 0; i < pyftFont.GetNumTables(); i++)
{
    var entry = pyftFont.GetDirectoryEntry(i);
    if (entry == null) continue;
    var tag = entry.tag.ToString();
    
    var pyftSize = (int)(pyftFont.GetTable(tag)?.GetBuffer()?.GetLength() ?? 0);
    var ourSize = (int)(ourFont.GetTable(tag)?.GetBuffer()?.GetLength() ?? 0);
    var ratio = pyftSize > 0 ? 100.0 * ourSize / pyftSize : 0;
    
    var status = Math.Abs(ratio - 100) <= 5 ? "✓" : (ourSize == 0 ? "MISS" : "≠");
    Console.WriteLine($"{tag,-8} {ourSize,8} {pyftSize,8} {ratio,9:F1}% {status}");
}

Console.WriteLine($"\n=== File Size ===");
Console.WriteLine($"Ours: {new FileInfo(ourPath).Length} bytes");
Console.WriteLine($"pyft: {new FileInfo(pyftPath).Length} bytes");
Console.WriteLine($"Ratio: {100.0 * new FileInfo(ourPath).Length / new FileInfo(pyftPath).Length:F1}%");
