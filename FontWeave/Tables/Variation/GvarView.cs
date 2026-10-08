using FontWeave.Core;
using FontWeave.Tables.Layout;

namespace FontWeave.Tables.Variation;

/// <summary>The whole-table read/validate face of gvar 1.0 (T5-03). The version
/// gate and the eight header scalars are declared; this partial owns everything
/// past the header - the flags-conditional offsets array walk (short form /2
/// decode), the forward-order checks (shared tuples follow the offsets array,
/// the GlyphVariationData array follows the shared tuples), the shared tuple
/// extent and the per-glyph GlyphVariationData walk through the shared store
/// reader. axisCount and numGlyphs are explicit inputs - the T5-02/T4 GID-domain
/// discipline: gvar's header counts must equal them (double authority, card 0
/// fix 4); the glyf point-count topology is only needed at materialization
/// (OQ-1 lean), so validation stays glyf-free.</summary>
public readonly ref partial struct GvarView
{
    public ReadStatus ValidateTable(int fvarAxisCount, int numGlyphs,
        out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        var budget = new BuildBudget(limits);
        if (!budget.Input(_data.Length, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        return ValidateTable(fvarAxisCount, numGlyphs, out diagnostic, budget);
    }

    internal ReadStatus ValidateTable(int fvarAxisCount, int numGlyphs,
        out FontDiagnostic diagnostic, BuildBudget budget)
    {
        ReadStatus status = TryRequireKnownVersion(out _, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = CoverageTableCompiler.CheckNumGlyphs(numGlyphs, Location, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (fvarAxisCount < 0)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.AtLocal(DataField.Count, 0), out diagnostic, (ulong)fvarAxisCount, 0);
        status = TryGetAxisCount(out ushort axisCount, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (axisCount != fvarAxisCount)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.AtLocal(DataField.Count, GvarLayout.AxisCountAt), out diagnostic,
                axisCount, (ulong)fvarAxisCount);
        status = TryGetGlyphCount(out ushort glyphCount, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (glyphCount != numGlyphs)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.AtLocal(DataField.Count, GvarLayout.GlyphCountAt), out diagnostic,
                glyphCount, (ulong)numGlyphs);
        status = TryGetFlags(out ushort flags, out diagnostic);
        if (status != ReadStatus.Done) return status;
        bool longForm = (flags & GvarLayout.LongOffsetsFlag) != 0;
        status = TryGetSharedTupleCount(out ushort sharedTupleCount, out diagnostic);
        if (status != ReadStatus.Done) return status;
        // The offsets array: glyphCount+1 entries right after the header, element
        // width by flags bit0, short-form values doubled (spec gvar.md L81-83).
        int width = longForm ? 4 : 2;
        long offsetsEnd = (long)GvarLayout.HeaderLength + (glyphCount + 1) * (long)width;
        if (offsetsEnd > _data.Length)
            return ReadWire.Fail(ReadStatus.Truncated, DiagnosticCode.TruncatedData,
                Location.AtLocal(DataField.Offset, GvarLayout.HeaderLength), out diagnostic,
                (ulong)_data.Length, (ulong)offsetsEnd);
        if (!budget.Work(glyphCount + 1, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        var offsets = new long[glyphCount + 1];
        int at = GvarLayout.HeaderLength;
        for (int i = 0; i <= glyphCount; i++)
        {
            offsets[i] = longForm
                ? System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(
                    _data.Slice(at, 4))
                : (long)System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(
                    _data.Slice(at, 2)) * 2;
            at += width;
            // Non-monotonic neighbors are a negative block size - the T0
            // "glyph offset non-monotonic" refusal (OQ-5: equal neighbors are the
            // legal absent form; no 0xFFFFFFFF sentinel exists, card 0 fix 3).
            if (i > 0 && offsets[i] < offsets[i - 1])
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    Location.AtLocal(DataField.Offset, (ulong)(GvarLayout.HeaderLength + i * width)),
                    out diagnostic, (ulong)offsets[i], (ulong)offsets[i - 1]);
        }
        // Forward order: the shared tuples follow the offsets array (spec L87,
        // 33/33 frozen hosts identity-satisfy); the GlyphVariationData array
        // follows the shared tuples.
        status = TryGetSharedTuplesOffset(out uint sharedTuplesOffset, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (sharedTuplesOffset < offsetsEnd)
            return ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.RangeOutsideSource,
                Location.AtLocal(DataField.Offset, GvarLayout.SharedTuplesOffsetAt),
                out diagnostic, sharedTuplesOffset, (ulong)offsetsEnd);
        long sharedTuplesEnd = (long)sharedTuplesOffset + (long)sharedTupleCount * axisCount * 2;
        if (sharedTuplesEnd > _data.Length)
            return ReadWire.Fail(ReadStatus.Truncated, DiagnosticCode.TruncatedData,
                Location.AtLocal(DataField.Count, GvarLayout.SharedTupleCountAt), out diagnostic,
                (ulong)_data.Length, (ulong)sharedTuplesEnd);
        if (!budget.Work((long)sharedTupleCount * axisCount, Location, out diagnostic))
            return ReadStatus.ResourceLimit;
        status = TryGetGlyphVariationDataArrayOffset(out uint gvdArrayOffset, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (gvdArrayOffset < sharedTuplesEnd)
            return ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.RangeOutsideSource,
                Location.AtLocal(DataField.Offset, GvarLayout.GlyphVariationDataArrayOffsetAt),
                out diagnostic, gvdArrayOffset, (ulong)sharedTuplesEnd);
        long gvdEnd = (long)gvdArrayOffset + offsets[glyphCount];
        if (gvdEnd > _data.Length)
            return ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.RangeOutsideSource,
                Location.AtLocal(DataField.Offset, GvarLayout.GlyphVariationDataArrayOffsetAt),
                out diagnostic, (ulong)gvdEnd, (ulong)_data.Length);
        // Per-glyph blocks: absent glyphs (size 0) are the legal positive
        // (OQ-5); every present block walks through the shared store reader in
        // the glyf-free structural pass (all-points tuples get the window
        // checks only - their delta runs need the point-count context, OQ-1).
        for (int glyphId = 0; glyphId < glyphCount; glyphId++)
        {
            int size = checked((int)(offsets[glyphId + 1] - offsets[glyphId]));
            if (size == 0) continue;
            int blockStart = checked((int)(gvdArrayOffset + offsets[glyphId]));
            if (!budget.Work(1, Location, out diagnostic)) return ReadStatus.ResourceLimit;
            status = TupleVariationStoreReader.ReadStore(_data, blockStart, blockStart + size,
                blockStart, isCvar: false, axisCount, sharedTupleCount, allPointsDeltaCount: -1,
                cvtValueCount: -1, materialize: false, tuplesOut: null, location: Location,
                budget: budget, tvcRaw: out _, sharedPointNumbers: out _, serializedEnd: out _,
                diagnostic: out diagnostic);
            if (status != ReadStatus.Done) return status;
        }
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>Reads the table into an owned <see cref="GvarTable"/>: the whole
    /// validation pass, then the offsets/shared-tuple materialization and the
    /// table byte copy that backs the lazy per-glyph reads (card 2 - the tuple
    /// objects stay unmaterialized; the 374,004-byte witness is carried by the
    /// budget gate, not by eager tuple objects).</summary>
    public ReadStatus TryReadTable(int fvarAxisCount, int numGlyphs, out GvarTable? model,
        out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        model = null;
        var budget = new BuildBudget(limits);
        if (!budget.Input(_data.Length, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        return TryReadTable(fvarAxisCount, numGlyphs, out model, out diagnostic, budget);
    }

    internal ReadStatus TryReadTable(int fvarAxisCount, int numGlyphs, out GvarTable? model,
        out FontDiagnostic diagnostic, BuildBudget budget)
    {
        model = null;
        ReadStatus status = ValidateTable(fvarAxisCount, numGlyphs, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        status = TryGetAxisCount(out ushort axisCount, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = TryGetGlyphCount(out ushort glyphCount, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = TryGetFlags(out ushort flags, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = TryGetSharedTupleCount(out ushort sharedTupleCount, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = TryGetGlyphVariationDataArrayOffset(out uint gvdArrayOffset, out diagnostic);
        if (status != ReadStatus.Done) return status;
        bool longForm = (flags & GvarLayout.LongOffsetsFlag) != 0;
        int width = longForm ? 4 : 2;
        var offsets = new int[glyphCount + 1];
        int at = GvarLayout.HeaderLength;
        for (int i = 0; i <= glyphCount; i++)
        {
            offsets[i] = checked((int)(longForm
                ? System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(_data.Slice(at, 4))
                : (long)System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(
                    _data.Slice(at, 2)) * 2));
            at += width;
        }
        var sharedTuples = new short[sharedTupleCount][];
        status = TryGetSharedTuplesOffset(out uint sharedTuplesOffset, out diagnostic);
        if (status != ReadStatus.Done) return status;
        for (int t = 0; t < sharedTupleCount; t++)
        {
            var tuple = new short[axisCount];
            for (int c = 0; c < axisCount; c++)
                tuple[c] = System.Buffers.Binary.BinaryPrimitives.ReadInt16BigEndian(
                    _data.Slice((int)sharedTuplesOffset + t * axisCount * 2 + c * 2, 2));
            sharedTuples[t] = tuple;
        }
        if (!budget.Buffer((long)_data.Length, Location, out diagnostic))
            return ReadStatus.ResourceLimit;
        model = new GvarTable(axisCount, glyphCount, flags, sharedTuples, offsets,
            checked((int)gvdArrayOffset), _data.ToArray(), Location, _data.Length);
        diagnostic = default;
        return ReadStatus.Done;
    }
}
