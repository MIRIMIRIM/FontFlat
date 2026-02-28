using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Jobs;
using OTFontFile;
using System.Buffers;

namespace OTFontFile.Benchmarks.Benchmarks
{
    /// <summary>
    /// 对象池化性能基准测试
    /// 用于验证 BufferPool (ArrayPool<byte>) 相比直接 new byte[] 的性能差异
    ///
    /// 测试场景：
    /// 1. 多次分配/释放大型缓冲区（模拟加载多个字体）
    /// 2. 分配/释放小型缓冲区（模拟 DirectoryEntry/OffsetTable）
    /// 3. 多种大小缓冲区的混合使用
    /// </summary>
    [MarkdownExporter, AsciiDocExporter, HtmlExporter, RPlotExporter]
    [MemoryDiagnoser]
    [SimpleJob(warmupCount: 3, iterationCount: 10)]
    public class ObjectPoolingBenchmarks
    {
        // 测试缓冲区大小范围
        private const int SmallBufferSize = 16;    // DirectoryEntry, OffsetTable 大小
        private const int MediumBufferSize = 4096;  // 典型小表大小
        private const int LargeBufferSize = 65536;  // 大型表大小（64KB，池化阈值）
        private const int ExtraLargeBufferSize = 1048576; // 超大表（1MB，如 glyf/CFF）

        // 测试迭代次数
        private const int IterationCount = 1000; // 分配/释放次数

        #region 1. 大型缓冲区分配 - 模拟表加载

        /// <summary>
        /// 分配大型缓冲区（64KB）- 对应大型表加载
        /// 这是池化器最理想的场景
        /// </summary>
        [Benchmark]
        [BenchmarkCategory("LargeBuffer", "NoPool")]
        public byte[] AllocateLargeBuffer_NoPool()
        {
            byte[]? buffer = null;
            for (int i = 0; i < IterationCount; i++)
            {
                buffer = new byte[LargeBufferSize];
            }
            return buffer ?? Array.Empty<byte>();
        }

        [Benchmark]
        [BenchmarkCategory("LargeBuffer", "WithPool")]
        public byte[] AllocateLargeBuffer_WithPool()
        {
            byte[]? buffer = null;
            var pool = ArrayPool<byte>.Create();
            
            for (int i = 0; i < IterationCount; i++)
            {
                buffer = pool.Rent(LargeBufferSize);
                pool.Return(buffer);
            }
            
            return buffer ?? Array.Empty<byte>();
        }

        /// <summary>
        /// 分配超大缓冲区（1MB）- 对应 glyf/CFF 表
        /// </summary>
        [Benchmark]
        [BenchmarkCategory("ExtraLargeBuffer", "NoPool")]
        public byte[] AllocateExtraLargeBuffer_NoPool()
        {
            byte[]? buffer = null;
            for (int i = 0; i < IterationCount; i++)
            {
                buffer = new byte[ExtraLargeBufferSize];
            }
            return buffer ?? Array.Empty<byte>();
        }

        [Benchmark]
        [BenchmarkCategory("ExtraLargeBuffer", "WithPool")]
        public byte[] AllocateExtraLargeBuffer_WithPool()
        {
            byte[]? buffer = null;
            var pool = ArrayPool<byte>.Create();
            
            for (int i = 0; i < IterationCount; i++)
            {
                buffer = pool.Rent(ExtraLargeBufferSize);
                pool.Return(buffer);
            }
            
            return buffer ?? Array.Empty<byte>();
        }

        #endregion

        #region 2. 小型缓冲区分配 - 模拟临时结构

        /// <summary>
        /// 分配小型缓冲区（16字节）- 对应 DirectoryEntry, OffsetTable
        /// 这是池化器的最不理想场景（overhead > 收益）
        /// </summary>
        [Benchmark]
        [BenchmarkCategory("SmallBuffer", "NoPool")]
        public byte[] AllocateSmallBuffer_NoPool()
        {
            byte[]? buffer = null;
            for (int i = 0; i < IterationCount; i++)
            {
                buffer = new byte[SmallBufferSize];
            }
            return buffer ?? Array.Empty<byte>();
        }

        [Benchmark]
        [BenchmarkCategory("SmallBuffer", "WithPool")]
        public byte[] AllocateSmallBuffer_WithPool()
        {
            byte[]? buffer = null;
            var pool = ArrayPool<byte>.Create();
            
            for (int i = 0; i < IterationCount; i++)
            {
                buffer = pool.Rent(SmallBufferSize);
                pool.Return(buffer);
            }
            
            return buffer ?? Array.Empty<byte>();
        }

        #endregion

        #region 3. 多字体加载模拟

        /// <summary>
        /// 混合大小分配 - 模拟加载多个不同大小的表
        /// 包括小（16B）、中（4KB）、大（64KB）、超大（1MB）
        /// </summary>
        [Benchmark]
        [BenchmarkCategory("MixedSizes", "NoPool")]
        public void AllocateMixedSizes_NoPool()
        {
            for (int i = 0; i < IterationCount; i++)
            {
                int size = GetRandomSize(i);
                var buffer = new byte[size];
            }
            GC.Collect();
        }

        [Benchmark]
        [BenchmarkCategory("MixedSizes", "WithPool")]
        public void AllocateMixedSizes_WithPool()
        {
            var pool = ArrayPool<byte>.Create();
            
            for (int i = 0; i < IterationCount; i++)
            {
                int size = GetRandomSize(i);
                var buffer = pool.Rent(size);
                pool.Return(buffer);
            }
            
            GC.Collect();
        }

        /// <summary>
        /// 根据索引生成可重现的随机大小
        /// </summary>
        private int GetRandomSize(int index)
        {
            // 使用固定序列确保可重现
            int pattern = index % 10;
            return pattern switch
            {
                0 => SmallBufferSize,          // 10% - 16B
                1 => SmallBufferSize,          // 10% - 16B
                2 => SmallBufferSize,          // 10% - 16B
                3 => MediumBufferSize,         // 10% - 4KB
                4 => LargeBufferSize,          // 10% - 64KB (池化阈值)
                5 => LargeBufferSize,          // 10% - 64KB
                6 => ExtraLargeBufferSize,     // 10% - 1MB
                7 => ExtraLargeBufferSize,     // 5% - 1MB
                8 => LargeBufferSize * 2,     // 5% - 128KB
                _ => LargeBufferSize           // 剩余 - 64KB
            };
        }

        #endregion

        #region 4. 实际字体加载集成测试

        private const string TestFontFileName = "SourceHanSansCN-Regular.otf";
        private string? _testFontPath;

        [GlobalSetup]
        public void Setup()
        {
            var performancePath = BenchmarkPathHelper.ResolvePerformanceTestFontsPath();
            var candidatePath = Path.Combine(performancePath, TestFontFileName);
            if (File.Exists(candidatePath))
            {
                _testFontPath = candidatePath;
                return;
            }

            _testFontPath = BenchmarkPathHelper.FindLargestTtf(performancePath);
            if (!string.IsNullOrEmpty(_testFontPath) && File.Exists(_testFontPath))
            {
                return;
            }

            var samplePath = BenchmarkPathHelper.ResolveSampleFontsPath();
            candidatePath = Path.Combine(samplePath, TestFontFileName);
            if (File.Exists(candidatePath))
            {
                _testFontPath = candidatePath;
                return;
            }

            _testFontPath = BenchmarkPathHelper.FindLargestTtf(samplePath);
            if (string.IsNullOrEmpty(_testFontPath) || !File.Exists(_testFontPath))
            {
                _testFontPath = null;
            }
        }

        /// <summary>
        /// 加载字体中的多个表 - 模拟实际使用场景
        /// 测试表缓存的总体效果（包括池化）
        /// </summary>
        [Benchmark]
        [BenchmarkCategory("FontLoading", "MultipleTables")]
        public void LoadAllTablesFromFont()
        {
            if (string.IsNullOrEmpty(_testFontPath))
            {
                Console.WriteLine("Warning: Test font not found for LoadAllTablesFromFont benchmark.");
                return;
            }

            var file = new OTFile();
            file.open(_testFontPath);

            // 加载所有表
            var tableManager = file.GetTableManager();
            var font = file.GetFont(0);

            // 获取典型的表列表
            string[] tablesToLoad = { "head", "maxp", "cmap", "hhea", "hmtx", "name", "OS/2", "post" };

            foreach (var tableName in tablesToLoad)
            {
                var table = font.GetTable(tableName);
            }

            file.close();
        }

        #endregion
    }
}
