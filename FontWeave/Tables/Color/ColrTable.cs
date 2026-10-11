using FontWeave.Core;

namespace FontWeave.Tables;

/// <summary>One base glyph record: the glyph, its first layer ordinal and the
/// consecutive layer count (the slice runs first-to-last in z order, spec
/// L673; sharing with another glyph's slice is legal, L675).</summary>
public readonly record struct ColrBaseGlyphRecord(ushort GlyphId, ushort FirstLayerIndex, ushort NumLayers);

/// <summary>One layer record: the painted glyph and its palette entry, with
/// 0xFFFF as the foreground sentinel (spec L688, the OQ-T6-01-7 line).</summary>
public readonly record struct ColrLayerRecord(ushort GlyphId, ushort PaletteIndex);

/// <summary>Owned COLR v0 table: the base glyph records in strict ascending
/// order, the layer records in wire order, and the caller's numGlyphs receipt
/// re-answered by every edit. This model carries no color interpretation, no
/// z-order expansion and no palette application - those are the renderer's
/// domain (card T6-01 §11). Produced by ColrView.TryReadTable and edited through
/// the ColrTableCompiler API family. The two record offsets are the source
/// coordinates the model was read from (canonical positions for factory-built
/// models), so row diagnostics stay addressable.</summary>
public sealed class ColrTable
{
    private readonly ColrBaseGlyphRecord[] _baseGlyphs;
    private readonly ColrLayerRecord[] _layers;

    internal ColrTable(ColrBaseGlyphRecord[] baseGlyphs, ColrLayerRecord[] layers,
        int numGlyphs, uint baseGlyphRecordsOffset, uint layerRecordsOffset,
        DataLocation location, int consumedLength)
    {
        _baseGlyphs = baseGlyphs;
        _layers = layers;
        NumGlyphs = numGlyphs;
        BaseGlyphRecordsOffset = baseGlyphRecordsOffset;
        LayerRecordsOffset = layerRecordsOffset;
        Location = location;
        ConsumedLength = consumedLength;
    }

    public ushort Version => 0;

    /// <summary>The caller's maxp-forward authority, receipted at read time and
    /// re-answered on every edit (the MATH explicit-input line).</summary>
    public int NumGlyphs { get; }

    public int BaseGlyphCount => _baseGlyphs.Length;
    public int LayerCount => _layers.Length;
    public IReadOnlyList<ColrBaseGlyphRecord> BaseGlyphs => Array.AsReadOnly(_baseGlyphs);
    public IReadOnlyList<ColrLayerRecord> Layers => Array.AsReadOnly(_layers);

    /// <summary>The base glyph records' array start in the coordinates the model
    /// was read from (wire offsets, or the canonical plan for factory models).</summary>
    public uint BaseGlyphRecordsOffset { get; }

    /// <summary>The layer records' array start in the same coordinates.</summary>
    public uint LayerRecordsOffset { get; }

    public DataLocation Location { get; }
    public int ConsumedLength { get; }

    internal ulong BaseGlyphRowPosition(int ordinal) =>
        BaseGlyphRecordsOffset + (ulong)ordinal * ColrLayout.BaseGlyphRecordSize;

    internal ulong LayerRowPosition(int ordinal) =>
        LayerRecordsOffset + (ulong)ordinal * ColrLayout.LayerRecordSize;

    /// <summary>Binary search over the owned records (the strict ascent is the
    /// read-side contract, spec L671). A miss is a normal outcome, not an error.</summary>
    public ReadStatus TryFindBaseGlyph(int glyphId, out int ordinal, out FontDiagnostic diagnostic)
    {
        ordinal = -1;
        if (glyphId < 0 || glyphId > ushort.MaxValue)
            return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                Location.AtLocal(DataField.GlyphId, BaseGlyphRecordsOffset), out diagnostic,
                unchecked((ulong)(long)glyphId), ushort.MaxValue);
        int low = 0, high = _baseGlyphs.Length - 1;
        while (low <= high)
        {
            int mid = low + (high - low) / 2;
            int id = _baseGlyphs[mid].GlyphId;
            if (id == glyphId)
            {
                ordinal = mid;
                diagnostic = default;
                return ReadStatus.Done;
            }
            if (id < glyphId) low = mid + 1;
            else high = mid - 1;
        }
        return ReadWire.Fail(ReadStatus.NotFound, DiagnosticCode.GlyphNotMapped,
            Location.AtLocal(DataField.GlyphId, BaseGlyphRecordsOffset, glyphId),
            out diagnostic, (uint)glyphId);
    }

    /// <summary>Projects one base glyph's contiguous layer slice from the owned
    /// records, shared or overlapping slices included verbatim (spec L675).</summary>
    public ReadStatus TryGetLayerSlice(int glyphId, out ColrLayerSlice slice, out FontDiagnostic diagnostic)
    {
        slice = default;
        ReadStatus status = TryFindBaseGlyph(glyphId, out int ordinal, out diagnostic);
        if (status != ReadStatus.Done) return status;
        var row = _baseGlyphs[ordinal];
        slice = new ColrLayerSlice(row.FirstLayerIndex, row.NumLayers);
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>Atomically moves one layer's glyph id (the card's edit witness).
    /// The position is the wire-order ordinal; the replacement glyph answers the
    /// caller's explicit numGlyphs authority here and is re-answered again by the
    /// root factory on re-rooting (the MATH variantGlyph line). Structural
    /// changes - layer additions, removals or cross-slice moves, base glyph
    /// changes - stay outside the surface (OQ-T6-01-9); consumers re-root through
    /// ColrTableCompiler.TryCreate.</summary>
    public ReadStatus TryWithLayerGlyph(int layerPosition, ushort glyphId, int numGlyphs,
        out ColrTable? updated, out FontDiagnostic diagnostic)
    {
        updated = null;
        ReadStatus status = RequireLayerPosition(layerPosition, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = RequireNumGlyphsInput(numGlyphs, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (glyphId >= (ushort)numGlyphs)
            return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                Location.AtLocal(DataField.GlyphId, LayerRowPosition(layerPosition), layerPosition),
                out diagnostic, glyphId, (ulong)numGlyphs);
        var layers = (ColrLayerRecord[])_layers.Clone();
        layers[layerPosition] = new(glyphId, layers[layerPosition].PaletteIndex);
        updated = WithLayers(layers);
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>Atomically moves one layer's palette entry; 0xFFFF is the legal
    /// foreground sentinel and the cross-table domain re-answers the caller's
    /// explicit numPaletteEntries (null skips the domain, the read-side
    /// OQ-T6-01-4 line).</summary>
    public ReadStatus TryWithLayerPaletteIndex(int layerPosition, ushort paletteIndex,
        int? cpalNumPaletteEntries, out ColrTable? updated, out FontDiagnostic diagnostic)
    {
        updated = null;
        ReadStatus status = RequireLayerPosition(layerPosition, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (cpalNumPaletteEntries is int entries &&
            paletteIndex != ColrLayout.ForegroundPaletteIndex && paletteIndex >= (uint)entries)
            return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                Location.AtLocal(DataField.Payload, LayerRowPosition(layerPosition) + 2, layerPosition),
                out diagnostic, paletteIndex, unchecked((ulong)(long)entries));
        var layers = (ColrLayerRecord[])_layers.Clone();
        layers[layerPosition] = new(layers[layerPosition].GlyphId, paletteIndex);
        updated = WithLayers(layers);
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>Reorders one base glyph's slice rows in place - the z-order edit.
    /// The permutation must be a permutation of the slice's row ordinals: the
    /// same multiset of rows, the same count, other slices untouched. Any other
    /// change (cross-slice moves, count changes) is a structural refusal
    /// (OQ-T6-01-9); the reordered rows carry their own glyph/palette values, so
    /// no domain is re-answered.</summary>
    public ReadStatus TryWithLayerSequence(int glyphId, IReadOnlyList<int> permutation,
        out ColrTable? updated, out FontDiagnostic diagnostic)
    {
        updated = null;
        ArgumentNullException.ThrowIfNull(permutation);
        ReadStatus status = TryFindBaseGlyph(glyphId, out int ordinal, out diagnostic);
        if (status != ReadStatus.Done) return status;
        var row = _baseGlyphs[ordinal];
        if (permutation.Count != row.NumLayers)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.AtLocal(DataField.Payload, BaseGlyphRowPosition(ordinal) + 4, ordinal),
                out diagnostic, (uint)permutation.Count, (ulong)row.NumLayers);
        var seen = new bool[row.NumLayers];
        foreach (int position in permutation)
        {
            if (position < 0 || position >= row.NumLayers)
                return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                    Location.AtLocal(DataField.Payload, LayerRowPosition(row.FirstLayerIndex), position),
                    out diagnostic, unchecked((ulong)(long)position), (ulong)row.NumLayers);
            if (seen[position])
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    Location.AtLocal(DataField.Payload,
                        LayerRowPosition(row.FirstLayerIndex + position), position),
                    out diagnostic, (uint)position, (ulong)row.NumLayers);
            seen[position] = true;
        }
        var layers = (ColrLayerRecord[])_layers.Clone();
        for (int i = 0; i < permutation.Count; i++)
            layers[row.FirstLayerIndex + i] = _layers[row.FirstLayerIndex + permutation[i]];
        updated = WithLayers(layers);
        diagnostic = default;
        return ReadStatus.Done;
    }

    private ReadStatus RequireLayerPosition(int layerPosition, out FontDiagnostic diagnostic)
    {
        if ((uint)layerPosition >= (uint)_layers.Length)
            return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                Location.AtLocal(DataField.GlyphId, LayerRecordsOffset +
                    4UL * (ulong)layerPosition, layerPosition), out diagnostic,
                unchecked((ulong)(long)layerPosition), (ulong)_layers.Length);
        diagnostic = default;
        return ReadStatus.Done;
    }

    private ReadStatus RequireNumGlyphsInput(int numGlyphs, out FontDiagnostic diagnostic)
    {
        if (numGlyphs is < 0 or > ushort.MaxValue)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.AtLocal(DataField.None, 0), out diagnostic,
                unchecked((ulong)(long)numGlyphs), ushort.MaxValue);
        diagnostic = default;
        return ReadStatus.Done;
    }

    private ColrTable WithLayers(ColrLayerRecord[] layers) =>
        new((ColrBaseGlyphRecord[])_baseGlyphs.Clone(), layers, NumGlyphs,
            BaseGlyphRecordsOffset, LayerRecordsOffset, Location, ConsumedLength);
}
