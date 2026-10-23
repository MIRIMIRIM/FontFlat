using System.Buffers.Binary;
using FontWeave.Core;
using FontWeave.Tables;
using Xunit;

namespace FontWeave.Tests;

public sealed class CmapMalformedDiagnosticTests
{
    [Fact]
    public void HighEntrySelectorReturnsTheUnsignedDiagnostic()
    {
        byte[] bytes = Convert.FromHexString("0004001800000002000280000000FFFF0000FFFF00010000");
        Assert.Equal(ReadStatus.Done, CmapFormat4View.TryOpen(bytes, out var view, out _));
        Assert.Equal(ReadStatus.InvalidFormat, view.Validate(true, 1, out var diagnostic));
        Assert.Equal(DiagnosticCode.InvalidValue, diagnostic.Code);
        Assert.Equal(0x80000002UL, diagnostic.Detail);
        Assert.Equal(2UL, diagnostic.Expected);
    }

    [Theory]
    [InlineData(0x8000, 0x8000)]
    [InlineData(0x8001, 0x8000)]
    public void HighPlatformOrderingReturnsTheUnsignedDiagnostic(int first, int second)
    {
        var bytes = new byte[30];
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(2), 2);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(4), (ushort)first);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(12), (ushort)second);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(8), 20);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(16), 20);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(20), 6);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(22), 10);
        Assert.Equal(ReadStatus.Done, CmapView.TryOpen(bytes, out var view, out _));
        Assert.Equal(ReadStatus.Done, CmapContext.TryCreate(1, out var context, out _));
        Assert.Equal(ReadStatus.InvalidFormat, view.Validate(context!, out var diagnostic));
        Assert.Equal(DiagnosticCode.InvalidValue, diagnostic.Code);
        Assert.Equal((ulong)(uint)second << 16, diagnostic.Detail);
        Assert.Equal((ulong)(uint)first << 16, diagnostic.Expected);
        Assert.Equal(1, diagnostic.Location.RecordOrdinal);
    }
}
