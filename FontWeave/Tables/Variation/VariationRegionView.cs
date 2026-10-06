using FontWeave.Core;

namespace FontWeave.Tables.Variation;

/// <summary>The semantic face of the VariationRegionList: the dynamic
/// axisCount-by-regionCount walk (stride axisCount*6) and the F2Dot14 value domain
/// are the card's hand-written boundary. The declared value rules (spec
/// otvarcommonformats.md RegionAxisCoordinates) are pinned here: all three values in
/// [-1.0, +1.0], start less or equal peak less or equal end, and the three either all
/// non-positive or all non-negative with the single peak-zero exception
/// (start less or equal 0 less or equal end).</summary>
public readonly ref partial struct VariationRegionListView
{
    internal const short F2Dot14One = 16384;

    /// <summary>The strict structural pass. Reports the declared region count and the
    /// declared list length; the axis count is additionally checked against the
    /// caller's explicit expectation (the T5-02-forward explicit axis-count contract,
    /// card 3.1).</summary>
    public ReadStatus Validate(int expectedAxisCount, out int regionCount, out int consumed,
        out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        regionCount = 0;
        consumed = 0;
        var budget = new BuildBudget(limits);
        if (!budget.Input(_data.Length, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        return Validate(expectedAxisCount, out regionCount, out consumed, out diagnostic, budget);
    }

    internal ReadStatus Validate(int expectedAxisCount, out int regionCount, out int consumed,
        out FontDiagnostic diagnostic, BuildBudget budget)
    {
        regionCount = 0;
        consumed = 0;
        ReadStatus status = TryGetAxisCount(out ushort axisCount, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (axisCount != expectedAxisCount)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.AtLocal(DataField.Count, VariationRegionLayout.AxisCountAt),
                out diagnostic, axisCount, (ulong)expectedAxisCount);
        status = TryGetRegionCount(out ushort rawRegionCount, out diagnostic);
        if (status != ReadStatus.Done) return status;
        // "The high-order bit of the regionCount field is reserved for future use and
        // must be cleared" (the count itself must be less than 32,768).
        if ((rawRegionCount & 0x8000) != 0)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.AtLocal(DataField.Count, VariationRegionLayout.RegionCountAt),
                out diagnostic, rawRegionCount, 0x7FFF);
        regionCount = rawRegionCount;
        long declared = VariationRegionLayout.HeaderLength + (long)regionCount * axisCount * 6;
        if (!budget.Work(declared, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        if (declared > _data.Length)
            return ReadWire.Fail(ReadStatus.Truncated, DiagnosticCode.TruncatedData,
                Location.AtLocal(DataField.Count, VariationRegionLayout.RegionCountAt),
                out diagnostic, (ulong)_data.Length, (ulong)declared);
        for (int region = 0; region < regionCount; region++)
        {
            int baseAt = VariationRegionLayout.HeaderLength + region * axisCount * 6;
            for (int axis = 0; axis < axisCount; axis++)
            {
                int at = baseAt + axis * 6;
                short start = ReadF2Dot14(at);
                short peak = ReadF2Dot14(at + 2);
                short end = ReadF2Dot14(at + 4);
                status = ValidateAxisTriple(start, peak, end,
                    Location.AtLocal(DataField.Payload, (ulong)at, region), out diagnostic);
                if (status != ReadStatus.Done) return status;
            }
        }
        consumed = (int)declared;
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>Decodes every region's axis triples into owned storage (all-nonpositive /
    /// all-nonnegative domains already proven by Validate).</summary>
    internal ReadStatus TryRead(int expectedAxisCount,
        out VariationRegionAxis[][] regions, out int regionCount, out int consumed,
        out FontDiagnostic diagnostic, BuildBudget budget)
    {
        regions = [];
        ReadStatus status = Validate(expectedAxisCount, out regionCount, out consumed,
            out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        status = TryGetAxisCount(out ushort axisCount, out _);
        if (status != ReadStatus.Done) return status;
        if (!budget.Buffer(6L * regionCount * axisCount, Location, out diagnostic))
            return ReadStatus.ResourceLimit;
        regions = new VariationRegionAxis[regionCount][];
        for (int region = 0; region < regionCount; region++)
        {
            var axes = new VariationRegionAxis[axisCount];
            int baseAt = VariationRegionLayout.HeaderLength + region * axisCount * 6;
            for (int axis = 0; axis < axisCount; axis++)
            {
                int at = baseAt + axis * 6;
                axes[axis] = new(ReadF2Dot14(at), ReadF2Dot14(at + 2), ReadF2Dot14(at + 4));
            }
            regions[region] = axes;
        }
        diagnostic = default;
        return ReadStatus.Done;
    }

    // The ordered triple domain (spec RegionAxisCoordinates): range, ordering, and the
    // sign rule whose one exception is peak == 0 with start negative-or-zero and end
    // positive-or-zero.
    private static ReadStatus ValidateAxisTriple(short start, short peak, short end,
        DataLocation location, out FontDiagnostic diagnostic)
    {
        if (start < -F2Dot14One || start > F2Dot14One)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                location, out diagnostic, (ulong)(long)start, (ulong)F2Dot14One);
        if (peak < -F2Dot14One || peak > F2Dot14One)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                location.AtLocal(DataField.Payload, 2), out diagnostic, (ulong)(long)peak, (ulong)F2Dot14One);
        if (end < -F2Dot14One || end > F2Dot14One)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                location.AtLocal(DataField.Payload, 4), out diagnostic, (ulong)(long)end, (ulong)F2Dot14One);
        if (start > peak)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                location.AtLocal(DataField.Payload, 2), out diagnostic, (ulong)(long)peak, (ulong)(long)start);
        if (peak > end)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                location.AtLocal(DataField.Payload, 4), out diagnostic, (ulong)(long)end, (ulong)(long)peak);
        // start < 0 and end > 0 oppose signs; legal only through the peak-zero exception.
        if (start < 0 && end > 0 && peak != 0)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                location.AtLocal(DataField.Payload, 4), out diagnostic, (ulong)(long)end, 0ul);
        diagnostic = default;
        return ReadStatus.Done;
    }

    private short ReadF2Dot14(int at) =>
        global::System.Buffers.Binary.BinaryPrimitives.ReadInt16BigEndian(RawBytes.Slice(at));
}
