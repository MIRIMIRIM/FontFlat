using BenchmarkDotNet.Attributes;
using OTFontFile;
using Baseline;
using System;
using System.Buffers.Binary;

namespace OTFontFile.Benchmarks.Benchmarks
{
    /// <summary>
    /// MBOBuffer Short/Long 原始实现 vs BinaryPrimitives 优化实现性能对比
    /// 调查为什么 short 优化后会变慢，以及 long 是否有性能提升
    /// </summary>
    [MemoryDiagnoser]
    [SimpleJob(warmupCount: 2, iterationCount: 5)]
    public class MBOBufferShortLongComparison
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

        #region Short (16-bit signed) - GetShort

        /// <summary>
        /// 原始方法：使用手动位操作 - (short)(m_buf[offset]<<8 | m_buf[offset+1])
        /// </summary>
        [Benchmark]
        public short GetShort_Sequencial_Original()
        {
            short sum = 0;
            for (uint i = 0; i < BufferSize; i += 2)
            {
                sum = (short)(sum + _baselineBuffer!.GetShort(i));
            }
            return sum;
        }

        /// <summary>
        /// 优化方法：使用 BinaryPrimitives.ReadInt16BigEndian
        /// 之前测试显示：6% slower
        /// </summary>
        [Benchmark]
        public short GetShort_Sequencial_BinaryPrimitives()
        {
            short sum = 0;
            for (uint i = 0; i < BufferSize; i += 2)
            {
                var span = _optimizedBuffer!.GetSpan();
                var value = BinaryPrimitives.ReadInt16BigEndian(span.Slice((int)i, 2));
                sum = (short)(sum + value);
            }
            return sum;
        }

        #endregion

        #region Short (16-bit signed) - SetShort

        /// <summary>
        /// 原始方法：使用手动位操作 - m_buf[offset  ] = (byte)(value >> 8);
        /// </summary>
        [Benchmark]
        public void SetShort_Sequencial_Original()
        {
            for (uint i = 0; i < BufferSize; i += 2)
            {
                _baselineBuffer!.SetShort((short)i, i);
            }
        }

        /// <summary>
        /// 优化方法：使用 BinaryPrimitives.WriteInt16BigEndian
        /// 之前测试显示：82% slower - 这是主要问题！
        /// </summary>
        [Benchmark]
        public void SetShort_Sequencial_BinaryPrimitives()
        {
            var span = _optimizedBuffer!.GetMutableSpan();
            for (uint i = 0; i < BufferSize; i += 2)
            {
                BinaryPrimitives.WriteInt16BigEndian(span.Slice((int)i, 2), (short)i);
            }
        }

        #endregion

        #region Ushort (16-bit unsigned) - GetUshort

        /// <summary>
        /// 原始方法：使用手动位操作
        /// </summary>
        [Benchmark]
        public ushort GetUshort_Sequencial_Original()
        {
            ushort sum = 0;
            for (uint i = 0; i < BufferSize; i += 2)
            {
                sum = (ushort)(sum + _baselineBuffer!.GetUshort(i));
            }
            return sum;
        }

        /// <summary>
        /// 优化方法：使用 BinaryPrimitives.ReadUInt16BigEndian
        /// 之前测试显示：6% slower
        /// </summary>
        [Benchmark]
        public ushort GetUshort_Sequencial_BinaryPrimitives()
        {
            ushort sum = 0;
            for (uint i = 0; i < BufferSize; i += 2)
            {
                var span = _optimizedBuffer!.GetSpan();
                var value = BinaryPrimitives.ReadUInt16BigEndian(span.Slice((int)i, 2));
                sum = (ushort)(sum + value);
            }
            return sum;
        }

        #endregion

        #region Ushort (16-bit unsigned) - SetUshort

        /// <summary>
        /// 原始方法：使用手动位操作
        /// </summary>
        [Benchmark]
        public void SetUshort_Sequencial_Original()
        {
            for (uint i = 0; i < BufferSize; i += 2)
            {
                _baselineBuffer!.SetUshort((ushort)i, i);
            }
        }

        /// <summary>
        /// 优化方法：使用 BinaryPrimitives.WriteUInt16BigEndian
        /// 之前测试显示：82% slower - 这是主要问题！
        /// </summary>
        [Benchmark]
        public void SetUshort_Sequencial_BinaryPrimitives()
        {
            var span = _optimizedBuffer!.GetMutableSpan();
            for (uint i = 0; i < BufferSize; i += 2)
            {
                BinaryPrimitives.WriteUInt16BigEndian(span.Slice((int)i, 2), (ushort)i);
            }
        }

        #endregion

        #region Long (64-bit signed) - GetLong - 未测试过

        /// <summary>
        /// 原始方法：使用手动位操作
        /// </summary>
        [Benchmark]
        public long GetLong_Sequencial_Original()
        {
            long sum = 0;
            for (uint i = 0; i < BufferSize; i += 8)
            {
                sum += _baselineBuffer!.GetLong(i);
            }
            return sum;
        }

        /// <summary>
        /// 优化方法：使用 BinaryPrimitives.ReadInt64BigEndian
        /// 待验证性能提升
        /// </summary>
        [Benchmark]
        public long GetLong_Sequencial_BinaryPrimitives()
        {
            long sum = 0;
            for (uint i = 0; i < BufferSize; i += 8)
            {
                var span = _optimizedBuffer!.GetSpan();
                var value = BinaryPrimitives.ReadInt64BigEndian(span.Slice((int)i, 8));
                sum += value;
            }
            return sum;
        }

        #endregion

        #region Long (64-bit signed) - SetLong - 未测试过

        /// <summary>
        /// 原始方法：使用手动位操作
        /// </summary>
        [Benchmark]
        public void SetLong_Sequencial_Original()
        {
            for (uint i = 0; i < BufferSize; i += 8)
            {
                _baselineBuffer!.SetLong((long)i, i);
            }
        }

        /// <summary>
        /// 优化方法：使用 BinaryPrimitives.WriteInt64BigEndian
        /// 待验证性能提升
        /// </summary>
        [Benchmark]
        public void SetLong_Sequencial_BinaryPrimitives()
        {
            var span = _optimizedBuffer!.GetMutableSpan();
            for (uint i = 0; i < BufferSize; i += 8)
            {
                BinaryPrimitives.WriteInt64BigEndian(span.Slice((int)i, 8), (long)i);
            }
        }

        #endregion

        #region Ulong (64-bit unsigned) - GetUlong - 未测试过

        /// <summary>
        /// 原始方法：使用手动位操作
        /// </summary>
        [Benchmark]
        public ulong GetUlong_Sequencial_Original()
        {
            ulong sum = 0;
            for (uint i = 0; i < BufferSize; i += 8)
            {
                sum += _baselineBuffer!.GetUlong(i);
            }
            return sum;
        }

        /// <summary>
        /// 优化方法：使用 BinaryPrimitives.ReadUInt64BigEndian
        /// 待验证性能提升
        /// </summary>
        [Benchmark]
        public ulong GetUlong_Sequencial_BinaryPrimitives()
        {
            ulong sum = 0;
            for (uint i = 0; i < BufferSize; i += 8)
            {
                var span = _optimizedBuffer!.GetSpan();
                var value = BinaryPrimitives.ReadUInt64BigEndian(span.Slice((int)i, 8));
                sum += value;
            }
            return sum;
        }

        #endregion

        #region Ulong (64-bit unsigned) - SetUlong - 未测试过

        /// <summary>
        /// 原始方法：使用手动位操作
        /// </summary>
        [Benchmark]
        public void SetUlong_Sequencial_Original()
        {
            for (uint i = 0; i < BufferSize; i += 8)
            {
                _baselineBuffer!.SetUlong(i, i);
            }
        }

        /// <summary>
        /// 优化方法：使用 BinaryPrimitives.WriteUInt64BigEndian
        /// 待验证性能提升
        /// </summary>
        [Benchmark]
        public void SetUlong_Sequencial_BinaryPrimitives()
        {
            var span = _optimizedBuffer!.GetMutableSpan();
            for (uint i = 0; i < BufferSize; i += 8)
            {
                BinaryPrimitives.WriteUInt64BigEndian(span.Slice((int)i, 8), i);
            }
        }

        #endregion
    }
}
