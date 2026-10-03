using FontWeave.Core;

namespace FontWeave.Tables;

/// <summary>Semantic half of the single cvt  View.
///
/// The generated half owns storage, binding, element slicing and per-element location. This
/// half adds only what needs the whole table: the even-length rule, the element count
/// derived from the table length, the declared element budget, and the owned model.
///
/// The wire format has no header, so a bound empty table is legal and remains distinct
/// from the default, unbound view.
/// </summary>
public readonly ref partial struct CvtView
{
    /// <summary>Encoded byte length, or 0 for an unbound view. IsBound distinguishes an
    /// unbound view from the legal empty table.</summary>
    public int EncodedLength => IsBound ? RawBytes.Length : 0;

    /// <summary>Opens a cvt  image after checking its whole-table length and count rules.
    /// The generated TryOpen remains available for raw, local access.</summary>
    public static ReadStatus TryOpenCvt(ReadOnlySpan<byte> data, out CvtView view,
        out FontDiagnostic diagnostic, DataLocation location = default)
    {
        view = default;
        location = location == default ? DataLocation.Anonymous(CvtLayout.Tag) : location;
        ReadStatus status = TryValidateLength(data.Length, location, out diagnostic);
        return status == ReadStatus.Done
            ? TryOpen(data, out view, out diagnostic, location)
            : status;
    }

    /// <summary>Element count derived from the table length. The wire carries no count, so
    /// this is the only source of truth for how many FWORDs the table holds.</summary>
    public int ValueCount => EncodedLength / CvtLayout.ValueStride;

    public bool IsEmpty => IsBound && EncodedLength == 0;

    /// <summary>Reads one value by index. The index bound is the derived element count, not
    /// the table length, so an index past the end is an index fault rather than truncation.</summary>
    public ReadStatus TryGetValue(int index, out short amount, out FontDiagnostic diagnostic)
    {
        amount = 0;
        if (!IsBound)
            return ReadWire.Fail(ReadStatus.Uninitialized, DiagnosticCode.UnboundView,
                Location, out diagnostic);
        if (index < 0 || index >= ValueCount)
            return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                Location.AtLocal(DataField.Count,
                    index < 0 ? 0 : (ulong)index * CvtLayout.ValueStride, index),
                out diagnostic, unchecked((ulong)(long)index), (ulong)ValueCount);
        ReadStatus status = TryReadValueAt((ulong)(index * CvtLayout.ValueStride), index,
            out var record, out diagnostic);
        if (status != ReadStatus.Done) return status;
        amount = record.Amount;
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>Reads every value into an owned model. Nothing is published unless the whole
    /// table reads, so a failure leaves no partial model.</summary>
    public ReadStatus TryRead(out CvtTable? table, out FontDiagnostic diagnostic,
        BuildLimits? limits = null)
    {
        table = null;
        ReadStatus status = TryValidateStructure(out diagnostic);
        if (status != ReadStatus.Done) return status;
        var budget = new BuildBudget(limits);
        // The input budget is charged against the table as it is read, matching the other
        // table views, so an over-budget source is refused before any work is done.
        if (!budget.Input(EncodedLength, Location, out diagnostic))
            return ReadStatus.ResourceLimit;
        return TryRead(out table, out diagnostic, budget);
    }

    internal ReadStatus TryRead(out CvtTable? table, out FontDiagnostic diagnostic,
        BuildBudget budget)
    {
        table = null;
        ReadStatus structureStatus = TryValidateStructure(out diagnostic);
        if (structureStatus != ReadStatus.Done) return structureStatus;
        if (!budget.Work(EncodedLength, Location, out diagnostic))
            return ReadStatus.ResourceLimit;
        int count = ValueCount;
        if (!budget.Buffer((long)count * CvtLayout.ValueStride, Location, out diagnostic))
            return ReadStatus.ResourceLimit;
        short[] values = count == 0 ? [] : new short[count];
        for (int i = 0; i < count; i++)
        {
            ReadStatus status = TryGetValue(i, out short amount, out diagnostic);
            if (status != ReadStatus.Done) return status;
            values[i] = amount;
        }
        table = new CvtTable(values, Location);
        diagnostic = default;
        return ReadStatus.Done;
    }

    private ReadStatus TryValidateStructure(out FontDiagnostic diagnostic)
    {
        if (!IsBound)
            return ReadWire.Fail(ReadStatus.Uninitialized, DiagnosticCode.UnboundView,
                Location, out diagnostic);
        return TryValidateLength(RawBytes.Length, Location, out diagnostic);
    }

    private static ReadStatus TryValidateLength(int length, DataLocation location,
        out FontDiagnostic diagnostic)
    {
        if (length % CvtLayout.ValueStride != 0)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                location.AtLocal(DataField.Length, 0), out diagnostic, (ulong)length,
                CvtLayout.ValueStride);
        int count = length / CvtLayout.ValueStride;
        if (count > CvtLayout.MaxValues)
            return ReadWire.Fail(ReadStatus.ResourceLimit, DiagnosticCode.BudgetExceeded,
                location.AtLocal(DataField.Count, 0), out diagnostic, (ulong)count,
                CvtLayout.MaxValues);
        diagnostic = default;
        return ReadStatus.Done;
    }
}
