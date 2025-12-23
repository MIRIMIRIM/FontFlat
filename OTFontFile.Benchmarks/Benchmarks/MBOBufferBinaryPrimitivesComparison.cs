using BenchmarkDotNet.Attributes;
using OTFontFile;
using System;

namespace OTFontFile.Benchmarks.Benchmarks
{
    /// <summary>
    /// MBOBuffer 原始方法 vs BinaryPrimitives 扩展方法性能对比
    /// Phase 1 性能优化验证
    /// </summary>
    [MemoryDiagnoser]
    [SimpleJob(warmupCount: 2, iterationCount: 5)]
    public class MBOBufferBinaryPrimitivesComparison
    {
        private const int BufferSize = 1000; // 1KB 测试缓冲区
        private MBOBuffer? _buffer;

        [GlobalSetup]
        public void Setup()
        {
            _buffer = new MBOBuffer((uint)BufferSize);
            // 预填充一些测试数据
            for (uint i = 0; i < BufferSize; i++)
            {
                _buffer!.SetByte((byte)(i & 0xFF), i);
            }
        }

        #region GetShort / GetShortEx 对比

        /// <summary>
        /// 原始方法：使用手动位操作
        /// </summary>
        [Benchmark(Baseline = true)]
        public short GetShort_Sequencial_Original()
        {
            short sum = 0;
            for (uint i = 0; i < BufferSize; i += 2)
            {
                sum += _buffer!.GetShort(i);
            }
            return sum;
        }

        /// <summary>
        /// 优化方法：使用 BinaryPrimitives
        /// </summary>
        [Benchmark]
        public short GetShort_Sequencial_BinaryPrimitives()
        {
            short sum = 0;
            for (uint i = 0; i < BufferSize; i += 2)
            {
                sum += _buffer!.GetShortEx(i);
            }
            return sum;
        }

        #endregion

        #region GetUshort / GetUshortEx 对比

        [Benchmark]
        public ushort GetUshort_Sequencial_Original()
        {
            ushort sum = 0;
            for (uint i = 0; i < BufferSize; i += 2)
            {
                sum += _buffer!.GetUshort(i);
            }
            return sum;
        }

        [Benchmark]
        public ushort GetUshort_Sequencial_BinaryPrimitives()
        {
            ushort sum = 0;
            for (uint i = 0; i < BufferSize; i += 2)
            {
                sum += _buffer!.GetUshortEx(i);
            }
            return sum;
        }

        #endregion

        #region GetInt / GetIntEx 对比

        [Benchmark]
        public int GetInt_Sequencial_Original()
        {
            int sum = 0;
            for (uint i = 0; i < BufferSize; i += 4)
            {
                sum += _buffer!.GetInt(i);
            }
            return sum;
        }

        [Benchmark]
        public int GetInt_Sequencial_BinaryPrimitives()
        {
            int sum = 0;
            for (uint i = 0; i < BufferSize; i += 4)
            {
                sum += _buffer!.GetIntEx(i);
            }
            return sum;
        }

        #endregion

        #region GetUint / GetUintEx 对比

        [Benchmark]
        public uint GetUint_Sequencial_Original()
        {
            uint sum = 0;
            for (uint i = 0; i < BufferSize; i += 4)
            {
                sum += _buffer!.GetUint(i);
            }
            return sum;
        }

        [Benchmark]
        public uint GetUint_Sequencial_BinaryPrimitives()
        {
            uint sum = 0;
            for (uint i = 0; i < BufferSize; i += 4)
            {
                sum += _buffer!.GetUintEx(i);
            }
            return sum;
        }

        #endregion

        #region SetShort / SetShortEx 对比

        [Benchmark]
        public void SetShort_Sequencial_Original()
        {
            for (uint i = 0; i < BufferSize; i += 2)
            {
                _buffer!.SetShort((short)i, i);
            }
        }

        [Benchmark]
        public void SetShort_Sequencial_BinaryPrimitives()
        {
            for (uint i = 0; i < BufferSize; i += 2)
            {
                _buffer!.SetShortEx((short)i, i);
            }
        }

        #endregion

        #region SetUshort / SetUshortEx 对比

        [Benchmark]
        public void SetUshort_Sequencial_Original()
        {
            for (uint i = 0; i < BufferSize; i += 2)
            {
                _buffer!.SetUshort((ushort)i, i);
            }
        }

        [Benchmark]
        public void SetUshort_Sequencial_BinaryPrimitives()
        {
            for (uint i = 0; i < BufferSize; i += 2)
            {
                _buffer!.SetUshortEx((ushort)i, i);
            }
        }

        #endregion

        #region SetInt / SetIntEx 对比

        [Benchmark]
        public void SetInt_Sequencial_Original()
        {
            for (uint i = 0; i < BufferSize; i += 4)
            {
                _buffer!.SetInt((int)i, i);
            }
        }

        [Benchmark]
        public void SetInt_Sequencial_BinaryPrimitives()
        {
            for (uint i = 0; i < BufferSize; i += 4)
            {
                _buffer!.SetIntEx((int)i, i);
            }
        }

        #endregion

        #region SetUint / SetUintEx 对比

        [Benchmark]
        public void SetUint_Sequencial_Original()
        {
            for (uint i = 0; i < BufferSize; i += 4)
            {
                _buffer!.SetUint(i, i);
            }
        }

        [Benchmark]
        public void SetUint_Sequencial_BinaryPrimitives()
        {
            for (uint i = 0; i < BufferSize; i += 4)
            {
                _buffer!.SetUintEx(i, i);
            }
        }

        #endregion

        #region 大块读取对比

        /// <summary>
        /// 原始方法：逐个调用 (假设解析10个int)
        /// </summary>
        [Benchmark]
        public void ReadBlockOfInts_Original()
        {
            for (uint i = 0; i < 10; i++)
            {
                var val = _buffer!.GetInt(i * 4);
            }
        }

        /// <summary>
        /// 优化方法：使用Span<T>零拷贝访问
        /// </summary>
        [Benchmark]
        public void ReadBlockWithSpan()
        {
            var span = _buffer!.GetSpan().Slice(0, 40);
            for (int i = 0; i < 10; i++)
            {
                var spanInt = span.Slice(i * 4, 4);
                var val = spanInt[0] << 24 | spanInt[1] << 16 | spanInt[2] << 8 | spanInt[3];
            }
        }

        #endregion

        #region Span<T> 零拷贝操作

        /// <summary>
        /// 使用 Span<T> 获取ReadOnlySpan - 验证零拷贝
        /// </summary>
        [Benchmark]
        public ReadOnlySpan<byte> GetSpan_ZeroCopy()
        {
            return _buffer!.GetSpan();
        }

        /// <summary>
        /// 使用 Span<T> 获取可写Span
        /// </summary>
        [Benchmark]
        public Span<byte> GetMutableSpan_ZeroCopy()
        {
            return _buffer!.GetMutableSpan();
        }

        #endregion

    }
}
