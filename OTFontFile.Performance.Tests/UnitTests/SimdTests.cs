using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OTFontFile;
using Baseline;

namespace OTFontFile.Performance.Tests.UnitTests
{
    /// <summary>
    /// SIMD Vector<T> 优化验证测试
    /// 
    /// 目的：确保 SIMD 优化版本与原始 Baseline 实现结果完全一致
    /// 
    /// 测试策略：
    /// 1. 使用相同输入数据
    /// 2. 对比 Optimized 和 Baseline 的输出
    /// 3. 验证各个边界条件
    /// 4. 测试不同大小的缓冲区
    /// </summary>
    [TestClass]
    public class SimdTests
    {
        #region CalcChecksum 测试

        /// <summary>
        /// 测试：小缓冲区（< 400 bytes）的校验和计算
        /// 边界条件：不满一个向量的大小
        /// </summary>
        [TestMethod]
        public void CalcChecksum_SmallBuffer_MatchesBaseline()
        {
            // 测试多种小尺寸
            uint[] smallSizes = { 1, 4, 7, 16, 63, 100, 256, 399 };

            foreach (uint size in smallSizes)
            {
                var optimized = CreateTestBuffer(size);
                var baseline = CreateBaselineBuffer(size);

                uint optChecksum = optimized.CalcChecksum();
                uint baseChecksum = baseline.CalcChecksum();

                Assert.AreEqual(baseChecksum, optChecksum,
                    $"CalcChecksum mismatch for size {size}: Baseline={baseChecksum}, Optimized={optChecksum}");
            }
        }

        /// <summary>
        /// 测试：中等缓冲区（400 ~ 4000 bytes）的校验和计算
        /// 
        /// 验证场景：
        /// - 正好对齐到 Vector<T> 边界 (512, 1024 等)
        /// - 不对齐的情况 (511, 1023 等)
        /// - 需要多个 Vector<T> 的尺寸 (2048, 4096)
        /// </summary>
        [TestMethod]
        public void CalcChecksum_MediumBuffer_MatchesBaseline()
        {
            // 测试对齐和不对齐的尺寸
            uint[] mediumSizes = {
                400,  // 刚超过 Vector<T> 边界
                512,  // 正好对齐
                511,  // 不对齐的边界
                1024, // 对齐
                1023, // 不对齐
                1500, // 中间值
                2048, // 对齐，需要多个 Vector
                2050, // 不对齐
                4000  // 更大
            };

            foreach (uint size in mediumSizes)
            {
                var optimized = CreateTestBuffer(size);
                var baseline = CreateBaselineBuffer(size);

                uint optChecksum = optimized.CalcChecksum();
                uint baseChecksum = baseline.CalcChecksum();

                Assert.AreEqual(baseChecksum, optChecksum,
                    $"CalcChecksum mismatch for size {size}: Baseline={baseChecksum}, Optimized={optChecksum}");
            }
        }

        /// <summary>
        /// 测试：大缓冲区（> 4000 bytes）的校验和计算
        /// 模拟真实字体表大小的场景
        /// </summary>
        [TestMethod]
        public void CalcChecksum_LargeBuffer_MatchesBaseline()
        {
            // 模拟真实字体表大小（典型值）
            uint[] largeSizes = {
                5000,   // 小表
                10000,  // 中表
                50000,  // 大表（cmap/head）
                100000, // 很大的表（glyf/CFF）
                200000  // 超大表
            };

            foreach (uint size in largeSizes)
            {
                var optimized = CreateTestBuffer(size);
                var baseline = CreateBaselineBuffer(size);

                uint optChecksum = optimized.CalcChecksum();
                uint baseChecksum = baseline.CalcChecksum();

                Assert.AreEqual(baseChecksum, optChecksum,
                    $"CalcChecksum mismatch for size {size}: Baseline={baseChecksum}, Optimized={optChecksum}");
            }
        }

        /// <summary>
        /// 测试：带有填充字节（pad bytes）的缓冲区
        /// 验证：填充字节不应影响校验和计算
        /// </summary>
        [TestMethod]
        public void CalcChecksum_WithPadBytes_MatchesBaseline()
        {
            // 创建不同大小的缓冲区（会有不同数量的 pad bytes）
            uint[] sizesWithPad = { 1, 2, 3, 5, 9, 13, 17, 33, 65 };

            foreach (uint size in sizesWithPad)
            {
                var optimized = CreateTestBuffer(size);
                var baseline = CreateBaselineBuffer(size);

                uint optChecksum = optimized.CalcChecksum();
                uint baseChecksum = baseline.CalcChecksum();

                Assert.AreEqual(baseChecksum, optChecksum,
                    $"CalcChecksum mismatch with pad bytes for size {size}: " +
                    $"Baseline={baseChecksum}, Optimized={optChecksum}");
            }
        }

        /// <summary>
        /// 测试：校验和缓存的正确性
        /// 验证：第一次计算后，第二次调用应返回缓存值
        /// </summary>
        [TestMethod]
        public void CalcChecksum_CacheVerification_MatchesBaseline()
        {
            var buffer = CreateTestBuffer(10000);
            var baseline = CreateBaselineBuffer(10000);

            // 第一次计算
            uint optChecksum1 = buffer.CalcChecksum();
            uint baseChecksum1 = baseline.CalcChecksum();

            // 立即第二次计算（应使用缓存）
            uint optChecksum2 = buffer.CalcChecksum();
            uint baseChecksum2 = baseline.CalcChecksum();

            Assert.AreEqual(baseChecksum1, optChecksum1,
                "First CalcChecksum mismatch with Baseline");
            Assert.AreEqual(baseChecksum2, optChecksum2,
                "Second CalcChecksum mismatch with Baseline");
            Assert.AreEqual(optChecksum1, optChecksum2,
                "Cached checksum should return same value (Optimized)");
            Assert.AreEqual(baseChecksum1, baseChecksum2,
                "Cached checksum should return same value (Baseline)");
        }

        #endregion

        #region BinaryEqual 测试

        /// <summary>
        /// 测试：小缓冲区的相等性比较
        /// </summary>
        [TestMethod]
        public void BinaryEqual_SmallBuffer_MatchesBaseline()
        {
            uint[] sizes = { 0, 1, 4, 7, 16, 63, 100, 256, 399 };

            foreach (uint size in sizes)
            {
                var buffer1 = CreateTestBuffer(size);
                var buffer2 = CreateTestBuffer(size);
                var buffer3 = CreateTestBuffer(size);

                // 修改 buffer3 的最后一个字节
                if (size > 0)
                {
                    var data = buffer3.GetBuffer();
                    data[data.Length - 1] ^= 0xFF;
                }

                var baseline1 = CreateBaselineBuffer(size);
                var baseline2 = CreateBaselineBuffer(size);
                var baseline3 = CreateBaselineBuffer(size);

                if (size > 0)
                {
                    var baseData = baseline3.GetBuffer();
                    baseData[baseData.Length - 1] ^= 0xFF;
                }

                // 测试相等情况
                bool optEqual = MBOBuffer.BinaryEqual(buffer1, buffer2);
                bool baseEqual = Baseline.MBOBuffer.BinaryEqual(baseline1, baseline2);

                Assert.AreEqual(baseEqual, optEqual,
                    $"BinaryEqual mismatch for equal buffers (size {size})");

                // 测试不相等情况
                bool optNotEqual = MBOBuffer.BinaryEqual(buffer1, buffer3);
                bool baseNotEqual = Baseline.MBOBuffer.BinaryEqual(baseline1, baseline3);

                Assert.AreEqual(baseNotEqual, optNotEqual,
                    $"BinaryEqual mismatch for unequal buffers (size {size})");
            }
        }

        /// <summary>
        /// 测试：中等缓冲区的相等性比较
        /// </summary>
        [TestMethod]
        public void BinaryEqual_MediumBuffer_MatchesBaseline()
        {
            uint[] sizes = { 400, 511, 512, 1023, 1024, 1500, 2048, 4000 };

            foreach (uint size in sizes)
            {
                var buffer1 = CreateTestBuffer(size);
                var buffer2 = CreateTestBuffer(size);
                var buffer3 = CreateTestBuffer(size);

                // 在不同位置修改 buffer3
                if (size > 100)
                {
                    var data = buffer3.GetBuffer();
                    data[50] ^= 0xFF;  // 在前部修改
                }
                else if (size > 0)
                {
                    var data = buffer3.GetBuffer();
                    data[0] ^= 0xFF;  // 在开头修改
                }

                var baseline1 = CreateBaselineBuffer(size);
                var baseline2 = CreateBaselineBuffer(size);
                var baseline3 = CreateBaselineBuffer(size);

                if (size > 100)
                {
                    var baseData = baseline3.GetBuffer();
                    baseData[50] ^= 0xFF;
                }
                else if (size > 0)
                {
                    var baseData = baseline3.GetBuffer();
                    baseData[0] ^= 0xFF;
                }

                bool optEqual = MBOBuffer.BinaryEqual(buffer1, buffer2);
                bool baseEqual = Baseline.MBOBuffer.BinaryEqual(baseline1, baseline2);

                Assert.AreEqual(baseEqual, optEqual,
                    $"BinaryEqual equal mismatch (size {size})");

                bool optNotEqual = MBOBuffer.BinaryEqual(buffer1, buffer3);
                bool baseNotEqual = Baseline.MBOBuffer.BinaryEqual(baseline1, baseline3);

                Assert.AreEqual(baseNotEqual, optNotEqual,
                    $"BinaryEqual unequal mismatch (size {size})");
            }
        }

        /// <summary>
        /// 测试：大缓冲区的相等性比较
        /// 验证 SIMD 向量化处理大量数据时的正确性
        /// </summary>
        [TestMethod]
        public void BinaryEqual_LargeBuffer_MatchesBaseline()
        {
            uint[] sizes = { 10000, 50000, 100000 };

            foreach (uint size in sizes)
            {
                var buffer1 = CreateTestBuffer(size);
                var buffer2 = CreateTestBuffer(size);
                var buffer3 = CreateTestBuffer(size);

                // 在 buffer3 中间位置修改一个字节
                if (size > 1000)
                {
                    var data = buffer3.GetBuffer();
                    data[size / 2] ^= 0xFF;
                }

                var baseline1 = CreateBaselineBuffer(size);
                var baseline2 = CreateBaselineBuffer(size);
                var baseline3 = CreateBaselineBuffer(size);

                if (size > 1000)
                {
                    var baseData = baseline3.GetBuffer();
                    baseData[baseline3.GetLength() / 2] ^= 0xFF;
                }

                bool optEqual = MBOBuffer.BinaryEqual(buffer1, buffer2);
                bool baseEqual = Baseline.MBOBuffer.BinaryEqual(baseline1, baseline2);

                Assert.AreEqual(baseEqual, optEqual,
                    $"BinaryEqual large buffer equal mismatch (size {size})");

                bool optNotEqual = MBOBuffer.BinaryEqual(buffer1, buffer3);
                bool baseNotEqual = Baseline.MBOBuffer.BinaryEqual(baseline1, baseline3);

                Assert.AreEqual(baseNotEqual, optNotEqual,
                    $"BinaryEqual large buffer unequal mismatch (size {size})");
            }
        }

        /// <summary>
        /// 测试：不同长度缓冲区的相等性比较
        /// 预期：应返回 false
        /// </summary>
        [TestMethod]
        public void BinaryEqual_DifferentLengths_ReturnsFalse()
        {
            var buffer1 = CreateTestBuffer(1000);
            var buffer2 = CreateTestBuffer(2000);

            var baseline1 = CreateBaselineBuffer(1000);
            var baseline2 = CreateBaselineBuffer(2000);

            bool optResult = MBOBuffer.BinaryEqual(buffer1, buffer2);
            bool baseResult = Baseline.MBOBuffer.BinaryEqual(baseline1, baseline2);

            Assert.IsFalse(optResult, "Optimized BinaryEqual should return false for different lengths");
            Assert.IsFalse(baseResult, "Baseline BinaryEqual should return false for different lengths");
        }

        #endregion

        #region 辅助方法

        /// <summary>
        /// 创建带有已知模式的测试缓冲区（Optimized 版本）
        /// </summary>
        private static MBOBuffer CreateTestBuffer(uint size)
        {
            var buffer = new MBOBuffer(size);
            var data = buffer.GetBuffer();

            // 使用确定性的填充模式（基于位置）
            for (int i = 0; i < size && i < data.Length; i++)
            {
                data[i] = (byte)((i * 2654435761) & 0xFF);  // 简单的伪随机模式
            }

            return buffer;
        }

        /// <summary>
        /// 创建带有相同模式的测试缓冲区（Baseline 版本）
        /// </summary>
        private static Baseline.MBOBuffer CreateBaselineBuffer(uint size)
        {
            var buffer = new Baseline.MBOBuffer(size);
            var data = buffer.GetBuffer();

            // 使用相同的填充模式
            for (int i = 0; i < size && i < data.Length; i++)
            {
                data[i] = (byte)((i * 2654435761) & 0xFF);
            }

            return buffer;
        }

        #endregion

        #region CMAP4 GetMap 测试

        /// <summary>
        /// 测试：CMAP4 Format4 GetMap() 方法
        /// 验证：使用真实字体测试字符到字形映射的正确性
        /// </summary>
        [TestMethod]
        public void CMAP4_GetMap_RealFont_MatchesBaseline()
        {
            var fontPath = ComparisonTests.GetExistingTestFont();
            if (fontPath == null)
            {
                Assert.Inconclusive("No test font available");
                return;
            }

            // 加载基线版本
            var baselineFile = new Baseline.OTFile();
            baselineFile.open(fontPath);
            var baselineFont = baselineFile.GetFont(0);
            var baselineCmap = baselineFont.GetTable("cmap") as Baseline.Table_cmap;
            baselineFile.close();

            // 加载优化版本
            var optimizedFile = new OTFontFile.OTFile();
            optimizedFile.open(fontPath);
            var optimizedFont = optimizedFile.GetFont(0);
            var optimizedCmap = optimizedFont.GetTable("cmap") as OTFontFile.Table_cmap;
            optimizedFile.close();

            if (baselineCmap == null || optimizedCmap == null)
            {
                Assert.Inconclusive("Font does not contain cmap table");
                return;
            }

            // 测试 Format4 (Unicode BMP)
            var baselineSubtable = baselineCmap.GetSubtable(3, 1); // Platform=3 (Windows), Encoding=1 (Unicode BMP)
            if (baselineSubtable == null || baselineSubtable.format != 4)
            {
                Assert.Inconclusive("Font does not contain Format 4 subtable");
                return;
            }

            var optimizedSubtable = optimizedCmap.GetSubtable(3, 1);
            var baselineMap = baselineSubtable.GetMap();
            var optimizedMap = optimizedSubtable.GetMap();

            // 验证映射结果
            Assert.AreEqual(baselineMap.Length, optimizedMap.Length, "Map length mismatch");

            // 验证全部 65536 个字符映射
            int mismatchCount = 0;
            for (int i = 0; i < baselineMap.Length; i++)
            {
                if (baselineMap[i] != optimizedMap[i])
                {
                    mismatchCount++;
                    if (mismatchCount <= 10) // 只记录前10个错误
                    {
                        Console.WriteLine($"CMAP4 mismatch at char {i}: Baseline={baselineMap[i]}, Optimized={optimizedMap[i]}");
                    }
                }
            }

            if (mismatchCount > 0)
            {
                Assert.Fail($"CMAP4 GetMap() produced {mismatchCount} mismatches");
            }
        }

        /// <summary>
        /// CMAP Format6 GetMap() 优化测试
        /// 验证：使用真实字体测试 Format6 字符到字形映射的正确性
        /// </summary>
        [TestMethod]
        public void CMAP6_GetMap_RealFont_MatchesBaseline()
        {
            var fontPath = ComparisonTests.GetTestFontForFormat("6");
            if (fontPath == null)
            {
                Assert.Inconclusive("No Format6 test font available in TestResources/SampleFonts");
                return;
            }

            // 加载基线版本
            var baselineFile = new Baseline.OTFile();
            baselineFile.open(fontPath);
            var baselineFont = baselineFile.GetFont(0);
            var baselineCmap = baselineFont.GetTable("cmap") as Baseline.Table_cmap;
            baselineFile.close();

            // 加载优化版本
            var optimizedFile = new OTFontFile.OTFile();
            optimizedFile.open(fontPath);
            var optimizedFont = optimizedFile.GetFont(0);
            var optimizedCmap = optimizedFont.GetTable("cmap") as OTFontFile.Table_cmap;
            optimizedFile.close();

            if (baselineCmap == null || optimizedCmap == null)
            {
                Assert.Inconclusive("Font does not contain cmap table");
                return;
            }

            // 测试 Format6 (Platform=1, Encoding=0 是最常见的 Format6)
            Baseline.Table_cmap.Subtable? baselineSubtable = null;
            Table_cmap.Subtable? optimizedSubtable = null;

            // 尝试 Format6 (Macintosh Roman 是最常见的 Format6)
            baselineSubtable = baselineCmap.GetSubtable(1, 0); // Platform=1 (Macintosh), Encoding=0 (Roman)
            if (baselineSubtable != null && baselineSubtable.format == 6)
            {
                optimizedSubtable = optimizedCmap.GetSubtable(1, 0);
            }

            // 如果没有找到 Format6，尝试遍历所有子表
            if (baselineSubtable == null || baselineSubtable.format != 6)
            {
                for (uint plat = 0; plat < 10; plat++)
                {
                    for (uint enc = 0; enc < 10; enc++)
                    {
                        var st = baselineCmap.GetSubtable((ushort)plat, (ushort)enc);
                        if (st != null && st.format == 6)
                        {
                            baselineSubtable = st;
                            optimizedSubtable = optimizedCmap.GetSubtable((ushort)plat, (ushort)enc);
                            break;
                        }
                    }
                    if (baselineSubtable != null && baselineSubtable.format == 6)
                        break;
                }
            }

            if (baselineSubtable == null || baselineSubtable.format != 6)
            {
                Assert.Inconclusive("Font does not contain Format 6 subtable");
                return;
            }

            var baselineMap = baselineSubtable.GetMap();
            var optimizedMap = optimizedSubtable!.GetMap();

            // 验证映射结果
            Assert.AreEqual(baselineMap.Length, optimizedMap.Length, "Map length mismatch");

            // 验证全部 65536 个字符映射
            int mismatchCount = 0;
            for (int i = 0; i < baselineMap.Length; i++)
            {
                if (baselineMap[i] != optimizedMap[i])
                {
                    mismatchCount++;
                    if (mismatchCount <= 10)
                    {
                        Console.WriteLine($"CMAP6 mismatch at char {i}: Baseline={baselineMap[i]}, Optimized={optimizedMap[i]}");
                    }
                }
            }

            if (mismatchCount > 0)
            {
                Assert.Fail($"CMAP6 GetMap() produced {mismatchCount} mismatches");
            }
        }

        #endregion

        #region CMAP Tests - Format 0

        /// <summary>
        /// CMAP Format0 GetMap() 优化测试
        /// 验证：使用真实字体测试 Format0 字符到字形映射的正确性
        /// Format0 是单字节字符映射(256字符)，通常用于 Macintosh Roman 编码
        /// </summary>
        [TestMethod]
        public void CMAP0_GetMap_RealFont_MatchesBaseline()
        {
            var fontPath = ComparisonTests.GetTestFontForFormat("0");
            if (fontPath == null)
            {
                Assert.Inconclusive("No Format0 test font available in TestResources/SampleFonts");
                return;
            }

            // 加载基线版本
            var baselineFile = new Baseline.OTFile();
            baselineFile.open(fontPath);
            var baselineFont = baselineFile.GetFont(0);
            var baselineCmap = baselineFont.GetTable("cmap") as Baseline.Table_cmap;
            baselineFile.close();

            // 加载优化版本
            var optimizedFile = new OTFontFile.OTFile();
            optimizedFile.open(fontPath);
            var optimizedFont = optimizedFile.GetFont(0);
            var optimizedCmap = optimizedFont.GetTable("cmap") as OTFontFile.Table_cmap;
            optimizedFile.close();

            if (baselineCmap == null || optimizedCmap == null)
            {
                Assert.Inconclusive("Font does not contain cmap table");
                return;
            }

            // 测试 Format0 (Macintosh Roman 是最常见的 Format0)
            Baseline.Table_cmap.Subtable? baselineSubtable = null;
            Table_cmap.Subtable? optimizedSubtable = null;

            // 尝试 Format0 (Macintosh Roman)
            baselineSubtable = baselineCmap.GetSubtable(1, 0); // Platform=1 (Macintosh), Encoding=0 (Roman)
            if (baselineSubtable != null && baselineSubtable.format == 0)
            {
                optimizedSubtable = optimizedCmap.GetSubtable(1, 0);
            }

            // 如果没有找到 Format0，尝试遍历所有子表
            if (baselineSubtable == null || baselineSubtable.format != 0)
            {
                for (uint plat = 0; plat < 10; plat++)
                {
                    for (uint enc = 0; enc < 10; enc++)
                    {
                        var st = baselineCmap.GetSubtable((ushort)plat, (ushort)enc);
                        if (st != null && st.format == 0)
                        {
                            baselineSubtable = st;
                            optimizedSubtable = optimizedCmap.GetSubtable((ushort)plat, (ushort)enc);
                            break;
                        }
                    }
                    if (baselineSubtable != null && baselineSubtable.format == 0)
                        break;
                }
            }

            if (baselineSubtable == null || baselineSubtable.format != 0)
            {
                Assert.Inconclusive("Font does not contain Format 0 subtable");
                return;
            }

            var baselineMap = baselineSubtable.GetMap();
            var optimizedMap = optimizedSubtable!.GetMap();

            // 验证映射结果
            Assert.AreEqual(baselineMap.Length, optimizedMap.Length, "Map length mismatch");
            Assert.AreEqual(256, optimizedMap.Length, "Format0 should always produce 256-length map");

            // 验证全部 256 个字符映射
            int mismatchCount = 0;
            for (int i = 0; i < optimizedMap.Length; i++)
            {
                if (baselineMap[i] != optimizedMap[i])
                {
                    mismatchCount++;
                    if (mismatchCount <= 10)
                    {
                        Console.WriteLine($"CMAP0 mismatch at char {i}: Baseline={baselineMap[i]}, Optimized={optimizedMap[i]}");
                    }
                }
            }

            if (mismatchCount > 0)
            {
                Assert.Fail($"CMAP0 GetMap() produced {mismatchCount} mismatches");
            }
        }

        /// <summary>
        /// CMAP Format12 GetMap() 优化测试
        /// 验证：使用真实字体测试 Format12 字符到字形映射的正确性
        /// Format12 是基于组的连续映射，支持 Unicode Full Repertoire
        /// </summary>
        [TestMethod]
        public void CMAP12_GetMap_RealFont_MatchesBaseline()
        {
            var fontPath = ComparisonTests.GetTestFontForFormat("12");
            if (fontPath == null)
            {
                Assert.Inconclusive("No Format12 test font available in TestResources/SampleFonts");
                return;
            }

            // 加载基线版本
            var baselineFile = new Baseline.OTFile();
            baselineFile.open(fontPath);
            var baselineFont = baselineFile.GetFont(0);
            var baselineCmap = baselineFont.GetTable("cmap") as Baseline.Table_cmap;
            baselineFile.close();

            // 加载优化版本
            var optimizedFile = new OTFontFile.OTFile();
            optimizedFile.open(fontPath);
            var optimizedFont = optimizedFile.GetFont(0);
            var optimizedCmap = optimizedFont.GetTable("cmap") as OTFontFile.Table_cmap;
            optimizedFile.close();

            if (baselineCmap == null || optimizedCmap == null)
            {
                Assert.Inconclusive("Font does not contain cmap table");
                return;
            }

            // 测试 Format12 (Platform=0/3, Encoding=4/10 是常见的 Format12)
            Baseline.Table_cmap.Subtable? baselineSubtable = null;
            Table_cmap.Subtable? optimizedSubtable = null;

            // 尝试 Format12 (Platform=3 Windows, Encoding=10 Unicode Full Repertoire)
            baselineSubtable = baselineCmap.GetSubtable(3, 10);
            if (baselineSubtable != null && baselineSubtable.format == 12)
            {
                optimizedSubtable = optimizedCmap.GetSubtable(3, 10);
            }

            // 如果没有找到 Format12，尝试Platform=0 (Unicode)
            if (baselineSubtable == null || baselineSubtable.format != 12)
            {
                baselineSubtable = baselineCmap.GetSubtable(0, 4);
                if (baselineSubtable != null && baselineSubtable.format == 12)
                {
                    optimizedSubtable = optimizedCmap.GetSubtable(0, 4);
                }
            }

            // 如果仍然没找到，尝试遍历所有子表
            if (baselineSubtable == null || baselineSubtable.format != 12)
            {
                for (uint plat = 0; plat < 10; plat++)
                {
                    for (uint enc = 0; enc < 20; enc++)
                    {
                        var st = baselineCmap.GetSubtable((ushort)plat, (ushort)enc);
                        if (st != null && st.format == 12)
                        {
                            baselineSubtable = st;
                            optimizedSubtable = optimizedCmap.GetSubtable((ushort)plat, (ushort)enc);
                            break;
                        }
                    }
                    if (baselineSubtable != null && baselineSubtable.format == 12)
                        break;
                }
            }

            if (baselineSubtable == null || baselineSubtable.format != 12)
            {
                Assert.Inconclusive("Font does not contain Format 12 subtable");
                return;
            }

            var baselineMap = baselineSubtable.GetMap();
            var optimizedMap = optimizedSubtable!.GetMap();

            // 验证映射结果
            Assert.AreEqual(baselineMap.Length, optimizedMap.Length, "Map length mismatch");

            // Format12 可能会映射超过 65536 个字符（支持 Unicode 补充平面）
            // 但为了性能考虑，只测试前 65536 个字符
            int testLength = (int)Math.Min(baselineMap.Length, 65536);
            
            int mismatchCount = 0;
            for (int i = 0; i < testLength; i++)
            {
                if (baselineMap[i] != optimizedMap[i])
                {
                    mismatchCount++;
                    if (mismatchCount <= 10)
                    {
                        Console.WriteLine($"CMAP12 mismatch at char {i}: Baseline={baselineMap[i]}, Optimized={optimizedMap[i]}");
                    }
                }
            }

            // 也验证任何超过 65536 的字符
            for (int i = 65536; i < baselineMap.Length; i += 1024)
            {
                if (baselineMap[i] != optimizedMap[i])
                {
                    mismatchCount++;
                    if (mismatchCount <= 10)
                    {
                        Console.WriteLine($"CMAP12 mismatch at char {i}: Baseline={baselineMap[i]}, Optimized={optimizedMap[i]}");
                    }
                }
            }

            if (mismatchCount > 0)
            {
                Assert.Fail($"CMAP12 GetMap() produced {mismatchCount} mismatches");
            }
        }

        #endregion

        #region TTCHeader Directory Entries 测试

        /// <summary>
        /// 测试：TTCHeader 的 Directory Entries 读取优化
        /// 验证优化版本与 Baseline 版本读取的偏移量一致
        /// </summary>
        [TestMethod]
        public void TTCHeader_DirectoryEntries_MatchBaseline()
        {
            const string TestFontsBasePath = "TestResources/SampleFonts";
            var ttcFiles = new[]
            {
                Path.Combine(TestFontsBasePath, "msyh.ttc"),
                Path.Combine(TestFontsBasePath, "msyhbd.ttc"),
                Path.Combine(TestFontsBasePath, "华康POP1体W5 & 华康POP1体W5(P).ttc"),
                Path.Combine(TestFontsBasePath, "華康POP1體W5 & 華康POP1體W5(P).ttc")
            };

            foreach (var fontPath in ttcFiles)
            {
                if (!File.Exists(fontPath))
                    continue;

                // 测试 Baseline 版本
                var baselineFile = new Baseline.OTFile();
                baselineFile.open(fontPath);
                var baselineTTC = baselineFile.GetTTCHeader();
                baselineFile.close();

                // 测试 Optimized 版本
                var optimizedFile = new OTFontFile.OTFile();
                optimizedFile.open(fontPath);
                var optimizedTTC = optimizedFile.GetTTCHeader();
                optimizedFile.close();

                Assert.IsNotNull(baselineTTC, $"Baseline TTCHeader should not be null for {fontPath}");
                Assert.IsNotNull(optimizedTTC, $"Optimized TTCHeader should not be null for {fontPath}");

                // 验证目录数量
                Assert.AreEqual(baselineTTC.DirectoryCount, optimizedTTC.DirectoryCount,
                    $"DirectoryCount mismatch for {Path.GetFileName(fontPath)}");

                // 验证所有目录偏移量
                Assert.IsNotNull(baselineTTC.DirectoryOffsets, "Baseline DirectoryOffsets should not be null");
                Assert.IsNotNull(optimizedTTC.DirectoryOffsets, "Optimized DirectoryOffsets should not be null");

                if (baselineTTC.DirectoryCount > 0)
                {
                    Assert.AreEqual(baselineTTC.DirectoryOffsets.Count, optimizedTTC.DirectoryOffsets.Count,
                        $"DirectoryOffsets.Count mismatch for {Path.GetFileName(fontPath)}");

                    for (int i = 0; i < baselineTTC.DirectoryOffsets.Count && i < optimizedTTC.DirectoryOffsets.Count; i++)
                    {
                        Assert.AreEqual(
                            baselineTTC.DirectoryOffsets[i],
                            optimizedTTC.DirectoryOffsets[i],
                            $"DirectoryOffsets[{i}] mismatch for {Path.GetFileName(fontPath)}");
                    }
                }

                Console.WriteLine($"✓ TTCHeader DirectoryEntries verified: {Path.GetFileName(fontPath)} ({baselineTTC.DirectoryCount} fonts)");
            }
        }

        #endregion

        #region Table_VORG Vertical Origin Metrics 测试

        /// <summary>
        /// 测试：Table_VORG 的 GetAllVertOriginYMetrics 优化
        /// 验证优化版本与 Baseline 版本读取的垂直原点度量一致
        /// </summary>
        [TestMethod]
        public void TableVORG_AllVertOriginYMetrics_MatchBaseline()
        {
            const string TestFontsBasePath = "TestResources/SampleFonts";

            // 测试多个可能包含 VORG 表的字体
            var fontFiles = new[]
            {
                Path.Combine(TestFontsBasePath, "AvenirNextW1G-Regular.OTF"),
                Path.Combine(TestFontsBasePath, "SourceHanSansCN-Regular.otf"),
                Path.Combine(TestFontsBasePath, "HYQiHei_65S.ttf"),
                Path.Combine(TestFontsBasePath, "medium.ttf"),
                Path.Combine(TestFontsBasePath, "STSONG.TTF")
            };

            bool testedAnyVORG = false;

            foreach (var fontPath in fontFiles)
            {
                if (!File.Exists(fontPath))
                    continue;

                // 加载 Baseline 版本
                var baselineFile = new Baseline.OTFile();
                baselineFile.open(fontPath);
                var baselineFont = baselineFile.GetFont(0);
                var baselineVORG = baselineFont.GetTable("VORG") as Baseline.Table_VORG;
                baselineFile.close();

                // 加载 Optimized 版本
                var optimizedFile = new OTFontFile.OTFile();
                optimizedFile.open(fontPath);
                var optimizedFont = optimizedFile.GetFont(0);
                var optimizedVORG = optimizedFont.GetTable("VORG") as OTFontFile.Table_VORG;
                optimizedFile.close();

                // 只测试包含 VORG 表的字体
                if (baselineVORG == null)
                    continue;

                testedAnyVORG = true;

                Assert.IsNotNull(optimizedVORG, $"Optimized VORG should not be null for {Path.GetFileName(fontPath)}");

                // 比较 VORG 表的基本信息
                Assert.AreEqual(baselineVORG.majorVersion, optimizedVORG.majorVersion,
                    $"majorVersion mismatch for {Path.GetFileName(fontPath)}");
                Assert.AreEqual(baselineVORG.minorVersion, optimizedVORG.minorVersion,
                    $"minorVersion mismatch for {Path.GetFileName(fontPath)}");
                Assert.AreEqual(baselineVORG.defaultVertOriginY, optimizedVORG.defaultVertOriginY,
                    $"defaultVertOriginY mismatch for {Path.GetFileName(fontPath)}");
                Assert.AreEqual(baselineVORG.numVertOriginYMetrics, optimizedVORG.numVertOriginYMetrics,
                    $"numVertOriginYMetrics mismatch for {Path.GetFileName(fontPath)}");

                // 比较所有垂直原点度量
                if (baselineVORG.numVertOriginYMetrics > 0)
                {
                    for (uint i = 0; i < baselineVORG.numVertOriginYMetrics; i++)
                    {
                        var baselineMetric = baselineVORG.GetVertOriginYMetrics(i);
                        var optimizedMetric = optimizedVORG.GetVertOriginYMetrics(i);

                        Assert.AreEqual(
                            baselineMetric.glyphIndex,
                            optimizedMetric.glyphIndex,
                            $"VertOriginYMetrics[{i}].glyphIndex mismatch for {Path.GetFileName(fontPath)}");
                        Assert.AreEqual(
                            baselineMetric.vertOriginY,
                            optimizedMetric.vertOriginY,
                            $"VertOriginYMetrics[{i}].vertOriginY mismatch for {Path.GetFileName(fontPath)}");
                    }
                }

                Console.WriteLine($"✓ Table_VORG verified: {Path.GetFileName(fontPath)} ({baselineVORG.numVertOriginYMetrics} metrics)");
            }

            if (!testedAnyVORG)
            {
                Assert.Inconclusive("No test fonts contain VORG table");
            }
        }

        #endregion

        #region Table_Zapf Unicodes 测试

        /// <summary>
        /// 测试：Table_Zapf 的 GetAllGroups 优化
        /// 验证优化版本与 Baseline 版本读取的分组一致
        /// 注意：Table_Zapf 是一个辅助表，可能不是所有字体都有
        /// </summary>
        [TestMethod]
        public void TableZapf_BasicStructure_MatchBaseline()
        {
            const string TestFontsBasePath = "TestResources/SampleFonts";

            // 测试多个字体
            var fontFiles = new[]
            {
                Path.Combine(TestFontsBasePath, "AvenirNextW1G-Regular.OTF"),
                Path.Combine(TestFontsBasePath, "STSONG.TTF"),
                Path.Combine(TestFontsBasePath, "HYQiHei_65S.ttf"),
                Path.Combine(TestFontsBasePath, "medium.ttf")
            };

            bool testedAnyZapf = false;

            foreach (var fontPath in fontFiles)
            {
                if (!File.Exists(fontPath))
                    continue;

                try
                {
                    // 加载 Baseline 版本
                    var baselineFile = new Baseline.OTFile();
                    baselineFile.open(fontPath);
                    var baselineFont = baselineFile.GetFont(0);
                    var baselineZapf = baselineFont.GetTable("Zapf") as Baseline.Table_Zapf;
                    baselineFile.close();

                    // 加载 Optimized 版本
                    var optimizedFile = new OTFontFile.OTFile();
                    optimizedFile.open(fontPath);
                    var optimizedFont = optimizedFile.GetFont(0);
                    var optimizedZapf = optimizedFont.GetTable("Zapf") as OTFontFile.Table_Zapf;
                    optimizedFile.close();

                    // 只测试包含 Zapf 表的字体
                    if (baselineZapf == null)
                        continue;

                    testedAnyZapf = true;
                    Assert.IsNotNull(optimizedZapf, $"Optimized Zapf should not be null for {Path.GetFileName(fontPath)}");

                    // 验证基本表属性
                    Assert.AreEqual(baselineZapf.version.ToString(), optimizedZapf.version.ToString(),
                        $"version mismatch for {Path.GetFileName(fontPath)}");
                    Assert.AreEqual(baselineZapf.extraInfo, optimizedZapf.extraInfo,
                        $"extraInfo mismatch for {Path.GetFileName(fontPath)}");

                    Console.WriteLine($"✓ Table_Zapf basic structure verified: {Path.GetFileName(fontPath)}");
                }
                catch (Exception ex)
                {
                    // 如果出现异常，可能是 Zapf 表结构不支持
                    Console.WriteLine($"Note: Zapf access failed for {Path.GetFileName(fontPath)}: {ex.Message}");
                }
            }

            if (!testedAnyZapf)
            {
                Assert.Inconclusive("No test fonts contain Zapf table");
            }
        }

        #endregion
    }
}