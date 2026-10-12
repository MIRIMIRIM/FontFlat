using FontWeave.Core;
using FontWeave.Tables.Layout;

namespace FontWeave.Tables;

/// <summary>Canonical sbix compiler (card T6-04 §4, OQ-T6-04-15's single
/// candidate). One planned layout: header(8) → strikeOffsets (4·numStrikes) →
/// the strikes in input order, each = ppem/ppi (4B) → glyphDataOffsets
/// (4·(numGlyphs+1), strike-relative) → the glyph data packed in glyph order.
/// The empty-slot canonical form is the equal-consecutive offsets pair (the
/// corpus's 28/28 sole shape): a NULL-form source re-emits as the equal form
/// and lands in the wire-equivalent tier (OQ-T6-04-11's decided double track).
/// Every offset is a single forward emission (the strikeOffsets and per-strike
/// glyphDataOffsets fixups); glyph payloads travel byte-for-byte and are never
/// decoded. The numGlyphs authority re-answers against the strikes (every
/// strike carries exactly numGlyphs slots), the flags three-segment contract,
/// the graphicType closed set and the dupe three-layer semantics (OQ-T6-04-12/
/// -13). There are no child models, so the SameSource guard surface is empty
/// by construction; structural changes (strike/slot count changes, cross-strike
/// moves, flags or version writes) have no surface (OQ-T6-04-14).</summary>
public static class SbixTableCompiler
{
    /// <summary>Explicit root-model factory from owned parts (card §4.1): the
    /// root re-answers the version 1 domain, the flags contract, the numGlyphs
    /// authority across the strikes, the graphicType closed set and the dupe
    /// graph's three layers. Factory models carry the canonical plan's own
    /// coordinates, so edit diagnostics stay addressable before the first
    /// compile re-roots them (the COLR factory line).</summary>
    public static ReadStatus TryCreate(ushort version, ushort flags, int numGlyphs,
        IReadOnlyList<SbixStrike> strikes, out SbixTable? model, out FontDiagnostic diagnostic,
        BuildLimits? limits = null, DataLocation location = default)
    {
        model = null;
        ArgumentNullException.ThrowIfNull(strikes);
        location = location == default ? DataLocation.Anonymous(SbixLayout.Tag) : location;
        var budget = new BuildBudget(limits);
        ReadStatus status = CoverageTableCompiler.CheckNumGlyphs(numGlyphs, location, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (version != SbixLayout.Version1)
            return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedVersion,
                location.AtLocal(DataField.Version, 0), out diagnostic, version,
                SbixLayout.Version1);
        if ((flags & SbixLayout.FlagsBit0) == 0)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                location.AtLocal(DataField.Flags, 2), out diagnostic, flags, 1UL);
        if ((flags & SbixLayout.FlagsReservedMask) != 0)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                location.AtLocal(DataField.Flags, 2), out diagnostic,
                (ulong)(flags & SbixLayout.FlagsReservedMask), 0UL);
        if (!budget.Work(strikes.Count * (numGlyphs + 1L), location, out diagnostic))
            return ReadStatus.ResourceLimit;
        for (int s = 0; s < strikes.Count; s++)
        {
            var strike = strikes[s];
            ArgumentNullException.ThrowIfNull(strike);
            if (strike.GlyphCount != numGlyphs)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    location.AtLocal(DataField.Count, 4), out diagnostic,
                    (uint)strike.GlyphCount, (uint)numGlyphs);
            for (int gid = 0; gid < numGlyphs; gid++)
            {
                if (strike.Glyphs[gid] is not { } glyph) continue;
                if (!SbixLayout.IsGraphicTypeDefined(glyph.GraphicType))
                    return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedVersion,
                        location.AtLocal(DataField.Tag, 0, gid), out diagnostic,
                        glyph.GraphicType, 0UL);
                if (glyph.GraphicType == SbixLayout.GraphicTypeDupe)
                {
                    status = RequireUsableDupe(strike, numGlyphs, gid, glyph, location,
                        out diagnostic);
                    if (status != ReadStatus.Done) return status;
                }
            }
        }
        long length = PlanCanonicalLayout(strikes.Count, numGlyphs, strikes);
        if (!budget.Buffer(length, location, out diagnostic)) return ReadStatus.ResourceLimit;
        model = new(version, flags, numGlyphs, [.. strikes], location, (int)length);
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>Deterministic write through the same planned emission; every
    /// budget admission completes before any byte lands, and a short
    /// destination leaves the span untouched (the T5-05 atomicity
    /// discipline).</summary>
    public static ReadStatus TryWrite(SbixTable model, Span<byte> destination,
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

    public static ReadStatus TryCompile(SbixTable model, out byte[]? bytes,
        out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        bytes = null;
        ArgumentNullException.ThrowIfNull(model);
        var budget = new BuildBudget(limits);
        // The compile-time re-answers (a hand-built model cannot smuggle a
        // refused shape past the emission).
        ReadStatus status = CoverageTableCompiler.CheckNumGlyphs(model.NumGlyphs,
            model.Location, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (model.Version != SbixLayout.Version1)
            return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedVersion,
                model.Location.AtLocal(DataField.Version, 0), out diagnostic, model.Version,
                SbixLayout.Version1);
        if ((model.Flags & SbixLayout.FlagsBit0) == 0)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                model.Location.AtLocal(DataField.Flags, 2), out diagnostic, model.Flags, 1UL);
        if ((model.Flags & SbixLayout.FlagsReservedMask) != 0)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                model.Location.AtLocal(DataField.Flags, 2), out diagnostic,
                (ulong)(model.Flags & SbixLayout.FlagsReservedMask), 0UL);
        long length = PlanCanonicalLayout(model.StrikeCount, model.NumGlyphs, model.Strikes);
        if (!budget.Work(length, model.Location, out diagnostic) ||
            !budget.Buffer(length, model.Location, out diagnostic) ||
            !budget.Output(length, model.Location, out diagnostic))
            return ReadStatus.ResourceLimit;
        foreach (var strike in model.Strikes)
        {
            if (strike.GlyphCount != model.NumGlyphs)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    model.Location.AtLocal(DataField.Count, 4), out diagnostic,
                    (uint)strike.GlyphCount, (uint)model.NumGlyphs);
        }
        var output = new byte[(int)length];
        // The generated header and strikeOffsets writers cannot fail after the
        // capacity preflight; a failure is a writer contract bug and surfaces
        // loudly.
        RequireWritten(SbixView.TryWriteVersion(output, model.Version, out _, model.Location));
        RequireWritten(SbixView.TryWriteFlags(output, model.Flags, out _, model.Location));
        RequireWritten(SbixView.TryWriteNumStrikes(output, (uint)model.StrikeCount,
            out _, model.Location));
        uint strikeCursor = (uint)(SbixLayout.StrikeOffsetsAt +
            4L * model.StrikeCount);
        for (int s = 0; s < model.StrikeCount; s++)
        {
            var strike = model.Strikes[s];
            RequireWritten(SbixView.TryWriteStrikeOffsets(output, (ulong)model.StrikeCount,
                new SbixView.StrikeOffsetsData(s, strikeCursor), out _, model.Location));
            int at = (int)strikeCursor;
            System.Buffers.Binary.BinaryPrimitives.WriteUInt16BigEndian(
                output.AsSpan(at), strike.Ppem);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt16BigEndian(
                output.AsSpan(at + 2), strike.Ppi);
            // The glyph data follows the offsets array; empty slots re-emit in
            // the canonical equal form (offsets stay equal and non-zero).
            int arrayAt = at + SbixLayout.StrikeHeaderLength;
            int dataAt = arrayAt + 4 * (model.NumGlyphs + 1);
            uint running = (uint)(SbixLayout.StrikeHeaderLength +
                4 * (model.NumGlyphs + 1));
            for (int gid = 0; gid < model.NumGlyphs; gid++)
            {
                var glyph = strike.Glyphs[gid];
                System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(
                    output.AsSpan(arrayAt + 4 * gid), running);
                if (glyph is not { } value) continue;
                System.Buffers.Binary.BinaryPrimitives.WriteInt16BigEndian(
                    output.AsSpan(dataAt), value.OriginOffsetX);
                System.Buffers.Binary.BinaryPrimitives.WriteInt16BigEndian(
                    output.AsSpan(dataAt + 2), value.OriginOffsetY);
                System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(
                    output.AsSpan(dataAt + 4), value.GraphicType);
                value.Data.AsSpan().CopyTo(output.AsSpan(dataAt + SbixLayout.GlyphDataHeaderLength));
                running += (uint)(SbixLayout.GlyphDataHeaderLength + value.Data.Length);
                dataAt += SbixLayout.GlyphDataHeaderLength + value.Data.Length;
            }
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(
                output.AsSpan(arrayAt + 4 * model.NumGlyphs), running);
            strikeCursor += (uint)(SbixLayout.StrikeHeaderLength +
                4 * (model.NumGlyphs + 1) + (dataAt - (arrayAt + 4 * (model.NumGlyphs + 1))));
        }
        budget.RecordCompiledTable(output.Length);
        bytes = output;
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>The canonical layout's single forward plan: header(8) →
    /// strikeOffsets → the strikes packed in input order, each = head(4) →
    /// offsets(4·(numGlyphs+1)) → the non-empty slots' bytes.</summary>
    private static long PlanCanonicalLayout(int strikeCount, int numGlyphs,
        IReadOnlyList<SbixStrike> strikes)
    {
        long length = SbixLayout.StrikeOffsetsAt + 4L * strikeCount;
        foreach (var strike in strikes)
        {
            length += SbixLayout.StrikeHeaderLength + 4L * (numGlyphs + 1);
            for (int gid = 0; gid < numGlyphs; gid++)
                if (strike.Glyphs[gid] is { } glyph)
                    length += SbixLayout.GlyphDataHeaderLength + glyph.Data.Length;
        }
        return length;
    }

    /// <summary>The dupe three layers against the factory parts (OQ-T6-04-13):
    /// a payload shorter than the u16 BE target, an out-of-domain target and an
    /// empty target refuse; the chain walk refuses a revisit as a cycle.</summary>
    private static ReadStatus RequireUsableDupe(SbixStrike strike, int numGlyphs,
        int gid, SbixGlyph glyph, DataLocation location, out FontDiagnostic diagnostic)
    {
        if (glyph.Data.Length < 2)
            return ReadWire.Fail(ReadStatus.Truncated, DiagnosticCode.TruncatedData,
                location.AtLocal(DataField.Payload, 0, gid), out diagnostic,
                (ulong)glyph.Data.Length, 2UL);
        int target = global::System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(glyph.Data);
        if (target >= numGlyphs)
            return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                location.AtLocal(DataField.GlyphId, 0, gid), out diagnostic,
                (uint)target, (uint)numGlyphs);
        var targetGlyph = strike.Glyphs[target];
        if (targetGlyph is null)
            return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                location.AtLocal(DataField.GlyphId, 0, gid), out diagnostic,
                (uint)target, 0UL);
        if (target == gid)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                location.AtLocal(DataField.GlyphId, 0, gid), out diagnostic,
                (uint)target, 0UL);
        // The chain walk from the new dupe: a revisit is a cycle.
        var visited = new HashSet<int> { gid };
        int current = target;
        while (true)
        {
            var slot = strike.Glyphs[current];
            if (slot is not { } value || value.GraphicType != SbixLayout.GraphicTypeDupe)
                break;
            if (value.Data.Length < 2)
                return ReadWire.Fail(ReadStatus.Truncated, DiagnosticCode.TruncatedData,
                    location.AtLocal(DataField.Payload, 0, current), out diagnostic,
                    (ulong)value.Data.Length, 2UL);
            int next = global::System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(value.Data);
            if (next >= numGlyphs)
                return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                    location.AtLocal(DataField.GlyphId, 0, current), out diagnostic,
                    (uint)next, (uint)numGlyphs);
            if (strike.Glyphs[next] is null)
                return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                    location.AtLocal(DataField.GlyphId, 0, current), out diagnostic,
                    (uint)next, 0UL);
            if (visited.Contains(next))
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    location.AtLocal(DataField.GlyphId, 0, current), out diagnostic,
                    (uint)next, 0UL);
            visited.Add(next);
            current = next;
        }
        diagnostic = default;
        return ReadStatus.Done;
    }

    private static void RequireWritten(ReadStatus status)
    {
        if (status != ReadStatus.Done)
            throw new InvalidOperationException("A preflighted sbix write failed.");
    }
}
