using System.Runtime.InteropServices;
using OTFontFile2.SourceGen;

namespace OTFontFile2.Tables;

/// <summary>
/// Mutable builder for the TrueType <c>prep</c> table.
/// </summary>
[OtTableBuilder("prep")]
public sealed partial class PrepTableBuilder : ISfntTableSource
{
    private ReadOnlyMemory<byte> _program = ReadOnlyMemory<byte>.Empty;

    public ReadOnlyMemory<byte> ProgramBytes => _program;

    public void SetProgram(ReadOnlyMemory<byte> program)
    {
        _program = program;
        MarkDirty();
    }

    public static bool TryFrom(PrepTable prep, out PrepTableBuilder builder)
    {
        builder = new PrepTableBuilder();
        builder.SetProgram(prep.Program.ToArray());
        return true;
    }

    private byte[] BuildTable()
    {
        if (_program.Length == 0)
            return Array.Empty<byte>();

        if (MemoryMarshal.TryGetArray(_program, out ArraySegment<byte> segment) &&
            segment.Array is not null &&
            segment.Offset == 0 &&
            segment.Count == segment.Array.Length)
        {
            return segment.Array;
        }

        return _program.ToArray();
    }
}
