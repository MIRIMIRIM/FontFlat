using FontWeave.Core;

namespace FontWeave.Operations;

/// <summary>Already materialized output; retains bytes and numeric decisions only, not source/face/map.</summary>
public sealed class CompiledFont
{
    private readonly byte[] _bytes;
    private readonly TableDecision[] _decisions;

    internal CompiledFont(byte[] bytes, BuildUsage usage, TableDecision[] decisions)
    {
        _bytes = bytes;
        Usage = usage;
        _decisions = decisions;
    }

    public int OutputLength => _bytes.Length;
    public BuildUsage Usage { get; }
    public ReadOnlySpan<TableDecision> Decisions => _decisions;

    /// <summary>Capacity failure writes nothing. Concurrent copies require distinct destinations.</summary>
    public ReadStatus TryCopyTo(Span<byte> destination, out int written, out FontDiagnostic diagnostic)
    {
        written = 0;
        diagnostic = default;
        if (destination.Length < _bytes.Length)
            return ReadWire.Fail(ReadStatus.DestinationTooSmall, DiagnosticCode.DestinationTooSmall,
                DataLocation.Anonymous().At(DataField.OutputBytes, 0) with { Basis = OffsetBasis.None },
                out diagnostic, (ulong)destination.Length, (ulong)_bytes.Length);
        _bytes.CopyTo(destination);
        written = _bytes.Length;
        return ReadStatus.Done;
    }
}
