using FontWeave.Core;

namespace FontWeave.Tables;

public readonly ref partial struct VorgView
{
    public ReadStatus Validate(VorgContext context, out FontDiagnostic diagnostic)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!IsBound)
            return ReadWire.Fail(ReadStatus.Uninitialized, DiagnosticCode.UnboundView,
                Location, out diagnostic);
        if (Location != context.Location)
            return ReadWire.Fail(ReadStatus.InvalidSource, DiagnosticCode.WrongSourceFace,
                Location, out diagnostic);
        ReadStatus status = TryRequireKnownVersion(out _, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (MinorVersion != 0)
            return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedVersion,
                Location.At(DataField.Version, 2), out diagnostic, MinorVersion);
        int count = NumVertOriginYMetrics;
        if (count > context.NumGlyphs)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.At(DataField.Count, 6), out diagnostic, (uint)count, context.NumGlyphs);
        int expected = VorgLayout.HeaderLength + VorgLayout.RecordStride * count;
        if (_data.Length < expected)
            return TryGetOriginRecord((_data.Length - VorgLayout.HeaderLength) / VorgLayout.RecordStride,
                out _, out diagnostic);
        if (_data.Length > expected)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.UnexpectedTrailingData,
                Location.At(DataField.Length, (ulong)expected), out diagnostic,
                (ulong)_data.Length, (ulong)expected);
        ushort previous = 0;
        for (int i = 0; i < count; i++)
        {
            _ = TryGetOriginRecord(i, out var record, out _);
            var location = Location.At(DataField.GlyphId,
                VorgLayout.HeaderLength + (ulong)i * VorgLayout.RecordStride, i);
            if (record.GlyphIndex >= context.NumGlyphs)
                return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                    location, out diagnostic, record.GlyphIndex, context.NumGlyphs);
            if (i > 0 && record.GlyphIndex <= previous)
                return ReadWire.Fail(ReadStatus.InvalidFormat,
                    record.GlyphIndex == previous ? DiagnosticCode.DuplicateGlyph : DiagnosticCode.InvalidValue,
                    location, out diagnostic, record.GlyphIndex, previous);
            previous = record.GlyphIndex;
        }
        diagnostic = default;
        return ReadStatus.Done;
    }

    public ReadStatus TryRead(VorgContext context, out VorgTable? model,
        out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        model = null;
        var budget = new BuildBudget(limits);
        if (!budget.Input(_data.Length, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        return TryRead(context, out model, out diagnostic, budget);
    }

    internal ReadStatus TryRead(VorgContext context, out VorgTable? model,
        out FontDiagnostic diagnostic, BuildBudget budget)
    {
        model = null;
        if (!budget.Work(_data.Length, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        ReadStatus status = Validate(context, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (!budget.Buffer(VorgLayout.RecordStride * (long)NumVertOriginYMetrics,
            Location, out diagnostic)) return ReadStatus.ResourceLimit;
        var records = new VorgRecord[NumVertOriginYMetrics];
        for (int i = 0; i < records.Length; i++)
        {
            _ = TryGetOriginRecord(i, out var record, out _);
            records[i] = new(record.GlyphIndex, record.VertOriginY);
        }
        model = new(DefaultVertOriginY, records, context.NumGlyphs, Location, context.MaxpLocation);
        diagnostic = default;
        return ReadStatus.Done;
    }
}
