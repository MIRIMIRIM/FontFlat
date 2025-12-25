using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Text;

namespace OTFontFile.Performance.Tests.UnitTests
{
    /// <summary>
    /// Rune 功能测试
    /// 验证Rune（替换BigUn）的功能正确性
    /// </summary>
    [TestClass]
    public class RuneTests
    {
        [TestMethod]
        public void Constructor_FromChar_Ascii()
        {
            var rune = new Rune('A');
            Assert.AreEqual(65u, (uint)rune.Value);
        }

        [TestMethod]
        public void Constructor_FromUint_Cjk()
        {
            uint expected = 0x4E2Du; // '中'
            var rune = new Rune(expected);
            Assert.AreEqual(expected, (uint)rune.Value);
        }

        [TestMethod]
        public void Constructor_FromSurrogatePair_Supplementary()
        {
            uint expected = 0x20BB7u; // '𠮷'
            var rune = new Rune('\uD842', '\uDFB7');
            Assert.AreEqual(expected, (uint)rune.Value);
        }

        [TestMethod]
        public void CmapMapping_HttpCharacters()
        {
            var characters = new[] { '-', '.', '/', '0', '9', 'A', 'z' };

            foreach (var c in characters)
            {
                var rune = new Rune(c);
                int value = rune.Value;
                Assert.IsTrue(value == (int)c);
            }
        }

        [TestMethod]
        public void CmapMapping_CjkCharacters()
        {
            var codePoints = new[] { 0x4E00, 0x4E8C, 0x4E09, 0x4E2D, 0x56FD };

            foreach (var cp in codePoints)
            {
                var rune = new Rune(cp);
                int index = rune.Value;
                Assert.AreEqual(cp, index);
            }
        }

        [TestMethod]
        public void MinMaxUnicodeScalarValues()
        {
            var minRune = new Rune(0x0000);
            var maxRune = new Rune(0x10FFFF);
            var validRune = new Rune(0x4E2D);

            Assert.AreEqual(0, minRune.Value);
            Assert.AreEqual(0x10FFFF, maxRune.Value);
            Assert.AreEqual(0x4E2D, validRune.Value);
        }

        [TestMethod]
        public void ComparisonOperators()
        {
            var r1 = new Rune(65);
            var r2 = new Rune(66);
            var r3 = new Rune(65);

            Assert.IsTrue(r1.Value == r3.Value);
            Assert.IsFalse(r1.Value == r2.Value);
            Assert.IsTrue(r1.Value < r2.Value);
        }

        [TestMethod]
        public void ArrayIndexBehavior_Verification()
        {
            // 模拟cmap映射场景
            var glyphMap = new Dictionary<int, ushort>
            {
                { 65, 10 }, // 'A'
                { 66, 11 }, // 'B'
                { 19968, 100 }, // '一' (0x4E00 = 19968)
                { 20013, 101 }  // '中' (0x4E2D = 20013)
            };

            var runeA = new Rune(65);
            var runeChinese = new Rune(20013);

            Assert.IsTrue(glyphMap.TryGetValue(runeA.Value, out ushort glyphA));
            Assert.AreEqual(10, glyphA);

            Assert.IsTrue(glyphMap.TryGetValue(runeChinese.Value, out ushort glyphChinese));
            Assert.AreEqual(101, glyphChinese);
        }

        [TestCleanup]
        public void TestCleanup()
        {
            // 清理资源
        }
    }
}
