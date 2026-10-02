using FontWeave.Core;

namespace FontWeave.Tables;

/// <summary>External count/format semantics over the sole schema-generated borrowed reader.
/// The bytes carry no header, so every structural fact comes from <see cref="LocaContext"/>.</summary>
public readonly ref partial struct LocaView
{
    /// <summary>Strict: element count, monotonicity and the final bound against glyf.</summary>
    public ReadStatus Validate(LocaContext context, out FontDiagnostic diagnostic)
    {
        ReadStatus status = RequireContext(context, out diagnostic);
        if (status != ReadStatus.Done) return status;
        ulong expected = ExpectedLength(context);
        if ((ulong)_data.Length < expected)
            return FailAt(context, ReadStatus.Truncated, DiagnosticCode.TruncatedData,
                context.NumGlyphs, out diagnostic, (ulong)_data.Length, expected);
        if ((ulong)_data.Length > expected)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.UnexpectedTrailingData,
                Location.At(DataField.Length, expected), out diagnostic, (ulong)_data.Length, expected);
        uint previous = 0;
        for (int i = 0; i <= context.NumGlyphs; i++)
        {
            status = TryGetRawOffset(context, i, out uint value, out diagnostic);
            if (status != ReadStatus.Done) return status;
            if (i > 0 && value < previous)
                return FailAt(context, ReadStatus.InvalidOffset, DiagnosticCode.InvalidValue,
                    i, out diagnostic, value, previous);
            previous = value;
        }
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>Validates loca in isolation, then proves the final offset fits the paired glyf length.
    /// glyfLength must come from the same source face; this never reads glyf itself.</summary>
    public ReadStatus Validate(LocaContext context, int glyfLength,
        out FontDiagnostic diagnostic)
    {
        ReadStatus status = Validate(context, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (glyfLength < 0)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.At(DataField.Length, 0), out diagnostic,
                unchecked((ulong)(long)glyfLength), 0);
        status = TryGetGlyphOffset(context, context.NumGlyphs, out uint last, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (last > (uint)glyfLength)
            return ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.RangeOutsideSource,
                Location.At(DataField.Offset, ElementPosition(context, context.NumGlyphs),
                    context.NumGlyphs), out diagnostic, last, (ulong)glyfLength);
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>Raw stored element (short format keeps the halved value).</summary>
    public ReadStatus TryGetRawOffset(LocaContext context, int ordinal, out uint value,
        out FontDiagnostic diagnostic)
    {
        value = 0;
        ReadStatus status = RequireContext(context, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (ordinal < 0 || ordinal > context.NumGlyphs)
            return InvalidIndex(context, ordinal, out diagnostic);
        ulong at = ElementPosition(context, ordinal);
        if (context.LongFormat)
        {
            status = TryReadLongOffsetAt(at, ordinal, out var record, out diagnostic);
            if (status != ReadStatus.Done) return status;
            value = record.Value;
        }
        else
        {
            status = TryReadShortOffsetAt(at, ordinal, out var record, out diagnostic);
            if (status != ReadStatus.Done) return status;
            value = record.Value;
        }
        return ReadStatus.Done;
    }

    /// <summary>Scaled glyph offset. Short format is multiplied by two.</summary>
    public ReadStatus TryGetGlyphOffset(LocaContext context, int ordinal, out uint value,
        out FontDiagnostic diagnostic)
    {
        ReadStatus status = TryGetRawOffset(context, ordinal, out uint raw, out diagnostic);
        value = 0;
        if (status != ReadStatus.Done) return status;
        value = context.LongFormat ? raw : unchecked(raw * LocaLayout.ShortScale);
        return ReadStatus.Done;
    }

    /// <summary>Half-open [start, end) range of one glyph inside glyf.</summary>
    public ReadStatus TryGetGlyphRange(LocaContext context, int glyphId, out LocaRange range,
        out FontDiagnostic diagnostic)
    {
        range = default;
        ReadStatus status = RequireContext(context, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (glyphId < 0 || glyphId >= context.NumGlyphs)
            return InvalidIndex(context, glyphId, out diagnostic);
        status = TryGetGlyphOffset(context, glyphId, out uint start, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = TryGetGlyphOffset(context, glyphId + 1, out uint end, out diagnostic);
        if (status != ReadStatus.Done) return status;
        range = new(glyphId, start, end);
        return ReadStatus.Done;
    }

    public ReadStatus TryRead(LocaContext context, out LocaTable? model,
        out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        model = null;
        var budget = new BuildBudget(limits);
        if (!budget.Input(_data.Length, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        return TryRead(context, out model, out diagnostic, budget);
    }

    internal ReadStatus TryRead(LocaContext context, out LocaTable? model,
        out FontDiagnostic diagnostic, BuildBudget budget)
    {
        model = null;
        ReadStatus status = Validate(context, out diagnostic);
        if (status != ReadStatus.Done) return status;
        int count = context.NumGlyphs + 1;
        if (!budget.Work(8L * count, Location, out diagnostic) ||
            !budget.Buffer(4L * count, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        var offsets = new uint[count];
        for (int i = 0; i < count; i++)
        {
            status = TryGetGlyphOffset(context, i, out offsets[i], out diagnostic);
            if (status != ReadStatus.Done) return status;
        }
        model = new(offsets, context.LongFormat, Location, context.HeadLocation,
            context.MaxpLocation);
        diagnostic = default;
        return ReadStatus.Done;
    }

    private static ulong ExpectedLength(LocaContext context) => (ulong)(context.NumGlyphs + 1) *
        (ulong)(context.LongFormat ? LocaLayout.LongStride : LocaLayout.ShortStride);

    private static ulong ElementPosition(LocaContext context, int ordinal) =>
        (ulong)ordinal * (ulong)(context.LongFormat ? LocaLayout.LongStride : LocaLayout.ShortStride);

    private ReadStatus RequireContext(LocaContext context, out FontDiagnostic diagnostic)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!IsBound)
            return ReadWire.Fail(ReadStatus.Uninitialized, DiagnosticCode.UnboundView,
                Location.At(DataField.None, 0), out diagnostic);
        if (Location != context.Location)
            return ReadWire.Fail(ReadStatus.InvalidSource, DiagnosticCode.WrongSourceFace,
                Location, out diagnostic);
        diagnostic = default;
        return ReadStatus.Done;
    }

    private ReadStatus FailAt(LocaContext context, ReadStatus status, DiagnosticCode code,
        int ordinal, out FontDiagnostic diagnostic, ulong detail = 0, ulong expected = 0) =>
        ReadWire.Fail(status, code,
            Location.At(DataField.Offset, ElementPosition(context, ordinal), ordinal),
            out diagnostic, detail, expected);

    private ReadStatus InvalidIndex(LocaContext context, int ordinal, out FontDiagnostic diagnostic) =>
        ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
            Location.At(DataField.Offset, 0, ordinal), out diagnostic,
            unchecked((ulong)(long)ordinal), (ulong)(context.NumGlyphs + 1));
}
