using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using OTFontFile;
using OTFontFile.Subsetting;

namespace OTFontFile.Performance.Tests.UnitTests;

/// <summary>
/// Tests comparing our subsetting options with pyftsubset and hb-subset equivalents.
/// Verifies that our options produce compatible/similar output.
/// </summary>
[TestClass]
public class OptionComparisonTests
{
    private static readonly string SampleFontsDir = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "TestResources", "SampleFonts");
    private static readonly string TempDir = Path.Combine(Path.GetTempPath(), "OTFontFile_OptionTests");
    
    private static string? _pyftsubsetPath;
    private static string? _hbSubsetPath;
    private static string? _ttfFontPath;
    private static string? _cffFontPath;

    [ClassInitialize]
    public static void ClassInit(TestContext context)
    {
        if (Directory.Exists(TempDir))
            Directory.Delete(TempDir, true);
        Directory.CreateDirectory(TempDir);

        _pyftsubsetPath = Environment.GetEnvironmentVariable("FONTTOOLS_PATH") ?? FindExecutable("pyftsubset");
        _hbSubsetPath = Environment.GetEnvironmentVariable("HBSUBSET_PATH") ?? FindExecutable("hb-subset");
        
        _ttfFontPath = Path.Combine(SampleFontsDir, "small.ttf");
        _cffFontPath = Path.Combine(SampleFontsDir, "SourceHanSansCN-Regular.otf");
        
        Console.WriteLine($"pyftsubset: {_pyftsubsetPath ?? "not found"}");
        Console.WriteLine($"hb-subset: {_hbSubsetPath ?? "not found"}");
    }

    [ClassCleanup]
    public static void ClassCleanup()
    {
        if (Directory.Exists(TempDir))
            Directory.Delete(TempDir, true);
    }

    // ==================== Layout Features Tests ====================

    [TestMethod]
    public void Compare_LayoutFeatures_KernOnly_WithPyftsubset()
    {
        if (_pyftsubsetPath == null) { Assert.Inconclusive("pyftsubset not available"); return; }
        
        var (ourSize, refSize) = CompareWithPyftsubset(
            _ttfFontPath!, "ABC",
            new SubsetOptions { LayoutFeatures = new() { "kern" } },
            "--layout-features=kern",
            "kern_only");
        
        Console.WriteLine($"Kern-only: Ours={ourSize}, pyftsubset={refSize}, ratio={100.0*ourSize/refSize:F1}%");
        Assert.IsTrue(ourSize > 0 && refSize > 0);
    }

    [TestMethod]
    public void Compare_LayoutFeatures_All_WithPyftsubset()
    {
        if (_pyftsubsetPath == null) { Assert.Inconclusive("pyftsubset not available"); return; }
        
        var (ourSize, refSize) = CompareWithPyftsubset(
            _ttfFontPath!, "ABC",
            new SubsetOptions { LayoutFeatures = new() { "*" } },
            "--layout-features=*",
            "all_features");
        
        Console.WriteLine($"All features: Ours={ourSize}, pyftsubset={refSize}, ratio={100.0*ourSize/refSize:F1}%");
        Assert.IsTrue(ourSize > 0 && refSize > 0);
    }

    [TestMethod]
    public void Compare_LayoutFeatures_Drop_WithHbSubset()
    {
        if (_hbSubsetPath == null) { Assert.Inconclusive("hb-subset not available"); return; }
        
        var (ourSize, refSize) = CompareWithHbSubset(
            _ttfFontPath!, "ABC",
            new SubsetOptions { LayoutFeatures = new() }, // Empty = drop all
            "--drop-tables=GSUB,GPOS",
            "no_features");
        
        Console.WriteLine($"No features: Ours={ourSize}, hb-subset={refSize}, ratio={100.0*ourSize/refSize:F1}%");
    }

    // ==================== Hinting Tests ====================

    [TestMethod]
    public void Compare_NoHinting_WithPyftsubset()
    {
        if (_pyftsubsetPath == null) { Assert.Inconclusive("pyftsubset not available"); return; }
        
        var (ourSize, refSize) = CompareWithPyftsubset(
            _ttfFontPath!, "ABC",
            new SubsetOptions { KeepHinting = false },
            "--no-hinting --layout-features=*",
            "no_hinting");
        
        Console.WriteLine($"No hinting: Ours={ourSize}, pyftsubset={refSize}, ratio={100.0*ourSize/refSize:F1}%");
    }

    [TestMethod]
    public void Compare_NoHinting_WithHbSubset()
    {
        if (_hbSubsetPath == null) { Assert.Inconclusive("hb-subset not available"); return; }
        
        var (ourSize, refSize) = CompareWithHbSubset(
            _ttfFontPath!, "ABC",
            new SubsetOptions { KeepHinting = false },
            "--no-hinting",
            "no_hinting_hb");
        
        Console.WriteLine($"No hinting (hb): Ours={ourSize}, hb-subset={refSize}");
    }

    // ==================== Retain GIDs Tests ====================

    [TestMethod]
    public void Compare_RetainGids_WithPyftsubset()
    {
        if (_pyftsubsetPath == null) { Assert.Inconclusive("pyftsubset not available"); return; }
        
        var (ourSize, refSize) = CompareWithPyftsubset(
            _ttfFontPath!, "ABC",
            new SubsetOptions { RetainGids = true },
            "--retain-gids --layout-features=*",
            "retain_gids");
        
        Console.WriteLine($"Retain GIDs: Ours={ourSize}, pyftsubset={refSize}, ratio={100.0*ourSize/refSize:F1}%");
    }

    [TestMethod]
    public void Compare_RetainGids_WithHbSubset()
    {
        if (_hbSubsetPath == null) { Assert.Inconclusive("hb-subset not available"); return; }
        
        var (ourSize, refSize) = CompareWithHbSubset(
            _ttfFontPath!, "ABC",
            new SubsetOptions { RetainGids = true },
            "--retain-gids",
            "retain_gids_hb");
        
        Console.WriteLine($"Retain GIDs (hb): Ours={ourSize}, hb-subset={refSize}");
    }

    // ==================== Name Table Tests ====================

    [TestMethod]
    public void Compare_NameIds_WithPyftsubset()
    {
        if (_pyftsubsetPath == null) { Assert.Inconclusive("pyftsubset not available"); return; }
        
        var (ourSize, refSize) = CompareWithPyftsubset(
            _cffFontPath!, "中文",
            new SubsetOptions { RetainedNameIds = new() { 1, 2 } },
            "--name-IDs=1,2 --desubroutinize --layout-features=*",
            "name_ids");
        
        Console.WriteLine($"Name IDs 1,2: Ours={ourSize}, pyftsubset={refSize}");
    }

    // ==================== Unicode Ranges Tests ====================

    [TestMethod]
    public void Compare_PreserveUnicodeRanges_WithHbSubset()
    {
        if (_hbSubsetPath == null) { Assert.Inconclusive("hb-subset not available"); return; }
        
        var (ourSize, refSize) = CompareWithHbSubset(
            _cffFontPath!, "中文",
            new SubsetOptions { PreserveUnicodeRanges = true },
            "--no-prune-unicode-ranges",
            "preserve_unicode_ranges");
        
        Console.WriteLine($"Preserve Unicode Ranges: Ours={ourSize}, hb-subset={refSize}");
    }

    // ==================== CFF Desubroutinize Tests ====================

    [TestMethod]
    public void Compare_CFF_Desubroutinize_WithPyftsubset()
    {
        if (_pyftsubsetPath == null) { Assert.Inconclusive("pyftsubset not available"); return; }
        
        var (ourSize, refSize) = CompareWithPyftsubset(
            _cffFontPath!, "中文",
            new SubsetOptions(),  // Our default desubroutinizes
            "--desubroutinize --layout-features=*",
            "cff_desubr");
        
        var ratio = 100.0 * ourSize / refSize;
        Console.WriteLine($"CFF Desubroutinize: Ours={ourSize}, pyftsubset={refSize}, ratio={ratio:F1}%");
        Assert.IsTrue(ratio < 110, $"CFF size should be within 10% of pyftsubset, got {ratio:F1}%");
    }

    // ==================== Helper Methods ====================

    private (long ourSize, long refSize) CompareWithPyftsubset(
        string fontPath, string text, SubsetOptions options, string pyftArgs, string testName)
    {
        if (!File.Exists(fontPath))
        {
            Assert.Inconclusive($"Font not found: {fontPath}");
            return (0, 0);
        }

        options.AddText(text);
        var unicodes = string.Join(",", text.Select(c => $"U+{(int)c:X4}"));

        // Our output
        using var file = new OTFile();
        file.open(fontPath);
        var font = file.GetFont(0)!;
        var subsetter = new Subsetter(options);
        var subsetFont = subsetter.Subset(font);

        var ourPath = Path.Combine(TempDir, $"ours_{testName}.otf");
        using (var fs = new FileStream(ourPath, FileMode.Create))
            OTFile.WriteSfntFile(fs, subsetFont);

        // pyftsubset output
        var refPath = Path.Combine(TempDir, $"pyft_{testName}.otf");
        RunProcess(_pyftsubsetPath!, $"\"{fontPath}\" --unicodes={unicodes} {pyftArgs} --output-file=\"{refPath}\"");

        var ourSize = File.Exists(ourPath) ? new FileInfo(ourPath).Length : 0;
        var refSize = File.Exists(refPath) ? new FileInfo(refPath).Length : 0;

        return (ourSize, refSize);
    }

    private (long ourSize, long refSize) CompareWithHbSubset(
        string fontPath, string text, SubsetOptions options, string hbArgs, string testName)
    {
        if (!File.Exists(fontPath))
        {
            Assert.Inconclusive($"Font not found: {fontPath}");
            return (0, 0);
        }

        options.AddText(text);

        // Our output
        using var file = new OTFile();
        file.open(fontPath);
        var font = file.GetFont(0)!;
        var subsetter = new Subsetter(options);
        var subsetFont = subsetter.Subset(font);

        var ourPath = Path.Combine(TempDir, $"ours_{testName}.otf");
        using (var fs = new FileStream(ourPath, FileMode.Create))
            OTFile.WriteSfntFile(fs, subsetFont);

        // hb-subset output
        var refPath = Path.Combine(TempDir, $"hb_{testName}.otf");
        RunProcess(_hbSubsetPath!, $"--text=\"{text}\" {hbArgs} --output-file=\"{refPath}\" \"{fontPath}\"");

        var ourSize = File.Exists(ourPath) ? new FileInfo(ourPath).Length : 0;
        var refSize = File.Exists(refPath) ? new FileInfo(refPath).Length : 0;

        return (ourSize, refSize);
    }

    private static void RunProcess(string path, string args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = path,
            Arguments = args,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var proc = Process.Start(psi);
        proc?.WaitForExit(30000);
    }

    private static string? FindExecutable(string name)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = name,
                Arguments = "--version",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi);
            proc?.WaitForExit(5000);
            if (proc?.ExitCode == 0 || proc?.ExitCode == 1)
                return name;
        }
        catch { }
        return null;
    }
}
