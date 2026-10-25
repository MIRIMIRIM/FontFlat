// Round-trip witnesses for the compiler families with no direct write-side face:
// each builds a model through the public factory, compiles it, and reads the bytes
// back through the typed view (the same chain the pipeline validates against).
using System.Buffers.Binary;
using FontWeave.Core;
using FontWeave.Tables;
using FontWeave.Tables.Variation;
using Xunit;

namespace FontWeave.Tests;

public sealed class TableFactoryRoundTripTests
{
    // avar: one axis with the required -1/0/+1 fixed points mapped monotonically.
    [Fact]
    public void AvarRoundTripsThroughTheView()
    {
        var maps = new[]
        {
            new AvarMapPair[] { new(-16384, -16384), new(0, 0), new(16384, 16384) },
        };
        Assert.Equal(ReadStatus.Done, AvarTableCompiler.TryCreate(maps, out var model, out _));
        Assert.Equal(ReadStatus.Done, AvarTableCompiler.TryCompile(model!, out var bytes, out _));

        Assert.Equal(ReadStatus.Done, AvarView.TryOpen(bytes!, out var view, out _));
        Assert.Equal(ReadStatus.Done, view.TryReadTable(fvarAxisCount: 1, out var read, out _));
        Assert.Single(read!.Maps);
        Assert.Equal(3, read.Maps[0].Pairs.Length);
        Assert.Equal(-16384, read.Maps[0].Pairs[0].From);
        Assert.Equal(16384, read.Maps[0].Pairs[2].To);
    }

    // mvar: one value record over a one-region store, read back with the same
    // axis count authority.
    [Fact]
    public void MvarRoundTripsThroughTheView()
    {
        Assert.Equal(ReadStatus.Done, ItemVariationDataTableCompiler.TryCreate(0, [0],
            [[100]], out var data, out _));
        Assert.Equal(ReadStatus.Done, VariationStoreTableCompiler.TryCreate(1,
            [[new VariationRegionAxis(-16384, 0, 16384)]], [data!],
            out var store, out _));
        var records = new[] { new MvarValueRecord(0x68617363, 0, 0) };
        Assert.Equal(ReadStatus.Done, MvarTableCompiler.TryCreate(store!, records,
            valueRecordSize: 8, axisCount: 1, out var model, out _));
        Assert.Equal(ReadStatus.Done, MvarTableCompiler.TryCompile(model!, out var bytes, out _));

        Assert.Equal(ReadStatus.Done, MvarView.TryOpen(bytes!, out var view, out _));
        Assert.Equal(ReadStatus.Done, view.TryReadTable(axisCount: 1, out var read, out _));
        Assert.Single(read!.Records);
        Assert.Equal(0x68617363u, read.Records[0].ValueTag);
        Assert.Equal(8, read.ValueRecordSize);
    }

    // STAT v1.1: two design axes with format-2 and format-3 values, and the
    // elided-fallback slot every STAT must declare.
    [Fact]
    public void StatRoundTripsThroughTheView()
    {
        var axes = new[]
        {
            new StatDesignAxis(0x77676874, 256, 0),
            new StatDesignAxis(0x77647468, 257, 1),
        };
        var values = new[]
        {
            new StatAxisValue(2, 0, 258, 0, 0, 0x00010000, 0, 0x00028000, 0, []),
            new StatAxisValue(3, 0, 259, 1, 0x00008000, 0, 0, 0, 0x00020000, []),
        };
        Assert.Equal(ReadStatus.Done, StatTableCompiler.TryCreate(0x00010001, axes, values,
            256, out var model, out _));
        Assert.Equal(ReadStatus.Done, StatTableCompiler.TryCompile(model!, out var bytes, out _));

        Assert.Equal(ReadStatus.Done, StatView.TryOpen(bytes!, out var view, out _));
        Assert.Equal(ReadStatus.Done, view.TryReadTable(fvar: null, out var read, out _));
        Assert.Equal(2, read!.DesignAxes.Count);
        Assert.Equal(0x77676874u, read.DesignAxes[0].AxisTag);
        Assert.Equal(1, read.DesignAxes[1].AxisOrdering);
        Assert.Equal(2, read.AxisValues.Count);
        Assert.Equal(3, read.AxisValues[1].Format);
        Assert.Equal(0x00020000, read.AxisValues[1].LinkedValue);
        Assert.Equal((ushort)256, read.ElidedFallbackNameID);
    }

    // fvar: two axes and one named instance with a PostScript name; read back with
    // the full validate chain.
    [Fact]
    public void FvarRoundTripsThroughTheView()
    {
        var axes = new[]
        {
            new FvarAxis(0x77676874, 0, 0, 0x00020000, 0, 256),
            new FvarAxis(0x77647468, 0, 0, 0x00010000, 0, 257),
        };
        var instances = new[] { new FvarInstance(258, 0, [0x00010000, 0], 259) };
        Assert.Equal(ReadStatus.Done, FvarTableCompiler.TryCreate(axes, instances,
            out var model, out _));
        Assert.Equal(ReadStatus.Done, FvarTableCompiler.TryCompile(model!, out var bytes, out _));

        Assert.Equal(ReadStatus.Done, FvarView.TryOpen(bytes!, out var view, out _));
        Assert.Equal(ReadStatus.Done, view.ValidateTable(out _));
        Assert.Equal(ReadStatus.Done, view.TryReadTable(out var read, out _));
        Assert.Equal(2, read!.AxisCount);
        Assert.Equal(0x77676874u, read.Axes[0].Tag);
        Assert.Single(read.Instances);
        Assert.Equal(0x00010000, read.Instances[0].Coordinates[0]);
        Assert.Equal((ushort)259, read.Instances[0].PostScriptNameID);
    }

    // CPAL v1: two entries shared by one palette through the record index, with
    // the three v1-only label/type arrays.
    [Fact]
    public void CpalRoundTripsThroughTheView()
    {
        // One 4-byte BGRA record per color; palette 0 starts at record 0.
        var colors = new byte[][]
        {
            [10, 20, 30, 40],
            [50, 60, 70, 80],
        };
        Assert.Equal(ReadStatus.Done, CpalTableCompiler.TryCreate(1, 2, [0], colors,
            paletteTypes: [0], paletteLabels: [256], paletteEntryLabels: [257, 258],
            out var model, out _));
        Assert.Equal(ReadStatus.Done, CpalTableCompiler.TryCompile(model!, out var bytes, out _));

        Assert.Equal(ReadStatus.Done, CpalView.TryOpen(bytes!, out var view, out _));
        Assert.Equal(ReadStatus.Done, view.TryReadTable(out var read, out _));
        Assert.Equal(1, read!.PaletteCount);
        Assert.Equal(2, read.NumPaletteEntries);
        Assert.Equal(0, read.ColorRecordIndices[0]);
        Assert.Equal(10, read.ColorRecords[0][0]);
        Assert.Equal(258, read.PaletteEntryLabels![1]);
    }

    // fpgm: the program is opaque bytes; the compiled table is the program verbatim.
    [Fact]
    public void InstructionProgramCompilesVerbatim()
    {
        var program = new byte[] { 0xB0, 0x01, 0x2C, 0xB8, 0x02, 0x2C, 0x17 };
        var model = new InstructionProgramTable(InstructionProgramKind.FontProgram,
            (byte[])program.Clone(), DataLocation.Anonymous(0x6670676D));

        Assert.Equal(ReadStatus.Done, InstructionProgramCompiler.TryCompile(model,
            out var bytes, out _));
        Assert.Equal(program, bytes);
    }
}
