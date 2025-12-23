using BenchmarkDotNet.Attributes;
using OTFontFile;
using Baseline;
using System;

namespace OTFontFile.Benchmarks.Benchmarks
{
    /// <summary>
    /// MBOBuffer 原始实现 vs BinaryPrimitives 优化实现性能对比
    /// Phase 1 性能优化验证 - 仅测试显著提升的方法
    /// 注意：不使用 Baseline 以避免组限制，直接对比 Mean 值
    /// </summary>
    [MemoryDiagnoser]
    [SimpleJob(warmupCount: 2, iterationCount: 5)]
    public class MBOBufferBinaryPrimitivesComparison
    {
        private const int BufferSize = 1000; // 1KB 测试缓冲区
        private OTFontFile.MBOBuffer? _optimizedBuffer;
        private Baseline.MBOBuffer? _baselineBuffer;

        [GlobalSetup]
        public void Setup()
        {
            _optimizedBuffer = new OTFontFile.MBOBuffer((uint)BufferSize);
            _baselineBuffer = new Baseline.MBOBuffer((uint)BufferSize);
            // 预填充一些测试数据
            for (uint i = 0; i < BufferSize; i++)
            {
                var value = (byte)(i & 0xFF);
                _optimizedBuffer!.SetByte(value, i);
                _baselineBuffer!.SetByte(value, i);
            }
        }

        #region GetInt / GetUint 对比 - BinaryPrimitives 提升显著

        /// <summary>
        /// 原始方法：使用手动位操作
        /// </summary>
        [Benchmark]
        public int GetInt_Sequencial_Original()
        {
            int sum = 0;
            for (uint i = 0; i < BufferSize; i += 4)
            {
                sum += _baselineBuffer!.GetInt(i);
            }
            return sum;
        }

        /// <summary>
        /// 优化方法：使用 BinaryPrimitives (已集成到 MBOBuffer.GetInt)
        /// 预期提升：~46%
        /// </summary>
        [Benchmark]
        public int GetInt_Sequencial_BinaryPrimitives()
        {
            int sum = 0;
            for (uint i = 0; i < BufferSize; i += 4)
            {
                sum += _optimizedBuffer!.GetInt(i);
            }
            return sum;
        }

        #endregion

        #region GetUint / GetUint 对比 - BinaryPrimitives 提升显著

        /// <summary>
        /// 原始方法：使用手动位操作
        /// </summary>
        [Benchmark]
        public uint GetUint_Sequencial_Original()
        {
            uint sum = 0;
            for (uint i = 0; i < BufferSize; i += 4)
            {
                sum += _baselineBuffer!.GetUint(i);
            }
            return sum;
        }

        /// <summary>
        /// 优化方法：使用 BinaryPrimitives (已集成到 MBOBuffer.GetUint)
        /// 预期提升：~47%
        /// </summary>
        [Benchmark]
        public uint GetUint_Sequencial_BinaryPrimitives()
        {
            uint sum = 0;
            for (uint i = 0; i < BufferSize; i += 4)
            {
                sum += _optimizedBuffer!.GetUint(i);
            }
            return sum;
        }

        #endregion

        #region SetInt / SetInt 对比 - BinaryPrimitives 提升显著

        /// <summary>
        /// 原始方法：使用手动位操作
        /// </summary>
        [Benchmark]
        public void SetInt_Sequencial_Original()
        {
            for (uint i = 0; i < BufferSize; i += 4)
            {
                _baselineBuffer!.SetInt((int)i, i);
            }
        }

        /// <summary>
        /// 优化方法：使用 BinaryPrimitives (已集成到 MBOBuffer.SetInt)
        /// 预期提升：~44%
        /// </summary>
        [Benchmark]
        public void SetInt_Sequencial_BinaryPrimitives()
        {
            for (uint i = 0; i < BufferSize; i += 4)
            {
                _optimizedBuffer!.SetInt((int)i, i);
            }
        }

        #endregion

        #region SetUint / SetUint 对比 - BinaryPrimitives 提升显著

        /// <summary>
        /// 原始方法：使用手动位操作
        /// </summary>
        [Benchmark]
        public void SetUint_Sequencial_Original()
        {
            for (uint i = 0; i < BufferSize; i += 4)
            {
                _baselineBuffer!.SetUint(i, i);
            }
        }

        /// <summary>
        /// 优化方法：使用 BinaryPrimitives (已集成到 MBOBuffer.SetUint)
        /// 预期提升：~41%
        /// </summary>
        [Benchmark]
        public void SetUint_Sequencial_BinaryPrimitives()
        {
            for (uint i = 0; i < BufferSize; i += 4)
            {
                _optimizedBuffer!.SetUint(i, i);
            }
        }

        #endregion

        #region 大块数据读取对比 - Span&lt;T&gt; 零拷贝提升显著

        /// <summary>
        /// 原始方法：逐个调用 (解析10个int)
        /// </summary>
        [Benchmark]
        public void ReadBlockOfInts_Original()
        {
            for (uint i = 0; i < 10; i++)
            {
                var val = _baselineBuffer!.GetInt(i * 4);
            }
        }

        /// <summary>
        /// 优化方法：使用Span&lt;T&gt;零拷贝访问
        /// 预期提升：~53%
        /// </summary>
        [Benchmark]
        public void ReadBlockWithSpan()
        {
            var span = _optimizedBuffer!.GetSpan();
            int sum = 0;
            for (int i = 0; i < 10; i++)
            {
                var spanInt = span.Slice(i * 4, 4);
                sum += spanInt[0] << 24 | spanInt[1] << 16 | spanInt[2] << 8 | spanInt[3];
            }
        }

        #endregion

        #region Span&lt;T&gt; 零拷贝操作 - 性能最优

        /// <summary>
        /// 使用 Span&lt;T&gt; 获取ReadOnlySpan - 验证零拷贝
        /// 预期性能：~0.019 ns (接近瞬时)
        /// </summary>
        [Benchmark]
        public ReadOnlySpan<byte> GetSpan_ZeroCopy()
        {
            return _optimizedBuffer!.GetSpan();
        }

        /// <summary>
        /// 使用 Span&lt;T&gt; 获取可写Span
        /// 预期性能：~0.008 ns (接近瞬时)
        /// </summary>
        [Benchmark]
        public Span<byte> GetMutableSpan_ZeroCopy()
        {
            return _optimizedBuffer!.GetMutableSpan();
        }

        #endregion
    }
}
