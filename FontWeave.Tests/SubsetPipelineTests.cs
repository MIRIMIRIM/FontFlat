// Success-path tests for the subset operation pipeline.
//
// Commit ① serves the all-preserve (zero-rebuild, cmap-less) profile end to end: the
// decision pipeline completes before any payload copy, and the emitter publishes exactly
// the preserved tables. Later commits extend these with the glyf/loca/cmap rebuilds.
using System.Buffers.Binary;
using FontWeave.Core;
using FontWeave.Operations;
using FontWeave.Operations.Subset;
using Xunit;

namespace FontWeave.Tests;

public sealed class SubsetPipelineTests
{
    [Fact]
    public void AllPreserveRequestProducesIdentityTables()
    {
        byte[] head = SubsetFontFixture.Head();
        byte[] gasp = SubsetFontFixture.Dummy(8);
        var face = SubsetFontFixture.Face(
            (SubsetFontFixture.HeadTag, head),
            (SubsetFontFixture.MaxpTag, SubsetFontFixture.Maxp(4)),
            (0x67617370, gasp));
        var request = new SubsetRequest
        {
            Face = face,
            Root = new SubsetGlyphRoot
            {
                Kind = SubsetRootKind.ExplicitGlyphSet,
                Gids = [0]
            },
            Mode = SubsetMode.RetainGids
        };
        var status = SubsetOperation.TryPrepare(request, out var result, out var report,
            out var diagnostic);
        Assert.Equal(ReadStatus.Done, status);
        Assert.Equal(DiagnosticCode.None, diagnostic.Code);
        Assert.NotNull(result);
        Assert.NotNull(report);
        Assert.Null(result!.Map);
        Assert.Null(report!.MapArtifactSha256);
        Assert.Empty(report.Diagnostics);
        Assert.True(report.MaxpMaximaNotRecomputed);
        Assert.True(report.OptionRecords.Count >= 5);

        // Only GID 0 is selected; every other slot is unselected and reported.
        Assert.Equal(3, report.UnselectedSlotCount);
        Assert.Empty(report.ClosureAddedGids);
        Assert.Empty(report.DroppedTables);

        // Every decision is a matrix-row preserve, and the compiled font's plain
        // decisions agree with the report.
        Assert.Equal(3, report.Decisions.Count);
        foreach (var decision in report.Decisions)
        {
            Assert.Equal(TableAction.Preserve, decision.Action);
            Assert.Equal(SubsetPolicy.RetainRaw, decision.Policy);
            Assert.Equal(SubsetDecisionOrigin.MatrixRow, decision.Origin);
        }
        Assert.Equal(report.Decisions.Count, result.Font.Decisions.Length);
        for (int i = 0; i < report.Decisions.Count; i++)
        {
            Assert.Equal(report.Decisions[i].Tag, result.Font.Decisions[i].Tag);
            Assert.Equal(report.Decisions[i].Action, result.Font.Decisions[i].Action);
        }

        // The output publishes through TryCopyTo and keeps the preserved tables'
        // bytes verbatim, including the untouched head.
        byte[] output = new byte[result.Font.OutputLength];
        var copyStatus = result.Font.TryCopyTo(output, out int written, out var copyDiagnostic);
        Assert.Equal(ReadStatus.Done, copyStatus);
        Assert.Equal(DiagnosticCode.None, copyDiagnostic.Code);
        Assert.Equal(output.Length, written);
        Assert.Equal(3, BinaryPrimitives.ReadUInt16BigEndian(output.AsSpan(4)));
        Assert.True(TryFindTable(output, SubsetFontFixture.HeadTag, out int headAt,
            out int headLength));
        Assert.Equal(head.Length, headLength);
        // The emitter recomputes head.checkSumAdjustment for the published bytes; every
        // other head byte moves verbatim.
        Assert.True(output.AsSpan(headAt, 8).SequenceEqual(head.AsSpan(0, 8)));
        Assert.True(output.AsSpan(headAt + 12, headLength - 12)
            .SequenceEqual(head.AsSpan(12, head.Length - 12)));
        Assert.True(TryFindTable(output, 0x67617370, out int gaspAt, out int gaspLength));
        Assert.True(output.AsSpan(gaspAt, gaspLength).SequenceEqual(gasp));
    }

    private static bool TryFindTable(byte[] sfnt, uint tag, out int offset, out int length)
    {
        offset = 0;
        length = 0;
        ushort count = BinaryPrimitives.ReadUInt16BigEndian(sfnt.AsSpan(4));
        for (int i = 0; i < count; i++)
        {
            int record = 12 + 16 * i;
            if (BinaryPrimitives.ReadUInt32BigEndian(sfnt.AsSpan(record)) != tag) continue;
            offset = (int)BinaryPrimitives.ReadUInt32BigEndian(sfnt.AsSpan(record + 8));
            length = (int)BinaryPrimitives.ReadUInt32BigEndian(sfnt.AsSpan(record + 12));
            return true;
        }
        return false;
    }
}
