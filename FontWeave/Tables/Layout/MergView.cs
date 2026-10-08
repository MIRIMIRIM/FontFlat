using FontWeave.Core;

namespace FontWeave.Tables.Layout;

/// <summary>One MergeEntry flag byte (card §2): the six defined bits and the two
/// reserved bits 0x08|0x80. The boolean projections are the spec's LTR/RTL visual-order
/// vocabulary; the reserved ridge is the OQ-1 hard face at the read chain (the spec's
/// imperative "set to 0" plus the T0 rejection list), so a constructed or read entry
/// with an occupied reserved bit refuses there - these projections only expose the
/// raw word's partitions. The orphaned-subordinate projections are the observation
/// face (the spec's "then it is ignored" - recorded, never refused and never treated
/// as active merging).</summary>
public readonly record struct MergeEntry(byte Flags)
{
    /// <summary>MERGE_LTR 0x01: merge in left-to-right visual order.</summary>
    public bool IsMergeLtr => (Flags & MergLayout.MergeLtrFlag) != 0;
    /// <summary>GROUP_LTR 0x02: group in left-to-right visual order.</summary>
    public bool IsGroupLtr => (Flags & MergLayout.GroupLtrFlag) != 0;
    /// <summary>SECOND_IS_SUBORDINATE_LTR 0x04.</summary>
    public bool IsSecondIsSubordinateLtr => (Flags & MergLayout.SecondIsSubordinateLtrFlag) != 0;
    /// <summary>MERGE_RTL 0x10: merge in right-to-left visual order.</summary>
    public bool IsMergeRtl => (Flags & MergLayout.MergeRtlFlag) != 0;
    /// <summary>GROUP_RTL 0x20: group in right-to-left visual order.</summary>
    public bool IsGroupRtl => (Flags & MergLayout.GroupRtlFlag) != 0;
    /// <summary>SECOND_IS_SUBORDINATE_RTL 0x40.</summary>
    public bool IsSecondIsSubordinateRtl => (Flags & MergLayout.SecondIsSubordinateRtlFlag) != 0;
    /// <summary>The reserved ridge 0x08|0x80 - zero on every legal wire (OQ-1 hard face).</summary>
    public byte ReservedBits => (byte)(Flags & MergLayout.ReservedFlagsMask);
    /// <summary>SecondIsSubordinate set with neither Merge nor Group in the same
    /// visual order: the spec's ignored form (the observation witness shape).</summary>
    public bool IsSubordinateLtrOrphan => IsSecondIsSubordinateLtr && !IsMergeLtr && !IsGroupLtr;
    /// <summary>The right-to-left orphan form of the same ignored shape.</summary>
    public bool IsSubordinateRtlOrphan => IsSecondIsSubordinateRtl && !IsMergeRtl && !IsGroupRtl;
}

/// <summary>The MERG read-only door (card §2): the generated accessors own the layout
/// (version gate, the two Required offsets, the classDefOffsets array over the raw
/// scalar start, the flat row-major merge matrix under the computed square count) and
/// the ClassDef reference openers; this partial adds only the semantics the wire's
/// two-level "rows × columns" organization needs - the two-dimensional accessor over
/// the flat array (row = first element's class, column = second's, flat position
/// row·mergeClassCount + column) with the OQ-3 lean shape. No child view exists for
/// the matrix (the count lives at the root and the matrix owns no count bytes) and no
/// MergClassDefView exists (the T4-01 frozen ClassDefView is the one formal ClassDef
/// implementation; AGENTS' single-View rule). The whole-table R/S/V, the hard
/// reserved-bit refusal and the cross-table strictly-increasing joint check are the
/// card's section 3 face and land with the read chain.</summary>
public readonly ref partial struct MergView
{
    /// <summary>Reads one merge entry by its (row, column) merge-class pair. The row
    /// is the first element's class and the column the second's (the spec's visual
    /// order note); both answer the mergeClassCount domain (InvalidOrdinal), the flat
    /// position is row·mergeClassCount + column (the row-major wire order), and the
    /// generated per-ordinal accessor owns the extent and truncation faces.</summary>
    public ReadStatus TryGetMergeEntry(int row, int column, out MergeEntry entry,
        out FontDiagnostic diagnostic)
    {
        entry = default;
        ReadStatus status = TryGetMergeClassCount(out ushort mergeClassCount, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if ((uint)row >= mergeClassCount)
            return InvalidOrdinal(row, mergeClassCount, out diagnostic);
        if ((uint)column >= mergeClassCount)
            return InvalidOrdinal(column, mergeClassCount, out diagnostic);
        status = TryGetMergeEntry(row * mergeClassCount + column,
            out var record, out diagnostic);
        if (status != ReadStatus.Done) return status;
        entry = new(record.Flags);
        diagnostic = default;
        return ReadStatus.Done;
    }

    private ReadStatus InvalidOrdinal(int ordinal, int mergeClassCount,
        out FontDiagnostic diagnostic) =>
        ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
            Location.AtLocal(DataField.Count, MergLayout.MergeClassCountAt, ordinal),
            out diagnostic, unchecked((ulong)(long)ordinal), (ulong)mergeClassCount);
}
