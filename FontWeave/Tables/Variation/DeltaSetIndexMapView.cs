using FontWeave.Core;

namespace FontWeave.Tables.Variation;

/// <summary>The semantic face of the shared DeltaSetIndexMap: the packed-entry decode
/// (entrySize, innerBits, the packed (outer, inner) split) is the card's hand-written
/// boundary. The format gate is native (formats 0 and 1 declared as the version
/// domain, T6-03 §2; format 3+ refuses natively); the reserved 0xC0 entryFormat bits
/// are refused here per spec ("Reserved for future use - set to 0"). The count word
/// is width-dispatched by format (u16 on format 0, u32 on format 1 - the declared
/// WhenVersion pair); the logical accessor below answers both.</summary>
public readonly ref partial struct DeltaSetIndexMapView
{
    internal const byte EntrySizeMask = 0x30, InnerBitsMask = 0x0F, ReservedMask = 0xC0;

    /// <summary>Packed entry size in bytes: ((entryFormat & 0x30) >> 4) + 1, i.e. 1..4.</summary>
    internal static int PackedEntrySize(byte entryFormat) => ((entryFormat & EntrySizeMask) >> 4) + 1;

    /// <summary>The packed-entry array's base: right behind the u16 count on format 0,
    /// behind the u32 count on format 1 (the wide count pushes the entries two bytes
    /// later - the declared floor of 4 stays the format-0 face).</summary>
    internal static int EntriesBase(byte format) =>
        format == 0 ? DeltaSetIndexMapLayout.EntriesAt : DeltaSetIndexMapLayout.Format1EntriesAt;

    /// <summary>Inner-level index bit count: (entryFormat & 0x0F) + 1, i.e. 1..16.</summary>
    internal static int InnerBitCount(byte entryFormat) => (entryFormat & InnerBitsMask) + 1;

    /// <summary>The count word in its format's width: u16 at 2 on format 0, u32 at 2
    /// on format 1 (the declared same-offset double-width pair).</summary>
    internal ReadStatus TryGetLogicalMapCount(out uint mapCount, out FontDiagnostic diagnostic)
    {
        ReadStatus status = TryRequireKnownVersion(out ulong version, out diagnostic);
        if (status != ReadStatus.Done)
        {
            mapCount = 0;
            return status;
        }
        if (version == 0)
        {
            status = TryGetMapCount(out ushort narrow, out diagnostic);
            mapCount = narrow;
            return status;
        }
        return TryGetMapCountWide(out mapCount, out diagnostic);
    }

    /// <summary>The HVAR/VVAR domain's format-0 re-anchor (OQ-T6-03-4): the version
    /// gate now admits format 1 for the COLR-era shared face, so the two metrics
    /// consumers check the format explicitly at their map open sites, reproducing
    /// the version gate's exact diagnostic shape (UnsupportedVersion@Version, the
    /// raw format in Detail, zero Expected).</summary>
    internal ReadStatus RequireFormatZero(out FontDiagnostic diagnostic)
    {
        byte format = Format;
        if (format != 0)
            return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedVersion,
                Location.AtLocal(DataField.Version, DeltaSetIndexMapLayout.FormatAt),
                out diagnostic, format, 0UL);
        diagnostic = default;
        return ReadStatus.Done;
    }

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
        ReadStatus status = TryRequireKnownVersion(out ulong version, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = TryGetEntryFormat(out byte entryFormat, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if ((entryFormat & ReservedMask) != 0)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.AtLocal(DataField.Flags, DeltaSetIndexMapLayout.EntryFormatAt),
                out diagnostic, (ulong)(entryFormat & ReservedMask), 0);
        status = TryGetLogicalMapCount(out uint mapCount, out diagnostic);
        if (status != ReadStatus.Done) return status;
        long declared = (long)EntriesBase((byte)version) + (long)PackedEntrySize(entryFormat) * mapCount;
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
        status = TryRequireKnownVersion(out ulong version, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = TryGetEntryFormat(out byte entryFormat, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = TryGetLogicalMapCount(out uint mapCount, out diagnostic);
        if (status != ReadStatus.Done) return status;
        int entrySize = PackedEntrySize(entryFormat);
        int innerBits = InnerBitCount(entryFormat);
        int entriesAt = EntriesBase((byte)version);
        if (!budget.Work((long)mapCount, Location, out diagnostic) ||
            !budget.Buffer(8L * mapCount, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        var entries = new DeltaSetIndex[(int)mapCount];
        uint innerMask = innerBits >= 32 ? uint.MaxValue : (1u << innerBits) - 1;
        for (int i = 0; i < (int)mapCount; i++)
        {
            int at = entriesAt + i * entrySize;
            uint packed = entrySize switch
            {
                1 => (uint)RawBytes[at],
                2 => (uint)global::System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(RawBytes.Slice(at)),
                3 => ReadUInt24(at),
                _ => global::System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(RawBytes.Slice(at)),
            };
            entries[i] = new((ushort)(packed >> innerBits), (ushort)(packed & innerMask));
        }
        model = new((byte)version, entryFormat, entries, Location,
            entriesAt + entrySize * (int)mapCount);
        diagnostic = default;
        return ReadStatus.Done;
    }

    private uint ReadUInt24(int at) =>
        (uint)RawBytes[at] << 16 | (uint)RawBytes[at + 1] << 8 | RawBytes[at + 2];
}
