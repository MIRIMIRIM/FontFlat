using System.Buffers.Binary;

namespace FontWeave;

public enum UnknownTablePolicy { Fail, Drop, PreserveExplicit }

public sealed record FontWriteOptions
{
    public bool DropSignature { get; init; }
    public UnknownTablePolicy UnknownTablePolicy { get; init; } = global::FontWeave.UnknownTablePolicy.Fail;
}

/// <summary>Repackages one face as a standalone sfnt; it does not sanitize table internals.</summary>
public static class SfntWriter
{
    // OpenType-defined tables whose unchanged payloads can be moved as opaque top-level data
    // for a name-only edit. This does not establish their semantic validity or name consistency.
    private static readonly HashSet<string> StandardTags = new(StringComparer.Ordinal)
    {
        "BASE", "CBDT", "CBLC", "CFF ", "CFF2", "COLR", "CPAL", "EBDT", "EBLC", "EBSC",
        "GDEF", "GPOS", "GSUB", "HVAR", "JSTF", "LTSH", "MATH", "MERG", "MVAR", "OS/2",
        "PCLT", "STAT", "SVG ", "VDMX", "VORG", "VVAR", "avar", "cmap", "cvar", "cvt ",
        "fpgm", "fvar", "gasp", "glyf", "gvar", "hdmx", "head", "hhea", "hmtx", "kern",
        "loca", "maxp", "meta", "name", "post", "prep", "sbix", "vhea", "vmtx"
    };

    public static FontWritePlan Prepare(FontFace face, ReadOnlySpan<byte> newName, FontWriteOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(face);
        options ??= new FontWriteOptions();
        if (!Enum.IsDefined(options.UnknownTablePolicy))
            throw new ArgumentOutOfRangeException(nameof(options), "Invalid unknown-table policy.");
        if (newName.Length > face.Limits.MaxNameBytes)
            throw new FontDataException(FontError.ResourceLimit, "New name table exceeds MaxNameBytes.");
        if (newName.IsEmpty)
            throw new FontDataException(FontError.InvalidName, "The replacement name table is empty.");

        var head = face.GetTable("head");
        face.GetTable("name");
        if (head.Length < 54)
            throw new FontDataException(FontError.InvalidFormat, "The head table is shorter than its 54-byte structure.");

        bool hasDsigTable = face.TryGetTable("DSIG", out _);
        bool signed = face.HasCollectionSignature || hasDsigTable;
        if (signed && !options.DropSignature)
            throw new FontDataException(FontError.SignedFont, "Signed input requires explicit DropSignature to rewrite.");

        var diagnostics = new List<string>();
        if (face.HasCollectionSignature)
            diagnostics.Add("Dropped TTC collection signature; standalone output is unsigned.");
        if (hasDsigTable)
            diagnostics.Add("Dropped DSIG table; output is unsigned.");
        var selected = new List<(string Tag, byte[] Data)>();
        var preserved = new List<string>();
        long outputLength = 12;
        foreach (SfntTable table in face.Tables)
        {
            if (table.Tag == "DSIG")
                continue;
            if (table.Tag == "name")
            {
                outputLength += 16L + ((newName.Length + 3L) & ~3L);
                CheckOutputBudget(outputLength, face.Limits);
                selected.Add((table.Tag, newName.ToArray()));
                continue;
            }
            if (!StandardTags.Contains(table.Tag))
            {
                if (options.UnknownTablePolicy == UnknownTablePolicy.Fail)
                    throw new FontDataException(FontError.UnknownTable, $"Unknown table '{table.Tag}' requires an explicit policy.");
                if (options.UnknownTablePolicy == UnknownTablePolicy.Drop)
                {
                    diagnostics.Add($"Dropped unknown table '{table.Tag}'.");
                    continue;
                }
                diagnostics.Add($"Explicitly preserved unknown table '{table.Tag}' as opaque bytes; semantic safety is unverified.");
            }
            outputLength += 16L + ((table.Length + 3L) & ~3L);
            CheckOutputBudget(outputLength, face.Limits);
            if (Checksum(table.Data, table.Tag == "head") != table.Checksum)
                diagnostics.Add($"Source checksum mismatch for '{table.Tag}'; output checksum was recalculated.");
            selected.Add((table.Tag, table.Data.ToArray()));
            if (table.Tag != "head")
                preserved.Add(table.Tag);
        }
        diagnostics.Add("Other standard table payloads were copied opaquely (except head.checkSumAdjustment); internals and name references were not validated.");

        selected.Sort((a, b) => StringComparer.Ordinal.Compare(a.Tag, b.Tag));
        int count = selected.Count;
        if (count > face.Limits.MaxTables)
            throw new FontDataException(FontError.ResourceLimit, "Output table count exceeds MaxTables.");
        if (count > 4095)
            throw new FontDataException(FontError.Unrepresentable, "Output sfnt search fields cannot represent this table count.");

        byte[] output = new byte[checked((int)outputLength)]; // zero-initialized padding
        Span<byte> destination = output;
        W32(destination, 0, face.SfntVersion);
        W16(destination, 4, checked((ushort)count));
        int entrySelector = 0;
        int searchRange = 16;
        while (searchRange <= count * 8)
        {
            searchRange *= 2;
            entrySelector++;
        }
        W16(destination, 6, checked((ushort)searchRange));
        W16(destination, 8, checked((ushort)entrySelector));
        W16(destination, 10, checked((ushort)(16 * count - searchRange)));

        int cursor = 12 + 16 * count;
        int headOffset = -1;
        for (int i = 0; i < count; i++)
        {
            var item = selected[i];
            int directory = 12 + 16 * i;
            for (int j = 0; j < 4; j++)
                destination[directory + j] = checked((byte)item.Tag[j]);
            item.Data.AsSpan().CopyTo(destination.Slice(cursor, item.Data.Length));
            if (item.Tag == "head")
            {
                headOffset = cursor;
                destination.Slice(cursor + 8, 4).Clear();
            }
            W32(destination, directory + 4, Checksum(destination.Slice(cursor, item.Data.Length)));
            W32(destination, directory + 8, checked((uint)cursor));
            W32(destination, directory + 12, checked((uint)item.Data.Length));
            cursor += (item.Data.Length + 3) & ~3;
        }
        W32(destination, headOffset + 8, unchecked(0xB1B0AFBAu - Checksum(destination)));
        return new FontWritePlan(output, diagnostics, preserved);
    }

    private static uint Checksum(ReadOnlySpan<byte> data, bool zeroHeadAdjustment = false)
    {
        uint sum = 0;
        int i = 0;
        for (; i + 4 <= data.Length; i += 4)
            sum = unchecked(sum + (zeroHeadAdjustment && i == 8 ? 0 : BinaryPrimitives.ReadUInt32BigEndian(data.Slice(i, 4))));
        if (i < data.Length)
        {
            uint last = 0;
            for (int j = 0; i + j < data.Length; j++)
                last |= (uint)data[i + j] << (24 - 8 * j);
            sum = unchecked(sum + last);
        }
        return sum;
    }

    private static void CheckOutputBudget(long length, FontLimits limits)
    {
        if (length > limits.MaxOutputBytes || length > int.MaxValue)
            throw new FontDataException(FontError.ResourceLimit, "Planned sfnt exceeds MaxOutputBytes.");
    }

    private static void W16(Span<byte> data, int offset, ushort value) => BinaryPrimitives.WriteUInt16BigEndian(data.Slice(offset, 2), value);
    private static void W32(Span<byte> data, int offset, uint value) => BinaryPrimitives.WriteUInt32BigEndian(data.Slice(offset, 4), value);
}

public sealed class FontWritePlan
{
    private readonly byte[] _output;

    internal FontWritePlan(byte[] output, List<string> diagnostics, List<string> preserved)
    {
        _output = output;
        Diagnostics = Array.AsReadOnly(diagnostics.ToArray());
        PreservedTables = Array.AsReadOnly(preserved.ToArray());
    }

    public int OutputLength => _output.Length;
    public IReadOnlyList<string> Diagnostics { get; }
    public IReadOnlyList<string> PreservedTables { get; }
    public byte[] ToArray() => (byte[])_output.Clone();

    public void CopyTo(Span<byte> destination)
    {
        if (destination.Length < _output.Length)
            throw new ArgumentException("Destination is shorter than the planned output.", nameof(destination));
        _output.AsSpan().CopyTo(destination);
    }
}
