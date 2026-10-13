// Synthetic standalone-sfnt fixtures for the subset operation tests.
//
// Every image is assembled from raw wire bytes directly, never by asking the product to
// write it, so a test feeds the operation an independent expectation. Only the fields the
// subset pipeline actually reads are populated; everything else stays zero.
using System.Buffers.Binary;
using FontWeave.Containers;
using FontWeave.Core;

namespace FontWeave.Tests;

internal static class SubsetFontFixture
{
    internal const uint HeadTag = 0x68656164, MaxpTag = 0x6D617870, GlyfTag = 0x676C7966,
        LocaTag = 0x6C6F6361, CmapTag = 0x636D6170, NameTag = 0x6E616D65,
        GsubTag = 0x47535542, DsigTag = 0x44534947, PostTag = 0x706F7374,
        FvarTag = 0x66766172, CffTag = 0x43464620, ZzZzTag = 0x5A7A5A7A;

    /// <summary>Assembles a standalone sfnt 1.0 directory over the given tables, records
    /// sorted by tag and payloads 4-byte aligned, mirroring the container's own layout.
    /// With <paramref name="sealedChecksums"/> the directory carries real per-table
    /// checksums and head.checkSumAdjustment is derived by the specification formula, so
    /// a re-emitted font can be compared byte-for-byte with the input.</summary>
    internal static byte[] Sfnt(bool sealedChecksums, params (uint Tag, byte[] Bytes)[] tables)
    {
        Array.Sort(tables, static (a, b) => a.Tag.CompareTo(b.Tag));
        int count = tables.Length;
        long offset = 12 + 16L * count;
        var offsets = new uint[count];
        for (int i = 0; i < count; i++)
        {
            offsets[i] = (uint)offset;
            offset += (tables[i].Bytes.Length + 3L) & ~3L;
        }
        var bytes = new byte[offset];
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(0), 0x00010000);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(4), (ushort)count);
        int searchRange = 16, selector = 0;
        while (searchRange <= count * 8) { searchRange *= 2; selector++; }
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(6), (ushort)searchRange);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(8), (ushort)selector);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(10), (ushort)(16 * count - searchRange));
        for (int i = 0; i < count; i++)
        {
            int record = 12 + 16 * i;
            BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(record), tables[i].Tag);
            if (sealedChecksums)
                BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(record + 4),
                    Checksum32(tables[i].Bytes));
            BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(record + 8), offsets[i]);
            BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(record + 12),
                (uint)tables[i].Bytes.Length);
            tables[i].Bytes.CopyTo(bytes.AsSpan((int)offsets[i]));
        }
        if (sealedChecksums)
        {
            int headOffset = -1;
            for (int i = 0; i < count; i++)
                if (tables[i].Tag == HeadTag) headOffset = (int)offsets[i];
            bytes.AsSpan(headOffset + 8, 4).Clear();
            uint adjustment = unchecked(0xB1B0AFBAu - Checksum32(bytes));
            BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(headOffset + 8), adjustment);
        }
        return bytes;
    }

    /// <summary>The sfnt checksum: u32 big-endian sums over the 4-byte-padded image.</summary>
    private static uint Checksum32(ReadOnlySpan<byte> bytes)
    {
        uint sum = 0;
        int at = 0;
        for (; at + 4 <= bytes.Length; at += 4)
            sum = unchecked(sum + BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(at, 4)));
        uint last = 0;
        for (int i = 0; at + i < bytes.Length; i++)
            last |= (uint)bytes[at + i] << (24 - 8 * i);
        return unchecked(sum + last);
    }

    internal static FontFaceRef Face(params (uint Tag, byte[] Bytes)[] tables) =>
        Face(false, tables);

    internal static FontFaceRef Face(bool sealedChecksums,
        params (uint Tag, byte[] Bytes)[] tables)
    {
        _ = FontSource.TryCopy(Sfnt(sealedChecksums, tables), out FontSource? source, out _);
        _ = source!.TryGetFace(0, out FontFaceRef face, out _);
        return face;
    }

    internal static byte[] Head(short indexToLocFormat = 1)
    {
        var bytes = new byte[54];
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(0), 0x00010000);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(4), 0x00010000);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(12), 0x5F0F3CF5);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(18), 1000);
        BinaryPrimitives.WriteInt16BigEndian(bytes.AsSpan(48), 2);
        BinaryPrimitives.WriteInt16BigEndian(bytes.AsSpan(50), indexToLocFormat);
        return bytes;
    }

    internal static byte[] Maxp(ushort numGlyphs)
    {
        var bytes = new byte[32];
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(0), 0x00010000);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(4), numGlyphs);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(14), 1); // maxZones
        return bytes;
    }

    /// <summary>A one-record cmap shell whose single subtable declares
    /// <paramref name="format"/>; only the declared extent is guaranteed.</summary>
    internal static byte[] CmapShell(ushort format, ushort platform = 3, ushort encoding = 1)
    {
        byte[] subtable = CmapSubtableShell(format);
        var bytes = new byte[4 + 8 + subtable.Length];
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(0), 0);      // version
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(2), 1);      // numTables
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(4), platform);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(6), encoding);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(8), 12);     // subtable offset
        subtable.CopyTo(bytes.AsSpan(12));
        return bytes;
    }

    private static byte[] CmapSubtableShell(ushort format)
    {
        var bytes = format is 8 or 10 or 12 or 13 ? new byte[16] :
            format == 14 ? new byte[12] : new byte[8];
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(0), format);
        if (format is 8 or 10 or 12 or 13)
        {
            BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(4), (uint)bytes.Length);
        }
        else if (format == 14)
        {
            BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(2), (uint)bytes.Length);
        }
        else
        {
            BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(2), (ushort)bytes.Length);
            BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(4), 0); // language
        }
        return bytes;
    }

    /// <summary>A real two-signature DSIG (payload longer than the header), so the empty
    /// marker's length test cannot pass.</summary>
    internal static byte[] DsigReal()
    {
        var bytes = new byte[8 + 12 + 4];
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(0), 1);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(4), 2);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(6), 0);
        return bytes;
    }

    /// <summary>An empty DSIG marker: version 1, zero signatures, zero flags, header-only.</summary>
    internal static byte[] DsigEmpty()
    {
        var bytes = new byte[8];
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(0), 1);
        return bytes;
    }

    internal static byte[] Post25(ushort numGlyphs)
    {
        var bytes = new byte[32 + 2 + numGlyphs];
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(0), 0x00025000);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(32), numGlyphs);
        return bytes;
    }

    /// <summary>A canonical cmap table {((3,1), format 4), ((3,10), format 12)} over the
    /// given mappings, laid out exactly as a canonical re-emission would place it: two
    /// records, payloads sequential with no alignment. Test-side independent encoding.</summary>
    internal static byte[] CmapCanonical(params (uint Code, ushort Gid)[] mappings)
    {
        var sorted = new List<(uint Code, ushort Gid)>(mappings);
        sorted.Sort(static (a, b) => a.Code.CompareTo(b.Code));
        byte[] format4 = CanonicalFormat4([.. sorted]);
        byte[] format12 = CanonicalFormat12([.. sorted]);
        var bytes = new byte[4 + 8 * 2 + format4.Length + format12.Length];
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(2), 2);
        WriteRecord(bytes, 4, 3, 1, 20);
        WriteRecord(bytes, 12, 3, 10, (uint)(20 + format4.Length));
        format4.CopyTo(bytes.AsSpan(20));
        format12.CopyTo(bytes.AsSpan(20 + format4.Length));
        return bytes;
    }

    /// <summary>Canonical cmap plus a (1,0) Mac Roman format 6 record, mirroring the
    /// record shape real corpora carry (roboto/source-sans).</summary>
    internal static byte[] CmapWithMacRoman(params (uint Code, ushort Gid)[] mappings)
    {
        var sorted = new List<(uint Code, ushort Gid)>(mappings);
        sorted.Sort(static (a, b) => a.Code.CompareTo(b.Code));
        byte[] format6 = CanonicalFormat6((uint)sorted[0].Code, [.. sorted]);
        byte[] format4 = CanonicalFormat4([.. sorted]);
        byte[] format12 = CanonicalFormat12([.. sorted]);
        var bytes = new byte[4 + 8 * 3 + format6.Length + format4.Length + format12.Length];
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(2), 3);
        WriteRecord(bytes, 4, 1, 0, 28);
        WriteRecord(bytes, 12, 3, 1, (uint)(28 + format6.Length));
        WriteRecord(bytes, 20, 3, 10, (uint)(28 + format6.Length + format4.Length));
        format6.CopyTo(bytes.AsSpan(28));
        format4.CopyTo(bytes.AsSpan(28 + format6.Length));
        format12.CopyTo(bytes.AsSpan(28 + format6.Length + format4.Length));
        return bytes;
    }

    private static void WriteRecord(byte[] bytes, int at, ushort platform, ushort encoding,
        uint offset)
    {
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(at), platform);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(at + 2), encoding);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(at + 4), offset);
    }

    internal static List<(uint Start, uint End, ushort FirstGid)> MergeRuns(
        IReadOnlyList<(uint Code, ushort Gid)> mappings)
    {
        var runs = new List<(uint Start, uint End, ushort FirstGid)>();
        foreach (var (code, gid) in mappings)
        {
            if (runs.Count > 0)
            {
                var last = runs[^1];
                if (code == last.End + 1 && gid == last.FirstGid + (int)(code - last.Start))
                {
                    runs[^1] = (last.Start, code, last.FirstGid);
                    continue;
                }
            }
            runs.Add((code, code, gid));
        }
        return runs;
    }

    private static byte[] CanonicalFormat4(IReadOnlyList<(uint Code, ushort Gid)> mappings)
    {
        var bmp = new List<(uint Code, ushort Gid)>();
        foreach (var (code, gid) in mappings)
            if (code <= 0xFFFF) bmp.Add((code, gid));
        var runs = MergeRuns(bmp);
        int count = runs.Count + 1; // plus the structural sentinel
        int length = 16 + 8 * count;
        var bytes = new byte[length];
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(0), 4);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(2), (ushort)length);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(6), (ushort)(count * 2));
        int power = 1, selector = 0;
        while (power <= count / 2) { power *= 2; selector++; }
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(8), (ushort)(power * 2));
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(10), (ushort)selector);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(12), (ushort)(count * 2 - power * 2));
        for (int r = 0; r < runs.Count; r++)
        {
            ushort endCode = (ushort)runs[r].End;
            ushort startCode = (ushort)runs[r].Start;
            ushort idDelta = unchecked((ushort)(runs[r].FirstGid - (int)runs[r].Start));
            BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(14 + 2 * r), endCode);
            BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(16 + 2 * count + 2 * r), startCode);
            BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(16 + 4 * count + 2 * r), idDelta);
            // idRangeOffset 0: the affine delta path, matching the canonical writer.
        }
        // Sentinel: (0xFFFF, 0xFFFF) with the literal delta 1, resolving to GID 0.
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(14 + 2 * runs.Count), 0xFFFF);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(16 + 2 * count + 2 * runs.Count), 0xFFFF);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(16 + 4 * count + 2 * runs.Count), 1);
        return bytes;
    }

    private static byte[] CanonicalFormat12(IReadOnlyList<(uint Code, ushort Gid)> mappings)
    {
        var runs = MergeRuns(mappings);
        int length = 16 + 12 * runs.Count;
        var bytes = new byte[length];
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(0), 12);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(4), (uint)length);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(12), (uint)runs.Count);
        for (int r = 0; r < runs.Count; r++)
        {
            int at = 16 + 12 * r;
            BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(at), runs[r].Start);
            BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(at + 4), runs[r].End);
            BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(at + 8), runs[r].FirstGid);
        }
        return bytes;
    }

    private static byte[] CanonicalFormat6(uint firstCode,
        IReadOnlyList<(uint Code, ushort Gid)> mappings)
    {
        int length = 10 + 2 * mappings.Count;
        var bytes = new byte[length];
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(0), 6);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(2), (ushort)length);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(6), (ushort)firstCode);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(8), (ushort)mappings.Count);
        for (int i = 0; i < mappings.Count; i++)
            BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(10 + 2 * i), mappings[i].Gid);
        return bytes;
    }

    internal static byte[] Dummy(int length = 16) => new byte[length];

    /// <summary>A one-contour one-point simple glyph carrying one instruction byte and
    /// short-coordinate deltas; 18 bytes on the wire.</summary>
    internal static byte[] InstructedSimpleGlyph()
    {
        var body = new List<byte>();
        WriteI16(body, 1);                    // numberOfContours
        for (int i = 0; i < 4; i++) WriteI16(body, 0); // bounding box
        WriteU16(body, 0);                    // endPtsOfContours[0]
        WriteU16(body, 1);                    // instructionLength
        body.Add(0x00);                       // one instruction byte
        body.Add(0x37);                       // flag: on-curve, x-short, y-short
        body.Add(5);                          // x delta
        body.Add(7);                          // y delta
        return [.. body];
    }

    internal static List<byte> WriteI16(List<byte> body, short value)
    {
        int raw = value & 0xFFFF;
        body.Add((byte)(raw >> 8));
        body.Add((byte)(raw & 0xFF));
        return body;
    }

    internal static List<byte> WriteU16(List<byte> body, ushort value)
    {
        body.Add((byte)(value >> 8));
        body.Add((byte)value);
        return body;
    }
}
