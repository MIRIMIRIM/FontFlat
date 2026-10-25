// The name record's Mac language field: major CJK vendors (the Fontworks FOT
// line, 44 of 1031 sampled corpus fonts) ship records with 0xFFFF there - the
// "language unspecified" sentinel - and every consumer reads them. The format-0
// high-bit reservation (platform 240..255 named-instance escape) stays enforced
// for every other value; the sentinel alone is tolerated.
using System.Buffers.Binary;
using FontWeave.Core;
using FontWeave.Tables;
using Xunit;

namespace FontWeave.Tests;

public sealed class NameLanguageSentinelTests
{
    private static byte[] NameTable(ushort languageId, ushort platformId = 1)
    {
        var bytes = new byte[6 + 12 + 2];
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(0), 0);   // format 0
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(2), 1);   // one record
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(4), 18);  // storage
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(6), platformId);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(8), 1);   // encoding
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(10), languageId);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(12), 1);  // nameId
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(14), 2);  // length
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(16), 0);  // storage offset
        bytes[18] = 0; bytes[19] = (byte)'A';
        return bytes;
    }

    [Fact]
    public void TheUnspecifiedLanguageSentinelIsTolerated()
    {
        byte[] table = NameTable(0xFFFF);
        Assert.Equal(ReadStatus.Done, NameView.TryOpen(table, out var view, out _));

        Assert.Equal(ReadStatus.Done, view.Validate(out var diagnostic));
    }

    [Fact]
    public void OtherHighBitsOutsideTheNamedInstancePlatformStillRefuse()
    {
        byte[] table = NameTable(0x8001);
        Assert.Equal(ReadStatus.Done, NameView.TryOpen(table, out var view, out _));

        Assert.Equal(ReadStatus.InvalidIndex, view.Validate(out var diagnostic));
        Assert.Equal(DiagnosticCode.IndexOutOfRange, diagnostic.Code);
    }
}
