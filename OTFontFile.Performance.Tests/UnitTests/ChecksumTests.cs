using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using OTFontFile;

namespace OTFontFile.Performance.Tests.UnitTests
{
    [TestClass]
    public class ChecksumTests
    {
        [TestMethod]
        public void VerifyMBOBufferChecksum_MatchesScalarReference()
        {
            var rnd = new Random(42);
            int[] sizes = { 
                4, 8, 12, 16, 20, 32, 64, 100, 128, 256, 512, 1024, 
                1024 + 1, 1024 + 3, 1024 + 13, // Unaligned sizes
                1024 * 1024 // Large buffer
            };

            foreach (var size in sizes)
            {
                byte[] data = new byte[size];
                rnd.NextBytes(data);
                
                var buf = new OTFontFile.MBOBuffer((uint)size);
                for(uint i=0; i<size; i++) buf.SetByte(data[i], i);
                
                // Optimized Checksum
                uint optimizedSum = buf.CalcChecksumUncached();
                
                // Reference Checksum (Scalar)
                uint refSum = CalcChecksumScalar(buf);
                
                Assert.AreEqual(refSum, optimizedSum, $"Checksum mismatch at size {size}. Ref: {refSum:X8}, Opt: {optimizedSum:X8}");
            }
        }

        private static uint CalcChecksumScalar(OTFontFile.MBOBuffer buf)
        {
            uint sum = 0;
            // GetPaddedLength ensures we read multiple of 4 bytes
            uint paddedLength = buf.GetPaddedLength();
            
            // Read as uints
            for (uint i = 0; i < paddedLength; i += 4)
            {
                sum += buf.GetUint(i);
            }
            return sum;
        }
    }
}
