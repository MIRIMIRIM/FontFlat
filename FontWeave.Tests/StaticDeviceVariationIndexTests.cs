// Device-table factory/edit parity: the read side accepts a 0x8000 VariationIndex
// body (the two header words are the outer/inner delta-set indices, no packed
// deltas), so the public factory and the no-op delta edit must accept the same body.
// They used to refuse it, which made a legal variable-layout device unconstructible
// through the API while the reader returned Done for the identical bytes.
using FontWeave.Core;
using FontWeave.Tables.Layout;
using Xunit;

namespace FontWeave.Tests;

public sealed class StaticDeviceVariationIndexTests
{
    // outer = 0, inner = 0 (the 0x8000 header carries the index pair, not a ppem range).
    private static readonly byte[] VariationIndexWire = [0, 0, 0, 0, 0x80, 0];

    [Fact]
    public void ReadFactoryAndNoOpEditAgree()
    {
        var location = DataLocation.Anonymous(0x47504F53);
        Assert.Equal(ReadStatus.Done, StaticDeviceView.TryOpen(VariationIndexWire,
            out var view, out _, location));
        Assert.Equal(ReadStatus.Done, view.TryRead(out var device, out _));
        Assert.True(device!.IsVariationDevice);
        Assert.Empty(device.Deltas.ToArray());

        Assert.Equal(ReadStatus.Done, device.TryWithDeltas([], out var edited, out _));
        Assert.NotNull(edited);
        Assert.True(edited!.IsVariationDevice);
        Assert.Equal(device.Outer, edited.Outer);
        Assert.Equal(device.Inner, edited.Inner);

        Assert.Equal(ReadStatus.Done, StaticDeviceTableCompiler.TryCreate(0, 0, 0x8000,
            [], out var created, out _));
        Assert.NotNull(created);
        Assert.True(created!.IsVariationDevice);
        Assert.Equal(ReadStatus.Done, StaticDeviceTableCompiler.TryCompile(created,
            out var bytes, out _));
        Assert.Equal(VariationIndexWire, bytes);
    }

    // The parity test pins only the all-zero index pair, where a swap of the two
    // header words is invisible. A non-zero pair with independently spelled expected
    // bytes keeps the round trip from passing by self-comparison: an index swap, a
    // word-endianness slip or a dropped header word must surface as a byte or field
    // diff, not as the implementation agreeing with itself.
    [Fact]
    public void FactoryRoundTripPreservesNonZeroIndices()
    {
        const ushort outer = 0x0102, inner = 0x0304;
        var location = DataLocation.Anonymous(0x47504F53);
        Assert.Equal(ReadStatus.Done, StaticDeviceTableCompiler.TryCreate(outer, inner, 0x8000,
            [], out var created, out _));
        Assert.NotNull(created);
        Assert.True(created!.IsVariationDevice);

        Assert.Equal(ReadStatus.Done, StaticDeviceTableCompiler.TryCompile(created,
            out var bytes, out _));
        Assert.NotNull(bytes);
        // Spelled from the device-table layout (big-endian StartSize, EndSize, 0x8000),
        // not derived from the implementation under test.
        byte[] expected = [0x01, 0x02, 0x03, 0x04, 0x80, 0x00];
        Assert.Equal(expected, bytes!);

        Assert.Equal(ReadStatus.Done, StaticDeviceView.TryOpen(bytes!, out var view, out _, location));
        Assert.Equal(ReadStatus.Done, view.TryRead(out var device, out _));
        Assert.NotNull(device);
        Assert.True(device!.IsVariationDevice);
        Assert.Equal(outer, device.Outer);
        Assert.Equal(inner, device.Inner);
        Assert.Equal(outer, device.StartSize);
        Assert.Equal(inner, device.EndSize);
        Assert.Equal((ushort)0x8000, device.DeltaFormat);
        Assert.Empty(device.Deltas.ToArray());
        Assert.Equal(6, device.ConsumedLength); // The declared body is the header alone.
    }

    // A non-empty delta array on a VariationIndex body is still the caller's error:
    // the reference carries no values to pack.
    [Fact]
    public void VariationIndexRefusesDeclaredDeltas()
    {
        Assert.Equal(ReadStatus.InvalidFormat,
            StaticDeviceTableCompiler.TryCreate(0, 0, 0x8000, [1], out var created, out _));
        Assert.Null(created);
    }
}
