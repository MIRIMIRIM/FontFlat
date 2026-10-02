using FontWeave.Core;

namespace FontWeave.Tables;

/// <summary>Which instruction table an <see cref="InstructionProgramTable"/> holds.
///
/// fpgm and prep share one layout, model and compiler because they are the same wire shape:
/// a bare byte stream with no header, no count field and no alignment padding. They differ
/// only in role, which is carried here rather than duplicated into two types.</summary>
public enum InstructionProgramKind : byte
{
    /// <summary>fpgm: font program, the FDEF/IDEF definitions.</summary>
    FontProgram = 0,

    /// <summary>prep: control value program, run when the point size changes.</summary>
    ControlValueProgram = 1
}

internal static class InstructionProgramLayout
{
    internal const uint FontProgramTag = 0x6670676D;
    internal const uint ControlValueProgramTag = 0x70726570;

    /// <summary>Declared byte budget. Both tables are whole byte streams, so the only
    /// structural limit is size.</summary>
    internal const int MaxBytes = 1 << 20;

    internal static uint TagOf(InstructionProgramKind kind) => kind switch
    {
        InstructionProgramKind.FontProgram => FontProgramTag,
        InstructionProgramKind.ControlValueProgram => ControlValueProgramTag,
        _ => 0
    };
}

/// <summary>An owned TrueType instruction byte stream.
///
/// This is a declared hand-written boundary rather than a Schema table: fpgm and prep have
/// no internal structure at all, so there is no layout for a schema to declare, and treating
/// the payload as a declared array of bytes would add a schema without adding a rule.
///
/// The bytes are deliberately not interpreted. No opcode is decoded, no push data is
/// validated, no FDEF/IDEF structure is checked, and no claim is made that the program is
/// semantically valid. This type is not a VM and must not become one.</summary>
public sealed class InstructionProgramTable
{
    private readonly byte[] _program;

    internal InstructionProgramTable(InstructionProgramKind kind, byte[] program,
        DataLocation location)
    {
        Kind = kind;
        _program = program;
        Location = location;
    }

    public InstructionProgramKind Kind { get; }

    public DataLocation Location { get; }

    /// <summary>The table tag this program was read from.</summary>
    public uint Tag => InstructionProgramLayout.TagOf(Kind);

    public int Length => _program.Length;

    public bool IsEmpty => _program.Length == 0;

    /// <summary>The owned instruction bytes, never interpreted.</summary>
    public ReadOnlySpan<byte> Program => _program;

    public ReadStatus TryGetByte(int index, out byte value, out FontDiagnostic diagnostic)
    {
        value = 0;
        if (index < 0 || index >= _program.Length)
            return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                Location.At(DataField.Payload, unchecked((ulong)(long)index), index),
                out diagnostic, unchecked((ulong)(long)index), (ulong)_program.Length);
        value = _program[index];
        diagnostic = default;
        return ReadStatus.Done;
    }
}

/// <summary>The single View for the fpgm/prep byte-stream layout.
///
/// The wire format has no header and no fields, so there is nothing for a generated reader to
/// bind: the whole table is the payload, and every rule is a whole-table rule. The boundary is
/// therefore declared here explicitly instead of being expressed as a degenerate schema.</summary>
public readonly ref struct InstructionProgramView
{
    private readonly ReadOnlySpan<byte> _data;

    private InstructionProgramView(ReadOnlySpan<byte> data, DataLocation location,
        InstructionProgramKind kind)
    {
        _data = data;
        Location = location;
        Kind = kind;
    }

    public DataLocation Location { get; }

    public InstructionProgramKind Kind { get; }

    /// <summary>An empty program is legal: both tables may legitimately be absent of code.</summary>
    public bool IsBound => true;

    public static ReadStatus TryOpen(ReadOnlySpan<byte> data, InstructionProgramKind kind,
        out InstructionProgramView view, out FontDiagnostic diagnostic,
        DataLocation location = default)
    {
        view = default;
        uint tag = InstructionProgramLayout.TagOf(kind);
        if (tag == 0)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.UnsupportedOperation,
                DataLocation.Anonymous().At(DataField.None, 0), out diagnostic, (ulong)kind, 1);
        if (location == default) location = DataLocation.Anonymous(tag);
        // The declared budget is the only structural limit: the table is a bare byte stream
        // whose length is its entire content, so there is no separate declared length to
        // disagree with and no trailing-byte rule to apply.
        if (data.Length > InstructionProgramLayout.MaxBytes)
            return ReadWire.Fail(ReadStatus.ResourceLimit, DiagnosticCode.BudgetExceeded,
                location.At(DataField.Length, 0), out diagnostic, (ulong)data.Length,
                InstructionProgramLayout.MaxBytes);
        view = new(data, location, kind);
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>Validates the table. The only rules are the byte budget and the provenance
    /// tag; no opcode is decoded and no program structure is checked.</summary>
    public ReadStatus Validate(out FontDiagnostic diagnostic)
    {
        if (Location.TableTag != InstructionProgramLayout.TagOf(Kind))
            return ReadWire.Fail(ReadStatus.InvalidSource, DiagnosticCode.UnknownTable,
                Location.At(DataField.Tag, 0), out diagnostic, Location.TableTag,
                InstructionProgramLayout.TagOf(Kind));
        if (_data.Length > InstructionProgramLayout.MaxBytes)
            return ReadWire.Fail(ReadStatus.ResourceLimit, DiagnosticCode.BudgetExceeded,
                Location.At(DataField.Length, 0), out diagnostic, (ulong)_data.Length,
                InstructionProgramLayout.MaxBytes);
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>Materializes the owned byte stream. The payload is copied verbatim; no
    /// normalization, padding or trimming is applied.</summary>
    public ReadStatus TryRead(out InstructionProgramTable? table, out FontDiagnostic diagnostic,
        BuildLimits? limits = null)
    {
        table = null;
        var budget = new BuildBudget(limits);
        // The input budget is charged against the table as it is read, matching the other
        // table views, so an over-budget source is refused before any work is done.
        if (!budget.Input(_data.Length, Location, out diagnostic))
            return ReadStatus.ResourceLimit;
        return TryRead(out table, out diagnostic, budget);
    }

    internal ReadStatus TryRead(out InstructionProgramTable? table, out FontDiagnostic diagnostic,
        BuildBudget budget)
    {
        table = null;
        if (!budget.Work(_data.Length, Location, out diagnostic))
            return ReadStatus.ResourceLimit;
        ReadStatus status = Validate(out diagnostic);
        if (status != ReadStatus.Done) return status;
        table = new InstructionProgramTable(Kind, _data.ToArray(), Location);
        diagnostic = default;
        return ReadStatus.Done;
    }
}
