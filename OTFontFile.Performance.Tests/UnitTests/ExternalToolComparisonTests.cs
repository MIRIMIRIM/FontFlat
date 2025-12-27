using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using OTFontFile;
using OTFontFile.Subsetting;

namespace OTFontFile.Performance.Tests.UnitTests
{
    /// <summary>
    /// Tests that compare our subsetting implementation with fonttools (pyftsubset) and harfbuzz (hb-subset).
    /// 
    /// These tests require external tools to be installed:
    /// - pyftsubset: pip install fonttools
    /// - hb-subset: Install harfbuzz from release binaries or build from source
    /// 
    /// Set environment variables to enable:
    /// - FONTTOOLS_PATH: Path to pyftsubset executable (or just "pyftsubset" if in PATH)
    /// - HBSUBSET_PATH: Path to hb-subset executable (or just "hb-subset" if in PATH)
    /// </summary>
    [TestClass]
    public class ExternalToolComparisonTests
    {
        private static readonly string SampleFontsDir = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "TestResources", "SampleFonts");

        private static readonly string RefFonttoolsDir = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "Ref", "fonttools", "Tests", "subset", "data");

        private static readonly string TempDir = Path.Combine(Path.GetTempPath(), "OTFontFile_ExternalTool_Tests");

        private static string? _pyftsubsetPath;
        private static string? _hbSubsetPath;

        [ClassInitialize]
        public static void ClassInit(TestContext context)
        {
            if (Directory.Exists(TempDir))
                Directory.Delete(TempDir, true);
            Directory.CreateDirectory(TempDir);

            // Try to find external tools
            _pyftsubsetPath = Environment.GetEnvironmentVariable("FONTTOOLS_PATH") ?? FindExecutable("pyftsubset");
            _hbSubsetPath = Environment.GetEnvironmentVariable("HBSUBSET_PATH") ?? FindExecutable("hb-subset");

            Console.WriteLine($"pyftsubset: {_pyftsubsetPath ?? "not found"}");
            Console.WriteLine($"hb-subset: {_hbSubsetPath ?? "not found"}");
        }

        [ClassCleanup]
        public static void ClassCleanup()
        {
            if (Directory.Exists(TempDir))
                Directory.Delete(TempDir, true);
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
                if (proc?.ExitCode == 0 || proc?.ExitCode == 1) // Some tools exit with 1 for --version
                    return name;
            }
            catch { }
            return null;
        }

        // ================== pyftsubset Comparison Tests ==================

        [TestMethod]
        public void Compare_TTF_Subsetting_WithPyftsubset()
        {
            if (_pyftsubsetPath == null)
            {
                Assert.Inconclusive("pyftsubset not available. Install with: pip install fonttools");
                return;
            }

            var fontPath = Path.Combine(SampleFontsDir, "small.ttf");
            if (!File.Exists(fontPath))
            {
                Assert.Inconclusive($"Test font not found: {fontPath}");
                return;
            }

            string testText = "ABC";
            var unicodes = string.Join(",", testText.Select(c => $"U+{(int)c:X4}"));

            // Run our subsetter
            using var file = new OTFile();
            file.open(fontPath);
            var font = file.GetFont(0)!;

            var options = new SubsetOptions().AddText(testText);
            var subsetter = new Subsetter(options);
            var subsetFont = subsetter.Subset(font);

            var ourOutput = Path.Combine(TempDir, "ours_ttf.ttf");
            using (var fs = new FileStream(ourOutput, FileMode.Create, FileAccess.Write))
            {
                OTFile.WriteSfntFile(fs, subsetFont);
            }

            // Run pyftsubset
            var ftOutput = Path.Combine(TempDir, "fonttools_ttf.ttf");
            var ftResult = RunProcess(_pyftsubsetPath, 
                $"\"{fontPath}\" --unicodes={unicodes} --output-file=\"{ftOutput}\"");

            if (!File.Exists(ftOutput))
            {
                Console.WriteLine($"pyftsubset stderr: {ftResult.stderr}");
                Assert.Inconclusive("pyftsubset failed to produce output");
                return;
            }

            // Compare results
            CompareSubsetResults(ourOutput, ftOutput, "pyftsubset (TTF)");
        }

        [TestMethod]
        public void Compare_CFF_Subsetting_WithPyftsubset()
        {
            if (_pyftsubsetPath == null)
            {
                Assert.Inconclusive("pyftsubset not available");
                return;
            }

            var fontPath = Path.Combine(SampleFontsDir, "SourceHanSansCN-Regular.otf");
            if (!File.Exists(fontPath))
            {
                Assert.Inconclusive($"Test font not found: {fontPath}");
                return;
            }

            string testText = "中文";
            var unicodes = string.Join(",", testText.Select(c => $"U+{(int)c:X4}"));

            // Run our subsetter
            using var file = new OTFile();
            file.open(fontPath);
            var font = file.GetFont(0)!;

            var options = new SubsetOptions().AddText(testText);
            var subsetter = new Subsetter(options);
            var subsetFont = subsetter.Subset(font);

            var ourOutput = Path.Combine(TempDir, "ours_cff.otf");
            using (var fs = new FileStream(ourOutput, FileMode.Create, FileAccess.Write))
            {
                OTFile.WriteSfntFile(fs, subsetFont);
            }

            // Run pyftsubset with desubroutinize (matching our approach)
            var ftOutput = Path.Combine(TempDir, "fonttools_cff.otf");
            var ftResult = RunProcess(_pyftsubsetPath,
                $"\"{fontPath}\" --unicodes={unicodes} --desubroutinize --output-file=\"{ftOutput}\"");

            if (!File.Exists(ftOutput))
            {
                Console.WriteLine($"pyftsubset stderr: {ftResult.stderr}");
                Assert.Inconclusive("pyftsubset failed to produce output");
                return;
            }

            CompareSubsetResults(ourOutput, ftOutput, "pyftsubset (CFF)");
        }

        // ================== hb-subset Comparison Tests ==================

        [TestMethod]
        public void Compare_TTF_Subsetting_WithHbSubset()
        {
            if (_hbSubsetPath == null)
            {
                Assert.Inconclusive("hb-subset not available. Install harfbuzz.");
                return;
            }

            var fontPath = Path.Combine(SampleFontsDir, "small.ttf");
            if (!File.Exists(fontPath))
            {
                Assert.Inconclusive($"Test font not found: {fontPath}");
                return;
            }

            string testText = "ABC";
            var unicodes = string.Join(",", testText.Select(c => $"U+{(int)c:X4}"));

            // Run our subsetter
            using var file = new OTFile();
            file.open(fontPath);
            var font = file.GetFont(0)!;

            var options = new SubsetOptions().AddText(testText);
            var subsetter = new Subsetter(options);
            var subsetFont = subsetter.Subset(font);

            var ourOutput = Path.Combine(TempDir, "ours_hb.ttf");
            using (var fs = new FileStream(ourOutput, FileMode.Create, FileAccess.Write))
            {
                OTFile.WriteSfntFile(fs, subsetFont);
            }

            // Run hb-subset
            var hbOutput = Path.Combine(TempDir, "hb_subset.ttf");
            var hbResult = RunProcess(_hbSubsetPath,
                $"--unicodes={unicodes} --output-file=\"{hbOutput}\" \"{fontPath}\"");

            if (!File.Exists(hbOutput))
            {
                Console.WriteLine($"hb-subset stderr: {hbResult.stderr}");
                Assert.Inconclusive("hb-subset failed to produce output");
                return;
            }

            CompareSubsetResults(ourOutput, hbOutput, "hb-subset (TTF)");
        }

        [TestMethod]
        public void Compare_CFF_Subsetting_WithHbSubset()
        {
            if (_hbSubsetPath == null)
            {
                Assert.Inconclusive("hb-subset not available");
                return;
            }

            var fontPath = Path.Combine(SampleFontsDir, "SourceHanSansCN-Regular.otf");
            if (!File.Exists(fontPath))
            {
                Assert.Inconclusive($"Test font not found: {fontPath}");
                return;
            }

            string testText = "中文";
            var unicodes = string.Join(",", testText.Select(c => $"U+{(int)c:X4}"));

            // Run our subsetter
            using var file = new OTFile();
            file.open(fontPath);
            var font = file.GetFont(0)!;

            var options = new SubsetOptions().AddText(testText);
            var subsetter = new Subsetter(options);
            var subsetFont = subsetter.Subset(font);

            var ourOutput = Path.Combine(TempDir, "ours_hb_cff.otf");
            using (var fs = new FileStream(ourOutput, FileMode.Create, FileAccess.Write))
            {
                OTFile.WriteSfntFile(fs, subsetFont);
            }

            // Run hb-subset with desubroutinize
            var hbOutput = Path.Combine(TempDir, "hb_subset_cff.otf");
            var hbResult = RunProcess(_hbSubsetPath,
                $"--unicodes={unicodes} --desubroutinize --output-file=\"{hbOutput}\" \"{fontPath}\"");

            if (!File.Exists(hbOutput))
            {
                Console.WriteLine($"hb-subset stderr: {hbResult.stderr}");
                Assert.Inconclusive("hb-subset failed to produce output");
                return;
            }

            CompareSubsetResults(ourOutput, hbOutput, "hb-subset (CFF)");
        }

        // ================== Benchmark Tests ==================

        [TestMethod]
        public void Benchmark_Subsetting_Performance()
        {
            var fontPath = Path.Combine(SampleFontsDir, "STSONG.TTF");
            if (!File.Exists(fontPath))
            {
                Assert.Inconclusive($"Test font not found: {fontPath}");
                return;
            }

            string testText = "测试性能比较文字子集化速度";
            var unicodes = string.Join(",", testText.Select(c => $"U+{(int)c:X4}"));
            
            const int iterations = 5;

            // Benchmark our subsetter
            using var file = new OTFile();
            file.open(fontPath);
            var font = file.GetFont(0)!;

            var options = new SubsetOptions().AddText(testText);
            options.LayoutClosure = true;
            
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < iterations; i++)
            {
                var subsetter = new Subsetter(options);
                var subsetFont = subsetter.Subset(font);
                
                // Force output to measure full pipeline
                var tempOutput = Path.Combine(TempDir, $"benchmark_ours_{i}.ttf");
                using (var fs = new FileStream(tempOutput, FileMode.Create, FileAccess.Write))
                {
                    OTFile.WriteSfntFile(fs, subsetFont);
                }
            }
            sw.Stop();
            var ourTime = sw.ElapsedMilliseconds / (double)iterations;
            Console.WriteLine($"OTFontFile subsetter: {ourTime:F2}ms per iteration");

            // Benchmark pyftsubset if available
            if (_pyftsubsetPath != null)
            {
                var ftOutput = Path.Combine(TempDir, "benchmark_ft.ttf");
                sw.Restart();
                for (int i = 0; i < iterations; i++)
                {
                    RunProcess(_pyftsubsetPath,
                        $"\"{fontPath}\" --unicodes={unicodes} --output-file=\"{ftOutput}\"");
                }
                sw.Stop();
                var ftTime = sw.ElapsedMilliseconds / (double)iterations;
                Console.WriteLine($"pyftsubset: {ftTime:F2}ms per iteration");
                Console.WriteLine($"Speedup vs pyftsubset: {ftTime / ourTime:F1}x");
            }

            // Benchmark hb-subset if available
            if (_hbSubsetPath != null)
            {
                var hbOutput = Path.Combine(TempDir, "benchmark_hb.ttf");
                sw.Restart();
                for (int i = 0; i < iterations; i++)
                {
                    RunProcess(_hbSubsetPath,
                        $"--unicodes={unicodes} --output-file=\"{hbOutput}\" \"{fontPath}\"");
                }
                sw.Stop();
                var hbTime = sw.ElapsedMilliseconds / (double)iterations;
                Console.WriteLine($"hb-subset: {hbTime:F2}ms per iteration");
                Console.WriteLine($"Speedup vs hb-subset: {hbTime / ourTime:F1}x");
            }
        }

        // ================== Reference Test Data ==================

        [TestMethod]
        public void Test_Using_FonttoolsTestData()
        {
            // Use fonttools' test data for validation
            var lobsterTtx = Path.Combine(RefFonttoolsDir, "Lobster.subset.ttx");
            if (!File.Exists(lobsterTtx))
            {
                Assert.Inconclusive($"fonttools test data not found: {lobsterTtx}");
                return;
            }

            // We can't directly use TTX files, but we can use pre-compiled fonts
            var lobsterOtf = Path.Combine(RefFonttoolsDir, "Lobster.subset.otf");
            if (!File.Exists(lobsterOtf))
            {
                Assert.Inconclusive($"fonttools test data not found: {lobsterOtf}");
                return;
            }

            using var file = new OTFile();
            Assert.IsTrue(file.open(lobsterOtf), "Should be able to open Lobster.subset.otf");

            var font = file.GetFont(0)!;
            
            // Verify this is a valid CFF font
            var cff = font.GetTable("CFF ");
            Assert.IsNotNull(cff, "Should have CFF table");

            Console.WriteLine($"Loaded fonttools test font: Lobster.subset.otf");
            Console.WriteLine($"Tables: {font.GetNumTables()}");

            // Test our subsetter on it
            var options = new SubsetOptions().AddText("abc");
            var subsetter = new Subsetter(options);
            var subsetFont = subsetter.Subset(font);

            var maxp = subsetFont.GetTable("maxp") as Table_maxp;
            Console.WriteLine($"Subset glyph count: {maxp?.NumGlyphs}");
            Assert.IsTrue((maxp?.NumGlyphs ?? 0) > 0, "Should have glyphs after subsetting");
        }

        // ================== Helper Methods ==================

        private void CompareSubsetResults(string ourPath, string refPath, string refName)
        {
            using var ourFile = new OTFile();
            Assert.IsTrue(ourFile.open(ourPath), "Should open our subset");
            var ourFont = ourFile.GetFont(0)!;

            using var refFile = new OTFile();
            Assert.IsTrue(refFile.open(refPath), "Should open reference subset");
            var refFont = refFile.GetFont(0)!;

            var ourMaxp = ourFont.GetTable("maxp") as Table_maxp;
            var refMaxp = refFont.GetTable("maxp") as Table_maxp;

            var ourSize = new FileInfo(ourPath).Length;
            var refSize = new FileInfo(refPath).Length;

            Console.WriteLine($"=== Comparison with {refName} ===");
            Console.WriteLine($"Glyph count - Ours: {ourMaxp?.NumGlyphs}, {refName}: {refMaxp?.NumGlyphs}");
            Console.WriteLine($"File size - Ours: {ourSize:N0}, {refName}: {refSize:N0}");
            Console.WriteLine($"Size ratio: {(double)ourSize / refSize:P1}");

            // Allow some variance in glyph count (different .notdef handling, etc.)
            var glyphDiff = Math.Abs((ourMaxp?.NumGlyphs ?? 0) - (refMaxp?.NumGlyphs ?? 0));
            Assert.IsTrue(glyphDiff <= 3, 
                $"Glyph count difference too large: {glyphDiff}");

            // Our output should be reasonably sized (not massively larger)
            var sizeRatio = (double)ourSize / refSize;
            Assert.IsTrue(sizeRatio < 3.0, 
                $"Our output is too large compared to {refName}: {sizeRatio:P1}");
        }

        private static (string stdout, string stderr, int exitCode) RunProcess(string exe, string args)
        {
            var psi = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = args,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var proc = Process.Start(psi)!;
            var stdout = proc.StandardOutput.ReadToEnd();
            var stderr = proc.StandardError.ReadToEnd();
            proc.WaitForExit(60000);
            
            return (stdout, stderr, proc.ExitCode);
        }
    }
}
