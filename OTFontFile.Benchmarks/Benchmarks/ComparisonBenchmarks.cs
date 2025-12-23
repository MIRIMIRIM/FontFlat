using BenchmarkDotNet.Attributes;
using System;
using System.IO;

namespace OTFontFile.Benchmarks.Benchmarks
{
    /// <summary>
    /// 新旧版本性能对比基准测试（简化版本）
    /// </summary>
    [MemoryDiagnoser]
    [SimpleJob(warmupCount: 3, iterationCount: 10)]
    public class ComparisonBenchmarks
    {
        private const string SmallFontPath = "BenchmarkResources/SampleFonts/small.ttf";

        #region 文件加载对比

        /// <summary>
        /// 基线版本：加载字体
        /// </summary>
        [Benchmark(Baseline = true)]
        public void Baseline_LoadFile()
        {
            var file = new Baseline.OTFile();
            file.open(GetExistingPath(SmallFontPath));
            file.close();
        }

        /// <summary>
        /// 优化版本：加载字体
        /// </summary>
        [Benchmark]
        public void Optimized_LoadFile()
        {
            var file = new OTFontFile.OTFile();
            file.open(GetExistingPath(SmallFontPath));
            file.close();
        }

        #endregion

        #region MBOBuffer操作对比

        /// <summary>
        /// 基线版本：1KB 缓冲区
        /// </summary>
        [Benchmark(Baseline = true)]
        public void Baseline_MBOBuffer_1KB()
        {
            var buffer = new Baseline.MBOBuffer(1024);
            buffer.CalcChecksum();
        }

        /// <summary>
        /// 优化版本：1KB 缓冲区
        /// </summary>
        [Benchmark]
        public void Optimized_MBOBuffer_1KB()
        {
            var buffer = new OTFontFile.MBOBuffer(1024);
            buffer.CalcChecksum();
        }

        /// <summary>
        /// 基线版本：64KB 缓冲区
        /// </summary>
        [Benchmark(Baseline = true)]
        public void Baseline_MBOBuffer_64KB()
        {
            var buffer = new Baseline.MBOBuffer(65536);
            buffer.CalcChecksum();
        }

        /// <summary>
        /// 优化版本：64KB 缓冲区
        /// </summary>
        [Benchmark]
        public void Optimized_MBOBuffer_64KB()
        {
            var buffer = new OTFontFile.MBOBuffer(65536);
            buffer.CalcChecksum();
        }

        #endregion

        #region 辅助方法

        /// <summary>
        /// 获取实际存在的文件路径
        /// </summary>
        private string GetExistingPath(string requestedPath)
        {
            if (File.Exists(requestedPath))
            {
                return requestedPath;
            }

            // 尝试其他可能的文件
            var alternatives = new[]
            {
                "BenchmarkResources/SampleFonts/arial.ttf",
                "BenchmarkResources/SampleFonts/Roboto-Regular.ttf",
                "BenchmarkResources/SampleFonts/NotoSans-Regular.ttf"
            };

            foreach (var alt in alternatives)
            {
                if (File.Exists(alt))
                {
                    return alt;
                }
            }

            // 都不存在，返回请求的路径（基准测试会失败，但这是预期的）
            return requestedPath;
        }

        #endregion
    }
}
