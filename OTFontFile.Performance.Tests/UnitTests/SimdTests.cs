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
            uint[] smallSizes = { 0, 1, 4, 7, 16, 63, 100, 256, 399 };

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
    }
}
