using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using System;
using System.Collections.Generic;

namespace OTFontFile.Benchmarks.Benchmarks
{
    /// <summary>
    /// BigUn vs Rune 性能基准测试
    /// 评估替换后的性能影响
    /// </summary>
    [MemoryDiagnoser]
    [SimpleJob(warmupCount: 3, iterationCount: 20)]
    public class BigUnRuneBenchmarks
    {
        #region 测试数据

        private List<char> asciiChars;
        private List<char> cjkChars;
        private List<(char high, char low)> surrogatePairs;
        private List<uint> codePoints;
        private BigUnWrapper[] bigUnArray;
        private System.Text.Rune[] runeArray;

        #endregion

        #region BigUn 包装器（用于基准测试）

        // 由于不能直接实例化 Baseline.BigUn（它是 struct），这里使用包装器
        private struct BigUnWrapper
        {
            private readonly uint m_char32;

            public BigUnWrapper(char c)
            {
                m_char32 = c;
            }

            public BigUnWrapper(uint char32)
            {
                m_char32 = char32;
            }

            public BigUnWrapper(char SurrogateHigh, char SurrogateLow)
            {
                m_char32 = ((uint)SurrogateHigh - 0xd800) * 0x0400 + ((uint)SurrogateLow - 0xdc00) + 0x10000;
            }

            public static explicit operator uint(BigUnWrapper char32)
            {
                return char32.m_char32;
            }

            public static explicit operator BigUnWrapper(uint char32)
            {
                return new BigUnWrapper(char32);
            }

            public static bool operator <(BigUnWrapper bg1, BigUnWrapper bg2)
            {
                return bg1.m_char32 < bg2.m_char32;
            }

            public static bool operator >(BigUnWrapper bg1, BigUnWrapper bg2)
            {
                return bg1.m_char32 > bg2.m_char32;
            }

            public static bool operator ==(BigUnWrapper bg1, BigUnWrapper bg2)
            {
                return bg1.m_char32 == bg2.m_char32;
            }

            public static bool operator !=(BigUnWrapper bg1, BigUnWrapper bg2)
            {
                return bg1.m_char32 != bg2.m_char32;
            }

            public override bool Equals(object? obj)
            {
                return obj is BigUnWrapper wrapper && m_char32 == wrapper.m_char32;
            }

            public override int GetHashCode()
            {
                return HashCode.Combine(m_char32);
            }
        }

        #endregion

        #region 初始化

        [GlobalSetup]
        public void Setup()
        {
            // 准备 ASCII 字符集（常见 HTTP 字符）
            asciiChars = new List<char>();
            for (char c = 'A'; c <= 'Z'; c++) asciiChars.Add(c);
            for (char c = 'a'; c <= 'z'; c++) asciiChars.Add(c);
            for (char c = '0'; c <= '9'; c++) asciiChars.Add(c);
            asciiChars.Add('-'); asciiChars.Add('.'); asciiChars.Add('/');

            // 准备 CJK 字符集（常见汉字）
            cjkChars = new List<char>();
            for (uint cp = 0x4E00; cp < 0x4F00; cp++)
            {
                cjkChars.Add((char)cp);
            }

            // 准备代理对（超出 BMP 的字符）
            surrogatePairs = new List<(char, char)>();
            for (uint cp = 0x10000; cp < 0x10050; cp++)
            {
                // 将码点转换为代理对
                cp -= 0x10000;
                char high = (char)((cp >> 10) + 0xd800);
                char low = (char)((cp & 0x3ff) + 0xdc00);
                surrogatePairs.Add((high, low));
            }

            // 准备码点列表
            codePoints = new List<uint>();
            uint[] commonCodePoints = { 0x4E00, 0x4E2D, 0x56FD, 0x10000, 0x12345, 0x20BB7 };
            for (int i = 0; i < 1000; i++)
            {
                codePoints.Add(commonCodePoints[i % commonCodePoints.Length]);
            }

            // 准备数组用于索引操作测试
            bigUnArray = new BigUnWrapper[codePoints.Count];
            runeArray = new System.Text.Rune[codePoints.Count];
            for (int i = 0; i < codePoints.Count; i++)
            {
                bigUnArray[i] = (BigUnWrapper)codePoints[i];
                runeArray[i] = new System.Text.Rune(codePoints[i]);
            }
        }

        #endregion

        #region 构造函数基准测试

        /// <summary>
        /// BigUn: 从 ASCII 字符构造
        /// </summary>
        [Benchmark]
        public void BigUn_Constructor_FromAsciiChar()
        {
            uint sum = 0;
            foreach (var c in asciiChars)
            {
                var bigUn = new BigUnWrapper(c);
                sum += (uint)bigUn;
            }
        }

        /// <summary>
        /// Rune: 从 ASCII 字符构造
        /// </summary>
        [Benchmark]
        public void Rune_Constructor_FromAsciiChar()
        {
            uint sum = 0;
            foreach (var c in asciiChars)
            {
                var rune = new System.Text.Rune(c);
                sum += (uint)rune.Value;
            }
        }

        /// <summary>
        /// BigUn: 从 CJK 字符构造
        /// </summary>
        [Benchmark]
        public void BigUn_Constructor_FromCjkChar()
        {
            uint sum = 0;
            foreach (var c in cjkChars)
            {
                var bigUn = new BigUnWrapper(c);
                sum += (uint)bigUn;
            }
        }

        /// <summary>
        /// Rune: 从 CJK 字符构造
        /// </summary>
        [Benchmark]
        public void Rune_Constructor_FromCjkChar()
        {
            uint sum = 0;
            foreach (var c in cjkChars)
            {
                var rune = new System.Text.Rune(c);
                sum += (uint)rune.Value;
            }
        }

        /// <summary>
        /// BigUn: 从代理对构造
        /// </summary>
        [Benchmark]
        public void BigUn_Constructor_FromSurrogatePair()
        {
            uint sum = 0;
            foreach (var pair in surrogatePairs)
            {
                var bigUn = new BigUnWrapper(pair.high, pair.low);
                sum += (uint)bigUn;
            }
        }

        /// <summary>
        /// Rune: 从代理对构造
        /// </summary>
        [Benchmark]
        public void Rune_Constructor_FromSurrogatePair()
        {
            uint sum = 0;
            foreach (var pair in surrogatePairs)
            {
                var rune = new System.Text.Rune(pair.high, pair.low);
                sum += (uint)rune.Value;
            }
        }

        /// <summary>
        /// BigUn: 从 uint 构造
        /// </summary>
        [Benchmark]
        public void BigUn_Constructor_FromUint()
        {
            uint sum = 0;
            foreach (var cp in codePoints)
            {
                var bigUn = new BigUnWrapper(cp);
                sum += (uint)bigUn;
            }
        }

        /// <summary>
        /// Rune: 从 uint 构造
        /// </summary>
        [Benchmark]
        public void Rune_Constructor_FromUint()
        {
            uint sum = 0;
            foreach (var cp in codePoints)
            {
                var rune = new System.Text.Rune(cp);
                sum += (uint)rune.Value;
            }
        }

        #endregion

        #region 转换为 uint 基准测试（关键：cmap 索引操作）

        /// <summary>
        /// BigUn: 转换为 uint（模拟 Table_cmap.MapCharToGlyph 中的数组索引）
        /// </summary>
        [Benchmark]
        public void BigUn_ToUint_ArrayIndexAccess()
        {
            uint sum = 0;
            foreach (var bigUn in bigUnArray)
            {
                // 模拟字符映射：使用 uint 值作为数组索引
                uint index = (uint)bigUn;
                sum += index;
            }
        }

        /// <summary>
        /// Rune: 转换为 uint（模拟 Table_cmap.MapCharToGlyph 中的数组索引）
        /// </summary>
        [Benchmark]
        public void Rune_ToUint_ArrayIndexAccess()
        {
            uint sum = 0;
            foreach (var rune in runeArray)
            {
                // 模拟字符映射：使用 uint 值作为数组索引
                uint index = (uint)rune.Value;
                sum += index;
            }
        }

        #endregion

        #region 比较运算基准测试

        /// <summary>
        /// BigUn: 相等比较
        /// </summary>
        [Benchmark]
        public bool BigUn_Comparison_Equality()
        {
            bool result = true;
            for (int i = 0; i < bigUnArray.Length - 1; i++)
            {
                result &= bigUnArray[i] == bigUnArray[i + 1];
                result &= bigUnArray[i] != bigUnArray[i + 1];
            }
            return result;
        }

        /// <summary>
        /// Rune: 相等比较
        /// </summary>
        [Benchmark]
        public bool Rune_Comparison_Equality()
        {
            bool result = true;
            for (int i = 0; i < runeArray.Length - 1; i++)
            {
                result &= runeArray[i] == runeArray[i + 1];
                result &= runeArray[i] != runeArray[i + 1];
            }
            return result;
        }

        /// <summary>
        /// BigUn: 小于比较
        /// </summary>
        [Benchmark]
        public bool BigUn_Comparison_LessThan()
        {
            bool result = true;
            for (int i = 0; i < bigUnArray.Length - 1; i++)
            {
                result &= bigUnArray[i] < bigUnArray[i + 1];
            }
            return result;
        }

        /// <summary>
        /// Rune: 小于比较
        /// </summary>
        [Benchmark]
        public bool Rune_Comparison_LessThan()
        {
            bool result = true;
            for (int i = 0; i < runeArray.Length - 1; i++)
            {
                result &= runeArray[i] < runeArray[i + 1];
            }
            return result;
        }

        #endregion

        #region 字符映射场景基准测试（最接近实际使用）

        /// <summary>
        /// BigUn: 模拟 Table_cmap.MapCharToGlyph - HTTP 字符
        /// </summary>
        [Benchmark]
        public void BigUn_CmapMapping_HttpChars()
        {
            var glyphMap = new Dictionary<uint, ushort>
            {
                { 0x002D, 1 }, // '-'
                { 0x002E, 2 }, // '.'
                { 0x002F, 3 }, // '/'
                { (uint)'0', 4 },
                { (uint)'A', 5 },
                { (uint)'a', 6 }
            };

            ushort result = 0;
            foreach (var c in asciiChars)
            {
                var bigUn = new BigUnWrapper(c);
                uint charCode = (uint)bigUn;
                if (glyphMap.TryGetValue(charCode, out ushort glyph))
                {
                    result = glyph;
                }
            }
        }

        /// <summary>
        /// Rune: 模拟 Table_cmap.MapCharToGlyph - HTTP 字符
        /// </summary>
        [Benchmark]
        public void Rune_CmapMapping_HttpChars()
        {
            var glyphMap = new Dictionary<uint, ushort>
            {
                { 0x002D, 1 }, // '-'
                { 0x002E, 2 }, // '.'
                { 0x002F, 3 }, // '/'
                { (uint)'0', 4 },
                { (uint)'A', 5 },
                { (uint)'a', 6 }
            };

            ushort result = 0;
            foreach (var c in asciiChars)
            {
                var rune = new System.Text.Rune(c);
                uint charCode = (uint)rune.Value;
                if (glyphMap.TryGetValue(charCode, out ushort glyph))
                {
                    result = glyph;
                }
            }
        }

        /// <summary>
        /// BigUn: 模拟 Table_cmap.MapCharToGlyph - CJK 字符
        /// </summary>
        [Benchmark]
        public void BigUn_CmapMapping_CjkChars()
        {
            // 模拟 CJK 字形映射表
            var glyphMap = new Dictionary<uint, ushort>();
            foreach (var c in cjkChars)
            {
                glyphMap[(uint)c] = (ushort)c;
            }

            ushort result = 0;
            foreach (var c in cjkChars)
            {
                var bigUn = new BigUnWrapper(c);
                uint charCode = (uint)bigUn;
                glyphMap.TryGetValue(charCode, out result);
            }
        }

        /// <summary>
        /// Rune: 模拟 Table_cmap.MapCharToGlyph - CJK 字符
        /// </summary>
        [Benchmark]
        public void Rune_CmapMapping_CjkChars()
        {
            // 模拟 CJK 字形映射表
            var glyphMap = new Dictionary<uint, ushort>();
            foreach (var c in cjkChars)
            {
                glyphMap[(uint)c] = (ushort)c;
            }

            ushort result = 0;
            foreach (var c in cjkChars)
            {
                var rune = new System.Text.Rune(c);
                uint charCode = (uint)rune.Value;
                glyphMap.TryGetValue(charCode, out result);
            }
        }

        #endregion
    }
}
