using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;
using OTFontFile;

namespace OTFontFile.Performance.Tests.UnitTests
{
    [TestClass]
    public class RoundTripTests
    {
        private static readonly string TestFontsDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Fonts));
        
        private static readonly string TempDir = Path.Combine(Path.GetTempPath(), "OTFontFile_RoundTrip_Tests");

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

        [TestMethod]
        public void RoundTrip_Arial_PreservesTableChecksums()
        {
            var fontPath = Path.Combine(TestFontsDir, "arial.ttf");
            if (!File.Exists(fontPath))
            {
                Assert.Inconclusive($"Test font not found: {fontPath}");
                return;
            }

            RoundTripVerify(fontPath);
        }

        [TestMethod]
        public void RoundTrip_Times_PreservesTableChecksums()
        {
            var fontPath = Path.Combine(TestFontsDir, "times.ttf");
            if (!File.Exists(fontPath))
            {
                Assert.Inconclusive($"Test font not found: {fontPath}");
                return;
            }

            RoundTripVerify(fontPath);
        }

        [TestMethod]
        public void RoundTrip_Consola_PreservesTableChecksums()
        {
            var fontPath = Path.Combine(TestFontsDir, "consola.ttf");
            if (!File.Exists(fontPath))
            {
                Assert.Inconclusive($"Test font not found: {fontPath}");
                return;
            }

            RoundTripVerify(fontPath);
        }

        [TestMethod]
        public void RoundTrip_Segoeui_PreservesTableChecksums()
        {
            var fontPath = Path.Combine(TestFontsDir, "segoeui.ttf");
            if (!File.Exists(fontPath))
            {
                Assert.Inconclusive($"Test font not found: {fontPath}");
                return;
            }

            RoundTripVerify(fontPath);
        }

        // ================== CJK Fonts ==================

        [TestMethod]
        public void RoundTrip_MsGothic_CJK_PreservesTableChecksums()
        {
            // Japanese font - larger, more complex
            var fontPath = Path.Combine(TestFontsDir, "msgothic.ttc");
            if (!File.Exists(fontPath))
            {
                Assert.Inconclusive($"Test font not found: {fontPath}");
                return;
            }

            RoundTripVerifyTTC(fontPath, 0); // MS Gothic
        }

        [TestMethod]
        public void RoundTrip_SimSun_CJK_PreservesTableChecksums()
        {
            // Chinese font
            var fontPath = Path.Combine(TestFontsDir, "simsun.ttc");
            if (!File.Exists(fontPath))
            {
                Assert.Inconclusive($"Test font not found: {fontPath}");
                return;
            }

            RoundTripVerifyTTC(fontPath, 0); // SimSun
        }

        [TestMethod]
        public void RoundTrip_MingLiU_CJK_PreservesTableChecksums()
        {
            // Traditional Chinese font
            var fontPath = Path.Combine(TestFontsDir, "mingliu.ttc");
            if (!File.Exists(fontPath))
            {
                Assert.Inconclusive($"Test font not found: {fontPath}");
                return;
            }

            RoundTripVerifyTTC(fontPath, 0);
        }

        [TestMethod]
        public void RoundTrip_YuGothic_CJK_PreservesTableChecksums()
        {
            // Modern Japanese font
            var fontPath = Path.Combine(TestFontsDir, "YuGothM.ttc");
            if (!File.Exists(fontPath))
            {
                Assert.Inconclusive($"Test font not found: {fontPath}");
                return;
            }

            RoundTripVerifyTTC(fontPath, 0);
        }

        [TestMethod]
        public void RoundTrip_MalgunGothic_Korean_PreservesTableChecksums()
        {
            // Korean font
            var fontPath = Path.Combine(TestFontsDir, "malgun.ttf");
            if (!File.Exists(fontPath))
            {
                Assert.Inconclusive($"Test font not found: {fontPath}");
                return;
            }

            RoundTripVerify(fontPath);
        }

        // ================== TTC Collection Tests ==================

        [TestMethod]
        public void RoundTrip_TTC_AllFontsInCollection()
        {
            var fontPath = Path.Combine(TestFontsDir, "msgothic.ttc");
            if (!File.Exists(fontPath))
            {
                Assert.Inconclusive($"Test font not found: {fontPath}");
                return;
            }

            using var original = new OTFile();
            if (!original.open(fontPath))
            {
                Assert.Fail($"Failed to open TTC: {fontPath}");
                return;
            }

            var numFonts = original.GetNumFonts();
            Console.WriteLine($"TTC contains {numFonts} fonts");

            // Test each font in the collection
            for (uint i = 0; i < numFonts; i++)
            {
                Console.WriteLine($"\n--- Testing font index {i} ---");
                RoundTripVerifyTTC(fontPath, i);
            }
        }

        [TestMethod]
        public void RoundTrip_TTC_WriteTTCFile()
        {
            var fontPath = Path.Combine(TestFontsDir, "msgothic.ttc");
            if (!File.Exists(fontPath))
            {
                Assert.Inconclusive($"Test font not found: {fontPath}");
                return;
            }

            var outputPath = Path.Combine(TempDir, "roundtrip_collection.ttc");

            using var original = new OTFile();
            if (!original.open(fontPath))
            {
                Assert.Fail($"Failed to open TTC: {fontPath}");
                return;
            }

            var numFonts = original.GetNumFonts();
            Console.WriteLine($"Original TTC contains {numFonts} fonts");

            // Collect all fonts
            var fonts = new OTFont[numFonts];
            for (uint i = 0; i < numFonts; i++)
            {
                fonts[i] = original.GetFont(i)!;
                Console.WriteLine($"  Font {i}: {fonts[i].GetNumTables()} tables");
            }

            // Write TTC
            using (var fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write))
            {
                bool success = OTFile.WriteTTCFile(fs, fonts);
                Assert.IsTrue(success, "WriteTTCFile failed");
            }

            // Read back and verify
            using var rebuilt = new OTFile();
            if (!rebuilt.open(outputPath))
            {
                Assert.Fail($"Failed to open rebuilt TTC: {outputPath}");
                return;
            }

            var rebuiltNumFonts = rebuilt.GetNumFonts();
            Assert.AreEqual(numFonts, rebuiltNumFonts, "Font count mismatch in TTC");

            Console.WriteLine($"\nRebuilt TTC contains {rebuiltNumFonts} fonts");

            // Verify each font
            for (uint i = 0; i < numFonts; i++)
            {
                var origFont = fonts[i];
                var rebuiltFont = rebuilt.GetFont(i)!;

                Assert.AreEqual(origFont.GetNumTables(), rebuiltFont.GetNumTables(),
                    $"Table count mismatch for font {i}");

                Console.WriteLine($"  Font {i}: {rebuiltFont.GetNumTables()} tables - OK");
            }

            var originalSize = new FileInfo(fontPath).Length;
            var rebuiltSize = new FileInfo(outputPath).Length;
            var sizeRatio = (double)rebuiltSize / originalSize;
            Console.WriteLine($"\nFile sizes: Original={originalSize}, Rebuilt={rebuiltSize}, Ratio={sizeRatio:F3}");
        }

        // ================== Helper Methods ==================

        private void RoundTripVerifyTTC(string fontPath, uint fontIndex)
        {
            var fontName = Path.GetFileName(fontPath);
            var outputPath = Path.Combine(TempDir, $"roundtrip_{fontName}_{fontIndex}.ttf");

            Console.WriteLine($"Testing round-trip for: {fontName} (index {fontIndex})");

            // Read original font
            using var original = new OTFile();
            if (!original.open(fontPath))
            {
                Assert.Fail($"Failed to open font: {fontPath}");
                return;
            }

            var originalFont = original.GetFont(fontIndex);
            if (originalFont == null)
            {
                Assert.Fail($"Failed to get font index {fontIndex} from file");
                return;
            }

            // Collect original table checksums
            var numTables = originalFont.GetNumTables();
            var originalChecksums = new (string tag, uint checksum, uint length)[numTables];
            
            Console.WriteLine($"Original font has {numTables} tables:");
            for (ushort i = 0; i < numTables; i++)
            {
                var table = originalFont.GetTable(i);
                if (table != null)
                {
                    var tag = table.GetTag().ToString();
                    var checksum = table.CalcChecksum();
                    var length = table.GetLength();
                    originalChecksums[i] = (tag, checksum, length);
                    Console.WriteLine($"  [{i:D2}] {tag}: checksum=0x{checksum:X8}, length={length}");
                }
            }

            // Write to new file (single font from TTC)
            using (var fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write))
            {
                bool writeSuccess = OTFile.WriteSfntFile(fs, originalFont);
                Assert.IsTrue(writeSuccess, "WriteSfntFile failed");
            }

            // Read back the written file
            using var rebuilt = new OTFile();
            if (!rebuilt.open(outputPath))
            {
                Assert.Fail($"Failed to open rebuilt font: {outputPath}");
                return;
            }

            var rebuiltFont = rebuilt.GetFont(0);
            if (rebuiltFont == null)
            {
                Assert.Fail("Failed to get font from rebuilt file");
                return;
            }

            // Compare table checksums
            var rebuiltNumTables = rebuiltFont.GetNumTables();
            Console.WriteLine($"\nRebuilt font has {rebuiltNumTables} tables:");

            int matchedTables = 0;
            int checksumMismatches = 0;

            foreach (var (tag, origChecksum, origLength) in originalChecksums)
            {
                var rebuiltTable = rebuiltFont.GetTable(tag);
                if (rebuiltTable == null)
                {
                    Console.WriteLine($"  WARNING: Table '{tag}' missing in rebuilt font");
                    continue;
                }

                var rebuiltChecksum = rebuiltTable.CalcChecksum();
                var rebuiltLength = rebuiltTable.GetLength();

                bool isHead = tag == "head";
                
                if (isHead)
                {
                    Console.WriteLine($"  {tag}: checksum changed (expected for head table)");
                    matchedTables++;
                }
                else if (origChecksum == rebuiltChecksum && origLength == rebuiltLength)
                {
                    Console.WriteLine($"  {tag}: OK (checksum=0x{origChecksum:X8})");
                    matchedTables++;
                }
                else
                {
                    Console.WriteLine($"  {tag}: MISMATCH!");
                    Console.WriteLine($"    Original:  checksum=0x{origChecksum:X8}, length={origLength}");
                    Console.WriteLine($"    Rebuilt:   checksum=0x{rebuiltChecksum:X8}, length={rebuiltLength}");
                    checksumMismatches++;
                }
            }

            Console.WriteLine($"\nSummary: {matchedTables} tables matched, {checksumMismatches} mismatches");
            
            Assert.AreEqual(0, checksumMismatches, 
                $"Found {checksumMismatches} table checksum mismatches in round-trip");
        }

        private void RoundTripVerify(string fontPath)
        {
            var fontName = Path.GetFileName(fontPath);
            var outputPath = Path.Combine(TempDir, $"roundtrip_{fontName}");

            Console.WriteLine($"Testing round-trip for: {fontName}");

            // Read original font
            using var original = new OTFile();
            if (!original.open(fontPath))
            {
                Assert.Fail($"Failed to open font: {fontPath}");
                return;
            }

            var originalFont = original.GetFont(0);
            if (originalFont == null)
            {
                Assert.Fail("Failed to get font from file");
                return;
            }

            // Collect original table checksums
            var numTables = originalFont.GetNumTables();
            var originalChecksums = new (string tag, uint checksum, uint length)[numTables];
            
            Console.WriteLine($"Original font has {numTables} tables:");
            for (ushort i = 0; i < numTables; i++)
            {
                var table = originalFont.GetTable(i);
                if (table != null)
                {
                    var tag = table.GetTag().ToString();
                    var checksum = table.CalcChecksum();
                    var length = table.GetLength();
                    originalChecksums[i] = (tag, checksum, length);
                    Console.WriteLine($"  [{i:D2}] {tag}: checksum=0x{checksum:X8}, length={length}");
                }
            }

            // Write to new file
            using (var fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write))
            {
                bool writeSuccess = OTFile.WriteSfntFile(fs, originalFont);
                Assert.IsTrue(writeSuccess, "WriteSfntFile failed");
            }

            // Read back the written file
            using var rebuilt = new OTFile();
            if (!rebuilt.open(outputPath))
            {
                Assert.Fail($"Failed to open rebuilt font: {outputPath}");
                return;
            }

            var rebuiltFont = rebuilt.GetFont(0);
            if (rebuiltFont == null)
            {
                Assert.Fail("Failed to get font from rebuilt file");
                return;
            }

            // Compare table checksums
            var rebuiltNumTables = rebuiltFont.GetNumTables();
            Console.WriteLine($"\nRebuilt font has {rebuiltNumTables} tables:");

            // Note: Table count might differ due to modified timestamp changing head checksum
            // We compare by tag name instead of index

            int matchedTables = 0;
            int checksumMismatches = 0;

            foreach (var (tag, origChecksum, origLength) in originalChecksums)
            {
                var rebuiltTable = rebuiltFont.GetTable(tag);
                if (rebuiltTable == null)
                {
                    Console.WriteLine($"  WARNING: Table '{tag}' missing in rebuilt font");
                    continue;
                }

                var rebuiltChecksum = rebuiltTable.CalcChecksum();
                var rebuiltLength = rebuiltTable.GetLength();

                // head table will have different checksum due to modified timestamp
                // checksumAdjustment field also changes
                bool isHead = tag == "head";
                
                if (isHead)
                {
                    Console.WriteLine($"  {tag}: checksum changed (expected for head table)");
                    Console.WriteLine($"    Original:  0x{origChecksum:X8}, length={origLength}");
                    Console.WriteLine($"    Rebuilt:   0x{rebuiltChecksum:X8}, length={rebuiltLength}");
                    matchedTables++;
                }
                else if (origChecksum == rebuiltChecksum && origLength == rebuiltLength)
                {
                    Console.WriteLine($"  {tag}: OK (checksum=0x{origChecksum:X8})");
                    matchedTables++;
                }
                else
                {
                    Console.WriteLine($"  {tag}: MISMATCH!");
                    Console.WriteLine($"    Original:  checksum=0x{origChecksum:X8}, length={origLength}");
                    Console.WriteLine($"    Rebuilt:   checksum=0x{rebuiltChecksum:X8}, length={rebuiltLength}");
                    checksumMismatches++;
                }
            }

            Console.WriteLine($"\nSummary: {matchedTables} tables matched, {checksumMismatches} mismatches");
            
            // Allow head table to differ (timestamp/checksumAdjustment changes)
            // All other tables should match
            Assert.AreEqual(0, checksumMismatches, 
                $"Found {checksumMismatches} table checksum mismatches in round-trip");

            // Verify file size is reasonable
            var originalSize = new FileInfo(fontPath).Length;
            var rebuiltSize = new FileInfo(outputPath).Length;
            var sizeRatio = (double)rebuiltSize / originalSize;
            
            Console.WriteLine($"\nFile sizes: Original={originalSize}, Rebuilt={rebuiltSize}, Ratio={sizeRatio:F3}");
            
            // Size should be very close (within 1% for most cases, but allow up to 5% for edge cases)
            Assert.IsTrue(sizeRatio >= 0.95 && sizeRatio <= 1.05,
                $"Rebuilt file size ratio {sizeRatio:F3} is outside expected range [0.95, 1.05]");
        }
    }
}
