using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;
using System.Diagnostics;
using OTFontFile;

namespace OTFontFile.Performance.Tests.UnitTests
{
    [TestClass]
    public class ConcurrencyTests
    {
        private string _fontPath;

        [TestInitialize]
        public void Setup()
        {
            _fontPath = Path.Combine("TestResources", "SampleFonts", "SourceHanSans.ttc");
            if (!File.Exists(_fontPath))
            {
                // Try finding it relative to bin if not execution directory
                string? baseDir = AppContext.BaseDirectory;
                string searchPath = Path.Combine(baseDir, "TestResources", "SampleFonts", "SourceHanSans.ttc");
                if (File.Exists(searchPath))
                {
                    _fontPath = searchPath;
                }
                else 
                {
                    Assert.Inconclusive($"Test font not found at {_fontPath} or {searchPath}");
                }
            }
        }

        [TestMethod]
        public void VerifyChecksums_MatchBaseline_ForSourceHanSans()
        {
            // Migrated from Program.cs "testconcurrency"
            
            Console.WriteLine($"Loading {_fontPath}...");

            var optFile = new OTFontFile.OTFile();
            if (!optFile.open(_fontPath)) 
            {
                Assert.Fail("Failed to open optimized OTFile");
            }
            var optFont = optFile.GetFont(0);
            Assert.IsNotNull(optFont, "Optimized font [0] is null");

            var baseFile = new Baseline.OTFile();
            if (!baseFile.open(_fontPath))
            {
                 Assert.Fail("Failed to open baseline OTFile");
            }
            var baseFont = baseFile.GetFont(0);
            Assert.IsNotNull(baseFont, "Baseline font [0] is null");

            Console.WriteLine("Comparing tables...");

            var optOt = optFont!.GetOffsetTable();
            Assert.IsNotNull(optOt, "Offset Table is null");

            int mismatchCount = 0;
            for (int i = 0; i < optOt.DirectoryEntries.Count; i++)
            {
                var de = optOt.DirectoryEntries[i];
                var tag = (string)de.tag;

                Console.Write($"Checking {tag} ... ");

                var optTable = optFont.GetTable(de.tag);

                // Convert OTFontFile.OTTag to Baseline.OTTag via string
                string tagStr = (string)de.tag;
                Baseline.OTTag baseTag = tagStr;
                Baseline.OTTable? baseTable = baseFont!.GetTable(baseTag);

                if (optTable == null || baseTable == null)
                {
                    Console.WriteLine($"[WARNING] Table '{tag}' missing in one version. Opt: {optTable != null}, Base: {baseTable != null}");
                    continue;
                }

                uint optSum = 0;
                uint baseSum = 0;
                try
                {
                    optSum = optTable.CalcChecksum();
                    baseSum = baseTable.CalcChecksum();
                }
                catch (Exception ex)
                {
                    Console.WriteLine();
                    Console.WriteLine($"[CRASH] Error calculating checksum for table '{tag}'");
                    Console.WriteLine($"  Exception: {ex.GetType().Name}: {ex.Message}");
                    throw;
                }

                if (optSum != baseSum)
                {
                    Console.WriteLine("MISMATCH!");
                    mismatchCount++;
                    Console.WriteLine($"[MISMATCH] Table '{tag}' Length: {de.length}");
                    Console.WriteLine($"  Baseline:  {baseSum}");
                    Console.WriteLine($"  Optimized: {optSum}");

                    // Verify content header
                    var optBuf = optTable.GetBuffer();
                    var baseBuf = baseTable.GetBuffer();

                    if (optBuf != null && baseBuf != null && optBuf.GetLength() >= 4 && baseBuf.GetLength() >= 4)
                    {
                        var optVal = optBuf.GetUint(0);
                        var baseVal = baseBuf.GetUint(0);
                        Console.WriteLine($"  Opt First Uint: {optVal:X8}");
                        Console.WriteLine($"  Base First Uint: {baseVal:X8}");
                    }
                }
                else
                {
                    Console.WriteLine("Match.");
                }
            }

            Assert.AreEqual(0, mismatchCount, $"Found {mismatchCount} checksum mismatches between Baseline and Optimized versions.");
            Console.WriteLine("All individual tables match!");
        }
    }
}
