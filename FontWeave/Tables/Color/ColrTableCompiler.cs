using FontWeave.Core;

namespace FontWeave.Tables;

/// <summary>Canonical COLR v0 compiler (card T6-01 §4, OQ-T6-01-8's majority
/// order): header(14) → layerRecords → baseGlyphRecords, the corpus's 41/52
/// layer-first majority, so most v0 wires recompile byte-identical and the
/// base-first forms land in the wire-equivalent tier. Both offsets are single
/// forward emissions (no nesting, no fixpoint); record rows travel byte-for-byte
/// in field order, so an unedited round trip preserves every payload byte.
/// Structural changes - base glyph additions, removals or reordering, layer
/// count changes, version writes - have no edit surface (OQ-T6-01-9); the three
/// layer edits return a new owned model and never mutate the source.</summary>
public static partial class ColrTableCompiler
{
    /// <summary>Explicit root-model factory from owned parts (card §4.1): the
    /// root re-answers the structural invariants the read side enforces - the
    /// strict glyphID ascent with the T4-01 face, both glyph domains against the
    /// caller's explicit numGlyphs, the wide slice relation and the cross-table
    /// paletteIndex domain (null skips it, the read side's OQ-T6-01-4 line).
    /// Empty record arrays refuse: a v0 table without records is the bare header
    /// shell the read side refuses (OQ-T6-01-3), so the factory never builds one.
    /// There are no child models, so the SameSource guard surface is empty by
    /// construction.</summary>
    public static ReadStatus TryCreate(IReadOnlyList<ColrBaseGlyphRecord> baseGlyphs,
        IReadOnlyList<ColrLayerRecord> layers, int numGlyphs, int? cpalNumPaletteEntries,
        out ColrTable? model, out FontDiagnostic diagnostic, BuildLimits? limits = null,
        DataLocation location = default)
    {
        model = null;
        ArgumentNullException.ThrowIfNull(baseGlyphs);
        ArgumentNullException.ThrowIfNull(layers);
        location = location == default ? DataLocation.Anonymous(ColrLayout.Tag) : location;
        var budget = new BuildBudget(limits);
        if (numGlyphs is < 0 or > ushort.MaxValue)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                location.AtLocal(DataField.None, 0), out diagnostic,
                unchecked((ulong)(long)numGlyphs), ushort.MaxValue);
        if (baseGlyphs.Count == 0 || layers.Count == 0)
        {
            ushort at = baseGlyphs.Count == 0 ? (ushort)2 : (ushort)12;
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                location.AtLocal(DataField.Count, at), out diagnostic, 0UL, 1UL);
        }
        // The u16 count fields bound the plan: an array longer than its count
        // word's domain cannot be declared on the wire (the NameContent line).
        if (baseGlyphs.Count > ushort.MaxValue || layers.Count > ushort.MaxValue)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                location.AtLocal(DataField.Count,
                    baseGlyphs.Count > ushort.MaxValue ? 2UL : 12UL), out diagnostic,
                (ulong)(baseGlyphs.Count > ushort.MaxValue ? baseGlyphs.Count : layers.Count),
                (ulong)ushort.MaxValue);
        // The canonical plan's offsets: layer records first (the majority order),
        // base records after - the model's edit diagnostics carry these
        // coordinates until the compiled wire re-roots them.
        uint layerRecordsOffset = ColrLayout.HeaderLength;
        uint baseGlyphRecordsOffset = layerRecordsOffset + (uint)(4L * layers.Count);
        ReadStatus status = ValidateRecords(baseGlyphs, layers, numGlyphs,
            cpalNumPaletteEntries, baseGlyphRecordsOffset, layerRecordsOffset, location,
            budget, out diagnostic);
        if (status != ReadStatus.Done) return status;
        long consumed = ColrLayout.HeaderLength + 4L * layers.Count + 6L * baseGlyphs.Count;
        model = new([.. baseGlyphs], [.. layers], numGlyphs, baseGlyphRecordsOffset,
            layerRecordsOffset, location, (int)consumed);
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>The v0 record rule set shared by both root factories (the v1 table's
    /// compatibility arrays answer the identical rules under the v1 zero grace): the
    /// strict glyphID ascent, both glyph domains against the caller's numGlyphs, the
    /// wide slice relation and the cross-table paletteIndex domain (null skips it,
    /// the read side's OQ-T6-01-4 line). The offsets are the model's own diagnostic
    /// coordinates.</summary>
    internal static ReadStatus ValidateRecords(
        IReadOnlyList<ColrBaseGlyphRecord> baseGlyphs, IReadOnlyList<ColrLayerRecord> layers,
        int numGlyphs, int? cpalNumPaletteEntries, uint baseGlyphRecordsOffset,
        uint layerRecordsOffset, DataLocation location, BuildBudget budget,
        out FontDiagnostic diagnostic)
    {
        if (!budget.Work(6L * (baseGlyphs.Count + layers.Count), location, out diagnostic))
            return ReadStatus.ResourceLimit;
        int previous = -1;
        for (int i = 0; i < baseGlyphs.Count; i++)
        {
            var row = baseGlyphs[i];
            var at = location.AtLocal(DataField.GlyphId,
                baseGlyphRecordsOffset + (ulong)i * ColrLayout.BaseGlyphRecordSize, i);
            if (row.GlyphId >= (ushort)numGlyphs)
                return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                    at, out diagnostic, row.GlyphId, (ulong)numGlyphs);
            if (i > 0 && row.GlyphId <= previous)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    at, out diagnostic, row.GlyphId, unchecked((ulong)previous));
            previous = row.GlyphId;
            if ((uint)row.FirstLayerIndex + (uint)row.NumLayers > (uint)layers.Count)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    location.AtLocal(DataField.Payload,
                        baseGlyphRecordsOffset + (ulong)i * ColrLayout.BaseGlyphRecordSize + 4, i),
                    out diagnostic, (uint)row.FirstLayerIndex + (uint)row.NumLayers, (uint)layers.Count);
        }
        for (int i = 0; i < layers.Count; i++)
        {
            var row = layers[i];
            var at = location.AtLocal(DataField.GlyphId,
                layerRecordsOffset + (ulong)i * ColrLayout.LayerRecordSize, i);
            if (row.GlyphId >= (ushort)numGlyphs)
                return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                    at, out diagnostic, row.GlyphId, (ulong)numGlyphs);
            if (cpalNumPaletteEntries is int entries &&
                row.PaletteIndex != ColrLayout.ForegroundPaletteIndex && row.PaletteIndex >= (uint)entries)
                return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                    location.AtLocal(DataField.Payload,
                        layerRecordsOffset + (ulong)i * ColrLayout.LayerRecordSize + 2, i),
                    out diagnostic, row.PaletteIndex, unchecked((ulong)(long)entries));
        }
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>Deterministic write through the same planned emission; every
    /// budget admission completes before any byte lands, and a short destination
    /// leaves the span untouched (the T5-05 atomicity discipline).</summary>
    public static ReadStatus TryWrite(ColrTable model, Span<byte> destination,
        out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        ReadStatus status = TryCompile(model, out byte[]? encoded, out diagnostic, limits);
        if (status != ReadStatus.Done) return status;
        if (destination.Length < encoded!.Length)
            return ReadWire.Fail(ReadStatus.DestinationTooSmall, DiagnosticCode.DestinationTooSmall,
                model.Location.At(DataField.OutputBytes, 0), out diagnostic,
                (ulong)destination.Length, (uint)encoded.Length);
        encoded.AsSpan().CopyTo(destination);
        diagnostic = default;
        return ReadStatus.Done;
    }

    public static ReadStatus TryCompile(ColrTable model, out byte[]? bytes,
        out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        bytes = null;
        ArgumentNullException.ThrowIfNull(model);
        // Version 1 emits through the canonical parents-first plan
        // (ColrTableCompilerV1); version 0 keeps the T6-01 majority-order plan.
        if (model.Version == 1)
            return TryCompileV1(model, out bytes, out diagnostic, limits);
        var budget = new BuildBudget(limits);
        // Defensive shape re-answers (unreachable for TryReadTable/TryCreate
        // models): the compiled plan must keep the read side's minimum shape.
        if (model.BaseGlyphCount == 0 || model.LayerCount == 0)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                model.Location.AtLocal(DataField.Count, model.BaseGlyphCount == 0 ? 2UL : 12UL),
                out diagnostic, 0UL, 1UL);
        if (model.BaseGlyphCount > ushort.MaxValue || model.LayerCount > ushort.MaxValue)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                model.Location.AtLocal(DataField.Count,
                    model.BaseGlyphCount > ushort.MaxValue ? 2UL : 12UL), out diagnostic,
                (ulong)(model.BaseGlyphCount > ushort.MaxValue
                    ? model.BaseGlyphCount : model.LayerCount), (ulong)ushort.MaxValue);
        long length = ColrLayout.HeaderLength + 4L * model.LayerCount + 6L * model.BaseGlyphCount;
        if (length > int.MaxValue)
            return ReadWire.Fail(ReadStatus.ResourceLimit, DiagnosticCode.BudgetExceeded,
                model.Location.AtLocal(DataField.Length, 0), out diagnostic,
                (ulong)length, (ulong)int.MaxValue);
        if (!budget.Work(length, model.Location, out diagnostic) ||
            !budget.Buffer(length, model.Location, out diagnostic) ||
            !budget.Output(length, model.Location, out diagnostic))
            return ReadStatus.ResourceLimit;
        var output = new byte[(int)length];
        // The canonical plan's own header words: a model read from a non-canonical
        // source order (the 11/52 base-first corpus tier) re-roots onto the
        // majority-order layout, so the declared offsets always address the
        // physical emissions below.
        uint planLayerRecordsOffset = ColrLayout.HeaderLength;
        uint planBaseGlyphRecordsOffset = planLayerRecordsOffset + (uint)(4L * model.LayerCount);
        // After the capacity preflight these generated writers cannot fail; a
        // failure is a writer contract bug and surfaces loudly.
        RequireWritten(ColrView.TryWriteVersion(output, model.Version, out _, model.Location));
        RequireWritten(ColrView.TryWriteNumBaseGlyphRecords(output, (ushort)model.BaseGlyphCount, out _, model.Location));
        RequireWritten(ColrView.TryWriteBaseGlyphRecordsOffset(output, planBaseGlyphRecordsOffset, out _, model.Location));
        RequireWritten(ColrView.TryWriteLayerRecordsOffset(output, planLayerRecordsOffset, out _, model.Location));
        RequireWritten(ColrView.TryWriteNumLayerRecords(output, (ushort)model.LayerCount, out _, model.Location));
        for (int i = 0; i < model.LayerCount; i++)
        {
            var row = model.Layers[i];
            RequireWritten(ColrView.TryWriteLayerRecordAt(output,
                ColrLayout.HeaderLength + (ulong)i * ColrLayout.LayerRecordSize,
                new ColrView.LayerRecordData(i, row.GlyphId, row.PaletteIndex), out _, model.Location));
        }
        uint baseAt = ColrLayout.HeaderLength + (uint)(4L * model.LayerCount);
        for (int i = 0; i < model.BaseGlyphCount; i++)
        {
            var row = model.BaseGlyphs[i];
            RequireWritten(ColrView.TryWriteBaseGlyphRecordAt(output,
                baseAt + (ulong)i * ColrLayout.BaseGlyphRecordSize,
                new ColrView.BaseGlyphRecordData(i, row.GlyphId, row.FirstLayerIndex, row.NumLayers),
                out _, model.Location));
        }
        budget.RecordCompiledTable(output.Length);
        bytes = output;
        diagnostic = default;
        return ReadStatus.Done;
    }

    private static void RequireWritten(ReadStatus status)
    {
        if (status != ReadStatus.Done) throw new InvalidOperationException("A preflighted colr write failed.");
    }
}
