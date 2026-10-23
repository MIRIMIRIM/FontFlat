using System.Buffers.Binary;
using FontWeave.Containers;
using FontWeave.Core;
using FontWeave.Operations;
using FontWeave.Operations.Subset;
using FontWeave.Tables.Variation;
using Xunit;

namespace FontWeave.Tests;

public sealed class SubsetDehintCvarTests
{
    [Theory]
    [InlineData(SubsetMode.RetainGids)]
    [InlineData(SubsetMode.CompactRemap)]
    public void DropHintingRemovesCvarAndCvtAndLeavesReusableVariableFont(SubsetMode mode)
    {
        ReadStatus status = SubsetOperation.TryPrepare(Request(Face(), mode, true, 3),
            out var result, out var report, out _);

        Assert.Equal(ReadStatus.Done, status);
        Assert.NotNull(result);
        byte[] output = FontBytes(result);
        var tables = Tables(output);
        foreach (uint tag in new[] { SubsetFontFixture.CvtTag, SubsetFontFixture.CvarTag })
        {
            Assert.DoesNotContain(tag, tables.Keys);
            Assert.Contains(tag, report!.DroppedTables);
            var decision = Assert.Single(report.Decisions, d => d.Tag == tag);
            Assert.Equal(TableAction.Drop, decision.Action);
            Assert.Equal(SubsetPolicy.Delete, decision.Policy);
            Assert.Equal(SubsetDecisionOrigin.RequestOption, decision.Origin);
        }
        Assert.Equal(SubsetFontFixture.Fvar(), tables[SubsetFontFixture.FvarTag]);
        Assert.Equal(mode == SubsetMode.CompactRemap ? 3 : 4,
            BinaryPrimitives.ReadUInt16BigEndian(tables[SubsetFontFixture.GvarTag].AsSpan(12)));

        ushort retainedComposite = mode == SubsetMode.CompactRemap ? (ushort)2 : (ushort)3;
        uint compositeStart = SubsetFontFixture.LocaOffset(tables[SubsetFontFixture.HeadTag],
            tables[SubsetFontFixture.LocaTag], retainedComposite);
        Assert.Equal(mode == SubsetMode.CompactRemap ? 1 : 2,
            BinaryPrimitives.ReadUInt16BigEndian(
                tables[SubsetFontFixture.GlyfTag].AsSpan((int)compositeStart + 12)));
        Assert.Equal(ReadStatus.Done, FontSource.TryCopy(output, out var source, out _));
        Assert.Equal(ReadStatus.Done, source!.TryGetFace(0, out var face, out _));
        status = SubsetOperation.TryPrepare(Request(face, mode, false, retainedComposite),
            out var second, out _, out var diagnostic);
        Assert.Equal(ReadStatus.Done, status);
        Assert.Equal(default, diagnostic);
        Assert.NotNull(second);
        var secondTables = Tables(FontBytes(second));
        Assert.DoesNotContain(SubsetFontFixture.CvarTag, secondTables.Keys);
        Assert.DoesNotContain(SubsetFontFixture.CvtTag, secondTables.Keys);
    }

    [Theory]
    [InlineData(SubsetMode.RetainGids)]
    [InlineData(SubsetMode.CompactRemap)]
    public void KeepingHintingPreservesCvarAndCvtTogether(SubsetMode mode)
    {
        ReadStatus status = SubsetOperation.TryPrepare(Request(Face(), mode, false, 3),
            out var result, out var report, out _);

        Assert.Equal(ReadStatus.Done, status);
        Assert.NotNull(result);
        var tables = Tables(FontBytes(result));
        Assert.Equal(Cvar(), tables[SubsetFontFixture.CvarTag]);
        Assert.Equal(new byte[] { 0, 64, 0, 128 }, tables[SubsetFontFixture.CvtTag]);
        foreach (uint tag in new[] { SubsetFontFixture.CvtTag, SubsetFontFixture.CvarTag })
        {
            Assert.DoesNotContain(tag, report!.DroppedTables);
            Assert.Equal(TableAction.Preserve,
                Assert.Single(report.Decisions, d => d.Tag == tag).Action);
        }
    }

    private static SubsetRequest Request(FontFaceRef face, SubsetMode mode,
        bool dropHinting, ushort root) => new()
    {
        Face = face,
        Root = new SubsetGlyphRoot { Kind = SubsetRootKind.ExplicitGlyphSet, Gids = [0, root] },
        Mode = mode,
        Options = new SubsetOptions { NoLayout = true, DropHinting = dropHinting }
    };

    private static byte[] Cvar() =>
    [
        0x00, 0x01, 0x00, 0x00,
        0x00, 0x01, 0x00, 0x0E,
        0x00, 0x05, 0xA0, 0x00, 0x40, 0x00,
        0x01, 0x00, 0x00, 0x00, 0x19
    ];

    private static FontFaceRef Face()
    {
        byte[] cvar = Cvar();
        Assert.Equal(ReadStatus.Done, CvarView.TryOpen(cvar, out var view, out _));
        Assert.Equal(ReadStatus.Done, view.TryReadTable(1, 2, out var model, out _));
        var tuple = Assert.Single(model!.Tuples);
        Assert.Equal(new short[] { 16384 }, tuple.PeakCoords);
        Assert.Equal(new[] { 0 }, tuple.PointNumbers);
        Assert.Equal(new short[] { 25 }, tuple.XDeltas);
        var (loca, glyf, _) = GlyfImages.Assemble(
        [
            GlyfImages.NoContourGlyphRecord(),
            GlyfImages.NoContourGlyphRecord(),
            SubsetFontFixture.InstructedSimpleGlyph(),
            GlyfImages.CompositeRecord(2)
        ]);
        return SubsetFontFixture.Face(
            (SubsetFontFixture.HeadTag, SubsetFontFixture.Head()),
            (SubsetFontFixture.MaxpTag, SubsetFontFixture.Maxp(4)),
            (SubsetFontFixture.GlyfTag, glyf),
            (SubsetFontFixture.LocaTag, loca),
            (SubsetFontFixture.CmapTag, SubsetFontFixture.CmapCanonical((0x41u, 3))),
            (SubsetCompactPolicyTests.HheaTag, SubsetCompactPolicyTests.Hhea(4)),
            (SubsetCompactPolicyTests.HmtxTag, SubsetCompactPolicyTests.Hmtx(4, 4)),
            (SubsetFontFixture.FvarTag, SubsetFontFixture.Fvar()),
            (SubsetFontFixture.GvarTag, SubsetFontFixture.Gvar(1, 4)),
            (SubsetFontFixture.CvtTag, [0, 64, 0, 128]),
            (SubsetFontFixture.CvarTag, cvar));
    }

    private static byte[] FontBytes(SubsetResult result)
    {
        var bytes = new byte[result.Font.OutputLength];
        Assert.Equal(ReadStatus.Done, result.Font.TryCopyTo(bytes, out int written, out _));
        Assert.Equal(bytes.Length, written);
        return bytes;
    }

    private static Dictionary<uint, byte[]> Tables(byte[] font)
    {
        int count = BinaryPrimitives.ReadUInt16BigEndian(font.AsSpan(4));
        var tables = new Dictionary<uint, byte[]>();
        for (int i = 0; i < count; i++)
        {
            int record = 12 + 16 * i;
            uint tag = BinaryPrimitives.ReadUInt32BigEndian(font.AsSpan(record));
            int offset = (int)BinaryPrimitives.ReadUInt32BigEndian(font.AsSpan(record + 8));
            int length = (int)BinaryPrimitives.ReadUInt32BigEndian(font.AsSpan(record + 12));
            tables.Add(tag, font[offset..(offset + length)]);
        }
        return tables;
    }
}
