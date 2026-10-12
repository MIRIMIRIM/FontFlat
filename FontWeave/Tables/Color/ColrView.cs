using FontWeave.Core;
using FontWeave.Tables.Layout;

namespace FontWeave.Tables;

/// <summary>Strict whole-table validation and materialization for COLR v0 (card
/// T6-01 §3). numGlyphs is the explicit maxp-forward authority for both glyph
/// domains (the T4-10 MATH variantGlyph line), and cpalNumPaletteEntries is the
/// explicit cross-table authority for the layer paletteIndex domain; null skips
/// that domain (OQ-T6-01-4: the container-level COLR⟹CPAL requirement is the
/// joint validator's business, and a CPAL-only face is legal, spec L157). The
/// v1 face takes fvarAxisCount (T6-03 §3, OQ-T6-03-1) as the store⟡fvar
/// signature authority: it must be given exactly when the table declares an
/// ItemVariationStore, and it feeds the store's region-axis domain. Error
/// priority follows the T4-01 line: version, presence, bounds, relations, value
/// domains. Pure validation materializes nothing.</summary>
public readonly ref partial struct ColrView
{
    /// <summary>The frozen two-input gate (cards T6-01/T6-02): a null
    /// fvarAxisCount, i.e. the non-variable face.</summary>
    public ReadStatus ValidateTable(int numGlyphs, int? cpalNumPaletteEntries,
        out FontDiagnostic diagnostic, BuildLimits? limits = null) =>
        ValidateTable(numGlyphs, cpalNumPaletteEntries, null, out diagnostic, limits);

    public ReadStatus ValidateTable(int numGlyphs, int? cpalNumPaletteEntries,
        int? fvarAxisCount, out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        var budget = new BuildBudget(limits);
        if (!budget.Input(_data.Length, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        return ValidateTable(numGlyphs, cpalNumPaletteEntries, fvarAxisCount, out diagnostic, budget);
    }

    internal ReadStatus ValidateTable(int numGlyphs, int? cpalNumPaletteEntries,
        out FontDiagnostic diagnostic, BuildBudget budget) =>
        ValidateTable(numGlyphs, cpalNumPaletteEntries, null, out diagnostic, budget);

    internal ReadStatus ValidateTable(int numGlyphs, int? cpalNumPaletteEntries,
        int? fvarAxisCount, out FontDiagnostic diagnostic, BuildBudget budget)
    {
        // The version gate first (the T4-01 priority line); version 1 dispatches
        // to the v1 half of this View (the compat grace, the three arrays, the
        // ClipList and the paint graph walk - ColrViewV1), version 0 keeps the
        // frozen T6-01 walk below byte for byte (OQ-T6-01-12's witness pool
        // became the accepted domain in T6-02; the v0 path never moved).
        ReadStatus status = TryRequireKnownVersion(out ulong version, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (version == ColrLayout.Version1)
        {
            status = CoverageTableCompiler.CheckNumGlyphs(numGlyphs, Location, out diagnostic);
            if (status != ReadStatus.Done) return status;
            return ValidateTableV1(numGlyphs, cpalNumPaletteEntries, fvarAxisCount,
                out diagnostic, budget);
        }
        status = CoverageTableCompiler.CheckNumGlyphs(numGlyphs, Location, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = TryGetNumBaseGlyphRecords(out ushort numBase, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = TryGetNumLayerRecords(out ushort numLayers, out diagnostic);
        if (status != ReadStatus.Done) return status;
        // "BaseGlyph and Layer records are required for COLR version 0" (spec L657):
        // the "may be 0 / may be NULL" wordings all hang off the version-1 lines, so
        // a zero count refuses on both heads (OQ-T6-01-3; a v0 table without records
        // is a bare header shell with no color semantics).
        if (numBase == 0)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.AtLocal(DataField.Count, 2), out diagnostic, numBase, 1UL);
        if (numLayers == 0)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.AtLocal(DataField.Count, 12), out diagnostic, numLayers, 1UL);
        status = TryGetBaseGlyphRecordsOffset(out uint baseOffset, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = TryGetLayerRecordsOffset(out uint layerOffset, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (baseOffset == 0)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.AtLocal(DataField.Offset, 4), out diagnostic, baseOffset, 1UL);
        if (layerOffset == 0)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.AtLocal(DataField.Offset, 8), out diagnostic, layerOffset, 1UL);
        if (!budget.Work((long)_data.Length + 6L * (long)(numBase + numLayers),
                Location, out diagnostic)) return ReadStatus.ResourceLimit;
        // The record windows: the generated extent require owns the backward-offset
        // and truncation faces with the first incomplete record's field provenance
        // (the T4-10 family line the card names for the record arrays).
        status = TryRequireBaseGlyphRecordExtent(out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = TryRequireLayerRecordExtent(out diagnostic);
        if (status != ReadStatus.Done) return status;
        // BaseGlyph walk: glyph domain, strict glyphID ascent (spec L671 "must";
        // duplicates refuse through the same face, the T4-01 Coverage line), slice
        // relation over the NumLayers word. numLayers==0 rows and shared layer
        // slices are observations (OQ-T6-01-6, spec L675), never refusals.
        int previous = -1;
        for (int i = 0; i < numBase; i++)
        {
            status = TryGetBaseGlyphRecord(i, out var row, out diagnostic);
            if (status != ReadStatus.Done) return status;
            var at = Location.AtLocal(DataField.GlyphId, baseOffset + (ulong)i * ColrLayout.BaseGlyphRecordSize, i);
            if (row.GlyphId >= (ushort)numGlyphs)
                return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                    at, out diagnostic, row.GlyphId, (ulong)numGlyphs);
            if (i > 0 && row.GlyphId <= previous)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    at, out diagnostic, row.GlyphId, unchecked((ulong)previous));
            previous = row.GlyphId;
            if ((uint)row.FirstLayerIndex + (uint)row.NumLayers > (uint)numLayers)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    Location.AtLocal(DataField.Payload,
                        baseOffset + (ulong)i * ColrLayout.BaseGlyphRecordSize + 4, i),
                    out diagnostic, (uint)row.FirstLayerIndex + (uint)row.NumLayers, (ulong)numLayers);
        }
        // Layer walk: glyph domain plus the cross-table paletteIndex domain - every
        // value but the 0xFFFF foreground sentinel must sit below the CPAL table's
        // numPaletteEntries (spec L688 "must"); the sentinel passes through.
        for (int i = 0; i < numLayers; i++)
        {
            status = TryGetLayerRecord(i, out var row, out diagnostic);
            if (status != ReadStatus.Done) return status;
            var at = Location.AtLocal(DataField.GlyphId, layerOffset + (ulong)i * ColrLayout.LayerRecordSize, i);
            if (row.GlyphId >= (ushort)numGlyphs)
                return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                    at, out diagnostic, row.GlyphId, (ulong)numGlyphs);
            if (cpalNumPaletteEntries is int entries &&
                row.PaletteIndex != ColrLayout.ForegroundPaletteIndex && row.PaletteIndex >= (uint)entries)
                return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                    Location.AtLocal(DataField.Payload,
                        layerOffset + (ulong)i * ColrLayout.LayerRecordSize + 2, i),
                    out diagnostic, row.PaletteIndex, unchecked((ulong)(long)entries));
        }
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>Reads the complete declared slice into an owned
    /// <see cref="ColrTable"/> with the caller's numGlyphs receipt.</summary>
    /// <summary>Reads the complete declared slice into an owned
    /// <see cref="ColrTable"/> with the caller's numGlyphs receipt (the
    /// non-variable two-input face).</summary>
    public ReadStatus TryReadTable(int numGlyphs, int? cpalNumPaletteEntries,
        out ColrTable? model, out FontDiagnostic diagnostic, BuildLimits? limits = null) =>
        TryReadTable(numGlyphs, cpalNumPaletteEntries, null, out model, out diagnostic, limits);

    public ReadStatus TryReadTable(int numGlyphs, int? cpalNumPaletteEntries,
        int? fvarAxisCount, out ColrTable? model, out FontDiagnostic diagnostic,
        BuildLimits? limits = null)
    {
        model = null;
        var budget = new BuildBudget(limits);
        if (!budget.Input(_data.Length, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        return TryReadTable(numGlyphs, cpalNumPaletteEntries, fvarAxisCount, out model,
            out diagnostic, budget);
    }

    internal ReadStatus TryReadTable(int numGlyphs, int? cpalNumPaletteEntries,
        out ColrTable? model, out FontDiagnostic diagnostic, BuildBudget budget) =>
        TryReadTable(numGlyphs, cpalNumPaletteEntries, null, out model, out diagnostic, budget);

    internal ReadStatus TryReadTable(int numGlyphs, int? cpalNumPaletteEntries,
        int? fvarAxisCount, out ColrTable? model, out FontDiagnostic diagnostic,
        BuildBudget budget)
    {
        model = null;
        // Version 1 materializes through the one v1 walk (the validate-only walk
        // and the materializing walk are the same code, so a model only exists
        // for fully validated wires); version 0 keeps the T6-01 read below.
        ReadStatus status = TryRequireKnownVersion(out ulong version, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (version == ColrLayout.Version1)
        {
            status = CoverageTableCompiler.CheckNumGlyphs(numGlyphs, Location, out diagnostic);
            if (status != ReadStatus.Done) return status;
            return ReadTableV1(numGlyphs, cpalNumPaletteEntries, fvarAxisCount, true,
                out model, out diagnostic, budget);
        }
        status = ValidateTable(numGlyphs, cpalNumPaletteEntries, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        status = TryGetNumBaseGlyphRecords(out ushort numBase, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = TryGetNumLayerRecords(out ushort numLayers, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = TryGetBaseGlyphRecordsOffset(out uint baseOffset, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = TryGetLayerRecordsOffset(out uint layerOffset, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (!budget.Buffer(ColrLayout.BaseGlyphRecordSize * (long)numBase +
                ColrLayout.LayerRecordSize * (long)numLayers, Location, out diagnostic))
            return ReadStatus.ResourceLimit;
        var baseGlyphs = new ColrBaseGlyphRecord[numBase];
        for (int i = 0; i < baseGlyphs.Length; i++)
        {
            status = TryGetBaseGlyphRecord(i, out var row, out diagnostic);
            if (status != ReadStatus.Done) return status;
            baseGlyphs[i] = new(row.GlyphId, row.FirstLayerIndex, row.NumLayers);
        }
        var layers = new ColrLayerRecord[numLayers];
        for (int i = 0; i < layers.Length; i++)
        {
            status = TryGetLayerRecord(i, out var row, out diagnostic);
            if (status != ReadStatus.Done) return status;
            layers[i] = new(row.GlyphId, row.PaletteIndex);
        }
        long consumed = ColrLayout.HeaderLength;
        consumed = Math.Max(consumed, (long)baseOffset + ColrLayout.BaseGlyphRecordSize * (long)numBase);
        consumed = Math.Max(consumed, (long)layerOffset + ColrLayout.LayerRecordSize * (long)numLayers);
        model = new(baseGlyphs, layers, numGlyphs, baseOffset, layerOffset, Location, (int)consumed);
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>Binary search over the base glyph records (spec L671's "binary
    /// search can be used" as the read-side API). Gates on the version and the
    /// record window but not the whole-table walk - the strict ascent the search
    /// assumes is the validation contract. A miss is a normal outcome
    /// (<see cref="ReadStatus.NotFound"/>, the GlyphNotMapped line), not an error.</summary>
    public ReadStatus TryFindBaseGlyph(int glyphId, out int ordinal, out FontDiagnostic diagnostic)
    {
        ordinal = -1;
        if (!IsBound)
            return ReadWire.Fail(ReadStatus.Uninitialized, DiagnosticCode.UnboundView,
                Location, out diagnostic);
        if (glyphId < 0 || glyphId > ushort.MaxValue)
            return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                Location.AtLocal(DataField.GlyphId, 0), out diagnostic,
                unchecked((ulong)(long)glyphId), ushort.MaxValue);
        ReadStatus status = TryRequireKnownVersion(out _, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = TryRequireBaseGlyphRecordExtent(out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = TryGetNumBaseGlyphRecords(out ushort numBase, out diagnostic);
        if (status != ReadStatus.Done) return status;
        int low = 0, high = numBase - 1;
        while (low <= high)
        {
            int mid = low + (high - low) / 2;
            status = TryGetBaseGlyphRecord(mid, out var row, out diagnostic);
            if (status != ReadStatus.Done) return status;
            if (row.GlyphId == glyphId)
            {
                ordinal = mid;
                diagnostic = default;
                return ReadStatus.Done;
            }
            if (row.GlyphId < glyphId) low = mid + 1;
            else high = mid - 1;
        }
        return ReadWire.Fail(ReadStatus.NotFound, DiagnosticCode.GlyphNotMapped,
            Location.AtLocal(DataField.GlyphId, 0, glyphId), out diagnostic, (uint)glyphId);
    }

    /// <summary>Projects one base glyph's contiguous layer slice: the resolved
    /// first layer ordinal and the layer count (the z order runs first to last,
    /// spec L673). Sharing or overlap with another glyph's slice is the spec-legal
    /// observation and is projected without any normalization (spec L675).</summary>
    public ReadStatus TryGetLayerSlice(int glyphId, out ColrLayerSlice slice, out FontDiagnostic diagnostic)
    {
        slice = default;
        ReadStatus status = TryFindBaseGlyph(glyphId, out int ordinal, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = TryGetBaseGlyphRecord(ordinal, out var row, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = TryRequireLayerRecordExtent(out diagnostic);
        if (status != ReadStatus.Done) return status;
        slice = new ColrLayerSlice(row.FirstLayerIndex, row.NumLayers);
        diagnostic = default;
        return ReadStatus.Done;
    }
}

/// <summary>One base glyph's layer slice: the first layer ordinal and the
/// consecutive count (the z order runs first to last, spec L673).</summary>
public readonly record struct ColrLayerSlice(int FirstLayerIndex, int LayerCount);
