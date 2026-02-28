using System.Runtime.InteropServices;
using OTFontFile2.SourceGen;

namespace OTFontFile2.Tables;

/// <summary>
/// Mutable builder for the TrueType <c>fpgm</c> table.
/// </summary>
[OtTableBuilder("fpgm")]
public sealed partial class FpgmTableBuilder : ISfntTableSource
{
    private ReadOnlyMemory<byte> _program = ReadOnlyMemory<byte>.Empty;

    public ReadOnlyMemory<byte> ProgramBytes => _program;

    public void SetProgram(ReadOnlyMemory<byte> program)
    {
        _program = program;
        MarkDirty();
    }

    public static bool TryFrom(FpgmTable fpgm, out FpgmTableBuilder builder)
    {
        builder = new FpgmTableBuilder();
        builder.SetProgram(fpgm.Program.ToArray());
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
