using OTFontFile;
using System.Collections.Frozen;
using System.Globalization;

namespace CJKCharacterCount.Core;

public sealed class FontAnalyzer
{
    public string FontPath { get; }
    public string FontName { get; }
    public int FontIndex { get; }
    public HashSet<int> CodePoints { get; }
    private readonly int[] _sortedCodePoints;

    public FontAnalyzer(string fontPath, int fontIndex = -1)
    {
        FontPath = fontPath;
        FontIndex = fontIndex;

        // Load font using OTFontFile
        // OTFontFile API usually takes a file stream or buffer?
        // OTFile is the wrapper.

        var otFile = new OTFile();
        if (!otFile.open(fontPath))
            throw new FileNotFoundException($"Failed to open font file: {fontPath}");



        // If index -1 and it's a collection, what to do?
        // Python code handles this by popup. We'll default to 0 if not specified for TTC?
        // Or throw.
        // If it's TTC and index is -1, usually means "first" or "error"? 
        // For CLI/Lib, we should probably be explicit.
        // OTFontFile.OTFont constructor:
        // public OTFont(OTFile file) // constructs from first font?
        // or ReadFont(OTFile file, uint offset)

        OTFont? font;
        if (otFile.IsCollection())
        {
            if (fontIndex < 0) fontIndex = 0;
            font = otFile.GetFont((uint)fontIndex);
        }
        else
        {
            font = otFile.GetFont(0);
        }

        if (font == null)
            throw new Exception("Failed to load font from file");

        FontName = font.GetFontName() ?? Path.GetFileName(fontPath);
        CodePoints = CmapExtractor.ExtractCodePoints(font);
        _sortedCodePoints = [.. CodePoints.OrderBy(x => x)];
    }

    public FontAnalyzeResult Analyze()
    {
        var cjkStats = new Dictionary<string, TableStatistics>();
        var unicodeStats = new Dictionary<string, int>();

        // Analyze CJK Tables
        // Get all tables from registry
        var groups = new[] { CJKGroup.Simplified, CJKGroup.Mixed, CJKGroup.Traditional };
        foreach (var group in groups)
        {
            foreach (var table in CJKTableRegistry.GetByGroup(group))
            {
                int covered = table.CountOverlap(_sortedCodePoints);
                cjkStats[table.Id] = new TableStatistics
                {
                    Covered = covered,
                    Total = table.Count
                };
            }
        }

        // Analyze Unicode Blocks
        foreach (var block in UnicodeBlocks.AllBlocks)
        {
            int covered = block.CountOverlap(_sortedCodePoints);
            unicodeStats[block.Name] = covered;
        }

        // Total block special handling (same as Python logic: sum of specific blocks)
        int totalCovered = UnicodeBlocks.Total.CountOverlap(_sortedCodePoints);
        unicodeStats["Total"] = totalCovered;

        return new FontAnalyzeResult
        {
            CJKStatistics = cjkStats,
            UnicodeBlockStatistics = unicodeStats,
            TotalCJKCharacters = totalCovered
        };
    }

    public static IReadOnlyList<(string Name, int Index)> GetFontsInCollection(string fontPath, CultureInfo? culture = null)
    {
        var results = new List<(string Name, int Index)>();
        using var otFile = new OTFile();
        if (!otFile.open(fontPath))
            return results; // Or throw

        try
        {
            if (otFile.IsCollection())
            {
                uint count = otFile.GetNumFonts();
                for (uint i = 0; i < count; i++)
                {
                    var font = otFile.GetFont(i);
                    if (font != null)
                    {
                        string name = GetLocalizedFontName(font, culture) ?? $"Font {i + 1}";
                        results.Add((name, (int)i));
                    }
                }
            }
            else
            {
                // Single font
                var font = otFile.GetFont(0);
                if (font != null)
                {
                    string name = GetLocalizedFontName(font, culture) ?? Path.GetFileName(fontPath);
                    results.Add((name, 0));
                }
            }
        }
        finally
        {
            otFile.Dispose();
        }

        return results;
    }

    private static string? GetLocalizedFontName(OTFont font, CultureInfo? culture)
    {
        // Default to English/Generic if null
        string? defaultName = font.GetFontName(); // This usually gets English/Any
        if (culture == null) return defaultName;

        var nameTable = (Table_name?)font.GetTable("name");
        if (nameTable == null) return defaultName;

        // Map Culture to LCID
        // Simple mapping for CJK + En
        ushort lcid = 0x0409; // Default en-US
        if (culture.Name.StartsWith("zh-Hans") || culture.Name.StartsWith("zh-CN")) lcid = 0x0804;
        else if (culture.Name.StartsWith("zh-Hant") || culture.Name.StartsWith("zh-TW")) lcid = 0x0404;
        else if (culture.Name.StartsWith("zh-HK")) lcid = 0x0C04;
        else if (culture.Name.StartsWith("ja")) lcid = 0x0411;
        else if (culture.Name.StartsWith("ko")) lcid = 0x0412;

        // Try to get specific name
        // Helper to try FullName (4) then Family (1)
        string? GetName(ushort id) => nameTable.GetString(Table_name.PlatformID.Windows, Table_name.EncodingIDWindows.Unicode_BMP, (Table_name.LanguageIDWindows)lcid, (Table_name.NameID)id);

        string? locName = GetName(4); // Full Name
        if (string.IsNullOrEmpty(locName)) locName = GetName(1); // Family Name

        return !string.IsNullOrEmpty(locName) ? locName : defaultName;
    }
}

public sealed class FontAnalyzeResult
{
    public required IReadOnlyDictionary<string, TableStatistics> CJKStatistics { get; init; }
    public required IReadOnlyDictionary<string, int> UnicodeBlockStatistics { get; init; }
    public required int TotalCJKCharacters { get; init; }
}

public readonly struct TableStatistics
{
    public required int Covered { get; init; }
    public required int Total { get; init; }
    public double Percentage => Total > 0 ? (double)Covered / Total * 100 : 0;
}
