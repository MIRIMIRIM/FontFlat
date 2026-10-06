using FontWeave.Core;

namespace FontWeave.Tables.Layout;

public readonly ref partial struct AttachPointView
{
    // Strictly increasing contour-point indices: the spec's "increasing numerical
    // order" is statically decidable for every entry, so equality refuses too
    // (card OQ-T4-02-1 adjudication, negative-pinned by the T4-02 wire oracle).
    internal ReadStatus Inspect(out int consumedLength, out FontDiagnostic diagnostic, BuildBudget budget)
    {
        consumedLength = 0;
        ReadStatus status = TryRequirePointIndexExtent(out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = TryGetPointIndexExtent(out _, out _, out ulong end, out diagnostic);
        if (status != ReadStatus.Done) return status;
        consumedLength = (int)end;
        if (!budget.Work(consumedLength, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        ulong row = AttachPointLayout.HeaderLength;
        int previous = -1;
        for (int i = 0; i < PointCount; i++)
        {
            status = TryGetPointIndex(i, out var record, out diagnostic);
            if (status != ReadStatus.Done) return status;
            if (record.Value <= previous)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    Location.AtLocal(DataField.Payload, row, i), out diagnostic,
                    record.Value, unchecked((ulong)(long)previous));
            previous = record.Value;
            row += AttachPointLayout.PointStride;
        }
        diagnostic = default;
        return ReadStatus.Done;
    }

    internal ReadStatus TryRead(out ushort[] points, out FontDiagnostic diagnostic, BuildBudget budget)
    {
        points = [];
        ReadStatus status = Inspect(out int consumedLength, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        if (!budget.Buffer(2L * PointCount, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        points = new ushort[PointCount];
        for (int i = 0; i < PointCount; i++)
        {
            _ = TryGetPointIndex(i, out var record, out _);
            points[i] = record.Value;
        }
        diagnostic = default;
        return ReadStatus.Done;
    }
}
