using FontWeave.Core;

namespace FontWeave.Tables.Layout;

public readonly ref partial struct LigGlyphView
{
    public ReadStatus Validate(out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        var budget = new BuildBudget(limits);
        if (!budget.Input(_data.Length, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        return Validate(out diagnostic, budget);
    }

    internal ReadStatus Validate(out FontDiagnostic diagnostic, BuildBudget budget) =>
        ReadChain(false, out _, out diagnostic, budget);

    public ReadStatus TryRead(out LigGlyphTable? model, out FontDiagnostic diagnostic,
        BuildLimits? limits = null)
    {
        model = null;
        var budget = new BuildBudget(limits);
        if (!budget.Input(_data.Length, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        return TryRead(out model, out diagnostic, budget);
    }

    internal ReadStatus TryRead(out LigGlyphTable? model, out FontDiagnostic diagnostic,
        BuildBudget budget) => ReadChain(true, out model, out diagnostic, budget);

    // The caret offset slots are LigGlyph-relative; formats 1/2/3 are discriminated
    // into owned entries (OQ-T4-02-3). Spec caretValueOffsets are "in increasing
    // coordinate order": a consecutive pair is comparable only when both entries
    // carry a coordinate (formats 1/3); pairs spanning a format 2 are statically
    // undecidable and skipped (OQ-T4-02-1). Writing keeps the declared order.
    private ReadStatus ReadChain(bool materialize, out LigGlyphTable? model,
        out FontDiagnostic diagnostic, BuildBudget budget)
    {
        model = null;
        int prefix = LigGlyphLayout.HeaderLength + LigGlyphLayout.CaretStride * CaretCount;
        if (!budget.Work(prefix, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        if (_data.Length < prefix)
        {
            int firstIncomplete = (_data.Length - LigGlyphLayout.HeaderLength) / LigGlyphLayout.CaretStride;
            ReadStatus status = TryGetCaretValueOffsetRecord(firstIncomplete, out _, out diagnostic);
            if (status == ReadStatus.Truncated)
                diagnostic = diagnostic with { Location = diagnostic.Location with { Field = DataField.Offset } };
            return status;
        }
        if (materialize && !budget.Buffer(16L * CaretCount, Location, out diagnostic))
            return ReadStatus.ResourceLimit;
        var entries = materialize && CaretCount != 0 ? new CaretValueEntry[CaretCount] : [];
        int consumed = prefix;
        int previousCoordinate = int.MinValue;
        for (int i = 0; i < CaretCount; i++)
        {
            ReadStatus status = TryGetCaretValueOffsetRecord(i, out var record, out diagnostic);
            if (status != ReadStatus.Done) return status;
            ulong row = LigGlyphLayout.HeaderLength + (ulong)i * LigGlyphLayout.CaretStride;
            status = TryOpenCaretValue(i, out var caretView, out _, out diagnostic);
            if (status != ReadStatus.Done) return status;
            status = caretView.TryGetFormat(out ushort format, out diagnostic);
            if (status != ReadStatus.Done) return status;
            int length;
            switch (format)
            {
                case 1:
                {
                    status = caretView.TryGetFormat1Header(out var header, out diagnostic);
                    if (status != ReadStatus.Done) return status;
                    length = CaretValueLayout.Format12Length;
                    if (!budget.Work(length, caretView.Location, out diagnostic)) return ReadStatus.ResourceLimit;
                    if (previousCoordinate != int.MinValue && header.Coordinate <= previousCoordinate)
                        return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                            Location.AtLocal(DataField.Offset, row, i), out diagnostic,
                            unchecked((ulong)(long)header.Coordinate),
                            unchecked((ulong)(long)previousCoordinate));
                    previousCoordinate = header.Coordinate;
                    if (materialize) entries[i] = CaretValueEntry.ForCoordinate(header.Coordinate, caretView.Location);
                    break;
                }
                case 2:
                {
                    status = caretView.TryGetFormat2Header(out var header, out diagnostic);
                    if (status != ReadStatus.Done) return status;
                    length = CaretValueLayout.Format12Length;
                    if (!budget.Work(length, caretView.Location, out diagnostic)) return ReadStatus.ResourceLimit;
                    // The point index is opaque UInt16 (OQ-T4-02-2): recorded raw,
                    // never checked against any outline domain.
                    if (materialize) entries[i] = CaretValueEntry.ForPointIndex(header.CaretValuePointIndex, caretView.Location);
                    break;
                }
                default:
                {
                    // Format 3 keeps the frozen CaretValue path: the semantic-layer
                    // zero refusal and the required Device child (zero drift with the
                    // caretValue3 family, T4-01-Static).
                    status = caretView.TryRead(out var caret, out diagnostic, budget);
                    if (status != ReadStatus.Done) return status;
                    length = caret!.ConsumedLength;
                    if (previousCoordinate != int.MinValue && caret.Coordinate <= previousCoordinate)
                        return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                            Location.AtLocal(DataField.Offset, row, i), out diagnostic,
                            unchecked((ulong)(long)caret.Coordinate),
                            unchecked((ulong)(long)previousCoordinate));
                    previousCoordinate = caret.Coordinate;
                    if (materialize) entries[i] = CaretValueEntry.ForDevice(caret.Coordinate, caret.Device, caretView.Location);
                    break;
                }
            }
            consumed = Math.Max(consumed, (int)record.CaretValueOffset + length);
        }
        if (materialize) model = new(entries, Location, consumed);
        diagnostic = default;
        return ReadStatus.Done;
    }
}
