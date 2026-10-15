// Compact-remap bitmap/color-document rebuild tests (SE card §4 commit ③).
//
// The input wires are hand-assembled canonical bytes (the CbdtCblcWriteTests
// fixture idiom), never product-emitted, so the rebuild's expectation is
// independent of the compilers under test; the output side rides product re-reads
// (the SE gate's struct oracle owns the independent verification on real corpus).
using System.Buffers.Binary;
using FontWeave.Containers;
using FontWeave.Core;
using FontWeave.Operations;
using FontWeave.Operations.Subset;
using FontWeave.Tables;
using Xunit;

namespace FontWeave.Tests;

public sealed class SubsetCompactBitmapTests
{
    private const uint CbdtTag = 0x43424454, CblcTag = 0x43424C43,
        EbdtTag = 0x45424454, EblcTag = 0x45424C43, EbscTag = 0x45425343,
        SbixTag = 0x73626978, SvgTag = 0x53564720;

    private static SubsetRequest Request(FontFaceRef face, IReadOnlyList<ushort> gids,
        SubsetMode mode) => new()
    {
        Face = face,
        Root = new SubsetGlyphRoot { Kind = SubsetRootKind.ExplicitGlyphSet, Gids = gids },
        Mode = mode,
    };

    private static FontFaceRef Spine(ushort numGlyphs, params (uint Tag, byte[] Bytes)[] extra)
    {
        var records = new List<byte[]>();
        for (int i = 0; i < numGlyphs; i++) records.Add(GlyfImages.NoContourGlyphRecord());
        var (loca, glyf, _) = GlyfImages.Assemble(records);
        var tables = new List<(uint, byte[])>
        {
            (SubsetFontFixture.HeadTag, SubsetFontFixture.Head()),
            (SubsetFontFixture.MaxpTag, SubsetFontFixture.Maxp(numGlyphs)),
            (SubsetFontFixture.GlyfTag, glyf),
            (SubsetFontFixture.LocaTag, loca),
            (SubsetFontFixture.CmapTag, SubsetFontFixture.CmapCanonical((0x41u, 0))),
        };
        tables.AddRange(extra);
        return SubsetFontFixture.Face([.. tables]);
    }

    private static void W16(byte[] b, int at, ushort v)
    {
        BinaryPrimitives.WriteUInt16BigEndian(b.AsSpan(at), v);
    }

    private static void W32(byte[] b, int at, uint v)
    {
        BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(at), v);
    }

    /// <summary>One format-17 CBDT frame: 5-byte small metrics + counted payload.</summary>
    private static byte[] Image17(byte seed) =>
        [16, 16, 0xFF, 2, 3, 0, 0, 0, 4, seed, seed, seed, seed];

    /// <summary>Canonical CBDT/CBLC pair: one strike (ppem 16, depth 32) over glyphs
    /// 2..5 with format-17 images on slots 2, 3 and 5 (slot 4 absent); the index is
    /// format 1 over the packed pool at the CBDT header's end.</summary>
    private static (byte[] Cblc, byte[] Cbdt) CbdtPair()
    {
        byte[] image = Image17(0x89);
        var cbdt = new byte[4 + 3 * image.Length];
        W16(cbdt, 0, 3);
        W16(cbdt, 2, 0);
        image.CopyTo(cbdt, 4);
        image.CopyTo(cbdt, 4 + 13);
        image.CopyTo(cbdt, 4 + 26);
        var cblc = new byte[56 + 8 + 8 + 20];
        W16(cblc, 0, 3);
        W16(cblc, 2, 0);
        W32(cblc, 4, 1);
        var row = new byte[48];
        W32(row, 0, 56);                    // indexSubtableListOffset
        W32(row, 4, 36);                    // indexSubtableListSize
        W32(row, 8, 1);                     // numIndexSubtables (colorRef @12 stays 0)
        W16(row, 40, 2);                    // startGlyphIndex
        W16(row, 42, 5);                    // endGlyphIndex
        row[44] = 16; row[45] = 16;         // ppem
        row[46] = 32;                       // bitDepth
        row[47] = 1;                        // flags
        row.CopyTo(cblc, 8);
        W16(cblc, 56, 2);
        W16(cblc, 58, 5);
        W32(cblc, 60, 8);                   // subtable offset from the list start
        W16(cblc, 64, 1);                   // indexFormat 1
        W16(cblc, 66, 17);                  // imageFormat 17
        W32(cblc, 68, 4);                   // imageDataOffset (the pool base)
        ulong[] offsets = [0, 13, 26, 26, 39];
        for (int k = 0; k < 5; k++) W32(cblc, 72 + 4 * k, (uint)offsets[k]);
        return (cblc, cbdt);
    }

    /// <summary>Canonical EBDT/EBLC pair (version 2): one strike (ppem 12, depth 1)
    /// over glyphs 1..2 with format-1 frames (5-byte small metrics + 2 data bytes).</summary>
    private static (byte[] Eblc, byte[] Ebdt) EbdtPair()
    {
        var ebdt = new byte[4 + 2 * 7];
        W16(ebdt, 0, 2);
        W16(ebdt, 2, 0);
        for (int k = 0; k < 2; k++)
        {
            int at = 4 + 7 * k;
            ebdt[at] = 2; ebdt[at + 1] = 8;
            ebdt[at + 2] = 0xFF; ebdt[at + 3] = 2; ebdt[at + 4] = 3;
            ebdt[at + 5] = (byte)(0xA0 + k); ebdt[at + 6] = 0x5A;
        }
        var eblc = new byte[56 + 8 + 8 + 12];
        W16(eblc, 0, 2);
        W16(eblc, 2, 0);
        W32(eblc, 4, 1);
        var row = new byte[48];
        W32(row, 0, 56);
        W32(row, 4, 28);
        W32(row, 8, 1);                     // numIndexSubtables
        W16(row, 40, 1);
        W16(row, 42, 2);
        row[44] = 12; row[45] = 12;
        row[46] = 1;
        row[47] = 1;
        row.CopyTo(eblc, 8);
        W16(eblc, 56, 1);
        W16(eblc, 58, 2);
        W32(eblc, 60, 8);
        W16(eblc, 64, 1);
        W16(eblc, 66, 1);
        W32(eblc, 68, 4);
        W32(eblc, 72, 0);
        W32(eblc, 76, 7);
        W32(eblc, 80, 14);
        return (eblc, ebdt);
    }

    /// <summary>A two-strike EBDT/EBLC (ppem 12 over glyphs 1..2, ppem 20 over
    /// glyphs 3..4, format-1 frames in chain order) plus an EBSC 2.0 with one
    /// scaling row per strike — the substitute-existence face the validated read
    /// enforces (EbscView.ValidateTable) needs every substitute to name a real
    /// strike.</summary>
    private static (byte[] Eblc, byte[] Ebdt, byte[] Ebsc) EbscTrio()
    {
        var ebdt = new byte[4 + 4 * 7];
        W16(ebdt, 0, 2);
        W16(ebdt, 2, 0);
        for (int k = 0; k < 4; k++)
        {
            int at = 4 + 7 * k;
            ebdt[at] = 2; ebdt[at + 1] = 8;
            ebdt[at + 2] = 0xFF; ebdt[at + 3] = 2; ebdt[at + 4] = 3;
            ebdt[at + 5] = (byte)(0xA0 + k); ebdt[at + 6] = 0x5A;
        }
        var eblc = new byte[56 + 2 * 48 + 2 * 28];
        W16(eblc, 0, 2);
        W16(eblc, 2, 0);
        W32(eblc, 4, 2);
        for (int s = 0; s < 2; s++)
        {
            var row = new byte[48];
            W32(row, 0, (uint)(152 + s * 28));  // indexSubtableListOffset
            W32(row, 4, 28);                    // indexSubtableListSize
            W32(row, 8, 1);                     // numIndexSubtables
            W16(row, 40, (ushort)(1 + 2 * s));  // startGlyphIndex
            W16(row, 42, (ushort)(2 + 2 * s));  // endGlyphIndex
            row[44] = (byte)(12 + 8 * s);
            row[45] = (byte)(12 + 8 * s);
            row[46] = 1;
            row[47] = 1;
            row.CopyTo(eblc, 8 + 48 * s);
            int listAt = 152 + s * 28;
            W16(eblc, listAt, (ushort)(1 + 2 * s));
            W16(eblc, listAt + 2, (ushort)(2 + 2 * s));
            W32(eblc, listAt + 4, 8);
            W16(eblc, listAt + 8, 1);           // indexFormat 1
            W16(eblc, listAt + 10, 1);          // imageFormat 1
            W32(eblc, listAt + 12, 4);          // imageDataOffset (the pool base)
            W32(eblc, listAt + 16, 0);
            W32(eblc, listAt + 20, 7);
            W32(eblc, listAt + 24, 14);
        }
        return (eblc, ebdt, EbscWire((12, 12, 12, 12), (20, 20, 20, 20)));
    }

    /// <summary>An EBSC 2.0 table over the given (ppemX, ppemY, subX, subY) rows;
    /// metrics bytes carry a recognizable pattern.</summary>
    private static byte[] EbscWire(params (byte X, byte Y, byte SubX, byte SubY)[] rows)
    {
        var bytes = new byte[8 + 28 * rows.Length];
        W16(bytes, 0, 2);
        W16(bytes, 2, 0);
        W32(bytes, 4, (uint)rows.Length);
        for (int r = 0; r < rows.Length; r++)
        {
            int at = 8 + 28 * r;
            for (int b = 0; b < 12; b++) bytes[at + b] = (byte)(0x30 + r);
            for (int b = 0; b < 12; b++) bytes[at + 12 + b] = (byte)(0x60 + r);
            bytes[at + 24] = rows[r].X;
            bytes[at + 25] = rows[r].Y;
            bytes[at + 26] = rows[r].SubX;
            bytes[at + 27] = rows[r].SubY;
        }
        return bytes;
    }

    /// <summary>A canonical one-strike sbix (ppem 109, ppi 72) with the given
    /// populated slots ('png ' payloads and 'dupe' targets), empty slots equal-form.</summary>
    private static byte[] SbixWire(int numGlyphs, Dictionary<int, (uint Tag, byte[] Data)> slots)
    {
        int dataLength = 0;
        foreach (var (_, (_, data)) in slots) dataLength += 8 + data.Length;
        var bytes = new byte[12 + 4 + 4 * (numGlyphs + 1) + dataLength];
        W16(bytes, 0, 1);
        W16(bytes, 2, 1);
        W32(bytes, 4, 1);
        W32(bytes, 8, 12);
        int at = 12;
        W16(bytes, at, 109);
        W16(bytes, at + 2, 72);
        int headerEnd = at + 4 + 4 * (numGlyphs + 1);
        uint running = (uint)(4 + 4 * (numGlyphs + 1));
        var dataAt = headerEnd;
        for (int g = 0; g < numGlyphs; g++)
        {
            W32(bytes, at + 4 + 4 * g, running);
            if (!slots.TryGetValue(g, out var slot)) continue;
            W32(bytes, dataAt + 4, slot.Tag);   // tag rides after the i16 origin pair
            slot.Data.CopyTo(bytes, dataAt + 8);
            running += 8 + (uint)slot.Data.Length;
            dataAt += 8 + slot.Data.Length;
        }
        W32(bytes, at + 4 + 4 * numGlyphs, running);
        return bytes;
    }

    /// <summary>A canonical SVG ␠ table: records over the given ranges, documents in
    /// first-occurrence order (record r references document docs[payload[r]]).</summary>
    private static byte[] SvgWire((ushort Start, ushort End, int Payload)[] records,
        byte[][] docs)
    {
        int listAt = 10;
        var bytes = new byte[listAt + 2 + 12 * records.Length +
            docs.Sum(d => d.Length)];
        W16(bytes, 0, 0);
        W32(bytes, 2, 10);
        W16(bytes, listAt, (ushort)records.Length);
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
            W16(bytes, at, records[r].Start);
            W16(bytes, at + 2, records[r].End);
            W32(bytes, at + 4, positions[records[r].Payload]);
            W32(bytes, at + 8, (uint)docs[records[r].Payload].Length);
        }
        return bytes;
    }

    /// <summary>A format-9 composite pair: one strike over glyphs 1..2, every slot
    /// a 14-byte composite frame (8 big metrics + counted component) naming the
    /// other glyph — the mutual shape exercises the component remap walk and the
    /// dangling-target refusal without a read-side cycle face (cycles re-answer at
    /// the compiler, so the read chain stays silent here).</summary>
    private static (byte[] Cblc, byte[] Cbdt) CbdtCompositePair()
    {
        var cbdt = new byte[4 + 2 * 14];
        W16(cbdt, 0, 3);
        W16(cbdt, 2, 0);
        for (int k = 0; k < 2; k++)
        {
            int at = 4 + 14 * k;
            for (int b = 0; b < 8; b++) cbdt[at + b] = (byte)(0x10 * (k + 1) + b);
            W16(cbdt, at + 8, 1);               // numComponents
            W16(cbdt, at + 10, (ushort)(k == 0 ? 2 : 1));
            cbdt[at + 12] = 0; cbdt[at + 13] = 0;
        }
        var cblc = new byte[56 + 8 + 8 + 12];
        W16(cblc, 0, 3);
        W16(cblc, 2, 0);
        W32(cblc, 4, 1);
        var row = new byte[48];
        W32(row, 0, 56);
        W32(row, 4, 28);
        W32(row, 8, 1);                     // numIndexSubtables
        W16(row, 40, 1);
        W16(row, 42, 2);
        row[44] = 16; row[45] = 16;
        row[46] = 1;
        row[47] = 1;
        row.CopyTo(cblc, 8);
        W16(cblc, 56, 1);
        W16(cblc, 58, 2);
        W32(cblc, 60, 8);
        W16(cblc, 64, 1);
        W16(cblc, 66, 9);
        W32(cblc, 68, 4);
        ulong[] offsets = [0, 14, 28];
        for (int k = 0; k < 3; k++) W32(cblc, 72 + 4 * k, (uint)offsets[k]);
        return (cblc, cbdt);
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

    // CBDT/CBLC: retained {0,2,4,5} keeps the images of glyphs 2 and 5, splits the
    // format-1 window into two single-slot records over the mapped bounds, and
    // rewrites the strike row's surviving span; frames travel verbatim.
    [Fact]
    public void CompactCbdtCblcPrunesSlotsAndSplitsRuns()
    {
        var (cblc, cbdt) = CbdtPair();
        var face = Spine(6, (CblcTag, cblc), (CbdtTag, cbdt));
        var status = SubsetOperation.TryPrepare(Request(face, [0, 2, 4, 5],
            SubsetMode.CompactRemap), out var result, out _, out var diagnostic);
        Assert.Equal(ReadStatus.Done, status);
        Assert.NotNull(result);
        // The rebuilt index layer re-reads: one strike, two single-slot records.
        _ = CblcView.TryOpen(OutputTable(result!, CblcTag), out var cblcView,
            out var readDiagnostic, DataLocation.Anonymous(CblcLayout.Tag));
        Assert.Equal(ReadStatus.Done, cblcView.TryReadTable(
            OutputTable(result!, CbdtTag).Length, 4, out var cblcOut, out readDiagnostic));
        Assert.Equal(1, cblcOut!.SizeCount);
        Assert.Equal((ushort)1, cblcOut.Sizes[0].StartGlyphIndex);
        Assert.Equal((ushort)3, cblcOut.Sizes[0].EndGlyphIndex);
        Assert.Equal(2, cblcOut.Sizes[0].Subtables.Count);
        var first = Assert.IsType<CblcOffsetSubtable>(cblcOut.Sizes[0].Subtables[0]);
        Assert.Equal((ushort)1, first.FirstGlyphIndex);
        Assert.Equal((ushort)1, first.LastGlyphIndex);
        var second = Assert.IsType<CblcOffsetSubtable>(cblcOut.Sizes[0].Subtables[1]);
        Assert.Equal((ushort)3, second.FirstGlyphIndex);
        Assert.Equal((ushort)3, second.LastGlyphIndex);
        // The rebuilt pool re-reads: two frames, byte-identical to their sources.
        _ = CbdtView.TryOpen(OutputTable(result!, CbdtTag), out var cbdtView,
            out _, DataLocation.Anonymous(CbdtLayout.Tag));
        Assert.Equal(ReadStatus.Done, cbdtView.TryReadTable(cblcView, 4,
            out var cbdtOut, out _));
        Assert.Equal(2, cbdtOut!.ImageCount);
        var frame = Image17(0x89);
        Assert.Equal(frame[..5], cbdtOut.Images[0].Metrics);
        Assert.Equal(frame[9..], cbdtOut.Images[0].Payload);
        Assert.Equal(17, cbdtOut.Images[0].ImageFormat);
        Assert.Equal(17, cbdtOut.Images[1].ImageFormat);
    }

    // The identity tier: full selection maps every reference to itself and the
    // already-canonical pair re-emits byte-for-byte.
    [Fact]
    public void CompactCbdtCblcFullSelectionIsByteIdentical()
    {
        var (cblc, cbdt) = CbdtPair();
        var face = Spine(6, (CblcTag, cblc), (CbdtTag, cbdt));
        var status = SubsetOperation.TryPrepare(Request(face,
            [0, 1, 2, 3, 4, 5], SubsetMode.CompactRemap), out var result, out _,
            out _);
        Assert.Equal(ReadStatus.Done, status);
        Assert.NotNull(result);
        Assert.Equal(cblc, OutputTable(result!, CblcTag));
        Assert.Equal(cbdt, OutputTable(result!, CbdtTag));
    }

    // An all-pruned pair drops both tables through the payload-time revision.
    [Fact]
    public void CompactCbdtCblcAllPrunedDropsBoth()
    {
        var (cblc, cbdt) = CbdtPair();
        var face = Spine(6, (CblcTag, cblc), (CbdtTag, cbdt));
        var status = SubsetOperation.TryPrepare(Request(face, [0],
            SubsetMode.CompactRemap), out var result, out var report, out _);
        Assert.Equal(ReadStatus.Done, status);
        Assert.NotNull(result);
        var byTag = report!.Decisions.ToDictionary(d => d.Tag);
        Assert.Equal(TableAction.Drop, byTag[CblcTag].Action);
        Assert.Equal(TableAction.Drop, byTag[CbdtTag].Action);
        Assert.Contains(CblcTag, report.DroppedTables);
        Assert.Contains(CbdtTag, report.DroppedTables);
        Assert.False(OutputHasTable(result!, CblcTag));
        Assert.False(OutputHasTable(result!, CbdtTag));
    }

    // RE3 (bitmap half): a composite frame whose component target the request did
    // not select refuses with InvalidValue at the kept slot, detail = the old GID.
    [Fact]
    public void CompactCbdtDanglingComponentRefuses()
    {
        var (cblc, cbdt) = CbdtCompositePair();
        var face = Spine(3, (CblcTag, cblc), (CbdtTag, cbdt));
        var status = SubsetOperation.TryPrepare(Request(face, [0, 2],
            SubsetMode.CompactRemap), out var result, out _, out var diagnostic);
        Assert.Equal(ReadStatus.InvalidFormat, status);
        Assert.Equal(DiagnosticCode.InvalidValue, diagnostic.Code);
        Assert.Equal(CbdtTag, diagnostic.Location.TableTag);
        Assert.Equal(1UL, diagnostic.Detail);
        Assert.Null(result);
    }

    // EBDT/EBLC: retained {0,2} keeps glyph 2's frame, remapped to output 1.
    [Fact]
    public void CompactEbdtEblcPrunesAndRemaps()
    {
        var (eblc, ebdt) = EbdtPair();
        var face = Spine(4, (EblcTag, eblc), (EbdtTag, ebdt));
        var status = SubsetOperation.TryPrepare(Request(face, [0, 2],
            SubsetMode.CompactRemap), out var result, out _, out _);
        Assert.Equal(ReadStatus.Done, status);
        Assert.NotNull(result);
        _ = EblcView.TryOpen(OutputTable(result!, EblcTag), out var eblcView,
            out var readDiagnostic, DataLocation.Anonymous(EblcLayout.Tag));
        Assert.Equal(ReadStatus.Done, eblcView.TryReadTable(
            OutputTable(result!, EbdtTag).Length, 2, out var eblcOut,
            out readDiagnostic));
        Assert.Equal(1, eblcOut!.SizeCount);
        Assert.Equal((ushort)1, eblcOut.Sizes[0].StartGlyphIndex);
        Assert.Equal((ushort)1, eblcOut.Sizes[0].EndGlyphIndex);
        var record = Assert.IsType<EblcOffsetSubtable>(eblcOut.Sizes[0].Subtables[0]);
        Assert.Equal((ushort)1, record.FirstGlyphIndex);
        Assert.Equal((ushort)1, record.LastGlyphIndex);
        _ = EbdtView.TryOpen(OutputTable(result!, EbdtTag), out var ebdtView,
            out _, DataLocation.Anonymous(EbdtLayout.Tag));
        Assert.Equal(ReadStatus.Done, ebdtView.TryReadTable(eblcView, 2,
            out var ebdtOut, out _));
        Assert.Equal(1, ebdtOut!.ImageCount);
        // The surviving frame's metrics and image bytes are the source's own.
        var metrics = ebdtOut.Images[0].Metrics!;
        Assert.Equal(2, metrics[0]);
        Assert.Equal(8, metrics[1]);
        Assert.Equal(0xA1, ebdtOut.Images[0].Data[0]);
    }

    // EBSC rides the rebuilt strike set: the strike whose glyphs all prune drops,
    // so its substitute row dies with it; the surviving row travels verbatim.
    [Fact]
    public void CompactEbscFiltersDeadSubstitutes()
    {
        var (eblc, ebdt, ebsc) = EbscTrio();
        var face = Spine(5, (EblcTag, eblc), (EbdtTag, ebdt), (EbscTag, ebsc));
        var status = SubsetOperation.TryPrepare(Request(face, [0, 2],
            SubsetMode.CompactRemap), out var result, out _, out _);
        Assert.Equal(ReadStatus.Done, status);
        Assert.NotNull(result);
        byte[] outEbsc = OutputTable(result!, EbscTag);
        Assert.Equal(1u, BinaryPrimitives.ReadUInt32BigEndian(outEbsc.AsSpan(4)));
        Assert.Equal(ebsc.AsSpan(8, 28).ToArray(), outEbsc.AsSpan(8, 28).ToArray());
    }

    // With every strike pruned the EBSC records lose their substitutes: the whole
    // trio drops (the pair via its all-empty rebuild, EBSC via the record filter).
    [Fact]
    public void CompactEbscAllDeadDropsTheTrio()
    {
        var (eblc, ebdt, ebsc) = EbscTrio();
        var face = Spine(5, (EblcTag, eblc), (EbdtTag, ebdt), (EbscTag, ebsc));
        var status = SubsetOperation.TryPrepare(Request(face, [0],
            SubsetMode.CompactRemap), out var result, out var report, out _);
        Assert.Equal(ReadStatus.Done, status);
        Assert.NotNull(result);
        var byTag = report!.Decisions.ToDictionary(d => d.Tag);
        foreach (var tag in new[] { EbdtTag, EblcTag, EbscTag })
        {
            Assert.Equal(TableAction.Drop, byTag[tag].Action);
            Assert.False(OutputHasTable(result!, tag));
        }
    }

    // RE2: an EBSC without its EBLC partner refuses through the paired read chain's
    // own missing-table face (no new diagnostic code).
    [Fact]
    public void CompactEbscWithoutEblcRefusesReadChain()
    {
        var face = Spine(1, (EbscTag, EbscWire((12, 12, 12, 12))));
        var status = SubsetOperation.TryPrepare(Request(face, [0],
            SubsetMode.CompactRemap), out var result, out _, out var diagnostic);
        Assert.Equal(ReadStatus.MissingTable, status);
        Assert.Equal(DiagnosticCode.TableNotFound, diagnostic.Code);
        Assert.Equal(EblcTag, diagnostic.Location.TableTag);
        Assert.Null(result);
    }

    // sbix: slots re-slot over the output domain, 'dupe' targets remap, the pruned
    // strike re-emits canonically.
    [Fact]
    public void CompactSbixReSlotsAndRemapsDupes()
    {
        var sbix = SbixWire(4, new Dictionary<int, (uint, byte[])>
        {
            { 1, (0x706E6720, [1, 2, 3, 4]) },
            { 2, (0x64757065, [0, 1]) },   // dupe → glyph 1 (u16 BE)
        });
        var face = Spine(4, (SbixTag, sbix));
        var status = SubsetOperation.TryPrepare(Request(face, [0, 1, 2],
            SubsetMode.CompactRemap), out var result, out _, out _);
        Assert.Equal(ReadStatus.Done, status);
        Assert.NotNull(result);
        _ = SbixView.TryOpen(OutputTable(result!, SbixTag), out var sbixView,
            out _, DataLocation.Anonymous(SbixLayout.Tag));
        Assert.Equal(ReadStatus.Done, sbixView.TryReadTable(3, out var sbixOut,
            out _));
        Assert.Equal(1, sbixOut!.StrikeCount);
        Assert.False(sbixOut.Strikes[0].Glyphs[0].HasValue);
        var png = sbixOut.Strikes[0].Glyphs[1]!.Value;
        Assert.Equal(0x706E6720u, png.GraphicType);
        Assert.Equal([1, 2, 3, 4], png.Data);
        var dupe = sbixOut.Strikes[0].Glyphs[2]!.Value;
        Assert.Equal(0x64757065u, dupe.GraphicType);
        Assert.Equal(1, BinaryPrimitives.ReadUInt16BigEndian(dupe.Data));
    }

    // RE3: a 'dupe' target the request did not select refuses with InvalidValue at
    // the referencing slot, detail = the old GID.
    [Fact]
    public void CompactSbixDanglingDupeRefuses()
    {
        var sbix = SbixWire(4, new Dictionary<int, (uint, byte[])>
        {
            { 1, (0x706E6720, [1, 2, 3, 4]) },
            { 2, (0x64757065, [0, 3]) },   // dupe → glyph 3 (u16 BE), unselected
            { 3, (0x706E6720, [9, 8, 7, 6]) },
        });
        var face = Spine(4, (SbixTag, sbix));
        var status = SubsetOperation.TryPrepare(Request(face, [0, 1, 2],
            SubsetMode.CompactRemap), out var result, out _, out var diagnostic);
        Assert.Equal(ReadStatus.InvalidFormat, status);
        Assert.Equal(DiagnosticCode.InvalidValue, diagnostic.Code);
        Assert.Equal(SbixTag, diagnostic.Location.TableTag);
        Assert.Equal(3UL, diagnostic.Detail);
        Assert.Null(result);
    }

    // An all-pruned sbix drops the table; the identity tier holds byte-for-byte.
    [Fact]
    public void CompactSbixAllPrunedDropsAndFullSelectionIsByteIdentical()
    {
        var sbix = SbixWire(4, new Dictionary<int, (uint, byte[])>
        {
            { 1, (0x706E6720, [1, 2, 3, 4]) },
        });
        var face = Spine(4, (SbixTag, sbix));
        var status = SubsetOperation.TryPrepare(Request(face, [0],
            SubsetMode.CompactRemap), out var result, out var report, out _);
        Assert.Equal(ReadStatus.Done, status);
        Assert.NotNull(result);
        Assert.Equal(TableAction.Drop,
            report!.Decisions.ToDictionary(d => d.Tag)[SbixTag].Action);
        Assert.False(OutputHasTable(result!, SbixTag));

        var full = SubsetOperation.TryPrepare(Request(face, [0, 1, 2, 3],
            SubsetMode.CompactRemap), out var fullResult, out _, out _);
        Assert.Equal(ReadStatus.Done, full);
        Assert.Equal(sbix, OutputTable(fullResult!, SbixTag));
    }

    // SVG ␠: dead records and unreferenced documents drop; the surviving records
    // re-span over the map and the documents keep their first-occurrence order.
    [Fact]
    public void CompactSvgPrunesRecordsAndDocuments()
    {
        byte[] docA = [0x61, 0x41];
        byte[] docB = [0x62, 0x42, 0x22];
        var svg = SvgWire([(2, 2, 0), (4, 5, 1), (7, 9, 0)], [docA, docB]);
        var face = Spine(10, (SvgTag, svg));
        var status = SubsetOperation.TryPrepare(Request(face, [0, 4, 5, 7],
            SubsetMode.CompactRemap), out var result, out _, out _);
        Assert.Equal(ReadStatus.Done, status);
        Assert.NotNull(result);
        _ = SvgView.TryOpen(OutputTable(result!, SvgTag), out var svgView,
            out _, DataLocation.Anonymous(SvgLayout.Tag));
        Assert.Equal(ReadStatus.Done, svgView.TryReadTable(out var svgOut, out _));
        Assert.Equal(2, svgOut!.RecordCount);
        Assert.Equal((ushort)1, svgOut.Records[0].StartGlyphID);
        Assert.Equal((ushort)2, svgOut.Records[0].EndGlyphID);
        Assert.Equal(0, svgOut.Records[0].PayloadIndex);
        Assert.Equal((ushort)3, svgOut.Records[1].StartGlyphID);
        Assert.Equal((ushort)3, svgOut.Records[1].EndGlyphID);
        Assert.Equal(1, svgOut.Records[1].PayloadIndex);
        Assert.Equal(2, svgOut.DocumentCount);
        Assert.Equal(docB, svgOut.Documents[0]);
        Assert.Equal(docA, svgOut.Documents[1]);
    }

    // The SVG identity tier: full selection re-emits the canonical source verbatim.
    [Fact]
    public void CompactSvgFullSelectionIsByteIdentical()
    {
        byte[] docA = [0x61, 0x41];
        byte[] docB = [0x62, 0x42, 0x22];
        var svg = SvgWire([(2, 2, 0), (4, 5, 1), (7, 9, 0)], [docA, docB]);
        var face = Spine(10, (SvgTag, svg));
        var status = SubsetOperation.TryPrepare(Request(face,
            [0, 1, 2, 3, 4, 5, 6, 7, 8, 9], SubsetMode.CompactRemap), out var result,
            out _, out _);
        Assert.Equal(ReadStatus.Done, status);
        Assert.NotNull(result);
        Assert.Equal(svg, OutputTable(result!, SvgTag));
    }

    // Retain-GIDs zero drift: every bitmap/document row keeps its raw bytes.
    [Fact]
    public void RetainKeepsBitmapTablesRaw()
    {
        var (cblc, cbdt) = CbdtPair();
        var sbix = SbixWire(4, new Dictionary<int, (uint, byte[])>
        {
            { 1, (0x706E6720, [1, 2, 3, 4]) },
        });
        var face = Spine(6, (CblcTag, cblc), (CbdtTag, cbdt), (SbixTag, sbix));
        var status = SubsetOperation.TryPrepare(Request(face, [0, 2, 4, 5],
            SubsetMode.RetainGids), out var result, out var report, out _);
        Assert.Equal(ReadStatus.Done, status);
        Assert.NotNull(result);
        var byTag = report!.Decisions.ToDictionary(d => d.Tag);
        foreach (var tag in new[] { CblcTag, CbdtTag, SbixTag })
            Assert.Equal(TableAction.Preserve, byTag[tag].Action);
        Assert.Equal(cblc, OutputTable(result!, CblcTag));
        Assert.Equal(cbdt, OutputTable(result!, CbdtTag));
        Assert.Equal(sbix, OutputTable(result!, SbixTag));
    }
}
