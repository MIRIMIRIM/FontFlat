using FontWeave.Core;

namespace FontWeave.Tables;

public readonly ref partial struct CmapView
{
    /// <summary>Mechanical subtable extent: record offset plus the dispatch length field
    /// (format 14 stores uint32 at +2, every other known family uint16 at +2).</summary>
    public ReadStatus TryGetSubtableExtent(int recordOrdinal, out ushort format, out uint offset,
        out ulong length, out FontDiagnostic diagnostic)
    {
        format = 0; offset = 0; length = 0;
        ReadStatus status = TryGetEncodingRecord(recordOrdinal, out var record, out diagnostic);
        if (status != ReadStatus.Done) return status;
        offset = record.SubtableOffset;
        if (offset > (ulong)_data.Length - 2)
            return ReadWire.Fail(ReadStatus.Truncated, DiagnosticCode.TruncatedData,
                Location.AtLocal(DataField.Offset, offset, recordOrdinal), out diagnostic,
                (ulong)_data.Length, offset + 2);
        format = ReadWire.U16(_data, (int)offset);
        // Dispatch length fields: format 14 stores uint32 at +2, formats 8/10/12/13 at +4
        // (after their reserved field), every other known family uint16 at +2.
        int lengthOffset = format switch { 14 => 2, 8 or 10 or 12 or 13 => 4, _ => 2 };
        int lengthWidth = format is 8 or 10 or 12 or 13 or 14 ? 4 : 2;
        if (offset > (ulong)_data.Length - (ulong)(uint)(lengthOffset + lengthWidth))
            return ReadWire.Fail(ReadStatus.Truncated, DiagnosticCode.TruncatedData,
                Location.AtLocal(DataField.Length, offset + (ulong)(uint)lengthOffset, recordOrdinal),
                out diagnostic, (ulong)_data.Length,
                offset + (ulong)(uint)(lengthOffset + lengthWidth));
        length = lengthWidth == 4
            ? ReadWire.U32(_data, (int)offset + lengthOffset)
            : ReadWire.U16(_data, (int)offset + lengthOffset);
        if (length < (ulong)(uint)(lengthOffset + lengthWidth) || offset + length > (ulong)_data.Length)
            return ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.RangeOutsideSource,
                Location.AtLocal(DataField.Length, offset + (ulong)(uint)lengthOffset, recordOrdinal),
                out diagnostic, offset + length, (ulong)_data.Length);
        return ReadStatus.Done;
    }

    public ReadStatus TryOpenFormat0(int recordOrdinal, out CmapFormat0View view,
        out FontDiagnostic diagnostic)
    {
        view = default;
        ReadStatus status = TryGetSubtableExtent(recordOrdinal, out ushort format, out uint offset,
            out ulong length, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (format != 0)
            return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedVersion,
                Location.AtLocal(DataField.Version, offset, recordOrdinal), out diagnostic,
                format, 0);
        return CmapFormat0View.TryOpen(_data.Slice(checked((int)offset), checked((int)length)),
            out view, out diagnostic, Location.AtLocal(DataField.Offset, offset, recordOrdinal));
    }

    public ReadStatus TryOpenFormat2(int recordOrdinal, out CmapFormat2View view,
        out FontDiagnostic diagnostic)
    {
        view = default;
        ReadStatus status = TryGetSubtableExtent(recordOrdinal, out ushort format, out uint offset,
            out ulong length, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (format != 2)
            return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedVersion,
                Location.AtLocal(DataField.Version, offset, recordOrdinal), out diagnostic,
                format, 2);
        return CmapFormat2View.TryOpen(_data.Slice(checked((int)offset), checked((int)length)),
            out view, out diagnostic, Location.AtLocal(DataField.Offset, offset, recordOrdinal));
    }

    public ReadStatus TryOpenFormat6(int recordOrdinal, out CmapFormat6View view,
        out FontDiagnostic diagnostic)
    {
        view = default;
        ReadStatus status = TryGetSubtableExtent(recordOrdinal, out ushort format, out uint offset,
            out ulong length, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (format != 6)
            return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedVersion,
                Location.AtLocal(DataField.Version, offset, recordOrdinal), out diagnostic,
                format, 6);
        return CmapFormat6View.TryOpen(_data.Slice(checked((int)offset), checked((int)length)),
            out view, out diagnostic, Location.AtLocal(DataField.Offset, offset, recordOrdinal));
    }

    public ReadStatus TryOpenFormat4(int recordOrdinal, out CmapFormat4View view,
        out FontDiagnostic diagnostic)
    {
        view = default;
        ReadStatus status = TryGetSubtableExtent(recordOrdinal, out ushort format, out uint offset,
            out ulong length, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (format != 4)
            return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedVersion,
                Location.AtLocal(DataField.Version, offset, recordOrdinal), out diagnostic,
                format, 4);
        return CmapFormat4View.TryOpen(_data.Slice(checked((int)offset), checked((int)length)),
            out view, out diagnostic, Location.AtLocal(DataField.Offset, offset, recordOrdinal));
    }

    public ReadStatus TryOpenFormat12(int recordOrdinal, out CmapFormat12View view,
        out FontDiagnostic diagnostic)
    {
        view = default;
        ReadStatus status = TryGetSubtableExtent(recordOrdinal, out ushort format, out uint offset,
            out ulong length, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (format != 12)
            return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedVersion,
                Location.AtLocal(DataField.Version, offset, recordOrdinal), out diagnostic,
                format, 12);
        return CmapFormat12View.TryOpen(_data.Slice(checked((int)offset), checked((int)length)),
            out view, out diagnostic, Location.AtLocal(DataField.Offset, offset, recordOrdinal));
    }

    public ReadStatus TryOpenFormat13(int recordOrdinal, out CmapFormat13View view,
        out FontDiagnostic diagnostic)
    {
        view = default;
        ReadStatus status = TryGetSubtableExtent(recordOrdinal, out ushort format, out uint offset,
            out ulong length, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (format != 13)
            return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedVersion,
                Location.AtLocal(DataField.Version, offset, recordOrdinal), out diagnostic,
                format, 13);
        return CmapFormat13View.TryOpen(_data.Slice(checked((int)offset), checked((int)length)),
            out view, out diagnostic, Location.AtLocal(DataField.Offset, offset, recordOrdinal));
    }

    public ReadStatus TryOpenFormat14(int recordOrdinal, out CmapFormat14View view,
        out FontDiagnostic diagnostic)
    {
        view = default;
        ReadStatus status = TryGetSubtableExtent(recordOrdinal, out ushort format, out uint offset,
            out ulong length, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (format != 14)
            return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedVersion,
                Location.AtLocal(DataField.Version, offset, recordOrdinal), out diagnostic,
                format, 14);
        return CmapFormat14View.TryOpen(_data.Slice(checked((int)offset), checked((int)length)),
            out view, out diagnostic, Location.AtLocal(DataField.Offset, offset, recordOrdinal));
    }

    public ReadStatus TryOpenFormat8(int recordOrdinal, out CmapFormat8View view,
        out FontDiagnostic diagnostic)
    {
        view = default;
        ReadStatus status = TryGetSubtableExtent(recordOrdinal, out ushort format, out uint offset,
            out ulong length, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (format != 8)
            return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedVersion,
                Location.AtLocal(DataField.Version, offset, recordOrdinal), out diagnostic,
                format, 8);
        return CmapFormat8View.TryOpen(_data.Slice(checked((int)offset), checked((int)length)),
            out view, out diagnostic, Location.AtLocal(DataField.Offset, offset, recordOrdinal));
    }

    public ReadStatus TryOpenFormat10(int recordOrdinal, out CmapFormat10View view,
        out FontDiagnostic diagnostic)
    {
        view = default;
        ReadStatus status = TryGetSubtableExtent(recordOrdinal, out ushort format, out uint offset,
            out ulong length, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (format != 10)
            return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedVersion,
                Location.AtLocal(DataField.Version, offset, recordOrdinal), out diagnostic,
                format, 10);
        return CmapFormat10View.TryOpen(_data.Slice(checked((int)offset), checked((int)length)),
            out view, out diagnostic, Location.AtLocal(DataField.Offset, offset, recordOrdinal));
    }

    /// <summary>Strict table validation: version, ordered unique records, extents that tile the
    /// table exactly with exact aliasing, and typed rules for every declared subtable format
    /// (0/2/4/6/8/10/12/13/14).</summary>
    public ReadStatus Validate(CmapContext context, out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        var budget = new BuildBudget(limits);
        if (!budget.Input(_data.Length, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        return Validate(context, out diagnostic, budget);
    }

    internal ReadStatus Validate(CmapContext context, out FontDiagnostic diagnostic, BuildBudget budget)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!IsBound)
            return ReadWire.Fail(ReadStatus.Uninitialized, DiagnosticCode.UnboundView, Location, out diagnostic);
        if (Location != context.Location)
            return ReadWire.Fail(ReadStatus.InvalidSource, DiagnosticCode.WrongSourceFace,
                Location, out diagnostic);
        ReadStatus status = TryRequireKnownVersion(out _, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (NumTables == 0)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.AtLocal(DataField.Count, 2), out diagnostic, 0, 1);
        long recordsEnd = CmapLayout.HeaderLength + (long)CmapLayout.RecordStride * NumTables;
        if (_data.Length < recordsEnd)
            return ReadWire.Fail(ReadStatus.Truncated, DiagnosticCode.TruncatedData,
                Location.AtLocal(DataField.Count, 2), out diagnostic, (ulong)_data.Length, (ulong)recordsEnd);
        if (!budget.Buffer(32L * NumTables, Location, out diagnostic) ||
            !budget.Work(_data.Length, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        var extents = new SubtableExtent[NumTables];
        int distinct = 0;
        int previousPlatform = -1, previousEncoding = -1;
        for (int i = 0; i < NumTables; i++)
        {
            status = TryGetEncodingRecord(i, out var record, out diagnostic);
            if (status != ReadStatus.Done) return status;
            if (i > 0 && (record.PlatformId < previousPlatform ||
                (record.PlatformId == previousPlatform && record.EncodingId <= previousEncoding)))
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    Location.AtLocal(DataField.PlatformId, (ulong)(4 + 8L * i), i), out diagnostic,
                    (uint)((record.PlatformId << 16) | record.EncodingId),
                    (uint)((previousPlatform << 16) | previousEncoding));
            previousPlatform = record.PlatformId;
            previousEncoding = record.EncodingId;
            if (record.SubtableOffset < (ulong)recordsEnd)
                return ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.StorageBeforeRecords,
                    Location.AtLocal(DataField.Offset, (ulong)(12 + 8L * i), i), out diagnostic,
                    record.SubtableOffset, (ulong)recordsEnd);
            status = TryGetSubtableExtent(i, out ushort format, out uint offset, out ulong length,
                out diagnostic);
            if (status != ReadStatus.Done) return status;
            int found = -1;
            for (int j = 0; j < distinct; j++)
                if (extents[j].Offset == offset) { found = j; break; }
            if (found < 0)
                extents[distinct++] = new(offset, checked((uint)length), format,
                    record.PlatformId != 1, i);
            else if (extents[found].Length != length || extents[found].Format != format)
                return ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.OverlappingRange,
                    Location.AtLocal(DataField.Offset, (ulong)(12 + 8L * i), i), out diagnostic,
                    offset, extents[found].Offset);
            else if (record.PlatformId != 1)
                extents[found] = extents[found] with { RequireZeroLanguage = true };
        }
        Array.Sort(extents, 0, distinct, SubtableExtentOrder.Instance);
        ulong cursor = (ulong)recordsEnd;
        for (int j = 0; j < distinct; j++)
        {
            if (extents[j].Offset < cursor)
                return ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.OverlappingRange,
                    Location.AtLocal(DataField.Offset, extents[j].Offset, extents[j].RecordOrdinal),
                    out diagnostic, extents[j].Offset, cursor);
            if (extents[j].Offset > cursor)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.UnexpectedTrailingData,
                    Location.AtLocal(DataField.Offset, cursor, extents[j].RecordOrdinal),
                    out diagnostic, extents[j].Offset, cursor);
            var slice = _data.Slice((int)extents[j].Offset,
                checked((int)extents[j].Length));
            var location = Location.AtLocal(DataField.Offset, extents[j].Offset, extents[j].RecordOrdinal);
            if (extents[j].Format == 0)
            {
                status = CmapFormat0View.TryOpen(slice, out var format0, out diagnostic, location);
                if (status != ReadStatus.Done) return status;
                status = format0.Validate(extents[j].RequireZeroLanguage, context.NumGlyphs,
                    out diagnostic, budget);
                if (status != ReadStatus.Done) return status;
            }
            else if (extents[j].Format == 2)
            {
                status = CmapFormat2View.TryOpen(slice, out var format2, out diagnostic, location);
                if (status != ReadStatus.Done) return status;
                status = format2.Validate(extents[j].RequireZeroLanguage, context.NumGlyphs,
                    out diagnostic, budget);
                if (status != ReadStatus.Done) return status;
            }
            else if (extents[j].Format == 4)
            {
                status = CmapFormat4View.TryOpen(slice, out var format4, out diagnostic, location);
                if (status != ReadStatus.Done) return status;
                status = format4.Validate(extents[j].RequireZeroLanguage, context.NumGlyphs,
                    out diagnostic, budget);
                if (status != ReadStatus.Done) return status;
            }
            else if (extents[j].Format == 6)
            {
                status = CmapFormat6View.TryOpen(slice, out var format6, out diagnostic, location);
                if (status != ReadStatus.Done) return status;
                status = format6.Validate(extents[j].RequireZeroLanguage, context.NumGlyphs,
                    out diagnostic, budget);
                if (status != ReadStatus.Done) return status;
            }
            else if (extents[j].Format == 12)
            {
                status = CmapFormat12View.TryOpen(slice, out var format12, out diagnostic, location);
                if (status != ReadStatus.Done) return status;
                status = format12.Validate(context.NumGlyphs, out diagnostic, budget);
                if (status != ReadStatus.Done) return status;
            }
            else if (extents[j].Format == 13)
            {
                status = CmapFormat13View.TryOpen(slice, out var format13, out diagnostic, location);
                if (status != ReadStatus.Done) return status;
                status = format13.Validate(context.NumGlyphs, out diagnostic, budget);
                if (status != ReadStatus.Done) return status;
            }
            else if (extents[j].Format == 8)
            {
                status = CmapFormat8View.TryOpen(slice, out var format8, out diagnostic, location);
                if (status != ReadStatus.Done) return status;
                status = format8.Validate(extents[j].RequireZeroLanguage, context.NumGlyphs,
                    out diagnostic, budget);
                if (status != ReadStatus.Done) return status;
            }
            else if (extents[j].Format == 10)
            {
                status = CmapFormat10View.TryOpen(slice, out var format10, out diagnostic, location);
                if (status != ReadStatus.Done) return status;
                status = format10.Validate(extents[j].RequireZeroLanguage, context.NumGlyphs,
                    out diagnostic, budget);
                if (status != ReadStatus.Done) return status;
            }
            else if (extents[j].Format == 14)
            {
                status = CmapFormat14View.TryOpen(slice, out var format14, out diagnostic, location);
                if (status != ReadStatus.Done) return status;
                status = format14.Validate(context.NumGlyphs, out diagnostic, budget);
                if (status != ReadStatus.Done) return status;
            }
            cursor = (ulong)extents[j].Offset + extents[j].Length;
        }
        if (cursor != (ulong)_data.Length)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.UnexpectedTrailingData,
                Location.AtLocal(DataField.Length, cursor), out diagnostic,
                (ulong)_data.Length, cursor);
        diagnostic = default;
        return ReadStatus.Done;
    }

    public ReadStatus TryRead(CmapContext context, out CmapTable? model,
        out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        model = null;
        var budget = new BuildBudget(limits);
        if (!budget.Input(_data.Length, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        return TryRead(context, out model, out diagnostic, budget);
    }

    internal ReadStatus TryRead(CmapContext context, out CmapTable? model,
        out FontDiagnostic diagnostic, BuildBudget budget)
    {
        model = null;
        ReadStatus status = Validate(context, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        var offsets = new uint[NumTables];
        for (int i = 0; i < NumTables; i++)
        {
            _ = TryGetEncodingRecord(i, out var record, out _);
            offsets[i] = record.SubtableOffset;
        }
        // Shared subtables are materialized once, in first-reference order.
        var subtableOffsets = new uint[NumTables];
        var subtableIndexes = new int[NumTables];
        int subtables = 0;
        for (int i = 0; i < NumTables; i++)
        {
            int found = -1;
            for (int j = 0; j < subtables; j++)
                if (subtableOffsets[j] == offsets[i]) { found = j; break; }
            if (found < 0)
            {
                subtableOffsets[subtables++] = offsets[i];
                found = subtables - 1;
            }
            subtableIndexes[i] = found;
        }
        long buffer = 64L * NumTables;
        var subtableObjects = new CmapSubtable[subtables];
        for (int j = 0; j < subtables; j++)
        {
            status = TryGetSubtableExtent(Array.IndexOf(offsets, subtableOffsets[j]),
                out ushort format, out uint offset, out ulong length, out diagnostic);
            if (status != ReadStatus.Done) return status;
            buffer += (long)length + 32;
            if (!budget.Buffer(buffer, Location, out diagnostic)) return ReadStatus.ResourceLimit;
            var location = Location.AtLocal(DataField.Offset, offset, -1);
            var slice = _data.Slice(checked((int)offset), checked((int)length));
            if (format == 0)
            {
                status = CmapFormat0View.TryOpen(slice, out var view0, out diagnostic, location);
                if (status != ReadStatus.Done) return status;
                subtableObjects[j] = MaterializeFormat0(view0, offset, location);
            }
            else if (format == 2)
            {
                status = CmapFormat2View.TryOpen(slice, out var view2, out diagnostic, location);
                if (status != ReadStatus.Done) return status;
                subtableObjects[j] = MaterializeFormat2(view2, offset, location);
            }
            else if (format == 4)
            {
                status = CmapFormat4View.TryOpen(slice, out var view, out diagnostic, location);
                if (status != ReadStatus.Done) return status;
                subtableObjects[j] = MaterializeFormat4(view, offset, location);
            }
            else if (format == 6)
            {
                status = CmapFormat6View.TryOpen(slice, out var view6, out diagnostic, location);
                if (status != ReadStatus.Done) return status;
                subtableObjects[j] = MaterializeFormat6(view6, offset, location);
            }
            else if (format == 12)
            {
                status = CmapFormat12View.TryOpen(slice, out var view, out diagnostic, location);
                if (status != ReadStatus.Done) return status;
                subtableObjects[j] = MaterializeFormat12(view, offset, location);
            }
            else if (format == 13)
            {
                status = CmapFormat13View.TryOpen(slice, out var view13, out diagnostic, location);
                if (status != ReadStatus.Done) return status;
                subtableObjects[j] = MaterializeFormat13(view13, offset, location);
            }
            else if (format == 8)
            {
                status = CmapFormat8View.TryOpen(slice, out var view8, out diagnostic, location);
                if (status != ReadStatus.Done) return status;
                subtableObjects[j] = MaterializeFormat8(view8, offset, location);
            }
            else if (format == 10)
            {
                status = CmapFormat10View.TryOpen(slice, out var view10, out diagnostic, location);
                if (status != ReadStatus.Done) return status;
                subtableObjects[j] = MaterializeFormat10(view10, offset, location);
            }
            else if (format == 14)
            {
                status = CmapFormat14View.TryOpen(slice, out var view14, out diagnostic, location);
                if (status != ReadStatus.Done) return status;
                subtableObjects[j] = MaterializeFormat14(view14, offset, location);
            }
            else
            {
                var raw = new byte[length];
                slice.CopyTo(raw);
                subtableObjects[j] = new CmapRawSubtable(format, offset, raw, location);
            }
        }
        var records = new CmapRecord[NumTables];
        for (int i = 0; i < NumTables; i++)
        {
            _ = TryGetEncodingRecord(i, out var record, out _);
            records[i] = new CmapRecord(record.PlatformId, record.EncodingId,
                record.SubtableOffset, subtableIndexes[i]);
        }
        model = new CmapTable(records, subtableObjects, context.NumGlyphs,
            Location, context.MaxpLocation);
        diagnostic = default;
        return ReadStatus.Done;
    }

    private static CmapFormat4Subtable MaterializeFormat4(CmapFormat4View view, uint sourceOffset,
        DataLocation location)
    {
        int count = view.SegmentCount;
        var segments = new CmapFormat4Segment[count];
        for (int i = 0; i < count; i++)
        {
            _ = view.TryGetStartCode(i, out var startRow, out _);
            _ = view.TryGetEndCode(i, out var endRow, out _);
            _ = view.TryGetIdDelta(i, out var deltaRow, out _);
            _ = view.TryGetIdRangeOffset(i, out var rangeOffsetRow, out _);
            segments[i] = new(startRow.Value, endRow.Value, deltaRow.Value, rangeOffsetRow.Value);
        }
        var bytes = view.RawBytes;
        long glyphEntries = (bytes.Length - view.GlyphArrayStart) / 2;
        var glyphArray = new ushort[glyphEntries];
        for (long i = 0; i < glyphEntries; i++)
            _ = view.TryReadGlyphIdAt((ulong)(view.GlyphArrayStart + 2 * i),
                out glyphArray[i], out _);
        return new CmapFormat4Subtable(view.Language, segments, glyphArray, sourceOffset, location);
    }

    private static CmapFormat0Subtable MaterializeFormat0(CmapFormat0View view, uint sourceOffset,
        DataLocation location)
    {
        var glyphArray = new byte[CmapFormat0Layout.GlyphCount];
        for (int i = 0; i < CmapFormat0Layout.GlyphCount; i++)
        {
            _ = view.TryGetGlyphEntry(i, out var entry, out _);
            glyphArray[i] = entry.Value;
        }
        return new CmapFormat0Subtable(view.Language, glyphArray, sourceOffset, location);
    }

    private static CmapFormat2Subtable MaterializeFormat2(CmapFormat2View view, uint sourceOffset,
        DataLocation location)
    {
        var keys = new ushort[CmapFormat2Layout.KeyCount];
        for (int i = 0; i < CmapFormat2Layout.KeyCount; i++)
        {
            _ = view.TryGetSubHeaderKey(i, out ushort key, out _);
            keys[i] = key;
        }
        int count = view.SubHeaderCount;
        var subHeaders = new CmapFormat2SubHeader[count];
        for (int k = 0; k < count; k++)
        {
            _ = view.TryGetSubHeader(k, out var header, out _);
            subHeaders[k] = new(header.FirstCode, header.EntryCount, header.IdDelta,
                header.IdRangeOffset);
        }
        long glyphEntries = (view.RawBytes.Length - CmapFormat2Layout.KeysEnd - 8L * count) / 2;
        var glyphArray = new ushort[glyphEntries];
        for (long i = 0; i < glyphEntries; i++)
            _ = view.TryReadGlyphAt((ulong)(CmapFormat2Layout.KeysEnd + 8L * count + 2L * i),
                out glyphArray[i], out _);
        return new CmapFormat2Subtable(view.Language, keys, subHeaders, glyphArray,
            sourceOffset, location);
    }

    private static CmapFormat6Subtable MaterializeFormat6(CmapFormat6View view, uint sourceOffset,
        DataLocation location)
    {
        var glyphArray = new ushort[view.EntryCount];
        for (int i = 0; (uint)i < view.EntryCount; i++)
        {
            _ = view.TryGetGlyphEntry(i, out var entry, out _);
            glyphArray[i] = entry.Value;
        }
        return new CmapFormat6Subtable(view.Language, view.FirstCode, glyphArray,
            sourceOffset, location);
    }

    private static CmapFormat12Subtable MaterializeFormat12(CmapFormat12View view, uint sourceOffset,
        DataLocation location)
    {
        var groups = new CmapFormat12Group[view.NumGroups];
        for (int i = 0; (uint)i < view.NumGroups; i++)
        {
            _ = view.TryGetGroup(i, out var group, out _);
            groups[i] = new(group.StartCharCode, group.EndCharCode, group.StartGlyphID);
        }
        return new CmapFormat12Subtable(groups, sourceOffset, location);
    }

    private static CmapFormat13Subtable MaterializeFormat13(CmapFormat13View view, uint sourceOffset,
        DataLocation location)
    {
        var groups = new CmapFormat13Group[view.NumGroups];
        for (int i = 0; (uint)i < view.NumGroups; i++)
        {
            _ = view.TryGetGroup(i, out var group, out _);
            groups[i] = new(group.StartCharCode, group.EndCharCode, group.StartGlyphID);
        }
        return new CmapFormat13Subtable(groups, sourceOffset, location);
    }

    private static CmapFormat8Subtable MaterializeFormat8(CmapFormat8View view, uint sourceOffset,
        DataLocation location)
    {
        var groups = new CmapFormat8Group[view.NumGroups];
        for (int i = 0; (uint)i < view.NumGroups; i++)
        {
            _ = view.TryGetGroup(i, out var group, out _);
            groups[i] = new(group.StartCharCode, group.EndCharCode, group.StartGlyphID);
        }
        // Validation pinned the exact 8208+12n extent, so the fixed 8192-byte bitmap is present.
        var is32 = new byte[CmapFormat8Layout.Is32Length];
        view.RawBytes.Slice(CmapFormat8Layout.Is32Offset, CmapFormat8Layout.Is32Length)
            .CopyTo(is32);
        return new CmapFormat8Subtable(view.Language, groups, is32, sourceOffset, location);
    }

    private static CmapFormat10Subtable MaterializeFormat10(CmapFormat10View view, uint sourceOffset,
        DataLocation location)
    {
        var glyphs = new ushort[view.NumChars];
        for (int i = 0; (uint)i < view.NumChars; i++)
        {
            _ = view.TryGetGlyphEntry(i, out var entry, out _);
            glyphs[i] = entry.Value;
        }
        return new CmapFormat10Subtable(view.Language, view.StartCharCode, glyphs,
            sourceOffset, location);
    }

    private static CmapFormat14Subtable MaterializeFormat14(CmapFormat14View view, uint sourceOffset,
        DataLocation location)
    {
        var selectors = new CmapVarSelectorRecord[(int)view.NumVarSelectorRecords];
        for (int i = 0; i < selectors.Length; i++)
        {
            _ = view.TryGetVarSelector(i, out var selectorRecord, out _);
            uint varSelector = selectorRecord.VarSelector;
            uint defaultOffset = selectorRecord.DefaultUVSOffset;
            uint nonDefaultOffset = selectorRecord.NonDefaultUVSOffset;
            CmapDefaultUvsRange[] defaultRanges = [];
            if (defaultOffset != 0)
            {
                _ = view.TryGetDefaultRangeCount(i, out int rangeCount, out _);
                defaultRanges = new CmapDefaultUvsRange[rangeCount];
                for (int r = 0; r < rangeCount; r++)
                {
                    _ = view.TryGetDefaultRange(i, r, out uint start, out byte additionalCount, out _);
                    defaultRanges[r] = new(start, additionalCount);
                }
            }
            CmapUvsMapping[] nonDefaultMappings = [];
            if (nonDefaultOffset != 0)
            {
                _ = view.TryGetUvsMappingCount(i, out int mappingCount, out _);
                nonDefaultMappings = new CmapUvsMapping[mappingCount];
                for (int r = 0; r < mappingCount; r++)
                {
                    _ = view.TryGetUvsMapping(i, r, out uint unicodeValue, out ushort glyphId, out _);
                    nonDefaultMappings[r] = new(unicodeValue, glyphId);
                }
            }
            selectors[i] = new(varSelector, defaultRanges, nonDefaultMappings);
        }
        return new CmapFormat14Subtable(selectors, sourceOffset, location);
    }

    private readonly record struct SubtableExtent(uint Offset, uint Length, ushort Format,
        bool RequireZeroLanguage, int RecordOrdinal);

    private sealed class SubtableExtentOrder : IComparer<SubtableExtent>
    {
        internal static readonly SubtableExtentOrder Instance = new();
        public int Compare(SubtableExtent x, SubtableExtent y) => x.Offset.CompareTo(y.Offset);
    }
}
