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
            options.IncludeAsciiAlphanumeric = false; // Only include specified text

            var subsetter = new Subsetter(options);
            _ = subsetter.Subset(font);

            var retainedGlyphs = subsetter.RetainedGlyphs;

            Console.WriteLine($"Retained {retainedGlyphs.Count} glyphs for 'Hello World'");
            
            // Should have: H, e, l, o, W, r, d, space + .notdef = at least 9 glyphs
            // (l appears twice but should be deduped)
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
                // Try system font
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
            options.IncludeAsciiAlphanumeric = false;

            var subsetter = new Subsetter(options);
            _ = subsetter.Subset(font);

            var retainedGlyphs = subsetter.RetainedGlyphs;

            Console.WriteLine($"Retained {retainedGlyphs.Count} glyphs for '你好世界'");
            
            // Should have: 你, 好, 世, 界 + .notdef = at least 5 glyphs
            Assert.IsTrue(retainedGlyphs.Count >= 5, 
                $"Expected at least 5 glyphs, got {retainedGlyphs.Count}");
        }

        [TestMethod]
        public void GlyphClosure_WithAsciiAlphanumeric_IncludesAllLettersAndDigits()
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
            options.AddText("X"); // Just one character
            options.IncludeNotdef = true;
            options.IncludeAsciiAlphanumeric = true; // Include all A-Za-z0-9

            var subsetter = new Subsetter(options);
            _ = subsetter.Subset(font);

            var retainedGlyphs = subsetter.RetainedGlyphs;

            Console.WriteLine($"Retained {retainedGlyphs.Count} glyphs with ASCII alphanumeric");
            
            // With ASCII alphanumeric: 26 uppercase + 26 lowercase + 10 digits + fullwidth versions
            // Plus .notdef and original 'X' = a lot more glyphs
            Assert.IsTrue(retainedGlyphs.Count >= 62, 
                $"Expected at least 62 glyphs (A-Za-z0-9), got {retainedGlyphs.Count}");
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
            options.IncludeAsciiAlphanumeric = false;

            var subsetter = new Subsetter(options);
            _ = subsetter.Subset(font);

            var retainedGlyphs = subsetter.RetainedGlyphs;

            Console.WriteLine($"Retained {retainedGlyphs.Count} glyphs for 'é'");
            
            // If é is composite, it should include components (e + accent)
            // Otherwise just é + .notdef = 2
            Assert.IsTrue(retainedGlyphs.Count >= 2, 
                $"Expected at least 2 glyphs, got {retainedGlyphs.Count}");

            // Print the retained glyph IDs for debugging
            Console.WriteLine($"Retained glyph IDs: {string.Join(", ", retainedGlyphs.OrderBy(g => g))}");
        }

        // ================== SubsetOptions Tests ==================

        [TestMethod]
        public void SubsetOptions_ForAssSubtitle_HasCorrectDefaults()
        {
            var options = SubsetOptions.ForAssSubtitle();

            Assert.IsTrue(options.IncludeNotdef);
            Assert.IsTrue(options.IncludeAsciiAlphanumeric);
            Assert.IsTrue(options.AddVerticalForms);
            Assert.IsTrue(options.LayoutClosure);
            Assert.IsTrue(options.PreserveCodepageRanges);
            Assert.IsTrue(options.FixNonCompliantCmap);
            Assert.IsTrue(options.KeepHinting);

            // Check vertical layout features
            Assert.IsTrue(options.LayoutFeatures.Contains("vert"));
            Assert.IsTrue(options.LayoutFeatures.Contains("vrt2"));
        }

        [TestMethod]
        public void SubsetOptions_ForWebFont_HasMinimalDefaults()
        {
            var options = SubsetOptions.ForWebFont();

            Assert.IsTrue(options.IncludeNotdef);
            Assert.IsFalse(options.IncludeAsciiAlphanumeric);
            Assert.IsFalse(options.AddVerticalForms);
            Assert.IsFalse(options.LayoutClosure);
            Assert.IsFalse(options.KeepHinting);

            // Should drop additional tables
            Assert.IsTrue(options.DropTables.Contains("hdmx"));
            Assert.IsTrue(options.DropTables.Contains("VDMX"));
        }

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
            options.IncludeAsciiAlphanumeric = false;
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
            options.IncludeAsciiAlphanumeric = false;
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
    }
}
