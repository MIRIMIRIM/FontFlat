// HVAR metric read face.
//
// The wire is assembled from the raw format directly, never by asking the product to
// write it. The cases pin the NULL advance map's implicit reading and the NULL
// side-bearing maps' no-variation answer.
using System.Buffers.Binary;
using FontWeave.Core;
using FontWeave.Tables.Variation;
using Xunit;

namespace FontWeave.Tests;

public sealed class HvarMetricReadTests
{
    private static readonly DataLocation Source = new(11, 0, 0, -1, DataField.None,
        OffsetBasis.Table, 0);

    // HVAR 1.0 over one region axis and one VarData row per glyph, with all three
    // optional maps NULL: only the advance-width metric has the spec's implicit
    // (0, gid) reading, so lsb/rsb carry no variation data.
    private static byte[] HvarWire(int numGlyphs)
    {
        const int ivsAt = 20, regionAt = 12, varDataAt = 22;
        int ivsLength = varDataAt + 6 + 2 + numGlyphs;
        var bytes = new byte[ivsAt + ivsLength];
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(0), 0x00010000);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(4), ivsAt);
        // advance/lsb/rsb map offsets stay zero (NULL)
        int ivs = ivsAt;
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(ivs), 1);          // format 1
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(ivs + 2), regionAt);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(ivs + 6), 1);      // one VarData
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(ivs + 8), varDataAt);
        int region = ivs + regionAt;
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(region), 1);       // axisCount
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(region + 2), 1);   // regionCount
        BinaryPrimitives.WriteInt16BigEndian(bytes.AsSpan(region + 4), 0);    // start
        BinaryPrimitives.WriteInt16BigEndian(bytes.AsSpan(region + 6), 0x4000);
        BinaryPrimitives.WriteInt16BigEndian(bytes.AsSpan(region + 8), 0x4000);
        int varData = ivs + varDataAt;
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(varData), (ushort)numGlyphs);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(varData + 2), 0);  // int8 deltas
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(varData + 4), 1);  // one region
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(varData + 6), 0);
        for (int gid = 0; gid < numGlyphs; gid++) bytes[varData + 8 + gid] = 42;
        return bytes;
    }

    [Fact]
    public void HvarNullSideMapsCarryNoSideBearingVariation()
    {
        const int numGlyphs = 3;
        ReadStatus status = HvarView.TryOpen(HvarWire(numGlyphs), out var view,
            out var diagnostic, Source);
        Assert.Equal(ReadStatus.Done, status);
        status = view.TryReadTable(axisCount: 1, numGlyphs, out var model, out diagnostic);
        Assert.Equal(ReadStatus.Done, status);
        Assert.Null(model!.LsbMapping);
        Assert.Null(model.RsbMapping);

        status = model.TryGetDeltaSets(HvarMetric.Advance, 1, out bool hasAdvance,
            out var advanceDeltas, out diagnostic);
        Assert.Equal(ReadStatus.Done, status);
        Assert.True(hasAdvance);
        Assert.Equal(new[] { 42 }, advanceDeltas.ToArray());

        status = model.TryGetDeltaSets(HvarMetric.Lsb, 1, out bool hasLsb,
            out var lsbDeltas, out diagnostic);
        Assert.Equal(ReadStatus.Done, status);
        Assert.False(hasLsb);
        Assert.True(lsbDeltas.IsEmpty);

        status = model.TryGetDeltaSets(HvarMetric.Rsb, 1, out bool hasRsb,
            out var rsbDeltas, out diagnostic);
        Assert.Equal(ReadStatus.Done, status);
        Assert.False(hasRsb);
        Assert.True(rsbDeltas.IsEmpty);
    }
}
