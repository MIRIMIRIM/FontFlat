using FontWeave.Core;

namespace FontWeave.Containers;

/// <summary>Wire numbers, not an owned table or an authority to slice a different input.</summary>
public readonly record struct SfntDirectoryRecord(int Ordinal, uint Tag, uint Checksum,
    uint FileOffset, uint Length);

/// <summary>Raw local reads preserve wire order, duplicates and declared counts.</summary>
public readonly ref struct SfntDirectoryView
{
    private readonly ReadOnlySpan<byte> _source;
    private readonly FontFaceRef _face;
    internal SfntDirectoryView(ReadOnlySpan<byte> source, FontFaceRef face)
    {
        _source = source;
        _face = face;
    }
    public bool IsBound => _face.IsBound;
    internal int SourceByteLength => _source.Length;
    public FontFaceRef Face => _face;
    public uint Version { get { RequireBound(); return ReadWire.U32(_source, (int)_face.DirectoryFileOffset); } }
    public ushort DeclaredCount { get { RequireBound(); return ReadWire.U16(_source, (int)_face.DirectoryFileOffset + 4); } }
    private void RequireBound()
    {
        if (!IsBound) throw new InvalidOperationException("The directory view is unbound.");
    }

    public ReadStatus TryGetRecord(int ordinal, out SfntDirectoryRecord record, out FontDiagnostic diagnostic)
    {
        record = default;
        diagnostic = default;
        if (!IsBound)
            return ReadWire.Fail(ReadStatus.Uninitialized, DiagnosticCode.UnboundView, _face.Location, out diagnostic);
        if (ordinal < 0 || ordinal >= DeclaredCount)
            return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                _face.Location.At(DataField.Count, (ulong)_face.DirectoryFileOffset + 4, ordinal),
                out diagnostic, ordinal < 0 ? 0 : (ulong)ordinal, DeclaredCount);
        ulong at = (ulong)_face.DirectoryFileOffset + 12 + 16UL * (uint)ordinal;
        if (!ReadWire.Slice(_source, at, 16, out var bytes))
            return ReadWire.Fail(ReadStatus.Truncated, DiagnosticCode.TruncatedData,
                _face.Location.At(DataField.Tag, at, ordinal), out diagnostic, (ulong)_source.Length, at + 16);
        record = new(ordinal, ReadWire.U32(bytes, 0), ReadWire.U32(bytes, 4), ReadWire.U32(bytes, 8), ReadWire.U32(bytes, 12));
        return ReadStatus.Done;
    }

    public DataLocation TableLocation(int ordinal)
    {
        ReadStatus status = TryGetRecord(ordinal, out var record, out _);
        if (status != ReadStatus.Done) throw new InvalidOperationException("Read the directory record successfully first.");
        return new(_face.SourceId, _face.FaceIndex, record.Tag, -1, DataField.None, OffsetBasis.Table, 0, ordinal);
    }

    public ReadStatus TryGetTableBytes(int ordinal, out ReadOnlySpan<byte> bytes, out FontDiagnostic diagnostic)
    {
        bytes = default;
        ReadStatus status = TryGetRecord(ordinal, out var record, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (!ReadWire.Slice(_source, record.FileOffset, record.Length, out bytes))
            return ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.RangeOutsideSource,
                RecordLocation(record, DataField.Offset, 8), out diagnostic,
                (ulong)record.FileOffset + record.Length, (ulong)_source.Length);
        return ReadStatus.Done;
    }

    // No first/last duplicate wins and no success based on a truncated directory prefix.
    public ReadStatus TryFindUniqueTable(uint tag, out int ordinal, out FontDiagnostic diagnostic)
    {
        ordinal = -1;
        diagnostic = default;
        if (!IsBound)
            return ReadWire.Fail(ReadStatus.Uninitialized, DiagnosticCode.UnboundView, _face.Location, out diagnostic);
        int found = -1;
        for (int i = 0; i < DeclaredCount; i++)
        {
            ReadStatus status = TryGetRecord(i, out var record, out diagnostic);
            if (status != ReadStatus.Done) return status;
            if (record.Tag != tag) continue;
            if (found >= 0)
                return ReadWire.Fail(ReadStatus.DuplicateTable, DiagnosticCode.DuplicateTag,
                    RecordLocation(record, DataField.Tag, 0), out diagnostic, tag, (ulong)found);
            found = i;
        }
        if (found < 0)
            return ReadWire.Fail(ReadStatus.MissingTable, DiagnosticCode.TableNotFound,
                _face.Location with { TableTag = tag }, out diagnostic, tag);
        ordinal = found;
        return ReadStatus.Done;
    }

    /// <summary>Standalone directory preflight, not full-font or operation-policy validation.
    /// TTC strict validation stays with M1 until the cross-face contract is migrated.</summary>
    public ReadStatus Validate(out FontDiagnostic diagnostic, int maxTables = 4095)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxTables);
        diagnostic = default;
        if (!IsBound)
            return ReadWire.Fail(ReadStatus.Uninitialized, DiagnosticCode.UnboundView, _face.Location, out diagnostic);
        if (_face.IsCollection || Version is not (0x00010000 or 0x4F54544F))
            return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedVersion,
                _face.Location.At(DataField.Version, _face.DirectoryFileOffset), out diagnostic,
                _face.IsCollection ? 0x74746366u : Version);
        if (DeclaredCount == 0)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                _face.Location.At(DataField.Count, (ulong)_face.DirectoryFileOffset + 4), out diagnostic, 0, 1);
        if (DeclaredCount > maxTables)
            return ReadWire.Fail(ReadStatus.ResourceLimit, DiagnosticCode.BudgetExceeded,
                _face.Location.At(DataField.Count, (ulong)_face.DirectoryFileOffset + 4), out diagnostic,
                DeclaredCount, (ulong)maxTables);
        ulong directoryEnd = (ulong)_face.DirectoryFileOffset + 12 + 16UL * DeclaredCount;
        var tags = new Dictionary<uint, int>();
        var occupied = new List<SfntDirectoryRecord>();
        for (int i = 0; i < DeclaredCount; i++)
        {
            ReadStatus status = TryGetRecord(i, out var record, out diagnostic);
            if (status != ReadStatus.Done) return status;
            for (int shift = 0; shift < 32; shift += 8)
                if (((record.Tag >> shift) & 0xFF) is < 0x20 or > 0x7E)
                    return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                        RecordLocation(record, DataField.Tag, 0), out diagnostic, record.Tag);
            if (!tags.TryAdd(record.Tag, i))
                return ReadWire.Fail(ReadStatus.DuplicateTable, DiagnosticCode.DuplicateTag,
                    RecordLocation(record, DataField.Tag, 0), out diagnostic, record.Tag, (ulong)tags[record.Tag]);
            status = TryGetTableBytes(i, out _, out diagnostic);
            if (status != ReadStatus.Done) return status;
            if (record.Length == 0) continue;
            if ((ulong)record.FileOffset < directoryEnd &&
                (ulong)record.FileOffset + record.Length > _face.DirectoryFileOffset)
                return ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.OverlappingRange,
                    RecordLocation(record, DataField.Offset, 8), out diagnostic, record.FileOffset, directoryEnd);
            occupied.Add(record);
        }
        occupied.Sort(static (a, b) => a.FileOffset.CompareTo(b.FileOffset));
        for (int i = 1; i < occupied.Count; i++)
            if ((ulong)occupied[i - 1].FileOffset + occupied[i - 1].Length > occupied[i].FileOffset)
                return ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.OverlappingRange,
                    RecordLocation(occupied[i], DataField.Offset, 8), out diagnostic,
                    occupied[i].FileOffset, (ulong)occupied[i - 1].FileOffset + occupied[i - 1].Length);
        return ReadStatus.Done;
    }

    private DataLocation RecordLocation(SfntDirectoryRecord record, DataField field, uint fieldOffset) =>
        _face.Location.At(field, (ulong)_face.DirectoryFileOffset + 12 + 16UL * (uint)record.Ordinal + fieldOffset,
            record.Ordinal) with { TableTag = record.Tag, TableOrdinal = record.Ordinal };
}
