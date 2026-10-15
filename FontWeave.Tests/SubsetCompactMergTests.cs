// Compact-remap MERG rebuild tests (SE card §4 commit ④).
//
// The input wires are hand-assembled canonical bytes (the bitmap test file's
// idiom), never product-emitted, so the rebuild's expectation is independent of
// the compilers under test; the output side rides product re-reads.
using System.Buffers.Binary;
using FontWeave.Containers;
using FontWeave.Core;
using FontWeave.Operations;
using FontWeave.Operations.Subset;
using FontWeave.Tables.Layout;
using Xunit;

namespace FontWeave.Tests;

public sealed class SubsetCompactMergTests
{
    private const uint MergTag = 0x4D455247;

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

    /// <summary>One format-1 ClassDef body: dense values over
    /// [startGlyph, startGlyph + values.Length).</summary>
    private static byte[] DenseBody(ushort startGlyph, params ushort[] values)
    {
        var bytes = new byte[6 + 2 * values.Length];
        W16(bytes, 0, 1);
        W16(bytes, 2, startGlyph);
        W16(bytes, 4, (ushort)values.Length);
        for (int i = 0; i < values.Length; i++) W16(bytes, 6 + 2 * i, values[i]);
        return bytes;
    }

    /// <summary>One format-2 ClassDef body over the given ranges.</summary>
    private static byte[] SparseBody(params (ushort Start, ushort End, ushort Class)[] ranges)
    {
        var bytes = new byte[4 + 6 * ranges.Length];
        W16(bytes, 0, 2);
        W16(bytes, 2, (ushort)ranges.Length);
        for (int i = 0; i < ranges.Length; i++)
        {
            W16(bytes, 4 + 6 * i, ranges[i].Start);
            W16(bytes, 6 + 6 * i, ranges[i].End);
            W16(bytes, 8 + 6 * i, ranges[i].Class);
        }
        return bytes;
    }

    /// <summary>A canonical MERG v0 wire: header(10), the classDefOffsets array,
    /// the bodies in first-arrival slot order and the row-major matrix last.</summary>
    private static byte[] MergWire(int mergeClassCount, byte[] matrix,
        byte[][] bodies, int[] slotToBody)
    {
        int offsetsAt = 10;
        int bodiesAt = offsetsAt + 2 * slotToBody.Length;
        var bodyOffsets = new ushort[bodies.Length];
        int cursor = bodiesAt;
        for (int b = 0; b < bodies.Length; b++)
        {
            bodyOffsets[b] = (ushort)cursor;
            cursor += bodies[b].Length;
        }
        int matrixAt = cursor;
        var bytes = new byte[matrixAt + matrix.Length];
        W16(bytes, 0, 0);
        W16(bytes, 2, (ushort)mergeClassCount);
        W16(bytes, 4, (ushort)matrixAt);
        W16(bytes, 6, (ushort)slotToBody.Length);
        W16(bytes, 8, (ushort)offsetsAt);
        for (int s = 0; s < slotToBody.Length; s++)
            W16(bytes, offsetsAt + 2 * s, bodyOffsets[slotToBody[s]]);
        for (int b = 0; b < bodies.Length; b++) bodies[b].CopyTo(bytes, bodyOffsets[b]);
        matrix.CopyTo(bytes, matrixAt);
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

    private static byte[] MergWire6() => MergWire(3,
        [0x01, 0x02, 0x00, 0x10, 0x00, 0x00, 0x20, 0x00, 0x00],
        [
            DenseBody(2, 1, 0, 3),                     // glyphs 2..4 (canonical: the
                                                       // span starts at its first
                                                       // non-zero), g3's zero explicit
            SparseBody((5, 5, 1)),                     // glyph 5 (after the dense span)
        ], [0, 1]);

    // The class defs re-answer over the output domain in their own formats while
    // the class-keyed matrix travels byte-verbatim (OQ-SE-4).
    [Fact]
    public void CompactMergRebuildsBodiesAndKeepsMatrix()
    {
        var merg = MergWire6();
        var face = Spine(6, (MergTag, merg));
        var status = SubsetOperation.TryPrepare(Request(face, [0, 2, 4, 5],
            SubsetMode.CompactRemap), out var result, out _, out _);
        Assert.Equal(ReadStatus.Done, status);
        Assert.NotNull(result);
        _ = MergView.TryOpen(OutputTable(result!, MergTag), out var mergView,
            out _, DataLocation.Anonymous(MergLayout.Tag));
        Assert.Equal(ReadStatus.Done, mergView.TryReadTable(4, out var mergOut,
            out _));
        Assert.Equal((ushort)3, mergOut!.MergeClassCount);
        // The matrix bytes are the source's own, at the rebuilt table's offset.
        var matrix = new byte[9];
        mergOut.Matrix.CopyTo(matrix);
        Assert.Equal(new byte[] { 0x01, 0x02, 0x00, 0x10, 0x00, 0x00, 0x20, 0x00, 0x00 },
            matrix);
        // The dense body re-answers: outputs {0,1,2,3} carry {0,1,3,0} — the
        // dense span [1,2] with values {1,3}.
        Assert.Equal(1, mergOut.ClassDefSlots[0].Format);
        Assert.Equal(0, mergOut.ClassDefSlots[0].ClassOf(0));
        Assert.Equal(1, mergOut.ClassDefSlots[0].ClassOf(1));
        Assert.Equal(3, mergOut.ClassDefSlots[0].ClassOf(2));
        Assert.Equal(0, mergOut.ClassDefSlots[0].ClassOf(3));
        // The sparse body keeps only the non-zero runs: {0,0,0,1}.
        Assert.Equal(2, mergOut.ClassDefSlots[1].Format);
        Assert.Equal(0, mergOut.ClassDefSlots[1].ClassOf(0));
        Assert.Equal(0, mergOut.ClassDefSlots[1].ClassOf(1));
        Assert.Equal(0, mergOut.ClassDefSlots[1].ClassOf(2));
        Assert.Equal(1, mergOut.ClassDefSlots[1].ClassOf(3));
        Assert.Equal(4, mergOut.NumGlyphs);
    }

    // The identity tier: full selection re-emits the canonical source verbatim.
    [Fact]
    public void CompactMergFullSelectionIsByteIdentical()
    {
        var merg = MergWire6();
        var face = Spine(6, (MergTag, merg));
        var status = SubsetOperation.TryPrepare(Request(face,
            [0, 1, 2, 3, 4, 5], SubsetMode.CompactRemap), out var result, out _,
            out _);
        Assert.Equal(ReadStatus.Done, status);
        Assert.NotNull(result);
        Assert.Equal(merg, OutputTable(result!, MergTag));
    }

    // Shared bodies stay shared: two slots aimed at one wire body keep one
    // instance through the rebuild; two distinct bodies whose glyphs all prune
    // rebuild to the same empty content and dedupe to one emission body.
    [Fact]
    public void CompactMergKeepsSharedBodiesAndDedupesEqualContent()
    {
        var merg = MergWire(2, [0x01, 0x00, 0x00, 0x01],
        [
            SparseBody((1, 2, 2)),                     // slots 0 and 1 share it
            SparseBody((4, 4, 2)),                     // after the first body's span
        ], [0, 0, 1]);
        var face = Spine(6, (MergTag, merg));
        var status = SubsetOperation.TryPrepare(Request(face, [0],
            SubsetMode.CompactRemap), out var result, out _, out _);
        Assert.Equal(ReadStatus.Done, status);
        Assert.NotNull(result);
        _ = MergView.TryOpen(OutputTable(result!, MergTag), out var mergView,
            out _, DataLocation.Anonymous(MergLayout.Tag));
        Assert.Equal(ReadStatus.Done, mergView.TryReadTable(1, out var mergOut,
            out _));
        // Every glyph of both bodies pruned: both rebuild to the empty sparse
        // form, dedupe onto one representative, and every slot aims there.
        Assert.Same(mergOut!.ClassDefSlots[0], mergOut.ClassDefSlots[1]);
        Assert.Same(mergOut.ClassDefSlots[0], mergOut.ClassDefSlots[2]);
        Assert.Single(mergOut.ClassDefs);
        // The canonical emission carries exactly one (4-byte) body.
        byte[] outBytes = OutputTable(result!, MergTag);
        ushort classDefCount = BinaryPrimitives.ReadUInt16BigEndian(outBytes.AsSpan(6));
        Assert.Equal(3, classDefCount);
        ushort offsetsAt = BinaryPrimitives.ReadUInt16BigEndian(outBytes.AsSpan(8));
        ushort firstOffset = BinaryPrimitives.ReadUInt16BigEndian(
            outBytes.AsSpan(offsetsAt));
        ushort secondOffset = BinaryPrimitives.ReadUInt16BigEndian(
            outBytes.AsSpan(offsetsAt + 2));
        ushort thirdOffset = BinaryPrimitives.ReadUInt16BigEndian(
            outBytes.AsSpan(offsetsAt + 4));
        Assert.Equal(firstOffset, secondOffset);
        Assert.Equal(firstOffset, thirdOffset);
        ushort matrixAt = BinaryPrimitives.ReadUInt16BigEndian(outBytes.AsSpan(4));
        Assert.Equal(matrixAt, firstOffset + 4); // one empty body, then the matrix
    }

    // An all-zero body rebuilds to the empty form of its own format; retain
    // keeps every MERG byte raw.
    [Fact]
    public void CompactMergAllZeroBodyAndRetainZeroDrift()
    {
        var merg = MergWire(2, [0x00, 0x01, 0x00, 0x00],
        [
            DenseBody(1, 1, 0, 0, 0),                  // g1=1, g2..4=0
        ], [0]);
        var face = Spine(6, (MergTag, merg));
        // Compact {0,5}: the only non-zero member prunes away — the body
        // rebuilds to the empty dense form; the table stays (never drops).
        var status = SubsetOperation.TryPrepare(Request(face, [0, 5],
            SubsetMode.CompactRemap), out var result, out _, out _);
        Assert.Equal(ReadStatus.Done, status);
        Assert.NotNull(result);
        _ = MergView.TryOpen(OutputTable(result!, MergTag), out var mergView,
            out _, DataLocation.Anonymous(MergLayout.Tag));
        Assert.Equal(ReadStatus.Done, mergView.TryReadTable(2, out var mergOut,
            out _));
        Assert.Equal(1, mergOut!.ClassDefSlots[0].Format);
        Assert.Equal(0, mergOut.ClassDefSlots[0].Ranges.Length);
        Assert.Equal(0, mergOut.ClassDefSlots[0].ClassOf(0));
        Assert.Equal(0, mergOut.ClassDefSlots[0].ClassOf(1));

        // Retain twin: raw copy, byte-for-byte.
        var retain = SubsetOperation.TryPrepare(Request(face, [0, 2, 4, 5],
            SubsetMode.RetainGids), out var retainResult, out var retainReport,
            out _);
        Assert.Equal(ReadStatus.Done, retain);
        Assert.NotNull(retainResult);
        Assert.Equal(TableAction.Preserve,
            retainReport!.Decisions.ToDictionary(d => d.Tag)[MergTag].Action);
        Assert.Equal(merg, OutputTable(retainResult!, MergTag));
    }
}
