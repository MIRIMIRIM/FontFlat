using FontWeave.Core;

namespace FontWeave.Tables;

/// <summary>All PCLT 1.0 fields, preserving fixed-width ASCII strings exactly.</summary>
public readonly record struct PcltValues(uint FontNumber, ushort Pitch, ushort XHeight,
    ushort Style, ushort TypeFamily, ushort CapHeight, ushort SymbolSet,
    string Typeface, ulong CharacterComplement, string FileName,
    sbyte StrokeWeight, sbyte WidthType, byte SerifStyle);

/// <summary>Validated, owned PCLT 1.0 values.</summary>
public sealed class PcltTable
{
    internal PcltTable(PcltValues values, DataLocation location)
    { Values = values; Location = location; }

    public uint VersionRaw => PcltLayout.Version;
    public PcltValues Values { get; }
    public DataLocation Location { get; }

    public ReadStatus TryWith(PcltValues values, out PcltTable? model,
        out FontDiagnostic diagnostic)
    {
        model = null;
        ReadStatus status = PcltView.ValidateValues(values, Location, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (values == Values)
            return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
                Location, out diagnostic);
        model = new(values, Location);
        diagnostic = default;
        return ReadStatus.Done;
    }
}
