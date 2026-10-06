using System.Numerics;
using FontWeave.Core;

namespace FontWeave.Tables.Layout;

/// <summary>One pair rule row (GPOS type 2): the second glyph plus the two glyph value
/// records. Presence belongs to the owning PairPos subtable's two ValueFormats; a
/// record keeps physical zero words for every present-but-zero slot.</summary>
public readonly record struct PairValueRecord(ushort SecondGlyph,
    StaticValueRecord Value1 = default, StaticValueRecord Value2 = default);

/// <summary>All pairs beginning with one covered glyph, in strict second-glyph order.
/// The empty set (pairValueCount=0) is the adjudicated legal no-op (OQ-T4-05-3).</summary>
public sealed class PairSetTable
{
    private readonly PairValueRecord[] _records;

    internal PairSetTable(PairValueRecord[] records, DataLocation location, int consumedLength)
    {
        _records = records; Location = location; ConsumedLength = consumedLength;
    }

    public int PairValueCount => _records.Length;
    public ReadOnlySpan<PairValueRecord> Records => _records;
    public DataLocation Location { get; }
    public int ConsumedLength { get; }
}

/// <summary>A static GPOS type-2 fragment. Format 1 keeps one PairSet per Coverage
/// index (including the empty set); format 2 keeps the declared class1×class2 matrix
/// physically, including every all-zero cell. Format-1 Device tables are PairSet
/// -relative and format-2 Device tables are subtable-relative (spec gpos.md:679); both
/// resolve to absolute positions before the read-state caches see them.</summary>
public sealed class PairPosTable
{
    private readonly PairSetTable[]? _pairSets;
    private readonly StaticValueRecord[]? _values1, _values2;

    internal PairPosTable(CoverageTable coverage, ushort format, ushort valueFormat1,
        ushort valueFormat2, PairSetTable[]? pairSets = null, ClassDefTable? classDef1 = null,
        ClassDefTable? classDef2 = null, ushort class1Count = 0, ushort class2Count = 0,
        StaticValueRecord[]? values1 = null, StaticValueRecord[]? values2 = null,
        DataLocation location = default, int consumedLength = 0)
    {
        Coverage = coverage; Format = format; ValueFormat1 = valueFormat1;
        ValueFormat2 = valueFormat2; _pairSets = pairSets; ClassDef1 = classDef1;
        ClassDef2 = classDef2; Class1Count = class1Count; Class2Count = class2Count;
        _values1 = values1; _values2 = values2;
        Location = location; ConsumedLength = consumedLength;
    }

    public CoverageTable Coverage { get; }
    public ushort Format { get; }
    public ushort ValueFormat1 { get; }
    public ushort ValueFormat2 { get; }
    public int NumGlyphs => Coverage.NumGlyphs;
    public DataLocation Location { get; }
    /// <summary>Actual source extent for a read model; zero for caller-created or
    /// edited models whose relocated output extent is determined only by planning.</summary>
    public int ConsumedLength { get; }
    public IReadOnlyList<PairSetTable>? PairSets => _pairSets is null ? null : Array.AsReadOnly(_pairSets);
    public ClassDefTable? ClassDef1 { get; }
    public ClassDefTable? ClassDef2 { get; }
    public int Class1Count { get; }
    public int Class2Count { get; }
    public int PairSetCount => _pairSets?.Length ?? 0;

    public ReadStatus TryGetPairSet(int coverageOrdinal, out PairSetTable? pairSet,
        out FontDiagnostic diagnostic)
    {
        pairSet = null;
        int count = _pairSets?.Length ?? 0;
        if (coverageOrdinal < 0 || coverageOrdinal >= count)
            return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                Location.AtLocal(DataField.Count, PairPosLayout.PairSetCountField, coverageOrdinal),
                out diagnostic, unchecked((ulong)(long)coverageOrdinal), (ulong)count);
        pairSet = _pairSets![coverageOrdinal]; diagnostic = default; return ReadStatus.Done;
    }

    // Plan-side enumerations: one device model per present Device slot, in row-major
    // order, so the write plan's identity dedup is deterministic.
    internal IEnumerable<StaticDeviceTable?> EnumerateDevices()
    {
        if (Format == 1)
        {
            int sets = _pairSets?.Length ?? 0;
            for (int i = 0; i < sets; i++)
            {
                var set = _pairSets![i];
                for (int j = 0; j < set.PairValueCount; j++)
                {
                    var record = set.Records[j];
                    for (int slot = 4; slot < 8; slot++)
                    {
                        yield return record.Value1.DeviceAt(slot);
                        yield return record.Value2.DeviceAt(slot);
                    }
                }
            }
        }
        else
        {
            int cells = Class1Count * Class2Count;
            for (int i = 0; i < cells; i++)
                for (int slot = 4; slot < 8; slot++)
                {
                    yield return _values1![i].DeviceAt(slot);
                    yield return _values2![i].DeviceAt(slot);
                }
        }
    }

    internal StaticValueRecord GetMatrixValue1(int class1, int class2) =>
        Format == 2 ? _values1![class1 * Class2Count + class2] : default;
    internal StaticValueRecord GetMatrixValue2(int class1, int class2) =>
        Format == 2 ? _values2![class1 * Class2Count + class2] : default;

    /// <summary>Atomically replaces one coverage key's pair rows. The format, masks,
    /// coverage and glyph context are retained; source identity is re-proven.</summary>
    public ReadStatus TryWithPairSet(int coverageOrdinal, ReadOnlySpan<PairValueRecord> records,
        out PairPosTable? updated, out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        updated = null;
        if (Format != 1 || _pairSets is null || coverageOrdinal < 0 || coverageOrdinal >= _pairSets.Length)
            return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                Location.AtLocal(DataField.Count, PairPosLayout.PairSetCountField, coverageOrdinal),
                out diagnostic, unchecked((ulong)(long)coverageOrdinal), (ulong)(_pairSets?.Length ?? 0));
        var rows = new PairValueRecord[_pairSets.Length][];
        for (int i = 0; i < _pairSets.Length; i++)
            rows[i] = _pairSets[i].Records.ToArray();
        records.CopyTo(rows[coverageOrdinal].AsSpan());
        return PairPosTableCompiler.TryCreate(Coverage.Glyphs, rows, NumGlyphs, ValueFormat1,
            ValueFormat2, out updated, out diagnostic, limits, Location, Coverage.Format);
    }

    /// <summary>Atomically replaces the class-matrix rows (physical class1-major order,
    /// class1Count x class2Count cells). The ClassDefs, coverage and context are retained.</summary>
    public ReadStatus TryWithMatrixValues(ReadOnlySpan<StaticValueRecord> values1,
        ReadOnlySpan<StaticValueRecord> values2, out PairPosTable? updated,
        out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        updated = null;
        if (Format != 2)
            return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
                Location.AtLocal(DataField.Version, 0), out diagnostic, Format);
        return PairPosTableCompiler.TryCreate(Coverage.Glyphs, ClassDef1!.Ranges, ClassDef2!.Ranges,
            (ushort)Class1Count, (ushort)Class2Count, values1, values2, NumGlyphs, ValueFormat1,
            ValueFormat2, out updated, out diagnostic, limits, Location, Coverage.Format);
    }

    public ReadStatus TryGetMatrixValue(int class1, int class2, out StaticValueRecord value1,
        out StaticValueRecord value2, out FontDiagnostic diagnostic)
    {
        value1 = value2 = default;
        if (Format != 2 || _values1 is null || _values2 is null)
            return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
                Location.AtLocal(DataField.Version, 0), out diagnostic, Format);
        if (class1 < 0 || class1 >= Class1Count || class2 < 0 || class2 >= Class2Count)
            return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                Location.AtLocal(DataField.Count, PairPosLayout.Format2HeaderField, class1),
                out diagnostic, unchecked((ulong)(long)class1), (ulong)Class1Count);
        int at = class1 * Class2Count + class2;
        value1 = _values1[at]; value2 = _values2[at];
        diagnostic = default; return ReadStatus.Done;
    }
}

/// <summary>One Lookup span's shared read facts for the GPOS positioning leaves: keys
/// are physical Lookup-local positions, never diagnostic coordinates (SinglePos
/// read-state precedent). ClassDef facts exist so the two format-2 offsets may legally
/// name one shared table (W12) without reading it twice.</summary>
internal sealed class PairPosReadState(int numGlyphs, DataLocation origin, int length, bool materialize)
{
    internal readonly record struct Leaf(int Count, int Length);
    internal int NumGlyphs { get; } = numGlyphs;
    internal DataLocation Origin { get; } = origin;
    internal int Length { get; } = length;
    internal bool Materialize { get; } = materialize;
    internal Dictionary<int, Leaf> CoverageFacts { get; } = new();
    internal Dictionary<int, Leaf> DeviceFacts { get; } = new();
    internal Dictionary<int, Leaf> ClassDefFacts { get; } = new();
    internal Dictionary<int, CoverageTable> Coverages { get; } = new();
    internal Dictionary<int, StaticDeviceTable> Devices { get; } = new();
    internal Dictionary<int, ClassDefTable> ClassDefs { get; } = new();
    internal DataLocation At(int position) => Origin.AtLocal(DataField.None, (ulong)position);
}
