using BenchmarkDotNet.Attributes;
using OTFontFile2.Tables;
using System;
using System.IO;
using System.Linq;
using Legacy = OTFontFile;

namespace OTFontFile.Benchmarks.Benchmarks
{
    [MemoryDiagnoser]
    [SimpleJob(warmupCount: 3, iterationCount: 10)]
    public class OTFontFileVsOTFontFile2Benchmarks
    {
        private string _ttfPath = string.Empty;
        private string _ttcPath = string.Empty;

        [GlobalSetup]
        public void Setup()
        {
            string fontsPath = BenchmarkPathHelper.ResolveSampleFontsPath();
            _ttfPath = ResolveTtfPath(fontsPath);
            _ttcPath = ResolveTtcPath(fontsPath);
        }

        [Benchmark]
        [BenchmarkCategory("OpenClose", "TTF", "OTFontFile")]
        public void OTFontFile_OpenClose_Ttf()
        {
            using var file = new Legacy.OTFile();
            file.open(_ttfPath);
            _ = file.GetFont(0);
        }

        [Benchmark]
        [BenchmarkCategory("OpenClose", "TTF", "OTFontFile2")]
        public void OTFontFile2_OpenClose_Ttf()
        {
            using var file = OTFontFile2.SfntFile.Open(_ttfPath);
            _ = file.GetFont(0);
        }

        [Benchmark]
        [BenchmarkCategory("ParseTables", "TTF", "OTFontFile")]
        public void OTFontFile_ParseCommonTables_Ttf()
        {
            using var file = new Legacy.OTFile();
            file.open(_ttfPath);

            var font = file.GetFont(0);
            if (font is null)
            {
                throw new InvalidOperationException($"Failed to read first font from {_ttfPath}");
            }

            _ = font.GetTable("head");
            _ = font.GetTable("maxp");
            _ = font.GetTable("name");
            _ = font.GetTable("cmap");
            _ = font.GetTable("glyf");
        }

        [Benchmark]
        [BenchmarkCategory("ParseTables", "TTF", "OTFontFile2")]
        public void OTFontFile2_ParseCommonTables_Ttf()
        {
            using var file = OTFontFile2.SfntFile.Open(_ttfPath);
            var font = file.GetFont(0);

            if (!font.TryGetHead(out var head)
                || !font.TryGetMaxp(out var maxp)
                || !font.TryGetName(out _)
                || !font.TryGetCmap(out _)
                || !font.TryGetLoca(out var loca)
                || !font.TryGetGlyf(out var glyf))
            {
                throw new InvalidOperationException($"Failed to parse required tables from {_ttfPath}");
            }

            _ = glyf.TryGetGlyphData(0, loca, head.IndexToLocFormat, maxp.NumGlyphs, out _);
        }

        [Benchmark]
        [BenchmarkCategory("OpenClose", "TTC", "OTFontFile")]
        public void OTFontFile_OpenClose_Ttc()
        {
            using var file = new Legacy.OTFile();
            file.open(_ttcPath);
            _ = file.GetFont(0);
        }

        [Benchmark]
        [BenchmarkCategory("OpenClose", "TTC", "OTFontFile2")]
        public void OTFontFile2_OpenClose_Ttc()
        {
            using var file = OTFontFile2.SfntFile.Open(_ttcPath);
            _ = file.GetFont(0);
        }

        [Benchmark]
        [BenchmarkCategory("ParseTables", "TTC", "OTFontFile")]
        public void OTFontFile_ParseCommonTables_Ttc()
        {
            using var file = new Legacy.OTFile();
            file.open(_ttcPath);

            var font = file.GetFont(0);
            if (font is null)
            {
                throw new InvalidOperationException($"Failed to read first font from {_ttcPath}");
            }

            _ = font.GetTable("head");
            _ = font.GetTable("maxp");
            _ = font.GetTable("name");
            _ = font.GetTable("cmap");
            _ = font.GetTable("glyf");
        }

        [Benchmark]
        [BenchmarkCategory("ParseTables", "TTC", "OTFontFile2")]
        public void OTFontFile2_ParseCommonTables_Ttc()
        {
            using var file = OTFontFile2.SfntFile.Open(_ttcPath);
            var font = file.GetFont(0);

            if (!font.TryGetHead(out var head)
                || !font.TryGetMaxp(out var maxp)
                || !font.TryGetName(out _)
                || !font.TryGetCmap(out _)
                || !font.TryGetLoca(out var loca)
                || !font.TryGetGlyf(out var glyf))
            {
                throw new InvalidOperationException($"Failed to parse required tables from {_ttcPath}");
            }

            _ = glyf.TryGetGlyphData(0, loca, head.IndexToLocFormat, maxp.NumGlyphs, out _);
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

            string? discovered = Directory.Exists(fontsPath)
                ? Directory.GetFiles(fontsPath, "*.ttf").FirstOrDefault()
                : null;

            return discovered ?? throw new FileNotFoundException($"No .ttf file found in {fontsPath}");
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

            string? discovered = Directory.Exists(fontsPath)
                ? Directory.GetFiles(fontsPath, "*.ttc").FirstOrDefault()
                : null;

            return discovered ?? throw new FileNotFoundException($"No .ttc file found in {fontsPath}");
        }
    }
}
