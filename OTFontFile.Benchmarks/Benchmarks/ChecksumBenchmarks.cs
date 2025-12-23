using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using OTFontFile;
using System;
using System.IO;

namespace OTFontFile.Benchmarks.Benchmarks
{
    /// <summary>
    /// 校验和计算性能基准测试（使用优化版本）
    /// </summary>
    [MarkdownExporter, AsciiDocExporter, HtmlExporter, RPlotExporter]
    [MemoryDiagnoser]
    [ThreadingDiagnoser]
    [SimpleJob(warmupCount: 3, iterationCount: 10)]
    public class ChecksumBenchmarks
    {
        [Params(1024, 4096, 16384, 65536, 262144, 1048576)]
        public int TableSize { get; set; }

        private OTFontFile.MBOBuffer? _buffer;

        [GlobalSetup]
        public void Setup()
        {
            // 创建指定大小的测试缓冲区
            _buffer = new OTFontFile.MBOBuffer((uint)TableSize);

            // 使用伪随机数据填充
            var random = new Random(42);
            for (uint i = 0; i < TableSize; i++)
            {
                _buffer.SetByte((byte)random.Next(256), i);
            }
        }

        [Benchmark]
        [BenchmarkCategory("CalcChecksum")]
        public uint CalcChecksum()
        {
            return _buffer!.CalcChecksum();
        }

        [Benchmark]
        [BenchmarkCategory("VerifyChecksum")]
        public bool VerifyChecksum()
        {
            var checksum = _buffer!.CalcChecksum();
            // 简单验证：如果非0，则认为有效
            return checksum != 0;
        }

        // 测试多个小表的校验和计算
        [Benchmark]
        [BenchmarkCategory("MultipleSmallTables")]
        public void CalcMultipleSmallTables()
        {
            const int numTables = 100;
            uint totalChecksum = 0;

            for (int i = 0; i < numTables; i++)
            {
                var smallBuffer = new OTFontFile.MBOBuffer(1024);
                totalChecksum += smallBuffer.CalcChecksum();
            }
        }

        // 比较大表和小表的性能差异
        [Benchmark(Baseline = true)]
        [BenchmarkCategory("Comparison")]
        public uint LargeTableChecksum()
        {
            if (TableSize < 1048576)
                return 0;

            // 创建大的测试缓冲区
            var largeBuffer = new OTFontFile.MBOBuffer(1048576);
            return largeBuffer.CalcChecksum();
        }

        [Benchmark]
        [BenchmarkCategory("Comparison")]
        public uint SmallTableChecksum()
        {
            if (TableSize > 4096)
                return 0;

            var smallBuffer = new OTFontFile.MBOBuffer(4096);
            return smallBuffer.CalcChecksum();
        }

        [GlobalCleanup]
        public void Cleanup()
        {
            _buffer = null;
        }
    }
}

