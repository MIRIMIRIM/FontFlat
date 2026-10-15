// Static CFF1 face admission and staleness report tests (S1b card §1.2/§3, commit ①
// acceptance). The CFF fixtures are assembled from the wire format directly (TN5176),
// never by asking the product to write them, so the admission read chain is exercised
// against an independent expectation. Refusal expectations pin outcome, diagnostic code
// and failure shape; staleness expectations pin the report facts the card §3 list names.
using System.Buffers.Binary;
using FontWeave.Core;
using FontWeave.Operations;
using FontWeave.Operations.Subset;
using Xunit;

namespace FontWeave.Tests;

public sealed class SubsetCffProfileTests
{
    private const uint Cff2Tag = 0x43464632, FvarTag = 0x66766172, VorgTag = 0x564F5247;

    // ---- wire-level name-keyed CFF1 fixture (independent of every product writer) ----

    /// <summary>Fixed-width DICT integer (29 + int32 big-endian), keeping the Top DICT a
    /// constant 17 bytes so the layout computes in two passes.</summary>
    private static IEnumerable<byte> Number(int value)
    {
        var bytes = new byte[5];
        bytes[0] = 29;
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(1), value);
        return bytes;
    }

    private static byte[] Index(params byte[][] items)
    {
        if (items.Length == 0) return [0x00, 0x00];
        var offsets = new List<int> { 1 };
        foreach (var item in items) offsets.Add(offsets[^1] + item.Length);
        int width = 1;
        while (offsets[^1] >= (1 << (8 * width))) width++;
        var bytes = new List<byte>
        {
            (byte)(items.Length >> 8), (byte)items.Length, (byte)width
        };
        foreach (int offset in offsets)
            for (int i = width - 1; i >= 0; i--) bytes.Add((byte)(offset >> (8 * i)));
        foreach (var item in items) bytes.AddRange(item);
        return [.. bytes];
    }

    /// <summary>A minimal valid name-keyed CFF1 table over <paramref name="numGlyphs"/>
    /// glyphs: predefined charset (ISOAdobe) and encoding, one Private DICT, no local
    /// subrs, and the strict tiling Header|Name|Top|String|GlobalSubrs|CharStrings|Private.
    /// Only CharStrings and Private live in the Top DICT, so the predefined charset and
    /// encoding forms are exercised and no custom SID is needed.</summary>
    private static byte[] NameKeyedCff(int numGlyphs)
    {
        var header = new byte[] { 0x01, 0x00, 0x04, 0x04 };
        byte[] name = Index("S1bSynthetic"u8.ToArray());
        byte[] strings = Index();
        byte[] globals = Index();
        // charstrings layout: Header(4) + name + top INDEX + strings + globals
        int charStringsAt = 4 + name.Length + 22 + strings.Length + globals.Length;
        byte[][] programs = new byte[numGlyphs][];
        for (int gid = 0; gid < numGlyphs; gid++) programs[gid] = [0x8b, 0x0e]; // 0 endchar
        byte[] charstrings = Index(programs);
        int privateAt = charStringsAt + charstrings.Length;
        const int privateSize = 2; // defaultWidthX 0
        var top = new List<byte>();
        top.AddRange(Number(charStringsAt)); top.Add(17);              // CharStrings
        top.AddRange(Number(privateSize)); top.AddRange(Number(privateAt));
        top.Add(18);                                                   // Private
        Assert.Equal(17, top.Count);
        byte[] topIndex = Index([.. top]);
        Assert.Equal(22, topIndex.Length);
        var bytes = new List<byte> { 0x01, 0x00, 0x04, 0x04 };
        bytes.AddRange(name);
        bytes.AddRange(topIndex);
        bytes.AddRange(strings);
        bytes.AddRange(globals);
        bytes.AddRange(charstrings);
        bytes.AddRange([0x8b, 0x14]); // Private DICT: defaultWidthX 0
        return [.. bytes];
    }

    /// <summary>Truncation that survives TryOpen's header admission (4 bytes) and fails
    /// inside Validate's INDEX walk.</summary>
    private static byte[] TruncatedNameKeyedCff() => [0x01, 0x00, 0x04, 0x04];

    private static byte[] Maxp05(ushort numGlyphs)
    {
        var bytes = new byte[6];
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(0), 0x00005000);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(4), numGlyphs);
        return bytes;
    }

    private static byte[] Vorg(short defaultOriginY, params (ushort Gid, short Y)[] records)
    {
        var bytes = new byte[8 + 4 * records.Length];
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(0), 0x00010000);
        BinaryPrimitives.WriteInt16BigEndian(bytes.AsSpan(4), defaultOriginY);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(6), (ushort)records.Length);
        for (int i = 0; i < records.Length; i++)
        {
            BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(8 + 4 * i), records[i].Gid);
            BinaryPrimitives.WriteInt16BigEndian(bytes.AsSpan(8 + 4 * i + 2), records[i].Y);
        }
        return bytes;
    }

    private static FontFaceRef Face(params (uint Tag, byte[] Bytes)[] tables) =>
        SubsetFontFixture.Face(tables);

    private static SubsetRequest Request(FontFaceRef face, IReadOnlyList<ushort> gids) =>
        new()
        {
            Face = face,
            Root = new SubsetGlyphRoot { Kind = SubsetRootKind.ExplicitGlyphSet, Gids = gids },
            Mode = SubsetMode.RetainGids,
        };

    // ---- admission: the static CFF1 profile is served ----

    [Fact]
    public void NameKeyedCffFaceAdmitsAndRegistersStaleness()
    {
        var face = Face(
            (SubsetFontFixture.HeadTag, SubsetFontFixture.Head()),
            (SubsetFontFixture.MaxpTag, Maxp05(3)),
            (SubsetFontFixture.CffTag, NameKeyedCff(3)));
        var status = SubsetOperation.TryPrepare(Request(face, [1]), out var result,
            out var report, out var diagnostic);
        Assert.Equal(ReadStatus.Done, status);
        Assert.NotNull(result);
        Assert.NotNull(report);
        var cff = report.Cff;
        Assert.NotNull(cff);
        Assert.Equal("CFF1 name-keyed", cff.Flavor);
        Assert.Equal(3, cff.CharStringsCount);
        Assert.True(cff.CharsetForm.Predefined);
        Assert.Equal(0, cff.CharsetForm.Format);
        Assert.NotNull(cff.EncodingForm);
        Assert.True(cff.EncodingForm!.Value.Predefined);
        Assert.Equal(0, cff.EncodingForm!.Value.Format);
        Assert.False(cff.EncodingForm!.Value.HasSupplement);
        Assert.True(cff.CharsetSidColumnReachable);
        Assert.Null(cff.RosSids);
        Assert.Null(cff.CidCount);
        Assert.Null(cff.FdSelectFormat);
        // no VORG on this face: the VORG staleness member stays null
        Assert.Null(report.Vorg);
        // every decision is a matrix-row preserve (the rebuild face is empty: OQ-S1b-6)
        Assert.All(report.Decisions, d =>
        {
            Assert.Equal(TableAction.Preserve, d.Action);
            Assert.Equal(SubsetPolicy.RetainRaw, d.Policy);
            Assert.Equal(SubsetDecisionOrigin.MatrixRow, d.Origin);
        });
        Assert.Equal(1, report.UnselectedSlotCount);
        // the CFF-face unselected-slot note is part of the staleness contract wording
        Assert.Contains("RETAIN_RAW", cff.UnselectedSlotSemantics);
    }

    [Fact]
    public void CidKeyedCffFaceAdmitsAndRegistersCidFacts()
    {
        var image = CidFontBuilder.Build(new CidFont { FdCount = 2, NumGlyphs = 3 });
        var face = Face(
            (SubsetFontFixture.HeadTag, SubsetFontFixture.Head()),
            (SubsetFontFixture.MaxpTag, Maxp05(3)),
            (SubsetFontFixture.CffTag, image.Bytes));
        var status = SubsetOperation.TryPrepare(Request(face, [1]), out var result,
            out var report, out _);
        Assert.Equal(ReadStatus.Done, status);
        Assert.NotNull(result);
        var cff = report!.Cff;
        Assert.NotNull(cff);
        Assert.Equal("CFF1 CID-keyed", cff.Flavor);
        Assert.Equal(3, cff.CharStringsCount);
        Assert.Null(cff.EncodingForm);
        Assert.False(cff.CharsetSidColumnReachable);
        Assert.Equal([391, 392, 0], cff.RosSids);
        Assert.Equal(3, cff.CidCount); // builder default: CIDCount = numGlyphs
        Assert.Equal(3, cff.FdSelectFormat); // the builder emits a format 3 FDSelect
    }

    [Fact]
    public void CffFaceWithVorgRegistersVorgStaleness()
    {
        var face = Face(
            (SubsetFontFixture.HeadTag, SubsetFontFixture.Head()),
            (SubsetFontFixture.MaxpTag, Maxp05(3)),
            (SubsetFontFixture.CffTag, NameKeyedCff(3)),
            (VorgTag, Vorg(880, (1, 700), (2, 720))));
        var status = SubsetOperation.TryPrepare(Request(face, [1]), out var result,
            out var report, out _);
        Assert.Equal(ReadStatus.Done, status);
        Assert.NotNull(result);
        var vorg = report!.Vorg;
        Assert.NotNull(vorg);
        Assert.Equal(880, vorg.DefaultVertOriginY);
        Assert.Equal(2, vorg.NumVertOriginYMetrics);
    }

    // ---- refusal face N1, migrated (SV card §4 M2): the CFF2 profile gate is gone.
    // A CFF2 face now walks the CFF branch's shape: the structural guards (A2/A4)
    // pass on this face and the table itself meets the typed read chain, which
    // refuses the shell payload with its own version diagnostic — the chain's
    // diagnostics propagate as-is, the same finite shape this face always had.
    [Fact]
    public void Cff2FaceRefusesAtItsOwnTag()
    {
        var face = Face(
            (SubsetFontFixture.HeadTag, SubsetFontFixture.Head()),
            (SubsetFontFixture.MaxpTag, Maxp05(3)),
            (Cff2Tag, new byte[16]));
        var status = SubsetOperation.TryPrepare(Request(face, [0]), out var result,
            out _, out var diagnostic);
        Assert.Equal(ReadStatus.UnsupportedFormat, status);
        Assert.Equal(DiagnosticCode.UnsupportedVersion, diagnostic.Code);
        Assert.Equal(Cff2Tag, diagnostic.Location.TableTag);
        Assert.Null(result);
    }

    // ---- refusal face N2: broken CFF face shapes ----

    [Fact]
    public void CffWithTrueTypeSpineRefuses()
    {
        var face = Face(
            (SubsetFontFixture.HeadTag, SubsetFontFixture.Head()),
            (SubsetFontFixture.MaxpTag, Maxp05(3)),
            (SubsetFontFixture.GlyfTag, [.. GlyfImages.NoContourGlyphRecord()]),
            (SubsetFontFixture.LocaTag, GlyfImages.LongLoca([0, 12])),
            (SubsetFontFixture.CffTag, NameKeyedCff(3)));
        var status = SubsetOperation.TryPrepare(Request(face, [0]), out var result,
            out _, out var diagnostic);
        Assert.Equal(ReadStatus.UnsupportedFormat, status);
        Assert.Equal(DiagnosticCode.OutlineProfileUnsupported, diagnostic.Code);
        Assert.Equal(SubsetFontFixture.CffTag, diagnostic.Location.TableTag);
        Assert.Null(result);
    }

    [Fact]
    public void CffFaceWithMaxp10Refuses()
    {
        var face = Face(
            (SubsetFontFixture.HeadTag, SubsetFontFixture.Head()),
            (SubsetFontFixture.MaxpTag, SubsetFontFixture.Maxp(3)),
            (SubsetFontFixture.CffTag, NameKeyedCff(3)));
        var status = SubsetOperation.TryPrepare(Request(face, [0]), out var result,
            out _, out var diagnostic);
        Assert.Equal(ReadStatus.UnsupportedFormat, status);
        Assert.Equal(DiagnosticCode.OutlineProfileUnsupported, diagnostic.Code);
        Assert.Equal(DataField.Version, diagnostic.Location.Field);
        Assert.Null(result);
    }

    // A1 (SV card §4): the CFF1 face + fvar form keeps its refusal — CFF1 is not a
    // variable outline spine — migrated from the retired static-profile code to the
    // profile code at the same refusal position (SV card §4 M1's companion move).
    [Fact]
    public void CffFaceWithFvarRefusesAsOutlineProfileUnsupported()
    {
        var face = Face(
            (SubsetFontFixture.HeadTag, SubsetFontFixture.Head()),
            (SubsetFontFixture.MaxpTag, Maxp05(3)),
            (SubsetFontFixture.CffTag, NameKeyedCff(3)),
            (FvarTag, new byte[16]));
        var status = SubsetOperation.TryPrepare(Request(face, [0]), out var result,
            out _, out var diagnostic);
        Assert.Equal(ReadStatus.UnsupportedFormat, status);
        Assert.Equal(DiagnosticCode.OutlineProfileUnsupported, diagnostic.Code);
        Assert.Equal(FvarTag, diagnostic.Location.TableTag);
        Assert.Null(result);
    }

    // ---- refusal face N3: CharStrings count must equal maxp.numGlyphs ----

    [Fact]
    public void CharStringsCountMismatchRefusesAtMaxpNumGlyphs()
    {
        var image = CidFontBuilder.Build(new CidFont { FdCount = 1, NumGlyphs = 3 });
        var face = Face(
            (SubsetFontFixture.HeadTag, SubsetFontFixture.Head()),
            (SubsetFontFixture.MaxpTag, Maxp05(4)), // face claims 4, CFF carries 3
            (SubsetFontFixture.CffTag, image.Bytes));
        var status = SubsetOperation.TryPrepare(Request(face, [0]), out var result,
            out _, out var diagnostic);
        Assert.Equal(ReadStatus.InvalidFormat, status);
        Assert.Equal(DiagnosticCode.InvalidValue, diagnostic.Code);
        Assert.Equal(DataField.NumGlyphs, diagnostic.Location.Field);
        Assert.Equal(4UL, diagnostic.Detail);
        Assert.Equal(3UL, diagnostic.Expected);
        Assert.Null(result);
    }

    // ---- refusal face N4: read-side diagnostics propagate as-is ----

    [Fact]
    public void TruncatedCffPropagatesReadSideDiagnostic()
    {
        var face = Face(
            (SubsetFontFixture.HeadTag, SubsetFontFixture.Head()),
            (SubsetFontFixture.MaxpTag, Maxp05(3)),
            (SubsetFontFixture.CffTag, TruncatedNameKeyedCff()));
        var status = SubsetOperation.TryPrepare(Request(face, [0]), out var result,
            out _, out var diagnostic);
        Assert.Equal(ReadStatus.Truncated, status);
        Assert.Equal(DiagnosticCode.TruncatedData, diagnostic.Code);
        Assert.Equal(SubsetFontFixture.CffTag, diagnostic.Location.TableTag);
        Assert.Null(result);
    }

    // ---- VORG staleness read failures refuse with the view's own diagnostic ----

    [Fact]
    public void VorgRecordOutsideDomainRefuses()
    {
        var face = Face(
            (SubsetFontFixture.HeadTag, SubsetFontFixture.Head()),
            (SubsetFontFixture.MaxpTag, Maxp05(3)),
            (SubsetFontFixture.CffTag, NameKeyedCff(3)),
            (VorgTag, Vorg(880, (7, 700)))); // GID 7 outside the 3-glyph domain
        var status = SubsetOperation.TryPrepare(Request(face, [0]), out var result,
            out _, out var diagnostic);
        Assert.Equal(ReadStatus.InvalidIndex, status);
        Assert.Equal(DiagnosticCode.IndexOutOfRange, diagnostic.Code);
        Assert.Equal(VorgTag, diagnostic.Location.TableTag);
        Assert.Null(result);
    }

    [Fact]
    public void VorgUnorderedRecordsRefuse()
    {
        var face = Face(
            (SubsetFontFixture.HeadTag, SubsetFontFixture.Head()),
            (SubsetFontFixture.MaxpTag, Maxp05(3)),
            (SubsetFontFixture.CffTag, NameKeyedCff(3)),
            (VorgTag, Vorg(880, (2, 700), (1, 720)))); // descending GIDs
        var status = SubsetOperation.TryPrepare(Request(face, [0]), out var result,
            out _, out var diagnostic);
        Assert.Equal(ReadStatus.InvalidFormat, status);
        Assert.Equal(DiagnosticCode.InvalidValue, diagnostic.Code);
        Assert.Null(result);
    }
}
