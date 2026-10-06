using FontWeave.Core;

namespace FontWeave.Tables;

public static class LocaTableCompiler
{
    public static ReadStatus TryCompile(LocaTable model, out byte[]? output,
        out FontDiagnostic diagnostic, BuildLimits? limits = null) =>
        TryCompile(model, out output, out diagnostic, new BuildBudget(limits));

    internal static ReadStatus TryCompile(LocaTable model, out byte[]? output,
        out FontDiagnostic diagnostic, BuildBudget budget)
    {
        ArgumentNullException.ThrowIfNull(model);
        output = null;
        int length = LengthOf(model);
        if (length < 0)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                model.Location.At(DataField.Offset, 0), out diagnostic,
                model.Offsets[^1], ushort.MaxValue * (uint)LocaLayout.ShortScale);
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

    public static ReadStatus TryWrite(LocaTable model, Span<byte> destination,
        out FontDiagnostic diagnostic)
    {
        ArgumentNullException.ThrowIfNull(model);
        int length = LengthOf(model);
        if (length < 0 || destination.Length < length)
            return ReadWire.Fail(ReadStatus.DestinationTooSmall, DiagnosticCode.DestinationTooSmall,
                model.Location.At(DataField.OutputBytes, 0), out diagnostic,
                (ulong)destination.Length, length < 0 ? 0UL : (ulong)length);
        Write(model, destination);
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>Rewrites one glyph's [start, end) extent, shifting every later offset by the
    /// same delta so all following glyph bytes keep their relative placement. A negative
    /// delta is allowed only when it does not make the range cross its neighbours.</summary>
    public static ReadStatus TrySetGlyphRange(LocaTable model, int glyphId, uint start, uint end,
        out LocaTable? updated, out FontDiagnostic diagnostic, BuildLimits? limits = null) =>
        TrySetGlyphRange(model, glyphId, start, end, out updated, out diagnostic,
            new BuildBudget(limits));

    internal static ReadStatus TrySetGlyphRange(LocaTable model, int glyphId, uint start, uint end,
        out LocaTable? updated, out FontDiagnostic diagnostic, BuildBudget budget)
    {
        ArgumentNullException.ThrowIfNull(model);
        updated = null;
        if (glyphId < 0 || glyphId >= model.NumGlyphs)
            return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                model.Location.At(DataField.GlyphId, 0, glyphId), out diagnostic,
                unchecked((ulong)(long)glyphId), model.NumGlyphs);
        var offsets = model.Offsets;
        if (end < start)
            return ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.InvalidValue,
                model.Location.At(DataField.Offset, 0, glyphId), out diagnostic, end, start);
        if (glyphId > 0 && start < offsets[glyphId - 1])
            return ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.InvalidValue,
                model.Location.At(DataField.Offset, 0, glyphId), out diagnostic,
                start, offsets[glyphId - 1]);
        if (glyphId + 1 < offsets.Length - 1 && end > offsets[glyphId + 2])
            return ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.InvalidValue,
                model.Location.At(DataField.Offset, 0, glyphId + 1), out diagnostic,
                end, offsets[glyphId + 2]);
        if (!budget.Work(8L * offsets.Length, model.Location, out diagnostic) ||
            !budget.Buffer(4L * offsets.Length, model.Location, out diagnostic))
            return ReadStatus.ResourceLimit;
        long delta = (long)end - offsets[glyphId + 1];
        var next = offsets.ToArray();
        next[glyphId] = start;
        next[glyphId + 1] = end;
        for (int i = glyphId + 2; i < next.Length; i++)
        {
            long shifted = next[i] + delta;
            if (shifted < 0 || shifted > uint.MaxValue)
                return ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.InvalidValue,
                    model.Location.At(DataField.Offset, 0, i), out diagnostic,
                    unchecked((ulong)shifted), uint.MaxValue);
            next[i] = (uint)shifted;
        }
        // The trailing extent must never regress below the edited end.
        for (int i = 1; i < next.Length; i++)
            if (next[i] < next[i - 1])
                return ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.InvalidValue,
                    model.Location.At(DataField.Offset, 0, i), out diagnostic,
                    next[i], next[i - 1]);
        if (!CanEncode(next, model.LongFormat))
            return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.InvalidValue,
                model.Location.At(DataField.Offset, 0, glyphId), out diagnostic,
                next[^1], ushort.MaxValue * (uint)LocaLayout.ShortScale);
        updated = new(next, model.LongFormat, model.Location, model.HeadLocation,
            model.MaxpLocation);
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>Re-encodes the same offsets in the other format. Short format cannot
    /// represent an odd or out-of-range offset, so the switch is explicitly rejected then.</summary>
    public static ReadStatus TryReencode(LocaTable model, bool longFormat,
        out LocaTable? updated, out FontDiagnostic diagnostic, BuildLimits? limits = null) =>
        TryReencode(model, longFormat, out updated, out diagnostic, new BuildBudget(limits));

    internal static ReadStatus TryReencode(LocaTable model, bool longFormat,
        out LocaTable? updated, out FontDiagnostic diagnostic, BuildBudget budget)
    {
        ArgumentNullException.ThrowIfNull(model);
        updated = null;
        if (longFormat == model.LongFormat)
            return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
                model.Location.At(DataField.IndexToLocFormat, 0), out diagnostic,
                longFormat ? 1UL : 0UL, model.LongFormat ? 1UL : 0UL);
        if (!budget.Work(4L * model.Offsets.Length, model.Location, out diagnostic))
            return ReadStatus.ResourceLimit;
        if (!CanEncode(model.Offsets, longFormat))
        {
            uint bad = FirstUnencodable(model.Offsets);
            return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.InvalidValue,
                model.Location.At(DataField.Offset, 0), out diagnostic, bad,
                ushort.MaxValue * (uint)LocaLayout.ShortScale);
        }
        updated = new(model.Offsets.ToArray(), longFormat, model.Location, model.HeadLocation,
            model.MaxpLocation);
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>Builds a loca model from offsets that a glyf re-encode produced. The caller
    /// is responsible for the offsets describing the same glyph count as this format's
    /// context; unrunnable content is rejected rather than accepted silently.</summary>
    public static ReadStatus TryFromOffsets(LocaTable shape, ReadOnlySpan<uint> offsets,
        out LocaTable? model, out FontDiagnostic diagnostic)
    {
        ArgumentNullException.ThrowIfNull(shape);
        model = null;
        if (offsets.Length != shape.Offsets.Length)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                shape.Location.At(DataField.Count, 0), out diagnostic,
                (ulong)offsets.Length, (ulong)shape.Offsets.Length);
        for (int i = 0; i < offsets.Length; i++)
        {
            if (i > 0 && offsets[i] < offsets[i - 1])
                return ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.InvalidValue,
                    shape.Location.At(DataField.Offset, 0, i), out diagnostic,
                    offsets[i], offsets[i - 1]);
            if (!shape.LongFormat && (offsets[i] & 1) != 0)
                return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.InvalidValue,
                    shape.Location.At(DataField.Offset, 0, i), out diagnostic,
                    offsets[i], 0);
        }
        if (!CanEncode(offsets, shape.LongFormat))
            return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.InvalidValue,
                shape.Location.At(DataField.Offset, 0), out diagnostic,
                FirstUnencodable(offsets), ushort.MaxValue * (uint)LocaLayout.ShortScale);
        model = new(offsets.ToArray(), shape.LongFormat, shape.Location, shape.HeadLocation,
            shape.MaxpLocation);
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>The head value this model requires. Callers write head themselves; this
    /// compiler never mutates or emits head bytes.</summary>
    public static short RequiredIndexToLocFormat(LocaTable model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return model.LongFormat ? (short)1 : (short)0;
    }

    private static bool CanEncode(ReadOnlySpan<uint> offsets, bool longFormat)
    {
        if (longFormat) return true;
        foreach (uint value in offsets)
            if ((value & 1) != 0 || value > ushort.MaxValue * (uint)LocaLayout.ShortScale)
                return false;
        return true;
    }

    private static uint FirstUnencodable(ReadOnlySpan<uint> offsets)
    {
        foreach (uint value in offsets)
            if ((value & 1) != 0 || value > ushort.MaxValue * (uint)LocaLayout.ShortScale)
                return value;
        return 0;
    }

    private static int LengthOf(LocaTable model) => model.LongFormat
        ? (model.NumGlyphs + 1) * LocaLayout.LongStride
        : CanEncode(model.Offsets, false) ? (model.NumGlyphs + 1) * LocaLayout.ShortStride : -1;

    private static void Write(LocaTable model, Span<byte> bytes)
    {
        // After the capacity preflight these generated writers cannot fail; a failure
        // is a writer contract bug and surfaces loudly.
        for (int i = 0; i < model.Offsets.Length; i++)
        {
            if (model.LongFormat)
            {
                RequireWritten(LocaView.TryWriteLongOffsetAt(bytes, (ulong)i * LocaLayout.LongStride,
                    new LocaView.LongOffsetData(i, model.Offsets[i]), out _, model.Location));
            }
            else
            {
                // Half-value encoding: the write half of the raw-stores-encoded contract.
                RequireWritten(LocaView.TryWriteShortOffsetAt(bytes, (ulong)i * LocaLayout.ShortStride,
                    new LocaView.ShortOffsetData(i, (ushort)(model.Offsets[i] / LocaLayout.ShortScale)),
                    out _, model.Location));
            }
        }
    }

    // Same contract as the vorg emission path: a preflighted write failing is a
    // mechanical bug, not a caller-facing outcome; it must never be silently discarded.
    private static void RequireWritten(ReadStatus status)
    {
        if (status != ReadStatus.Done) throw new InvalidOperationException("A preflighted loca write failed.");
    }
}
