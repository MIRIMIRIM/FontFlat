using FontWeave.Core;

namespace FontWeave.Tables;

/// <summary>Owned CPAL table (card T6-01 §2): the palette-entry count, the color
/// record indices in wire order, the BGRA color records as verbatim byte quads
/// (wire order blue/green/red/alpha, non-premultiplied sRGB - never interpreted,
/// never reordered to RGBA), and the three v1 trailing arrays (null exactly when
/// the offset is zero, "no array provided", spec L80-82; always null on v0).
/// No palette application, no color interpolation, no nameID dereference - those
/// are consumer domains (card T6-01 §11). Produced by CpalView.TryReadTable and
/// edited through the CpalTableCompiler API family.</summary>
public sealed class CpalTable
{
    private readonly ushort[] _colorRecordIndices;
    private readonly byte[][] _colorRecords;

    internal CpalTable(ushort version, ushort numPaletteEntries, ushort[] colorRecordIndices,
        byte[][] colorRecords, uint[]? paletteTypes, ushort[]? paletteLabels,
        ushort[]? paletteEntryLabels, uint colorRecordsOffset, uint paletteTypesOffset,
        uint paletteLabelsOffset, uint paletteEntryLabelsOffset, DataLocation location,
        int consumedLength)
    {
        Version = version;
        NumPaletteEntries = numPaletteEntries;
        _colorRecordIndices = colorRecordIndices;
        _colorRecords = colorRecords;
        PaletteTypes = paletteTypes;
        PaletteLabels = paletteLabels;
        PaletteEntryLabels = paletteEntryLabels;
        ColorRecordsOffset = colorRecordsOffset;
        PaletteTypesOffset = paletteTypesOffset;
        PaletteLabelsOffset = paletteLabelsOffset;
        PaletteEntryLabelsOffset = paletteEntryLabelsOffset;
        Location = location;
        ConsumedLength = consumedLength;
    }

    /// <summary>The wire version: 0 or 1 (the declared supported domain).</summary>
    public ushort Version { get; }
    public bool IsVersion1 => Version == CpalLayout.Version1;

    /// <summary>The per-palette entry count; every palette projects this many
    /// consecutive records from its colorRecordIndices[palette] start.</summary>
    public ushort NumPaletteEntries { get; }

    public int PaletteCount => _colorRecordIndices.Length;
    public int ColorRecordCount => _colorRecords.Length;
    public IReadOnlyList<ushort> ColorRecordIndices => Array.AsReadOnly(_colorRecordIndices);

    /// <summary>The BGRA byte quads in wire order; unedited rows round-trip
    /// byte-for-byte (the identity hard gate's basis).</summary>
    public IReadOnlyList<byte[]> ColorRecords => Array.AsReadOnly(_colorRecords);

    /// <summary>The v1 paletteTypes words, or null when the array is absent.</summary>
    public IReadOnlyList<uint>? PaletteTypes { get; }

    /// <summary>The v1 paletteLabels nameIDs (0xFFFF = no string), or null when absent.</summary>
    public IReadOnlyList<ushort>? PaletteLabels { get; }

    /// <summary>The v1 paletteEntryLabels nameIDs (0xFFFF = no string), or null when absent.</summary>
    public IReadOnlyList<ushort>? PaletteEntryLabels { get; }

    public DataLocation Location { get; }
    public int ConsumedLength { get; }

    /// <summary>The first record ordinal a palette projects; the count is
    /// <see cref="NumPaletteEntries"/>. Sharing/overlap is projected verbatim.</summary>
    public ReadStatus TryGetPaletteRecords(int paletteIndex, out CpalPaletteRecords records,
        out FontDiagnostic diagnostic)
    {
        records = default;
        if ((uint)paletteIndex >= (uint)PaletteCount)
            return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                Location.AtLocal(DataField.Count, CpalLayout.IndicesAt + 2UL * (ulong)paletteIndex),
                out diagnostic, unchecked((ulong)(long)paletteIndex), (ulong)PaletteCount);
        records = new CpalPaletteRecords(_colorRecordIndices[paletteIndex], NumPaletteEntries);
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>The BGRA quad of one record in wire order.</summary>
    public ReadStatus TryGetColorRecord(int recordIndex, out byte blue, out byte green, out byte red,
        out byte alpha, out FontDiagnostic diagnostic)
    {
        blue = green = red = alpha = 0;
        if ((uint)recordIndex >= (uint)_colorRecords.Length)
            return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                Location.AtLocal(DataField.Payload, ColorRecordsOffset +
                    4UL * (ulong)recordIndex, recordIndex), out diagnostic,
                unchecked((ulong)(long)recordIndex), (ulong)_colorRecords.Length);
        var row = _colorRecords[recordIndex];
        blue = row[0];
        green = row[1];
        red = row[2];
        alpha = row[3];
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>The color records' array start in the coordinates the model was
    /// read from (wire offset, or the canonical plan for factory models) - the
    /// record-row diagnostics' coordinate.</summary>
    public uint ColorRecordsOffset { get; }

    /// <summary>The three v1 trailing arrays' starts in the same coordinates;
    /// zero marks an absent array.</summary>
    public uint PaletteTypesOffset { get; }
    public uint PaletteLabelsOffset { get; }
    public uint PaletteEntryLabelsOffset { get; }

    /// <summary>Atomically replaces one BGRA record row (the card's edit
    /// witness). Palettes sharing the record follow by spec semantics - the
    /// index arithmetic is the sharing, no special case exists (spec L41/L86).
    /// Structural changes - palette, entry or record count changes, version
    /// conversion - stay outside the surface (OQ-T6-01-9); consumers re-root
    /// through CpalTableCompiler.TryCreate.</summary>
    public ReadStatus TryWithColorRecord(int recordIndex, byte blue, byte green, byte red, byte alpha,
        out CpalTable? updated, out FontDiagnostic diagnostic)
    {
        updated = null;
        if ((uint)recordIndex >= (uint)_colorRecords.Length)
            return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                Location.AtLocal(DataField.Payload, ColorRecordsOffset + 4UL * (ulong)recordIndex,
                    recordIndex), out diagnostic, unchecked((ulong)(long)recordIndex),
                (ulong)_colorRecords.Length);
        var records = (byte[][])_colorRecords.Clone();
        records[recordIndex] = [blue, green, red, alpha];
        updated = new(Version, NumPaletteEntries, _colorRecordIndices, records,
            (uint[]?)PaletteTypesInternal?.Clone(), (ushort[]?)PaletteLabelsInternal?.Clone(),
            (ushort[]?)PaletteEntryLabelsInternal?.Clone(), ColorRecordsOffset, PaletteTypesOffset,
            PaletteLabelsOffset, PaletteEntryLabelsOffset, Location, ConsumedLength);
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>Atomically replaces one v1 paletteTypes word; the two defined
    /// flag bits are legal together and the reserved 0xFFFC mask refuses with
    /// the same read-side face (OQ-T6-01-5). A v0 table has no surface.</summary>
    public ReadStatus TryWithPaletteType(int paletteIndex, uint flags,
        out CpalTable? updated, out FontDiagnostic diagnostic)
    {
        updated = null;
        ReadStatus status = RequirePaletteEditDomain(paletteIndex, PaletteTypesInternal,
            DataField.Flags, PaletteTypesOffset + 4UL * (ulong)paletteIndex, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if ((flags & CpalLayout.PaletteReservedMask) != 0)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.AtLocal(DataField.Flags, PaletteTypesOffset + 4UL * (ulong)paletteIndex,
                    paletteIndex), out diagnostic, flags, 0UL);
        var types = (uint[])PaletteTypesInternal!.Clone();
        types[paletteIndex] = flags;
        updated = WithTrailingArrays(types, PaletteLabelsInternal, PaletteEntryLabelsInternal);
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>Atomically replaces one v1 paletteLabels nameID; any u16 is a
    /// raw value and 0xFFFF is the legal no-string (spec L135) - never
    /// dereferenced against the name table (OQ-T6-01-7).</summary>
    public ReadStatus TryWithPaletteLabel(int paletteIndex, ushort nameId,
        out CpalTable? updated, out FontDiagnostic diagnostic)
    {
        updated = null;
        ReadStatus status = RequirePaletteEditDomain(paletteIndex, PaletteLabelsInternal,
            DataField.Payload, PaletteLabelsOffset + 2UL * (ulong)paletteIndex, out diagnostic);
        if (status != ReadStatus.Done) return status;
        var labels = (ushort[])PaletteLabelsInternal!.Clone();
        labels[paletteIndex] = nameId;
        updated = WithTrailingArrays((uint[]?)PaletteTypesInternal?.Clone(), labels,
            PaletteEntryLabelsInternal);
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>Atomically replaces one v1 paletteEntryLabels nameID over the
    /// entry domain; 0xFFFF is the legal no-string (spec L143).</summary>
    public ReadStatus TryWithPaletteEntryLabel(int entryIndex, ushort nameId,
        out CpalTable? updated, out FontDiagnostic diagnostic)
    {
        updated = null;
        if (!IsVersion1)
            return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
                Location.AtLocal(DataField.Version, 0), out diagnostic, (uint)Version);
        if (PaletteEntryLabelsInternal is null)
            return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
                Location.AtLocal(DataField.Offset, PaletteEntryLabelsOffset), out diagnostic,
                PaletteEntryLabelsOffset, 0UL);
        if ((uint)entryIndex >= (uint)PaletteEntryLabelsInternal.Length)
            return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                Location.AtLocal(DataField.Count, 2, entryIndex), out diagnostic,
                unchecked((ulong)(long)entryIndex), (ulong)PaletteEntryLabelsInternal.Length);
        var labels = (ushort[])PaletteEntryLabelsInternal!.Clone();
        labels[entryIndex] = nameId;
        updated = WithTrailingArrays((uint[]?)PaletteTypesInternal?.Clone(),
            (ushort[]?)PaletteLabelsInternal?.Clone(), labels);
        diagnostic = default;
        return ReadStatus.Done;
    }

    private ReadStatus RequirePaletteEditDomain(int paletteIndex, Array? array, DataField field,
        ulong at, out FontDiagnostic diagnostic)
    {
        if (!IsVersion1)
            return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
                Location.AtLocal(DataField.Version, 0), out diagnostic, (uint)Version);
        if (array is null)
            return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
                Location.AtLocal(DataField.Offset, at), out diagnostic, at, 0UL);
        if ((uint)paletteIndex >= (uint)array.Length)
            return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                Location.AtLocal(DataField.Count, 4, paletteIndex), out diagnostic,
                unchecked((ulong)(long)paletteIndex), (ulong)array.Length);
        diagnostic = default;
        return ReadStatus.Done;
    }

    private CpalTable WithTrailingArrays(uint[]? types, ushort[]? labels, ushort[]? entryLabels) =>
        new(Version, NumPaletteEntries, _colorRecordIndices, _colorRecords, types, labels,
            entryLabels, ColorRecordsOffset, PaletteTypesOffset, PaletteLabelsOffset,
            PaletteEntryLabelsOffset, Location, ConsumedLength);

    internal uint[]? PaletteTypesInternal => (uint[]?)PaletteTypes;
    internal ushort[]? PaletteLabelsInternal => (ushort[]?)PaletteLabels;
    internal ushort[]? PaletteEntryLabelsInternal => (ushort[]?)PaletteEntryLabels;
}
