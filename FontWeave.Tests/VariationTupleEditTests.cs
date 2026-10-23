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
        short[]? end, short[] deltas)
    {
        bool intermediate = start is not null;
        int tupleIndex = 0x8000 | (intermediate ? 0x4000 : 0) | 0x2000;
        int headerSize = 4 + 2 * axisCount + (intermediate ? 4 * axisCount : 0);
        int dataOffset = 4 + headerSize;
        byte[] points = [0x01, 0x00, 0x00];
        byte[] deltaBytes = DeltaRun(deltas);
        int runLength = points.Length + deltaBytes.Length;
        int blockLength = dataOffset + runLength;
        int padded = (blockLength + 1) & ~1;
        var block = new byte[padded];
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

        const int gvdAt = 20 + 2 * 2; // header + (glyphCount + 1) short offsets
        var bytes = new byte[gvdAt + block.Length];
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(0), 0x00010000);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(4), (ushort)axisCount);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(6), 0);   // no shared tuples
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(8), gvdAt);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(12), 1);  // one glyph
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(14), 0);  // short offsets
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(16), gvdAt);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(20), 0);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(22), (ushort)(block.Length / 2));
        block.CopyTo(bytes.AsSpan(gvdAt));
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
}
