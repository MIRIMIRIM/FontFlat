// Variation family factory domain faces: header counts and sizes are uint16 wire
// fields; the plan owns their refusal, never the writer's narrowing.
using System.Buffers.Binary;
using FontWeave.Core;
using FontWeave.Tables.Variation;
using Xunit;

namespace FontWeave.Tests;

public sealed class VariationFactoryDomainTests
{
    // gvar's axisCount is a uint16 header word; the factory bounded numGlyphs and the
    // shared-tuple count but not the axis count, so the assembly's narrowing threw in a
    // checked build (and truncated the word in an unchecked one).
    [Fact]
    public void GvarAxisCountPastTheWordIsRefused()
    {
        var status = GvarTableCompiler.TryCreate(70_000, 0, 0, [],
            Array.Empty<GlyphVariationDataTable?>(), out var model, out _);

        Assert.Equal(ReadStatus.InvalidFormat, status);
        Assert.Null(model);
    }

    // cvar's per-tuple variationDataSize is a uint16 field. gvar guards the accumulated
    // run length; cvar checked only the table-level dataOffset, so one wide tuple reached
    // the writer and threw.
    [Fact]
    public void CvarTupleRunPastTheWordIsRefused()
    {
        const int count = 40_000;
        var deltas = new short[count];
        Array.Fill(deltas, (short)1000);
        Assert.Equal(ReadStatus.Done, TupleVariation.Create(
            TupleVariationCodec.EmbeddedPeakFlag, [0], null, null, false,
            null, deltas, [], out var tuple, out _));

        var status = CvarTableCompiler.TryCreate(1, count, null, [tuple!],
            out var model, out _);

        Assert.Equal(ReadStatus.InvalidFormat, status);
        Assert.Null(model);
    }

    // fvar's instanceSize is a uint16 field: 4*axisCount+6 must fit, so 16383 axes are
    // past the domain even though the count fits its own word.
    [Fact]
    public void FvarAxisListPastTheInstanceSizeWordIsRefused()
    {
        var axes = Enumerable.Range(0, 16_383)
            .Select(i => new FvarAxis((uint)(0x61786961 + i), 0, 0, 100, 0, 256)).ToArray();

        var status = FvarTableCompiler.TryCreate(axes, [], out var model, out _);

        Assert.Equal(ReadStatus.InvalidFormat, status);
        Assert.Null(model);
    }

    // The same domain on the read side: a hostile fvar declaring 16383 axes and one
    // instance derives a base instanceSize past the word. The reader must answer a
    // diagnosis instead of throwing at the derived size.
    [Fact]
    public void FvarReadDiagnosesInstanceSizePastTheWord()
    {
        const int axes = 16_383;
        var bytes = new byte[16 + axes * 20 + 6];
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(0), 0x00010000);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(4), 16);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(6), 2);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(8), axes);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(10), 20);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(12), 1);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(14), 6);
        for (int i = 0; i < axes; i++)
        {
            int at = 16 + i * 20;
            BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(at), (uint)(0x61786961 + i));
            BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(at + 12), 0x00010000);
            BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(at + 18), 256);
        }
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(16 + axes * 20), 256);

        Assert.Equal(ReadStatus.Done, FvarView.TryOpen(bytes, out var view, out _));
        Assert.NotEqual(ReadStatus.Done, view.ValidateTable(out var diagnostic));
    }

    // ItemVariationData states itemCount and regionIndexCount as uint16 fields; the
    // factory left both to the budget alone, so wide inputs reached the writer's
    // narrowing.
    [Fact]
    public void ItemCountsPastTheWordAreRefused()
    {
        var rows = Enumerable.Range(0, 70_000).Select(_ => Array.Empty<int>()).ToArray();
        Assert.Equal(ReadStatus.InvalidFormat, ItemVariationDataTableCompiler.TryCreate(0, [],
            rows, out var model, out _));
        Assert.Null(model);

        var regions = Enumerable.Range(0, 70_000).Select(_ => (ushort)0).ToArray();
        Assert.Equal(ReadStatus.InvalidFormat, ItemVariationDataTableCompiler.TryCreate(0,
            regions, [new int[70_000]], out model, out _));
        Assert.Null(model);
    }
}
