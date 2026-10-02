using FontWeave.Core;

namespace FontWeave.Tables;

/// <summary>Semantic half of the single cvt  View.
///
/// The generated half owns storage, binding, element slicing and per-element location. This
/// half adds only what needs the whole table: the even-length rule, the element count
/// derived from the table length, the declared element budget, and the owned model.
///
/// The wire format has no header, so an empty table is legal. The schema's positive
/// minimum is a structural floor, not the format rule; the rule is enforced here.
/// </summary>
public readonly ref partial struct CvtView
{
    /// <summary>Encoded byte length, or 0 when the table is empty.
    ///
    /// The generated storage treats an empty table as unbound and its RawBytes accessor
    /// throws rather than reporting zero length, so an empty view is reported as length 0
    /// here instead of faulting. A second declaration of this partial cannot add an instance
    /// field, so the length is derived from the binding state rather than recorded.</summary>
    public int EncodedLength => IsBound ? RawBytes.Length : 0;

    /// <summary>Opens a cvt  image, accepting the legal zero-length table that the
    /// generated structural floor would refuse.</summary>
    public static ReadStatus TryOpenCvt(ReadOnlySpan<byte> data, out CvtView view,
        out FontDiagnostic diagnostic, DataLocation location = default)
    {
        view = default;
        location = location == default ? DataLocation.Anonymous(CvtLayout.Tag) : location;
        if (data.Length % CvtLayout.ValueStride != 0)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                location.At(DataField.Length, 0), out diagnostic, (ulong)data.Length,
                CvtLayout.ValueStride);
        if (data.Length > CvtLayout.MaxValues * CvtLayout.ValueStride)
            return ReadWire.Fail(ReadStatus.ResourceLimit, DiagnosticCode.BudgetExceeded,
                location.At(DataField.Count, 0), out diagnostic,
                (ulong)(data.Length / CvtLayout.ValueStride), CvtLayout.MaxValues);
        view = new CvtView(data, location);
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>Element count derived from the table length. The wire carries no count, so
    /// this is the only source of truth for how many FWORDs the table holds.</summary>
    public int ValueCount => EncodedLength / CvtLayout.ValueStride;

    public bool IsEmpty => EncodedLength == 0;

    /// <summary>Reads one value by index. The index bound is the derived element count, not
    /// the table length, so an index past the end is an index fault rather than truncation.</summary>
    public ReadStatus TryGetValue(int index, out short amount, out FontDiagnostic diagnostic)
    {
        amount = 0;
        if (index < 0 || index >= ValueCount)
            return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                Location.At(DataField.Count, (ulong)(index * CvtLayout.ValueStride), index),
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
        if (!budget.Work(EncodedLength, Location, out diagnostic))
            return ReadStatus.ResourceLimit;
        int count = ValueCount;
        var values = new short[count];
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
}
