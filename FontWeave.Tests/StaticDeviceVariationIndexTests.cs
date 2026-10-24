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
