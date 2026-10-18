// Synthetic TTC 1.0 fixtures for the CLI collection-face tests.
//
// Assembled from raw wire bytes directly (the shared-fixture convention: never written
// by the product), so a test feeds the CLI an independent expectation. The layout keeps
// every constraint FontFile.Parse and SfntFaces.TryGetFace enforce visible: both face
// directories sit after the TTC header, and all table payloads sit after every
// directory, so no table can overlap any face directory.
using System.Buffers.Binary;
using System.Text;

namespace FontWeave.Tests;

internal static class CliTtcFixture
{
    internal static byte[] TwoFaceTtc() => Ttc(
        new[] { (HeadTag, Head()), (MaxpTag, Maxp()), (NameTag, Name(FaceZeroNameRecords)) },
        new[] { (HeadTag, Head()), (MaxpTag, Maxp()), (NameTag, Name(FaceOneNameRecords)),
            (VorgTag, Dummy(8)) });

    internal static readonly (ushort Platform, ushort Encoding, ushort Language,
        ushort NameId, string Text)[] FaceZeroNameRecords =
        [(3, 1, 0x409, 1, "TTC Face Zero"), (3, 1, 0x409, 2, "Regular")];

    internal static readonly (ushort Platform, ushort Encoding, ushort Language,
        ushort NameId, string Text)[] FaceOneNameRecords =
        [(3, 1, 0x409, 1, "TTC Face One"), (3, 1, 0x409, 2, "Bold")];

    internal const uint HeadTag = 0x68656164, MaxpTag = 0x6D617870, NameTag = 0x6E616D65,
        VorgTag = 0x564F5247;

    /// <summary>Assembles a TTC 1.0 container over the given per-face tables. Tags are
    /// recorded in ascending order and payloads are 4-byte aligned; every table offset
    /// is absolute, so face images are rebased into the shared data pool after all
    /// directories.</summary>
    internal static byte[] Ttc(params (uint Tag, byte[] Bytes)[][] faces)
    {
        var faceOffsets = new uint[faces.Length];
        uint cursor = (uint)(12 + 4 * faces.Length);
        for (int i = 0; i < faces.Length; i++)
        {
            faceOffsets[i] = cursor;
            cursor += (uint)(12 + 16 * faces[i].Length);
        }
        long payload = faces.Sum(face => face.Sum(table => (table.Bytes.Length + 3L) & ~3L));
        var bytes = new byte[checked((int)(cursor + payload))];
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(0), 0x74746366);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(4), 0x00010000);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(8), (uint)faces.Length);
        for (int i = 0; i < faces.Length; i++)
            BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(12 + 4 * i), faceOffsets[i]);
        uint dataCursor = cursor;
        for (int i = 0; i < faces.Length; i++)
        {
            var tables = faces[i].OrderBy(table => table.Tag).ToArray();
            int at = (int)faceOffsets[i];
            BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(at), 0x00010000);
            BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(at + 4), (ushort)tables.Length);
            int searchRange = 16, selector = 0;
            while (searchRange <= tables.Length * 8) { searchRange *= 2; selector++; }
            BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(at + 6), (ushort)searchRange);
            BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(at + 8), (ushort)selector);
            BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(at + 10),
                (ushort)(16 * tables.Length - searchRange));
            for (int j = 0; j < tables.Length; j++)
            {
                int record = at + 12 + 16 * j;
                BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(record), tables[j].Tag);
                BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(record + 8), dataCursor);
                BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(record + 12),
                    (uint)tables[j].Bytes.Length);
                tables[j].Bytes.CopyTo(bytes.AsSpan((int)dataCursor));
                dataCursor += (uint)((tables[j].Bytes.Length + 3) & ~3);
            }
        }
        return bytes;
    }

    /// <summary>A format-0 name table over the given records. Records must arrive in
    /// ascending (platform, encoding, language, nameId) order: the editor chain refuses
    /// unsorted sources and the fixture keeps that contract visible instead of hiding
    /// a sort here.</summary>
    internal static byte[] Name(params (ushort Platform, ushort Encoding, ushort Language,
        ushort NameId, string Text)[] records)
    {
        byte[][] payloads = records.Select(record =>
            Encoding.BigEndianUnicode.GetBytes(record.Text)).ToArray();
        int headerSize = 6 + 12 * records.Length;
        var bytes = new byte[headerSize + payloads.Sum(payload => payload.Length)];
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(0), 0); // format 0
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(2), (ushort)records.Length);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(4), (ushort)headerSize);
        int storage = headerSize;
        for (int i = 0; i < records.Length; i++)
        {
            int at = 6 + 12 * i;
            BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(at), records[i].Platform);
            BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(at + 2), records[i].Encoding);
            BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(at + 4), records[i].Language);
            BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(at + 6), records[i].NameId);
            BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(at + 8), (ushort)payloads[i].Length);
            BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(at + 10), (ushort)(storage - headerSize));
            payloads[i].CopyTo(bytes.AsSpan(storage));
            storage += payloads[i].Length;
        }
        return bytes;
    }

    internal static byte[] Head()
    {
        var bytes = new byte[54];
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(0), 0x00010000);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(18), 1000);
        return bytes;
    }

    internal static byte[] Maxp()
    {
        var bytes = new byte[6];
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(0), 0x00005000);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(4), 4);
        return bytes;
    }

    internal static byte[] Dummy(int length) => new byte[length];

    // ---- strictly validatable table builders (validate/subset TTC faces) ----------

    /// <summary>A head table that passes the strict HeadView domain: 54 bytes exactly,
    /// the required magic number, unitsPerEm 1000, indexToLocFormat in domain.</summary>
    internal static byte[] ValidHead(short indexToLocFormat = 0)
    {
        var bytes = new byte[54];
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(0), 0x00010000);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(12), 0x5F0F3CF5);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(18), 1000);
        BinaryPrimitives.WriteInt16BigEndian(bytes.AsSpan(50), indexToLocFormat);
        return bytes;
    }

    /// <summary>A VORG 1.0 header with zero records: the strict validator's exact
    /// expected length, count within any maxp domain.</summary>
    internal static byte[] ValidVorg()
    {
        var bytes = new byte[8];
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(0), 0x00010000);
        return bytes;
    }

    /// <summary>The two-face container whose every table passes its official strict
    /// validator, so validate --face N and the whole-container aggregate succeed.</summary>
    internal static byte[] ValidatableTwoFaceTtc() => Ttc(
        new[] { (HeadTag, ValidHead()), (MaxpTag, Maxp()), (NameTag, Name(FaceZeroNameRecords)) },
        new[] { (HeadTag, ValidHead()), (MaxpTag, Maxp()), (NameTag, Name(FaceOneNameRecords)),
            (VorgTag, ValidVorg()) });
}
