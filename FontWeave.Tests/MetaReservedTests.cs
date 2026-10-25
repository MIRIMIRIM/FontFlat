// The meta header's byte-8 field: major toolchains shipped fonts with the
// historical non-zero value there (Windows core fonts carry 0x28), and every
// consumer ignores it. The read face already tolerates it; strict validation
// must too, or it rejects the ecosystem's real fonts (104 of 526 in the corpus
// sweep, Arial included).
using System.Buffers.Binary;
using FontWeave.Core;
using FontWeave.Tables;
using Xunit;

namespace FontWeave.Tests;

public sealed class MetaReservedTests
{
    private static byte[] Meta(uint reserved)
    {
        var bytes = new byte[16 + 12 + 3];
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(0), 1);          // version
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(4), 0);          // flags
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(8), reserved);   // the historical field
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(12), 1);         // one data map
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(16), 0x646C6E67); // 'dlng'
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(20), 28);        // payload offset
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(24), 3);         // payload length
        bytes[28] = (byte)'a'; bytes[29] = (byte)'b'; bytes[30] = (byte)'c';
        return bytes;
    }

    [Theory]
    [InlineData(0)]
    [InlineData(0x28)] // the Windows core fonts' historical value
    public void ReservedIsIgnoredOnBothFaces(uint reserved)
    {
        byte[] table = Meta(reserved);
        Assert.Equal(ReadStatus.Done, MetaView.TryOpen(table, out var view, out _));

        Assert.Equal(ReadStatus.Done, view.Validate(out var diagnostic));
        Assert.Equal(ReadStatus.Done, view.TryRead(out var model, out _));
        // The compatibility value rides on the model; the compiler re-emits zero.
        Assert.Equal(reserved, model!.SourceReserved);
    }

    [Fact]
    public void FlagsRemainStrict()
    {
        byte[] table = Meta(0);
        BinaryPrimitives.WriteUInt32BigEndian(table.AsSpan(4), 1);

        Assert.Equal(ReadStatus.Done, MetaView.TryOpen(table, out var view, out _));

        Assert.Equal(ReadStatus.InvalidFormat, view.Validate(out var diagnostic));
        Assert.Equal(DiagnosticCode.InvalidValue, diagnostic.Code);
    }
}
