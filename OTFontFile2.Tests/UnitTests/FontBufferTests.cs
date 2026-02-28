using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OTFontFile2.Tests.UnitTests;

[TestClass]
public sealed class FontBufferTests
{
    [TestMethod]
    public void MapReadOnlyFile_UsesExactFileLengthForBounds()
    {
        string path = Path.GetTempFileName();
        try
        {
            byte[] data = new byte[123];
            for (int i = 0; i < data.Length; i++)
                data[i] = (byte)i;

            File.WriteAllBytes(path, data);

            using var buffer = FontBuffer.MapReadOnlyFile(path);
            Assert.AreEqual(data.Length, buffer.Length);

            Assert.IsTrue(buffer.TrySlice(0, data.Length, out var full));
            CollectionAssert.AreEqual(data, full.ToArray());

            Assert.IsFalse(buffer.TrySlice(data.Length, 1, out _));
            Assert.IsFalse(buffer.TrySlice(data.Length - 1, 2, out _));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
