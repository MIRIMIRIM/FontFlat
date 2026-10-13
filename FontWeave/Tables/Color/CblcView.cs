using FontWeave.Core;
using FontWeave.Tables.Layout;

namespace FontWeave.Tables;

/// <summary>Strict whole-table validation and materialization for CBLC v3.0
/// (card T6-05 §3). cbdtDataLength is the explicit required input - the
/// per-glyph data windows live in the CBDT pool and no other authority exists
/// (OQ-T6-05-12's lean: the T5-04/T5-05 dual-input + T6-01 cpalNumPaletteEntries
/// family); the joint layer additionally holds the CbdtView bounded view,
/// never guessing one side from the other. numGlyphs is the optional maxp
/// forward (OQ-T6-05-3's candidate B): null skips the glyph-domain face, a
/// value refuses an endGlyphIndex or sparse glyph ID at or beyond it (the
/// corpus's subset-tool witness is the caller-supplied refusal case). The
/// strict {3} version gate (OQ-T6-05-1's ruling: no 2.0 definition text
/// exists; the four 2.0 corpus hosts are the expected typed-refusal witness
/// pool), the minor!=0 refusal (OQ-T6-05-2, the CFF2 family), the per-row
/// value domains (colorRef set-to-0, flags reserved 0xFC, the bitDepth closed
/// set), the record ordering must + range-overlap refusal (OQ-T6-05-4's lean)
/// and the pairing matrix (OQ-T6-05-9's lean) are the hard faces. numSizes==0
/// is a legal shell (OQ-10), subtable 32-bit alignment and pad elements are
/// observations (OQ-5: the corpus's real subtables violate E L268 and must
/// not refuse), sbitOffset==0 is a legal first element (OQ-11: the pool
/// origin value, not a NULL mark) and imageDataOffset==0 is observed. Error
/// priority follows the T4-01 line: input contract, version, presence,
/// bounds, relations, value domains.</summary>
public readonly ref partial struct CblcView
{
    public ReadStatus ValidateTable(int cbdtDataLength, int? numGlyphs,
        out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        var budget = new BuildBudget(limits);
        if (!budget.Input(_data.Length, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        return ValidateTable(cbdtDataLength, numGlyphs, out diagnostic, budget);
    }

    internal ReadStatus ValidateTable(int cbdtDataLength, int? numGlyphs,
        out FontDiagnostic diagnostic, BuildBudget budget)
    {
        // The explicit input contract first (the frozen walk order): the pool
        // window length, then the optional glyph-domain authority.
        if (cbdtDataLength < 0)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.AtLocal(DataField.InputBytes, 0), out diagnostic,
                unchecked((ulong)(long)cbdtDataLength), 0UL);
        if (numGlyphs is { } glyphDomain)
        {
            ReadStatus contract = CoverageTableCompiler.CheckNumGlyphs(glyphDomain,
                Location, out diagnostic);
            if (contract != ReadStatus.Done) return contract;
        }
        // The version gate: strict {3} (C L43/L48; OQ-T6-05-1's ruling), then
        // the minor face ("= 0", C L44; the CFF2 OQ-T5-05-2 family).
        ReadStatus status = RequireVersion(out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = TryGetNumSizes(out uint numSizes, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (!budget.Work((long)_data.Length + 48L * (long)numSizes, Location,
                out diagnostic)) return ReadStatus.ResourceLimit;
        // The bitmapSizes array window: the generated extent require owns the
        // first-incomplete-row provenance (the T4-10 family line).
        status = TryRequireBitmapSizeExtent(out diagnostic);
        if (status != ReadStatus.Done) return status;
        for (int i = 0; i < numSizes; i++)
        {
            status = TryGetBitmapSize(i, out var row, out diagnostic);
            if (status != ReadStatus.Done) return status;
            status = ValidateStrike(i, row, cbdtDataLength, numGlyphs, budget,
                out diagnostic);
            if (status != ReadStatus.Done) return status;
        }
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>The strict version gate (the sbix shape: the refusal names the
    /// one supported major as its expectation).</summary>
    private ReadStatus RequireVersion(out FontDiagnostic diagnostic)
    {
        ReadStatus status = TryGetMajorVersion(out ushort major, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (major != CblcLayout.MajorVersion3)
            return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedVersion,
                Location.AtLocal(DataField.Version, 0), out diagnostic, major,
                CblcLayout.MajorVersion3);
        status = TryGetMinorVersion(out ushort minor, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (minor != 0)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.AtLocal(DataField.Payload, 2), out diagnostic, minor, 0UL);
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>One strike's walk in the frozen order: the list region's
    /// explicit total window (C L57's definitional premise), the record array
    /// fit, the row value domains, then the per-record faces (windows,
    /// ordering must, overlap refusal, format faces, pairing matrix, body
    /// orderings, glyph domain, CBDT data windows).</summary>
    private ReadStatus ValidateStrike(int sizeIndex, in BitmapSizeData row,
        int cbdtDataLength, int? numGlyphs, BuildBudget budget, out FontDiagnostic diagnostic)
    {
        diagnostic = default;
        // The strike's index region: an explicit total window (listOffset from
        // the table start, listSize covering the record array plus every
        // subtable body, C L56-57). Outside the table is a range face at the
        // row's own offset slot; past the table end a truncation.
        ulong rowAt = CblcLayout.BitmapSizeAt + (ulong)row.Ordinal * CblcLayout.BitmapSizeStride;
        if (numGlyphs is { } glyphDomain && row.EndGlyphIndex >= glyphDomain)
            return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                Location.AtLocal(DataField.GlyphId, rowAt + 42, row.Ordinal), out diagnostic,
                row.EndGlyphIndex, (uint)glyphDomain);
        ulong listStart = row.IndexSubtableListOffset;
        ulong listEnd = listStart + row.IndexSubtableListSize;
        if (listStart > (ulong)_data.Length)
            return ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.RangeOutsideSource,
                Location.AtLocal(DataField.Offset, rowAt, row.Ordinal), out diagnostic,
                listStart, (ulong)_data.Length);
        if (listEnd > (ulong)_data.Length)
            return ReadWire.Fail(ReadStatus.Truncated, DiagnosticCode.TruncatedData,
                Location.AtLocal(DataField.Length, rowAt + 4, row.Ordinal), out diagnostic,
                (ulong)_data.Length - listStart, (ulong)row.IndexSubtableListSize);
        if (8UL * row.NumIndexSubtables > (ulong)row.IndexSubtableListSize)
            return ReadWire.Fail(ReadStatus.Truncated, DiagnosticCode.TruncatedData,
                Location.AtLocal(DataField.Count, rowAt + 8, row.Ordinal), out diagnostic,
                (ulong)row.IndexSubtableListSize, 8UL * row.NumIndexSubtables);
        // The row value domains in the card's order: start<=end (C L62-63's
        // semantics), the optional glyph-domain face, colorRef set-to-0
        // (OQ-6), the flags reserved mask (OQ-7; a negative int8 byte is the
        // same reserved-bits face), the bitDepth closed set.
        if (row.StartGlyphIndex > row.EndGlyphIndex)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.AtLocal(DataField.GlyphId, rowAt + 42, row.Ordinal), out diagnostic,
                row.StartGlyphIndex, row.EndGlyphIndex);
        if (row.ColorRef != 0)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.AtLocal(DataField.Payload, rowAt + 12, row.Ordinal), out diagnostic,
                row.ColorRef, 0UL);
        if ((row.Flags & CblcLayout.FlagsReservedMask) != 0)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.AtLocal(DataField.Flags, rowAt + 47, row.Ordinal), out diagnostic,
                (ulong)(row.Flags & CblcLayout.FlagsReservedMask), 0UL);
        if (!CblcLayout.IsBitDepthDefined(row.BitDepth))
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.AtLocal(DataField.Payload, rowAt + 46, row.Ordinal), out diagnostic,
                row.BitDepth, 0UL);
        // The records: window + ordering faces, then the deshape and the
        // format/pairing/data-window faces.
        ushort previousFirst = 0, previousLast = 0;
        for (int r = 0; r < row.NumIndexSubtables; r++)
        {
            ReadStatus status = ReadRecord(listStart, r, out var record, out diagnostic);
            if (status != ReadStatus.Done) return status;
            if (numGlyphs is { } recordGlyphDomain && record.LastGlyphIndex >= recordGlyphDomain)
                return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                    Location.AtLocal(DataField.GlyphId, listStart + 8UL * (uint)r + 2UL, r),
                    out diagnostic, record.LastGlyphIndex, (uint)recordGlyphDomain);
            // E L183's "must be sorted by firstGlyphIndex" and the OQ-4 lean's
            // overlap refusal: an earlier first is the must face, a first at
            // or inside the previous range the overlap face. Cross-strike
            // range overlap stays an observation (OQ-4's split: multi-ppem
            // coverage of one glyph is the format's purpose).
            if (r > 0)
            {
                if (record.FirstGlyphIndex < previousFirst)
                    return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                        Location.AtLocal(DataField.GlyphId, listStart + 8UL * (uint)r, r),
                        out diagnostic, record.FirstGlyphIndex, previousFirst);
                if (record.FirstGlyphIndex <= previousLast)
                    return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.OverlappingRange,
                        Location.AtLocal(DataField.GlyphId, listStart + 8UL * (uint)r, r),
                        out diagnostic, record.FirstGlyphIndex, previousLast);
            }
            previousFirst = record.FirstGlyphIndex;
            previousLast = record.LastGlyphIndex;
            status = DeshapeSubtable(listStart, listEnd, record, budget, out var subtable,
                out diagnostic);
            if (status != ReadStatus.Done) return status;
            status = ValidateSubtable(sizeIndex, r, subtable, cbdtDataLength, numGlyphs,
                listStart + record.IndexSubtableOffset, budget, out diagnostic);
            if (status != ReadStatus.Done) return status;
        }
        diagnostic = default;
        return ReadStatus.Done;
    }

    private ReadStatus ReadRecord(ulong listStart, int recordOrdinal,
        out CblcIndexSubtableRecord record, out FontDiagnostic diagnostic)
    {
        record = default;
        ulong at = listStart + 8UL * (ulong)recordOrdinal;
        // The record array's fit is proven by 8·numIndexSubtables <= listSize;
        // the slice stays the defensive bound.
        if (!TrySlice(_data, at, CblcLayout.IndexSubtableRecordStride, out var bytes))
            return ReadWire.Fail(ReadStatus.Truncated, DiagnosticCode.TruncatedData,
                Location.AtLocal(DataField.GlyphId, at, recordOrdinal), out diagnostic,
                (ulong)_data.Length, at + CblcLayout.IndexSubtableRecordStride);
        record = new CblcIndexSubtableRecord(recordOrdinal,
            global::System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(bytes),
            global::System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(bytes.Slice(2)),
            global::System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(4)));
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>One subtable's format faces over the deshaped body: the image
    /// format's support face (OQ-8 - inherited, never-supported and unknown
    /// all refuse under the same face with the wire value as the detail), the
    /// pairing matrix (OQ-9's full hard refusal), the body orderings (F4's
    /// pairs and F5's glyph IDs - the E L262/L258 definitional premises the
    /// window derivation stands on), the optional glyph domain over the sparse
    /// IDs, and the CBDT data windows with cbdtDataLength as the pool
    /// authority. The equal-consecutive offsets pair is the legal absent form
    /// and skips the window walk (E L264); sbitOffset==0 is a populated first
    /// element (OQ-11), never a NULL mark.</summary>
    private ReadStatus ValidateSubtable(int sizeIndex, int recordOrdinal,
        CblcIndexSubtable subtable, int cbdtDataLength, int? numGlyphs, ulong headerAt, BuildBudget budget,
        out FontDiagnostic diagnostic)
    {
        diagnostic = default;
        if (!CblcLayout.IsImageFormatTyped(subtable.ImageFormat))
        {
            // Every non-typed image format refuses under the same face; the
            // inherited {1,2,5,6,7,8,9} are T6-08's known formats (the
            // diagnosis registers the deferral, never "unknown"), {3,4} are
            // never supported (EBDT obsolete 3 / Apple-only 4), the rest
            // unknown (OQ-8's lean).
            return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
                Location.AtLocal(DataField.Payload, headerAt + 2UL, recordOrdinal), out diagnostic,
                subtable.ImageFormat, 0UL);
        }
        if (!CblcLayout.IsPairingDefined(subtable.IndexFormat, subtable.ImageFormat))
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.AtLocal(DataField.Payload, headerAt + 2UL, recordOrdinal), out diagnostic,
                subtable.ImageFormat, 0UL);
        ulong poolEnd = (ulong)cbdtDataLength;
        switch (subtable)
        {
            case CblcOffsetSubtable offsets:
            {
                var array = offsets.SbitOffsets;
                if (!budget.Work(array.Length, Location, out diagnostic))
                    return ReadStatus.ResourceLimit;
                for (int k = 0; k < array.Length - 1; k++)
                {
                    ulong offsetAt = headerAt + 8UL + (ulong)k *
                        (offsets.IndexFormat == CblcLayout.IndexFormat1 ? 4UL : 2UL);
                    if (array[k] == array[k + 1]) continue;
                    ulong position = (ulong)offsets.ImageDataOffset + array[k];
                    ulong end = (ulong)offsets.ImageDataOffset + array[k + 1];
                    if (position > poolEnd || end > poolEnd)
                        return ReadWire.Fail(ReadStatus.InvalidOffset,
                            DiagnosticCode.RangeOutsideSource,
                            Location.AtLocal(DataField.Offset, offsetAt, k), out diagnostic,
                            Math.Max(position, end), poolEnd);
                    // "image data must be stored contiguously and in glyph ID
                    // order" (E L262): the non-decreasing offsets are the size
                    // derivation's premise; equality is the absent form.
                    if (array[k] > array[k + 1])
                        return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                            Location.AtLocal(DataField.Offset, offsetAt, k), out diagnostic,
                            array[k + 1], array[k]);
                }
                break;
            }
            case CblcImageSizeSubtable imageSize:
            {
                if (!budget.Work(checked((long)imageSize.ImageSize), Location, out diagnostic))
                    return ReadStatus.ResourceLimit;
                // The images are contiguous (E L262): one window covers the
                // whole span - the last glyph's end is the span's end.
                ulong span = (ulong)(imageSize.LastGlyphIndex - imageSize.FirstGlyphIndex) + 1UL;
                ulong total = (ulong)imageSize.ImageDataOffset + span * imageSize.ImageSize;
                if (total > poolEnd)
                    return ReadWire.Fail(ReadStatus.InvalidOffset,
                        DiagnosticCode.RangeOutsideSource,
                        Location.AtLocal(DataField.Offset, headerAt + 4UL, recordOrdinal), out diagnostic,
                        total, poolEnd);
                break;
            }
            case CblcPairSubtable pairs:
            {
                var gids = pairs.GlyphIds;
                var array = pairs.SbitOffsets;
                if (!budget.Work(gids.Length, Location, out diagnostic))
                    return ReadStatus.ResourceLimit;
                for (int k = 0; k < gids.Length; k++)
                {
                    ulong glyphAt = headerAt + 12UL + 4UL * (uint)k;
                    // The pairs ascend strictly (the sentinel included) - the
                    // E L262 contiguous-order premise for the sparse form.
                    if (k > 0 && gids[k] <= gids[k - 1])
                        return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                            Location.AtLocal(DataField.GlyphId, glyphAt, k), out diagnostic,
                            gids[k], gids[k - 1]);
                    // The final pair closes the last image's offset range; it
                    // does not address a glyph in the maxp domain.
                    if (k < gids.Length - 1 && numGlyphs is { } glyphDomain && gids[k] >= glyphDomain)
                        return ReadWire.Fail(ReadStatus.InvalidIndex,
                            DiagnosticCode.IndexOutOfRange,
                            Location.AtLocal(DataField.GlyphId, glyphAt, k), out diagnostic,
                            gids[k], (uint)glyphDomain);
                }
                for (int k = 0; k < array.Length - 1; k++)
                {
                    ulong offsetAt = headerAt + 14UL + 4UL * (uint)k;
                    if (array[k] == array[k + 1]) continue;
                    ulong position = (ulong)pairs.ImageDataOffset + array[k];
                    ulong end = (ulong)pairs.ImageDataOffset + array[k + 1];
                    if (position > poolEnd || end > poolEnd)
                        return ReadWire.Fail(ReadStatus.InvalidOffset,
                            DiagnosticCode.RangeOutsideSource,
                            Location.AtLocal(DataField.Offset, offsetAt, k), out diagnostic,
                            Math.Max(position, end), poolEnd);
                    if (array[k] > array[k + 1])
                        return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                            Location.AtLocal(DataField.Offset, offsetAt, k), out diagnostic,
                            array[k + 1], array[k]);
                }
                break;
            }
            case CblcGlyphIdArraySubtable glyphIds:
            {
                var gids = glyphIds.GlyphIds;
                if (!budget.Work(gids.Length, Location, out diagnostic))
                    return ReadStatus.ResourceLimit;
                for (int k = 0; k < gids.Length; k++)
                {
                    ulong glyphAt = headerAt + 24UL + 2UL * (uint)k;
                    // "sorted by glyph ID" (E L258): strictly ascending, the
                    // sparse form's hard face.
                    if (k > 0 && gids[k] <= gids[k - 1])
                        return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                            Location.AtLocal(DataField.GlyphId, glyphAt, k), out diagnostic,
                            gids[k], gids[k - 1]);
                    if (numGlyphs is { } glyphDomain && gids[k] >= glyphDomain)
                        return ReadWire.Fail(ReadStatus.InvalidIndex,
                            DiagnosticCode.IndexOutOfRange,
                            Location.AtLocal(DataField.GlyphId, glyphAt, k), out diagnostic,
                            gids[k], (uint)glyphDomain);
                }
                ulong total = (ulong)glyphIds.ImageDataOffset +
                    (ulong)gids.Length * glyphIds.ImageSize;
                if (total > poolEnd)
                    return ReadWire.Fail(ReadStatus.InvalidOffset,
                        DiagnosticCode.RangeOutsideSource,
                        Location.AtLocal(DataField.Offset, headerAt + 4UL, recordOrdinal), out diagnostic,
                        total, poolEnd);
                break;
            }
        }
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>One record's deshape: the first>last value face, the header's
    /// window faces (table bounds, then the strike's list window), the index
    /// format closed set, the body length (the card §3 formula) and the body
    /// reads. This is the shared mechanical path of ValidateTable,
    /// TryGetIndexSubtable and TryGetGlyphLocation - one walk, never a second
    /// parser (the AGENTS single-View line).</summary>
    private ReadStatus DeshapeSubtable(ulong listStart, ulong listEnd,
        in CblcIndexSubtableRecord record, BuildBudget budget, out CblcIndexSubtable subtable,
        out FontDiagnostic diagnostic)
    {
        subtable = null!;
        diagnostic = default;
        if (record.FirstGlyphIndex > record.LastGlyphIndex)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.AtLocal(DataField.GlyphId,
                    listStart + 8UL * (ulong)record.Ordinal + 2UL, record.Ordinal),
                out diagnostic, record.FirstGlyphIndex, record.LastGlyphIndex);
        ulong headerAt = listStart + record.IndexSubtableOffset;
        if (headerAt > (ulong)_data.Length)
            return ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.RangeOutsideSource,
                Location.AtLocal(DataField.Offset,
                    listStart + 8UL * (ulong)record.Ordinal + 4UL, record.Ordinal),
                out diagnostic, headerAt, (ulong)_data.Length);
        if (headerAt + CblcLayout.IndexSubHeaderLength > (ulong)_data.Length)
            return ReadWire.Fail(ReadStatus.Truncated, DiagnosticCode.TruncatedData,
                Location.AtLocal(DataField.Payload, headerAt, record.Ordinal), out diagnostic,
                (ulong)_data.Length - headerAt, (ulong)CblcLayout.IndexSubHeaderLength);
        if (headerAt + CblcLayout.IndexSubHeaderLength > listEnd)
            return ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.RangeOutsideSource,
                Location.AtLocal(DataField.Offset,
                    listStart + 8UL * (ulong)record.Ordinal + 4UL, record.Ordinal),
                out diagnostic, headerAt + (ulong)CblcLayout.IndexSubHeaderLength, listEnd);
        if (!TrySlice(_data, headerAt, CblcLayout.IndexSubHeaderLength, out var header))
            return ReadWire.Fail(ReadStatus.Truncated, DiagnosticCode.TruncatedData,
                Location.AtLocal(DataField.Payload, headerAt, record.Ordinal), out diagnostic,
                (ulong)_data.Length, headerAt + (ulong)CblcLayout.IndexSubHeaderLength);
        ushort indexFormat = global::System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(header);
        ushort imageFormat = global::System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(header.Slice(2));
        uint imageDataOffset = global::System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(header.Slice(4));
        // The index format closed set (E L197/201): an unknown index format
        // has no deshape - the refusal is structural, before any body length.
        if (!CblcLayout.IsIndexFormatDefined(indexFormat))
            return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedVersion,
                Location.AtLocal(DataField.Payload, headerAt, record.Ordinal), out diagnostic,
                indexFormat, 0UL);
        // The body length over the card §3 formula: n' = last-first+2 keeps
        // the sentinel for formats 1/3; n is the body numGlyphs for 4/5 (read
        // in situ). Padding elements are NOT part of the assumed window
        // (OQ-5's observation lean). All arithmetic is u64: the u32 counts
        // cannot wrap the additions.
        ulong spanCount = (ulong)(record.LastGlyphIndex - record.FirstGlyphIndex) + 2UL;
        ulong bodyLength;
        switch (indexFormat)
        {
            case CblcLayout.IndexFormat1:
                bodyLength = (ulong)CblcLayout.IndexSubHeaderLength + 4UL * spanCount;
                break;
            case CblcLayout.IndexFormat2:
                bodyLength = (ulong)CblcLayout.IndexSubHeaderLength + 4UL +
                    (ulong)CblcLayout.BigGlyphMetricsLength;
                break;
            case CblcLayout.IndexFormat3:
                bodyLength = (ulong)CblcLayout.IndexSubHeaderLength + 2UL * spanCount;
                break;
            case CblcLayout.IndexFormat4:
            case CblcLayout.IndexFormat5:
            {
                ulong countAt = headerAt + (indexFormat == CblcLayout.IndexFormat4
                    ? CblcLayout.IndexSubHeaderLength
                    : CblcLayout.IndexSubHeaderLength + 4UL + (ulong)CblcLayout.BigGlyphMetricsLength);
                if (countAt + 4UL > (ulong)_data.Length)
                    return ReadWire.Fail(ReadStatus.Truncated, DiagnosticCode.TruncatedData,
                        Location.AtLocal(DataField.Count, countAt, record.Ordinal),
                        out diagnostic, (ulong)_data.Length, countAt + 4UL);
                if (countAt + 4UL > listEnd)
                    return ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.RangeOutsideSource,
                        Location.AtLocal(DataField.Offset,
                            listStart + 8UL * (ulong)record.Ordinal + 4UL, record.Ordinal),
                        out diagnostic, countAt + 4UL, listEnd);
                if (!TrySlice(_data, countAt, 4, out var countBytes))
                    return ReadWire.Fail(ReadStatus.Truncated, DiagnosticCode.TruncatedData,
                        Location.AtLocal(DataField.Count, countAt, record.Ordinal),
                        out diagnostic, (ulong)_data.Length, countAt + 4UL);
                ulong bodyGlyphs = global::System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(countBytes);
                bodyLength = indexFormat == CblcLayout.IndexFormat4
                    ? (ulong)CblcLayout.IndexSubHeaderLength + 4UL + 4UL * (bodyGlyphs + 1UL)
                    : (ulong)CblcLayout.IndexSubHeaderLength + 4UL +
                        (ulong)CblcLayout.BigGlyphMetricsLength + 4UL + 2UL * bodyGlyphs;
                break;
            }
            default:
                return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedVersion,
                    Location.AtLocal(DataField.Payload, headerAt, record.Ordinal), out diagnostic,
                    indexFormat, 0UL);
        }
        // The body window: past the table is a truncation, past the strike's
        // declared list region a range face (the C L57 explicit total window).
        if (headerAt + bodyLength > (ulong)_data.Length)
            return ReadWire.Fail(ReadStatus.Truncated, DiagnosticCode.TruncatedData,
                Location.AtLocal(DataField.Payload, headerAt, record.Ordinal), out diagnostic,
                (ulong)_data.Length - headerAt, bodyLength);
        if (headerAt + bodyLength > listEnd)
            return ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.RangeOutsideSource,
                Location.AtLocal(DataField.Offset,
                    listStart + 8UL * (ulong)record.Ordinal + 4UL, record.Ordinal),
                out diagnostic, headerAt + bodyLength, listEnd);
        long ownedBytes = indexFormat switch
        {
            CblcLayout.IndexFormat1 or CblcLayout.IndexFormat3 => (long)(4UL * spanCount),
            CblcLayout.IndexFormat2 => CblcLayout.BigGlyphMetricsLength,
            CblcLayout.IndexFormat4 => (long)bodyLength - 12L,
            _ => (long)bodyLength - 16L,
        };
        if (!budget.Work((long)bodyLength, Location, out diagnostic) ||
            !budget.Buffer(ownedBytes, Location, out diagnostic))
            return ReadStatus.ResourceLimit;
        return indexFormat switch
        {
            CblcLayout.IndexFormat1 => DeshapeOffsetSubtable(record, indexFormat, imageFormat,
                imageDataOffset, headerAt, 4, spanCount, out subtable, out diagnostic),
            CblcLayout.IndexFormat3 => DeshapeOffsetSubtable(record, indexFormat, imageFormat,
                imageDataOffset, headerAt, 2, spanCount, out subtable, out diagnostic),
            CblcLayout.IndexFormat2 => DeshapeImageSizeSubtable(record, imageFormat,
                imageDataOffset, headerAt, out subtable, out diagnostic),
            CblcLayout.IndexFormat4 => DeshapePairSubtable(record, imageFormat, imageDataOffset,
                headerAt, out subtable, out diagnostic),
            _ => DeshapeGlyphIdArraySubtable(record, imageFormat, imageDataOffset, headerAt,
                out subtable, out diagnostic),
        };
    }

    private ReadStatus DeshapeOffsetSubtable(in CblcIndexSubtableRecord record,
        ushort indexFormat, ushort imageFormat, uint imageDataOffset, ulong headerAt,
        uint elementWidth, ulong spanCount, out CblcIndexSubtable subtable,
        out FontDiagnostic diagnostic)
    {
        subtable = null!;
        var offsets = new uint[spanCount];
        ulong at = headerAt + (ulong)CblcLayout.IndexSubHeaderLength;
        for (ulong k = 0; k < spanCount; k++)
        {
            if (!TrySlice(_data, at + k * elementWidth, elementWidth, out var slot))
                return ReadWire.Fail(ReadStatus.Truncated, DiagnosticCode.TruncatedData,
                    Location.AtLocal(DataField.Offset, at + k * elementWidth, (int)k),
                    out diagnostic, (ulong)_data.Length, at + k * elementWidth + elementWidth);
            offsets[k] = elementWidth == 4
                ? global::System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(slot)
                : global::System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(slot);
        }
        subtable = new CblcOffsetSubtable(record.Ordinal, indexFormat, imageFormat,
            imageDataOffset, record.FirstGlyphIndex, record.LastGlyphIndex, offsets);
        diagnostic = default;
        return ReadStatus.Done;
    }

    private ReadStatus DeshapeImageSizeSubtable(in CblcIndexSubtableRecord record,
        ushort imageFormat, uint imageDataOffset, ulong headerAt,
        out CblcIndexSubtable subtable, out FontDiagnostic diagnostic)
    {
        subtable = null!;
        if (!TrySlice(_data, headerAt + CblcLayout.IndexSubHeaderLength,
                4UL + (ulong)CblcLayout.BigGlyphMetricsLength, out var body))
            return ReadWire.Fail(ReadStatus.Truncated, DiagnosticCode.TruncatedData,
                Location.AtLocal(DataField.Payload,
                    headerAt + CblcLayout.IndexSubHeaderLength, record.Ordinal),
                out diagnostic, (ulong)_data.Length,
                headerAt + CblcLayout.IndexSubHeaderLength + 4UL +
                    (ulong)CblcLayout.BigGlyphMetricsLength);
        uint imageSize = global::System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(body);
        var bigMetrics = body.Slice(4, CblcLayout.BigGlyphMetricsLength).ToArray();
        subtable = new CblcImageSizeSubtable(record.Ordinal, imageFormat, imageDataOffset,
            record.FirstGlyphIndex, record.LastGlyphIndex, imageSize, bigMetrics);
        diagnostic = default;
        return ReadStatus.Done;
    }

    private ReadStatus DeshapePairSubtable(in CblcIndexSubtableRecord record,
        ushort imageFormat, uint imageDataOffset, ulong headerAt,
        out CblcIndexSubtable subtable, out FontDiagnostic diagnostic)
    {
        subtable = null!;
        ulong countAt = headerAt + CblcLayout.IndexSubHeaderLength;
        if (!TrySlice(_data, countAt, 4, out var countBytes))
            return ReadWire.Fail(ReadStatus.Truncated, DiagnosticCode.TruncatedData,
                Location.AtLocal(DataField.Count, countAt, record.Ordinal), out diagnostic,
                (ulong)_data.Length, countAt + 4UL);
        ulong numGlyphs = global::System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(countBytes);
        var glyphIds = new ushort[numGlyphs + 1UL];
        var offsets = new ushort[numGlyphs + 1UL];
        ulong at = countAt + 4UL;
        for (ulong k = 0; k <= numGlyphs; k++)
        {
            if (!TrySlice(_data, at + 4UL * k, 4, out var pair))
                return ReadWire.Fail(ReadStatus.Truncated, DiagnosticCode.TruncatedData,
                    Location.AtLocal(DataField.GlyphId, at + 4UL * k, (int)k), out diagnostic,
                    (ulong)_data.Length, at + 4UL * k + 4UL);
            glyphIds[k] = global::System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(pair);
            offsets[k] = global::System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(pair.Slice(2));
        }
        subtable = new CblcPairSubtable(record.Ordinal, imageFormat, imageDataOffset,
            record.FirstGlyphIndex, record.LastGlyphIndex, glyphIds, offsets);
        diagnostic = default;
        return ReadStatus.Done;
    }

    private ReadStatus DeshapeGlyphIdArraySubtable(in CblcIndexSubtableRecord record,
        ushort imageFormat, uint imageDataOffset, ulong headerAt,
        out CblcIndexSubtable subtable, out FontDiagnostic diagnostic)
    {
        subtable = null!;
        ulong at = headerAt + CblcLayout.IndexSubHeaderLength;
        if (!TrySlice(_data, at, 4 + CblcLayout.BigGlyphMetricsLength + 4, out var head))
            return ReadWire.Fail(ReadStatus.Truncated, DiagnosticCode.TruncatedData,
                Location.AtLocal(DataField.Payload, at, record.Ordinal), out diagnostic,
                (ulong)_data.Length, at + 8UL + (ulong)CblcLayout.BigGlyphMetricsLength);
        uint imageSize = global::System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(head);
        var bigMetrics = head.Slice(4, CblcLayout.BigGlyphMetricsLength).ToArray();
        ulong numGlyphs = global::System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(
            head.Slice(4 + CblcLayout.BigGlyphMetricsLength, 4));
        var glyphIds = new ushort[numGlyphs];
        ulong gidsAt = at + 8UL + (ulong)CblcLayout.BigGlyphMetricsLength;
        for (ulong k = 0; k < numGlyphs; k++)
        {
            if (!TrySlice(_data, gidsAt + 2UL * k, 2, out var gid))
                return ReadWire.Fail(ReadStatus.Truncated, DiagnosticCode.TruncatedData,
                    Location.AtLocal(DataField.GlyphId, gidsAt + 2UL * k, (int)k),
                    out diagnostic, (ulong)_data.Length, gidsAt + 2UL * k + 2UL);
            glyphIds[k] = global::System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(gid);
        }
        subtable = new CblcGlyphIdArraySubtable(record.Ordinal, imageFormat, imageDataOffset,
            record.FirstGlyphIndex, record.LastGlyphIndex, imageSize, bigMetrics, glyphIds);
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>One record's wire triple (E L179-181) with the ordinal and
    /// window gates: the strike and record domains, the list window, and the
    /// header's presence are all the raw accessor's own bounds.</summary>
    public ReadStatus TryGetIndexSubtableRecord(int sizeIndex, int recordIndex,
        out CblcIndexSubtableRecord record, out FontDiagnostic diagnostic)
    {
        record = default;
        ReadStatus status = RequireListWindow(sizeIndex, out _, out ulong listStart,
            out ulong listEnd, out uint numSubtables, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (recordIndex < 0 || (uint)recordIndex >= numSubtables)
            return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                Location.AtLocal(DataField.Count, CblcLayout.BitmapSizeAt + 8UL +
                    48UL * (ulong)sizeIndex, recordIndex), out diagnostic,
                unchecked((ulong)(long)recordIndex), numSubtables);
        status = ReadRecord(listStart, recordIndex, out record, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (record.IndexSubtableOffset > listEnd ||
            listStart + record.IndexSubtableOffset > listEnd)
        {
            return ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.RangeOutsideSource,
                Location.AtLocal(DataField.Offset,
                    listStart + 8UL * (ulong)recordIndex + 4UL, recordIndex), out diagnostic,
                listStart + record.IndexSubtableOffset, listEnd);
        }
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>One record's subtable deshaped through the shared mechanical
    /// path (the five-format body, card §2). The image format travels raw -
    /// the support and pairing faces are the ValidateTable gate's, so a raw
    /// read answers for any wire value an index format can carry.</summary>
    public ReadStatus TryGetIndexSubtable(int sizeIndex, int recordIndex,
        out CblcIndexSubtable subtable, out FontDiagnostic diagnostic)
    {
        subtable = null!;
        var budget = new BuildBudget(null);
        if (!budget.Input(_data.Length, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        ReadStatus status = RequireListWindow(sizeIndex, out _, out ulong listStart,
            out ulong listEnd, out uint numSubtables, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (recordIndex < 0 || (uint)recordIndex >= numSubtables)
            return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                Location.AtLocal(DataField.Count, CblcLayout.BitmapSizeAt + 8UL +
                    48UL * (ulong)sizeIndex, recordIndex), out diagnostic,
                unchecked((ulong)(long)recordIndex), numSubtables);
        status = ReadRecord(listStart, recordIndex, out var record, out diagnostic);
        if (status != ReadStatus.Done) return status;
        return DeshapeSubtable(listStart, listEnd, record, budget, out subtable,
            out diagnostic);
    }

    /// <summary>One glyph's CBDT window resolved through its strike's index
    /// layer (card §2's location face): the record binary search by first
    /// glyph index, then the format's own derivation - F1/F3 index the offset
    /// array directly, F2/F5 place the contiguous slot, F4/F5 binary search
    /// the sparse arrays. Absence (an uncovered glyph ID, the
    /// equal-consecutive offsets pair, a missing sparse entry) answers
    /// present=false, never an error.</summary>
    public ReadStatus TryGetGlyphLocation(int sizeIndex, int glyphId,
        out CblcGlyphLocation location, out bool present, out FontDiagnostic diagnostic)
        => TryGetGlyphLocation(sizeIndex, glyphId, out location, out present,
            out _, out _, out _, out diagnostic, new BuildBudget());

    internal ReadStatus TryGetGlyphLocation(int sizeIndex, int glyphId,
        out CblcGlyphLocation location, out bool present, out ushort indexFormat,
        out ulong imageFormatPosition, out byte bitDepth, out FontDiagnostic diagnostic, BuildBudget budget)
    {
        location = default;
        present = false;
        indexFormat = 0;
        imageFormatPosition = 0;
        bitDepth = 0;
        if (glyphId is < 0 or > ushort.MaxValue)
            return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                Location.AtLocal(DataField.GlyphId, 0), out diagnostic,
                unchecked((ulong)(long)glyphId), ushort.MaxValue);
        if (!budget.Input(_data.Length, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        ReadStatus status = RequireListWindow(sizeIndex, out var row, out ulong listStart, out ulong listEnd,
            out uint numSubtables, out diagnostic);
        if (status != ReadStatus.Done) return status;
        // The record binary search: the ascending firstGlyphIndex order is the
        // E L183 must face; the coverage check keeps an un-validated view
        // honest (a miss answers absent, not garbage).
        int low = 0, high = (int)numSubtables - 1, found = -1;
        while (low <= high)
        {
            if (!budget.Work(1, Location, out diagnostic)) return ReadStatus.ResourceLimit;
            int middle = (low + high) / 2;
            status = ReadRecord(listStart, middle, out var candidate, out diagnostic);
            if (status != ReadStatus.Done) return status;
            if (glyphId < candidate.FirstGlyphIndex) high = middle - 1;
            else if (glyphId > candidate.LastGlyphIndex) low = middle + 1;
            else { found = middle; break; }
        }
        if (found < 0)
        {
            diagnostic = default;
            return ReadStatus.Done;
        }
        status = ReadRecord(listStart, found, out var record, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = DeshapeSubtable(listStart, listEnd, record, budget, out var subtable,
            out diagnostic);
        if (status != ReadStatus.Done) return status;
        ulong headerAt = listStart + record.IndexSubtableOffset;
        status = LocateGlyph(subtable, glyphId, headerAt, budget, out location, out present, out diagnostic);
        if (status == ReadStatus.Done && present)
        {
            indexFormat = subtable.IndexFormat;
            imageFormatPosition = headerAt + 2UL;
            bitDepth = row.BitDepth;
        }
        return status;
    }

    private ReadStatus LocateGlyph(CblcIndexSubtable subtable, int glyphId, ulong headerAt, BuildBudget budget,
        out CblcGlyphLocation location, out bool present, out FontDiagnostic diagnostic)
    {
        location = default;
        present = false;
        diagnostic = default;
        switch (subtable)
        {
            case CblcOffsetSubtable offsets:
            {
                int k = glyphId - offsets.FirstGlyphIndex;
                uint anchor = offsets.SbitOffsets[k], next = offsets.SbitOffsets[k + 1];
                if (next < anchor)
                    return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                        Location.AtLocal(DataField.Offset, headerAt + 8UL +
                            (subtable.IndexFormat == CblcLayout.IndexFormat1 ? 4UL : 2UL) * (ulong)(k + 1), k + 1),
                        out diagnostic, next, anchor);
                if (anchor == next) return Done(out diagnostic);
                location = new CblcGlyphLocation(offsets.ImageFormat,
                    (ulong)offsets.ImageDataOffset + anchor, next - anchor);
                present = true;
                return Done(out diagnostic);
            }
            case CblcImageSizeSubtable imageSize:
            {
                ulong slot = (ulong)(glyphId - imageSize.FirstGlyphIndex) * imageSize.ImageSize;
                location = new CblcGlyphLocation(imageSize.ImageFormat,
                    (ulong)imageSize.ImageDataOffset + slot, imageSize.ImageSize);
                present = imageSize.ImageSize != 0;
                return Done(out diagnostic);
            }
            case CblcPairSubtable pairs:
            {
                ReadStatus status = BinarySearch(pairs.GlyphIds, pairs.GlyphIds.Length - 1, glyphId,
                    budget, Location, out int index, out diagnostic);
                if (status != ReadStatus.Done) return status;
                if (index < 0) return Done(out diagnostic);
                uint anchor = pairs.SbitOffsets[index], next = pairs.SbitOffsets[index + 1];
                if (next < anchor)
                    return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                        Location.AtLocal(DataField.Offset, headerAt + 14UL + 4UL * (ulong)(index + 1), index + 1),
                        out diagnostic, next, anchor);
                if (anchor == next) return Done(out diagnostic);
                location = new CblcGlyphLocation(pairs.ImageFormat,
                    (ulong)pairs.ImageDataOffset + anchor, (ulong)(next - anchor));
                present = true;
                return Done(out diagnostic);
            }
            case CblcGlyphIdArraySubtable glyphIds:
            {
                ReadStatus status = BinarySearch(glyphIds.GlyphIds, glyphIds.GlyphIds.Length, glyphId,
                    budget, Location, out int index, out diagnostic);
                if (status != ReadStatus.Done) return status;
                if (index < 0) return Done(out diagnostic);
                ulong slot = (ulong)index * glyphIds.ImageSize;
                location = new CblcGlyphLocation(glyphIds.ImageFormat,
                    (ulong)glyphIds.ImageDataOffset + slot, glyphIds.ImageSize);
                present = glyphIds.ImageSize != 0;
                return Done(out diagnostic);
            }
            default:
                return Done(out diagnostic);
        }
    }

    private static ReadStatus Done(out FontDiagnostic diagnostic)
    {
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>Ascending ushort search over the sparse arrays (F4's real-glyph
    /// prefix of length count-1, F5's whole array); -1 on a miss.</summary>
    private static ReadStatus BinarySearch(ushort[] values, int count, int target,
        BuildBudget budget, DataLocation location, out int index, out FontDiagnostic diagnostic)
    {
        index = -1;
        int low = 0, high = count - 1;
        while (low <= high)
        {
            if (!budget.Work(1, location, out diagnostic)) return ReadStatus.ResourceLimit;
            int middle = (low + high) / 2;
            if (target < values[middle]) high = middle - 1;
            else if (target > values[middle]) low = middle + 1;
            else { index = middle; return Done(out diagnostic); }
        }
        return Done(out diagnostic);
    }

    /// <summary>The color-strike observation key (card §2): bitDepth 32 marks
    /// the BGRA pre-multiplied sRGB domain - a consumer note, never enforced.</summary>
    public ReadStatus IsColorStrike(int sizeIndex, out bool isColor,
        out FontDiagnostic diagnostic)
    {
        isColor = false;
        ReadStatus status = TryGetBitmapSize(sizeIndex, out var row, out diagnostic);
        if (status != ReadStatus.Done) return status;
        isColor = CblcLayout.IsColorStrike(row.BitDepth);
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>The strike row's own window facts: the row domain, the list
    /// region's bounds inside the table, and the record count. The shared gate
    /// of every index-layer accessor.</summary>
    private ReadStatus RequireListWindow(int sizeIndex, out BitmapSizeData row,
        out ulong listStart, out ulong listEnd, out uint numSubtables,
        out FontDiagnostic diagnostic)
    {
        row = default;
        listStart = listEnd = 0;
        numSubtables = 0;
        if (!IsBound)
            return ReadWire.Fail(ReadStatus.Uninitialized, DiagnosticCode.UnboundView,
                Location, out diagnostic);
        ReadStatus status = RequireVersion(out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = TryGetNumSizes(out uint numSizes, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (sizeIndex < 0 || (uint)sizeIndex >= numSizes)
            return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                Location.AtLocal(DataField.Count, CblcLayout.BitmapSizeAt, sizeIndex),
                out diagnostic, unchecked((ulong)(long)sizeIndex), numSizes);
        status = TryGetBitmapSize(sizeIndex, out row, out diagnostic);
        if (status != ReadStatus.Done) return status;
        listStart = row.IndexSubtableListOffset;
        listEnd = listStart + row.IndexSubtableListSize;
        numSubtables = row.NumIndexSubtables;
        if (listStart > (ulong)_data.Length)
            return ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.RangeOutsideSource,
                Location.AtLocal(DataField.Offset, CblcLayout.BitmapSizeAt + 48UL * (ulong)sizeIndex, sizeIndex),
                out diagnostic, listStart, (ulong)_data.Length);
        if (listEnd > (ulong)_data.Length)
            return ReadWire.Fail(ReadStatus.Truncated, DiagnosticCode.TruncatedData,
                Location.AtLocal(DataField.Length,
                    CblcLayout.BitmapSizeAt + 48UL * (ulong)sizeIndex + 4, sizeIndex),
                out diagnostic, (ulong)_data.Length - listStart,
                (ulong)row.IndexSubtableListSize);
        if (8UL * numSubtables > row.IndexSubtableListSize)
            return ReadWire.Fail(ReadStatus.Truncated, DiagnosticCode.TruncatedData,
                Location.AtLocal(DataField.Count, CblcLayout.BitmapSizeAt + 48UL * (ulong)sizeIndex + 8UL, sizeIndex),
                out diagnostic, row.IndexSubtableListSize, 8UL * numSubtables);
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>Reads the validated table into an owned <see cref="CblcTable"/>:
    /// the rows travel with their raw metrics blocks and every subtable's
    /// deshaped body (the card §2 owned shape; pure Validate never
    /// materializes - the T4 discipline).</summary>
    public ReadStatus TryReadTable(int cbdtDataLength, int? numGlyphs, out CblcTable? model,
        out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        model = null;
        var budget = new BuildBudget(limits);
        if (!budget.Input(_data.Length, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        return TryReadTable(cbdtDataLength, numGlyphs, out model, out diagnostic, budget);
    }

    internal ReadStatus TryReadTable(int cbdtDataLength, int? numGlyphs, out CblcTable? model,
        out FontDiagnostic diagnostic, BuildBudget budget)
    {
        model = null;
        ReadStatus status = ValidateTable(cbdtDataLength, numGlyphs, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        status = TryGetMajorVersion(out ushort major, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = TryGetMinorVersion(out ushort minor, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = TryGetNumSizes(out uint numSizes, out diagnostic);
        if (status != ReadStatus.Done) return status;
        long buffer = 48L * (long)numSizes;
        if (!budget.Buffer(buffer, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        ulong consumed = CblcLayout.BitmapSizeAt + 48UL * numSizes;
        var sizes = new CblcStrike[numSizes];
        for (int i = 0; i < numSizes; i++)
        {
            status = TryGetBitmapSize(i, out var row, out diagnostic);
            if (status != ReadStatus.Done) return status;
            ulong listStart = row.IndexSubtableListOffset;
            ulong listEnd = listStart + row.IndexSubtableListSize;
            consumed = Math.Max(consumed, listEnd);
            if (!budget.Buffer(row.NumIndexSubtables * (long)IntPtr.Size, Location, out diagnostic))
                return ReadStatus.ResourceLimit;
            var subtables = new CblcIndexSubtable[row.NumIndexSubtables];
            for (int r = 0; r < row.NumIndexSubtables; r++)
            {
                status = ReadRecord(listStart, r, out var record, out diagnostic);
                if (status != ReadStatus.Done) return status;
                status = DeshapeSubtable(listStart, listEnd, record, budget, out var subtable,
                    out diagnostic);
                if (status != ReadStatus.Done) return status;
                subtables[r] = subtable;
            }
            var strike = new CblcStrike(row.IndexSubtableListOffset,
                row.IndexSubtableListSize, row.ColorRef, MetricsBlock(row.HoriAscender,
                    row.HoriDescender, row.HoriWidthMax, row.HoriCaretSlopeNumerator,
                    row.HoriCaretSlopeDenominator, row.HoriCaretOffset, row.HoriMinOriginSB,
                    row.HoriMinAdvanceSB, row.HoriMaxBeforeBL, row.HoriMinAfterBL,
                    row.HoriPad1, row.HoriPad2), MetricsBlock(row.VertAscender,
                    row.VertDescender, row.VertWidthMax, row.VertCaretSlopeNumerator,
                    row.VertCaretSlopeDenominator, row.VertCaretOffset, row.VertMinOriginSB,
                    row.VertMinAdvanceSB, row.VertMaxBeforeBL, row.VertMinAfterBL,
                    row.VertPad1, row.VertPad2), row.StartGlyphIndex, row.EndGlyphIndex,
                row.PpemX, row.PpemY, row.BitDepth, row.Flags, subtables);
            sizes[i] = strike;
        }
        model = new(major, minor, sizes, Location, (int)consumed);
        diagnostic = default;
        return ReadStatus.Done;
    }

    private static byte[] MetricsBlock(sbyte b0, sbyte b1, byte b2, sbyte b3, sbyte b4,
        sbyte b5, sbyte b6, sbyte b7, sbyte b8, sbyte b9, sbyte b10, sbyte b11) =>
    [
        unchecked((byte)b0), unchecked((byte)b1), b2, unchecked((byte)b3),
        unchecked((byte)b4), unchecked((byte)b5), unchecked((byte)b6),
        unchecked((byte)b7), unchecked((byte)b8), unchecked((byte)b9),
        unchecked((byte)b10), unchecked((byte)b11),
    ];
}
