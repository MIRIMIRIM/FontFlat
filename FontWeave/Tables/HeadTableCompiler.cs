using System.Buffers.Binary;
using FontWeave.Core;

namespace FontWeave.Tables;

/// <summary>Only the fontRevision and modified fields of a validated head table may change.</summary>
public readonly record struct HeadMetadataEdit(int FontRevisionRaw, long ModifiedSeconds);

public static class HeadTableCompiler
{
    public static ReadStatus TryCompile(ReadOnlySpan<byte> input, HeadMetadataEdit edit,
        out byte[]? output, out FontDiagnostic diagnostic, BuildLimits? limits = null,
        DataLocation location = default) =>
        Compile(input, edit, out output, out diagnostic, new BuildBudget(limits), location, true);

    internal static ReadStatus TryCompile(ReadOnlySpan<byte> input, HeadMetadataEdit edit,
        out byte[]? output, out FontDiagnostic diagnostic, BuildBudget budget,
        DataLocation location = default) =>
        Compile(input, edit, out output, out diagnostic, budget, location, false);

    private static ReadStatus Compile(ReadOnlySpan<byte> input, HeadMetadataEdit edit,
        out byte[]? output, out FontDiagnostic diagnostic, BuildBudget budget,
        DataLocation location, bool chargeInput)
    {
        output = null;
        location = location == default ? DataLocation.Anonymous(0x68656164) : location;
        if (chargeInput && !budget.Input(input.Length, location, out diagnostic))
            return ReadStatus.ResourceLimit;
        if (!budget.Work(input.Length, location, out diagnostic))
            return ReadStatus.ResourceLimit;

        ReadStatus status = HeadView.TryOpen(input, out var view, out diagnostic, location);
        if (status != ReadStatus.Done) return status;
        status = view.Validate(out diagnostic);
        if (status != ReadStatus.Done) return status;

        if (!budget.Work(input.Length, location, out diagnostic) ||
            !budget.Output(input.Length, location, out diagnostic) ||
            !budget.Buffer(input.Length, location, out diagnostic))
            return ReadStatus.ResourceLimit;

        byte[] compiled = input.ToArray();
        BinaryPrimitives.WriteInt32BigEndian(compiled.AsSpan(4, 4), edit.FontRevisionRaw);
        BinaryPrimitives.WriteInt64BigEndian(compiled.AsSpan(28, 8), edit.ModifiedSeconds);
        budget.RecordCompiledTable(compiled.Length);
        output = compiled;
        diagnostic = default;
        return ReadStatus.Done;
    }
}
