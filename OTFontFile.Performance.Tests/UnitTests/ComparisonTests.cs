using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Diagnostics;

namespace OTFontFile.Performance.Tests.UnitTests
{
    [TestClass]
    public class ComparisonTests
    {
        private const string TestFontsBasePath = "TestResources/SampleFonts";

        /// <summary>
        /// 对比新旧版本文件加载性能
        /// </summary>
        [TestMethod]
        public void Compare_FileLoadingTime_SmallFont()
        {
            // 预热
            LoadBaselineFontOnce();
            LoadOptimizedFontOnce();
            GC.Collect();
            GC.WaitForPendingFinalizers();

            // 测试原始版本
            var baselineTime = Measure(() => LoadBaselineFontOnce());

            // 测试优化版本
            var optimizedTime = Measure(() => LoadOptimizedFontOnce());

            // 验证性能提升（目标：50%+）
            var improvement = (baselineTime - optimizedTime) / (double)baselineTime;

            // 注意：如果优化版本还没实施，这个测试可能会失败
            Console.WriteLine($"基线加载时间: {baselineTime} ms");
            Console.WriteLine($"优化加载时间: {optimizedTime} ms");
            Console.WriteLine($"性能提升: {improvement:P1}");

            // 在优化完成前，这个测试应该是 Inconclusive
            Assert.Inconclusive("Performance comparison test placeholder - run after optimizations");
        }

        /// <summary>
        /// 对比新旧版本功能正确性
        /// </summary>
        [TestMethod]
        public void Compare_FunctionalCorrectness()
        {
            // 简单的功能对比测试
            var fontPath = GetExistingTestFont();

            if (fontPath == null)
            {
                Assert.Inconclusive("No test font available");
                return;
            }

            // 加载基线版本
            var baselineFile = new Baseline.OTFile();
            baselineFile.open(fontPath);
            var baselineFont = baselineFile.GetFont(0);
            var baselineHead = baselineFont.GetTable("head") as Baseline.Table_head;
            baselineFile.close();

            // 加载优化版本
            var optimizedFile = new OTFontFile.OTFile();
            optimizedFile.open(fontPath);
            var optimizedFont = optimizedFile.GetFont(0);
            var optimizedHead = optimizedFont.GetTable("head") as OTFontFile.Table_head;
            optimizedFile.close();

            // 验证表内容相同
            if (baselineHead != null && optimizedHead != null)
            {
                Assert.AreEqual(baselineHead.unitsPerEm, optimizedHead.unitsPerEm,
                    "unitsPerEm 解析结果不一致");

                Console.WriteLine("✓ Head 表解析验证通过");
            }

            Console.WriteLine("功能正确性对比完成");
        }

        /// <summary>
        /// 测试两个版本都能正常加载字体
        /// </summary>
        [TestMethod]
        public void Verify_BothVersions_CanLoadFont()
        {
            var fontPath = GetExistingTestFont();

            if (fontPath == null)
            {
                Assert.Inconclusive("No test font available");
                return;
            }

            var baselineFile = new Baseline.OTFile();
            var result = baselineFile.open(fontPath);
            Assert.IsTrue(result, "Baseline version failed to open font");
            baselineFile.close();

            var optimizedFile = new OTFontFile.OTFile();
            result = optimizedFile.open(fontPath);
            Assert.IsTrue(result, "Optimized version failed to open font");
            optimizedFile.close();
        }

        #region Helper Methods

        private void LoadBaselineFontOnce()
        {
            var fontPath = GetExistingTestFont();
            if (fontPath != null)
            {
                var file = new Baseline.OTFile();
                file.open(fontPath);
                file.close();
            }
        }

        private void LoadOptimizedFontOnce()
        {
            var fontPath = GetExistingTestFont();
            if (fontPath != null)
            {
                var file = new OTFontFile.OTFile();
                file.open(fontPath);
                file.close();
            }
        }

        private long Measure(Action action)
        {
            var stopwatch = Stopwatch.StartNew();
            action();
            stopwatch.Stop();
            return stopwatch.ElapsedMilliseconds;
        }

        public static string GetExistingTestFont()
        {
            // 尝试查找可用的测试字体
            var path = TestFontsBasePath;

            if (System.IO.Directory.Exists(path))
            {
                var font = System.IO.Directory.GetFiles(path, "*.ttf").FirstOrDefault();
                if (font != null && System.IO.File.Exists(font))
                {
                    return font;
                }
                // 尝试 otf 文件
                font = System.IO.Directory.GetFiles(path, "*.otf").FirstOrDefault();
                if (font != null && System.IO.File.Exists(font))
                {
                    return font;
                }
            }

            return null;
        }

        /// <summary>
        /// 获取指定格式的测试字体
        /// </summary>
        public static string GetTestFontForFormat(string format)
        {
            var path = TestFontsBasePath;
            if (!System.IO.Directory.Exists(path))
                return null;

            // 查找 cmap{format}_font*.otf 文件
            var font = System.IO.Directory.GetFiles(path, $"cmap{format}_font*.*").FirstOrDefault();
            if (font != null && System.IO.File.Exists(font))
            {
                return font;
            }

            return null;
        }

        #endregion
    }
}
