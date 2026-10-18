using System.Buffers.Binary;
using FontWeave.Core;

namespace FontWeave.Containers;

/// <summary>Container-only writer over already parsed faces: each face re-emits through
/// SfntEmitter as a standalone image, and the images concatenate under one TTC 1.0 header
/// whose face directories carry container-absolute table offsets. Every face's tables land
/// independently — identical-bytes sharing across faces is a later optimization that does
/// not change the container's legality.</summary>
public static class SfntCollectionWriter
{
    public static ReadStatus TryPrepare(IReadOnlyList<FontFace> faces,
        out FontCollectionWritePlan? plan, out FontDiagnostic diagnostic,
        BuildLimits? limits = null)
    {
        plan = null;
        diagnostic = default;
        ArgumentNullException.ThrowIfNull(faces);
        if (faces.Count == 0)
            throw new ArgumentException("At least one face is required.", nameof(faces));
        var budget = new BuildBudget(limits);
        var images = new byte[faces.Count][];
        var faceTableCounts = new int[faces.Count];
        long containerLength = 12L + 4L * faces.Count;
        for (int i = 0; i < faces.Count; i++)
        {
            FontFace face = faces[i] ?? throw new ArgumentNullException(nameof(faces),
                $"Face {i} is null.");
            var location = new DataLocation(0, i, 0, -1, DataField.Signature,
                OffsetBasis.File, 0);
            if (!budget.Work(12L + 16L * face.Tables.Count, location, out diagnostic))
                return ReadStatus.ResourceLimit;
            var tables = new SortedDictionary<uint, byte[]>();
            foreach (SfntTable table in face.Tables)
            {
                if (!budget.Buffer(table.Length, location, out diagnostic) ||
                    !budget.Work(table.Length, location, out diagnostic))
                    return ReadStatus.ResourceLimit;
                tables.Add(TagWord(table.Tag), table.Data.ToArray());
            }
            faceTableCounts[i] = tables.Count;
            ReadStatus status = SfntEmitter.TryEmit(face.SfntVersion, location, tables,
                out byte[]? image, out diagnostic, budget);
            if (status != ReadStatus.Done) return status;
            images[i] = image!;
            containerLength += image!.Length;
        }
        if (containerLength > int.MaxValue)
            return ReadWire.Fail(ReadStatus.ResourceLimit, DiagnosticCode.BudgetExceeded,
                new DataLocation(0, 0, 0, -1, DataField.OutputBytes, OffsetBasis.File, 0),
                out diagnostic, (ulong)containerLength, int.MaxValue);
        if (!budget.Output(containerLength, new DataLocation(0, 0, 0, -1, DataField.OutputBytes,
                OffsetBasis.File, 0), out diagnostic) ||
            !budget.Work(containerLength, new DataLocation(0, 0, 0, -1, DataField.OutputBytes,
                OffsetBasis.File, 0), out diagnostic))
            return ReadStatus.ResourceLimit;

        byte[] output = new byte[checked((int)containerLength)]; // zero-initialized padding
        BinaryPrimitives.WriteUInt32BigEndian(output.AsSpan(0), 0x74746366);
        BinaryPrimitives.WriteUInt32BigEndian(output.AsSpan(4), 0x00010000);
        BinaryPrimitives.WriteUInt32BigEndian(output.AsSpan(8), (uint)faces.Count);
        long faceBase = 12L + 4L * faces.Count;
        for (int i = 0; i < faces.Count; i++)
        {
            int at = checked((int)faceBase);
            BinaryPrimitives.WriteUInt32BigEndian(output.AsSpan(12 + 4 * i), (uint)at);
            images[i].CopyTo(output.AsSpan(at));
            RebaseAndResign(output, at, images[i].Length);
            faceBase += images[i].Length;
        }
        plan = new FontCollectionWritePlan(output, budget.Usage, faceTableCounts);
        diagnostic = default;
        return ReadStatus.Done;
    }

    // Each standalone image declared its table offsets relative to its own start; inside
    // the container they are absolute, so the rebase adds the face's base to every record.
    // The rebase rewrites directory bytes, so head.checkSumAdjustment is resealed over the
    // rebased face span afterwards.
    private static void RebaseAndResign(Span<byte> container, int faceBase, int imageLength)
    {
        ushort count = BinaryPrimitives.ReadUInt16BigEndian(container.Slice(faceBase + 4, 2));
        int headPayload = -1;
        for (int i = 0; i < count; i++)
        {
            int record = faceBase + 12 + 16 * i;
            uint rebased = unchecked((uint)faceBase +
                BinaryPrimitives.ReadUInt32BigEndian(container.Slice(record + 8, 4)));
            BinaryPrimitives.WriteUInt32BigEndian(container.Slice(record + 8, 4), rebased);
            if (BinaryPrimitives.ReadUInt32BigEndian(container.Slice(record, 4)) == 0x68656164)
                headPayload = checked((int)rebased);
        }
        container.Slice(headPayload + 8, 4).Clear();
        uint adjustment = unchecked(0xB1B0AFBAu - Checksum(container.Slice(faceBase, imageLength)));
        BinaryPrimitives.WriteUInt32BigEndian(container.Slice(headPayload + 8, 4), adjustment);
    }

    private static uint TagWord(string tag) =>
        (uint)tag[0] << 24 | (uint)tag[1] << 16 | (uint)tag[2] << 8 | tag[3];

    private static uint Checksum(ReadOnlySpan<byte> bytes)
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
}

public sealed class FontCollectionWritePlan
{
    private readonly byte[] _output;

    internal FontCollectionWritePlan(byte[] output, BuildUsage usage, int[] faceTableCounts)
    {
        _output = output;
        Usage = usage;
        FaceTableCounts = Array.AsReadOnly(faceTableCounts);
    }

    public int OutputLength => _output.Length;
    public BuildUsage Usage { get; }
    /// <summary>Per-face emitted table counts, in the requested face order.</summary>
    public IReadOnlyList<int> FaceTableCounts { get; }

    public byte[] ToArray() => (byte[])_output.Clone();

    public void CopyTo(Span<byte> destination)
    {
        if (destination.Length < _output.Length)
            throw new ArgumentException("Destination is shorter than the planned output.", nameof(destination));
        _output.AsSpan().CopyTo(destination);
    }
}
