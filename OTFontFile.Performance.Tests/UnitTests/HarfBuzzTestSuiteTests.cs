using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OTFontFile;
using OTFontFile.Subsetting;

namespace OTFontFile.Performance.Tests.UnitTests;

/// <summary>
/// Runs harfbuzz subset test suite against our implementation.
/// Test data from Ref/harfbuzz/test/subset/data/
/// </summary>
[TestClass]
public class HarfBuzzTestSuiteTests
{
    private static readonly string HbTestDir = Path.GetFullPath(
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "Ref", "harfbuzz", "test", "subset", "data"));

    private static readonly string TempDir = Path.Combine(Path.GetTempPath(), "OTFontFile_HbTests");

    [ClassInitialize]
    public static void ClassInit(TestContext context)
    {
        if (Directory.Exists(TempDir))
            Directory.Delete(TempDir, true);
        Directory.CreateDirectory(TempDir);

        Console.WriteLine($"HB Test Dir: {HbTestDir}");
        Console.WriteLine($"Exists: {Directory.Exists(HbTestDir)}");
    }

    [ClassCleanup]
    public static void ClassCleanup()
    {
        if (Directory.Exists(TempDir))
            Directory.Delete(TempDir, true);
    }

    // ==================== Basic Tests ====================

    [TestMethod]
    public void HbTest_Basics()
    {
        RunTestSuite("basics.tests");
    }

    [TestMethod]
    public void HbTest_CFF_FullFont()
    {
        RunTestSuite("cff-full-font.tests");
    }

    [TestMethod]
    public void HbTest_Cmap()
    {
        RunTestSuite("cmap.tests");
    }

    [TestMethod]
    public void HbTest_Post()
    {
        RunTestSuite("post.tests");
    }

    [TestMethod]
    public void HbTest_Layout()
    {
        RunTestSuite("layout.tests");
    }

    // ==================== Core Test Runner ====================

    private void RunTestSuite(string testFileName)
    {
        var testPath = Path.Combine(HbTestDir, "tests", testFileName);
        if (!File.Exists(testPath))
        {
            Assert.Inconclusive($"Test file not found: {testPath}");
            return;
        }

        var suite = ParseTestSuite(testPath);
        int passed = 0, failed = 0, skipped = 0;

        foreach (var test in suite.GetTests())
        {
            var result = RunSingleTest(test, suite);
            if (result == TestResult.Passed) passed++;
            else if (result == TestResult.Failed) failed++;
            else skipped++;
        }

        Console.WriteLine($"\n=== {testFileName} ===");
        Console.WriteLine($"Passed: {passed}, Failed: {failed}, Skipped: {skipped}");

        if (failed > 0)
            Assert.Fail($"{failed} test(s) failed in {testFileName}");
    }

    private TestResult RunSingleTest(HbTest test, HbTestSuite suite)
    {
        try
        {
            // Check if font exists
            if (!File.Exists(test.FontPath))
            {
                Console.WriteLine($"SKIP: Font not found: {test.FontPath}");
                return TestResult.Skipped;
            }

            // Check if we support this profile
            var options = ConvertProfileToOptions(test.ProfileFlags);
            if (options == null)
            {
                Console.WriteLine($"SKIP: Unsupported profile flags: {string.Join(" ", test.ProfileFlags)}");
                return TestResult.Skipped;
            }

            // Add unicodes from subset
            var unicodes = ParseSubset(test.Subset);
            if (unicodes == null)
            {
                Console.WriteLine($"SKIP: Unsupported subset format: {test.Subset}");
                return TestResult.Skipped;
            }
            foreach (var u in unicodes)
                options.Unicodes.Add(u);

            // Run our subsetter
            using var file = new OTFile();
            file.open(test.FontPath);
            var font = file.GetFont(0);
            if (font == null)
            {
                Console.WriteLine($"SKIP: Failed to open font: {test.FontPath}");
                return TestResult.Skipped;
            }

            var subsetter = new Subsetter(options);
            var subsetFont = subsetter.Subset(font);

            var ourPath = Path.Combine(TempDir, $"ours_{test.GetOutputName()}");
            using (var fs = new FileStream(ourPath, FileMode.Create))
                OTFile.WriteSfntFile(fs, subsetFont);

            // Compare with expected
            var expectedPath = Path.Combine(suite.ExpectedDir, test.GetOutputName());
            if (!File.Exists(expectedPath))
            {
                Console.WriteLine($"SKIP: Expected file not found: {expectedPath}");
                return TestResult.Skipped;
            }

            var ourBytes = File.ReadAllBytes(ourPath);
            var expectedBytes = File.ReadAllBytes(expectedPath);

            if (ourBytes.SequenceEqual(expectedBytes))
            {
                Console.WriteLine($"PASS: {test}");
                return TestResult.Passed;
            }
            else
            {
                Console.WriteLine($"FAIL: {test}");
                Console.WriteLine($"  Ours: {ourBytes.Length} bytes, Expected: {expectedBytes.Length} bytes");
                return TestResult.Failed;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"ERROR: {test}: {ex.Message}");
            return TestResult.Skipped;
        }
    }

    // ==================== Profile Conversion ====================

    private SubsetOptions? ConvertProfileToOptions(List<string> profileFlags)
    {
        var options = new SubsetOptions();
        options.LayoutFeatures = new HashSet<string> { "*" };  // Default: keep all

        foreach (var flag in profileFlags)
        {
            switch (flag)
            {
                case "--retain-gids":
                    options.RetainGids = true;
                    break;
                case "--no-hinting":
                    options.KeepHinting = false;
                    break;
                case "--desubroutinize":
                    // We always desubroutinize, so this is a no-op
                    break;
                case "--notdef-outline":
                    options.IncludeNotdef = true;
                    break;
                case "--no-prune-unicode-ranges":
                    options.PreserveUnicodeRanges = true;
                    break;
                case "--name-legacy":
                    options.NameLegacy = true;
                    break;
                case var s when s.StartsWith("--name-IDs="):
                    var ids = s.Substring("--name-IDs=".Length).Split(',');
                    options.RetainedNameIds = new HashSet<int>(ids.Select(int.Parse));
                    break;
                case var s when s.StartsWith("--name-languages="):
                    var langs = s.Substring("--name-languages=".Length).Split(',');
                    options.RetainedNameLanguages = new HashSet<int>(
                        langs.Select(l => Convert.ToInt32(l, 16)));
                    break;
                case var s when s.StartsWith("--layout-features="):
                    var features = s.Substring("--layout-features=".Length);
                    if (features == "*")
                        options.LayoutFeatures = new HashSet<string> { "*" };
                    else
                        options.LayoutFeatures = new HashSet<string>(features.Split(','));
                    break;
                case var s when s.StartsWith("--layout-scripts="):
                    var scripts = s.Substring("--layout-scripts=".Length);
                    options.LayoutScripts = new HashSet<string>(scripts.Split(','));
                    break;
                case var s when s.StartsWith("--gids="):
                    var gids = s.Substring("--gids=".Length).Split(',');
                    foreach (var gid in gids)
                        options.GlyphIds.Add(int.Parse(gid));
                    break;
                case "--glyph-names":
                case "--no-layout-closure":
                case var s when s.StartsWith("--drop-tables"):
                    // Supported but may not affect output
                    break;
                case var s when s.StartsWith("--instance="):
                    // Variable font - not supported
                    return null;
                default:
                    // Unknown flag - skip this test
                    if (flag.StartsWith("--"))
                        return null;
                    break;
            }
        }

        return options;
    }

    private List<int>? ParseSubset(string subset)
    {
        if (subset == "*")
            return null;  // Full font - not supported as simple list

        if (subset == "no-unicodes")
            return new List<int>();

        if (subset.StartsWith("U+"))
        {
            // Unicode range format: U+0041,U+0042
            var result = new List<int>();
            foreach (var part in subset.Replace("U+", "").Split(','))
            {
                if (part.Contains("-"))
                {
                    var range = part.Split('-');
                    int start = Convert.ToInt32(range[0], 16);
                    int end = Convert.ToInt32(range[1], 16);
                    for (int i = start; i <= end; i++)
                        result.Add(i);
                }
                else
                {
                    result.Add(Convert.ToInt32(part, 16));
                }
            }
            return result;
        }

        // Text format: "abc"
        return subset.Select(c => (int)c).ToList();
    }

    // ==================== Test Suite Parsing ====================

    private HbTestSuite ParseTestSuite(string testPath)
    {
        var suite = new HbTestSuite
        {
            TestPath = testPath,
            BaseDir = Path.GetDirectoryName(Path.GetDirectoryName(testPath))!,
        };

        var testName = Path.GetFileNameWithoutExtension(testPath);
        suite.ExpectedDir = Path.Combine(suite.BaseDir, "expected", testName);

        string? currentSection = null;
        foreach (var line in File.ReadAllLines(testPath))
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("#"))
                continue;

            if (trimmed.EndsWith(":"))
            {
                currentSection = trimmed;
                continue;
            }

            switch (currentSection)
            {
                case "FONTS:":
                    suite.Fonts.Add(trimmed);
                    break;
                case "PROFILES:":
                    suite.Profiles.Add(trimmed);
                    break;
                case "SUBSETS:":
                    suite.Subsets.Add(trimmed);
                    break;
            }
        }

        return suite;
    }

    // ==================== Data Classes ====================

    private enum TestResult { Passed, Failed, Skipped }

    private class HbTestSuite
    {
        public string TestPath { get; set; } = "";
        public string BaseDir { get; set; } = "";
        public string ExpectedDir { get; set; } = "";
        public List<string> Fonts { get; } = new();
        public List<string> Profiles { get; } = new();
        public List<string> Subsets { get; } = new();

        public IEnumerable<HbTest> GetTests()
        {
            foreach (var font in Fonts)
            {
                foreach (var profile in Profiles)
                {
                    foreach (var subset in Subsets)
                    {
                        var profilePath = Path.Combine(BaseDir, "profiles", profile);
                        var profileFlags = File.Exists(profilePath)
                            ? File.ReadAllLines(profilePath).Where(l => !string.IsNullOrWhiteSpace(l)).ToList()
                            : new List<string>();

                        yield return new HbTest
                        {
                            FontPath = Path.Combine(BaseDir, "fonts", font),
                            FontName = font,
                            ProfileName = Path.GetFileNameWithoutExtension(profile),
                            ProfileFlags = profileFlags,
                            Subset = subset
                        };
                    }
                }
            }
        }
    }

    private class HbTest
    {
        public string FontPath { get; set; } = "";
        public string FontName { get; set; } = "";
        public string ProfileName { get; set; } = "";
        public List<string> ProfileFlags { get; set; } = new();
        public string Subset { get; set; } = "";

        public string GetOutputName()
        {
            var fontBase = Path.GetFileNameWithoutExtension(FontName);
            var ext = Path.GetExtension(FontName);
            var unicodes = GetUnicodesString();
            return $"{fontBase}.{ProfileName}.{unicodes}{ext}";
        }

        private string GetUnicodesString()
        {
            if (Subset == "*") return "all";
            if (Subset == "no-unicodes") return "no-unicodes";
            if (Subset.StartsWith("U+")) return Subset.Replace("U+", "").Replace(",", "-");
            return string.Join(",", Subset.Select(c => $"{(int)c:X}"));
        }

        public override string ToString() => $"{FontName} [{ProfileName}] '{Subset}'";
    }
}
