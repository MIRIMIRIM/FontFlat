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
}
