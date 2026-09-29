using System.Buffers.Binary;
using FontWeave.Core;

namespace FontWeave.Tables;

public static class VorgTableCompiler
{
    public static ReadStatus TryCompile(VorgTable model, out byte[]? output,
        out FontDiagnostic diagnostic, BuildLimits? limits = null) =>
        TryCompile(model, out output, out diagnostic, new BuildBudget(limits));

    internal static ReadStatus TryCompile(VorgTable model, out byte[]? output,
        out FontDiagnostic diagnostic, BuildBudget budget)
    {
        ArgumentNullException.ThrowIfNull(model);
        output = null;
        int length = VorgLayout.HeaderLength + VorgLayout.RecordStride * model.NumVertOriginYMetrics;
        if (!budget.Work(length, model.Location, out diagnostic) ||
            !budget.Output(length, model.Location, out diagnostic) ||
            !budget.Buffer(length, model.Location, out diagnostic)) return ReadStatus.ResourceLimit;
        byte[] bytes = new byte[length];
        Write(model, bytes);
        budget.RecordCompiledTable(length);
        output = bytes;
        diagnostic = default;
        return ReadStatus.Done;
    }

    public static ReadStatus TryWrite(VorgTable model, Span<byte> destination,
        out FontDiagnostic diagnostic)
    {
        ArgumentNullException.ThrowIfNull(model);
        int length = VorgLayout.HeaderLength + VorgLayout.RecordStride * model.NumVertOriginYMetrics;
        if (destination.Length < length)
            return ReadWire.Fail(ReadStatus.DestinationTooSmall, DiagnosticCode.DestinationTooSmall,
                model.Location.At(DataField.OutputBytes, 0), out diagnostic,
                (ulong)destination.Length, (ulong)length);
        Write(model, destination);
        diagnostic = default;
        return ReadStatus.Done;
    }

    public static ReadStatus TryApply(VorgTable model, VorgEditPlan edit,
        out VorgTable? updated, out FontDiagnostic diagnostic, BuildLimits? limits = null) =>
        TryApply(model, edit, out updated, out diagnostic, new BuildBudget(limits));

    internal static ReadStatus TryApply(VorgTable model, VorgEditPlan edit,
        out VorgTable? updated, out FontDiagnostic diagnostic, BuildBudget budget)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(edit);
        ArgumentNullException.ThrowIfNull(edit.Records);
        updated = null;
        if (!budget.Work(32L * edit.Records.Count, model.Location, out diagnostic)) return ReadStatus.ResourceLimit;
        if (edit.DefaultVertOriginY is < short.MinValue or > short.MaxValue)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                model.Location.At(DataField.Payload, 4), out diagnostic);
        foreach (var item in edit.Records)
        {
            if (item.GlyphId < 0 || item.GlyphId >= model.NumGlyphs)
                return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                    model.Location.At(DataField.GlyphId, 0, item.GlyphId), out diagnostic,
                    unchecked((ulong)(long)item.GlyphId), model.NumGlyphs);
            int ordinal = model.FindRecord(item.GlyphId);
            if (ordinal < 0)
                return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
                    model.Location.At(DataField.GlyphId, 0, item.GlyphId), out diagnostic, (uint)item.GlyphId);
            if (item.VertOriginY is < short.MinValue or > short.MaxValue)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    model.Location.At(DataField.Payload,
                        VorgLayout.HeaderLength + (ulong)ordinal * VorgLayout.RecordStride + 2, ordinal),
                    out diagnostic);
        }
        long bytes = VorgLayout.RecordStride * (long)model.NumVertOriginYMetrics;
        if (!budget.Work(bytes, model.Location, out diagnostic) ||
            !budget.Buffer(bytes + 16L * edit.Records.Count, model.Location, out diagnostic)) return ReadStatus.ResourceLimit;
        var requests = edit.Records.ToArray();
        Array.Sort(requests, static (a, b) => a.GlyphId.CompareTo(b.GlyphId));
        for (int i = 1; i < requests.Length; i++)
            if (requests[i].GlyphId == requests[i - 1].GlyphId)
                return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.DuplicateGlyph,
                    RecordLocation(model, model.FindRecord(requests[i].GlyphId)), out diagnostic,
                    (uint)requests[i].GlyphId);
        var records = model.Records.ToArray();
        short defaultY = (short)(edit.DefaultVertOriginY ?? model.DefaultVertOriginY);
        bool changed = defaultY != model.DefaultVertOriginY;
        foreach (var item in requests)
        {
            int ordinal = model.FindRecord(item.GlyphId);
            var value = new VorgRecord((ushort)item.GlyphId, (short)item.VertOriginY);
            changed |= records[ordinal] != value;
            records[ordinal] = value;
        }
        if (!changed)
            return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
                model.Location, out diagnostic);
        updated = new(defaultY, records, model.NumGlyphs, model.Location, model.MaxpLocation);
        diagnostic = default;
        return ReadStatus.Done;
    }

    private static DataLocation RecordLocation(VorgTable model, int ordinal) =>
        model.Location.At(DataField.GlyphId,
            VorgLayout.HeaderLength + (ulong)ordinal * VorgLayout.RecordStride, ordinal);

    private static void Write(VorgTable model, Span<byte> bytes)
    {
        BinaryPrimitives.WriteUInt16BigEndian(bytes, model.MajorVersion);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.Slice(2), model.MinorVersion);
        BinaryPrimitives.WriteInt16BigEndian(bytes.Slice(4), model.DefaultVertOriginY);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.Slice(6), model.NumVertOriginYMetrics);
        int at = VorgLayout.HeaderLength;
        foreach (var record in model.Records)
        {
            BinaryPrimitives.WriteUInt16BigEndian(bytes.Slice(at), record.GlyphIndex);
            BinaryPrimitives.WriteInt16BigEndian(bytes.Slice(at + 2), record.VertOriginY);
            at += VorgLayout.RecordStride;
        }
    }
}
