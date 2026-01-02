using System.Collections.Frozen;
using System.Text.RegularExpressions;

namespace CJKCharacterCount.Core;

public enum CJKGroup { Simplified, Mixed, Traditional }

public sealed partial class CJKTable
{
    public string Id { get; init; }
    public CJKGroup Group { get; init; }
    public IReadOnlyDictionary<string, string> LocalizedNames { get; init; }

    private readonly FrozenSet<int> _codePoints;
    private readonly int[] _sortedCodePoints; // for SIMD intersection

    public int Count => _codePoints.Count;

    public CJKTable(string id, CJKGroup group, IReadOnlyDictionary<string, string> names, IEnumerable<int> codePoints)
    {
        Id = id;
        Group = group;
        LocalizedNames = names;
        _codePoints = codePoints.ToFrozenSet();
        _sortedCodePoints = [.. codePoints.OrderBy(x => x)];
    }

    public bool Contains(int codePoint) => _codePoints.Contains(codePoint);

    public int CountOverlap(ReadOnlySpan<int> sortedFontCodePoints)
    {
        // Intersection of two sorted arrays can be done efficiently
        // But here we can just iterate the smaller one and check presence in formatted structure?
        // Or use the provided sorted arrays to do linear merge-like count.

        int count = 0;
        int i = 0; // index for this table
        int j = 0; // index for font

        var tableSpan = _sortedCodePoints.AsSpan();
        var fontSpan = sortedFontCodePoints;

        while (i < tableSpan.Length && j < fontSpan.Length)
        {
            int val1 = tableSpan[i];
            int val2 = fontSpan[j];

            if (val1 == val2)
            {
                count++;
                i++;
                j++;
            }
            else if (val1 < val2)
            {
                i++;
            }
            else
            {
                j++;
            }
        }
        return count;
    }

    public IEnumerable<int> GetMissingCharacters(HashSet<int> fontCodePoints)
    {
        foreach (var c in _sortedCodePoints)
        {
            if (!fontCodePoints.Contains(c))
                yield return c;
        }
    }

    public static CJKTable LoadFromResource(string resourceName)
    {
        var userAssembly = typeof(CJKTable).Assembly;

        // Find full resource name
        var fullResourceName = userAssembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith(resourceName));

        if (fullResourceName == null)
            throw new FileNotFoundException($"Resource {resourceName} not found");

        using var stream = userAssembly.GetManifestResourceStream(fullResourceName)!;
        using var reader = new StreamReader(stream);
        var content = reader.ReadToEnd();

        return ParseTableContent(resourceName.Replace(".txt", ""), content);
    }

    private static CJKTable ParseTableContent(string filename, string content)
    {
        // Simple YAML frontmatter parser
        var match = FrontmatterRegex().Match(content);
        if (!match.Success)
            throw new InvalidDataException($"Invalid frontmatter in {filename}");

        var yaml = match.Groups[1].Value;
        var body = match.Groups[2].Value;

        // Parse Metadata manually
        var group = CJKGroup.Mixed;
        var names = new Dictionary<string, string>();

        // Parse cjk_group
        var groupMatch = GroupRegex().Match(yaml);
        if (groupMatch.Success)
        {
            group = groupMatch.Groups[1].Value.ToLower() switch
            {
                "jian" => CJKGroup.Simplified,
                "fan" => CJKGroup.Traditional,
                _ => CJKGroup.Mixed
            };
        }

        // Parse localized names
        // name:
        //   en: ...
        //   zhs: ...
        var nameBlockMatch = NameRegex().Match(yaml);
        if (nameBlockMatch.Success)
        {
            var nameLines = nameBlockMatch.Groups[1].Value.Split('\n');
            foreach (var line in nameLines)
            {
                var kv = line.Trim().Split(':', 2);
                if (kv.Length == 2)
                {
                    names[kv[0].Trim()] = kv[1].Trim();
                }
            }
        }

        // Parse characters
        var charList = new List<int>();
        using var bodyReader = new StringReader(body);
        string? lineChar;
        while ((lineChar = bodyReader.ReadLine()) != null)
        {
            if (string.IsNullOrWhiteSpace(lineChar)) continue;
            // Should be a single char per line usually, but verify?
            // Python implementation: map(str.strip, content...splitlines())
            // It implicitly assumes characters.
            var trimmed = lineChar.Trim();
            if (trimmed.Length > 0)
            {
                int cp = char.ConvertToUtf32(trimmed, 0);
                charList.Add(cp);
            }
        }

        return new CJKTable(filename.Replace("-han", ""), group, names, charList);
    }

    [GeneratedRegex(@"^---\s*(.*?)\s*---\s*(.*)", RegexOptions.Singleline)]
    private static partial Regex FrontmatterRegex();
    [GeneratedRegex(@"cjk_group:\s*(\w+)")]
    private static partial Regex GroupRegex();
    [GeneratedRegex(@"name:\s*((?:\s+.*)+)", RegexOptions.Singleline)]
    private static partial Regex NameRegex();
}
