namespace FontWeave.Core;

/// <summary>Per-operation admission limits, not a process memory limit. TemporaryBytes
/// counts cumulative charged array payloads; CLR headers, containers and JIT are excluded.</summary>
public sealed record BuildLimits
{
    public int MaxInputBytes { get; init; } = 64 * 1024 * 1024;
    public int MaxOutputBytes { get; init; } = 64 * 1024 * 1024;
    public long MaxTemporaryBytes { get; init; } = 128L * 1024 * 1024;
    public long MaxWorkUnits { get; init; } = 512L * 1024 * 1024;
}

/// <summary>Deterministic accounting; not GC allocation bytes or peak live memory.</summary>
public readonly record struct BuildUsage(long WorkUnits, long TemporaryBytes,
    int InputBytes, long CopiedTableBytes, int TableCopyCount,
    long CompiledTableBytes, int CompiledTableCount, int OutputBytes);

internal sealed class BuildBudget
{
    internal BuildLimits Limits { get; }
    internal BuildUsage Usage { get; private set; }

    internal BuildBudget(BuildLimits? limits = null)
    {
        Limits = limits ?? new();
        ArgumentOutOfRangeException.ThrowIfNegative(Limits.MaxInputBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(Limits.MaxOutputBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(Limits.MaxTemporaryBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(Limits.MaxWorkUnits);
    }

    // Repeated checks of the same source are idempotent, not repeated ownership charges.
    internal bool Input(int length, DataLocation at, out FontDiagnostic diagnostic)
    {
        diagnostic = default;
        if (length > Limits.MaxInputBytes)
            return Fail(at, DataField.InputBytes, (ulong)length, (ulong)Limits.MaxInputBytes, out diagnostic);
        Usage = Usage with { InputBytes = length };
        return true;
    }

    internal bool Work(long amount, DataLocation at, out FontDiagnostic diagnostic)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(amount);
        diagnostic = default;
        if (amount > Limits.MaxWorkUnits - Usage.WorkUnits)
            return Fail(at, DataField.WorkUnits, (ulong)Usage.WorkUnits + (ulong)amount,
                (ulong)Limits.MaxWorkUnits, out diagnostic);
        Usage = Usage with { WorkUnits = Usage.WorkUnits + amount };
        return true;
    }

    internal bool Buffer(long bytes, DataLocation at, out FontDiagnostic diagnostic)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bytes);
        diagnostic = default;
        if (bytes > Limits.MaxTemporaryBytes - Usage.TemporaryBytes)
            return Fail(at, DataField.TemporaryBytes, (ulong)Usage.TemporaryBytes + (ulong)bytes,
                (ulong)Limits.MaxTemporaryBytes, out diagnostic);
        Usage = Usage with { TemporaryBytes = Usage.TemporaryBytes + bytes };
        return true;
    }

    internal bool Output(long bytes, DataLocation at, out FontDiagnostic diagnostic)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bytes);
        diagnostic = default;
        if (bytes > Limits.MaxOutputBytes)
            return Fail(at, DataField.OutputBytes, (ulong)bytes, (ulong)Limits.MaxOutputBytes, out diagnostic);
        Usage = Usage with { OutputBytes = (int)bytes };
        return true;
    }

    internal void RecordTableCopy(int bytes) => Usage = Usage with
    { CopiedTableBytes = Usage.CopiedTableBytes + bytes, TableCopyCount = Usage.TableCopyCount + 1 };
    internal void RecordCompiledTable(int bytes) => Usage = Usage with
    { CompiledTableBytes = Usage.CompiledTableBytes + bytes, CompiledTableCount = Usage.CompiledTableCount + 1 };

    private static bool Fail(DataLocation at, DataField field, ulong detail, ulong expected,
        out FontDiagnostic diagnostic)
    {
        diagnostic = new(DiagnosticCode.BudgetExceeded, at with { Field = field }, detail, expected);
        return false;
    }
}
