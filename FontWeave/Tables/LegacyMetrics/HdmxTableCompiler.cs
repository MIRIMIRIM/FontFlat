using FontWeave.Core;

namespace FontWeave.Tables;

public static class HdmxTableCompiler
{
    public static ReadStatus TryCompile(HdmxTable model, out byte[]? output,
        out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        output = null;
        var budget = new BuildBudget(limits);
        int length = Length(model);
        if (!budget.Work(length, model.Location, out diagnostic) ||
            !budget.Output(length, model.Location, out diagnostic) ||
            !budget.Buffer(length, model.Location, out diagnostic)) return ReadStatus.ResourceLimit;
        var bytes = new byte[length];
        Write(model, bytes);
        budget.RecordCompiledTable(length);
        output = bytes;
        diagnostic = default;
        return ReadStatus.Done;
    }

    public static ReadStatus TryWrite(HdmxTable model, Span<byte> destination,
        out FontDiagnostic diagnostic)
    {
        ArgumentNullException.ThrowIfNull(model);
        int length = Length(model);
        if (destination.Length < length)
            return ReadWire.Fail(ReadStatus.DestinationTooSmall, DiagnosticCode.DestinationTooSmall,
                model.Location.At(DataField.OutputBytes, 0), out diagnostic,
                (ulong)destination.Length, (ulong)length);
        Write(model, destination.Slice(0, length));
        diagnostic = default;
        return ReadStatus.Done;
    }

    public static ReadStatus TryApply(HdmxTable model, HdmxEditPlan edit,
        out HdmxTable? updated, out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(edit);
        ArgumentNullException.ThrowIfNull(edit.Widths);
        updated = null;
        var budget = new BuildBudget(limits);
        if (!budget.Work(32L * edit.Widths.Count, model.Location, out diagnostic)) return ReadStatus.ResourceLimit;
        foreach (var item in edit.Widths)
        {
            if (item.RecordOrdinal < 0 || item.RecordOrdinal >= model.NumRecords)
                return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                    model.Location.At(DataField.Count, 2, item.RecordOrdinal), out diagnostic,
                    unchecked((ulong)(long)item.RecordOrdinal), model.NumRecords);
            if (item.GlyphId < 0 || item.GlyphId >= model.NumGlyphs)
                return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                    model.Location.At(DataField.GlyphId, RecordPosition(model, item.RecordOrdinal), item.GlyphId),
                    out diagnostic, unchecked((ulong)(long)item.GlyphId), model.NumGlyphs);
            if (item.Width is < byte.MinValue or > byte.MaxValue)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    WidthLocation(model, item), out diagnostic, unchecked((ulong)(long)item.Width));
        }
        long ownedBytes = (long)model.NumRecords * (model.NumGlyphs + IntPtr.Size + 48L);
        if (!budget.Work(Length(model), model.Location, out diagnostic) ||
            !budget.Buffer(ownedBytes + 24L * edit.Widths.Count, model.Location, out diagnostic)) return ReadStatus.ResourceLimit;
        var requests = edit.Widths.ToArray();
        Array.Sort(requests, static (a, b) => a.RecordOrdinal != b.RecordOrdinal
            ? a.RecordOrdinal.CompareTo(b.RecordOrdinal) : a.GlyphId.CompareTo(b.GlyphId));
        for (int i = 1; i < requests.Length; i++)
            if (requests[i].RecordOrdinal == requests[i - 1].RecordOrdinal && requests[i].GlyphId == requests[i - 1].GlyphId)
                return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.DuplicateGlyph,
                    WidthLocation(model, requests[i]), out diagnostic, (uint)requests[i].GlyphId);
        var records = new HdmxDeviceRecord[model.NumRecords];
        int nextRequest = 0;
        bool changed = false;
        for (int i = 0; i < records.Length; i++)
        {
            var old = model.Records[i];
            byte[] widths = old.Widths.ToArray();
            while (nextRequest < requests.Length && requests[nextRequest].RecordOrdinal == i)
            {
                var request = requests[nextRequest++];
                changed |= widths[request.GlyphId] != request.Width;
                widths[request.GlyphId] = (byte)request.Width;
            }
            records[i] = new(old.PixelSize, Maximum(widths), widths);
        }
        if (!changed)
            return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
                model.Location, out diagnostic);
        updated = new(model.NumGlyphs, model.SourceSizeDeviceRecord, records, model.Location, model.MaxpLocation);
        diagnostic = default;
        return ReadStatus.Done;
    }

    private static int Length(HdmxTable model) =>
        HdmxLayout.HeaderLength + model.NumRecords * HdmxLayout.CanonicalStride(model.NumGlyphs);

    private static ulong RecordPosition(HdmxTable model, int ordinal) =>
        HdmxLayout.HeaderLength + (ulong)ordinal * model.SourceSizeDeviceRecord;

    private static DataLocation WidthLocation(HdmxTable model, HdmxWidthEdit edit) => model.Location.At(
        DataField.Payload, RecordPosition(model, edit.RecordOrdinal) + HdmxLayout.RecordHeaderLength + (uint)edit.GlyphId,
        edit.GlyphId);

    private static byte Maximum(ReadOnlySpan<byte> widths)
    {
        byte maximum = 0;
        foreach (byte width in widths) maximum = Math.Max(maximum, width);
        return maximum;
    }

    private static void Write(HdmxTable model, Span<byte> bytes)
    {
        // The zero padding between the last width and the canonical stride is the write
        // half of the read contract (non-zero padding is tolerated, compilation writes
        // zeros), so the clear stays a semantic owner here.
        bytes.Clear();
        // After the capacity preflight these generated writers cannot fail; a failure is
        // a writer contract bug and surfaces loudly. The freshly allocated zero version
        // bits also go through the declared writer (pclt precedent). The width tail
        // stays a span copy: per-byte generated writes would add NumGlyphs calls per
        // record with no structural win (OQ-M2a-2 keep-churn).
        int stride = HdmxLayout.CanonicalStride(model.NumGlyphs);
        RequireWritten(HdmxView.TryWriteVersion(bytes, 0, out _, model.Location));
        RequireWritten(HdmxView.TryWriteNumRecords(bytes, model.NumRecords, out _, model.Location));
        RequireWritten(HdmxView.TryWriteSizeDeviceRecord(bytes, (uint)stride, out _, model.Location));
        for (int i = 0; i < model.NumRecords; i++)
        {
            var record = model.Records[i];
            ulong position = (ulong)(HdmxLayout.HeaderLength + i * stride);
            RequireWritten(HdmxView.TryWriteDeviceRecordHeaderAt(bytes, position,
                new HdmxView.DeviceRecordHeaderData(i, record.PixelSize, Maximum(record.Widths)),
                out _, model.Location));
            record.Widths.CopyTo(bytes.Slice((int)position + HdmxLayout.RecordHeaderLength));
        }
    }

    // Same contract as the pclt emission path: a preflighted write failing is a
    // mechanical bug, not a caller-facing outcome; it must never be silently discarded.
    private static void RequireWritten(ReadStatus status)
    {
        if (status != ReadStatus.Done) throw new InvalidOperationException("A preflighted hdmx write failed.");
    }
}
