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
}
