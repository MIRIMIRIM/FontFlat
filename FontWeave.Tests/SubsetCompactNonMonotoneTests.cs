// Compact-remap span survivors over non-monotone maps (the S1c follow-up).
//
// The CFF1 compact freeze orders the output by the charset's SID identity, so the
// frozen map's output-to-input order is not the ascending input-GID order there.
// The fixtures are name-keyed CFF1 faces assembled from the wire format directly
// (the S3 seac fixture idiom) with hand-built SVG/COLR companions, so every span
// expectation is independent of the map's output order; the assertions ride the
// output tables' typed re-reads.
using System.Buffers.Binary;
using FontWeave.Containers;
using FontWeave.Core;
using FontWeave.Operations;
using FontWeave.Operations.Subset;
using FontWeave.Tables;
using Xunit;

namespace FontWeave.Tests;

public sealed class SubsetCompactNonMonotoneTests
{
    private const uint SvgTag = 0x53564720, ColrTag = 0x434F4C52;

    private static SubsetRequest Request(FontFaceRef face, IReadOnlyList<ushort> gids) =>
        new()
        {
            Face = face,
            Root = new SubsetGlyphRoot { Kind = SubsetRootKind.ExplicitGlyphSet, Gids = gids },
            Mode = SubsetMode.CompactRemap,
        };

    // ---- wire-level name-keyed CFF1 fixture (the S3 seac idiom) -------------------

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

    /// <summary>A name-keyed CFF1 table over the given charset SID column (entry 0 is
    /// .notdef) with one bare-endchar program per glyph: the custom format-0 charset
    /// is what makes the compact freeze sort by SID, permuting the input-GID order.</summary>
    private static byte[] NameKeyedCff(int[] sids)
    {
        byte[] header = [0x01, 0x00, 0x04, 0x01];
        byte[] name = Index("S1cSynthetic"u8.ToArray());
        byte[] strings = Index();
        byte[] globalIndex = Index();
        byte[] charset = [(byte)0, .. sids.Skip(1).SelectMany(s =>
            new[] { (byte)((s >> 8) & 0xFF), (byte)(s & 0xFF) })];
        byte[] charstrings = Index(sids.Select(_ => new byte[] { 14 }).ToArray());
        byte[] privateDict = [0x8b, 0x14];
        int at = header.Length + name.Length + 28 + strings.Length + globalIndex.Length;
        int charsetAt = at;
        int charStringsAt = charsetAt + charset.Length;
        int privateAt = charStringsAt + charstrings.Length;
        var top = new List<byte>();
        void Entry(int op, int value)
        {
            top.AddRange([29, (byte)((value >> 24) & 0xFF), (byte)((value >> 16) & 0xFF),
                (byte)((value >> 8) & 0xFF), (byte)(value & 0xFF), (byte)op]);
        }
        Entry(17, charStringsAt);                    // CharStrings
        Entry(15, charsetAt);                        // charset (custom, format 0)
        top.AddRange([29, 0, 0, 0, (byte)privateDict.Length,
            29, (byte)((privateAt >> 24) & 0xFF), (byte)((privateAt >> 16) & 0xFF),
            (byte)((privateAt >> 8) & 0xFF), (byte)(privateAt & 0xFF), 18]);
        byte[] topIndex = Index([.. top]);
        Assert.Equal(28, topIndex.Length);
        var bytes = new List<byte>();
        bytes.AddRange(header);
        bytes.AddRange(name);
        bytes.AddRange(topIndex);
        bytes.AddRange(strings);
        bytes.AddRange(globalIndex);
        bytes.AddRange(charset);
        bytes.AddRange(charstrings);
        bytes.AddRange(privateDict);
        return [.. bytes];
    }

    private static FontFaceRef CffFace(int[] sids, params (uint Tag, byte[] Bytes)[] extra)
    {
        var tables = new List<(uint, byte[])>
        {
            (SubsetFontFixture.HeadTag, SubsetFontFixture.Head()),
            (SubsetFontFixture.MaxpTag, SubsetFontFixture.Maxp05((ushort)sids.Length)),
            (SubsetFontFixture.CffTag, NameKeyedCff(sids)),
        };
        tables.AddRange(extra);
        return SubsetFontFixture.Face([.. tables]);
    }

    /// <summary>A canonical SVG ␣ table: records over the given ranges, documents in
    /// first-occurrence order (the SubsetCompactBitmapTests wire idiom).</summary>
    private static byte[] SvgWire((ushort Start, ushort End, int Payload)[] records,
        byte[][] docs)
    {
        int listAt = 10;
        var bytes = new byte[listAt + 2 + 12 * records.Length +
            docs.Sum(d => d.Length)];
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(0), 0);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(2), 10);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(listAt), (ushort)records.Length);
        int docAt = listAt + 2 + 12 * records.Length;
        var positions = new uint[docs.Length];
        for (int r = 0; r < records.Length; r++)
        {
            int at = listAt + 2 + 12 * r;
            if (positions[records[r].Payload] == 0)
            {
                positions[records[r].Payload] = (uint)(docAt - 10);
                docs[records[r].Payload].CopyTo(bytes, docAt);
                docAt += docs[records[r].Payload].Length;
            }
            BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(at), records[r].Start);
            BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(at + 2), records[r].End);
            BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(at + 4),
                positions[records[r].Payload]);
            BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(at + 8),
                (uint)docs[records[r].Payload].Length);
        }
        return bytes;
    }

    private static byte[] OutputBytes(SubsetResult result)
    {
        byte[] bytes = new byte[result.Font.OutputLength];
        var copy = result.Font.TryCopyTo(bytes, out int written, out _);
        Assert.Equal(ReadStatus.Done, copy);
        Assert.Equal(bytes.Length, written);
        return bytes;
    }

    private static byte[] OutputTable(SubsetResult result, uint tag)
    {
        _ = FontSource.TryCopy(OutputBytes(result), out var source, out _);
        _ = source!.TryGetFace(0, out var face, out _);
        _ = face.TryOpenDirectory(out var directory, out _);
        var status = directory.TryFindUniqueTable(tag, out int ordinal, out _);
        Assert.Equal(ReadStatus.Done, status);
        status = directory.TryGetTableBytes(ordinal, out var payload, out _);
        Assert.Equal(ReadStatus.Done, status);
        return payload.ToArray();
    }

    private static bool OutputHasTable(SubsetResult result, uint tag)
    {
        _ = FontSource.TryCopy(OutputBytes(result), out var source, out _);
        _ = source!.TryGetFace(0, out var face, out _);
        _ = face.TryOpenDirectory(out var directory, out _);
        return directory.TryFindUniqueTable(tag, out _, out _) == ReadStatus.Done;
    }

    // The minimal trigger: charset [0, 2, 1] freezes OutputToInput = [0, 2, 1], so
    // the record's whole input range [1, 1] sits at output 2 while the output-order
    // binary search lands past it — the record (and with it the table) must not
    // drop for a glyph the map retained.
    [Fact]
    public void CompactSvgRecordSurvivesNonMonotoneSpan()
    {
        byte[] doc = [0x61, 0x41];
        var svg = SvgWire([(1, 1, 0)], [doc]);
        var face = CffFace([0, 2, 1], (SvgTag, svg));
        var status = SubsetOperation.TryPrepare(Request(face, [0, 1, 2]),
            out var result, out var report, out var diagnostic);
        Assert.Equal(ReadStatus.Done, status);
        Assert.NotNull(result);
        // The map itself proves the permutation the record must survive.
        Assert.Equal([0, 2, 1],
            result!.Map!.Entries.Select(e => (int)e.InputGid).ToArray());
        Assert.Equal(TableAction.Rebuild,
            report!.Decisions.Single(d => d.Tag == SvgTag).Action);
        Assert.True(OutputHasTable(result, SvgTag), "the populated SVG table dropped");
        _ = SvgView.TryOpen(OutputTable(result, SvgTag), out var svgView,
            out _, DataLocation.Anonymous(SvgLayout.Tag));
        Assert.Equal(ReadStatus.Done, svgView.TryReadTable(out var svgOut, out _));
        Assert.Equal(1, svgOut!.RecordCount);
        Assert.Equal((ushort)2, svgOut.Records[0].StartGlyphID);
        Assert.Equal((ushort)2, svgOut.Records[0].EndGlyphID);
        Assert.Equal(0, svgOut.Records[0].PayloadIndex);
        Assert.Equal(1, svgOut.DocumentCount);
        Assert.Equal(doc, svgOut.Documents[0]);
    }

    // Span endpoints across a non-monotone order: the survivors answer in the input
    // domain, so each single-glyph record lands on its own glyph's output — the
    // output-order search wrote the neighbor's span into the row instead.
    [Fact]
    public void CompactSvgSpanEndpointsFollowTheInputDomain()
    {
        byte[] docA = [0x61, 0x41];
        byte[] docB = [0x62, 0x42];
        var svg = SvgWire([(0, 0, 0), (2, 2, 1)], [docA, docB]);
        var face = CffFace([0, 2, 1, 3], (SvgTag, svg));
        var status = SubsetOperation.TryPrepare(Request(face, [0, 1, 2, 3]),
            out var result, out _, out var diagnostic);
        Assert.Equal(ReadStatus.Done, status);
        Assert.NotNull(result);
        // charset [0, 2, 1, 3] freezes [0, 2, 1, 3]: input 2 rides output 1.
        _ = SvgView.TryOpen(OutputTable(result!, SvgTag), out var svgView,
            out _, DataLocation.Anonymous(SvgLayout.Tag));
        Assert.Equal(ReadStatus.Done, svgView.TryReadTable(out var svgOut, out _));
        Assert.Equal(2, svgOut!.RecordCount);
        Assert.Equal(((ushort)0, (ushort)0, 0),
            (svgOut.Records[0].StartGlyphID, svgOut.Records[0].EndGlyphID,
                svgOut.Records[0].PayloadIndex));
        Assert.Equal(((ushort)1, (ushort)1, 1),
            (svgOut.Records[1].StartGlyphID, svgOut.Records[1].EndGlyphID,
                svgOut.Records[1].PayloadIndex));
        Assert.Equal(2, svgOut.DocumentCount);
    }

    // The row-order defect: two single-glyph records whose input rows ascend but
    // whose mapped outputs invert (charset [0, 2, 1] sends input 1 to output 2 and
    // input 2 to output 1). The wire contract — the DocumentRecords' strict ascent —
    // is over the output domain and independent of the input rows' order, so the
    // rows re-emerge in the output order and each keeps its own payload by content.
    [Fact]
    public void CompactSvgRowOrderFollowsTheOutputDomain()
    {
        byte[] docA = [0x61, 0x41];
        byte[] docB = [0x62, 0x42];
        var svg = SvgWire([(1, 1, 0), (2, 2, 1)], [docA, docB]);
        var face = CffFace([0, 2, 1], (SvgTag, svg));
        var status = SubsetOperation.TryPrepare(Request(face, [0, 1, 2]),
            out var result, out var report, out var diagnostic);
        Assert.Equal(ReadStatus.Done, status);
        Assert.NotNull(result);
        Assert.Equal([0, 2, 1],
            result!.Map!.Entries.Select(e => (int)e.InputGid).ToArray());
        Assert.Equal(TableAction.Rebuild,
            report!.Decisions.Single(d => d.Tag == SvgTag).Action);
        _ = SvgView.TryOpen(OutputTable(result, SvgTag), out var svgView,
            out _, DataLocation.Anonymous(SvgLayout.Tag));
        Assert.Equal(ReadStatus.Done, svgView.TryReadTable(out var svgOut, out _));
        Assert.Equal(2, svgOut!.RecordCount);
        // The rows sit in the output domain's ascent, and each row's payload is the
        // one its own input row carried (asserted by content, not by index).
        Assert.Equal(((ushort)1, (ushort)1),
            (svgOut.Records[0].StartGlyphID, svgOut.Records[0].EndGlyphID));
        Assert.Equal(docB, svgOut.Documents[svgOut.Records[0].PayloadIndex]);
        Assert.Equal(((ushort)2, (ushort)2),
            (svgOut.Records[1].StartGlyphID, svgOut.Records[1].EndGlyphID));
        Assert.Equal(docA, svgOut.Documents[svgOut.Records[1].PayloadIndex]);
        // The document table follows the sorted traversal's first-reference order.
        Assert.Equal(2, svgOut.DocumentCount);
        Assert.Equal(docB, svgOut.Documents[0]);
        Assert.Equal(docA, svgOut.Documents[1]);
    }

    // The same inverted shape with both rows sharing one payload: the dedup runs
    // over the sorted traversal, so one document survives and both records carry
    // its index.
    [Fact]
    public void CompactSvgSharedDocumentStaysOneEntryUnderInversion()
    {
        byte[] doc = [0x61, 0x41];
        var svg = SvgWire([(1, 1, 0), (2, 2, 0)], [doc]);
        var face = CffFace([0, 2, 1], (SvgTag, svg));
        var status = SubsetOperation.TryPrepare(Request(face, [0, 1, 2]),
            out var result, out _, out var diagnostic);
        Assert.Equal(ReadStatus.Done, status);
        Assert.NotNull(result);
        _ = SvgView.TryOpen(OutputTable(result!, SvgTag), out var svgView,
            out _, DataLocation.Anonymous(SvgLayout.Tag));
        Assert.Equal(ReadStatus.Done, svgView.TryReadTable(out var svgOut, out _));
        Assert.Equal(2, svgOut!.RecordCount);
        Assert.Equal(((ushort)1, (ushort)1),
            (svgOut.Records[0].StartGlyphID, svgOut.Records[0].EndGlyphID));
        Assert.Equal(((ushort)2, (ushort)2),
            (svgOut.Records[1].StartGlyphID, svgOut.Records[1].EndGlyphID));
        Assert.Equal(1, svgOut.DocumentCount);
        Assert.Equal(doc, svgOut.Documents[0]);
        Assert.Equal(0, svgOut.Records[0].PayloadIndex);
        Assert.Equal(0, svgOut.Records[1].PayloadIndex);
    }

    // The monotone control: on an identity-ordered charset the sort is the identity
    // and the dedup replays the input traversal, so the rebuilt table re-emits the
    // input wire byte-for-byte.
    [Fact]
    public void CompactSvgMonotoneOrderReemitsByteIdentical()
    {
        byte[] docA = [0x61, 0x41];
        byte[] docB = [0x62, 0x42];
        var svg = SvgWire([(1, 1, 0), (2, 2, 1)], [docA, docB]);
        var face = CffFace([0, 1, 2], (SvgTag, svg));
        var status = SubsetOperation.TryPrepare(Request(face, [0, 1, 2]),
            out var result, out _, out var diagnostic);
        Assert.Equal(ReadStatus.Done, status);
        Assert.NotNull(result);
        Assert.Equal([0, 1, 2],
            result!.Map!.Entries.Select(e => (int)e.InputGid).ToArray());
        Assert.Equal(svg, OutputTable(result, SvgTag));
    }

    // COLR v1 ClipList over the non-monotone map: the probe fixture's clip [10, 20]
    // keeps two survivors whose outputs invert (input 10 rides the last output
    // slot), so the row bounds are the survivors' output extrema [3, 5] — the
    // output-order search narrowed the row to the wrong survivor [3, 3].
    [Fact]
    public void CompactColrClipsReSpanOverNonMonotoneMap()
    {
        // Identity SIDs with GID 10 pushed past 51: the freeze sorts it last.
        int[] sids = [.. Enumerable.Range(0, 52)];
        sids[10] = 52;
        var face = CffFace(sids, (ColrTag, SubsetCompactColrTests.ColrV1WireForProbe()));
        var status = SubsetOperation.TryPrepare(Request(face, [0, 5, 6, 10, 20, 51]),
            out var result, out _, out var diagnostic);
        Assert.Equal(ReadStatus.Done, status);
        Assert.NotNull(result);
        Assert.Equal([0, 5, 6, 20, 51, 10],
            result!.Map!.Entries.Select(e => (int)e.InputGid).ToArray());
        _ = ColrView.TryOpen(OutputTable(result, ColrTag), out var colrView,
            out _, DataLocation.Anonymous(ColrLayout.Tag));
        Assert.Equal(ReadStatus.Done, colrView.TryReadTable(6, null,
            out var colrOut, out _));
        Assert.NotNull(colrOut!);
        Assert.Equal(2, colrOut.ClipCount);
        // Clip [5, 6] maps to the contiguous (1, 2); clip [10, 20]'s survivors
        // occupy outputs 3 and 5, and the row keeps every survivor inside.
        Assert.Equal(((ushort)1, (ushort)2),
            (colrOut.Clips[0].StartGlyphId, colrOut.Clips[0].EndGlyphId));
        Assert.Equal(((ushort)3, (ushort)5),
            (colrOut.Clips[1].StartGlyphId, colrOut.Clips[1].EndGlyphId));
        Assert.Equal(2, colrOut.BaseGlyphPaintCount);
        Assert.Equal((ushort)1, colrOut.BaseGlyphPaints[0].GlyphId);
        Assert.Equal((ushort)2, colrOut.BaseGlyphPaints[1].GlyphId);
    }

    // The ClipList row-order defect: the probe fixture's two clip rows ascend in the
    // input ([5, 6] then [10, 20]), but a SID column that freezes the retained set
    // as [0, 10, 20, 5, 6, 51] maps row [5, 6] onto (3, 4) and row [10, 20] onto
    // (1, 2) — the input-row order now descends. The ClipList's strict ascent is the
    // output domain's contract, so the rows re-emerge sorted and each keeps its own
    // box (asserted by content).
    [Fact]
    public void CompactColrClipRowsSortToTheOutputDomain()
    {
        int[] sids = new int[52];
        sids[0] = 0;
        sids[10] = 1;
        sids[20] = 2;
        sids[5] = 3;
        sids[6] = 4;
        sids[51] = 5;
        int next = 6;
        for (int gid = 1; gid < sids.Length; gid++)
            if (sids[gid] == 0) sids[gid] = next++;
        var face = CffFace(sids, (ColrTag, SubsetCompactColrTests.ColrV1WireForProbe()));
        var status = SubsetOperation.TryPrepare(Request(face, [0, 5, 6, 10, 20, 51]),
            out var result, out _, out var diagnostic);
        Assert.Equal(ReadStatus.Done, status);
        Assert.NotNull(result);
        Assert.Equal([0, 10, 20, 5, 6, 51],
            result!.Map!.Entries.Select(e => (int)e.InputGid).ToArray());
        _ = ColrView.TryOpen(OutputTable(result, ColrTag), out var colrView,
            out _, DataLocation.Anonymous(ColrLayout.Tag));
        Assert.Equal(ReadStatus.Done, colrView.TryReadTable(6, null,
            out var colrOut, out _));
        Assert.NotNull(colrOut!);
        Assert.Equal(2, colrOut.ClipCount);
        // Row (1, 2) is input clip [10, 20] — its box is the second wire box
        // (0, 0, 4, 4); row (3, 4) is input clip [5, 6] — the first (-10..10).
        Assert.Equal(((ushort)1, (ushort)2),
            (colrOut.Clips[0].StartGlyphId, colrOut.Clips[0].EndGlyphId));
        Assert.Equal((0, 0, 4, 4),
            (colrOut.Clips[0].Box.XMin, colrOut.Clips[0].Box.YMin,
                colrOut.Clips[0].Box.XMax, colrOut.Clips[0].Box.YMax));
        Assert.Equal(((ushort)3, (ushort)4),
            (colrOut.Clips[1].StartGlyphId, colrOut.Clips[1].EndGlyphId));
        Assert.Equal((-10, -10, 10, 10),
            (colrOut.Clips[1].Box.XMin, colrOut.Clips[1].Box.YMin,
                colrOut.Clips[1].Box.XMax, colrOut.Clips[1].Box.YMax));
    }
}
