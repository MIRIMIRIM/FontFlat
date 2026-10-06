using FontWeave.Core;

namespace FontWeave.Tables.Variation;

/// <summary>The semantic face of the HVAR DeltaSetIndexMap: the packed-entry decode
/// (entrySize, innerBits, the packed (outer, inner) split) is the card's hand-written
/// boundary. The format gate is native (format 0 only, declared as the version field);
/// the reserved 0xC0 entryFormat bits are refused here per spec ("Reserved for future
/// use - set to 0").</summary>
public readonly ref partial struct DeltaSetIndexMapView
{
    internal const byte EntrySizeMask = 0x30, InnerBitsMask = 0x0F, ReservedMask = 0xC0;

    /// <summary>Packed entry size in bytes: ((entryFormat & 0x30) >> 4) + 1, i.e. 1..4.</summary>
    internal static int PackedEntrySize(byte entryFormat) => ((entryFormat & EntrySizeMask) >> 4) + 1;

    /// <summary>Inner-level index bit count: (entryFormat & 0x0F) + 1, i.e. 1..16.</summary>
    internal static int InnerBitCount(byte entryFormat) => (entryFormat & InnerBitsMask) + 1;

    /// <summary>The strict structural pass: entry format value domain, then the packed
    /// entry array extent (entrySize * mapCount) against the window.</summary>
    public ReadStatus Validate(out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        var budget = new BuildBudget(limits);
        if (!budget.Input(_data.Length, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        return Validate(out diagnostic, budget);
    }

    internal ReadStatus Validate(out FontDiagnostic diagnostic, BuildBudget budget)
    {
        ReadStatus status = TryRequireKnownVersion(out _, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = TryGetEntryFormat(out byte entryFormat, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if ((entryFormat & ReservedMask) != 0)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.AtLocal(DataField.Flags, DeltaSetIndexMapLayout.EntryFormatAt),
                out diagnostic, (ulong)(entryFormat & ReservedMask), 0);
        status = TryGetMapCount(out ushort mapCount, out diagnostic);
        if (status != ReadStatus.Done) return status;
        long declared = DeltaSetIndexMapLayout.EntriesAt + (long)PackedEntrySize(entryFormat) * mapCount;
        if (!budget.Work(declared, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        if (declared > _data.Length)
            return ReadWire.Fail(ReadStatus.Truncated, DiagnosticCode.TruncatedData,
                Location.AtLocal(DataField.Count, DeltaSetIndexMapLayout.MapCountAt),
                out diagnostic, (ulong)_data.Length, (ulong)declared);
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>Typed materialization of every logical entry.</summary>
    public ReadStatus TryRead(out DeltaSetIndexMapTable? model, out FontDiagnostic diagnostic,
        BuildLimits? limits = null)
    {
        model = null;
        var budget = new BuildBudget(limits);
        if (!budget.Input(_data.Length, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        return TryRead(out model, out diagnostic, budget);
    }

    internal ReadStatus TryRead(out DeltaSetIndexMapTable? model, out FontDiagnostic diagnostic,
        BuildBudget budget)
    {
        model = null;
        ReadStatus status = Validate(out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        status = TryGetEntryFormat(out byte entryFormat, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = TryGetMapCount(out ushort mapCount, out diagnostic);
        if (status != ReadStatus.Done) return status;
        int entrySize = PackedEntrySize(entryFormat);
        int innerBits = InnerBitCount(entryFormat);
        if (!budget.Work((long)mapCount, Location, out diagnostic) ||
            !budget.Buffer(8L * mapCount, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        var entries = new DeltaSetIndex[mapCount];
        uint innerMask = innerBits >= 32 ? uint.MaxValue : (1u << innerBits) - 1;
        for (int i = 0; i < mapCount; i++)
        {
            int at = DeltaSetIndexMapLayout.EntriesAt + i * entrySize;
            uint packed = entrySize switch
            {
                1 => (uint)RawBytes[at],
                2 => (uint)global::System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(RawBytes.Slice(at)),
                3 => ReadUInt24(at),
                _ => global::System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(RawBytes.Slice(at)),
            };
            entries[i] = new((ushort)(packed >> innerBits), (ushort)(packed & innerMask));
        }
        model = new(entryFormat, entries, Location,
            DeltaSetIndexMapLayout.EntriesAt + entrySize * mapCount);
        diagnostic = default;
        return ReadStatus.Done;
    }

    private uint ReadUInt24(int at) =>
        (uint)RawBytes[at] << 16 | (uint)RawBytes[at + 1] << 8 | RawBytes[at + 2];
}
