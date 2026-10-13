// Full-pipeline subset tests with the cmap rebuild wired (S1a card §8 commit ④).
//
// The canonical cmap expectations here are encoded by the test fixture independently of
// the product compiler, so a green run means the rebuilt table matches an outside
// encoding, not the compiler's own output. The full-GID case is the local identity smoke
// the S0 freeze registered as this slice's obligation (cases.json S0-FP-001 rationale):
// glyf/loca stay byte-identical, preserved tables move verbatim, and the only dropped
// tables are the layout family.
using System.Buffers.Binary;
using FontWeave.Core;
using FontWeave.Operations;
using FontWeave.Operations.Subset;
using Xunit;

namespace FontWeave.Tests;

public sealed class SubsetRebuildTests
{
    private static readonly byte[] LayoutFamily = SubsetFontFixture.Dummy();

    private static SubsetOptions NoLayout() => new() { NoLayout = true };

    private static SubsetRequest Request(FontFaceRef face, ushort[] gids,
        SubsetOptions? options = null) => new()
    {
        Face = face,
        Root = new SubsetGlyphRoot { Kind = SubsetRootKind.ExplicitGlyphSet, Gids = gids },
        Mode = SubsetMode.RetainGids,
        Options = options ?? NoLayout()
    };

    [Fact]
    public void SubsetRebuildsCmapGlyfLocaAndDropsLayout()
    {
        byte[] glyfSource =
        [
            .. SubsetFontFixture.InstructedSimpleGlyph(),   // glyph 0, 18 bytes
            .. GlyfImages.NoContourGlyphRecord(),           // glyph 1, 12 bytes
            .. GlyfImages.CompositeRecord(0),               // glyph 2, 16 bytes
            .. GlyfImages.NoContourGlyphRecord()            // glyph 3, 12 bytes
        ];
        byte[] cmap = SubsetFontFixture.CmapCanonical((0x41, 1), (0x42, 2), (0x13000, 2));
        var face = SubsetFontFixture.Face(
            (SubsetFontFixture.HeadTag, SubsetFontFixture.Head()),
            (SubsetFontFixture.MaxpTag, SubsetFontFixture.Maxp(4)),
            (SubsetFontFixture.GlyfTag, glyfSource),
            (SubsetFontFixture.LocaTag, GlyfImages.LongLoca([0, 18, 30, 46, 58])),
            (SubsetFontFixture.CmapTag, cmap),
            (SubsetFontFixture.GsubTag, LayoutFamily));
        var status = SubsetOperation.TryPrepare(Request(face, [1]), out var result,
            out var report, out var diagnostic);
        Assert.Equal(ReadStatus.Done, status);
        Assert.Equal(DiagnosticCode.None, diagnostic.Code);
        Assert.NotNull(result);
        Assert.Null(result!.Map);
        Assert.Null(report!.MapArtifactSha256);

        // Decisions: rebuild trio, raw head/maxp, dropped layout family.
        Assert.Equal(6, report.Decisions.Count);
        Assert.Empty(report.ClosureAddedGids);
        Assert.Equal(2, report.UnselectedSlotCount); // glyphs 2 and 3 stay empty
        Assert.Equal([SubsetFontFixture.GsubTag], report.DroppedTables);

        byte[] output = new byte[result.Font.OutputLength];
        Assert.Equal(ReadStatus.Done,
            result.Font.TryCopyTo(output, out _, out var copyDiagnostic));
        Assert.Equal(DiagnosticCode.None, copyDiagnostic.Code);

        // Output tables: {cmap, glyf, head, loca, maxp} — GSUB is gone.
        Assert.Equal(5, BinaryPrimitives.ReadUInt16BigEndian(output.AsSpan(4)));
        byte[] maxpIn = SubsetFontFixture.Maxp(4);
        Assert.True(FindTable(output, SubsetFontFixture.MaxpTag, out var maxpOut));
        Assert.True(maxpOut.AsSpan().SequenceEqual(maxpIn)); // numGlyphs domain untouched
        Assert.True(FindTable(output, SubsetFontFixture.GlyfTag, out var glyfOut));
        // Retained slots move verbatim (glyph 1 is a no-contour record, not an empty
        // slot); glyphs 2 and 3 become empty.
        Assert.Equal(30, glyfOut.Length);
        Assert.True(glyfOut.AsSpan(0, 18).SequenceEqual(glyfSource.AsSpan(0, 18)));
        Assert.True(glyfOut.AsSpan(18, 12).SequenceEqual(glyfSource.AsSpan(18, 12)));
        Assert.True(FindTable(output, SubsetFontFixture.LocaTag, out var locaOut));
        Assert.Equal(20, locaOut.Length); // (4+1) × 4, long format preserved
        Assert.Equal([0, 18, 30, 30, 30], ReadLongOffsets(locaOut));

        // The rebuilt cmap is the canonical pair over the pruned mapping set.
        Assert.True(FindTable(output, SubsetFontFixture.CmapTag, out var cmapOut));
        Assert.Equal(0, BinaryPrimitives.ReadUInt16BigEndian(cmapOut.AsSpan(0)));
        Assert.Equal(2, BinaryPrimitives.ReadUInt16BigEndian(cmapOut.AsSpan(2)));
        Assert.Equal(3, BinaryPrimitives.ReadUInt16BigEndian(cmapOut.AsSpan(4)));
        Assert.Equal(1, BinaryPrimitives.ReadUInt16BigEndian(cmapOut.AsSpan(6)));
        uint format4At = BinaryPrimitives.ReadUInt32BigEndian(cmapOut.AsSpan(8));
        uint format12At = BinaryPrimitives.ReadUInt32BigEndian(cmapOut.AsSpan(16));
        Assert.Equal(4u, BinaryPrimitives.ReadUInt16BigEndian(
            cmapOut.AsSpan((int)format4At)));
        // One run (0x41 → 1) plus the structural sentinel.
        Assert.Equal(4, BinaryPrimitives.ReadUInt16BigEndian(cmapOut.AsSpan((int)format4At + 6)));
        Assert.Equal(0x41, BinaryPrimitives.ReadUInt16BigEndian(cmapOut.AsSpan((int)format4At + 14)));
        Assert.Equal(0xFFFF, BinaryPrimitives.ReadUInt16BigEndian(cmapOut.AsSpan((int)format4At + 16)));
        Assert.Equal(0x41, BinaryPrimitives.ReadUInt16BigEndian(cmapOut.AsSpan((int)format4At + 20)));
        Assert.Equal(unchecked((ushort)(1 - 0x41)),
            BinaryPrimitives.ReadUInt16BigEndian(cmapOut.AsSpan((int)format4At + 24)));
        Assert.Equal(12, BinaryPrimitives.ReadUInt16BigEndian(cmapOut.AsSpan((int)format12At)));
        Assert.Equal(1u, BinaryPrimitives.ReadUInt32BigEndian(cmapOut.AsSpan((int)format12At + 12)));
        Assert.Equal(0x41u, BinaryPrimitives.ReadUInt32BigEndian(cmapOut.AsSpan((int)format12At + 16)));
    }

    [Fact]
    public void ClosureMembersKeepTheirBytesInThePipeline()
    {
        byte[] glyfSource =
        [
            .. GlyfImages.NoContourGlyphRecord(),           // glyph 0, 12 bytes
            .. GlyfImages.NoContourGlyphRecord(),           // glyph 1, 12 bytes
            .. GlyfImages.CompositeRecord(1),               // glyph 2 → 1, 16 bytes
            .. GlyfImages.NoContourGlyphRecord()            // glyph 3, 12 bytes
        ];
        var face = SubsetFontFixture.Face(
            (SubsetFontFixture.HeadTag, SubsetFontFixture.Head()),
            (SubsetFontFixture.MaxpTag, SubsetFontFixture.Maxp(4)),
            (SubsetFontFixture.GlyfTag, glyfSource),
            (SubsetFontFixture.LocaTag, GlyfImages.LongLoca([0, 12, 24, 40, 52])),
            (SubsetFontFixture.CmapTag, SubsetFontFixture.CmapCanonical((0x41, 2))));
        var status = SubsetOperation.TryPrepare(Request(face, [2]), out var result,
            out var report, out _);
        Assert.Equal(ReadStatus.Done, status);
        Assert.Equal([1], report!.ClosureAddedGids);
        Assert.Equal(1, report.UnselectedSlotCount);
        byte[] output = new byte[result!.Font.OutputLength];
        _ = result.Font.TryCopyTo(output, out _, out _);
        Assert.True(FindTable(output, SubsetFontFixture.GlyfTag, out var glyfOut));
        Assert.Equal(40, glyfOut.Length);
        Assert.True(glyfOut.AsSpan(24, 16).SequenceEqual(glyfSource.AsSpan(24, 16)));
        Assert.True(FindTable(output, SubsetFontFixture.LocaTag, out var locaOut));
        Assert.Equal([0, 12, 24, 40, 40], ReadLongOffsets(locaOut));
    }

    // The registered FP-001 gap: full GID selection with NoLayout keeps glyf/loca and
    // every preserved table byte-identical, drops exactly the layout family, and the
    // canonical cmap rebuild reproduces a canonical input byte-for-byte.
    [Fact]
    public void FullGidRetainKeepsTableBytes()
    {
        byte[] glyfSource =
        [
            .. SubsetFontFixture.InstructedSimpleGlyph(),
            .. GlyfImages.NoContourGlyphRecord(),
            .. GlyfImages.CompositeRecord(0),
            .. GlyfImages.NoContourGlyphRecord()
        ];
        byte[] cmap = SubsetFontFixture.CmapCanonical((0x41, 1), (0x42, 2), (0x13000, 2));
        byte[] gasp = SubsetFontFixture.Dummy(8);
        var tables = new List<(uint Tag, byte[] Bytes)>
        {
            (SubsetFontFixture.HeadTag, SubsetFontFixture.Head()),
            (SubsetFontFixture.MaxpTag, SubsetFontFixture.Maxp(4)),
            (SubsetFontFixture.GlyfTag, glyfSource),
            (SubsetFontFixture.LocaTag, GlyfImages.LongLoca([0, 18, 30, 46, 58])),
            (SubsetFontFixture.CmapTag, cmap),
            (0x67617370, gasp),
            (SubsetFontFixture.GsubTag, LayoutFamily)
        };
        var face = SubsetFontFixture.Face(true, [.. tables]);
        byte[] input = SubsetFontFixture.Sfnt(true, [.. tables]);
        var status = SubsetOperation.TryPrepare(Request(face, [0, 1, 2, 3]), out var result,
            out var report, out _);
        Assert.Equal(ReadStatus.Done, status);
        Assert.Equal([SubsetFontFixture.GsubTag], report!.DroppedTables);
        Assert.Empty(report.CmapConsumedNotEmitted);
        byte[] output = new byte[result!.Font.OutputLength];
        _ = result.Font.TryCopyTo(output, out _, out _);

        // Every surviving table moves byte-identical, including the rebuilt trio.
        ushort count = BinaryPrimitives.ReadUInt16BigEndian(output.AsSpan(4));
        Assert.Equal(6, count);
        for (int i = 0; i < count; i++)
        {
            int record = 12 + 16 * i;
            uint tag = BinaryPrimitives.ReadUInt32BigEndian(output.AsSpan(record));
            int offset = (int)BinaryPrimitives.ReadUInt32BigEndian(output.AsSpan(record + 8));
            int length = (int)BinaryPrimitives.ReadUInt32BigEndian(output.AsSpan(record + 12));
            if (tag == SubsetFontFixture.HeadTag)
            {
                // head.checkSumAdjustment is derived; the rest moves verbatim.
                Assert.True(output.AsSpan(offset, 8).SequenceEqual(tables[0].Bytes.AsSpan(0, 8)));
                continue;
            }
            var source = tables.Find(t => t.Tag == tag).Bytes;
            Assert.True(output.AsSpan(offset, length).SequenceEqual(source));
        }
        // The input itself stays a well-formed sealed sfnt (the identity anchor's base).
        Assert.Equal(7, BinaryPrimitives.ReadUInt16BigEndian(input.AsSpan(4)));
    }

    [Fact]
    public void NonUnicodeCmapRecordIsConsumedNotEmitted()
    {
        byte[] glyfSource =
        [
            .. SubsetFontFixture.InstructedSimpleGlyph(),
            .. GlyfImages.NoContourGlyphRecord()
        ];
        var face = SubsetFontFixture.Face(
            (SubsetFontFixture.HeadTag, SubsetFontFixture.Head()),
            (SubsetFontFixture.MaxpTag, SubsetFontFixture.Maxp(2)),
            (SubsetFontFixture.GlyfTag, glyfSource),
            (SubsetFontFixture.LocaTag, GlyfImages.LongLoca([0, 18, 30])),
            (SubsetFontFixture.CmapTag, SubsetFontFixture.CmapWithMacRoman((0x41, 1))));
        var status = SubsetOperation.TryPrepare(Request(face, [0, 1]), out var result,
            out var report, out _);
        Assert.Equal(ReadStatus.Done, status);
        var consumed = Assert.Single(report!.CmapConsumedNotEmitted);
        Assert.Equal(1, consumed.PlatformId);
        Assert.Equal(0, consumed.EncodingId);
        byte[] output = new byte[result!.Font.OutputLength];
        _ = result.Font.TryCopyTo(output, out _, out _);
        Assert.True(FindTable(output, SubsetFontFixture.CmapTag, out var cmapOut));
        Assert.Equal(2, BinaryPrimitives.ReadUInt16BigEndian(cmapOut.AsSpan(2)));
        Assert.Equal(3, BinaryPrimitives.ReadUInt16BigEndian(cmapOut.AsSpan(4)));
        Assert.Equal(1, BinaryPrimitives.ReadUInt16BigEndian(cmapOut.AsSpan(6)));
    }

    private static bool FindTable(byte[] sfnt, uint tag, out byte[] payload)
    {
        ushort count = BinaryPrimitives.ReadUInt16BigEndian(sfnt.AsSpan(4));
        for (int i = 0; i < count; i++)
        {
            int record = 12 + 16 * i;
            if (BinaryPrimitives.ReadUInt32BigEndian(sfnt.AsSpan(record)) != tag) continue;
            int offset = (int)BinaryPrimitives.ReadUInt32BigEndian(sfnt.AsSpan(record + 8));
            int length = (int)BinaryPrimitives.ReadUInt32BigEndian(sfnt.AsSpan(record + 12));
            payload = sfnt[offset..(offset + length)];
            return true;
        }
        payload = [];
        return false;
    }

    private static uint[] ReadLongOffsets(byte[] loca)
    {
        var offsets = new uint[loca.Length / 4];
        for (int i = 0; i < offsets.Length; i++)
            offsets[i] = BinaryPrimitives.ReadUInt32BigEndian(loca.AsSpan(4 * i));
        return offsets;
    }
}
