using BenchmarkDotNet.Attributes;
using OTFontFile;
using System;
using System.IO;
using System.Linq;

namespace OTFontFile.Benchmarks.Benchmarks
{
    /// <summary>
    /// 字体表解析性能基准测试（使用优化版本）
    /// </summary>
    [MarkdownExporter, AsciiDocExporter, HtmlExporter, RPlotExporter]
    [MemoryDiagnoser]
    [SimpleJob(warmupCount: 3, iterationCount: 10)]
    public class TableParsingBenchmarks
    {
        private string _mediumFontPath;
        private OTFile _otFile;

        [GlobalSetup]
        public void Setup()
        {
            var resourcesPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "BenchmarkResources", "SampleFonts");
            _mediumFontPath = Directory.GetFiles(resourcesPath, "*.ttf")
                .FirstOrDefault(f => new FileInfo(f).Length > 100000 && new FileInfo(f).Length < 1000000);

            if (string.IsNullOrEmpty(_mediumFontPath))
            {
                throw new FileNotFoundException("Medium-sized font file not found for benchmarks");
            }

            _otFile = new OTFile();
            _otFile.open(_mediumFontPath);
        }

        [GlobalCleanup]
        public void Cleanup()
        {
            _otFile?.close();
        }

        [Benchmark]
        [BenchmarkCategory("Table")]
        public void LoadHeadTable()
        {
            var font = _otFile.GetFont(0);
            var table = font.GetTable("head");
        }

        [Benchmark]
        [BenchmarkCategory("Table")]
        public void LoadMaxpTable()
        {
            var font = _otFile.GetFont(0);
            var table = font.GetTable("maxp");
        }

        [Benchmark]
        [BenchmarkCategory("Table")]
        public void LoadNameTable()
        {
            var font = _otFile.GetFont(0);
            var table = font.GetTable("name");
        }

        [Benchmark]
        [BenchmarkCategory("Table")]
        public void LoadCmapTable()
        {
            var font = _otFile.GetFont(0);
            var table = font.GetTable("cmap");
        }

        [Benchmark]
        [BenchmarkCategory("Table")]
        public void LoadGlyfTable()
        {
            var font = _otFile.GetFont(0);
            var table = font.GetTable("glyf");
        }
    }
}
