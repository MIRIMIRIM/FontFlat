using OTFontFile2.SourceGen;

namespace OTFontFile2.Tables;

/// <summary>
/// Mutable builder for the OpenType <c>DSIG</c> table.
/// </summary>
[OtTableBuilder("DSIG")]
public sealed partial class DsigTableBuilder : ISfntTableSource
{
    private readonly List<SignatureEntry> _signatures = new();

    private uint _version = 1;
    private ushort _flags;

    public uint Version
    {
        get => _version;
        set
        {
            if (value == _version)
                return;

            _version = value;
            MarkDirty();
        }
    }

    public ushort Flags
    {
        get => _flags;
        set
        {
            if (value == _flags)
                return;

            _flags = value;
            MarkDirty();
        }
    }

    public int SignatureCount => _signatures.Count;

    public IReadOnlyList<SignatureEntry> Signatures => _signatures;

    public void Clear()
    {
        _signatures.Clear();
        MarkDirty();
    }

    public void AddSignature(uint format, ushort reserved1, ushort reserved2, ReadOnlyMemory<byte> signature)
    {
        _signatures.Add(new SignatureEntry(format, reserved1, reserved2, signature));
        MarkDirty();
    }

    public bool RemoveAt(int index)
    {
        if ((uint)index >= (uint)_signatures.Count)
            return false;

        _signatures.RemoveAt(index);
        MarkDirty();
        return true;
    }

    public static bool TryFrom(DsigTable dsig, out DsigTableBuilder builder)
    {
        builder = null!;

        var b = new DsigTableBuilder
        {
            Version = dsig.Version,
            Flags = dsig.Flags
        };

        int count = dsig.SignatureCount;
        for (int i = 0; i < count; i++)
        {
            if (!dsig.TryGetSignatureRecord(i, out var record))
                continue;

            if (!dsig.TryGetSignatureBlock(i, out var block))
                continue;

            if (!block.TryGetSignatureSpan(out var sigBytes))
                continue;

            b._signatures.Add(new SignatureEntry(
                format: record.Format,
                reserved1: block.Reserved1,
                reserved2: block.Reserved2,
                signature: sigBytes.ToArray()));
        }

        b.MarkDirty();
        builder = b;
        return true;
    }

    private byte[] BuildTable()
    {
        if (_signatures.Count > ushort.MaxValue)
            throw new InvalidOperationException("DSIG signature count must fit in uint16.");

        int count = _signatures.Count;

        int headerSize = checked(8 + (count * 12));
        int dataPos = headerSize;

        var offsets = new uint[count];
        var lengths = new uint[count];

        for (int i = 0; i < count; i++)
        {
            var entry = _signatures[i];

            int sigLen = entry.Signature.Length;
            if (sigLen < 0)
                throw new InvalidOperationException("Negative signature length.");

            int blockLen = checked(8 + sigLen);
            offsets[i] = checked((uint)dataPos);
            lengths[i] = checked((uint)blockLen);

            dataPos = checked(dataPos + blockLen);
            if (i != count - 1)
                dataPos = Pad4(dataPos);
        }

        byte[] table = new byte[dataPos];
        var span = table.AsSpan();

        BigEndian.WriteUInt32(span, 0, Version);
        BigEndian.WriteUInt16(span, 4, (ushort)count);
        BigEndian.WriteUInt16(span, 6, Flags);

        int recordOffset = 8;
        for (int i = 0; i < count; i++)
        {
            var entry = _signatures[i];
            BigEndian.WriteUInt32(span, recordOffset + 0, entry.Format);
            BigEndian.WriteUInt32(span, recordOffset + 4, lengths[i]);
            BigEndian.WriteUInt32(span, recordOffset + 8, offsets[i]);
            recordOffset += 12;
        }

        for (int i = 0; i < count; i++)
        {
            var entry = _signatures[i];
            int offset = checked((int)offsets[i]);

            BigEndian.WriteUInt16(span, offset + 0, entry.Reserved1);
            BigEndian.WriteUInt16(span, offset + 2, entry.Reserved2);
            BigEndian.WriteUInt32(span, offset + 4, checked((uint)entry.Signature.Length));

            entry.Signature.Span.CopyTo(span.Slice(offset + 8, entry.Signature.Length));
        }

        return table;
    }

    private static int Pad4(int length) => (length + 3) & ~3;

    public readonly struct SignatureEntry
    {
        public uint Format { get; }
        public ushort Reserved1 { get; }
        public ushort Reserved2 { get; }
        public ReadOnlyMemory<byte> Signature { get; }

        public SignatureEntry(uint format, ushort reserved1, ushort reserved2, ReadOnlyMemory<byte> signature)
        {
            Format = format;
            Reserved1 = reserved1;
            Reserved2 = reserved2;
            Signature = signature;
        }
    }
}
