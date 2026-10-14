// Compact-remap mode branch and policy column tests (SC card §4 commit ① acceptance).
//
// The decide loop is the primaryPolicy column's execution site: these tests pin the
// per-tag policy/action pairs for both modes on the same face shape, the RC1-RC5
// compact refusal face, and the untouched retain-GIDs decisions on a metrics-bearing
// face. Keyword contracts are keyed by the registered DiagnosticCode values
// (ReadContracts.cs), mirroring the SubsetRefusalTests idiom.
using System.Buffers.Binary;
using FontWeave.Core;
using FontWeave.Operations;
using FontWeave.Operations.Subset;
using Xunit;

namespace FontWeave.Tests;

public sealed class SubsetCompactPolicyTests
{
    private static SubsetRequest Request(FontFaceRef face, IReadOnlyList<ushort> gids,
        SubsetMode mode, SubsetOptions? options = null) => new()
    {
        Face = face,
        Root = new SubsetGlyphRoot { Kind = SubsetRootKind.ExplicitGlyphSet, Gids = gids },
        Mode = mode,
        Options = options ?? new SubsetOptions()
    };

    /// <summary>A static TT face shaped like the MC-002 profile: 4 glyphs where glyph 3
    /// is a composite over glyph 2, metrics pairs present, post 2.0, LTSH and hdmx for
    /// the compact rebuild rows, the layout family for the NoLayout alternate.</summary>
    internal static FontFaceRef MatrixFace()
    {
        var (loca, glyf, _) = GlyfImages.Assemble(
        [
            GlyfImages.NoContourGlyphRecord(),
            GlyfImages.NoContourGlyphRecord(),
            GlyfImages.NoContourGlyphRecord(),
            GlyfImages.CompositeRecord(2),
        ]);
        return SubsetFontFixture.Face(
            (SubsetFontFixture.HeadTag, SubsetFontFixture.Head()),
            (SubsetFontFixture.MaxpTag, SubsetFontFixture.Maxp(4)),
            (SubsetFontFixture.GlyfTag, glyf),
            (SubsetFontFixture.LocaTag, loca),
            (SubsetFontFixture.CmapTag, SubsetFontFixture.CmapCanonical((0x41u, 2))),
            (HheaTag, Hhea(4)),
            (HmtxTag, Hmtx(4, 4)),
            (SubsetFontFixture.PostTag, Post20(4)),
            (LtshTag, Ltsh(4)),
            (HdmxTag, Hdmx(4)),
            (NameTag, SubsetFontFixture.Dummy(16)),
            (Os2Tag, SubsetFontFixture.Dummy(96)),
            (SubsetFontFixture.GsubTag, SubsetFontFixture.Dummy(12)));
    }

    internal const uint HheaTag = 0x68686561, HmtxTag = 0x686D7478, LtshTag = 0x4C545348,
        HdmxTag = 0x68646D78, NameTag = 0x6E616D65, Os2Tag = 0x4F532F32,
        VorgTag = 0x564F5247, MergTag = 0x4D455247, ColrTag = 0x434F4C52,
        SbixTag = 0x73626978, CbdtTag = 0x43424454, EbscTag = 0x45425343,
        VmtxTag = 0x766D7478, VheaTag = 0x76686561;

    internal static byte[] Hhea(ushort numberOfHMetrics, ushort advanceWidthMax = 908)
    {
        var bytes = new byte[36];
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(0), 0x00010000);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(10), advanceWidthMax);
        BinaryPrimitives.WriteInt16BigEndian(bytes.AsSpan(18), 1); // caretSlopeRise
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(34), numberOfHMetrics);
        return bytes;
    }

    internal static byte[] Hmtx(int glyphs, ushort numberOfHMetrics)
    {
        var bytes = new byte[4 * numberOfHMetrics + 2 * (glyphs - numberOfHMetrics)];
        for (int i = 0; i < numberOfHMetrics; i++)
        {
            BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(4 * i), (ushort)(100 + i));
            BinaryPrimitives.WriteInt16BigEndian(bytes.AsSpan(4 * i + 2), (short)i);
        }
        for (int i = numberOfHMetrics; i < glyphs; i++)
            BinaryPrimitives.WriteInt16BigEndian(bytes.AsSpan(4 * numberOfHMetrics + 2 * (i - numberOfHMetrics)), 0);
        return bytes;
    }

    /// <summary>A canonical post 2.0: header, numGlyphs and standard-name indices only,
    /// exactly what a re-emission would write for these values.</summary>
    internal static byte[] Post20(ushort numGlyphs)
    {
        var bytes = new byte[32 + 2 + 2 * numGlyphs];
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(0), 0x00020000);
        BinaryPrimitives.WriteInt16BigEndian(bytes.AsSpan(4), -120);   // underlinePosition
        BinaryPrimitives.WriteInt16BigEndian(bytes.AsSpan(6), 40);     // underlineThickness
        BinaryPrimitives.WriteInt16BigEndian(bytes.AsSpan(32), unchecked((short)numGlyphs));
        // glyphNameIndex entries stay below 258: standard macintosh names, no names segment.
        return bytes;
    }

    internal static byte[] Ltsh(ushort numGlyphs)
    {
        var bytes = new byte[4 + numGlyphs];
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(2), numGlyphs);
        return bytes;
    }

    internal static byte[] Hdmx(ushort numGlyphs)
    {
        int stride = (numGlyphs + 2 + 3) & ~3;
        var bytes = new byte[8 + stride];
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(2), 1);            // numRecords
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(4), (uint)stride); // sizeDeviceRecord
        bytes.AsSpan(8, 2)[0] = 12;                                           // pixelSize
        return bytes;
    }

    private static Dictionary<uint, (TableAction Action, SubsetPolicy Policy)> DecisionMap(
        SubsetReport report)
    {
        var byTag = new Dictionary<uint, (TableAction, SubsetPolicy)>();
        foreach (var decision in report.Decisions) byTag[decision.Tag] = (decision.Action, decision.Policy);
        return byTag;
    }

    // The compact decide loop reads the primaryPolicy column: the identity spine plus
    // every per-GID companion table rebuilds, the raw rows stay raw, and the layout
    // family drops under the explicit NoLayout option (MC-002 tableDecisions shape).
    [Fact]
    public void CompactDecisionsFollowPrimaryPolicyColumn()
    {
        var face = MatrixFace();
        var options = new SubsetOptions { NoLayout = true };
        var status = SubsetOperation.TryPrepare(Request(face, [0, 1, 3], SubsetMode.CompactRemap,
            options), out _, out var report, out _);
        Assert.NotNull(report);
        Assert.Equal(SubsetMode.CompactRemap, report!.Mode);
        Assert.Contains(report.OptionRecords, r =>
            r.Id == SubsetOptionId.Mode && r.Value == nameof(SubsetMode.CompactRemap));
        var byTag = DecisionMap(report);
        Assert.Equal(13, report.Decisions.Count);
        foreach (var tag in new[] { SubsetFontFixture.GlyfTag, SubsetFontFixture.LocaTag,
            SubsetFontFixture.CmapTag, SubsetFontFixture.MaxpTag, HheaTag, HmtxTag,
            LtshTag, HdmxTag, SubsetFontFixture.PostTag })
            Assert.True(byTag.TryGetValue(tag, out var rebuilt) &&
                rebuilt == (TableAction.Rebuild, SubsetPolicy.RebuildTyped),
                $"tag 0x{tag:X8} must rebuild under compact");
        foreach (var tag in new[] { SubsetFontFixture.HeadTag, NameTag, Os2Tag })
            Assert.True(byTag.TryGetValue(tag, out var kept) &&
                kept == (TableAction.Preserve, SubsetPolicy.RetainRaw),
                $"tag 0x{tag:X8} must stay raw under compact");
        Assert.Equal((TableAction.Drop, SubsetPolicy.Delete), byTag[SubsetFontFixture.GsubTag]);
        var dropped = report.DroppedTables.ToList();
        var droppedTag = Assert.Single(dropped);
        Assert.Equal(SubsetFontFixture.GsubTag, droppedTag);
    }

    // Zero-drift witness: the same face under retain-GIDs keeps every companion table
    // raw and maxp raw, rebuilding only the identity spine.
    [Fact]
    public void RetainDecisionsUnchangedOnMetricsFace()
    {
        var face = MatrixFace();
        var options = new SubsetOptions { NoLayout = true };
        var status = SubsetOperation.TryPrepare(Request(face, [0, 1, 3], SubsetMode.RetainGids,
            options), out var result, out var report, out _);
        Assert.Equal(ReadStatus.Done, status);
        Assert.NotNull(result);
        var byTag = DecisionMap(report!);
        foreach (var tag in new[] { SubsetFontFixture.MaxpTag, HheaTag, HmtxTag,
            LtshTag, HdmxTag, SubsetFontFixture.PostTag, SubsetFontFixture.HeadTag,
            NameTag, Os2Tag })
            Assert.True(byTag.TryGetValue(tag, out var kept) &&
                kept == (TableAction.Preserve, SubsetPolicy.RetainRaw),
                $"tag 0x{tag:X8} must stay raw under retain");
        foreach (var tag in new[] { SubsetFontFixture.GlyfTag, SubsetFontFixture.LocaTag,
            SubsetFontFixture.CmapTag })
            Assert.Equal((TableAction.Rebuild, SubsetPolicy.RebuildTyped), byTag[tag]);
        Assert.All(report!.Decisions, d => Assert.NotEqual(SubsetMode.CompactRemap,
            report.Mode));
    }

    // RC1: a CFF outline face refuses identically in both modes (profile gate unchanged).
    [Fact]
    public void CompactCffFaceRefusesLikeRetain()
    {
        var face = SubsetFontFixture.Face(
            (SubsetFontFixture.HeadTag, SubsetFontFixture.Head()),
            (SubsetFontFixture.MaxpTag, SubsetFontFixture.Maxp(4)),
            (SubsetFontFixture.CffTag, SubsetFontFixture.Dummy()));
        foreach (var mode in new[] { SubsetMode.RetainGids, SubsetMode.CompactRemap })
        {
            var status = SubsetOperation.TryPrepare(Request(face, [0], mode), out var result,
                out _, out var diagnostic);
            Assert.Equal(ReadStatus.UnsupportedFormat, status);
            Assert.Equal(DiagnosticCode.OutlineProfileUnsupported, diagnostic.Code);
            Assert.Null(result);
        }
    }

    // RC2: VORG's compact rebuild rides the CFF slice, so a compact request refuses
    // while retain keeps the table raw.
    [Fact]
    public void CompactVorgRefusesRetainKeepsRaw()
    {
        var (loca, glyf, _) = GlyfImages.Assemble([GlyfImages.NoContourGlyphRecord()]);
        var tables = new List<(uint, byte[])>
        {
            (SubsetFontFixture.HeadTag, SubsetFontFixture.Head()),
            (SubsetFontFixture.MaxpTag, SubsetFontFixture.Maxp(1)),
            (SubsetFontFixture.GlyfTag, glyf),
            (SubsetFontFixture.LocaTag, loca),
            (SubsetFontFixture.CmapTag, SubsetFontFixture.CmapCanonical((0x41u, 0))),
            (VorgTag, SubsetFontFixture.Dummy(10)),
        };
        var retain = SubsetOperation.TryPrepare(
            Request(SubsetFontFixture.Face([.. tables]), [0], SubsetMode.RetainGids),
            out var retainResult, out var retainReport, out _);
        Assert.Equal(ReadStatus.Done, retain);
        Assert.NotNull(retainResult);
        Assert.Equal((TableAction.Preserve, SubsetPolicy.RetainRaw),
            DecisionMap(retainReport!)[VorgTag]);

        var compact = SubsetOperation.TryPrepare(
            Request(SubsetFontFixture.Face([.. tables]), [0], SubsetMode.CompactRemap),
            out var compactResult, out _, out var compactDiagnostic);
        Assert.Equal(ReadStatus.UnsupportedFormat, compact);
        Assert.Equal(DiagnosticCode.UnsupportedOperation, compactDiagnostic.Code);
        Assert.Equal(VorgTag, compactDiagnostic.Location.TableTag);
        Assert.Null(compactResult);
    }

    // RC3: MERG is DEFERRED_TO_S1 in the primary column; compact refuses.
    [Fact]
    public void CompactMergRefuses()
    {
        var face = MatrixFace();
        var withMerg = SubsetFontFixture.Face(
            (SubsetFontFixture.HeadTag, SubsetFontFixture.Head()),
            (SubsetFontFixture.MaxpTag, SubsetFontFixture.Maxp(4)),
            (SubsetFontFixture.GlyfTag, GlyfImages.Assemble([GlyfImages.NoContourGlyphRecord()]).Glyf),
            (SubsetFontFixture.LocaTag, GlyfImages.Assemble([GlyfImages.NoContourGlyphRecord()]).Loca),
            (SubsetFontFixture.CmapTag, SubsetFontFixture.CmapCanonical((0x41u, 0))),
            (MergTag, SubsetFontFixture.Dummy()));
        var status = SubsetOperation.TryPrepare(
            Request(withMerg, [0], SubsetMode.CompactRemap), out var result, out _,
            out var diagnostic);
        Assert.Equal(ReadStatus.UnsupportedFormat, status);
        Assert.Equal(DiagnosticCode.UnsupportedOperation, diagnostic.Code);
        Assert.Equal(MergTag, diagnostic.Location.TableTag);
        Assert.Null(result);
    }

    // RC4: each color/bitmap deferral row refuses a compact request by tag.
    [Theory]
    [InlineData(ColrTag)]
    [InlineData(SbixTag)]
    [InlineData(CbdtTag)]
    [InlineData(EbscTag)]
    public void CompactDeferredBitmapColorRowsRefuse(uint tag)
    {
        var (loca, glyf, _) = GlyfImages.Assemble([GlyfImages.NoContourGlyphRecord()]);
        var face = SubsetFontFixture.Face(
            (SubsetFontFixture.HeadTag, SubsetFontFixture.Head()),
            (SubsetFontFixture.MaxpTag, SubsetFontFixture.Maxp(1)),
            (SubsetFontFixture.GlyfTag, glyf),
            (SubsetFontFixture.LocaTag, loca),
            (SubsetFontFixture.CmapTag, SubsetFontFixture.CmapCanonical((0x41u, 0))),
            (tag, SubsetFontFixture.Dummy()));
        var status = SubsetOperation.TryPrepare(
            Request(face, [0], SubsetMode.CompactRemap), out var result, out _,
            out var diagnostic);
        Assert.Equal(ReadStatus.UnsupportedFormat, status);
        Assert.Equal(DiagnosticCode.UnsupportedOperation, diagnostic.Code);
        Assert.Equal(tag, diagnostic.Location.TableTag);
        Assert.Null(result);
    }

    // RC5: the compact metrics rebuild obligation has no execution path without the
    // pairing partner; retain's raw copy has no pairing requirement.
    [Theory]
    [InlineData(HheaTag, HmtxTag)]
    [InlineData(VheaTag, VmtxTag)]
    public void CompactOrphanMetricsRefuseRetainAccepts(uint present, uint missing)
    {
        var (loca, glyf, _) = GlyfImages.Assemble([GlyfImages.NoContourGlyphRecord()]);
        var tables = new List<(uint, byte[])>
        {
            (SubsetFontFixture.HeadTag, SubsetFontFixture.Head()),
            (SubsetFontFixture.MaxpTag, SubsetFontFixture.Maxp(1)),
            (SubsetFontFixture.GlyfTag, glyf),
            (SubsetFontFixture.LocaTag, loca),
            (SubsetFontFixture.CmapTag, SubsetFontFixture.CmapCanonical((0x41u, 0))),
            (present, present == HheaTag ? Hhea(1) : Hmtx(1, 1)),
        };
        var retainFace = SubsetFontFixture.Face([.. tables]);
        var retain = SubsetOperation.TryPrepare(
            Request(retainFace, [0], SubsetMode.RetainGids), out var retainResult,
            out _, out _);
        Assert.Equal(ReadStatus.Done, retain);
        Assert.NotNull(retainResult);

        var compact = SubsetOperation.TryPrepare(
            Request(retainFace, [0], SubsetMode.CompactRemap), out var compactResult,
            out _, out var diagnostic);
        Assert.Equal(ReadStatus.UnsupportedFormat, compact);
        Assert.Equal(DiagnosticCode.OutlineProfileUnsupported, diagnostic.Code);
        Assert.Equal(present, diagnostic.Location.TableTag);
        Assert.NotEqual(missing, diagnostic.Location.TableTag);
        Assert.Null(compactResult);
    }

    // Out-of-domain mode values keep the S0-RF-006-MODE refusal with the value as detail.
    [Fact]
    public void OutOfDomainModeStillRefuses()
    {
        var face = MatrixFace();
        var status = SubsetOperation.TryPrepare(
            Request(face, [0], (SubsetMode)99), out var result, out _,
            out var diagnostic);
        Assert.Equal(ReadStatus.UnsupportedFormat, status);
        Assert.Equal(DiagnosticCode.SubsetModeRequired, diagnostic.Code);
        Assert.Equal(99u, diagnostic.Detail);
        Assert.Null(result);
    }

    // The layout refusal is mode-independent (W1 §2.2): without NoLayout both modes
    // refuse pointing at S2.
    [Theory]
    [InlineData(SubsetMode.RetainGids)]
    [InlineData(SubsetMode.CompactRemap)]
    public void LayoutWithoutNoLayoutRefusesInBothModes(SubsetMode mode)
    {
        var face = MatrixFace();
        var status = SubsetOperation.TryPrepare(Request(face, [0], mode), out var result,
            out _, out var diagnostic);
        Assert.Equal(ReadStatus.UnsupportedFormat, status);
        Assert.Equal(DiagnosticCode.LayoutFamilyUnsupported, diagnostic.Code);
        Assert.Null(result);
    }
}
