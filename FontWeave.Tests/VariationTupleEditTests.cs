// Value-level tuple-variation edits (cvar/gvar) and the HVAR metric read face.
//
// Every wire here is assembled from the raw format directly, never by asking the
// product to write it, so the read -> edit -> read round trip compares the request
// against an independent expectation. The cases pin the coordinate groups (peak,
// intermediate start, intermediate end), the per-column delta counts of sparse
// tuples, the signed delta width classes, and the NULL side-bearing maps.
using System.Buffers.Binary;
using FontWeave.Core;
using FontWeave.Tables.Variation;
using Xunit;

namespace FontWeave.Tests;

public sealed class VariationTupleEditTests
{
    private static readonly DataLocation Source = new(11, 0, 0, -1, DataField.None,
        OffsetBasis.Table, 0);

    // ---- cvar -------------------------------------------------------------------

    // One cvar 1.0 store: header (8), the TupleVariationHeader array, the serialized
    // run at dataOffset. The tuple carries the mandatory embedded peak, an optional
    // intermediate region, and a private point-number block (one CVT index) so the
    // run's delta count is independent of the CVT authority.
    private static byte[] CvarWire(int axisCount, short[] peak, short[]? start,
        short[]? end, short[] deltas)
    {
        bool intermediate = start is not null;
        int tupleIndex = 0x8000 | (intermediate ? 0x4000 : 0) | 0x2000; // peak|int|private
        int headerSize = 4 + 2 * axisCount + (intermediate ? 4 * axisCount : 0);
        int runAt = 8 + headerSize;
        int dataOffset = runAt;
        // The private point block: count 1, one run of one int8 difference (point 0).
        byte[] points = [0x01, 0x00, 0x00];
        byte[] deltaBytes = DeltaRun(deltas);
        int runLength = points.Length + deltaBytes.Length;
        var bytes = new byte[runAt + runLength];
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(0), 0x00010000);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(4), 1);      // one tuple
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(6), (ushort)dataOffset);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(8), (ushort)runLength);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(10), (ushort)tupleIndex);
        int at = 12;
        foreach (short coordinate in peak)
        {
            BinaryPrimitives.WriteInt16BigEndian(bytes.AsSpan(at), coordinate);
            at += 2;
        }
        if (intermediate)
        {
            foreach (short coordinate in start!)
            {
                BinaryPrimitives.WriteInt16BigEndian(bytes.AsSpan(at), coordinate);
                at += 2;
            }
            foreach (short coordinate in end!)
            {
                BinaryPrimitives.WriteInt16BigEndian(bytes.AsSpan(at), coordinate);
                at += 2;
            }
        }
        points.CopyTo(bytes.AsSpan(runAt));
        deltaBytes.CopyTo(bytes.AsSpan(runAt + points.Length));
        return bytes;
    }

    // One packed delta run of the minimum width the values need (zero/int8/int16).
    private static byte[] DeltaRun(short[] deltas)
    {
        var bytes = new List<byte>();
        int at = 0;
        while (at < deltas.Length)
        {
            int width = deltas[at] == 0 ? 0 : deltas[at] is < sbyte.MinValue or > sbyte.MaxValue ? 2 : 1;
            int end = at + 1;
            while (end < deltas.Length && end - at < 64 &&
                (deltas[end] == 0 ? 0 : deltas[end] is < sbyte.MinValue or > sbyte.MaxValue ? 2 : 1) == width)
                end++;
            bytes.Add((byte)((width == 0 ? 0x80 : 0) | (width == 2 ? 0x40 : 0) | (end - at - 1)));
            for (int i = at; i < end && width != 0; i++)
            {
                if (width == 2)
                {
                    ushort raw = unchecked((ushort)deltas[i]);
                    bytes.Add((byte)(raw >> 8));
                    bytes.Add((byte)raw);
                }
                else
                {
                    bytes.Add(unchecked((byte)deltas[i]));
                }
            }
            at = end;
        }
        return [.. bytes];
    }

    private static CvarTable ReadCvar(byte[] wire, int axisCount, int cvtCount)
    {
        ReadStatus status = CvarView.TryOpen(wire, out var view, out var diagnostic, Source);
        Assert.Equal(ReadStatus.Done, status);
        status = view.TryReadTable(axisCount, cvtCount, out var model, out diagnostic);
        Assert.Equal(ReadStatus.Done, status);
        return model!;
    }

    // The intermediate-region write starts after the mandatory embedded peak. Writing
    // the start group at the first inline pair overwrote the peak and shifted the end
    // group onto the start pair, silently changing the tuple's region.
    [Fact]
    public void CvarIntermediateEditKeepsThePeakAndWritesStartThenEnd()
    {
        short[] peak = [4000, 5000], start = [1000, 2000], end = [7000, 8000];
        CvarTable model = ReadCvar(CvarWire(2, peak, start, end, [0]), 2, 1);
        Assert.Equal(peak, model.Tuples[0].PeakCoords!);

        ReadStatus status = model.TryWithTupleIntermediateCoords(0, [1100, 2200],
            [7700, 8800], out var updated, out var diagnostic);
        Assert.Equal(ReadStatus.Done, status);
        Assert.NotNull(updated);
        TupleVariation tuple = updated!.Tuples[0];
        Assert.Equal(peak, tuple.PeakCoords!);
        Assert.Equal(new short[] { 1100, 2200 }, tuple.IntermediateStartCoords!);
        Assert.Equal(new short[] { 7700, 8800 }, tuple.IntermediateEndCoords!);
        Assert.Equal(default, diagnostic);
    }

    // A legal int16 delta at the signed boundary must encode, not throw: the absolute
    // value of short.MinValue has no short representation.
    [Fact]
    public void CvarDeltaEditAcceptsTheInt16Boundary()
    {
        CvarTable model = ReadCvar(CvarWire(2, [4000, 5000], null, null, [0]), 2, 1);
        ReadStatus status = model.TryWithTupleDeltas(0, [short.MinValue], out var updated,
            out var diagnostic);
        Assert.Equal(ReadStatus.Done, status);
        Assert.NotNull(updated);
        Assert.Equal(new short[] { short.MinValue }, updated!.Tuples[0].XDeltas);
        Assert.Equal(0, updated.Tuples[0].SerializedSlack);
        Assert.Equal(default, diagnostic);
    }

    // ---- gvar -------------------------------------------------------------------

    // One gvar 1.0 table over one glyph: header (20), short-form offsets, no shared
    // tuples, one GlyphVariationData block (padded to the even short-form stride).
    private static byte[] GvarWire(int axisCount, short[] peak, short[]? start,
        short[]? end, short[] deltas) =>
        GvarWireMulti(axisCount, GvarBlock(axisCount, peak, start, end, deltas));

    // One GlyphVariationData block with a private point block (one point) and the
    // packed X then Y runs. The block is returned unpadded; GvarWireMulti lays the
    // even short-form stride.
    private static byte[] GvarBlock(int axisCount, short[] peak, short[]? start,
        short[]? end, short[] deltas)
    {
        bool intermediate = start is not null;
        int tupleIndex = 0x8000 | (intermediate ? 0x4000 : 0) | 0x2000;
        int headerSize = 4 + 2 * axisCount + (intermediate ? 4 * axisCount : 0);
        int dataOffset = 4 + headerSize;
        byte[] points = [0x01, 0x00, 0x00];
        byte[] deltaBytes = DeltaRun(deltas);
        int runLength = points.Length + deltaBytes.Length;
        var block = new byte[dataOffset + runLength];
        BinaryPrimitives.WriteUInt16BigEndian(block.AsSpan(0), 1);
        BinaryPrimitives.WriteUInt16BigEndian(block.AsSpan(2), (ushort)dataOffset);
        BinaryPrimitives.WriteUInt16BigEndian(block.AsSpan(4), (ushort)runLength);
        BinaryPrimitives.WriteUInt16BigEndian(block.AsSpan(6), (ushort)tupleIndex);
        int at = 8;
        foreach (short coordinate in peak)
        {
            BinaryPrimitives.WriteInt16BigEndian(block.AsSpan(at), coordinate);
            at += 2;
        }
        if (intermediate)
        {
            foreach (short coordinate in start!)
            {
                BinaryPrimitives.WriteInt16BigEndian(block.AsSpan(at), coordinate);
                at += 2;
            }
            foreach (short coordinate in end!)
            {
                BinaryPrimitives.WriteInt16BigEndian(block.AsSpan(at), coordinate);
                at += 2;
            }
        }
        points.CopyTo(block.AsSpan(dataOffset));
        deltaBytes.CopyTo(block.AsSpan(dataOffset + points.Length));
        return block;
    }

    // One gvar over N glyphs: header (20), the short offsets array, no shared tuples,
    // then the blocks on the even short-form stride.
    private static byte[] GvarWireMulti(int axisCount, params byte[][] blocks)
    {
        int glyphCount = blocks.Length;
        const int offsetsAt = 20;
        int gvdAt = offsetsAt + (glyphCount + 1) * 2;
        int total = gvdAt + blocks.Sum(block => (block.Length + 1) & ~1);
        var bytes = new byte[total];
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(0), 0x00010000);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(4), (ushort)axisCount);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(6), 0);   // no shared tuples
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(8), (uint)gvdAt);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(12), (ushort)glyphCount);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(14), 0);  // short offsets
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(16), (uint)gvdAt);
        int offset = 0;
        for (int i = 0; i < glyphCount; i++)
        {
            BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(offsetsAt + 2 * i),
                (ushort)(offset / 2));
            blocks[i].CopyTo(bytes.AsSpan(gvdAt + offset));
            offset += (blocks[i].Length + 1) & ~1;
        }
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(offsetsAt + 2 * glyphCount),
            (ushort)(offset / 2));
        return bytes;
    }

    private static GvarTable ReadGvar(byte[] wire, int axisCount, int numGlyphs)
    {
        ReadStatus status = GvarView.TryOpen(wire, out var view, out var diagnostic, Source);
        Assert.Equal(ReadStatus.Done, status);
        status = view.TryReadTable(axisCount, numGlyphs, out var model, out diagnostic);
        Assert.Equal(ReadStatus.Done, status);
        return model!;
    }

    private static GlyphVariationDataTable ReadGlyph(GvarTable model, int glyphId)
    {
        ReadStatus status = model.TryReadGlyphVariationData(glyphId, pointCount: -1,
            out var data, out bool present, out var diagnostic);
        Assert.Equal(ReadStatus.Done, status);
        Assert.True(present);
        return data!;
    }

    // The intermediate region is axis-major: every start, then every end. Interleaving
    // them per axis changed the region a two-axis tuple describes.
    [Fact]
    public void GvarIntermediateEditWritesAxisMajorGroups()
    {
        short[] peak = [4000, 5000], start = [1000, 2000], end = [7000, 8000];
        GvarTable model = ReadGvar(GvarWire(2, peak, start, end, [0, 0]), 2, 1);
        Assert.Equal(peak, ReadGlyph(model, 0).Tuples[0].PeakCoords!);

        ReadStatus status = model.TryWithGlyphIntermediateCoords(0, 0, [1100, 2200],
            [7700, 8800], out var updated, out var diagnostic);
        Assert.Equal(ReadStatus.Done, status);
        Assert.NotNull(updated);
        TupleVariation tuple = ReadGlyph(updated!, 0).Tuples[0];
        Assert.Equal(peak, tuple.PeakCoords!);
        Assert.Equal(new short[] { 1100, 2200 }, tuple.IntermediateStartCoords!);
        Assert.Equal(new short[] { 7700, 8800 }, tuple.IntermediateEndCoords!);
        Assert.Equal(default, diagnostic);
    }

    // A sparse tuple's column length is its point count, not twice it: the X and Y
    // columns each carry one delta per point. The old check demanded 2N per column, so
    // the legal N-length request refused while a 2N request overflowed into the Y
    // column on read-back.
    [Fact]
    public void GvarSparseDeltaEditUsesThePerColumnPointCount()
    {
        GvarTable model = ReadGvar(GvarWire(2, [0, 0], null, null, [0, 0]), 2, 1);
        ReadStatus status = model.TryWithGlyphDeltas(0, 0, [30], [40], out var updated,
            out var diagnostic);
        Assert.Equal(ReadStatus.Done, status);
        Assert.NotNull(updated);
        TupleVariation tuple = ReadGlyph(updated!, 0).Tuples[0];
        Assert.Equal(new short[] { 30 }, tuple.XDeltas);
        Assert.Equal(new short[] { 40 }, tuple.YDeltas);
        Assert.Equal(0, tuple.SerializedSlack);
        Assert.Equal(default, diagnostic);

        // The doubled columns stay a count mismatch, never a silent X-into-Y read.
        status = model.TryWithGlyphDeltas(0, 0, [30, 31], [40, 41], out var rejected,
            out diagnostic);
        Assert.Equal(ReadStatus.InvalidFormat, status);
        Assert.Null(rejected);
        Assert.Equal(DiagnosticCode.InvalidValue, diagnostic.Code);
        Assert.Equal(2UL, diagnostic.Detail);
        Assert.Equal(1UL, diagnostic.Expected);
    }

    // The edit can leave an odd block. The short-offset form pads it to the even /2
    // stride, so the offsets array must follow the padded layout: writing the model's
    // odd offset truncates it and the compiled table cannot be read back.
    [Fact]
    public void GvarSparseEditCompilesToAReadableShortFormTable()
    {
        GvarTable model = ReadGvar(GvarWire(2, [0, 0], null, null, [0, 0]), 2, 1);
        ReadStatus status = model.TryWithGlyphDeltas(0, 0, [30], [40], out var edited,
            out var diagnostic);
        Assert.Equal(ReadStatus.Done, status);
        Assert.Equal(19, edited!.GlyphVariationDataOffsets[1]); // the odd edited block

        status = GvarTableCompiler.TryCompile(edited, out var compiled, out diagnostic);
        Assert.Equal(ReadStatus.Done, status);
        Assert.NotNull(compiled);
        GvarTable reread = ReadGvar(compiled!, 2, 1);
        TupleVariation tuple = ReadGlyph(reread, 0).Tuples[0];
        Assert.Equal(new short[] { 30 }, tuple.XDeltas);
        Assert.Equal(new short[] { 40 }, tuple.YDeltas);
        Assert.Equal(default, diagnostic);
    }

    // The same layout rule across glyphs: after an odd first block is padded, the next
    // glyph's offset must point past the pad, not at the unpadded model offset.
    [Fact]
    public void GvarSecondGlyphStaysAddressableAfterAnOddFirstBlock()
    {
        GvarTable model = ReadGvar(GvarWireMulti(2,
            GvarBlock(2, [0, 0], null, null, [0, 0]),
            GvarBlock(2, [0, 0], null, null, [0x10, 0x20])), 2, 2);
        ReadStatus status = model.TryWithGlyphDeltas(0, 0, [30], [40], out var edited,
            out var diagnostic);
        Assert.Equal(ReadStatus.Done, status);

        status = GvarTableCompiler.TryCompile(edited!, out var compiled, out diagnostic);
        Assert.Equal(ReadStatus.Done, status);
        GvarTable reread = ReadGvar(compiled!, 2, 2);
        TupleVariation first = ReadGlyph(reread, 0).Tuples[0];
        Assert.Equal(new short[] { 30 }, first.XDeltas);
        Assert.Equal(new short[] { 40 }, first.YDeltas);
        TupleVariation second = ReadGlyph(reread, 1).Tuples[0];
        Assert.Equal(new short[] { 0x10 }, second.XDeltas);
        Assert.Equal(new short[] { 0x20 }, second.YDeltas);
        Assert.Equal(default, diagnostic);
    }

    // ---- parts-built stores (TryCreate) -----------------------------------------

    private static TupleVariation Tuple(ushort tupleIndex, short[] peak, bool hasPrivatePoints,
        int[]? points, short[] x, short[] y)
    {
        ReadStatus status = TupleVariation.Create(tupleIndex, peak, null, null,
            hasPrivatePoints, points, x, y, out var tuple, out var diagnostic);
        Assert.Equal(ReadStatus.Done, status);
        Assert.Equal(default, diagnostic);
        return tuple!;
    }

    private static GlyphVariationDataTable Block(int[]? sharedPoints,
        params TupleVariation[] tuples)
    {
        var location = DataLocation.Anonymous(0x67766172);
        ReadStatus status = GlyphVariationDataTable.Create((ushort)tuples.Length, sharedPoints,
            tuples, location, out var block, out var diagnostic);
        Assert.Equal(ReadStatus.Done, status);
        Assert.Equal(default, diagnostic);
        return block!;
    }

    // The shared point block sits at dataOffset, not behind it: a parts-built store
    // with the shared all-points marker must read back at the same offset the reader
    // walks.
    [Fact]
    public void GvarCreateRoundTripsTheSharedAllPointsMarker()
    {
        var block = Block([], Tuple(0x8000, [0, 0], false, null, [1, 2, 3, 4], [5, 6, 7, 8]));
        ReadStatus status = GvarTableCompiler.TryCreate(2, 1, 0, [], [block],
            out var model, out var diagnostic);
        Assert.Equal(ReadStatus.Done, status);
        Assert.NotNull(model);
        status = GvarTableCompiler.TryCompile(model!, out var compiled, out diagnostic);
        Assert.Equal(ReadStatus.Done, status);
        GvarTable reread = ReadGvar(compiled!, 2, 1);
        status = reread.TryReadGlyphVariationData(0, pointCount: 0, out var data,
            out bool present, out diagnostic);
        Assert.Equal(ReadStatus.Done, status);
        Assert.True(present);
        Assert.Equal(new short[] { 1, 2, 3, 4 }, data!.Tuples[0].XDeltas);
        Assert.Equal(new short[] { 5, 6, 7, 8 }, data.Tuples[0].YDeltas);
        Assert.Equal(default, diagnostic);
    }

    // The explicit shared point list follows the same offset rule.
    [Fact]
    public void GvarCreateRoundTripsASharedExplicitPointList()
    {
        var block = Block([0], Tuple(0x8000, [0, 0], false, null, [30], [40]));
        ReadStatus status = GvarTableCompiler.TryCreate(2, 1, 0, [], [block],
            out var model, out var diagnostic);
        Assert.Equal(ReadStatus.Done, status);
        status = GvarTableCompiler.TryCompile(model!, out var compiled, out diagnostic);
        Assert.Equal(ReadStatus.Done, status);
        TupleVariation tuple = ReadGlyph(ReadGvar(compiled!, 2, 1), 0).Tuples[0];
        Assert.Equal(new short[] { 30 }, tuple.XDeltas);
        Assert.Equal(new short[] { 40 }, tuple.YDeltas);
        Assert.Equal(default, diagnostic);
    }

    // The cvar factory's shared all-CVTs marker follows the same dataOffset rule; the
    // store's own read-back is the final gate before the model is returned.
    [Fact]
    public void CvarCreateRoundTripsTheSharedAllCvtMarker()
    {
        var location = DataLocation.Anonymous(0x63766172);
        ReadStatus status = TupleVariation.Create(0x8000, [0, 0], null, null,
            false, null, [30], [], out var tuple, out var diagnostic);
        Assert.Equal(ReadStatus.Done, status);
        status = CvarTableCompiler.TryCreate(2, 1, [], [tuple!], out var model,
            out diagnostic, location: location);
        Assert.Equal(ReadStatus.Done, status);
        Assert.NotNull(model);
        status = CvarTableCompiler.TryCompile(model!, out var compiled, out diagnostic);
        Assert.Equal(ReadStatus.Done, status);
        CvarTable reread = ReadCvar(compiled!, 2, 1);
        Assert.Equal(new short[] { 30 }, reread.Tuples[0].XDeltas);
        Assert.Equal(default, diagnostic);
    }
}
