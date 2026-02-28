// 文件暂时禁用 - 等待 OTFontFile 项目编译成功后再启用
// ComparisonBenchmarks 需要同时引用 OTFontFile 和 Baseline 项目
// 由于命名空间冲突问题，暂时禁用此文件
//
// TODO: 启用此文件需要：
// 1. 解决 OTFileFile vs OTFile 命名空间冲突
// 2. 确保两个项目都有完整的命名空间

/*
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;
using OTFileFile;
using Baseline;

namespace OTFileFile.Benchmarks.Benchmarks
{
    /// <summary>
    /// 新旧版本性能对比基准测试
    /// 使用 Baseline = true 标记基线版本方法
    /// BenchmarkDotNet 会自动生成对比报告
    /// </summary>
    [Config(typeof(ComparisonConfig))]
    [MemoryDiagnoser]
    public class ComparisonBenchmarks
    {
        private const string SmallFontPath = "BenchmarkResources/SampleFonts/small.ttf";
        private const string MediumFontPath = "BenchmarkResources/SampleFonts/medium.ttf";

        [Benchmark(Baseline = true)]
        [Arguments(SmallFontPath)]
        public void Baseline_LoadFile(string fontPath)
        {
            var file = new Baseline.OTFile();
            file.open(GetExistingPath(fontPath));
            file.close();
        }

        [Benchmark]
        [Arguments(SmallFontPath)]
        public void Optimized_LoadFile(string fontPath)
        {
            var file = new OTFile.OTFile();
            file.open(GetExistingPath(fontPath));
            file.close();
        }

        private string GetExistingPath(string requestedPath)
        {
            if (System.IO.File.Exists(requestedPath))
                return requestedPath;
            return requestedPath;
        }
    }

    public class ComparisonConfig : ManualConfig
    {
        public ComparisonConfig()
        {
            AddJob(Job.Default.WithIterationCount(10).WithWarmupCount(5));
        }
    }
}
*/
