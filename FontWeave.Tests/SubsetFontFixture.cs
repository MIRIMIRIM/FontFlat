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
    internal const uint Os2Tag = 0x4F532F32;

    internal static uint LocaOffset(byte[] head, byte[] loca, int index) =>
        BinaryPrimitives.ReadInt16BigEndian(head.AsSpan(50)) switch
        {
            0 => 2u * BinaryPrimitives.ReadUInt16BigEndian(loca.AsSpan(index * 2)),
            1 => BinaryPrimitives.ReadUInt32BigEndian(loca.AsSpan(index * 4)),
            _ => throw new InvalidOperationException("Invalid loca format")
        };

    internal const uint HeadTag = 0x68656164, MaxpTag = 0x6D617870, GlyfTag = 0x676C7966,
        LocaTag = 0x6C6F6361, CmapTag = 0x636D6170, NameTag = 0x6E616D65,
        GsubTag = 0x47535542, DsigTag = 0x44534947, PostTag = 0x706F7374,
        FvarTag = 0x66766172, CffTag = 0x43464620, ZzZzTag = 0x5A7A5A7A,
        GdefTag = 0x47444546, GposTag = 0x47504F53, KernTag = 0x6B65726E;
    // The variable-family tags (SV slice): append-only additions for the VF profile
    // fixtures, per the shared-fixture convention (never edit an existing member).
    internal const uint Cff2Tag = 0x43464632, GvarTag = 0x67766172, HvarTag = 0x48564152,
        VvarTag = 0x56564152, CvarTag = 0x63766172, MvarTag = 0x4D564152,
        AvarTag = 0x61766172, StatTag = 0x53544154, CvtTag = 0x63767420,
        VorgTag = 0x564F5247;

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

    /// <summary>A version-2 OS/2 table with the legacy fsSelection high bits and the
    /// ulCodePageRange reserved bits set: the commercial-font dialect the tolerant
    /// validation admits and the strict entry refuses. Only the checked fields are
    /// non-zero; the rest of the fixed layout stays zero.</summary>
    internal static byte[] Os2Legacy(ushort fsSelection = 0x0101,
        uint codePageRange1 = 0x00000201, uint codePageRange2 = 0x00000001)
    {
        var bytes = new byte[96];
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(0), 2);   // version 2
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(4), 400); // weight
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(6), 5);   // width
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(62), fsSelection);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(78), codePageRange1);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(82), codePageRange2);
        return bytes;
    }

    /// <summary>A version-2 OS/2 table with only spec-legal values.</summary>
    internal static byte[] Os2Strict()
    {
        var bytes = new byte[96];
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(0), 2);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(4), 400);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(6), 5);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(62), 0x0040);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(78), 0x00000001);
        return bytes;
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

    /// <summary>A minimal valid format 2 subtable: all 256 subHeaderKeys zero (the
    /// single-byte path), one subHeader with one entry pointing at a zero glyph. The
    /// mapping itself is irrelevant: a (1,x) record is consumed as evidence, never
    /// merged. Test-side independent encoding.</summary>
    internal static byte[] MacFormat2Subtable()
    {
        const int keysEnd = 6 + 2 * 256;
        var bytes = new byte[keysEnd + 8 + 2];
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(0), 2);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(2), (ushort)bytes.Length);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(4), 0); // language
        // subHeaders[0] at keysEnd: firstCode 0, entryCount 1, idDelta 0, idRangeOffset 2.
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(keysEnd + 2), 1);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(keysEnd + 6), 2);
        // glyphIndexArray[0] at keysEnd+8 stays zero (unmapped).
        return bytes;
    }

    /// <summary>A (1,1) format 2 record ahead of the canonical (3,1) format 4: the
    /// historical Mac Japanese shape the admission gate used to refuse.</summary>
    internal static byte[] CmapWithMacFormat2(params (uint Code, ushort Gid)[] mappings)
    {
        var sorted = new List<(uint Code, ushort Gid)>(mappings);
        sorted.Sort(static (a, b) => a.Code.CompareTo(b.Code));
        byte[] format2 = MacFormat2Subtable();
        byte[] format4 = CanonicalFormat4([.. sorted]);
        var bytes = new byte[4 + 8 * 2 + format2.Length + format4.Length];
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(2), 2);
        WriteRecord(bytes, 4, 1, 1, 20);
        WriteRecord(bytes, 12, 3, 1, (uint)(20 + format2.Length));
        format2.CopyTo(bytes.AsSpan(20));
        format4.CopyTo(bytes.AsSpan(20 + format2.Length));
        return bytes;
    }

    /// <summary>Inserts <paramref name="gap"/> bytes before the first subtable and
    /// appends <paramref name="tail"/> bytes, shifting every record offset. The fill
    /// byte makes the zero-padding tolerance testable in both directions.</summary>
    internal static byte[] PadCmap(byte[] cmap, int gap, int tail, byte fill = 0)
    {
        int tables = BinaryPrimitives.ReadUInt16BigEndian(cmap.AsSpan(2));
        int header = 4 + 8 * tables;
        var result = new byte[cmap.Length + gap + tail];
        cmap.AsSpan(0, header).CopyTo(result);
        for (int i = 0; i < tables; i++)
        {
            uint offset = BinaryPrimitives.ReadUInt32BigEndian(cmap.AsSpan(8 + 8 * i));
            BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(8 + 8 * i), offset + (uint)gap);
        }
        for (int i = 0; i < gap; i++) result[header + i] = fill;
        cmap.AsSpan(header).CopyTo(result.AsSpan(header + gap));
        for (int i = 0; i < tail; i++) result[result.Length - tail + i] = fill;
        return result;
    }

    /// <summary>One valid format 14 subtable: a single variation selector carrying the
    /// given non-default mappings (in the given order) and no default-UVS table.
    /// Test-side independent encoding.</summary>
    internal static byte[] UvsSubtable(uint varSelector,
        params (uint UnicodeValue, ushort Gid)[] nonDefault)
    {
        int length = 10 + 11 + 4 + 5 * nonDefault.Length;
        var bytes = new byte[length];
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(0), 14);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(2), (uint)length);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(6), 1);       // one selector
        bytes[10] = (byte)((varSelector >> 16) & 0xFF);                  // u24 varSelector
        bytes[11] = (byte)((varSelector >> 8) & 0xFF);
        bytes[12] = (byte)(varSelector & 0xFF);
        // defaultUVSOffset (13..16) stays zero; the non-default table follows the record.
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(17), 10 + 11);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(21), (uint)nonDefault.Length);
        for (int i = 0; i < nonDefault.Length; i++)
        {
            bytes[25 + 5 * i] = (byte)((nonDefault[i].UnicodeValue >> 16) & 0xFF);  // u24
            bytes[26 + 5 * i] = (byte)((nonDefault[i].UnicodeValue >> 8) & 0xFF);
            bytes[27 + 5 * i] = (byte)(nonDefault[i].UnicodeValue & 0xFF);
            BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(28 + 5 * i), nonDefault[i].Gid);
        }
        return bytes;
    }

    /// <summary>Canonical cmap plus a (0,5) Unicode variation sequence record over one
    /// real format 14 subtable (selector U+FE0F mapping 0x41 to glyph 3 non-default),
    /// mirroring the record shape real corpora carry (stix/noto). Test-side independent
    /// encoding.</summary>
    internal static byte[] CmapWithUvs(params (uint Code, ushort Gid)[] mappings)
    {
        var sorted = new List<(uint Code, ushort Gid)>(mappings);
        sorted.Sort(static (a, b) => a.Code.CompareTo(b.Code));
        byte[] uvs = UvsSubtable(0xFE0F, (0x41, 3));
        byte[] format4 = CanonicalFormat4([.. sorted]);
        byte[] format12 = CanonicalFormat12([.. sorted]);
        var bytes = new byte[4 + 8 * 3 + uvs.Length + format4.Length + format12.Length];
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(2), 3);
        WriteRecord(bytes, 4, 0, 5, 28);
        WriteRecord(bytes, 12, 3, 1, (uint)(28 + uvs.Length));
        WriteRecord(bytes, 20, 3, 10, (uint)(28 + uvs.Length + format4.Length));
        uvs.CopyTo(bytes.AsSpan(28));
        format4.CopyTo(bytes.AsSpan(28 + uvs.Length));
        format12.CopyTo(bytes.AsSpan(28 + uvs.Length + format4.Length));
        return bytes;
    }

    /// <summary>A one-record cmap whose only record is (0,5) over one real format 14
    /// subtable (selector U+FE0F, one non-default 0x41 mapping): the pure-UVS cmap the
    /// S0 freeze registered as its C5 real-corpus witness shape.</summary>
    internal static byte[] CmapUvsOnly(ushort uvsGid)
    {
        byte[] uvs = UvsSubtable(0xFE0F, (0x41, uvsGid));
        var bytes = new byte[4 + 8 + uvs.Length];
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(0), 0);       // version
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(2), 1);
        WriteRecord(bytes, 4, 0, 5, 12);
        uvs.CopyTo(bytes.AsSpan(12));
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

    // ---- variable-family fixtures (SV slice; append-only) ------------------------

    /// <summary>A valid maxp 0.5 (the CFF/CFF2 face's version): version word plus
    /// numGlyphs, nothing else.</summary>
    internal static byte[] Maxp05(ushort numGlyphs)
    {
        var bytes = new byte[6];
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(0), 0x00005000);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(4), numGlyphs);
        return bytes;
    }

    /// <summary>A valid fvar 1.0 carrying <paramref name="axisCount"/> 'wght'-tagged
    /// axes in [0, 1, 1] and zero instances (the axes' name ID 256 is the hard
    /// domain's floor). The subset operation reads this table as the axis authority;
    /// the wire layout is test-side independent encoding.</summary>
    internal static byte[] Fvar(ushort axisCount = 1)
    {
        var bytes = new byte[16 + 20 * axisCount];
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(0), 0x00010000);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(4), 16);  // axesArrayOffset
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(6), 2);   // reserved
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(8), axisCount);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(10), 20); // axisSize
        // instanceCount/instanceSize stay zero
        for (int i = 0; i < axisCount; i++)
        {
            int at = 16 + 20 * i;
            BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(at), 0x77676874); // 'wght'
            BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(at + 4), 0);        // min 0.0 (Fixed)
            BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(at + 8), 0x00010000);  // default 1.0
            BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(at + 12), 0x00010000); // max 1.0
            // flags 0; axisNameID 256 (the hard floor)
            BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(at + 18), 256);
        }
        return bytes;
    }

    /// <summary>A valid gvar 1.0 whose every glyph carries the absent-block form
    /// (the offsets array is all zeros, short form): header plus offsets only.
    /// The compact rebuild re-splices the per-GID blocks; the retain path copies
    /// this table raw.</summary>
    internal static byte[] Gvar(ushort axisCount, ushort numGlyphs)
    {
        int sharedTuplesOffset = 20 + (numGlyphs + 1) * 2;
        var bytes = new byte[sharedTuplesOffset];
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(0), 0x00010000);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(4), axisCount);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(6), 0);   // sharedTupleCount
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(8), (uint)sharedTuplesOffset);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(12), numGlyphs);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(14), 0);  // flags: short form
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(16), (uint)sharedTuplesOffset);
        // the numGlyphs+1 short offsets stay zero: every block absent
        return bytes;
    }

    /// <summary>A valid HVAR 1.0 over one region axis and one VarData subtable whose
    /// item count covers the glyph domain (the NULL advance map's implicit domain),
    /// all three maps NULL. Region coordinates are all zero (a degenerate but legal
    /// region); the delta rows are zero bytes.</summary>
    internal static byte[] Hvar(ushort axisCount, ushort numGlyphs)
    {
        int ivsAt = 20;
        int regionListAt = ivsAt + 12;                         // IVS header 8 + one data offset 4
        int varDataAt = regionListAt + 4 + 6 * axisCount;      // region list: two counts + axes
        int varDataLength = 6 + 2 + numGlyphs;                 // header + one region index + rows
        var bytes = new byte[varDataAt + varDataLength];
        // HVAR header: version 1.0, store at 20, the three maps NULL.
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(0), 0x00010000);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(4), (uint)ivsAt);
        // IVS: format 1, region list, one VarData.
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(ivsAt), 1);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(ivsAt + 2), (uint)(regionListAt - ivsAt));
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(ivsAt + 6), 1);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(ivsAt + 8), (uint)(varDataAt - ivsAt));
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(regionListAt), axisCount);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(regionListAt + 2), 1); // regionCount
        // the axis triples stay zero (start = peak = end = 0)
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(varDataAt), numGlyphs);   // itemCount
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(varDataAt + 2), 0);       // wordDeltaCount
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(varDataAt + 4), 1);       // regionIndexCount
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(varDataAt + 6), 0);       // regionIndexes[0]
        // the delta rows stay zero bytes
        return bytes;
    }

    /// <summary>A cvt table with <paramref name="valueCount"/> zero entries (the
    /// cvar pairing partner's value-count authority).</summary>
    internal static byte[] Cvt(ushort valueCount) => new byte[2 * valueCount];

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
