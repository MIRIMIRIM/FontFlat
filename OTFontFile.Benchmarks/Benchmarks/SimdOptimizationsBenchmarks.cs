using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Jobs;
using OTFontFile;
using Baseline;

namespace OTFontFile.Benchmarks.Benchmarks
{
    /// <summary>
    /// SIMD优化性能基准测试
    /// 用于验证所有SIMD批处理优化效果，包含Baseline对比
    /// 
    /// 优化列表（从 commit 8f05cb1 开始）:
    /// 1. MBOBuffer.BinaryEqual - Vector<byte>.EqualsAll (commit 8f05cb1)
    /// 2. CMAP4 Format4.GetMap - batchSize=64 (commit f766da7)
    /// 3. CMAP6 Format6.GetMap - batchSize=64 (commit 9077fe0)
    /// 4. CMAP0 Format0.GetMap - Dynamic batchSize (commit 9077fe0)
    /// 5. CMAP12 Format12.GetMap - batchSize=64 (commit 860d816)
    /// 6. TTCHeader DirectoryOffsets - batchSize=4 (commit f2d23f4)
    /// 7. Table_VORG GetAllVertOriginYMetrics - batchSize=8 (commit f2d23f4)
    /// 
    /// 测试方法参考：OTFontFile.Performance.Tests/UnitTests/SimdTests.cs
    /// </summary>
    [MarkdownExporter, AsciiDocExporter, HtmlExporter, RPlotExporter]
    [MemoryDiagnoser]
    [SimpleJob(warmupCount: 2, iterationCount: 5)]
    public class SimdOptimizationsBenchmarks
    {
        // 字体文件路径来自 Performance.Tests/TestResources/SampleFonts
        private const string TestResourcesPath = "../OTFontFile.Performance.Tests/TestResources/SampleFonts";

        // CMAP 测试字体（各个 Format 的专用字体）
        private static readonly string? s_cmap0FontPath = Path.Combine(TestResourcesPath, "cmap0_font1.otf");
        private static readonly string? s_cmap4FontPath = Path.Combine(TestResourcesPath, "cmap4_font1.otf");
        private static readonly string? s_cmap6FontPath = Path.Combine(TestResourcesPath, "cmap6_font1.otf");
        private static readonly string? s_cmap12FontPath = Path.Combine(TestResourcesPath, "cmap12_font1.otf");

        // VORG 测试字体（包含 VORG 表的字体）
        private static readonly string? s_vorgFontPath = Path.Combine(TestResourcesPath, "AvenirNextW1G-Regular.OTF");

        // TTC 测试字体系列
        private static readonly string[] s_ttcFontPaths = new[]
        {
            Path.Combine(TestResourcesPath, "msyh.ttc"),
            Path.Combine(TestResourcesPath, "华康POP1体W5 & 华康POP1体W5(P).ttc")
        };

        // MBOBuffer 测试数据
        private MBOBuffer? _optimizedBufferSmall;
        private MBOBuffer? _optimizedBufferMedium;
        private MBOBuffer? _optimizedBufferLarge;
        private Baseline.MBOBuffer? _baselineBufferSmall;
        private Baseline.MBOBuffer? _baselineBufferMedium;
        private Baseline.MBOBuffer? _baselineBufferLarge;

        // 字体文件对象
        private OTFile? _optimizedFontFile;
        private Baseline.OTFile? _baselineFontFile;

        // CMAP 表对象（各个 Format 专用）
        private Table_cmap? _optimizedCmapFormat4;
        private Table_cmap? _optimizedCmapFormat6;
        private Table_cmap? _optimizedCmapFormat0;
        private Table_cmap? _optimizedCmapFormat12;
        private Baseline.Table_cmap? _baselineCmapFormat4;
        private Baseline.Table_cmap? _baselineCmapFormat6;
        private Baseline.Table_cmap? _baselineCmapFormat0;
        private Baseline.Table_cmap? _baselineCmapFormat12;

        // VORG 表对象
        private Table_VORG? _optimizedVORG;
        private Baseline.Table_VORG? _baselineVORG;

        // TTCHeader 对象（使用第一个可用的 TTC 文件）
        private string? _activeTTCPath;
        private OTFile? _optimizedTTCFile;
        private Baseline.OTFile? _baselineTTCFile;
        private TTCHeader? _optimizedTTCHeader;
        private Baseline.TTCHeader? _baselineTTCHeader;

        [GlobalSetup]
        public void Setup()
        {
            // 1. MBOBuffer 测试数据
            _optimizedBufferSmall = new MBOBuffer(64);
            _optimizedBufferMedium = new MBOBuffer(1024);
            _optimizedBufferLarge = new MBOBuffer(10240);
            _baselineBufferSmall = new Baseline.MBOBuffer(64);
            _baselineBufferMedium = new Baseline.MBOBuffer(1024);
            _baselineBufferLarge = new Baseline.MBOBuffer(10240);

            FillBuffers(_optimizedBufferSmall, _baselineBufferSmall, 64);
            FillBuffers(_optimizedBufferMedium, _baselineBufferMedium, 1024);
            FillBuffers(_optimizedBufferLarge, _baselineBufferLarge, 10240);

            // 2. 加载 CMAP 格式专用字体
            LoadCmapFormatFonts();

            // 3. 加载 VORG 字体
            LoadVORGFont();

            // 4. 加载第一个可用的 TTC 字体
            LoadTTCFont();
        }

        [GlobalCleanup]
        public void Cleanup()
        {
            // 关闭所有打开的字体文件
            _optimizedFontFile?.close();
            _baselineFontFile?.close();
            _optimizedTTCFile?.close();
            _baselineTTCFile?.close();
        }

        private void LoadCmapFormatFonts()
        {
            // 加载 Format4 字体
            if (s_cmap4FontPath != null && File.Exists(s_cmap4FontPath))
            {
                _optimizedFontFile = new OTFile();
                _optimizedFontFile.open(s_cmap4FontPath);
                var optimizedFont = _optimizedFontFile.GetFont(0);
                _optimizedCmapFormat4 = optimizedFont.GetTable("cmap") as Table_cmap;

                _baselineFontFile = new Baseline.OTFile();
                _baselineFontFile.open(s_cmap4FontPath);
                var baselineFont = _baselineFontFile.GetFont(0);
                _baselineCmapFormat4 = baselineFont.GetTable("cmap") as Baseline.Table_cmap;
            }

            // 加载 Format6 字体（打开新文件）
            if (s_cmap6FontPath != null && File.Exists(s_cmap6FontPath))
            {
                var optFile = new OTFile();
                optFile.open(s_cmap6FontPath);
                var optFont = optFile.GetFont(0);
                _optimizedCmapFormat6 = optFont.GetTable("cmap") as Table_cmap;

                var baseFile = new Baseline.OTFile();
                baseFile.open(s_cmap6FontPath);
                var baseFont = baseFile.GetFont(0);
                _baselineCmapFormat6 = baseFont.GetTable("cmap") as Baseline.Table_cmap;
            }

            // 加载 Format0 字体（打开新文件）
            if (s_cmap0FontPath != null && File.Exists(s_cmap0FontPath))
            {
                var optFile = new OTFile();
                optFile.open(s_cmap0FontPath);
                var optFont = optFile.GetFont(0);
                _optimizedCmapFormat0 = optFont.GetTable("cmap") as Table_cmap;

                var baseFile = new Baseline.OTFile();
                baseFile.open(s_cmap0FontPath);
                var baseFont = baseFile.GetFont(0);
                _baselineCmapFormat0 = baseFont.GetTable("cmap") as Baseline.Table_cmap;
            }

            // 加载 Format12 字体（打开新文件）
            if (s_cmap12FontPath != null && File.Exists(s_cmap12FontPath))
            {
                var optFile = new OTFile();
                optFile.open(s_cmap12FontPath);
                var optFont = optFile.GetFont(0);
                _optimizedCmapFormat12 = optFont.GetTable("cmap") as Table_cmap;

                var baseFile = new Baseline.OTFile();
                baseFile.open(s_cmap12FontPath);
                var baseFont = baseFile.GetFont(0);
                _baselineCmapFormat12 = baseFont.GetTable("cmap") as Baseline.Table_cmap;
            }
        }

        private void LoadVORGFont()
        {
            if (s_vorgFontPath != null && File.Exists(s_vorgFontPath))
            {
                var optFile = new OTFile();
                optFile.open(s_vorgFontPath);
                var optFont = optFile.GetFont(0);
                _optimizedVORG = optFont.GetTable("VORG") as Table_VORG;

                var baseFile = new Baseline.OTFile();
                baseFile.open(s_vorgFontPath);
                var baseFont = baseFile.GetFont(0);
                _baselineVORG = baseFont.GetTable("VORG") as Baseline.Table_VORG;
            }
        }

        private void LoadTTCFont()
        {
            // 查找第一个可用的 TTC 文件
            foreach (var ttcPath in s_ttcFontPaths)
            {
                if (ttcPath != null && File.Exists(ttcPath))
                {
                    _activeTTCPath = ttcPath;

                    // 加载 Optimized 版本
                    _optimizedTTCFile = new OTFile();
                    _optimizedTTCFile.open(_activeTTCPath);
                    _optimizedTTCHeader = _optimizedTTCFile.GetTTCHeader();

                    // 加载 Baseline 版本
                    _baselineTTCFile = new Baseline.OTFile();
                    _baselineTTCFile.open(_activeTTCPath);
                    _baselineTTCHeader = _baselineTTCFile.GetTTCHeader();

                    break;
                }
            }
        }

        #region 1. MBOBuffer.BinaryEqual - SIMD 批量字节比较 (commit 8f05cb1)
        
        /// <summary>
        /// 小缓冲区 (64字节) - 低于 SIMD 阈值 (128字节)
        /// 应使用原始实现，SIMD 版本因条件检查会有轻微开销
        /// </summary>
        [Benchmark]
        [BenchmarkCategory("BinaryEqual", "Baseline")]
        public bool BinaryEqual_SmallBuffer_Baseline()
        {
            return Baseline.MBOBuffer.BinaryEqual(_baselineBufferSmall!, _baselineBufferSmall!);
        }

        [Benchmark]
        [BenchmarkCategory("BinaryEqual", "SIMD")]
        public bool BinaryEqual_SmallBuffer_Optimized()
        {
            return MBOBuffer.BinaryEqual(_optimizedBufferSmall!, _optimizedBufferSmall!);
        }

        /// <summary>
        /// 中等缓冲区 (1KB) - 高于 SIMD 阈值
        /// 应启用 SIMD 批量比较，预期显著加速
        /// </summary>
        [Benchmark]
        [BenchmarkCategory("BinaryEqual", "Baseline")]
        public bool BinaryEqual_MediumBuffer_Baseline()
        {
            return Baseline.MBOBuffer.BinaryEqual(_baselineBufferMedium!, _baselineBufferMedium!);
        }

        [Benchmark]
        [BenchmarkCategory("BinaryEqual", "SIMD")]
        public bool BinaryEqual_MediumBuffer_Optimized()
        {
            return MBOBuffer.BinaryEqual(_optimizedBufferMedium!, _optimizedBufferMedium!);
        }

        /// <summary>
        /// 大缓冲区 (10KB) - SIMD 效益最大
        /// 预期提升最明显
        /// </summary>
        [Benchmark]
        [BenchmarkCategory("BinaryEqual", "Baseline")]
        public bool BinaryEqual_LargeBuffer_Baseline()
        {
            return Baseline.MBOBuffer.BinaryEqual(_baselineBufferLarge!, _baselineBufferLarge!);
        }

        [Benchmark]
        [BenchmarkCategory("BinaryEqual", "SIMD")]
        public bool BinaryEqual_LargeBuffer_Optimized()
        {
            return MBOBuffer.BinaryEqual(_optimizedBufferLarge!, _optimizedBufferLarge!);
        }

        #endregion

        #region 2-5. CMAP GetMap() - SIMD 批量字符映射 (commits f766da7, 9077fe0, 860d816)

        /// <summary>
        /// CMAP4 Format4.GetMap() - Unicode BMP 子表
        /// 优化：使用 SIMD 批量读取 batchSize=64 个字符映射
        /// </summary>
        [Benchmark]
        [BenchmarkCategory("CMAP", "Baseline")]
        public uint[]? CMAP4_GetMap_Baseline()
        {
            if (_baselineCmapFormat4 == null) return null;
            var subtable = _baselineCmapFormat4.GetSubtable(3, 1); // Platform=3 (Windows), Encoding=1 (Unicode BMP)
            if (subtable == null || subtable.format != 4)
            {
                // 尝试遍历所有子表寻找 Format4
                for (uint plat = 0; plat < 10; plat++)
                {
                    for (uint enc = 0; enc < 10; enc++)
                    {
                        var st = _baselineCmapFormat4.GetSubtable((ushort)plat, (ushort)enc);
                        if (st?.format == 4)
                        {
                            subtable = st;
                            break;
                        }
                    }
                    if (subtable?.format == 4) break;
                }
            }
            return subtable?.GetMap();
        }

        [Benchmark]
        [BenchmarkCategory("CMAP", "SIMD")]
        public uint[]? CMAP4_GetMap_Optimized()
        {
            if (_optimizedCmapFormat4 == null) return null;
            var subtable = _optimizedCmapFormat4.GetSubtable(3, 1); // Platform=3 (Windows), Encoding=1 (Unicode BMP)
            if (subtable == null || subtable.format != 4)
            {
                // 尝试遍历所有子表寻找 Format4
                for (uint plat = 0; plat < 10; plat++)
                {
                    for (uint enc = 0; enc < 10; enc++)
                    {
                        var st = _optimizedCmapFormat4.GetSubtable((ushort)plat, (ushort)enc);
                        if (st?.format == 4)
                        {
                            subtable = st;
                            break;
                        }
                    }
                    if (subtable?.format == 4) break;
                }
            }
            return subtable?.GetMap();
        }

        /// <summary>
        /// CMAP6 Format6.GetMap() - 稠密映射子表
        /// 优化：使用 SIMD 批量读取 batchSize=64 个字符映射
        /// </summary>
        [Benchmark]
        [BenchmarkCategory("CMAP", "Baseline")]
        public uint[]? CMAP6_GetMap_Baseline()
        {
            if (_baselineCmapFormat6 == null) return null;
            // Format6 通常是 Platform=1 (Macintosh), Encoding=0 (Roman)
            var subtable = _baselineCmapFormat6.GetSubtable(1, 0);
            if (subtable?.format != 6)
            {
                // 尝试遍历所有子表寻找 Format6
                for (uint plat = 0; plat < 10; plat++)
                {
                    for (uint enc = 0; enc < 10; enc++)
                    {
                        var st = _baselineCmapFormat6.GetSubtable((ushort)plat, (ushort)enc);
                        if (st?.format == 6)
                        {
                            subtable = st;
                            break;
                        }
                    }
                    if (subtable?.format == 6) break;
                }
            }
            return subtable?.GetMap();
        }

        [Benchmark]
        [BenchmarkCategory("CMAP", "SIMD")]
        public uint[]? CMAP6_GetMap_Optimized()
        {
            if (_optimizedCmapFormat6 == null) return null;
            // Format6 通常是 Platform=1 (Macintosh), Encoding=0 (Roman)
            var subtable = _optimizedCmapFormat6.GetSubtable(1, 0);
            if (subtable?.format != 6)
            {
                // 尝试遍历所有子表寻找 Format6
                for (uint plat = 0; plat < 10; plat++)
                {
                    for (uint enc = 0; enc < 10; enc++)
                    {
                        var st = _optimizedCmapFormat6.GetSubtable((ushort)plat, (ushort)enc);
                        if (st?.format == 6)
                        {
                            subtable = st;
                            break;
                        }
                    }
                    if (subtable?.format == 6) break;
                }
            }
            return subtable?.GetMap();
        }

        /// <summary>
        /// CMAP0 Format0.GetMap() - 单字节字符映射（256字符）
        /// 优化：使用 SIMD Vector<byte> 批量读取
        /// </summary>
        [Benchmark]
        [BenchmarkCategory("CMAP", "Baseline")]
        public uint[]? CMAP0_GetMap_Baseline()
        {
            if (_baselineCmapFormat0 == null) return null;
            // Format0 通常是 Platform=1 (Macintosh), Encoding=0 (Roman)
            var subtable = _baselineCmapFormat0.GetSubtable(1, 0);
            if (subtable?.format != 0)
            {
                // 尝试遍历所有子表寻找 Format0
                for (uint plat = 0; plat < 10; plat++)
                {
                    for (uint enc = 0; enc < 10; enc++)
                    {
                        var st = _baselineCmapFormat0.GetSubtable((ushort)plat, (ushort)enc);
                        if (st?.format == 0)
                        {
                            subtable = st;
                            break;
                        }
                    }
                    if (subtable?.format == 0) break;
                }
            }
            return subtable?.GetMap();
        }

        [Benchmark]
        [BenchmarkCategory("CMAP", "SIMD")]
        public uint[]? CMAP0_GetMap_Optimized()
        {
            if (_optimizedCmapFormat0 == null) return null;
            // Format0 通常是 Platform=1 (Macintosh), Encoding=0 (Roman)
            var subtable = _optimizedCmapFormat0.GetSubtable(1, 0);
            if (subtable?.format != 0)
            {
                // 尝试遍历所有子表寻找 Format0
                for (uint plat = 0; plat < 10; plat++)
                {
                    for (uint enc = 0; enc < 10; enc++)
                    {
                        var st = _optimizedCmapFormat0.GetSubtable((ushort)plat, (ushort)enc);
                        if (st?.format == 0)
                        {
                            subtable = st;
                            break;
                        }
                    }
                    if (subtable?.format == 0) break;
                }
            }
            return subtable?.GetMap();
        }

        /// <summary>
        /// CMAP12 Format12.GetMap() - Unicode Full Repertoire 子表
        /// 优化：使用 SIMD 批量读取 batchSize=64 个字符映射
        /// </summary>
        [Benchmark]
        [BenchmarkCategory("CMAP", "Baseline")]
        public uint[]? CMAP12_GetMap_Baseline()
        {
            if (_baselineCmapFormat12 == null) return null;
            // Format12 通常是 Platform=3 (Windows), Encoding=10 (Unicode Full Repertoire)
            var subtable = _baselineCmapFormat12.GetSubtable(3, 10);
            if (subtable?.format != 12)
            {
                // 尝试 Platform=0 (Unicode)
                subtable = _baselineCmapFormat12.GetSubtable(0, 4);
                if (subtable?.format != 12)
                {
                    // 遍历所有子表寻找 Format12
                    for (uint plat = 0; plat < 10; plat++)
                    {
                        for (uint enc = 0; enc < 20; enc++)
                        {
                            var st = _baselineCmapFormat12.GetSubtable((ushort)plat, (ushort)enc);
                            if (st?.format == 12)
                            {
                                subtable = st;
                                break;
                            }
                        }
                        if (subtable?.format == 12) break;
                    }
                }
            }
            return subtable?.GetMap();
        }

        [Benchmark]
        [BenchmarkCategory("CMAP", "SIMD")]
        public uint[]? CMAP12_GetMap_Optimized()
        {
            if (_optimizedCmapFormat12 == null) return null;
            // Format12 通常是 Platform=3 (Windows), Encoding=10 (Unicode Full Repertoire)
            var subtable = _optimizedCmapFormat12.GetSubtable(3, 10);
            if (subtable?.format != 12)
            {
                // 尝试 Platform=0 (Unicode)
                subtable = _optimizedCmapFormat12.GetSubtable(0, 4);
                if (subtable?.format != 12)
                {
                    // 遍历所有子表寻找 Format12
                    for (uint plat = 0; plat < 10; plat++)
                    {
                        for (uint enc = 0; enc < 20; enc++)
                        {
                            var st = _optimizedCmapFormat12.GetSubtable((ushort)plat, (ushort)enc);
                            if (st?.format == 12)
                            {
                                subtable = st;
                                break;
                            }
                        }
                        if (subtable?.format == 12) break;
                    }
                }
            }
            return subtable?.GetMap();
        }

        #endregion

        #region 6. TTCHeader DirectoryOffsets - SIMD 批量偏移量读取 (commit f2d23f4)

        /// <summary>
        /// TTCHeader DirectoryOffsets 批量读取
        /// 优化：使用 SIMD 批量读取 batchSize=4 个目录偏移量
        /// </summary>
        [Benchmark]
        [BenchmarkCategory("TTCHeader", "Baseline")]
        public List<uint>? TTCHeader_DirectoryOffsets_Baseline()
        {
            return _baselineTTCHeader?.DirectoryOffsets;
        }

        [Benchmark]
        [BenchmarkCategory("TTCHeader", "SIMD")]
        public List<uint>? TTCHeader_DirectoryOffsets_Optimized()
        {
            return _optimizedTTCHeader?.DirectoryOffsets;
        }

        #endregion

        #region 7. Table_VORG GetAllVertOriginYMetrics - SIMD 批量读取 (commit f2d23f4)

        /// <summary>
        /// Table_VORG GetAllVertOriginYMetrics 批量读取
        /// 优化：使用 SIMD 批量读取 batchSize=8 个垂直原点度量
        /// 注意：返回类型是 vertOriginYMetrics[] 数组，不是 List
        /// </summary>
        [Benchmark]
        [BenchmarkCategory("Table_VORG", "Baseline")]
        public Baseline.Table_VORG.vertOriginYMetrics[]? TableVORG_GetAllVertOriginYMetrics_Baseline()
        {
            return _baselineVORG?.GetAllVertOriginYMetrics();
        }

        [Benchmark]
        [BenchmarkCategory("Table_VORG", "SIMD")]
        public Table_VORG.vertOriginYMetrics[]? TableVORG_GetAllVertOriginYMetrics_Optimized()
        {
            return _optimizedVORG?.GetAllVertOriginYMetrics();
        }

        #endregion

        #region 辅助方法

        private void FillBuffers(MBOBuffer optBuf, Baseline.MBOBuffer baselineBuf, uint size)
        {
            for (uint i = 0; i < size; i++)
            {
                var value = (byte)(i & 0xFF);
                optBuf.GetBuffer()[(int)i] = value;
                baselineBuf.GetBuffer()[(int)i] = value;
            }
        }

        #endregion
    }
}
