using System.Buffers.Binary;

namespace FontWeave;

/// <summary>An owned snapshot of one sfnt or a TTC 1.0/2.0 collection.</summary>
public sealed class FontFile
{
    private readonly FontFace[] _faces;

    private FontFile(FontFace[] faces, bool isCollection)
    {
        _faces = faces;
        IsCollection = isCollection;
    }

    public bool IsCollection { get; }
    public int FaceCount => _faces.Length;

    public FontFace GetFace(int index) => index >= 0 && index < _faces.Length
        ? _faces[index]
        : throw new FontDataException(FontError.InvalidIndex, $"Face index {index} is out of range.");

    public static FontFile Parse(ReadOnlySpan<byte> data, FontLimits? limits = null)
    {
        limits ??= new FontLimits();
        limits.Validate();
        if (data.Length > limits.MaxInputBytes)
            throw new FontDataException(FontError.ResourceLimit, "Input exceeds MaxInputBytes.");
        if (data.Length < 4)
            throw new FontDataException(FontError.Truncated, "The font signature is truncated.");

        uint signature = BinaryPrimitives.ReadUInt32BigEndian(data);
        if (signature is 0x774F4646 or 0x774F4632) // wOFF / wOF2
            throw new FontDataException(FontError.UnsupportedFormat, "WOFF containers are not supported in M1.");
        if (signature is not (0x74746366 or 0x00010000 or 0x4F54544F))
            throw new FontDataException(FontError.UnsupportedFormat, $"Unsupported font signature 0x{signature:X8}.");
        if (data.Length < 12)
            throw new FontDataException(FontError.Truncated, "The font container header is truncated.");
        if (signature != 0x74746366 && limits.MaxFaces < 1)
            throw new FontDataException(FontError.ResourceLimit, "A standalone font exceeds MaxFaces.");

        // No caller-owned memory is retained, including when parsing several TTC faces.
        byte[] snapshot = data.ToArray();
        ReadOnlySpan<byte> source = snapshot;
        if (signature != 0x74746366) // ttcf
            return new FontFile([ParseFace(snapshot, 0, limits, false, false)], false);

        RequireRange(source, 0, 12, "TTC header");
        uint version = U32(source, 4);
        if (version is not (0x00010000 or 0x00020000))
            throw new FontDataException(FontError.UnsupportedFormat, $"Unsupported TTC version 0x{version:X8}.");
        uint count = U32(source, 8);
        if (count == 0)
            throw new FontDataException(FontError.InvalidFormat, "TTC has no faces.");
        if (count > limits.MaxFaces)
            throw new FontDataException(FontError.ResourceLimit, "TTC face count exceeds MaxFaces.");
        ulong headerLength = 12UL + 4UL * count + (version == 0x00020000 ? 12UL : 0UL);
        RequireRange(source, 0, headerLength, "TTC header and face offsets");

        bool signedCollection = false;
        uint dsigOffset = 0;
        uint dsigLength = 0;
        if (version == 0x00020000)
        {
            int at = checked((int)(12 + 4 * count));
            uint dsigTag = U32(source, at);
            dsigLength = U32(source, at + 4);
            dsigOffset = U32(source, at + 8);
            if (dsigTag == 0 && dsigLength == 0 && dsigOffset == 0)
            {
                signedCollection = false;
            }
            else if (dsigTag == 0x44534947 && dsigLength != 0)
            {
                RequireRange(source, dsigOffset, dsigLength, "TTC DSIG");
                if (Overlaps(dsigOffset, dsigLength, 0, headerLength))
                    throw new FontDataException(FontError.InvalidOffset, "TTC DSIG overlaps the TTC header.");
                signedCollection = true;
            }
            else
            {
                throw new FontDataException(FontError.InvalidFormat, "Invalid TTC DSIG header fields.");
            }
        }

        var faces = new FontFace[checked((int)count)];
        var directoryOffsets = new uint[faces.Length];
        var directoryLengths = new ulong[faces.Length];
        // Establish all directory spans before parsing any table records. A later face
        // offset can point into an earlier directory and corrupt its apparent records.
        for (int i = 0; i < faces.Length; i++)
        {
            uint offset = U32(source, 12 + 4 * i);
            directoryOffsets[i] = offset;
            if (Overlaps(offset, 12, 0, headerLength))
                throw new FontDataException(FontError.InvalidOffset, "A TTC face directory overlaps the TTC header.");
            for (int j = 0; j < i; j++)
                if (offset != directoryOffsets[j] && Overlaps(offset, 12, directoryOffsets[j], directoryLengths[j]))
                    throw new FontDataException(FontError.InvalidOffset, "TTC face directories partially overlap.");
            RequireRange(source, offset, 12, "TTC face directory header");
            if (signedCollection && Overlaps(dsigOffset, dsigLength, offset, 12))
                throw new FontDataException(FontError.InvalidOffset, "TTC DSIG overlaps a face directory.");
            int at = checked((int)offset);
            uint sfntVersion = U32(source, at);
            if (sfntVersion is not (0x00010000 or 0x4F54544F))
                throw new FontDataException(FontError.UnsupportedFormat, $"Unsupported sfnt version 0x{sfntVersion:X8}.");
            int tables = U16(source, at + 4);
            if (tables == 0)
                throw new FontDataException(FontError.InvalidFormat, "A TTC face has no tables.");
            if (tables > limits.MaxTables)
                throw new FontDataException(FontError.ResourceLimit, "Table count exceeds MaxTables.");
            ulong directoryLength = 12UL + 16UL * (uint)tables;
            RequireRange(source, offset, directoryLength, "TTC face directory");
            directoryLengths[i] = directoryLength;
            if (signedCollection && Overlaps(dsigOffset, dsigLength, offset, directoryLength))
                throw new FontDataException(FontError.InvalidOffset, "TTC DSIG overlaps a face directory.");
            for (int j = 0; j < i; j++)
                if (Overlaps(offset, directoryLength, directoryOffsets[j], directoryLengths[j]) &&
                    (offset != directoryOffsets[j] || directoryLength != directoryLengths[j]))
                    throw new FontDataException(FontError.InvalidOffset, "TTC face directories partially overlap.");
        }
        for (int i = 0; i < faces.Length; i++)
            faces[i] = ParseFace(snapshot, directoryOffsets[i], limits, true, signedCollection);
        for (int i = 0; i < faces.Length; i++)
        {
            foreach (SfntTable table in faces[i].Tables)
            {
                if (table.Length == 0)
                    continue;
                if (Overlaps(table.Offset, table.Length, 0, headerLength))
                    throw new FontDataException(FontError.InvalidOffset, $"Table '{table.Tag}' overlaps the TTC header.");
                if (signedCollection && Overlaps(table.Offset, table.Length, dsigOffset, dsigLength))
                    throw new FontDataException(FontError.InvalidOffset, $"Table '{table.Tag}' overlaps the TTC DSIG payload.");
                for (int j = 0; j < faces.Length; j++)
                    if (Overlaps(table.Offset, table.Length, directoryOffsets[j], directoryLengths[j]))
                        throw new FontDataException(FontError.InvalidOffset, $"Table '{table.Tag}' overlaps a TTC face directory.");
            }
        }
        return new FontFile(faces, true);
    }

    private static FontFace ParseFace(byte[] snapshot, uint faceOffset, FontLimits limits, bool collection, bool signedCollection)
    {
        ReadOnlySpan<byte> source = snapshot;
        RequireRange(source, faceOffset, 12, "sfnt directory header");
        int start = checked((int)faceOffset);
        uint version = U32(source, start);
        if (version is not (0x00010000 or 0x4F54544F))
            throw new FontDataException(FontError.UnsupportedFormat, $"Unsupported sfnt version 0x{version:X8}.");
        int count = U16(source, start + 4);
        if (count == 0)
            throw new FontDataException(FontError.InvalidFormat, "An sfnt face has no tables.");
        if (count > limits.MaxTables)
            throw new FontDataException(FontError.ResourceLimit, "Table count exceeds MaxTables.");
        RequireRange(source, faceOffset, 12UL + 16UL * (uint)count, "sfnt table directory");

        var tables = new SfntTable[count];
        var tags = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < count; i++)
        {
            int at = start + 12 + 16 * i;
            ReadOnlySpan<byte> rawTag = source.Slice(at, 4);
            foreach (byte c in rawTag)
                if (c is < 0x20 or > 0x7E)
                    throw new FontDataException(FontError.InvalidFormat, "Table tag is not printable ASCII.");
            string tag = System.Text.Encoding.ASCII.GetString(rawTag);
            if (!tags.Add(tag))
                throw new FontDataException(FontError.DuplicateTable, $"Duplicate table tag '{tag}'.");
            uint checksum = U32(source, at + 4);
            uint offset = U32(source, at + 8);
            uint length = U32(source, at + 12);
            RequireRange(source, offset, length, $"Table '{tag}'");
            if (length != 0 && Overlaps(offset, length, faceOffset, 12UL + 16UL * (uint)count))
                throw new FontDataException(FontError.InvalidOffset, $"Table '{tag}' overlaps its sfnt directory.");
            tables[i] = new SfntTable(snapshot, tag, checksum, offset, length);
        }
        // Shared ranges across TTC faces are normal, but overlapping distinct tables
        // within one face cannot be repacked as independent opaque payloads safely.
        var occupied = tables.Where(t => t.Length != 0).OrderBy(t => t.Offset).ToArray();
        for (int i = 1; i < occupied.Length; i++)
            if ((ulong)occupied[i - 1].Offset + occupied[i - 1].Length > occupied[i].Offset)
                throw new FontDataException(FontError.InvalidOffset, "Distinct tables overlap within one face.");
        return new FontFace(version, tables, limits, collection, signedCollection);
    }

    private static bool Overlaps(ulong left, ulong leftLength, ulong right, ulong rightLength) =>
        left < right + rightLength && right < left + leftLength;

    private static void RequireRange(ReadOnlySpan<byte> data, ulong offset, ulong length, string description)
    {
        if (offset > (ulong)data.Length || length > (ulong)data.Length - offset)
            throw new FontDataException(FontError.InvalidOffset, $"{description} extends beyond the input.");
    }

    private static ushort U16(ReadOnlySpan<byte> data, int offset) => BinaryPrimitives.ReadUInt16BigEndian(data.Slice(offset, 2));
    private static uint U32(ReadOnlySpan<byte> data, int offset) => BinaryPrimitives.ReadUInt32BigEndian(data.Slice(offset, 4));
}

public sealed class FontFace
{
    private readonly IReadOnlyList<SfntTable> _tables;
    private readonly Dictionary<string, SfntTable> _byTag;

    internal FontFace(uint sfntVersion, SfntTable[] tables, FontLimits limits, bool collection, bool signedCollection)
    {
        SfntVersion = sfntVersion;
        _tables = Array.AsReadOnly(tables);
        _byTag = tables.ToDictionary(t => t.Tag, StringComparer.Ordinal);
        Limits = limits;
        IsFromCollection = collection;
        HasCollectionSignature = signedCollection;
    }

    public uint SfntVersion { get; }
    public IReadOnlyList<SfntTable> Tables => _tables;
    internal FontLimits Limits { get; }
    internal bool IsFromCollection { get; }
    internal bool HasCollectionSignature { get; }

    public SfntTable GetTable(string tag) => _byTag.TryGetValue(tag, out var table)
        ? table
        : throw new FontDataException(FontError.MissingTable, $"Table '{tag}' is missing.");

    public bool TryGetTable(string tag, out SfntTable? table) => _byTag.TryGetValue(tag, out table);
}

public sealed class SfntTable
{
    private readonly byte[] _snapshot;

    internal SfntTable(byte[] snapshot, string tag, uint checksum, uint offset, uint length)
    {
        _snapshot = snapshot;
        Tag = tag;
        Checksum = checksum;
        Offset = offset;
        Length = length;
    }

    public string Tag { get; }
    public uint Checksum { get; }
    public uint Offset { get; }
    public uint Length { get; }
    public ReadOnlySpan<byte> Data => _snapshot.AsSpan(checked((int)Offset), checked((int)Length));
}
