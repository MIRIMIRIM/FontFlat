using FontWeave.Core;

namespace FontWeave.Tables;

/// <summary>Handwritten head semantics over the schema-generated borrowed reader.</summary>
public readonly ref partial struct HeadView
{
    private const uint RequiredVersion = 0x00010000;
    private const uint RequiredMagicNumber = 0x5F0F3CF5;
    private const int TableLength = 54;

    /// <summary>
    /// Checks the finite R1 profile: the 54-byte size, version, magic number, and units-per-em.
    /// It does not validate every field in the <c>head</c> table.
    /// </summary>
    public ReadStatus Validate(out FontDiagnostic diagnostic)
    {
        if (!IsBound)
            return ReadWire.Fail(ReadStatus.Uninitialized, DiagnosticCode.UnboundView,
                Location.At(DataField.None, 0), out diagnostic);
        if (_data.Length < TableLength)
            return ReadWire.Fail(ReadStatus.Truncated, DiagnosticCode.TruncatedData,
                Location.At(DataField.None, 0), out diagnostic, (ulong)_data.Length, TableLength);
        if (_data.Length != TableLength)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.At(DataField.Length, 0), out diagnostic, (ulong)_data.Length, TableLength);

        uint version = Version;
        if (version != RequiredVersion)
            return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedVersion,
                Location.At(DataField.Version, 0), out diagnostic, version, RequiredVersion);

        ReadStatus status = TryGetMagicNumber(out uint magic, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (magic != RequiredMagicNumber)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.At(DataField.MagicNumber, 12), out diagnostic, magic, RequiredMagicNumber);

        status = TryGetUnitsPerEm(out ushort unitsPerEm, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (unitsPerEm is < 16 or > 16384)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.At(DataField.UnitsPerEm, 18), out diagnostic, unitsPerEm, 16384);

        diagnostic = default;
        return ReadStatus.Done;
    }

}
