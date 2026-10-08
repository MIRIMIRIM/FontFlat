using FontWeave.Core;

namespace FontWeave.Tables.Layout;

/// <summary>Owned whole-MERG slice (version 0, card §3/§4): the raw header words, the
/// raw classDefOffsets slot values, the classDef models with shared-offset identity (a
/// wire position materializes once - two slots aimed at one body carry the same
/// <see cref="ClassDefTable"/> instance, the GDEF dual-offset precedent) over the
/// distinct first-appearance list, the row-major matrix bytes and the consumed
/// accounting over the whole walk (the BASE R9/JSTF R convention: the max end over
/// the header, the offsets array, every distinct classDef body and the matrix).
/// numGlyphs is the explicit authority input every ClassDef GID domain was answered
/// with. The open class domain is the spec's own reading (class ≥ mergeClassCount is
/// the legal "never required to merge" form, so no class-limit check exists here) and
/// merge application (the spec's Processing section) stays outside this model (the T0
/// Operations ban).</summary>
public sealed class MergTable
{
    private readonly byte[] _matrix;
    private readonly ClassDefTable[] _slots;
    private readonly ClassDefTable[] _distinct;
    private readonly ushort[] _classDefOffsets;

    internal MergTable(ushort version, int numGlyphs, ushort mergeClassCount,
        ushort mergeDataOffset, ushort classDefCount, ushort offsetToClassDefOffsets,
        ushort[] classDefOffsets, ClassDefTable[] slots, byte[] matrix,
        DataLocation location, int consumedLength)
    {
        Version = version;
        NumGlyphs = numGlyphs;
        MergeClassCount = mergeClassCount;
        MergeDataOffset = mergeDataOffset;
        ClassDefCount = classDefCount;
        OffsetToClassDefOffsets = offsetToClassDefOffsets;
        _classDefOffsets = classDefOffsets;
        _slots = slots;
        // The distinct first-appearance list: identity is the materialized instance
        // (read models share one instance per wire body; factory models keep one
        // representative instance per emission body - the write-side dedupe dual).
        var distinct = new List<ClassDefTable>(slots.Length);
        var seen = new HashSet<ClassDefTable>(ReferenceEqualityComparer.Instance);
        foreach (var slot in slots)
        {
            if (seen.Add(slot)) distinct.Add(slot);
        }
        _distinct = distinct.ToArray();
        _matrix = matrix;
        Location = location;
        ConsumedLength = consumedLength;
    }

    /// <summary>The wire version word; only 0 exists (the SupportedVersions gate).</summary>
    public ushort Version { get; }
    /// <summary>The explicit numGlyphs input every ClassDef GID domain was answered with.</summary>
    public int NumGlyphs { get; }
    /// <summary>The merge-class square's side; the matrix holds exactly n² bytes.</summary>
    public ushort MergeClassCount { get; }
    /// <summary>The raw mergeDataOffset word as read (the matrix start, table-relative).</summary>
    public ushort MergeDataOffset { get; }
    /// <summary>The raw classDefCount word as read.</summary>
    public ushort ClassDefCount { get; }
    /// <summary>The raw offsetToClassDefOffsets word as read.</summary>
    public ushort OffsetToClassDefOffsets { get; }
    /// <summary>The raw Offset16 slot values in array order (one per declared element).</summary>
    public IReadOnlyList<ushort> ClassDefOffsets => Array.AsReadOnly(_classDefOffsets);
    /// <summary>The per-slot classDef models in array order; two slots aimed at one
    /// wire body carry the same instance (the shared-identity rule).</summary>
    public IReadOnlyList<ClassDefTable> ClassDefSlots => Array.AsReadOnly(_slots);
    /// <summary>The distinct classDef bodies in first-appearance (array) order - the
    /// sequence the cross-table strict-increase fact was proven over.</summary>
    public IReadOnlyList<ClassDefTable> ClassDefs => Array.AsReadOnly(_distinct);
    /// <summary>The merge matrix in row-major wire order: entry (row, column) at
    /// row · <see cref="MergeClassCount"/> + column. The bytes are the raw flag words;
    /// the reserved ridge is provably clear (the OQ-1 hard face refused it otherwise).</summary>
    public ReadOnlySpan<byte> Matrix => _matrix;
    /// <summary>The matrix byte count, always <see cref="MergeClassCount"/>².</summary>
    public int MatrixLength => _matrix.Length;
    public DataLocation Location { get; }
    public int ConsumedLength { get; }

    private static ReadStatus InvalidOrdinal(int ordinal, int mergeClassCount,
        DataLocation location, out FontDiagnostic diagnostic) =>
        ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
            location.AtLocal(DataField.Count, MergLayout.MergeClassCountAt, ordinal),
            out diagnostic, unchecked((ulong)(long)ordinal), (ulong)mergeClassCount);

    /// <summary>Reads one merge entry by its (row, column) merge-class pair (row =
    /// first element's class, column = second's, the spec's visual-order note); the
    /// flat position is row · mergeClassCount + column.</summary>
    public ReadStatus TryGetMergeEntry(int row, int column, out MergeEntry entry,
        out FontDiagnostic diagnostic)
    {
        entry = default;
        if ((uint)row >= (uint)MergeClassCount)
            return InvalidOrdinal(row, MergeClassCount, Location, out diagnostic);
        if ((uint)column >= (uint)MergeClassCount)
            return InvalidOrdinal(column, MergeClassCount, Location, out diagnostic);
        entry = new(_matrix[row * MergeClassCount + column]);
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>Atomically moves one merge entry (the card's edit witness, OQ-7's
    /// single API): the position answers the mergeClassCount domain and the replacement
    /// flags answer the defined-bits domain - the reserved ridge 0x08|0x80 refuses with
    /// the read chain's InvalidValue shape (the OQ-1 ruling at the edit face). The
    /// classDef slots and every structural word ride unchanged: matrix resizes, classDef
    /// additions or removals and version-upgrade writes stay outside the surface;
    /// consumers re-root through <see cref="MergTableCompiler.TryCreate"/> and ClassDef
    /// content edits go through the existing ClassDef editing chain.</summary>
    public ReadStatus TryWithMergeEntry(int row, int column, byte flags,
        out MergTable? updated, out FontDiagnostic diagnostic)
    {
        updated = null;
        if ((uint)row >= (uint)MergeClassCount)
            return InvalidOrdinal(row, MergeClassCount, Location, out diagnostic);
        if ((uint)column >= (uint)MergeClassCount)
            return InvalidOrdinal(column, MergeClassCount, Location, out diagnostic);
        if ((flags & MergLayout.ReservedFlagsMask) != 0)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.AtLocal(DataField.Payload,
                    (ulong)(MergeDataOffset + row * MergeClassCount + column),
                    row * MergeClassCount + column),
                out diagnostic, flags, (ulong)(flags & MergLayout.DefinedFlagsMask));
        var matrix = (byte[])_matrix.Clone();
        matrix[row * MergeClassCount + column] = flags;
        updated = new(Version, NumGlyphs, MergeClassCount, MergeDataOffset,
            ClassDefCount, OffsetToClassDefOffsets, _classDefOffsets,
            (ClassDefTable[])_slots.Clone(), matrix, Location, ConsumedLength);
        diagnostic = default;
        return ReadStatus.Done;
    }
}
