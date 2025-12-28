#:project OTFontFile/OTFontFile.csproj

using System;
using System.Text;
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

// Check for duplicate strings
var subsetName = subset.GetTable("name") as Table_name;
Console.WriteLine($"Name records: {subsetName!.NumberNameRecords}");

var strings = new Dictionary<string, List<ushort>>();
for (ushort i = 0; i < subsetName.NumberNameRecords; i++)
{
    var nr = subsetName.GetNameRecord(i)!;
    var str = subsetName.GetString(nr.PlatformID, nr.EncodingID, nr.LanguageID, nr.NameID) ?? "";
    if (!strings.ContainsKey(str))
        strings[str] = new List<ushort>();
    strings[str].Add(nr.NameID);
}

Console.WriteLine("\nString usage:");
foreach (var kvp in strings)
{
    if (kvp.Value.Count > 1)
        Console.WriteLine($"  DUPLICATE: \"{kvp.Key.Substring(0, Math.Min(30, kvp.Key.Length))}\" used by IDs: {string.Join(", ", kvp.Value)}");
    else
        Console.WriteLine($"  ID {kvp.Value[0]}: \"{kvp.Key.Substring(0, Math.Min(30, kvp.Key.Length))}\"");
}

// Calculate savings
int duplicateSavings = 0;
foreach (var kvp in strings)
{
    if (kvp.Value.Count > 1)
    {
        var strBytes = Encoding.BigEndianUnicode.GetBytes(kvp.Key);
        duplicateSavings += strBytes.Length * (kvp.Value.Count - 1);
    }
}
Console.WriteLine($"\nPotential savings from deduplication: {duplicateSavings} bytes");
