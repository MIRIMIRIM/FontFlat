using FontWeave.Core;
using FontWeave.Tables.Layout;

namespace FontWeave.Tables;

/// <summary>EBLC 2.0 index traversal and strict validation. The bitmap size
/// array is read only through its generated schema accessors.</summary>
public readonly ref partial struct EblcView
{
    public ReadStatus ValidateTable(int ebdtDataLength, int? numGlyphs,
        out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        var budget = new BuildBudget(limits);
        if (!budget.Input(_data.Length, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        return ValidateTable(ebdtDataLength, numGlyphs, out diagnostic, budget);
    }

    internal ReadStatus ValidateTable(int ebdtDataLength, int? numGlyphs,
        out FontDiagnostic diagnostic, BuildBudget budget)
    {
        if (ebdtDataLength < 0)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.AtLocal(DataField.InputBytes, 0), out diagnostic,
                unchecked((ulong)(long)ebdtDataLength), 0UL);
        if (numGlyphs is { } glyphDomain)
        {
            ReadStatus contract = CoverageTableCompiler.CheckNumGlyphs(glyphDomain,
                Location, out diagnostic);
            if (contract != ReadStatus.Done) return contract;
        }
        ReadStatus status = RequireVersion(out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = TryGetNumSizes(out uint numSizes, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (!budget.Work((long)_data.Length + 48L * (long)numSizes, Location,
                out diagnostic)) return ReadStatus.ResourceLimit;
        status = TryRequireBitmapSizeExtent(out diagnostic);
        if (status != ReadStatus.Done) return status;
        for (int i = 0; i < numSizes; i++)
        {
            status = TryGetBitmapSize(i, out var row, out diagnostic);
            if (status != ReadStatus.Done) return status;
            status = ValidateStrike(i, row, ebdtDataLength, numGlyphs, budget,
                out diagnostic);
            if (status != ReadStatus.Done) return status;
        }
        diagnostic = default;
        return ReadStatus.Done;
    }

    private ReadStatus RequireVersion(out FontDiagnostic diagnostic)
    {
        ReadStatus status = TryGetMajorVersion(out ushort major, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (major != EblcLayout.MajorVersion2)
            return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedVersion,
                Location.AtLocal(DataField.Version, 0), out diagnostic, major,
                EblcLayout.MajorVersion2);
        status = TryGetMinorVersion(out ushort minor, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (minor != 0)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.AtLocal(DataField.Payload, 2), out diagnostic, minor, 0UL);
        diagnostic = default;
        return ReadStatus.Done;
    }

    private ReadStatus ValidateStrike(int sizeIndex, in BitmapSizeData row,
        int ebdtDataLength, int? numGlyphs, BuildBudget budget, out FontDiagnostic diagnostic)
    {
        diagnostic = default;
        ulong rowAt = EblcLayout.BitmapSizeAt + (ulong)row.Ordinal * EblcLayout.BitmapSizeStride;
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
        if (row.StartGlyphIndex > row.EndGlyphIndex)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.AtLocal(DataField.GlyphId, rowAt + 40, row.Ordinal), out diagnostic,
                row.StartGlyphIndex, row.EndGlyphIndex);
        if (numGlyphs is { } glyphDomain && row.EndGlyphIndex >= glyphDomain)
            return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                Location.AtLocal(DataField.GlyphId, rowAt + 42, row.Ordinal), out diagnostic,
                row.EndGlyphIndex, (uint)glyphDomain);
        if (row.ColorRef != 0)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.AtLocal(DataField.Payload, rowAt + 12, row.Ordinal), out diagnostic,
                row.ColorRef, 0UL);
        if ((row.Flags & EblcLayout.FlagsReservedMask) != 0)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.AtLocal(DataField.Flags, rowAt + 47, row.Ordinal), out diagnostic,
                (ulong)(row.Flags & EblcLayout.FlagsReservedMask), 0UL);
        if (!EblcLayout.IsBitDepthDefined(row.BitDepth))
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.AtLocal(DataField.Payload, rowAt + 46, row.Ordinal), out diagnostic,
                row.BitDepth, 0UL);
        ushort previousFirst = 0, previousLast = 0;
        for (int r = 0; r < row.NumIndexSubtables; r++)
        {
            ReadStatus status = ReadRecord(listStart, r, out var record, out diagnostic);
            if (status != ReadStatus.Done) return status;
            if (numGlyphs is { } recordDomain && record.LastGlyphIndex >= recordDomain)
                return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                    Location.AtLocal(DataField.GlyphId, listStart + 8UL * (uint)r + 2UL, r),
                    out diagnostic, record.LastGlyphIndex, (uint)recordDomain);
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
            status = ValidateSubtable(sizeIndex, r, listStart + record.IndexSubtableOffset,
                subtable, ebdtDataLength, numGlyphs,
                budget, out diagnostic);
            if (status != ReadStatus.Done) return status;
        }
        diagnostic = default;
        return ReadStatus.Done;
    }

    private ReadStatus ReadRecord(ulong listStart, int recordOrdinal,
        out EblcIndexSubtableRecord record, out FontDiagnostic diagnostic)
    {
        record = default;
        ulong at = listStart + 8UL * (ulong)recordOrdinal;
        if (!TrySlice(_data, at, EblcLayout.IndexSubtableRecordStride, out var bytes))
            return ReadWire.Fail(ReadStatus.Truncated, DiagnosticCode.TruncatedData,
                Location.AtLocal(DataField.GlyphId, at, recordOrdinal), out diagnostic,
                (ulong)_data.Length, at + EblcLayout.IndexSubtableRecordStride);
        record = new EblcIndexSubtableRecord(recordOrdinal,
            global::System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(bytes),
            global::System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(bytes.Slice(2)),
            global::System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(4)));
        diagnostic = default;
        return ReadStatus.Done;
    }

    private ReadStatus ValidateSubtable(int sizeIndex, int recordOrdinal, ulong subtableAt,
        EblcIndexSubtable subtable, int ebdtDataLength, int? numGlyphs, BuildBudget budget,
        out FontDiagnostic diagnostic)
    {
        diagnostic = default;
        if (!EblcLayout.IsImageFormatTyped(subtable.ImageFormat))
        {
            return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
                Location.AtLocal(DataField.Payload, subtableAt + 2UL, recordOrdinal), out diagnostic,
                subtable.ImageFormat, 0UL);
        }
        if (!EblcLayout.IsPairingDefined(subtable.IndexFormat, subtable.ImageFormat))
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.AtLocal(DataField.Payload, subtableAt + 2UL, recordOrdinal), out diagnostic,
                subtable.ImageFormat, 0UL);
        ulong poolEnd = (ulong)ebdtDataLength;
        switch (subtable)
        {
            case EblcOffsetSubtable offsets:
            {
                var array = offsets.SbitOffsets;
                if (!budget.Work(array.Length, Location, out diagnostic))
                    return ReadStatus.ResourceLimit;
                for (int k = 0; k < array.Length - 1; k++)
                {
                    ulong wordAt = subtableAt + 8UL +
                        (subtable.IndexFormat == EblcLayout.IndexFormat1 ? 4UL : 2UL) * (ulong)k;
                    if (array[k] > array[k + 1])
                        return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                            Location.AtLocal(DataField.Offset, wordAt +
                                (subtable.IndexFormat == EblcLayout.IndexFormat1 ? 4UL : 2UL), k + 1), out diagnostic,
                            array[k + 1], array[k]);
                    if (array[k] == array[k + 1]) continue;
                    ulong position = (ulong)offsets.ImageDataOffset + array[k];
                    ulong length = array[k + 1] - array[k];
                    ReadStatus window = CheckPoolWindow(position, length, poolEnd,
                        Location.AtLocal(DataField.Offset, wordAt, k), out diagnostic);
                    if (window != ReadStatus.Done) return window;
                }
                break;
            }
            case EblcImageSizeSubtable imageSize:
            {
                if (!budget.Work(imageSize.LastGlyphIndex - imageSize.FirstGlyphIndex + 1L,
                        Location, out diagnostic))
                    return ReadStatus.ResourceLimit;
                ulong span = (ulong)(imageSize.LastGlyphIndex - imageSize.FirstGlyphIndex) + 1UL;
                for (ulong k = 0; k < span; k++)
                {
                    ReadStatus window = CheckPoolWindow((ulong)imageSize.ImageDataOffset + k * imageSize.ImageSize,
                        imageSize.ImageSize, poolEnd,
                        Location.AtLocal(DataField.Length, subtableAt + 8UL, recordOrdinal), out diagnostic);
                    if (window != ReadStatus.Done) return window;
                }
                break;
            }
            case EblcPairSubtable pairs:
            {
                var gids = pairs.GlyphIds;
                var array = pairs.SbitOffsets;
                if (!budget.Work(gids.Length, Location, out diagnostic))
                    return ReadStatus.ResourceLimit;
                for (int k = 0; k < gids.Length; k++)
                {
                    if (k > 0 && gids[k] <= gids[k - 1])
                        return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                            Location.AtLocal(DataField.GlyphId, subtableAt + 12UL + 4UL * (ulong)k, k), out diagnostic,
                            gids[k], gids[k - 1]);
                    if (k < gids.Length - 1 && numGlyphs is { } glyphDomain && gids[k] >= glyphDomain)
                        return ReadWire.Fail(ReadStatus.InvalidIndex,
                            DiagnosticCode.IndexOutOfRange,
                            Location.AtLocal(DataField.GlyphId, subtableAt + 12UL + 4UL * (ulong)k, k), out diagnostic,
                            gids[k], (uint)glyphDomain);
                }
                for (int k = 0; k < array.Length - 1; k++)
                {
                    if (array[k] > array[k + 1])
                        return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                            Location.AtLocal(DataField.Offset, subtableAt + 18UL + 4UL * (ulong)k, k + 1), out diagnostic,
                            array[k + 1], array[k]);
                    if (array[k] == array[k + 1]) continue;
                    ulong position = (ulong)pairs.ImageDataOffset + array[k];
                    ulong length = (ulong)(array[k + 1] - array[k]);
                    ReadStatus window = CheckPoolWindow(position, length, poolEnd,
                        Location.AtLocal(DataField.Offset, subtableAt + 14UL + 4UL * (ulong)k, k),
                        out diagnostic);
                    if (window != ReadStatus.Done) return window;
                }
                break;
            }
            case EblcGlyphIdArraySubtable glyphIds:
            {
                var gids = glyphIds.GlyphIds;
                if (!budget.Work(gids.Length, Location, out diagnostic))
                    return ReadStatus.ResourceLimit;
                for (int k = 0; k < gids.Length; k++)
                {
                    if (k > 0 && gids[k] <= gids[k - 1])
                        return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                            Location.AtLocal(DataField.GlyphId, subtableAt + 24UL + 2UL * (ulong)k, k), out diagnostic,
                            gids[k], gids[k - 1]);
                    if (numGlyphs is { } glyphDomain && gids[k] >= glyphDomain)
                        return ReadWire.Fail(ReadStatus.InvalidIndex,
                            DiagnosticCode.IndexOutOfRange,
                            Location.AtLocal(DataField.GlyphId, subtableAt + 24UL + 2UL * (ulong)k, k), out diagnostic,
                            gids[k], (uint)glyphDomain);
                }
                for (int k = 0; k < gids.Length; k++)
                {
                    ReadStatus window = CheckPoolWindow((ulong)glyphIds.ImageDataOffset + (ulong)k * glyphIds.ImageSize,
                        glyphIds.ImageSize, poolEnd,
                        Location.AtLocal(DataField.Length, subtableAt + 8UL, recordOrdinal), out diagnostic);
                    if (window != ReadStatus.Done) return window;
                }
                break;
            }
        }
        diagnostic = default;
        return ReadStatus.Done;
    }

    private ReadStatus DeshapeSubtable(ulong listStart, ulong listEnd,
        in EblcIndexSubtableRecord record, BuildBudget budget, out EblcIndexSubtable subtable,
        out FontDiagnostic diagnostic)
    {
        subtable = null!;
        diagnostic = default;
        if (record.FirstGlyphIndex > record.LastGlyphIndex)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.AtLocal(DataField.GlyphId,
                    listStart + 8UL * (ulong)record.Ordinal, record.Ordinal),
                out diagnostic, record.FirstGlyphIndex, record.LastGlyphIndex);
        ulong headerAt = listStart + record.IndexSubtableOffset;
        if (headerAt > (ulong)_data.Length)
            return ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.RangeOutsideSource,
                Location.AtLocal(DataField.Offset,
                    listStart + 8UL * (ulong)record.Ordinal + 4UL, record.Ordinal),
                out diagnostic, headerAt, (ulong)_data.Length);
        if (headerAt + EblcLayout.IndexSubHeaderLength > (ulong)_data.Length)
            return ReadWire.Fail(ReadStatus.Truncated, DiagnosticCode.TruncatedData,
                Location.AtLocal(DataField.Payload, headerAt, record.Ordinal), out diagnostic,
                (ulong)_data.Length - headerAt, (ulong)EblcLayout.IndexSubHeaderLength);
        if (headerAt + EblcLayout.IndexSubHeaderLength > listEnd)
            return ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.RangeOutsideSource,
                Location.AtLocal(DataField.Offset,
                    listStart + 8UL * (ulong)record.Ordinal + 4UL, record.Ordinal),
                out diagnostic, headerAt + (ulong)EblcLayout.IndexSubHeaderLength, listEnd);
        if (!TrySlice(_data, headerAt, EblcLayout.IndexSubHeaderLength, out var header))
            return ReadWire.Fail(ReadStatus.Truncated, DiagnosticCode.TruncatedData,
                Location.AtLocal(DataField.Payload, headerAt, record.Ordinal), out diagnostic,
                (ulong)_data.Length, headerAt + (ulong)EblcLayout.IndexSubHeaderLength);
        ushort indexFormat = global::System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(header);
        ushort imageFormat = global::System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(header.Slice(2));
        uint imageDataOffset = global::System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(header.Slice(4));
        if (!EblcLayout.IsIndexFormatDefined(indexFormat))
            return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
                Location.AtLocal(DataField.Payload, headerAt, record.Ordinal), out diagnostic,
                indexFormat, 0UL);
        ulong spanCount = (ulong)(record.LastGlyphIndex - record.FirstGlyphIndex) + 2UL;
        ulong bodyLength;
        switch (indexFormat)
        {
            case EblcLayout.IndexFormat1:
                bodyLength = (ulong)EblcLayout.IndexSubHeaderLength + 4UL * spanCount;
                break;
            case EblcLayout.IndexFormat2:
                bodyLength = (ulong)EblcLayout.IndexSubHeaderLength + 4UL +
                    (ulong)EblcLayout.BigGlyphMetricsLength;
                break;
            case EblcLayout.IndexFormat3:
                bodyLength = (ulong)EblcLayout.IndexSubHeaderLength + 2UL * spanCount;
                break;
            case EblcLayout.IndexFormat4:
            case EblcLayout.IndexFormat5:
            {
                ulong countAt = headerAt + (indexFormat == EblcLayout.IndexFormat4
                    ? EblcLayout.IndexSubHeaderLength
                    : EblcLayout.IndexSubHeaderLength + 4UL + (ulong)EblcLayout.BigGlyphMetricsLength);
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
                bodyLength = indexFormat == EblcLayout.IndexFormat4
                    ? (ulong)EblcLayout.IndexSubHeaderLength + 4UL + 4UL * (bodyGlyphs + 1UL)
                    : (ulong)EblcLayout.IndexSubHeaderLength + 4UL +
                        (ulong)EblcLayout.BigGlyphMetricsLength + 4UL + 2UL * bodyGlyphs;
                break;
            }
            default:
                return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
                    Location.AtLocal(DataField.Payload, headerAt, record.Ordinal), out diagnostic,
                    indexFormat, 0UL);
        }
        if (headerAt + bodyLength > (ulong)_data.Length)
            return ReadWire.Fail(ReadStatus.Truncated, DiagnosticCode.TruncatedData,
                Location.AtLocal(DataField.Payload, headerAt, record.Ordinal), out diagnostic,
                (ulong)_data.Length - headerAt, bodyLength);
        if (headerAt + bodyLength > listEnd)
            return ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.RangeOutsideSource,
                Location.AtLocal(DataField.Offset,
                    listStart + 8UL * (ulong)record.Ordinal + 4UL, record.Ordinal),
                out diagnostic, headerAt + bodyLength, listEnd);
        long ownedBytes = indexFormat == EblcLayout.IndexFormat3 ? (long)(4UL * spanCount) : (long)bodyLength;
        if (!budget.Work((long)bodyLength, Location, out diagnostic) ||
            !budget.Buffer(ownedBytes, Location, out diagnostic))
            return ReadStatus.ResourceLimit;
        return indexFormat switch
        {
            EblcLayout.IndexFormat1 => DeshapeOffsetSubtable(record, indexFormat, imageFormat,
                imageDataOffset, headerAt, 4, spanCount, out subtable, out diagnostic),
            EblcLayout.IndexFormat3 => DeshapeOffsetSubtable(record, indexFormat, imageFormat,
                imageDataOffset, headerAt, 2, spanCount, out subtable, out diagnostic),
            EblcLayout.IndexFormat2 => DeshapeImageSizeSubtable(record, imageFormat,
                imageDataOffset, headerAt, out subtable, out diagnostic),
            EblcLayout.IndexFormat4 => DeshapePairSubtable(record, imageFormat, imageDataOffset,
                headerAt, out subtable, out diagnostic),
            _ => DeshapeGlyphIdArraySubtable(record, imageFormat, imageDataOffset, headerAt,
                out subtable, out diagnostic),
        };
    }

    private ReadStatus DeshapeOffsetSubtable(in EblcIndexSubtableRecord record,
        ushort indexFormat, ushort imageFormat, uint imageDataOffset, ulong headerAt,
        uint elementWidth, ulong spanCount, out EblcIndexSubtable subtable,
        out FontDiagnostic diagnostic)
    {
        subtable = null!;
        var offsets = new uint[spanCount];
        ulong at = headerAt + (ulong)EblcLayout.IndexSubHeaderLength;
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
        subtable = new EblcOffsetSubtable(record.Ordinal, indexFormat, imageFormat,
            imageDataOffset, record.FirstGlyphIndex, record.LastGlyphIndex, offsets);
        diagnostic = default;
        return ReadStatus.Done;
    }

    private ReadStatus DeshapeImageSizeSubtable(in EblcIndexSubtableRecord record,
        ushort imageFormat, uint imageDataOffset, ulong headerAt,
        out EblcIndexSubtable subtable, out FontDiagnostic diagnostic)
    {
        subtable = null!;
        if (!TrySlice(_data, headerAt + EblcLayout.IndexSubHeaderLength,
                4UL + (ulong)EblcLayout.BigGlyphMetricsLength, out var body))
            return ReadWire.Fail(ReadStatus.Truncated, DiagnosticCode.TruncatedData,
                Location.AtLocal(DataField.Payload,
                    headerAt + EblcLayout.IndexSubHeaderLength, record.Ordinal),
                out diagnostic, (ulong)_data.Length,
                headerAt + EblcLayout.IndexSubHeaderLength + 4UL +
                    (ulong)EblcLayout.BigGlyphMetricsLength);
        uint imageSize = global::System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(body);
        var bigMetrics = body.Slice(4, EblcLayout.BigGlyphMetricsLength).ToArray();
        subtable = new EblcImageSizeSubtable(record.Ordinal, imageFormat, imageDataOffset,
            record.FirstGlyphIndex, record.LastGlyphIndex, imageSize, bigMetrics);
        diagnostic = default;
        return ReadStatus.Done;
    }

    private ReadStatus DeshapePairSubtable(in EblcIndexSubtableRecord record,
        ushort imageFormat, uint imageDataOffset, ulong headerAt,
        out EblcIndexSubtable subtable, out FontDiagnostic diagnostic)
    {
        subtable = null!;
        ulong countAt = headerAt + EblcLayout.IndexSubHeaderLength;
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
        subtable = new EblcPairSubtable(record.Ordinal, imageFormat, imageDataOffset,
            record.FirstGlyphIndex, record.LastGlyphIndex, glyphIds, offsets);
        diagnostic = default;
        return ReadStatus.Done;
    }

    private ReadStatus DeshapeGlyphIdArraySubtable(in EblcIndexSubtableRecord record,
        ushort imageFormat, uint imageDataOffset, ulong headerAt,
        out EblcIndexSubtable subtable, out FontDiagnostic diagnostic)
    {
        subtable = null!;
        ulong at = headerAt + EblcLayout.IndexSubHeaderLength;
        if (!TrySlice(_data, at, 4 + EblcLayout.BigGlyphMetricsLength + 4, out var head))
            return ReadWire.Fail(ReadStatus.Truncated, DiagnosticCode.TruncatedData,
                Location.AtLocal(DataField.Payload, at, record.Ordinal), out diagnostic,
                (ulong)_data.Length, at + 8UL + (ulong)EblcLayout.BigGlyphMetricsLength);
        uint imageSize = global::System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(head);
        var bigMetrics = head.Slice(4, EblcLayout.BigGlyphMetricsLength).ToArray();
        ulong numGlyphs = global::System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(
            head.Slice(4 + EblcLayout.BigGlyphMetricsLength, 4));
        var glyphIds = new ushort[numGlyphs];
        ulong gidsAt = at + 8UL + (ulong)EblcLayout.BigGlyphMetricsLength;
        for (ulong k = 0; k < numGlyphs; k++)
        {
            if (!TrySlice(_data, gidsAt + 2UL * k, 2, out var gid))
                return ReadWire.Fail(ReadStatus.Truncated, DiagnosticCode.TruncatedData,
                    Location.AtLocal(DataField.GlyphId, gidsAt + 2UL * k, (int)k),
                    out diagnostic, (ulong)_data.Length, gidsAt + 2UL * k + 2UL);
            glyphIds[k] = global::System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(gid);
        }
        subtable = new EblcGlyphIdArraySubtable(record.Ordinal, imageFormat, imageDataOffset,
            record.FirstGlyphIndex, record.LastGlyphIndex, imageSize, bigMetrics, glyphIds);
        diagnostic = default;
        return ReadStatus.Done;
    }

    public ReadStatus TryGetIndexSubtableRecord(int sizeIndex, int recordIndex,
        out EblcIndexSubtableRecord record, out FontDiagnostic diagnostic)
    {
        record = default;
        ReadStatus status = RequireListWindow(sizeIndex, out _, out ulong listStart,
            out ulong listEnd, out uint numSubtables, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (recordIndex < 0 || (uint)recordIndex >= numSubtables)
            return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                Location.AtLocal(DataField.Count, EblcLayout.BitmapSizeAt + 8UL +
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

    public ReadStatus TryGetIndexSubtable(int sizeIndex, int recordIndex,
        out EblcIndexSubtable subtable, out FontDiagnostic diagnostic)
    {
        subtable = null!;
        var budget = new BuildBudget(null);
        if (!budget.Input(_data.Length, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        return TryGetIndexSubtable(sizeIndex, recordIndex, out subtable, out diagnostic, budget);
    }

    internal ReadStatus TryGetIndexSubtable(int sizeIndex, int recordIndex,
        out EblcIndexSubtable subtable, out FontDiagnostic diagnostic, BuildBudget budget)
    {
        subtable = null!;
        ReadStatus status = RequireListWindow(sizeIndex, out _, out ulong listStart,
            out ulong listEnd, out uint numSubtables, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (recordIndex < 0 || (uint)recordIndex >= numSubtables)
            return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                Location.AtLocal(DataField.Count, EblcLayout.BitmapSizeAt + 8UL +
                    48UL * (ulong)sizeIndex, recordIndex), out diagnostic,
                unchecked((ulong)(long)recordIndex), numSubtables);
        status = ReadRecord(listStart, recordIndex, out var record, out diagnostic);
        if (status != ReadStatus.Done) return status;
        return DeshapeSubtable(listStart, listEnd, record, budget, out subtable,
            out diagnostic);
    }

    public ReadStatus TryGetGlyphLocation(int sizeIndex, int glyphId,
        out EblcGlyphLocation location, out bool present, out FontDiagnostic diagnostic)
        => TryGetGlyphLocation(sizeIndex, glyphId, out location, out present,
            out diagnostic, new BuildBudget());

    internal ReadStatus TryGetGlyphLocation(int sizeIndex, int glyphId,
        out EblcGlyphLocation location, out bool present, out FontDiagnostic diagnostic,
        BuildBudget budget)
    {
        location = default;
        present = false;
        if (glyphId is < 0 or > ushort.MaxValue)
            return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                Location.AtLocal(DataField.GlyphId, 0), out diagnostic,
                unchecked((ulong)(long)glyphId), ushort.MaxValue);
        if (!budget.Input(_data.Length, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        ReadStatus status = RequireListWindow(sizeIndex, out var row, out ulong listStart, out ulong listEnd,
            out uint numSubtables, out diagnostic);
        if (status != ReadStatus.Done) return status;
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
        ulong subtableAt = listStart + record.IndexSubtableOffset;
        status = LocateGlyph(subtable, glyphId, subtableAt, out location, out present,
            out diagnostic);
        if (status == ReadStatus.Done && present)
            location = location with { BitDepth = row.BitDepth, IndexFormat = subtable.IndexFormat,
                ImageFormatPosition = subtableAt + 2UL };
        return status;
    }

    private ReadStatus LocateGlyph(EblcIndexSubtable subtable, int glyphId, ulong subtableAt,
        out EblcGlyphLocation location, out bool present, out FontDiagnostic diagnostic)
    {
        location = default;
        present = false;
        diagnostic = default;
        switch (subtable)
        {
            case EblcOffsetSubtable offsets:
            {
                int k = glyphId - offsets.FirstGlyphIndex;
                uint anchor = offsets.SbitOffsets[k], next = offsets.SbitOffsets[k + 1];
                if (next < anchor)
                    return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                        Location.AtLocal(DataField.Offset, subtableAt + 8UL +
                            (subtable.IndexFormat == EblcLayout.IndexFormat1 ? 4UL : 2UL) *
                            (ulong)(k + 1), k + 1), out diagnostic,
                        next, anchor);
                if (anchor == next) return Done(out diagnostic);
                location = new EblcGlyphLocation(offsets.ImageFormat,
                    (ulong)offsets.ImageDataOffset + anchor, next - anchor);
                present = true;
                return Done(out diagnostic);
            }
            case EblcImageSizeSubtable imageSize:
            {
                ulong slot = (ulong)(glyphId - imageSize.FirstGlyphIndex) * imageSize.ImageSize;
                location = new EblcGlyphLocation(imageSize.ImageFormat,
                    (ulong)imageSize.ImageDataOffset + slot, imageSize.ImageSize);
                present = imageSize.ImageSize != 0;
                return Done(out diagnostic);
            }
            case EblcPairSubtable pairs:
            {
                int index = BinarySearch(pairs.GlyphIds, pairs.GlyphIds.Length - 1, glyphId);
                if (index < 0) return Done(out diagnostic);
                uint anchor = pairs.SbitOffsets[index], next = pairs.SbitOffsets[index + 1];
                if (next < anchor)
                    return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                        Location.AtLocal(DataField.Offset, subtableAt + 14UL +
                            4UL * (ulong)(index + 1), index + 1), out diagnostic,
                        next, anchor);
                if (anchor == next) return Done(out diagnostic);
                location = new EblcGlyphLocation(pairs.ImageFormat,
                    (ulong)pairs.ImageDataOffset + anchor, (ulong)(next - anchor));
                present = true;
                return Done(out diagnostic);
            }
            case EblcGlyphIdArraySubtable glyphIds:
            {
                int index = BinarySearch(glyphIds.GlyphIds, glyphIds.GlyphIds.Length, glyphId);
                if (index < 0) return Done(out diagnostic);
                ulong slot = (ulong)index * glyphIds.ImageSize;
                location = new EblcGlyphLocation(glyphIds.ImageFormat,
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

    private static ReadStatus CheckPoolWindow(ulong position, ulong length, ulong poolEnd,
        DataLocation reference, out FontDiagnostic diagnostic)
    {
        if (length == 0) return Done(out diagnostic);
        if (position >= poolEnd)
            return ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.RangeOutsideSource,
                reference, out diagnostic, position, poolEnd);
        if (length > poolEnd - position)
            return ReadWire.Fail(ReadStatus.Truncated, DiagnosticCode.TruncatedData,
                reference, out diagnostic, poolEnd - position, length);
        return Done(out diagnostic);
    }

    private static int BinarySearch(ushort[] values, int count, int target)
    {
        int low = 0, high = count - 1;
        while (low <= high)
        {
            int middle = (low + high) / 2;
            if (target < values[middle]) high = middle - 1;
            else if (target > values[middle]) low = middle + 1;
            else return middle;
        }
        return -1;
    }

    public ReadStatus IsMonochromeStrike(int sizeIndex, out bool isMonochrome,
        out FontDiagnostic diagnostic)
    {
        isMonochrome = false;
        ReadStatus status = TryGetBitmapSize(sizeIndex, out var row, out diagnostic);
        if (status != ReadStatus.Done) return status;
        isMonochrome = EblcLayout.IsMonochrome(row.BitDepth);
        diagnostic = default;
        return ReadStatus.Done;
    }

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
                Location.AtLocal(DataField.Count, EblcLayout.BitmapSizeAt, sizeIndex),
                out diagnostic, unchecked((ulong)(long)sizeIndex), numSizes);
        status = TryGetBitmapSize(sizeIndex, out row, out diagnostic);
        if (status != ReadStatus.Done) return status;
        listStart = row.IndexSubtableListOffset;
        listEnd = listStart + row.IndexSubtableListSize;
        numSubtables = row.NumIndexSubtables;
        if (listEnd > (ulong)_data.Length)
            return ReadWire.Fail(ReadStatus.Truncated, DiagnosticCode.TruncatedData,
                Location.AtLocal(DataField.Length,
                    EblcLayout.BitmapSizeAt + 48UL * (ulong)sizeIndex + 4, sizeIndex),
                out diagnostic, (ulong)_data.Length - listStart,
                (ulong)row.IndexSubtableListSize);
        diagnostic = default;
        return ReadStatus.Done;
    }

    public ReadStatus TryReadTable(int ebdtDataLength, int? numGlyphs, out EblcTable? model,
        out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        model = null;
        var budget = new BuildBudget(limits);
        if (!budget.Input(_data.Length, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        return TryReadTable(ebdtDataLength, numGlyphs, out model, out diagnostic, budget);
    }

    internal ReadStatus TryReadTable(int ebdtDataLength, int? numGlyphs, out EblcTable? model,
        out FontDiagnostic diagnostic, BuildBudget budget)
    {
        model = null;
        ReadStatus status = ValidateTable(ebdtDataLength, numGlyphs, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        status = TryGetMajorVersion(out ushort major, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = TryGetMinorVersion(out ushort minor, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = TryGetNumSizes(out uint numSizes, out diagnostic);
        if (status != ReadStatus.Done) return status;
        long buffer = 48L * (long)numSizes;
        if (!budget.Buffer(buffer, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        ulong consumed = EblcLayout.BitmapSizeAt + 48UL * numSizes;
        var sizes = new EblcStrike[numSizes];
        for (int i = 0; i < numSizes; i++)
        {
            status = TryGetBitmapSize(i, out var row, out diagnostic);
            if (status != ReadStatus.Done) return status;
            ulong listStart = row.IndexSubtableListOffset;
            ulong listEnd = listStart + row.IndexSubtableListSize;
            consumed = Math.Max(consumed, listEnd);
            if (!budget.Buffer(row.NumIndexSubtables * (long)IntPtr.Size, Location, out diagnostic))
                return ReadStatus.ResourceLimit;
            var subtables = new EblcIndexSubtable[row.NumIndexSubtables];
            for (int r = 0; r < row.NumIndexSubtables; r++)
            {
                status = ReadRecord(listStart, r, out var record, out diagnostic);
                if (status != ReadStatus.Done) return status;
                status = DeshapeSubtable(listStart, listEnd, record, budget, out var subtable,
                    out diagnostic);
                if (status != ReadStatus.Done) return status;
                subtables[r] = subtable;
            }
            var strike = new EblcStrike(row.IndexSubtableListOffset,
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
