using System.Buffers.Binary;
using FontWeave.Core;
using FontWeave.Tables.Layout;

namespace FontWeave.Tables.Variation;

/// <summary>The gvar root writer (HvarTableCompiler shape): one planned, atomically
/// published whole-table emission. The canonical layout is the spec's narrative
/// order - header(20), the offsets array, the shared tuples, then the
/// GlyphVariationData array in glyph ID order with absent glyphs as zero-size
/// blocks; inside a block the shared point numbers precede the per-tuple runs.
/// The offsets form (flags bit0) is preserved from the model (OQ-7) - no form
/// upgrade write exists; short-form blocks are padded to the even /2 stride and the
/// offsets array is re-derived over that padded layout (an edit can produce an odd
/// block), long-form blocks carry no pad. Unedited blocks re-emit their source bytes
/// verbatim
/// (edited tuples splice in place, so the model's bytes are the truth) and
/// parts-built blocks encode deterministically at TryCreate. Every fixup, count
/// and budget check completes before any byte is written; a refused plan never
/// touches the destination.</summary>
public static class GvarTableCompiler
{
    /// <summary>Explicit parts factory for the synthetic compile path (T4-02
    /// precedent): the two authorities are re-answered here - every shared tuple
    /// vector must carry exactly <paramref name="axisCount"/> raw F2DOT14 values,
    /// every embedded peak and intermediate vector likewise, shared tuple
    /// indices stay inside the shared set, and the delta columns must agree with
    /// the point sets (one delta per point in each of gvar's X and Y columns).
    /// The blocks' locations must share the root location's source (SameSource);
    /// a null block is the absent-glyph form.</summary>
    public static ReadStatus TryCreate(int axisCount, int numGlyphs, ushort flags,
        IReadOnlyList<short[]> sharedTuples, IReadOnlyList<GlyphVariationDataTable?> glyphData,
        out GvarTable? model, out FontDiagnostic diagnostic, BuildLimits? limits = null,
        DataLocation location = default)
    {
        model = null;
        ArgumentNullException.ThrowIfNull(sharedTuples);
        ArgumentNullException.ThrowIfNull(glyphData);
        var budget = new BuildBudget(limits);
        location = location == default ? DataLocation.Anonymous(GvarLayout.Tag) : location;
        if (axisCount < 0)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                location.AtLocal(DataField.Count, GvarLayout.AxisCountAt), out diagnostic,
                (ulong)axisCount, 0);
        ReadStatus status = CoverageTableCompiler.CheckNumGlyphs(numGlyphs, location,
            out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (glyphData.Count != numGlyphs)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                location.AtLocal(DataField.Count, GvarLayout.GlyphCountAt), out diagnostic,
                (ulong)glyphData.Count, (ulong)numGlyphs);
        if (sharedTuples.Count > ushort.MaxValue)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                location.AtLocal(DataField.Count, GvarLayout.SharedTupleCountAt), out diagnostic,
                (ulong)sharedTuples.Count, (ulong)ushort.MaxValue);
        foreach (var tuple in sharedTuples)
            if (tuple.Length != axisCount)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    location.AtLocal(DataField.Count, GvarLayout.SharedTupleCountAt),
                    out diagnostic, (ulong)tuple.Length, (ulong)axisCount);
        if (!budget.Work((long)sharedTuples.Count * axisCount + numGlyphs, location,
                out diagnostic)) return ReadStatus.ResourceLimit;
        foreach (var block in glyphData)
        {
            if (block is null) continue;
            if (!SameSource(location, block.Location))
                return ReadWire.Fail(ReadStatus.InvalidSource, DiagnosticCode.WrongSourceFace,
                    location.AtLocal(DataField.None, 0), out diagnostic);
            status = ValidateBlock(block, axisCount, sharedTuples.Count, out diagnostic);
            if (status != ReadStatus.Done) return status;
        }
        // Encode every present block into canonical bytes; the model keeps them
        // as the emission truth (the read-built model keeps its source bytes).
        bool longForm = (flags & GvarLayout.LongOffsetsFlag) != 0;
        var offsets = new int[numGlyphs + 1];
        var blocks = new byte[numGlyphs][];
        int gvdTotal = 0;
        for (int glyphId = 0; glyphId < numGlyphs; glyphId++)
        {
            var block = glyphData[glyphId];
            if (block is null)
            {
                blocks[glyphId] = Array.Empty<byte>();
            }
            else
            {
                status = EncodeBlock(block, axisCount, longForm, budget, out blocks[glyphId],
                    out diagnostic);
                if (status != ReadStatus.Done) return status;
            }
            gvdTotal += blocks[glyphId].Length;
            offsets[glyphId + 1] = gvdTotal;
        }
        // The offsets form is preserved here too: a parts-built table whose
        // blocks overflow the source form refuses before assembly (the same
        // width-class guard the compile plan applies).
        if (!longForm && gvdTotal / 2 > ushort.MaxValue)
            return ReadWire.Fail(ReadStatus.UnsupportedFormat,
                DiagnosticCode.UnsupportedOperation,
                location.AtLocal(DataField.Offset, GvarLayout.HeaderLength), out diagnostic,
                (ulong)gvdTotal, (ulong)(ushort.MaxValue * 2));
        var bytes = Assemble(axisCount, numGlyphs, flags, sharedTuples, offsets, blocks);
        if (!budget.Buffer((long)bytes.Length, location, out diagnostic))
            return ReadStatus.ResourceLimit;
        model = new GvarTable(axisCount, numGlyphs, flags, sharedTuples, offsets,
            HeaderEnd(numGlyphs, longForm) + sharedTuples.Count * axisCount * 2,
            bytes, location, bytes.Length);
        diagnostic = default;
        return ReadStatus.Done;
    }

    private static bool SameSource(DataLocation location, DataLocation child) =>
        location.SourceId == child.SourceId && location.FaceIndex == child.FaceIndex &&
        (location.SourceId == 0 || location.TableTag == child.TableTag);

    private static int HeaderEnd(int numGlyphs, bool longForm) =>
        GvarLayout.HeaderLength + (numGlyphs + 1) * (longForm ? 4 : 2);

    // The per-block root checks: reserved bits, the embedded/shared
    // classification with the shared index domain, the coordinate vector widths
    // and the delta-column agreement (the OQ-7 relation set).
    private static ReadStatus ValidateBlock(GlyphVariationDataTable block, int axisCount,
        int sharedTupleCount, out FontDiagnostic diagnostic)
    {
        foreach (var tuple in block.Tuples)
        {
            if ((tuple.TupleIndex & TupleVariationCodec.TupleIndexReservedMask) != 0)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    block.Location.AtLocal(DataField.Flags, 0), out diagnostic,
                    (ulong)(tuple.TupleIndex & TupleVariationCodec.TupleIndexReservedMask), 0);
            if ((tuple.TupleIndex & TupleVariationCodec.EmbeddedPeakFlag) == 0)
            {
                if (tuple.SharedTupleIndex >= sharedTupleCount)
                    return ReadWire.Fail(ReadStatus.InvalidIndex,
                        DiagnosticCode.IndexOutOfRange, block.Location.AtLocal(DataField.Flags, 0),
                        out diagnostic, tuple.SharedTupleIndex, (ulong)sharedTupleCount);
            }
            else if (tuple.PeakCoords is null || tuple.PeakCoords.Length != axisCount)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    block.Location.AtLocal(DataField.Count, 0), out diagnostic,
                    (ulong)(tuple.PeakCoords?.Length ?? 0), (ulong)axisCount);
            if (tuple.HasIntermediate && (tuple.IntermediateStartCoords is null ||
                    tuple.IntermediateEndCoords is null ||
                    tuple.IntermediateStartCoords.Length != axisCount ||
                    tuple.IntermediateEndCoords.Length != axisCount))
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    block.Location.AtLocal(DataField.Count, 0), out diagnostic, 0,
                    (ulong)axisCount);
            // The gvar columns: X then Y, once per point number each (spec gvar
            // L245). Explicit point sets pin the count; all-points tuples carry
            // the caller's topology at read-back, so only the column agreement
            // is checkable here.
            if (tuple.XDeltas.Count != tuple.YDeltas.Count)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    block.Location.AtLocal(DataField.Count, 0), out diagnostic,
                    (ulong)tuple.YDeltas.Count, (ulong)tuple.XDeltas.Count);
            if (tuple.PointNumbers is { Count: > 0 } &&
                tuple.XDeltas.Count != tuple.PointNumbers.Count)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    block.Location.AtLocal(DataField.Count, 0), out diagnostic,
                    (ulong)tuple.XDeltas.Count, (ulong)tuple.PointNumbers.Count);
        }
        diagnostic = default;
        return ReadStatus.Done;
    }

    // Encodes one block: [tvc][dataOffset][TVH array][shared points][runs], with
    // the short-form even pad at the block end (the /2 offsets stay integral).
    private static ReadStatus EncodeBlock(GlyphVariationDataTable block, int axisCount,
        bool longForm, BuildBudget budget, out byte[] bytes, out FontDiagnostic diagnostic)
    {
        bytes = Array.Empty<byte>();
        // Shared point number data first (empty list = the count==0 marker).
        int sharedLength = block.SharedPointNumbers is null ? 0 :
            block.SharedPointNumbers.Count == 0 ? 1 :
            TupleVariationCodec.MeasurePointNumberValues(block.SharedPointNumbers.ToArray());
        int headersLength = 4;
        foreach (var tuple in block.Tuples)
            headersLength += TupleVariationCodec.TupleVariationHeaderSize(tuple.TupleIndex,
                axisCount);
        long runLength = 0;
        foreach (var tuple in block.Tuples)
        {
            runLength += MeasureRun(tuple);
            if (runLength > ushort.MaxValue)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    block.Location.AtLocal(DataField.Length, 0), out diagnostic,
                    (ulong)runLength, (ulong)ushort.MaxValue);
        }
        // dataOffset points at the serialized data start - the shared point block when
        // present - not past it; the shared bytes are part of the block length only.
        int dataOffset = headersLength;
        int blockLength = dataOffset + sharedLength + (int)runLength;
        if (dataOffset > ushort.MaxValue)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                block.Location.AtLocal(DataField.Offset, 0), out diagnostic,
                (ulong)dataOffset, (ulong)ushort.MaxValue);
        if (!longForm && blockLength % 2 != 0) blockLength++;
        if (!budget.Buffer(blockLength, block.Location, out diagnostic))
            return ReadStatus.ResourceLimit;
        var buffer = new byte[blockLength];
        BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(0, 2), block.TupleVariationCountRaw);
        BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(2, 2), (ushort)dataOffset);
        int at = 4;
        foreach (var tuple in block.Tuples)
        {
            BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(at, 2),
                (ushort)MeasureRun(tuple));
            BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(at + 2, 2), tuple.TupleIndex);
            int payload = at + 4;
            if (tuple.PeakCoords is not null)
            {
                WriteCoords(buffer.AsSpan(payload), tuple.PeakCoords);
                payload += 2 * axisCount;
            }
            if (tuple.IntermediateStartCoords is not null)
            {
                WriteCoords(buffer.AsSpan(payload), tuple.IntermediateStartCoords);
                WriteCoords(buffer.AsSpan(payload + 2 * axisCount), tuple.IntermediateEndCoords!);
            }
            at += TupleVariationCodec.TupleVariationHeaderSize(tuple.TupleIndex, axisCount);
        }
        if (block.SharedPointNumbers is not null)
        {
            if (block.SharedPointNumbers.Count == 0)
            {
                buffer[at++] = 0;
            }
            else
            {
                var points = block.SharedPointNumbers.ToArray();
                TupleVariationCodec.WritePointNumberValues(buffer.AsSpan(at), points);
                at += TupleVariationCodec.MeasurePointNumberValues(points);
            }
        }
        foreach (var tuple in block.Tuples)
            at += WriteRun(tuple, buffer.AsSpan(at));
        bytes = buffer;
        diagnostic = default;
        return ReadStatus.Done;
    }

    private static int MeasureRun(TupleVariation tuple)
    {
        int pointBytes = 0;
        if (tuple.HasPrivatePoints)
            pointBytes = tuple.PointNumbers is null ? 1 :
                TupleVariationCodec.MeasurePointNumberValues((int[])tuple.PointNumbers);
        return pointBytes + TupleVariationCodec.MeasureDeltaRuns((short[])tuple.XDeltas) +
            TupleVariationCodec.MeasureDeltaRuns((short[])tuple.YDeltas);
    }

    private static int WriteRun(TupleVariation tuple, Span<byte> destination)
    {
        int at = 0;
        if (tuple.HasPrivatePoints)
        {
            if (tuple.PointNumbers is null)
            {
                destination[at++] = 0;
            }
            else
            {
                var points = (int[])tuple.PointNumbers;
                TupleVariationCodec.WritePointNumberValues(destination, points);
                at += TupleVariationCodec.MeasurePointNumberValues(points);
            }
        }
        var x = (short[])tuple.XDeltas;
        TupleVariationCodec.WriteDeltaRuns(x, destination.Slice(at));
        at += TupleVariationCodec.MeasureDeltaRuns(x);
        var y = (short[])tuple.YDeltas;
        if (y.Length > 0)
        {
            TupleVariationCodec.WriteDeltaRuns(y, destination.Slice(at));
            at += TupleVariationCodec.MeasureDeltaRuns(y);
        }
        return at;
    }

    private static void WriteCoords(Span<byte> destination, short[] coords)
    {
        for (int i = 0; i < coords.Length; i++)
            BinaryPrimitives.WriteInt16BigEndian(destination.Slice(i * 2, 2), coords[i]);
    }

    private static byte[] Assemble(int axisCount, int numGlyphs, ushort flags,
        IReadOnlyList<short[]> sharedTuples, int[] offsets, byte[][] blocks)
    {
        bool longForm = (flags & GvarLayout.LongOffsetsFlag) != 0;
        int width = longForm ? 4 : 2;
        int sharedTuplesOffset = HeaderEnd(numGlyphs, longForm);
        int gvdArrayOffset = sharedTuplesOffset + sharedTuples.Count * axisCount * 2;
        var bytes = new byte[gvdArrayOffset + offsets[^1]];
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(0, 4), GvarLayout.Version10);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(4, 2), (ushort)axisCount);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(6, 2), (ushort)sharedTuples.Count);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(8, 4), (uint)sharedTuplesOffset);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(12, 2), (ushort)numGlyphs);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(14, 2), flags);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(16, 4), (uint)gvdArrayOffset);
        int at = GvarLayout.HeaderLength;
        for (int i = 0; i <= numGlyphs; i++)
        {
            if (longForm)
                BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(at, 4), (uint)offsets[i]);
            else
                BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(at, 2),
                    (ushort)(offsets[i] / 2));
            at += width;
        }
        foreach (var tuple in sharedTuples)
        {
            WriteCoords(bytes.AsSpan(at), tuple);
            at += tuple.Length * 2;
        }
        foreach (var block in blocks)
        {
            block.CopyTo(bytes, at);
            at += block.Length;
        }
        return bytes;
    }

    /// <summary>Compiles the table into a fresh canonical byte array; the whole
    /// plan (lengths, fixups, budget admission) completes before allocation.</summary>
    public static ReadStatus TryCompile(GvarTable model, out byte[]? output,
        out FontDiagnostic diagnostic, BuildLimits? limits = null) =>
        TryCompile(model, out output, out diagnostic, new BuildBudget(limits));

    internal static ReadStatus TryCompile(GvarTable model, out byte[]? output,
        out FontDiagnostic diagnostic, BuildBudget budget)
    {
        ArgumentNullException.ThrowIfNull(model);
        output = null;
        ReadStatus status = TryPlan(model, out int length, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        if (!budget.Buffer(length, model.Location, out diagnostic)) return ReadStatus.ResourceLimit;
        var bytes = new byte[length];
        WritePlanned(model, bytes);
        budget.RecordCompiledTable(bytes.Length);
        output = bytes;
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>Writes the canonical emission into the destination; every plan
    /// admission completes before the capacity check (the destination is
    /// untouched on refusal).</summary>
    public static ReadStatus TryWrite(GvarTable model, Span<byte> destination,
        out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        var budget = new BuildBudget(limits);
        ReadStatus status = TryPlan(model, out int length, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        if (destination.Length < length)
            return ReadWire.Fail(ReadStatus.DestinationTooSmall,
                DiagnosticCode.DestinationTooSmall,
                model.Location.AtLocal(DataField.OutputBytes, 0), out diagnostic,
                (ulong)destination.Length, (ulong)length);
        WritePlanned(model, destination);
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>Wire length of the canonical emission; budget-free arithmetic for
    /// callers that only measure.</summary>
    internal static int Measure(GvarTable model)
    {
        bool longForm = model.LongOffsets;
        int width = longForm ? 4 : 2;
        int length = HeaderEnd(model.NumGlyphs, longForm) +
            model.SharedTuples.Count * model.AxisCount * 2;
        for (int glyphId = 0; glyphId < model.NumGlyphs; glyphId++)
        {
            int size = model.GlyphVariationDataOffsets[glyphId + 1] -
                model.GlyphVariationDataOffsets[glyphId];
            length += size;
            if (!longForm && size % 2 != 0) length++; // the short-form even pad
        }
        return length;
    }

    // The final emitted offset of the GlyphVariationData array: the cumulative
    // padded block layout, not the model's source offsets. A short-form source only
    // carries even blocks, but an edit can splice an odd run into one, so the emitted
    // offsets array must be re-derived from the sizes the writer actually lays down.
    private static int EmittedDataLength(GvarTable model)
    {
        bool longForm = model.LongOffsets;
        int length = 0;
        for (int glyphId = 0; glyphId < model.NumGlyphs; glyphId++)
        {
            int size = model.GlyphVariationDataOffsets[glyphId + 1] -
                model.GlyphVariationDataOffsets[glyphId];
            length += size;
            if (!longForm && size % 2 != 0) length++;
        }
        return length;
    }

    // The single canonical candidate: every fixup value and the whole budget
    // admission happen here, before any byte exists. The offsets form is
    // preserved (OQ-7): an offset that no longer fits the source form refuses
    // with UnsupportedOperation (the T5-01 width-class precedent).
    private static ReadStatus TryPlan(GvarTable model, out int length,
        out FontDiagnostic diagnostic, BuildBudget budget)
    {
        length = 0;
        bool longForm = model.LongOffsets;
        long last = EmittedDataLength(model);
        if (!longForm && last > (long)ushort.MaxValue * 2)
            return ReadWire.Fail(ReadStatus.UnsupportedFormat,
                DiagnosticCode.UnsupportedOperation,
                model.Location.AtLocal(DataField.Offset, GvarLayout.HeaderLength),
                out diagnostic, (ulong)last, (ulong)ushort.MaxValue * 2);
        if (longForm && last > int.MaxValue)
            return ReadWire.Fail(ReadStatus.UnsupportedFormat,
                DiagnosticCode.UnsupportedOperation,
                model.Location.AtLocal(DataField.Offset, GvarLayout.HeaderLength),
                out diagnostic, (ulong)last, int.MaxValue);
        int measured = Measure(model);
        if (!budget.Work(measured + model.NumGlyphs, model.Location, out diagnostic) ||
            !budget.Output(measured, model.Location, out diagnostic))
            return ReadStatus.ResourceLimit;
        length = measured;
        diagnostic = default;
        return ReadStatus.Done;
    }

    // Only a fully preflighted plan and destination reach this path. The header
    // fixups and the offsets array re-derive from the model's counts and block
    // sizes; the shared tuples re-encode (F2DOT14, byte-identical) and the
    // GlyphVariationData blocks copy from the model's bytes (edited tuples are
    // already spliced - the OQ-7 verbatim emission).
    private static void WritePlanned(GvarTable model, Span<byte> destination)
    {
        var location = model.Location;
        bool longForm = model.LongOffsets;
        int width = longForm ? 4 : 2;
        int sharedTuplesOffset = HeaderEnd(model.NumGlyphs, longForm);
        int gvdArrayOffset = sharedTuplesOffset + model.SharedTuples.Count * model.AxisCount * 2;
        CoverageTableCompiler.RequireWritten(GvarView.TryWriteVersion(destination,
            GvarLayout.Version10, out _, location));
        CoverageTableCompiler.RequireWritten(GvarView.TryWriteAxisCount(destination,
            (ushort)model.AxisCount, out _, location));
        CoverageTableCompiler.RequireWritten(GvarView.TryWriteSharedTupleCount(destination,
            (ushort)model.SharedTuples.Count, out _, location));
        CoverageTableCompiler.RequireWritten(GvarView.TryWriteSharedTuplesOffset(destination,
            (uint)sharedTuplesOffset, out _, location));
        CoverageTableCompiler.RequireWritten(GvarView.TryWriteGlyphCount(destination,
            (ushort)model.NumGlyphs, out _, location));
        CoverageTableCompiler.RequireWritten(GvarView.TryWriteFlags(destination, model.Flags,
            out _, location));
        CoverageTableCompiler.RequireWritten(
            GvarView.TryWriteGlyphVariationDataArrayOffset(destination, (uint)gvdArrayOffset,
                out _, location));
        int at = GvarLayout.HeaderLength;
        // The offsets array follows the padded block layout the loop below emits: a
        // short-form block pads to the even /2 stride, so the model's own (unpadded)
        // offsets would truncate an odd edit and shift every later glyph.
        int dataOffset = 0;
        for (int i = 0; i <= model.NumGlyphs; i++)
        {
            if (longForm)
                BinaryPrimitives.WriteUInt32BigEndian(destination.Slice(at, 4), (uint)dataOffset);
            else
                BinaryPrimitives.WriteUInt16BigEndian(destination.Slice(at, 2),
                    (ushort)(dataOffset / 2));
            at += width;
            if (i < model.NumGlyphs)
            {
                int size = model.GlyphVariationDataOffsets[i + 1] -
                    model.GlyphVariationDataOffsets[i];
                dataOffset += size;
                if (!longForm && size % 2 != 0) dataOffset++;
            }
        }
        foreach (var tuple in model.SharedTuples)
        {
            WriteCoords(destination.Slice(at), tuple);
            at += tuple.Length * 2;
        }
        var source = model.RawTableBytes;
        for (int glyphId = 0; glyphId < model.NumGlyphs; glyphId++)
        {
            int start = model.GvdArrayOffset + model.GlyphVariationDataOffsets[glyphId];
            int size = model.GlyphVariationDataOffsets[glyphId + 1] -
                model.GlyphVariationDataOffsets[glyphId];
            source.AsSpan(start, size).CopyTo(destination.Slice(at));
            at += size;
            if (!longForm && size % 2 != 0) destination[at++] = 0;
        }
    }
}
