using Microsoft.VisualStudio.TestTools.UnitTesting;
using OTFontFile;
using System.IO;

namespace OTFontFile.Performance.Tests.UnitTests
{
    /// <summary>
    /// 测试MBOBuffer的字节序和读取功能
    /// </summary>
    [TestClass]
    public class BufferTests
    {
        [TestMethod]
        public void MBOBuffer_GetByte_ShouldReturnCorrectValue()
        {
            // Arrange
            byte[] testData = { 0x12, 0x34, 0x56, 0x78, 0x9A, 0xBC, 0xDE, 0xF0 };
            var buffer = new MBOBuffer((uint)testData.Length);
            for (int i = 0; i < testData.Length; i++)
            {
                buffer.SetByte(testData[i], (uint)i);
            }

            // Act & Assert
            for (int i = 0; i < testData.Length; i++)
            {
                Assert.AreEqual(testData[i], buffer.GetByte((uint)i),
                    $"Byte at offset {i} should match");
            }
        }

        [TestMethod]
        public void MBOBuffer_GetShort_ShouldReadBigEndian()
        {
            // Arrange
            var buffer = new MBOBuffer(4);
            buffer.SetShort(0x1234, 0);
            buffer.SetShort(0x5678, 2);

            // Act & Assert
            Assert.AreEqual((short)0x1234, buffer.GetShort(0),
                "Should read short as big-endian");
            Assert.AreEqual((short)0x5678, buffer.GetShort(2),
                "Should read second short correctly");
        }

        [TestMethod]
        public void MBOBuffer_GetUshort_ShouldReadBigEndian()
        {
            // Arrange
            var buffer = new MBOBuffer(4);
            buffer.SetUshort(0x1234, 0);
            buffer.SetUshort(0xABCD, 2);

            // Act & Assert
            Assert.AreEqual((ushort)0x1234, buffer.GetUshort(0),
                "Should read ushort as big-endian");
            Assert.AreEqual((ushort)0xABCD, buffer.GetUshort(2),
                "Should read second ushort correctly");
        }

        [TestMethod]
        public void MBOBuffer_GetInt_ShouldReadBigEndian()
        {
            // Arrange
            var buffer = new MBOBuffer(8);
            buffer.SetInt(0x12345678, 0);
            buffer.SetInt(-1, 4);

            // Act & Assert
            Assert.AreEqual(0x12345678, buffer.GetInt(0),
                "Should read int as big-endian");
            Assert.AreEqual(-1, buffer.GetInt(4),
                "Should handle negative values");
        }

        [TestMethod]
        public void MBOBuffer_GetUint_ShouldReadBigEndian()
        {
            // Arrange
            var buffer = new MBOBuffer(8);
            buffer.SetUint(0x12345678, 0);
            buffer.SetUint(0xABCDEF00, 4);

            // Act & Assert
            Assert.AreEqual(0x12345678u, buffer.GetUint(0),
                "Should read uint as big-endian");
            Assert.AreEqual(0xABCDEF00u, buffer.GetUint(4),
                "Should read second uint correctly");
        }

        [TestMethod]
        public void MBOBuffer_CalcChecksum_ShouldReturnCorrect()
        {
            // Arrange
            var buffer = MBOBuffer.CalcPadBytes(12, 4);
            var buf = new MBOBuffer(12);
            buf.SetUint(0x12345678, 0);
            buf.SetUint(0x00000000, 4);
            buf.SetUint(0x00000000, 8);

            // Act
            uint checksum = buf.CalcChecksum();

            // Assert
            Assert.AreEqual(0x12345678u, checksum,
                "Checksum should sum all uints correctly");
        }

        [TestMethod]
        public void MBOBuffer_BinaryEqual_ShouldWorkCorrectly()
        {
            // Arrange
            var buf1 = new MBOBuffer(100);
            var buf2 = new MBOBuffer(100);
            var buf3 = new MBOBuffer(50);

            // Act & Assert
            Assert.IsTrue(MBOBuffer.BinaryEqual(buf1, buf1),
                "Same buffer should be equal to itself");

            for (uint i = 0; i < 100; i++)
            {
                buf1.SetByte((byte)(i % 256), i);
                buf2.SetByte((byte)(i % 256), i);
            }

            Assert.IsTrue(MBOBuffer.BinaryEqual(buf1, buf2),
                "Buffers with same content should be equal");

            Assert.IsFalse(MBOBuffer.BinaryEqual(buf1, buf3),
                "Buffers with different length should not be equal");
        }

        [TestMethod]
        public void MBOBuffer_Static_ConversionMethods_ShouldWork()
        {
            // Arrange
            byte[] testData = { 0x12, 0x34, 0x56, 0x78 };

            // Act & Assert
            Assert.AreEqual((short)0x1234, MBOBuffer.GetMBOshort(testData, 0),
                "GetMBOshort should read big-endian");
            Assert.AreEqual((ushort)0x1234, MBOBuffer.GetMBOushort(testData, 0),
                "GetMBOushort should read big-endian");
            Assert.AreEqual(0x12345678, MBOBuffer.GetMBOint(testData, 0),
                "GetMBOint should read big-endian");
            Assert.AreEqual(0x12345678u, MBOBuffer.GetMBOuint(testData, 0),
                "GetMBOuint should read big-endian");
        }
    }
}
