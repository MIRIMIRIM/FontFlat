using BenchmarkDotNet.Attributes;
using OTFontFile2.Tables;
using System;
using System.IO;
using Legacy = OTFontFile;

namespace OTFontFile.Benchmarks.Benchmarks
{
    [MemoryDiagnoser]
    [SimpleJob(warmupCount: 3, iterationCount: 10)]
    public class RealWorldParsingBenchmarks
    {
        private const int MaxGlyphsToScan = 2048;

        private string _ttfPath = string.Empty;
        private string _ttcPath = string.Empty;
        private char[] _probeChars = Array.Empty<char>();

        [GlobalSetup]
        public void Setup()
        {
            string fontsPath = BenchmarkPathHelper.ResolveSampleFontsPath();
            _ttfPath = ResolveTtfPath(fontsPath);
            _ttcPath = ResolveTtcPath(fontsPath);
            _probeChars = BuildProbeChars();
        }

        [Benchmark]
        [BenchmarkCategory("RealWorld", "Name", "OTFontFile")]
        public int OTFontFile_NameDecode_Ttf()
        {
            using var file = new Legacy.OTFile();
            file.open(_ttfPath);

            var font = file.GetFont(0) ?? throw new InvalidOperationException("Failed to open first font.");
            var name = font.GetTable("name") as Legacy.Table_name;
            if (name is null)
            {
                return 0;
            }

            int checksum = 0;
            for (uint i = 0; i < name.NumberNameRecords; i++)
            {
                var record = name.GetNameRecord(i);
                if (record is null)
                {
                    continue;
                }

                byte[]? bytes = name.GetEncodedString(record);
                if (bytes is null)
                {
                    continue;
                }

                string? decoded = Legacy.Table_name.DecodeString(
                    record.PlatformID,
                    record.EncodingID,
                    record.LanguageID,
                    bytes);

                if (!string.IsNullOrEmpty(decoded))
                {
                    checksum += decoded.Length;
                }
            }

            return checksum;
        }

        [Benchmark]
        [BenchmarkCategory("RealWorld", "Name", "OTFontFile2")]
        public int OTFontFile2_NameDecode_Ttf()
        {
            using var file = OTFontFile2.SfntFile.Open(_ttfPath);
            var font = file.GetFont(0);
            if (!font.TryGetName(out var name))
            {
                return 0;
            }

            int checksum = 0;
            int count = name.Count;
            for (int i = 0; i < count; i++)
            {
                if (!name.TryGetRecord(i, out var record))
                {
                    continue;
                }

                string? decoded = name.DecodeString(record);
                if (!string.IsNullOrEmpty(decoded))
                {
                    checksum += decoded.Length;
                }
            }

            return checksum;
        }

        [Benchmark]
        [BenchmarkCategory("RealWorld", "Name", "TTC", "OTFontFile")]
        public int OTFontFile_NameDecode_Ttc()
        {
            using var file = new Legacy.OTFile();
            file.open(_ttcPath);

            var font = file.GetFont(0) ?? throw new InvalidOperationException("Failed to open first font.");
            var name = font.GetTable("name") as Legacy.Table_name;
            if (name is null)
            {
                return 0;
            }

            int checksum = 0;
            for (uint i = 0; i < name.NumberNameRecords; i++)
            {
                var record = name.GetNameRecord(i);
                if (record is null)
                {
                    continue;
                }

                byte[]? bytes = name.GetEncodedString(record);
                if (bytes is null)
                {
                    continue;
                }

                string? decoded = Legacy.Table_name.DecodeString(
                    record.PlatformID,
                    record.EncodingID,
                    record.LanguageID,
                    bytes);

                if (!string.IsNullOrEmpty(decoded))
                {
                    checksum += decoded.Length;
                }
            }

            return checksum;
        }

        [Benchmark]
        [BenchmarkCategory("RealWorld", "Name", "TTC", "OTFontFile2")]
        public int OTFontFile2_NameDecode_Ttc()
        {
            using var file = OTFontFile2.SfntFile.Open(_ttcPath);
            var font = file.GetFont(0);
            if (!font.TryGetName(out var name))
            {
                return 0;
            }

            int checksum = 0;
            int count = name.Count;
            for (int i = 0; i < count; i++)
            {
                if (!name.TryGetRecord(i, out var record))
                {
                    continue;
                }

                string? decoded = name.DecodeString(record);
                if (!string.IsNullOrEmpty(decoded))
                {
                    checksum += decoded.Length;
                }
            }

            return checksum;
        }

        [Benchmark]
        [BenchmarkCategory("RealWorld", "Cmap", "OTFontFile")]
        public uint OTFontFile_CmapLookup_Ttf()
        {
            using var file = new Legacy.OTFile();
            file.open(_ttfPath);

            var font = file.GetFont(0) ?? throw new InvalidOperationException("Failed to open first font.");
            uint acc = 0;
            foreach (char c in _probeChars)
            {
                acc += font.FastMapUnicodeToGlyphID(c);
            }

            return acc;
        }

        [Benchmark]
        [BenchmarkCategory("RealWorld", "Cmap", "OTFontFile2")]
        public uint OTFontFile2_CmapLookup_Ttf()
        {
            using var file = OTFontFile2.SfntFile.Open(_ttfPath);
            var font = file.GetFont(0);

            if (!OTFontFile2.CmapUnicodeMap.TryCreate(font, out var cmap))
            {
                return 0;
            }

            uint acc = 0;
            foreach (char c in _probeChars)
            {
                cmap.TryMapCodePoint(c, out uint glyphId);
                acc += glyphId;
            }

            return acc;
        }

        [Benchmark]
        [BenchmarkCategory("RealWorld", "Cmap", "TTC", "OTFontFile")]
        public uint OTFontFile_CmapLookup_Ttc()
        {
            using var file = new Legacy.OTFile();
            file.open(_ttcPath);

            var font = file.GetFont(0) ?? throw new InvalidOperationException("Failed to open first font.");
            uint acc = 0;
            foreach (char c in _probeChars)
            {
                acc += font.FastMapUnicodeToGlyphID(c);
            }

            return acc;
        }

        [Benchmark]
        [BenchmarkCategory("RealWorld", "Cmap", "TTC", "OTFontFile2")]
        public uint OTFontFile2_CmapLookup_Ttc()
        {
            using var file = OTFontFile2.SfntFile.Open(_ttcPath);
            var font = file.GetFont(0);

            if (!OTFontFile2.CmapUnicodeMap.TryCreate(font, out var cmap))
            {
                return 0;
            }

            uint acc = 0;
            foreach (char c in _probeChars)
            {
                cmap.TryMapCodePoint(c, out uint glyphId);
                acc += glyphId;
            }

            return acc;
        }

        [Benchmark]
        [BenchmarkCategory("RealWorld", "Glyf", "OTFontFile")]
        public int OTFontFile_GlyfScan_Ttf()
        {
            using var file = new Legacy.OTFile();
            file.open(_ttfPath);

            var font = file.GetFont(0) ?? throw new InvalidOperationException("Failed to open first font.");
            var glyf = font.GetTable("glyf") as Legacy.Table_glyf;
            if (glyf is null)
            {
                return 0;
            }

            int count = Math.Min((int)font.GetMaxpNumGlyphs(), MaxGlyphsToScan);
            int score = 0;
            for (int i = 0; i < count; i++)
            {
                var header = glyf.GetGlyphHeader((uint)i, font);
                if (header is not null)
                {
                    score += header.numberOfContours;
                }
            }

            return score;
        }

        [Benchmark]
        [BenchmarkCategory("RealWorld", "Glyf", "OTFontFile2")]
        public int OTFontFile2_GlyfScan_Ttf()
        {
            using var file = OTFontFile2.SfntFile.Open(_ttfPath);
            var font = file.GetFont(0);

            if (!font.TryGetHead(out var head)
                || !font.TryGetMaxp(out var maxp)
                || !font.TryGetLoca(out var loca)
                || !font.TryGetGlyf(out var glyf))
            {
                return 0;
            }

            int count = Math.Min((int)maxp.NumGlyphs, MaxGlyphsToScan);
            int score = 0;
            for (int i = 0; i < count; i++)
            {
                if (!glyf.TryGetGlyphData((ushort)i, loca, head.IndexToLocFormat, maxp.NumGlyphs, out ReadOnlySpan<byte> glyphData))
                {
                    continue;
                }

                if (GlyfTable.TryReadGlyphHeader(glyphData, out var header))
                {
                    score += header.NumberOfContours;
                }
            }

            return score;
        }

        [Benchmark]
        [BenchmarkCategory("RealWorld", "Glyf", "TTC", "OTFontFile")]
        public int OTFontFile_GlyfScan_Ttc()
        {
            using var file = new Legacy.OTFile();
            file.open(_ttcPath);

            var font = file.GetFont(0) ?? throw new InvalidOperationException("Failed to open first font.");
            var glyf = font.GetTable("glyf") as Legacy.Table_glyf;
            if (glyf is null)
            {
                return 0;
            }

            int count = Math.Min((int)font.GetMaxpNumGlyphs(), MaxGlyphsToScan);
            int score = 0;
            for (int i = 0; i < count; i++)
            {
                var header = glyf.GetGlyphHeader((uint)i, font);
                if (header is not null)
                {
                    score += header.numberOfContours;
                }
            }

            return score;
        }

        [Benchmark]
        [BenchmarkCategory("RealWorld", "Glyf", "TTC", "OTFontFile2")]
        public int OTFontFile2_GlyfScan_Ttc()
        {
            using var file = OTFontFile2.SfntFile.Open(_ttcPath);
            var font = file.GetFont(0);

            if (!font.TryGetHead(out var head)
                || !font.TryGetMaxp(out var maxp)
                || !font.TryGetLoca(out var loca)
                || !font.TryGetGlyf(out var glyf))
            {
                return 0;
            }

            int count = Math.Min((int)maxp.NumGlyphs, MaxGlyphsToScan);
            int score = 0;
            for (int i = 0; i < count; i++)
            {
                if (!glyf.TryGetGlyphData((ushort)i, loca, head.IndexToLocFormat, maxp.NumGlyphs, out ReadOnlySpan<byte> glyphData))
                {
                    continue;
                }

                if (GlyfTable.TryReadGlyphHeader(glyphData, out var header))
                {
                    score += header.NumberOfContours;
                }
            }

            return score;
        }

        private static string ResolveTtfPath(string fontsPath)
        {
            string[] preferred =
            {
                Path.Combine(fontsPath, "medium.ttf"),
                Path.Combine(fontsPath, "small.ttf"),
                Path.Combine(fontsPath, "NotoSans-Regular.ttf")
            };

            foreach (string path in preferred)
            {
                if (File.Exists(path))
                {
                    return path;
                }
            }

            if (Directory.Exists(fontsPath))
            {
                string[] files = Directory.GetFiles(fontsPath, "*.ttf");
                if (files.Length > 0)
                {
                    return files[0];
                }
            }

            throw new FileNotFoundException($"No .ttf file found in {fontsPath}");
        }

        private static string ResolveTtcPath(string fontsPath)
        {
            string[] preferred =
            {
                Path.Combine(fontsPath, "collection.ttc"),
                Path.Combine(fontsPath, "NotoSansCJK.ttc")
            };

            foreach (string path in preferred)
            {
                if (File.Exists(path))
                {
                    return path;
                }
            }

            if (Directory.Exists(fontsPath))
            {
                string[] files = Directory.GetFiles(fontsPath, "*.ttc");
                if (files.Length > 0)
                {
                    return files[0];
                }
            }

            throw new FileNotFoundException($"No .ttc file found in {fontsPath}");
        }

        private static char[] BuildProbeChars()
        {
            // Typical mix: ASCII letters + digits + punctuation + common CJK.
            string ascii = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789.,;:!?-_()/[]{}";
            char[] cjk =
            {
                '你','好','中','文','字','体','測','試','国','汉',
                '日','本','語','韓','한','글','繁','簡','常','用'
            };

            char[] result = new char[ascii.Length + cjk.Length];
            ascii.CopyTo(0, result, 0, ascii.Length);
            Array.Copy(cjk, 0, result, ascii.Length, cjk.Length);
            return result;
        }
    }
}
