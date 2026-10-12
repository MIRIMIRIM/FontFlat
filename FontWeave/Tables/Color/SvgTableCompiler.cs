using FontWeave.Core;

namespace FontWeave.Tables;

/// <summary>Canonical SVG compiler (card T6-04 §4, OQ-T6-04-15's single
/// candidate). One planned layout: header(10) → numEntries + the 12-byte
/// document records (input order, the ascent contract re-answered) → the
/// documents in first-occurrence record order, each distinct payload emitted
/// exactly once (the shared-group single emission). The corpus's 92/92 hosts
/// all carry docList immediately after the header and record-order documents,
/// so same-shape sources recompile byte-identical; other placements and
/// inter-region padding land in the wire-equivalent tier. Every offset is a
/// single forward emission (the DocumentListOffset and per-record svgDocOffset
/// fixups); documents and record rows travel byte-for-byte - gzip members stay
/// verbatim, never recompressed. There are no child models, so the SameSource
/// guard surface is empty by construction; structural changes (record/payload
/// count changes, reordering, version conversion) have no surface
/// (OQ-T6-04-14).</summary>
public static class SvgTableCompiler
{
    /// <summary>Explicit root-model factory from owned parts (card §4.1): the
    /// root re-answers the structural invariants the read side enforces - the
    /// version 0 domain, the reserved word, the non-empty record set, the
    /// payload-index domain, the V L84 range clauses in record order and the
    /// V L82 non-zero document lengths. Factory models carry the canonical
    /// plan's own coordinates, so edit diagnostics stay addressable before the
    /// first compile re-roots them (the COLR factory line).</summary>
    public static ReadStatus TryCreate(ushort version, uint reserved,
        IReadOnlyList<SvgDocumentRecord> records, IReadOnlyList<byte[]> documents,
        out SvgTable? model, out FontDiagnostic diagnostic, BuildLimits? limits = null,
        DataLocation location = default)
    {
        model = null;
        ArgumentNullException.ThrowIfNull(records);
        ArgumentNullException.ThrowIfNull(documents);
        location = location == default ? DataLocation.Anonymous(SvgLayout.Tag) : location;
        var budget = new BuildBudget(limits);
        if (version != SvgLayout.Version0)
            return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedVersion,
                location.AtLocal(DataField.Version, 0), out diagnostic, version);
        if (reserved != 0)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                location.AtLocal(DataField.Payload, SvgLayout.HeaderReservedAt), out diagnostic,
                reserved, 0UL);
        if (records.Count == 0)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                location.AtLocal(DataField.Count, SvgLayout.HeaderListOffsetAt), out diagnostic,
                0UL, 1UL);
        if (!budget.Work(records.Count + documents.Count + TotalDocumentBytes(documents),
                location, out diagnostic))
            return ReadStatus.ResourceLimit;
        foreach (var document in documents)
        {
            ArgumentNullException.ThrowIfNull(document);
            if (document.Length == 0)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    location.AtLocal(DataField.Length, 0), out diagnostic, 0UL, 1UL);
        }
        ushort previousEnd = 0;
        for (int i = 0; i < records.Count; i++)
        {
            var record = records[i];
            ulong at = SvgLayout.HeaderLength + 2UL + (ulong)i * SvgLayout.RecordStride;
            if ((uint)record.PayloadIndex >= (uint)documents.Count)
                return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                    location.AtLocal(DataField.Length, at + 8, i), out diagnostic,
                    unchecked((ulong)(long)record.PayloadIndex), (uint)documents.Count);
            if (record.StartGlyphID > record.EndGlyphID)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    location.AtLocal(DataField.GlyphId, at, i), out diagnostic,
                    record.StartGlyphID, record.EndGlyphID);
            if (i > 0 && record.StartGlyphID <= previousEnd)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    location.AtLocal(DataField.GlyphId, at, i), out diagnostic,
                    record.StartGlyphID, previousEnd);
            previousEnd = record.EndGlyphID;
        }
        long length = PlanCanonicalLayout(records.Count, documents);
        if (!budget.Buffer(length, location, out diagnostic)) return ReadStatus.ResourceLimit;
        model = new(version, reserved, SvgLayout.HeaderLength, [.. records],
            [.. documents], location, (int)length);
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>Deterministic write through the same planned emission; every
    /// budget admission completes before any byte lands, and a short
    /// destination leaves the span untouched (the T5-05 atomicity
    /// discipline).</summary>
    public static ReadStatus TryWrite(SvgTable model, Span<byte> destination,
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

    public static ReadStatus TryCompile(SvgTable model, out byte[]? bytes,
        out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        bytes = null;
        ArgumentNullException.ThrowIfNull(model);
        var budget = new BuildBudget(limits);
        // The compile-time re-answers: a hand-built model cannot smuggle a
        // refused shape past the emission (the factory refuses first; these
        // guard direct construction).
        if (model.Version != SvgLayout.Version0)
            return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedVersion,
                model.Location.AtLocal(DataField.Version, 0), out diagnostic, model.Version);
        if (model.Reserved != 0)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                model.Location.AtLocal(DataField.Payload, SvgLayout.HeaderReservedAt),
                out diagnostic, model.Reserved, 0UL);
        if (model.RecordCount == 0)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                model.Location.AtLocal(DataField.Count, SvgLayout.HeaderListOffsetAt),
                out diagnostic, 0UL, 1UL);
        long length = PlanCanonicalLayout(model.RecordCount, model.Documents);
        if (!budget.Work(length, model.Location, out diagnostic) ||
            !budget.Buffer(length, model.Location, out diagnostic) ||
            !budget.Output(length, model.Location, out diagnostic))
            return ReadStatus.ResourceLimit;
        ushort previousEnd = 0;
        for (int i = 0; i < model.RecordCount; i++)
        {
            var record = model.Records[i];
            ulong at = SvgLayout.HeaderLength + 2UL + (ulong)i * SvgLayout.RecordStride;
            if ((uint)record.PayloadIndex >= (uint)model.DocumentCount)
                return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                    model.Location.AtLocal(DataField.Length, at + 8, i), out diagnostic,
                    unchecked((ulong)(long)record.PayloadIndex), (uint)model.DocumentCount);
            if (model.Documents[record.PayloadIndex] is not { } document || document.Length == 0)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    model.Location.AtLocal(DataField.Length, at + 8, i), out diagnostic,
                    0UL, 1UL);
            if (record.StartGlyphID > record.EndGlyphID)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    model.Location.AtLocal(DataField.GlyphId, at, i), out diagnostic,
                    record.StartGlyphID, record.EndGlyphID);
            if (i > 0 && record.StartGlyphID <= previousEnd)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    model.Location.AtLocal(DataField.GlyphId, at, i), out diagnostic,
                    record.StartGlyphID, previousEnd);
            previousEnd = record.EndGlyphID;
        }
        var output = new byte[(int)length];
        // The generated header writers cannot fail after the capacity
        // preflight; a failure is a writer contract bug and surfaces loudly.
        RequireWritten(SvgView.TryWriteVersion(output, model.Version, out _, model.Location));
        RequireWritten(SvgView.TryWriteDocumentListOffset(output, SvgLayout.HeaderLength,
            out _, model.Location));
        RequireWritten(SvgView.TryWriteReserved(output, 0, out _, model.Location));
        // The document list is the partial's runtime-positioned face: the entry
        // count and the record rows have no generated writers.
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16BigEndian(
            output.AsSpan(SvgLayout.HeaderLength), (ushort)model.RecordCount);
        uint documentCursor = (uint)(SvgLayout.HeaderLength + 2 +
            (long)model.RecordCount * SvgLayout.RecordStride);
        var documentPositions = new uint[model.DocumentCount];
        for (int i = 0; i < model.RecordCount; i++)
        {
            var record = model.Records[i];
            int at = SvgLayout.HeaderLength + 2 + 12 * i;
            uint position = documentPositions[record.PayloadIndex];
            if (position == 0)
            {
                // First occurrence of this payload index: emit the document and
                // remember its list-relative position (0 is never a valid
                // emitted position because the list header occupies it).
                position = documentCursor - SvgLayout.HeaderLength;
                documentPositions[record.PayloadIndex] = position;
                model.Documents[record.PayloadIndex].AsSpan()
                    .CopyTo(output.AsSpan((int)documentCursor));
                documentCursor += (uint)model.Documents[record.PayloadIndex].Length;
            }
            System.Buffers.Binary.BinaryPrimitives.WriteUInt16BigEndian(
                output.AsSpan(at), record.StartGlyphID);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt16BigEndian(
                output.AsSpan(at + 2), record.EndGlyphID);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(
                output.AsSpan(at + 4), position);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(
                output.AsSpan(at + 8), (uint)model.Documents[record.PayloadIndex].Length);
        }
        budget.RecordCompiledTable(output.Length);
        bytes = output;
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>The canonical layout's single forward plan: header(10) →
    /// numEntries + records (12n) → the distinct documents in first-occurrence
    /// record order.</summary>
    private static long PlanCanonicalLayout(int recordCount, IReadOnlyList<byte[]> documents)
    {
        long length = SvgLayout.HeaderLength + 2L + (long)recordCount * SvgLayout.RecordStride;
        foreach (var document in documents)
            length += document.Length;
        return length;
    }

    private static long TotalDocumentBytes(IReadOnlyList<byte[]> documents)
    {
        long total = 0;
        foreach (var document in documents)
            total += document.Length;
        return total;
    }

    private static void RequireWritten(ReadStatus status)
    {
        if (status != ReadStatus.Done)
            throw new InvalidOperationException("A preflighted svg write failed.");
    }
}
