using FontWeave.Core;

namespace FontWeave.Tables;

/// <summary>A fpgm/prep payload replacement.
///
/// The replacement is a whole payload, not a patch: these tables have no internal structure,
/// so there is nothing finer to address. The bytes are never interpreted, so no plan can
/// request a semantic change to a program.</summary>
public sealed record InstructionProgramEditPlan
{
    /// <summary>Replacement payload, or null to keep the current bytes.</summary>
    public byte[]? Program { get; init; }
}

/// <summary>Canonical fpgm/prep encoding.
///
/// The form is exactly the payload bytes: no header, no length field, no padding and no
/// alignment. An unedited table re-encodes to its own bytes, including the empty table, which
/// encodes to zero bytes.</summary>
public static class InstructionProgramCompiler
{
    public static ReadStatus TryCompile(InstructionProgramTable model, out byte[]? output,
        out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        var budget = new BuildBudget(limits);
        return TryCompile(model, out output, out diagnostic, budget);
    }

    private static ReadStatus TryCompile(InstructionProgramTable model, out byte[]? output,
        out FontDiagnostic diagnostic, BuildBudget budget)
    {
        output = null;
        int length = model.Length;
        if (length > InstructionProgramLayout.MaxBytes)
            return ReadWire.Fail(ReadStatus.ResourceLimit, DiagnosticCode.BudgetExceeded,
                model.Location.At(DataField.Length, 0), out diagnostic, (ulong)length,
                InstructionProgramLayout.MaxBytes);
        if (!budget.Work(length, model.Location, out diagnostic) ||
            !budget.Output(length, model.Location, out diagnostic) ||
            !budget.Buffer(length, model.Location, out diagnostic))
            return ReadStatus.ResourceLimit;
        byte[] bytes = model.Program.ToArray();
        budget.RecordCompiledTable(length);
        output = bytes;
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>Applies a payload replacement and returns the rebuilt bytes.</summary>
    public static ReadStatus TryApply(InstructionProgramTable model,
        InstructionProgramEditPlan plan, out byte[]? output, out FontDiagnostic diagnostic,
        BuildLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(plan);
        output = null;
        if (plan.Program is null) return TryCompile(model, out output, out diagnostic, limits);
        if (plan.Program.Length > InstructionProgramLayout.MaxBytes)
            return ReadWire.Fail(ReadStatus.ResourceLimit, DiagnosticCode.BudgetExceeded,
                model.Location.At(DataField.Length, 0), out diagnostic,
                (ulong)plan.Program.Length, InstructionProgramLayout.MaxBytes);
        var edited = new InstructionProgramTable(model.Kind, plan.Program.ToArray(),
            model.Location);
        return TryCompile(edited, out output, out diagnostic, limits);
    }

    /// <summary>Writes the canonical payload into a caller-supplied destination, writing
    /// nothing unless the whole payload fits.</summary>
    public static ReadStatus TryWrite(InstructionProgramTable model, Span<byte> destination,
        out int written, out FontDiagnostic diagnostic, BuildLimits? limits = null)
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

    /// <summary>Builds a model from an explicit payload.</summary>
    public static InstructionProgramTable FromBytes(InstructionProgramKind kind,
        ReadOnlySpan<byte> program, DataLocation location = default) =>
        new(kind, program.ToArray(), location == default
            ? DataLocation.Anonymous(InstructionProgramLayout.TagOf(kind)) : location);
}
