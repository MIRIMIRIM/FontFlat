namespace FontWeave.Core;

/// <summary>Already materialized immutable output, not a deferred layout plan.
/// Retains only its own output bytes and numeric usage, never the source face or map.</summary>
public sealed class CffCompiledFont
{
    private readonly byte[] _bytes;
    internal CffCompiledFont(byte[] bytes, BuildUsage usage)
    {
        _bytes = bytes;
        Usage = usage;
    }
    public int OutputLength => _bytes.Length;
    public BuildUsage Usage { get; }

    /// <summary>Checks capacity before writing. Does not allocate or mutate the result;
    /// concurrent calls must use distinct destination storage.</summary>
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
