using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using System.IO;

namespace OTFontFile.Benchmarks.Benchmarks
{
    //[SimpleJob(RuntimeMoniker.Net80)]
    [BenchmarkDotNet.Attributes.InProcess]
    [MemoryDiagnoser]
    public class ConcurrencyBenchmarks
    {
        private string _fontPath = null!;
        private OTFontFile.OTFont _optimizedFont = null!;
        private Baseline.OTFont _baselineFont = null!;
        
        // Use separate files to avoid sharing file handles/locks if any
        private OTFontFile.OTFile _optimizedFile = null!;
        private Baseline.OTFile _baselineFile = null!;

        [GlobalSetup]
        public void Setup()
        {
            try 
            {
                // Use a large TTC font to see the benefit of parallelism
                var perfFontsPath = BenchmarkPathHelper.ResolvePerformanceTestFontsPath();
                _fontPath = Path.Combine(perfFontsPath, "SourceHanSans.ttc");

                if (!File.Exists(_fontPath))
                {
                    _fontPath = BenchmarkPathHelper.FindLargestTtc(perfFontsPath);
                }

                if (string.IsNullOrEmpty(_fontPath) || !File.Exists(_fontPath))
                {
                    var sampleFontsPath = BenchmarkPathHelper.ResolveSampleFontsPath();
                    _fontPath = BenchmarkPathHelper.FindLargestTtc(sampleFontsPath);
                }

                Console.WriteLine($"[Setup] Looking for font at: {_fontPath}"); // DEBUG
                if (string.IsNullOrEmpty(_fontPath) || !File.Exists(_fontPath))
                {
                     throw new FileNotFoundException("Test font not found for concurrency benchmarks");
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[SETUP ERROR] {ex.Message}");
                throw;
            }
        }

        [Benchmark(Baseline = true)]
        public uint CalcChecksum_Baseline_Serial()
        {
            // Instantiate per-call to measure cold start (Open + Checksum)
            // ensuring no caching interferes
            var file = new Baseline.OTFile();
            if (!file.open(_fontPath)) throw new Exception("Failed to open baseline");
            var font = file.GetFont(0);
            return font.CalcChecksum();
        }

        [Benchmark]
        public uint CalcChecksum_Optimized_Parallel()
        {
             // Instantiate per-call
            var file = new OTFontFile.OTFile();
            if (!file.open(_fontPath)) throw new Exception("Failed to open optimized");
            var font = file.GetFont(0);
            return font.CalcChecksum();
        }
    }
}
