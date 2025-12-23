using BenchmarkDotNet.Attributes;
using OTFontFile;
using System;
using System.Linq;

namespace OTFontFile.Benchmarks.Benchmarks
{
    /// <summary>
    /// OTFontFile.MBOBuffer 操作性能基准测试（使用优化版本）
    /// </summary>
    [MarkdownExporter, AsciiDocExporter, HtmlExporter, RPlotExporter]
    [MemoryDiagnoser]
    [SimpleJob(warmupCount: 3, iterationCount: 10)]
    public class MBOBufferBenchmarks
    {
        private const int BufferSize = 1000;
        private OTFontFile.MBOBuffer? _buffer;

        [GlobalSetup]
        public void Setup()
        {
            _buffer = new OTFontFile.MBOBuffer((uint)BufferSize);
        }

        // ===== Bytes Tests =====
        [Benchmark]
        [BenchmarkCategory("Read", "Byte")]
        public void ReadByte_Sequential()
        {
            for (uint i = 0; i < BufferSize; i++)
            {
                var b = _buffer!.GetByte(i);
            }
        }

        [Benchmark]
        [BenchmarkCategory("Write", "Byte")]
        public void WriteByte_Sequential()
        {
            for (uint i = 0; i < BufferSize; i++)
            {
                _buffer!.SetByte((byte)i, i);
            }
        }

        // ===== Short Tests =====
        [Benchmark]
        [BenchmarkCategory("Read", "Short")]
        public void ReadShort_Sequential()
        {
            for (uint i = 0; i < BufferSize; i += 2)
            {
                var s = _buffer!.GetShort(i);
            }
        }

        [Benchmark]
        [BenchmarkCategory("Write", "Short")]
        public void WriteShort_Sequential()
        {
            for (uint i = 0; i < BufferSize; i += 2)
            {
                _buffer!.SetShort((short)i, i);
            }
        }

        // ===== UShort Tests =====
        [Benchmark]
        [BenchmarkCategory("Read", "UShort")]
        public void ReadUshort_Sequential()
        {
            for (uint i = 0; i < BufferSize; i += 2)
            {
                var us = _buffer!.GetUshort(i);
            }
        }

        [Benchmark]
        [BenchmarkCategory("Write", "UShort")]
        public void WriteUshort_Sequential()
        {
            for (uint i = 0; i < BufferSize; i += 2)
            {
                _buffer!.SetUshort((ushort)i, i);
            }
        }

        // ===== Int Tests =====
        [Benchmark]
        [BenchmarkCategory("Read", "Int")]
        public void ReadInt_Sequential()
        {
            for (uint i = 0; i < BufferSize; i += 4)
            {
                var n = _buffer!.GetInt(i);
            }
        }

        [Benchmark]
        [BenchmarkCategory("Write", "Int")]
        public void WriteInt_Sequential()
        {
            for (uint i = 0; i < BufferSize; i += 4)
            {
                _buffer!.SetInt((int)i, i);
            }
        }

        // ===== UInt Tests =====
        [Benchmark]
        [BenchmarkCategory("Read", "UInt")]
        public void ReadUint_Sequential()
        {
            for (uint i = 0; i < BufferSize; i += 4)
            {
                var n = _buffer!.GetUint(i);
            }
        }

        [Benchmark]
        [BenchmarkCategory("Write", "UInt")]
        public void WriteUint_Sequential()
        {
            for (uint i = 0; i < BufferSize; i += 4)
            {
                _buffer!.SetUint(i, i);
            }
        }

        // ===== Static Conversion Methods Tests =====
        [Benchmark]
        [BenchmarkCategory("Static", "Read")]
        public void StaticGetMBOshort_MassiveCalls()
        {
            byte[] testArray = Enumerable.Repeat((byte)0x12, 10000).ToArray();
            for (int i = 0; i < testArray.Length - 1; i += 2)
            {
                var result = OTFontFile.MBOBuffer.GetMBOshort(testArray, (uint)i);
            }
        }

        [Benchmark]
        [BenchmarkCategory("Static", "Read")]
        public void StaticGetMBOushort_MassiveCalls()
        {
            byte[] testArray = Enumerable.Repeat((byte)0x12, 10000).ToArray();
            for (int i = 0; i < testArray.Length - 1; i += 2)
            {
                var result = OTFontFile.MBOBuffer.GetMBOushort(testArray, (uint)i);
            }
        }

        [Benchmark]
        [BenchmarkCategory("Static", "Read")]
        public void StaticGetMBOint_MassiveCalls()
        {
            byte[] testArray = Enumerable.Repeat((byte)0x12, 10000).ToArray();
            for (int i = 0; i < testArray.Length - 3; i += 4)
            {
                var result = OTFontFile.MBOBuffer.GetMBOint(testArray, (uint)i);
            }
        }

        [Benchmark]
        [BenchmarkCategory("Static", "Read")]
        public void StaticGetMBOuint_MassiveCalls()
        {
            byte[] testArray = Enumerable.Repeat((byte)0x12, 10000).ToArray();
            for (int i = 0; i < testArray.Length - 3; i += 4)
            {
                var result = OTFontFile.MBOBuffer.GetMBOuint(testArray, (uint)i);
            }
        }

        [GlobalCleanup]
        public void Cleanup()
        {
            _buffer = null;
        }
    }
}

