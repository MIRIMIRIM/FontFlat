using BenchmarkDotNet.Attributes;
using OTFontFile;
using System;
using System.IO;
using System.Linq;

namespace OTFontFile.Benchmarks.Benchmarks
{
    [MarkdownExporter, AsciiDocExporter, HtmlExporter, RPlotExporter]
    [MemoryDiagnoser]
    [SimpleJob(warmupCount: 3, iterationCount: 10)]
    public class FileLoadingBenchmarks
    {
        [Params("Small", "Medium", "Large", "Collection")]
        public string FontType { get; set; } = "Small";

        private string _fontPath;

        [GlobalSetup]
        public void Setup()
        {
            var resourcesPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "BenchmarkResources", "SampleFonts");
            _fontPath = FontType switch
            {
                "Small" => Directory.GetFiles(resourcesPath, "*[Ss]mall*.ttf").FirstOrDefault(),
                "Medium" => Directory.GetFiles(resourcesPath, "*[Mm]edium*.ttf").FirstOrDefault(),
                "Large" => Directory.GetFiles(resourcesPath, "*[Ll]arge*.ttf").FirstOrDefault(),
                "Collection" => Directory.GetFiles(resourcesPath, "*.ttc").FirstOrDefault(),
                _ => null
            };

            if (string.IsNullOrEmpty(_fontPath) || !File.Exists(_fontPath))
            {
                throw new FileNotFoundException($"Font file not found for type: {FontType}");
            }
        }

        [Benchmark]
        [BenchmarkCategory("Open")]
        public OTFile OpenFontFile()
        {
            var otFile = new OTFile();
            otFile.open(_fontPath);
            return otFile;
        }

        [Benchmark]
        [BenchmarkCategory("GetFont")]
        public OTFont GetFirstFont()
        {
            var otFile = new OTFile();
            otFile.open(_fontPath);
            var font = otFile.GetFont(0);
            return font;
        }

        [Benchmark]
        [BenchmarkCategory("OpenClose")]
        public void OpenAndCloseFontFile()
        {
            var otFile = new OTFile();
            otFile.open(_fontPath);
            otFile.close();
        }
    }
}
