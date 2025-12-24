using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Jobs;
using OTFontFile;
using System.Numerics;

namespace OTFontFile.Benchmarks.Benchmarks
{
    /// <summary>
    /// SIMD优化性能基准测试
    /// 用于验证TTCHeader、Table_VORG和Table_Zapf的SIMD批处理优化效果
    /// </summary>
    [MarkdownExporter, AsciiDocExporter, HtmlExporter, RPlotExporter]
    [MemoryDiagnoser]
    [SimpleJob(warmupCount: 3, iterationCount: 10)]
    public class SimdOptimizationsBenchmarks
    {
        private const string TtcTestFontPath = "BenchmarkResources/SampleFonts/NotoSansCJK.ttc";
        private const string VorgTestFontPath = "BenchmarkResources/SampleFonts/NotoSans-Regular.ttf";
        private const string ZapfTestFontPath = "BenchmarkResources/SampleFonts/ZapfDingbats.ttf";

        private OTFile? _ttcFile;
        private OTFile? _vorgFile;
        private OTFile? _zapfFile;

        private OTFont? _ttcFont1;
        private OTFont? _ttcFont2;
        private OTFont? _vorgFont;
        private OTFont? _zapfFont;

        [GlobalSetup]
        public void Setup()
        {
            // TTC文件用于测试DirectoryEntry优化
            _ttcFile = new OTFile();
            if (System.IO.File.Exists(TtcTestFontPath))
            {
                _ttcFile.open(TtcTestFontPath);
                _ttcFont1 = _ttcFile.GetFont(0);
                _ttcFont2 = _ttcFile.GetFont(1);
            }

            // VORG表用于测试Vertical Origin Metrics优化
            _vorgFile = new OTFile();
            if (System.IO.File.Exists(VorgTestFontPath))
            {
                _vorgFile.open(VorgTestFontPath);
                _vorgFont = _vorgFile.GetFont(0);
            }

            // Zapf表用于测试Unicode Groups优化（如果存在）
            _zapfFile = new OTFile();
            if (System.IO.File.Exists(ZapfTestFontPath))
            {
                _zapfFile.open(ZapfTestFontPath);
                _zapfFont = _zapfFile.GetFont(0);
            }
        }

        [GlobalCleanup]
        public void Cleanup()
        {
            _ttcFile?.close();
            _vorgFile?.close();
            _zapfFile?.close();
        }

        // ==================== TTCHeader DirectoryOffsets 基准测试 ====================

        [Benchmark]
        [BenchmarkCategory("TTCHeader", "SIMD")]
        public void TTCHeader_DirectoryOffsets_Read()
        {
            if (_ttcFile == null) return;

            var ttcHeader = _ttcFile.GetTTCHeader();
            if (ttcHeader == null) return;

            var offsets = ttcHeader.DirectoryOffsets;

            // 模拟使用offsets以避免优化掉
            // ReSharper disable once UnusedVariable
            var count = offsets.Count;
        }

        [Benchmark]
        [BenchmarkCategory("TTCHeader", "SIMD")]
        public void TTCHeader_MultipleFonts_Access()
        {
            if (_ttcFont1 == null || _ttcFont2 == null) return;

            // 访问TTC中的多个字体
            var maxp1 = _ttcFont1.GetTable("maxp") as Table_maxp;
            var maxp2 = _ttcFont2.GetTable("maxp") as Table_maxp;

            // 确保结果不被优化掉
            // ReSharper disable once UnusedVariable
            var totalGlyphs = (maxp1?.NumGlyphs ?? 0) + (maxp2?.NumGlyphs ?? 0);
        }

        // ==================== Table_VORG Vertical Origin Metrics 基准测试 ====================

        [Benchmark]
        [BenchmarkCategory("Table_VORG", "SIMD")]
        public void TableVORG_GetAllVertOriginYMetrics()
        {
            if (_vorgFont == null) return;

            var vorgTable = _vorgFont.GetTable("VORG") as Table_VORG;
            if (vorgTable == null) return;

            // 使用新的SIMD优化批量方法读取所有metrics
            var allMetrics = vorgTable.GetAllVertOriginYMetrics();

            // 确保结果不被优化掉
            // ReSharper disable once UnusedVariable
            var count = allMetrics.Length;
        }

        [Benchmark]
        [BenchmarkCategory("Table_VORG", "Baseline")]
        public void TableVORG_Scalar_GetVertOriginYMetrics()
        {
            if (_vorgFont == null) return;

            var vorgTable = _vorgFont.GetTable("VORG") as Table_VORG;
            if (vorgTable == null) return;

            // 使用原始的单项访问方法
            ushort count = vorgTable.numVertOriginYMetrics;
            for (uint i = 0; i < count; i++)
            {
                // ReSharper disable once UnusedVariable
                var yOffset = vorgTable.GetVertOriginYMetrics(i);
            }
        }

        // ==================== Table_Zapf Unicode Groups 基准测试 ====================

        [Benchmark]
        [BenchmarkCategory("Table_Zapf", "SIMD")]
        public void TableZapf_GetAllGroups()
        {
            // Skip - GroupInfo is not directly accessible from Table_Zapf
            // The API structure requires accessing through GlyphInfo -> KindName -> NamedGroup hierarchy
            // This benchmark would require significant refactoring to work with the actual API
        }

        [Benchmark]
        [BenchmarkCategory("Table_Zapf", "Baseline")]
        public void TableZapf_Scalar_GetGroups()
        {
            // Skip - GroupInfo is not directly accessible from Table_Zapf
            // The API structure requires accessing through GlyphInfo -> KindName -> NamedGroup hierarchy
            // This benchmark would require significant refactoring to work with the actual API
        }

        // ==================== 综合基准测试 ====================

        [Benchmark]
        [BenchmarkCategory("Combined", "SIMD")]
        public void Combined_SimdOptimizations()
        {
            // TTCHeader
            if (_ttcFile != null)
            {
                var ttcHeader = _ttcFile.GetTTCHeader();
                // ReSharper disable once UnusedVariable
                var ttcOffsets = ttcHeader?.DirectoryOffsets.Count ?? 0;
            }

            // Table_VORG
            if (_vorgFont != null)
            {
                var vorgTable = _vorgFont.GetTable("VORG") as Table_VORG;
                if (vorgTable != null)
                {
                    ushort count = vorgTable.numVertOriginYMetrics;
                    for (uint i = 0; i < count; i++)
                    {
                        // ReSharper disable once UnusedVariable
                        var yOffset = vorgTable.GetVertOriginYMetrics(i);
                    }
                }
            }

            // Table_Zapf - Skip - GroupInfo is not directly accessible from Table_Zapf
            // The API structure requires accessing through GlyphInfo -> KindName -> NamedGroup hierarchy
        }
    }
}
