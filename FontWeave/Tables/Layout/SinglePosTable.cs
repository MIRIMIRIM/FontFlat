using FontWeave.Core;

namespace FontWeave.Tables.Layout;

/// <summary>Static values only. Presence belongs to the enclosing SinglePos ValueFormat.</summary>
public readonly record struct StaticValueRecord(short XPlacement = 0, short YPlacement = 0,
    short XAdvance = 0, short YAdvance = 0, StaticDeviceTable? XPlacementDevice = null,
    StaticDeviceTable? YPlacementDevice = null, StaticDeviceTable? XAdvanceDevice = null,
    StaticDeviceTable? YAdvanceDevice = null)
{
    internal short ScalarAt(int slot) => slot switch
    { 0 => XPlacement, 1 => YPlacement, 2 => XAdvance, _ => YAdvance };
    internal StaticDeviceTable? DeviceAt(int slot) => slot switch
    { 4 => XPlacementDevice, 5 => YPlacementDevice, 6 => XAdvanceDevice, _ => YAdvanceDevice };
}

/// <summary>A static GPOS type-1 fragment. Values retains physical rows: one for
/// format 1, one per Coverage key for format 2, including zero-size wire rows.</summary>
public sealed class SinglePosTable
{
    private readonly StaticValueRecord[] _values;
    internal SinglePosTable(CoverageTable coverage, StaticValueRecord[] values, ushort format,
        ushort valueFormat, DataLocation location, int consumedLength)
    {
        Coverage = coverage; _values = values; Format = format; ValueFormat = valueFormat;
        Location = location; ConsumedLength = consumedLength;
    }

    public CoverageTable Coverage { get; }
    public ReadOnlySpan<ushort> Glyphs => Coverage.Glyphs;
    public ReadOnlySpan<StaticValueRecord> Values => _values;
    public ushort Format { get; }
    public ushort ValueFormat { get; }
    public int NumGlyphs => Coverage.NumGlyphs;
    public DataLocation Location { get; }
    /// <summary>Actual source extent for a read model; zero for caller-created or
    /// edited models whose relocated output extent is determined only by planning.</summary>
    public int ConsumedLength { get; }

    public ReadStatus TryGetValueAt(int coverageOrdinal, out StaticValueRecord value, out FontDiagnostic diagnostic)
    {
        value = default;
        if (coverageOrdinal < 0 || coverageOrdinal >= Glyphs.Length)
            return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                Location.AtLocal(DataField.Count, 0, coverageOrdinal), out diagnostic,
                unchecked((ulong)(long)coverageOrdinal), (ulong)Glyphs.Length);
        value = ValueAt(coverageOrdinal); diagnostic = default; return ReadStatus.Done;
    }

    public ReadStatus TryGetValue(int glyphId, out StaticValueRecord value, out FontDiagnostic diagnostic)
    {
        value = default;
        if (glyphId < 0 || glyphId >= NumGlyphs)
            return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                Location.AtLocal(DataField.GlyphId, 0), out diagnostic,
                unchecked((ulong)(long)glyphId), (ulong)NumGlyphs);
        int index = Coverage.FindGlyph(glyphId);
        if (index < 0)
            return ReadWire.Fail(ReadStatus.NotFound, DiagnosticCode.GlyphNotMapped,
                Coverage.Location.AtLocal(DataField.GlyphId, 0), out diagnostic, (ulong)glyphId);
        value = ValueAt(index); diagnostic = default; return ReadStatus.Done;
    }

    internal StaticValueRecord ValueAt(int ordinal) => _values[Format == 1 ? 0 : ordinal];

    /// <summary>Atomically replaces keys and physical rows with an explicitly chosen
    /// format and presence mask. Source/glyph context is retained, not inferred.</summary>
    public ReadStatus TryWithValues(ReadOnlySpan<ushort> glyphs, ReadOnlySpan<StaticValueRecord> values,
        ushort format, ushort valueFormat, out SinglePosTable? updated, out FontDiagnostic diagnostic,
        BuildLimits? limits = null) => SinglePosTableCompiler.TryCreate(glyphs, values, NumGlyphs, format,
            valueFormat, out updated, out diagnostic, limits, Location, Coverage.Format);
}

internal sealed class SinglePosReadState(int numGlyphs, DataLocation origin, int length, bool materialize)
{
    internal readonly record struct Leaf(int Count, int Length);
    internal int NumGlyphs { get; } = numGlyphs;
    internal DataLocation Origin { get; } = origin;
    internal int Length { get; } = length;
    internal bool Materialize { get; } = materialize;
    internal Dictionary<int, Leaf> CoverageFacts { get; } = new();
    internal Dictionary<int, Leaf> DeviceFacts { get; } = new();
    internal Dictionary<int, CoverageTable> Coverages { get; } = new();
    internal Dictionary<int, StaticDeviceTable> Devices { get; } = new();
    internal DataLocation At(int position) => Origin.AtLocal(DataField.None, (ulong)position);
}
