using FontWeave.Core;

namespace FontWeave.Tables.Layout;

/// <summary>The whole-table read/validate face of the kern root. The version gate,
/// the dialect count records and every opener are declared; this partial adds the
/// semantics the single views cannot see: the length-driven subtable walk (section 7's
/// keep - the per-subtable stride is data-dependent, so the sequence is semantic-layer
/// walking that opens the declared views over the exact length slices, the cmap root
/// precedent), the dialect/format dispatch (v0 coverage high byte, Apple low byte -
/// OQ-8), the OQ-1 declared-length contract and the hard/observed split of section 3.
/// numGlyphs is an explicit input with no default (the pair GID-domain hard check's
/// context, the Coverage/GDEF discipline). The search fields, the walk slack, the
/// class-table numGlyphs range and unknown formats are observations, never refusals;
/// the hard face is the card's list - versions, the coverage reserved ridges, the
/// pair order and GID domain, the format 2 offset and class-table bounds.</summary>
public readonly ref partial struct KernView
{
    /// <summary>Whole-table validate face. The search fields, walk slack, class-range
    /// and unknown-format observations are not reported here (no diagnostic is ever
    /// emitted for them); only the hard faces refuse.</summary>
    public ReadStatus ValidateTable(int numGlyphs, out FontDiagnostic diagnostic,
        BuildLimits? limits = null)
    {
        var budget = new BuildBudget(limits);
        if (!budget.Input(_data.Length, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        return ReadTable(numGlyphs, false, out _, out diagnostic, budget);
    }

    /// <summary>Reads the complete table into an owned <see cref="KernTable"/> with
    /// every typed subtable body materialized (pairs and class matrices per budget;
    /// raw bodies keep their slice bytes). The materialize flag decides owned models,
    /// never which checks run, so both entries answer with identical diagnostics at
    /// identical locations.</summary>
    public ReadStatus TryReadTable(int numGlyphs, out KernTable? model,
        out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        model = null;
        var budget = new BuildBudget(limits);
        if (!budget.Input(_data.Length, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        ReadStatus status = ReadTable(numGlyphs, true, out var read, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        model = read;
        return ReadStatus.Done;
    }

    // The one strict pass: version gate, the explicit glyph context, the dialect
    // count record, then the length-driven subtable walk.
    private ReadStatus ReadTable(int numGlyphs, bool materialize, out KernTable? model,
        out FontDiagnostic diagnostic, BuildBudget budget)
    {
        model = null;
        ReadStatus status = TryRequireKnownVersion(out ulong version, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = CoverageTableCompiler.CheckNumGlyphs(numGlyphs, Location, out diagnostic);
        if (status != ReadStatus.Done) return status;
        bool apple = version == KernLayout.VersionApple;
        status = TryGetHeaderEnd(out ulong headerEnd, out diagnostic);
        if (status != ReadStatus.Done) return status;
        int subtableHeader = apple
            ? KernLayout.SubtableHeaderV1
            : KernLayout.SubtableHeaderV0;
        int nTables;
        if (apple)
        {
            // The record reader owns the v1 existence domain (extent 4..7 refuses
            // truncated here, the header-truncation negative). The fixed32's low half
            // must be zero - 0x00010000 is the only defined Apple version (the
            // declared record cannot express the constant, commit 1's registered
            // fallback), so the semantic check lives read-side.
            status = TryReadAppleHeaderAt(2, 0, out var appleHeader, out diagnostic);
            if (status != ReadStatus.Done) return status;
            if (appleHeader.VersionHigh != 0)
                return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedVersion,
                    Location.AtLocal(DataField.Version, 2), out diagnostic,
                    appleHeader.VersionHigh, 0ul);
            status = CheckSubtableCount(appleHeader.NTablesApple, 4, headerEnd, subtableHeader,
                out nTables, out diagnostic);
            if (status != ReadStatus.Done) return status;
        }
        else
        {
            status = TryReadLegacyCountAt(2, 0, out var legacy, out diagnostic);
            if (status != ReadStatus.Done) return status;
            status = CheckSubtableCount(legacy.NTablesLegacy, 2, headerEnd, subtableHeader,
                out nTables, out diagnostic);
            if (status != ReadStatus.Done) return status;
        }
        if (!budget.Work((long)headerEnd, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        var subtables = materialize ? new KernSubtable[nTables] : null;
        int walk = (int)headerEnd;
        for (int i = 0; i < nTables; i++)
        {
            status = ReadSubtable(i, ref walk, apple, numGlyphs, materialize,
                out var subtable, out diagnostic, budget);
            if (status != ReadStatus.Done) return status;
            if (subtables is not null)
                subtables[i] = subtable!;
        }
        bool exactTiling = walk == _data.Length;
        if (materialize)
        {
            if (!budget.Buffer(16, Location, out diagnostic)) return ReadStatus.ResourceLimit;
            model = new((uint)version, apple ? KernDialect.Apple : KernDialect.Version0, numGlyphs,
                subtables!, Location, walk, exactTiling);
        }
        diagnostic = default;
        return ReadStatus.Done;
    }

    // The count is the walk's upper bound, so it must fit the extent it walks:
    // nTables == 0 is the meaningless sequence (the cmap NumTables shape) and a count
    // above (extent - header) / subtableHeader cannot tile even minimum subtables.
    private ReadStatus CheckSubtableCount(ulong count, int countAt, ulong headerEnd,
        int subtableHeader, out int nTables, out FontDiagnostic diagnostic)
    {
        nTables = 0;
        if (count == 0)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.AtLocal(DataField.Count, (ulong)countAt), out diagnostic, 0ul, 1ul);
        ulong bound = ((ulong)_data.Length - headerEnd) / (ulong)subtableHeader;
        if (count > bound)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.AtLocal(DataField.Count, (ulong)countAt), out diagnostic, count, bound);
        nTables = (int)count;
        diagnostic = default;
        return ReadStatus.Done;
    }

    // One walk step: the mechanical header probe advances the walk by the declared
    // length, the dialect/format dispatch opens the declared view over the exact
    // length slice, and unknown formats observe + skip (OQ-3; the Apple fmt1/3
    // boundary keeps the raw bytes without decoding them).
    private ReadStatus ReadSubtable(int ordinal, ref int walk, bool apple, int numGlyphs,
        bool materialize, out KernSubtable? model, out FontDiagnostic diagnostic,
        BuildBudget budget)
    {
        model = null;
        int start = walk;
        ReadStatus status = TryGetSubtableExtent(ref walk, apple, ordinal, out uint length,
            out ushort coverage, out ushort tupleIndex, out diagnostic);
        if (status != ReadStatus.Done) return status;
        ushort format = apple ? (ushort)(coverage & KernLayout.AppleFormatMask)
            : (ushort)((coverage & KernLayout.Format0Mask) >> 8);
        var location = Location.AtLocal(DataField.Offset, (ulong)start, ordinal);
        if (!budget.Work(length, location, out diagnostic)) return ReadStatus.ResourceLimit;
        var slice = _data.Slice(start, (int)length);
        KernSubtableBody body;
        switch (format)
        {
            case 0 when apple:
                status = KernAppleSubtable0View.TryOpen(slice, out var appleFormat0,
                    out diagnostic, location);
                if (status != ReadStatus.Done) return status;
                status = appleFormat0.ReadBody(numGlyphs, materialize, out var appleBody0,
                    out diagnostic, budget);
                body = appleBody0!;
                break;
            case 0:
                status = KernSubtable0View.TryOpen(slice, out var format0, out diagnostic, location);
                if (status != ReadStatus.Done) return status;
                status = format0.ReadBody(numGlyphs, materialize, out var body0,
                    out diagnostic, budget);
                body = body0!;
                break;
            case 2 when apple:
                status = KernAppleSubtable2View.TryOpen(slice, out var appleFormat2,
                    out diagnostic, location);
                if (status != ReadStatus.Done) return status;
                status = appleFormat2.ReadBody(numGlyphs, materialize, out var appleBody2,
                    out diagnostic, budget);
                body = appleBody2!;
                break;
            case 2:
                status = KernSubtable2View.TryOpen(slice, out var format2, out diagnostic, location);
                if (status != ReadStatus.Done) return status;
                status = format2.ReadBody(numGlyphs, materialize, out var body2,
                    out diagnostic, budget);
                body = body2!;
                break;
            default:
                return FinishRawSubtable(ordinal, start, slice, (int)length, apple, format,
                    coverage, tupleIndex, materialize, location, out model, out diagnostic,
                    budget);
        }
        if (status != ReadStatus.Done) return status;
        if (materialize)
        {
            if (!budget.Buffer(16, location, out diagnostic)) return ReadStatus.ResourceLimit;
            model = new(ordinal, start, (int)length,
                apple ? KernDialect.Apple : KernDialect.Version0, format, coverage,
                tupleIndex, body, location);
        }
        diagnostic = default;
        return ReadStatus.Done;
    }

    // Raw observe + length skip (OQ-3's lean ruling; the table stays valid).
    // Materialization keeps the raw slice bytes for byte-level boundary accounting;
    // validate mode charges the walk only.
    private ReadStatus FinishRawSubtable(int ordinal, int start, ReadOnlySpan<byte> slice,
        int length, bool apple, ushort format, ushort coverage, ushort tupleIndex,
        bool materialize, DataLocation location, out KernSubtable? model,
        out FontDiagnostic diagnostic, BuildBudget budget)
    {
        model = null;
        KernSubtableBody body;
        if (materialize)
        {
            if (!budget.Buffer(slice.Length, location, out diagnostic))
                return ReadStatus.ResourceLimit;
            body = new KernRawSubtableBody(slice.ToArray(), location, length);
            if (!budget.Buffer(16, location, out diagnostic)) return ReadStatus.ResourceLimit;
            model = new(ordinal, start, length, apple ? KernDialect.Apple : KernDialect.Version0,
                format, coverage, tupleIndex, body, location);
        }
        else
        {
            body = new KernRawSubtableBody(Array.Empty<byte>(), location, length);
        }
        diagnostic = default;
        return ReadStatus.Done;
    }

    // The mechanical subtable header probe (the cmap TryGetSubtableExtent precedent:
    // raw wire reads for the dispatch fields no single declared view can see ahead of
    // the format dispatch). Advances walk by the declared length on success. The
    // hard faces here are the walk's own: header truncation, the length floor and
    // extent ceiling (OQ-1's contract authority), the coverage reserved ridge and
    // the v0 subtable version gate (unknown formats ride the same gates).
    private ReadStatus TryGetSubtableExtent(ref int walk, bool apple, int ordinal,
        out uint length, out ushort coverage, out ushort tupleIndex, out FontDiagnostic diagnostic)
    {
        tupleIndex = 0;
        length = 0;
        coverage = 0;
        int headerSize = apple ? KernLayout.SubtableHeaderV1 : KernLayout.SubtableHeaderV0;
        if (_data.Length - walk < headerSize)
            return ReadWire.Fail(ReadStatus.Truncated, DiagnosticCode.TruncatedData,
                Location.AtLocal(DataField.Length, (ulong)(walk + (apple ? 0 : 2)), ordinal),
                out diagnostic, (ulong)_data.Length, (ulong)(walk + headerSize));
        ulong declared;
        if (apple)
        {
            declared = ReadWire.U32(_data, walk);
            coverage = ReadWire.U16(_data, walk + 4);
            tupleIndex = ReadWire.U16(_data, walk + 6);
            if ((coverage & KernLayout.AppleUnusedMask) != 0)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    Location.AtLocal(DataField.Flags, (ulong)(walk + 4), ordinal),
                    out diagnostic, coverage,
                    (ulong)(KernLayout.AppleFlagsMask | KernLayout.AppleFormatMask));
        }
        else
        {
            ushort subVersion = ReadWire.U16(_data, walk);
            if (subVersion != KernLayout.Version0)
                return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedVersion,
                    Location.AtLocal(DataField.Version, (ulong)walk, ordinal),
                    out diagnostic, subVersion, 0ul);
            declared = ReadWire.U16(_data, walk + 2);
            coverage = ReadWire.U16(_data, walk + 4);
            if ((coverage & KernLayout.Flags0ReservedMask) != 0)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    Location.AtLocal(DataField.Flags, (ulong)(walk + 4), ordinal),
                    out diagnostic, coverage,
                    (ulong)(KernLayout.Flags0LegalMask | KernLayout.Format0Mask));
        }
        if (declared < (ulong)headerSize)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.AtLocal(DataField.Length, (ulong)(walk + (apple ? 0 : 2)), ordinal),
                out diagnostic, declared, (ulong)headerSize);
        if (declared > (ulong)(_data.Length - walk))
            return ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.RangeOutsideSource,
                Location.AtLocal(DataField.Length, (ulong)(walk + (apple ? 0 : 2)), ordinal),
                out diagnostic, (ulong)walk + declared, (ulong)_data.Length);
        length = (uint)declared;
        walk += (int)declared;
        diagnostic = default;
        return ReadStatus.Done;
    }
}

/// <summary>The shared kern semantics the per-dialect body readers call, so every
/// rule is stated once: the frozen R6b search recomputation and the per-pair
/// order/GID hard checks (the CoverageView diagnostic shapes).</summary>
internal static class KernSemantics
{
    /// <summary>The canonical search triple under the frozen R6b convention: the
    /// largest power of two at or under the pair count drives the range, and an empty
    /// array recomputes to 0/0/0 (so the empty witness's wire 6/0/0 reads as the
    /// registered non-canonical observation). The searchRange is an int because the
    /// true value leaves the u16 field above 10922 pairs (the OpenSans 98304 shape) -
    /// exactly why the triple is an observation face, never a hard check (OQ-2).</summary>
    internal static void CanonicalSearchFields(int pairCount, out int searchRange,
        out int entrySelector, out int rangeShift)
    {
        if (pairCount <= 0)
        {
            searchRange = 0; entrySelector = 0; rangeShift = 0;
            return;
        }
        uint largest = 1;
        while (largest * 2 <= (uint)pairCount) largest *= 2;
        searchRange = (int)(largest * KernLayout.PairStride);
        entrySelector = System.Numerics.BitOperations.Log2(largest);
        rangeShift = ((int)pairCount - (int)largest) * KernLayout.PairStride;
    }

    /// <summary>One pair's hard checks at its own row position: the numGlyphs domain
    /// first (the OQ-4 hard half; the Coverage GID-domain shape), then the strict
    /// order over the (left &lt;&lt; 16) | right key (the T0 rejection face; equal
    /// keys refuse exactly like descending ones).</summary>
    internal static ReadStatus CheckPair(int numGlyphs, ref long previousKey,
        ushort left, ushort right, ulong row, DataLocation location,
        out FontDiagnostic diagnostic)
    {
        if (left >= (ushort)numGlyphs)
            return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                location.AtLocal(DataField.GlyphId, row), out diagnostic,
                left, (ulong)numGlyphs);
        if (right >= (ushort)numGlyphs)
            return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                location.AtLocal(DataField.GlyphId, row + 2), out diagnostic,
                right, (ulong)numGlyphs);
        long key = ((long)left << 16) | right;
        if (previousKey >= 0 && key <= previousKey)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                location.AtLocal(DataField.GlyphId, row), out diagnostic,
                (ulong)key, (ulong)previousKey);
        previousKey = key;
        diagnostic = default;
        return ReadStatus.Done;
    }
}
