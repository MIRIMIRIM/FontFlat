using System;
using System.Diagnostics;
using OTFontFile;
using Baseline;

namespace OTFontFile.Benchmarks
{
    /// <summary>
    /// CalculateChecksum性能测试脚本
    /// 避免BenchmarkDotNet的编译器优化问题，直接使用Stopwatch测量
    /// </summary>
    public static class TestChecksumPerformance
    {
        private static string GetFontPath()
        {
            const string FONT_FILE = "OTFontFile.Performance.Tests/TestResources/SampleFonts/SourceHanSansCN-Regular.otf";

            var candidates = new[]
            {
                $"../../../{FONT_FILE}",
                $"../../{FONT_FILE}",
                $"../{FONT_FILE}",
                FONT_FILE
            };

            foreach (var candidate in candidates)
            {
                var fullPath = Path.GetFullPath(candidate);
                if (File.Exists(fullPath))
                {
                    return fullPath;
                }
            }

            throw new FileNotFoundException("Font file not found in any of the candidate locations");
        }

        public static void Run()
        {
            const int ITERATIONS = 50;  // 迭代次数
            string FONT_PATH = GetFontPath();

            Console.WriteLine($"CalculateChecksum Performance Test");
            Console.WriteLine($"Font: {FONT_PATH}");
            Console.WriteLine($"Iterations: {ITERATIONS}");
            Console.WriteLine();

            // 1. 加载字体数据
            if (!File.Exists(FONT_PATH))
            {
                Console.WriteLine($"Error: Font file not found: {FONT_PATH}");
                return;
            }

            byte[] fontBytes = File.ReadAllBytes(FONT_PATH);
            Console.WriteLine($"Loaded font: {fontBytes.Length:N0} bytes");
            Console.WriteLine();

            // 2. 测试 Baseline 版本
            Console.WriteLine("Testing Baseline version...");
            var baselineTime = MeasureBaseline(fontBytes, ITERATIONS);
            Console.WriteLine($"Baseline Total Time: {baselineTime.TotalMilliseconds:F2} ms");
            Console.WriteLine($"Baseline Avg Time: {baselineTime.TotalMilliseconds / ITERATIONS:F4} ms/call");
            Console.WriteLine();

            // 3. 测试 Optimized 版本
            Console.WriteLine("Testing Optimized version...");
            var optimizedTime = MeasureOptimized(fontBytes, ITERATIONS);
            Console.WriteLine($"Optimized Total Time: {optimizedTime.TotalMilliseconds:F2} ms");
            Console.WriteLine($"Optimized Avg Time: {optimizedTime.TotalMilliseconds / ITERATIONS:F4} ms/call");
            Console.WriteLine();

            // 4. 对比结果
            double speedup = baselineTime.TotalMilliseconds / optimizedTime.TotalMilliseconds;
            double improvement = ((baselineTime.TotalMilliseconds - optimizedTime.TotalMilliseconds) / baselineTime.TotalMilliseconds) * 100;
            Console.WriteLine("========================================");
            if (speedup > 1)
            {
                Console.WriteLine($"Optimized version is {speedup:F2}x FASTER");
                Console.WriteLine($"Performance improvement: {improvement:F2}%");
            }
            else
            {
                Console.WriteLine($"Optimized version is {1/speedup:F2}x SLOWER");
                Console.WriteLine($"Performance change: {-improvement:F2}%");
            }
            Console.WriteLine("========================================");
        }

        static TimeSpan MeasureBaseline(byte[] fontBytes, int iterations)
        {
            var sw = Stopwatch.StartNew();
            uint sum = 0;

            for (int i = 0; i < iterations; i++)
            {
                var buf = new Baseline.MBOBuffer((uint)fontBytes.Length);
                Array.Copy(fontBytes, buf.GetBuffer(), fontBytes.Length);

                // 修改一个字节确保checksum不同，防止编译器优化
                buf.GetBuffer()[i % 1024] = (byte)i;

                sum += buf.CalcChecksumUncached();
            }

            sw.Stop();
            Console.WriteLine($"Baseline Checksum Sum: 0x{sum:X8}");
            return sw.Elapsed;
        }

        static TimeSpan MeasureOptimized(byte[] fontBytes, int iterations)
        {
            var sw = Stopwatch.StartNew();
            uint sum = 0;

            for (int i = 0; i < iterations; i++)
            {
                var buf = new MBOBuffer((uint)fontBytes.Length);
                Array.Copy(fontBytes, buf.GetBuffer(), fontBytes.Length);

                // 修改一个字节确保checksum不同，防止编译器优化
                buf.GetBuffer()[i % 1024] = (byte)i;

                sum += buf.CalcChecksumUncached();
            }

            sw.Stop();
            Console.WriteLine($"Optimized Checksum Sum: 0x{sum:X8}");
            return sw.Elapsed;
        }
    }
}
