using System.Buffers.Binary;
using FontWeave.Core;

namespace FontWeave.Tables;

public static class LtshTableCompiler
{
    public static ReadStatus TryCompile(LtshTable model, out byte[]? output,
        out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        output = null;
        var budget = new BuildBudget(limits);
        int length = LtshLayout.HeaderLength + model.NumGlyphs;
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

    public static ReadStatus TryWrite(LtshTable model, Span<byte> destination,
        out FontDiagnostic diagnostic)
    {
        ArgumentNullException.ThrowIfNull(model);
        int length = LtshLayout.HeaderLength + model.NumGlyphs;
        if (destination.Length < length)
            return ReadWire.Fail(ReadStatus.DestinationTooSmall, DiagnosticCode.DestinationTooSmall,
                model.Location.At(DataField.OutputBytes, 0), out diagnostic,
                (ulong)destination.Length, (ulong)length);
        Write(model, destination.Slice(0, length));
        diagnostic = default;
        return ReadStatus.Done;
    }

    public static ReadStatus TryApply(LtshTable model, LtshEditPlan edit,
        out LtshTable? updated, out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(edit);
        ArgumentNullException.ThrowIfNull(edit.Thresholds);
        updated = null;
        var budget = new BuildBudget(limits);
        if (!budget.Work(16L * edit.Thresholds.Count, model.Location, out diagnostic)) return ReadStatus.ResourceLimit;
        foreach (var item in edit.Thresholds)
        {
            if (item.GlyphId < 0 || item.GlyphId >= model.NumGlyphs)
                return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                    model.Location.At(DataField.GlyphId, LtshLayout.HeaderLength, item.GlyphId), out diagnostic,
                    unchecked((ulong)(long)item.GlyphId), model.NumGlyphs);
            if (item.YPixels is < byte.MinValue or > byte.MaxValue)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    ThresholdLocation(model, item.GlyphId), out diagnostic, unchecked((ulong)(long)item.YPixels));
        }
        if (!budget.Work(model.NumGlyphs, model.Location, out diagnostic) ||
            !budget.Buffer(model.NumGlyphs + 16L * edit.Thresholds.Count, model.Location, out diagnostic)) return ReadStatus.ResourceLimit;
        var requests = edit.Thresholds.ToArray();
        Array.Sort(requests, static (a, b) => a.GlyphId.CompareTo(b.GlyphId));
        for (int i = 1; i < requests.Length; i++)
            if (requests[i].GlyphId == requests[i - 1].GlyphId)
                return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.DuplicateGlyph,
                    ThresholdLocation(model, requests[i].GlyphId), out diagnostic, (uint)requests[i].GlyphId);
        byte[] thresholds = model.YPixels.ToArray();
        bool changed = false;
        foreach (var item in requests)
        {
            changed |= thresholds[item.GlyphId] != item.YPixels;
            thresholds[item.GlyphId] = (byte)item.YPixels;
        }
        if (!changed)
            return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
                model.Location, out diagnostic);
        updated = new(thresholds, model.Location, model.MaxpLocation);
        diagnostic = default;
        return ReadStatus.Done;
    }

    private static DataLocation ThresholdLocation(LtshTable model, int glyphId) =>
        model.Location.At(DataField.Payload, LtshLayout.HeaderLength + (ulong)glyphId, glyphId);

    private static void Write(LtshTable model, Span<byte> bytes)
    {
        BinaryPrimitives.WriteUInt16BigEndian(bytes, 0);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.Slice(2), model.NumGlyphs);
        model.YPixels.CopyTo(bytes.Slice(LtshLayout.HeaderLength));
    }
}
