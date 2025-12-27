using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;
using System.Linq;
using OTFontFile;
using OTFontFile.Subsetting;

namespace OTFontFile.Performance.Tests.UnitTests
{
    [TestClass]
    public class SubsettingTests
    {
        private static readonly string TestFontsDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Fonts));

        private static readonly string SampleFontsDir = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "TestResources", "SampleFonts");

        private static readonly string TempDir = Path.Combine(Path.GetTempPath(), "OTFontFile_Subsetting_Tests");

        [ClassInitialize]
        public static void ClassInit(TestContext context)
        {
            if (Directory.Exists(TempDir))
                Directory.Delete(TempDir, true);
            Directory.CreateDirectory(TempDir);
        }

        [ClassCleanup]
        public static void ClassCleanup()
        {
            if (Directory.Exists(TempDir))
                Directory.Delete(TempDir, true);
        }

        // ================== Glyph Closure Tests ==================

        [TestMethod]
        public void GlyphClosure_SimpleText_MapsToGlyphs()
        {
            var fontPath = Path.Combine(TestFontsDir, "arial.ttf");
            if (!File.Exists(fontPath))
            {
                Assert.Inconclusive($"Test font not found: {fontPath}");
                return;
            }

            using var file = new OTFile();
            file.open(fontPath);
            var font = file.GetFont(0)!;

            var options = new SubsetOptions();
            options.AddText("Hello World");
            options.IncludeNotdef = true;

            var subsetter = new Subsetter(options);
            _ = subsetter.Subset(font);

            var retainedGlyphs = subsetter.RetainedGlyphs;

            Console.WriteLine($"Retained {retainedGlyphs.Count} glyphs for 'Hello World'");
            
            // Should have: H, e, l, o, W, r, d, space + .notdef = at least 9 glyphs
            Assert.IsTrue(retainedGlyphs.Count >= 9, 
                $"Expected at least 9 glyphs, got {retainedGlyphs.Count}");

            // .notdef (glyph 0) should be included
            Assert.IsTrue(retainedGlyphs.Contains(0), ".notdef glyph should be included");
        }

        [TestMethod]
        public void GlyphClosure_ChineseText_MapsToGlyphs()
        {
            var fontPath = Path.Combine(SampleFontsDir, "SourceHanSansCN-Regular.otf");
            if (!File.Exists(fontPath))
            {
                fontPath = Path.Combine(TestFontsDir, "msyh.ttc");
                if (!File.Exists(fontPath))
                {
                    Assert.Inconclusive("No CJK font found for testing");
                    return;
                }
            }

            using var file = new OTFile();
            file.open(fontPath);
            var font = file.GetFont(0)!;

            var options = new SubsetOptions();
            options.AddText("你好世界");
            options.IncludeNotdef = true;

            var subsetter = new Subsetter(options);
            _ = subsetter.Subset(font);

            var retainedGlyphs = subsetter.RetainedGlyphs;

            Console.WriteLine($"Retained {retainedGlyphs.Count} glyphs for '你好世界'");
            
            // Should have: 你, 好, 世, 界 + .notdef = at least 5 glyphs
            Assert.IsTrue(retainedGlyphs.Count >= 5, 
                $"Expected at least 5 glyphs, got {retainedGlyphs.Count}");
        }

        [TestMethod]
        public void GlyphClosure_CompositeGlyph_IncludesComponents()
        {
            var fontPath = Path.Combine(TestFontsDir, "arial.ttf");
            if (!File.Exists(fontPath))
            {
                Assert.Inconclusive($"Test font not found: {fontPath}");
                return;
            }

            using var file = new OTFile();
            file.open(fontPath);
            var font = file.GetFont(0)!;

            // Accented characters like 'é' are often composite glyphs
            var options = new SubsetOptions();
            options.Unicodes.Add(0x00E9); // é (e with acute accent)
            options.IncludeNotdef = true;

            var subsetter = new Subsetter(options);
            _ = subsetter.Subset(font);

            var retainedGlyphs = subsetter.RetainedGlyphs;

            Console.WriteLine($"Retained {retainedGlyphs.Count} glyphs for 'é'");
            Console.WriteLine($"Retained glyph IDs: {string.Join(", ", retainedGlyphs.OrderBy(g => g))}");
            
            // If é is composite, it should include components (e + accent)
            Assert.IsTrue(retainedGlyphs.Count >= 2, 
                $"Expected at least 2 glyphs, got {retainedGlyphs.Count}");
        }

        // ================== SubsetOptions Tests ==================

        [TestMethod]
        public void SubsetOptions_AddText_AddsAllCodepoints()
        {
            var options = new SubsetOptions();
            options.AddText("Hello 你好 🎉");

            // Should include ASCII, Chinese, and emoji
            Assert.IsTrue(options.Unicodes.Contains((int)'H'));
            Assert.IsTrue(options.Unicodes.Contains((int)'你'));
            Assert.IsTrue(options.Unicodes.Contains(0x1F389)); // 🎉 party popper
        }

        [TestMethod]
        public void SubsetOptions_AddRange_AddsAllInRange()
        {
            var options = new SubsetOptions();
            options.AddRange(0x0041, 0x005A); // A-Z

            Assert.AreEqual(26, options.Unicodes.Count);
            Assert.IsTrue(options.Unicodes.Contains((int)'A'));
            Assert.IsTrue(options.Unicodes.Contains((int)'Z'));
        }

        [TestMethod]
        public void SubsetOptions_AddGlyphIds_AddsDirectly()
        {
            var options = new SubsetOptions();
            options.AddGlyphIds(new[] { 1, 2, 3, 100, 200 });

            Assert.AreEqual(5, options.GlyphIds.Count);
            Assert.IsTrue(options.GlyphIds.Contains(100));
        }

        // ================== GlyphIdMap Tests ==================

        [TestMethod]
        public void GlyphIdMap_Compact_StartsFromZero()
        {
            var fontPath = Path.Combine(TestFontsDir, "arial.ttf");
            if (!File.Exists(fontPath))
            {
                Assert.Inconclusive($"Test font not found: {fontPath}");
                return;
            }

            using var file = new OTFile();
            file.open(fontPath);
            var font = file.GetFont(0)!;

            var options = new SubsetOptions();
            options.AddText("ABC");
            options.IncludeNotdef = true;
            options.RetainGids = false; // Compact mode

            var subsetter = new Subsetter(options);
            _ = subsetter.Subset(font);

            var glyphIdMap = subsetter.GlyphIdMap;

            Console.WriteLine($"Glyph ID mapping (old → new):");
            foreach (var kv in glyphIdMap.OrderBy(x => x.Value))
            {
                Console.WriteLine($"  {kv.Key} → {kv.Value}");
            }

            // In compact mode, new IDs should be consecutive starting from 0
            var newIds = glyphIdMap.Values.OrderBy(v => v).ToList();
            for (int i = 0; i < newIds.Count; i++)
            {
                Assert.AreEqual(i, newIds[i], 
                    $"Expected new glyph ID {i}, got {newIds[i]}");
            }
        }

        [TestMethod]
        public void GlyphIdMap_RetainGids_KeepsOriginalIds()
        {
            var fontPath = Path.Combine(TestFontsDir, "arial.ttf");
            if (!File.Exists(fontPath))
            {
                Assert.Inconclusive($"Test font not found: {fontPath}");
                return;
            }

            using var file = new OTFile();
            file.open(fontPath);
            var font = file.GetFont(0)!;

            var options = new SubsetOptions();
            options.AddText("ABC");
            options.IncludeNotdef = true;
            options.RetainGids = true; // Keep original IDs

            var subsetter = new Subsetter(options);
            _ = subsetter.Subset(font);

            var glyphIdMap = subsetter.GlyphIdMap;

            // In retain mode, old ID should equal new ID
            foreach (var kv in glyphIdMap)
            {
                Assert.AreEqual(kv.Key, kv.Value, 
                    $"Expected glyph ID to be retained: {kv.Key}");
            }
        }

        // ================== End-to-End Subsetting Tests ==================

        [TestMethod]
        public void Subset_SimpleText_ProducesValidFont()
        {
            var fontPath = Path.Combine(TestFontsDir, "arial.ttf");
            if (!File.Exists(fontPath))
            {
                Assert.Inconclusive($"Test font not found: {fontPath}");
                return;
            }

            var outputPath = Path.Combine(TempDir, "subset_hello.ttf");

            using var file = new OTFile();
            file.open(fontPath);
            var font = file.GetFont(0)!;

            Console.WriteLine($"Original font: {font.GetMaxpNumGlyphs()} glyphs");

            var options = new SubsetOptions();
            options.AddText("Hello World");
            options.IncludeNotdef = true;

            var subsetter = new Subsetter(options);
            var subsetFont = subsetter.Subset(font);

            Console.WriteLine($"Subset font: {subsetter.RetainedGlyphs.Count} glyphs");
            Console.WriteLine($"Tables: {subsetFont.GetNumTables()}");

            // Write subset font
            using (var fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write))
            {
                bool success = OTFile.WriteSfntFile(fs, subsetFont);
                Assert.IsTrue(success, "WriteSfntFile failed");
            }

            // Read back and verify
            using var rebuilt = new OTFile();
            Assert.IsTrue(rebuilt.open(outputPath), "Failed to open subset font");

            var rebuiltFont = rebuilt.GetFont(0);
            Assert.IsNotNull(rebuiltFont, "Failed to get font from subset file");

            var maxp = rebuiltFont.GetTable("maxp") as Table_maxp;
            Assert.IsNotNull(maxp, "maxp table not found");

            Console.WriteLine($"Rebuilt font: {maxp.NumGlyphs} glyphs");
            Assert.AreEqual(subsetter.RetainedGlyphs.Count, maxp.NumGlyphs, 
                "Glyph count mismatch in subset font");

            // Verify file size is smaller
            var originalSize = new FileInfo(fontPath).Length;
            var subsetSize = new FileInfo(outputPath).Length;
            var ratio = (double)subsetSize / originalSize;

            Console.WriteLine($"File sizes: Original={originalSize}, Subset={subsetSize}, Ratio={ratio:F3}");
            Assert.IsTrue(ratio < 0.5, $"Subset should be <50% of original, got {ratio:P0}");
        }

        [TestMethod]
        public void Subset_ChineseText_ProducesValidFont()
        {
            var fontPath = Path.Combine(TestFontsDir, "msyh.ttc");
            if (!File.Exists(fontPath))
            {
                Assert.Inconclusive($"Test font not found: {fontPath}");
                return;
            }

            var outputPath = Path.Combine(TempDir, "subset_chinese.ttf");

            using var file = new OTFile();
            file.open(fontPath);
            var font = file.GetFont(0)!;

            Console.WriteLine($"Original font: {font.GetMaxpNumGlyphs()} glyphs");

            var options = new SubsetOptions();
            options.AddText("你好世界");
            options.IncludeNotdef = true;

            var subsetter = new Subsetter(options);
            var subsetFont = subsetter.Subset(font);

            Console.WriteLine($"Subset font: {subsetter.RetainedGlyphs.Count} glyphs");

            // Write subset font
            using (var fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write))
            {
                bool success = OTFile.WriteSfntFile(fs, subsetFont);
                Assert.IsTrue(success, "WriteSfntFile failed");
            }

            // Read back and verify
            using var rebuilt = new OTFile();
            Assert.IsTrue(rebuilt.open(outputPath), "Failed to open subset font");

            var rebuiltFont = rebuilt.GetFont(0);
            Assert.IsNotNull(rebuiltFont, "Failed to get font from subset file");

            var maxp = rebuiltFont.GetTable("maxp") as Table_maxp;
            Assert.IsNotNull(maxp, "maxp table not found");

            Console.WriteLine($"Rebuilt font: {maxp.NumGlyphs} glyphs");

            // Verify significant size reduction
            var originalSize = new FileInfo(fontPath).Length;
            var subsetSize = new FileInfo(outputPath).Length;
            var ratio = (double)subsetSize / originalSize;

            Console.WriteLine($"File sizes: Original={originalSize}, Subset={subsetSize}, Ratio={ratio:F3}");
            Assert.IsTrue(ratio < 0.1, $"Chinese subset should be <10% of original, got {ratio:P0}");
        }

        [TestMethod]
        public void Subset_ByText_StaticMethod()
        {
            var fontPath = Path.Combine(TestFontsDir, "arial.ttf");
            if (!File.Exists(fontPath))
            {
                Assert.Inconclusive($"Test font not found: {fontPath}");
                return;
            }

            using var file = new OTFile();
            file.open(fontPath);
            var font = file.GetFont(0)!;

            // Use static factory method
            var subsetFont = Subsetter.SubsetByText(font, "Test");

            var maxp = subsetFont.GetTable("maxp") as Table_maxp;
            Assert.IsNotNull(maxp);

            // T, e, s, t + .notdef = 5 glyphs (s appears twice but deduped)
            Console.WriteLine($"Subset has {maxp.NumGlyphs} glyphs");
            Assert.IsTrue(maxp.NumGlyphs >= 4, 
                $"Expected at least 4 glyphs, got {maxp.NumGlyphs}");
        }

        [TestMethod]
        public void Subset_ByGlyphIds_StaticMethod()
        {
            var fontPath = Path.Combine(TestFontsDir, "arial.ttf");
            if (!File.Exists(fontPath))
            {
                Assert.Inconclusive($"Test font not found: {fontPath}");
                return;
            }

            using var file = new OTFile();
            file.open(fontPath);
            var font = file.GetFont(0)!;

            // Subset by glyph IDs directly
            var subsetFont = Subsetter.SubsetByGlyphIds(font, new[] { 0, 1, 2, 3, 4 });

            var maxp = subsetFont.GetTable("maxp") as Table_maxp;
            Assert.IsNotNull(maxp);

            Console.WriteLine($"Subset has {maxp.NumGlyphs} glyphs");
            Assert.AreEqual(5, maxp.NumGlyphs);
        }

        // ================== Comprehensive Real Font Tests ==================

        [TestMethod]
        public void Subset_SourceHanSans_CFF_ChineseText()
        {
            // Test CFF/OTF font subsetting
            var fontPath = Path.Combine(SampleFontsDir, "SourceHanSansCN-Regular.otf");
            if (!File.Exists(fontPath))
            {
                Assert.Inconclusive($"Test font not found: {fontPath}");
                return;
            }

            using var file = new OTFile();
            file.open(fontPath);
            var font = file.GetFont(0)!;

            // Verify it's a CFF font
            var cffTable = font.GetTable("CFF ");
            Assert.IsNotNull(cffTable, "Font should have CFF table");

            var options = new SubsetOptions().AddText("你好世界");
            options.LayoutClosure = true;

            var subsetter = new Subsetter(options);
            var subsetFont = subsetter.Subset(font);

            // Verify subset has CFF table
            var subsetCFF = subsetFont.GetTable("CFF ");
            Assert.IsNotNull(subsetCFF, "Subset font should have CFF table");

            var maxp = subsetFont.GetTable("maxp") as Table_maxp;
            Assert.IsNotNull(maxp);
            Console.WriteLine($"SourceHanSans CFF subset: {maxp.NumGlyphs} glyphs");
            Assert.IsTrue(maxp.NumGlyphs >= 4, $"Expected at least 4 glyphs, got {maxp.NumGlyphs}");

            // Save and verify size reduction
            var outputPath = Path.Combine(TempDir, "SourceHanSans_CFF_subset.otf");
            using (var fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write))
            {
                OTFile.WriteSfntFile(fs, subsetFont);
            }

            var originalSize = new FileInfo(fontPath).Length;
            var subsetSize = new FileInfo(outputPath).Length;
            var ratio = (double)subsetSize / originalSize;
            Console.WriteLine($"CFF Size: {originalSize:N0} -> {subsetSize:N0} ({ratio:P1})");
            
            // CFF subsetting should reduce size significantly
            Assert.IsTrue(ratio < 0.1, $"Expected <10% size, got {ratio:P1}");
        }

        [TestMethod]
        public void Subset_STSONG_ChineseText()
        {
            // Using TTF font for TrueType subsetting
            var fontPath = Path.Combine(SampleFontsDir, "STSONG.TTF");
            if (!File.Exists(fontPath))
            {
                Assert.Inconclusive($"Test font not found: {fontPath}");
                return;
            }

            using var file = new OTFile();
            file.open(fontPath);
            var font = file.GetFont(0)!;

            var options = new SubsetOptions().AddText("你好世界，这是一个测试");
            options.LayoutClosure = true;

            var subsetter = new Subsetter(options);
            var subsetFont = subsetter.Subset(font);

            var maxp = subsetFont.GetTable("maxp") as Table_maxp;
            Assert.IsNotNull(maxp);
            
            Console.WriteLine($"STSONG subset: {maxp.NumGlyphs} glyphs retained");
            Assert.IsTrue(maxp.NumGlyphs >= 10, $"Expected at least 10 glyphs, got {maxp.NumGlyphs}");

            // Save and verify file can be reopened
            var outputPath = Path.Combine(TempDir, "STSONG_subset.ttf");
            using (var fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write))
            {
                OTFile.WriteSfntFile(fs, subsetFont);
            }

            // Verify size reduction (TTF subsetting works, size should be much smaller)
            var originalSize = new FileInfo(fontPath).Length;
            var subsetSize = new FileInfo(outputPath).Length;
            var ratio = (double)subsetSize / originalSize;
            Console.WriteLine($"Size reduction: {originalSize:N0} -> {subsetSize:N0} ({ratio:P1})");
            Assert.IsTrue(ratio < 0.1, $"Expected <10% size, got {ratio:P1}");
        }

        [TestMethod]
        public void Subset_Medium_LatinTTF()
        {
            // Using TTF font to verify TrueType subsetting
            var fontPath = Path.Combine(SampleFontsDir, "medium.ttf");
            if (!File.Exists(fontPath))
            {
                Assert.Inconclusive($"Test font not found: {fontPath}");
                return;
            }

            using var file = new OTFile();
            file.open(fontPath);
            var font = file.GetFont(0)!;

            var options = new SubsetOptions().AddText("The quick brown fox jumps over the lazy dog 0123456789");
            options.NewFontNameSuffix = "_subset";

            var subsetter = new Subsetter(options);
            var subsetFont = subsetter.Subset(font);

            var maxp = subsetFont.GetTable("maxp") as Table_maxp;
            Assert.IsNotNull(maxp);
            Console.WriteLine($"medium.ttf subset: {maxp.NumGlyphs} glyphs");

            // Save and reopen
            var outputPath = Path.Combine(TempDir, "medium_subset.ttf");
            using (var fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write))
            {
                OTFile.WriteSfntFile(fs, subsetFont);
            }

            using var verifyFile = new OTFile();
            verifyFile.open(outputPath);
            var verifyFont = verifyFile.GetFont(0);
            Assert.IsNotNull(verifyFont);
        }

        [TestMethod]
        public void Subset_TTC_FirstFont()
        {
            var fontPath = Path.Combine(SampleFontsDir, "msyh.ttc");
            if (!File.Exists(fontPath))
            {
                Assert.Inconclusive($"Test font not found: {fontPath}");
                return;
            }

            using var file = new OTFile();
            file.open(fontPath);
            
            Assert.IsTrue(file.GetNumFonts() > 1, "Expected TTC with multiple fonts");
            var font = file.GetFont(0)!;

            var options = new SubsetOptions().AddText("微软雅黑测试");
            var subsetter = new Subsetter(options);
            var subsetFont = subsetter.Subset(font);

            var maxp = subsetFont.GetTable("maxp") as Table_maxp;
            Assert.IsNotNull(maxp);
            Console.WriteLine($"MSYH TTC subset: {maxp.NumGlyphs} glyphs from font 0");

            // Save as regular OTF (not TTC)
            var outputPath = Path.Combine(TempDir, "msyh_subset.ttf");
            using (var fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write))
            {
                OTFile.WriteSfntFile(fs, subsetFont);
            }

            Assert.IsTrue(File.Exists(outputPath));
        }

        [TestMethod]
        public void Subset_HYQiHei_WithGSUBClosure()
        {
            var fontPath = Path.Combine(SampleFontsDir, "HYQiHei_65S.ttf");
            if (!File.Exists(fontPath))
            {
                Assert.Inconclusive($"Test font not found: {fontPath}");
                return;
            }

            using var file = new OTFile();
            file.open(fontPath);
            var font = file.GetFont(0)!;

            // Test GSUB closure with layout features
            var options = new SubsetOptions().AddText("汉仪旗黑测试");
            options.LayoutClosure = true;

            var subsetter = new Subsetter(options);
            var subsetFont = subsetter.Subset(font);

            var retainedCount = subsetter.RetainedGlyphs.Count;
            Console.WriteLine($"HYQiHei with GSUB closure: {retainedCount} glyphs retained");

            var outputPath = Path.Combine(TempDir, "HYQiHei_subset.ttf");
            using (var fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write))
            {
                OTFile.WriteSfntFile(fs, subsetFont);
            }

            // Verify reopening
            using var verifyFile = new OTFile();
            verifyFile.open(outputPath);
            Assert.IsNotNull(verifyFile.GetFont(0));
        }

        [TestMethod]
        public void Subset_Small_VerifyAllTables()
        {
            var fontPath = Path.Combine(SampleFontsDir, "small.ttf");
            if (!File.Exists(fontPath))
            {
                Assert.Inconclusive($"Test font not found: {fontPath}");
                return;
            }

            using var file = new OTFile();
            file.open(fontPath);
            var font = file.GetFont(0)!;

            var options = new SubsetOptions().AddText("ABC");

            var subsetter = new Subsetter(options);
            var subsetFont = subsetter.Subset(font);

            // Verify core tables exist
            var tablesToCheck = new[] { "glyf", "loca", "head", "maxp", "hhea", "hmtx", "cmap", "post" };
            foreach (var tag in tablesToCheck)
            {
                var table = subsetFont.GetTable(tag);
                Assert.IsNotNull(table, $"Missing table: {tag}");
                Console.WriteLine($"Table {tag}: OK");
            }

            var outputPath = Path.Combine(TempDir, "small_subset.ttf");
            using (var fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write))
            {
                OTFile.WriteSfntFile(fs, subsetFont);
            }

            Console.WriteLine($"Output file: {new FileInfo(outputPath).Length} bytes");
        }

        [TestMethod]
        public void Subset_MultipleFormats_SizeReduction()
        {
            var testCases = new[]
            {
                ("STSONG.TTF", "华文宋体测试", 0.05),
                ("HYQiHei_65S.ttf", "汉仪旗黑", 0.05),
                ("small.ttf", "ABC", 0.5),  // Small font won't reduce as much
            };

            foreach (var (fontFile, text, maxRatio) in testCases)
            {
                var fontPath = Path.Combine(SampleFontsDir, fontFile);
                if (!File.Exists(fontPath))
                {
                    Console.WriteLine($"Skipping {fontFile} - not found");
                    continue;
                }

                using var file = new OTFile();
                file.open(fontPath);
                var font = file.GetFont(0)!;

                var options = new SubsetOptions().AddText(text);
                var subsetter = new Subsetter(options);
                var subsetFont = subsetter.Subset(font);

                var outputPath = Path.Combine(TempDir, $"{Path.GetFileNameWithoutExtension(fontFile)}_multi.ttf");
                using (var fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write))
                {
                    OTFile.WriteSfntFile(fs, subsetFont);
                }

                var originalSize = new FileInfo(fontPath).Length;
                var subsetSize = new FileInfo(outputPath).Length;
                var ratio = (double)subsetSize / originalSize;

                Console.WriteLine($"{fontFile}: {originalSize:N0} -> {subsetSize:N0} ({ratio:P2})");
                Assert.IsTrue(ratio < maxRatio, $"{fontFile}: Expected <{maxRatio:P0}, got {ratio:P1}");
            }
        }
    }
}
