using FontWeave.Core;

namespace FontWeave.Tables;

/// <summary>One cvt  value replacement.</summary>
public sealed record CvtValueEdit
{
    public required int Index { get; init; }
    public required short Amount { get; init; }
}

/// <summary>A cvt  rewrite: value replacements plus optional appends.</summary>
public sealed record CvtEditPlan
{
    public IReadOnlyList<CvtValueEdit> Values { get; init; } = [];

    /// <summary>Values appended after the existing ones, in order.</summary>
    public IReadOnlyList<short> Append { get; init; } = [];
}

/// <summary>Canonical cvt  encoding.
///
/// The form is minimal by construction: the wire has no header, so the encoding is exactly
/// the int16 values in order with no padding. An unedited table therefore re-encodes to its
/// own bytes, and the empty table encodes to zero bytes.</summary>
public static class CvtTableCompiler
{
    private const int Stride = CvtLayout.ValueStride;

    public static ReadStatus TryCompile(CvtTable model, out byte[]? output,
        out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        var budget = new BuildBudget(limits);
        return TryCompile(model, out output, out diagnostic, budget);
    }

    internal static ReadStatus TryCompile(CvtTable model, out byte[]? output,
        out FontDiagnostic diagnostic, BuildBudget budget)
    {
        output = null;
        int length = model.Count * Stride;
        if (!budget.Work(length, model.Location, out diagnostic) ||
            !budget.Output(length, model.Location, out diagnostic) ||
            !budget.Buffer(length, model.Location, out diagnostic))
            return ReadStatus.ResourceLimit;
        var bytes = new byte[length];
        for (int i = 0; i < model.Count; i++)
        {
            ushort raw = unchecked((ushort)model[i]);
            bytes[i * Stride] = unchecked((byte)(raw >> 8));
            bytes[i * Stride + 1] = unchecked((byte)raw);
        }
        budget.RecordCompiledTable(length);
        output = bytes;
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>Applies a plan and returns the rebuilt table's bytes. A failure publishes
    /// nothing, so a refused edit leaves the caller's model untouched.</summary>
    public static ReadStatus TryApply(CvtTable model, CvtEditPlan plan, out byte[]? output,
        out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(plan);
        output = null;
        var budget = new BuildBudget(limits);
        int count = model.Count + plan.Append.Count;
        if (count > CvtLayout.MaxValues)
            return ReadWire.Fail(ReadStatus.ResourceLimit, DiagnosticCode.BudgetExceeded,
                model.Location.At(DataField.Count, 0), out diagnostic, (ulong)count,
                CvtLayout.MaxValues);
        var values = new short[count];
        for (int i = 0; i < model.Count; i++) values[i] = model[i];
        for (int i = 0; i < plan.Append.Count; i++) values[model.Count + i] = plan.Append[i];
        foreach (var edit in plan.Values)
        {
            ArgumentNullException.ThrowIfNull(edit);
            if (edit.Index < 0 || edit.Index >= count)
                return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                    model.Location.At(DataField.Count, (ulong)(edit.Index * Stride), edit.Index),
                    out diagnostic, unchecked((ulong)(long)edit.Index), (ulong)count);
            values[edit.Index] = edit.Amount;
        }
        var edited = new CvtTable(values, model.Location);
        return TryCompile(edited, out output, out diagnostic, budget);
    }

    /// <summary>Writes a canonical encoding into a caller-supplied destination. Nothing is
    /// written unless the whole table fits.</summary>
    public static ReadStatus TryWrite(CvtTable model, Span<byte> destination, out int written,
        out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        written = 0;
        ReadStatus status = TryCompile(model, out var bytes, out diagnostic, limits);
        if (status != ReadStatus.Done) return status;
        if (destination.Length < bytes!.Length)
            return ReadWire.Fail(ReadStatus.DestinationTooSmall,
                DiagnosticCode.DestinationTooSmall,
                model.Location.At(DataField.OutputBytes, 0), out diagnostic,
                (ulong)destination.Length, (ulong)bytes.Length);
        bytes.CopyTo(destination);
        written = bytes.Length;
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>Builds a model from an explicit value sequence, so a caller can construct a
    /// table without going through the wire reader.</summary>
    public static CvtTable FromValues(ReadOnlySpan<short> values, DataLocation location = default) =>
        new(values.ToArray(), location == default ? DataLocation.Anonymous(CvtLayout.Tag) : location);
}
