using FontWeave.Core;

namespace FontWeave.Tables.Layout;

/// <summary>One static anchor point (GPOS shared leaf). Format 2's contour point index
/// is carried opaquely (OQ-T4-05-5); format 3's Device slots keep the present-null
/// semantics - a null model is both an absent device and an offset-0 present-null
/// (T4-01 mask/offset decoupling), and the wire always carries both offset fields.</summary>
public sealed class AnchorTable
{
    internal AnchorTable(ushort format, short xCoordinate, short yCoordinate, ushort anchorPoint,
        StaticDeviceTable? xDevice, StaticDeviceTable? yDevice, DataLocation location,
        int consumedLength)
    {
        Format = format; XCoordinate = xCoordinate; YCoordinate = yCoordinate;
        AnchorPoint = anchorPoint; XDevice = xDevice; YDevice = yDevice;
        Location = location; ConsumedLength = consumedLength;
    }

    public ushort Format { get; }
    public short XCoordinate { get; }
    public short YCoordinate { get; }
    public ushort AnchorPoint { get; }
    public StaticDeviceTable? XDevice { get; }
    public StaticDeviceTable? YDevice { get; }
    public DataLocation Location { get; }
    public int ConsumedLength { get; }
}

/// <summary>One cursive attachment record: both slots may be null, individually or
/// together - the adjudicated legal no-op record (OQ-T4-05-4).</summary>
public readonly record struct EntryExitTable(AnchorTable? Entry = null, AnchorTable? Exit = null);

/// <summary>A static GPOS type-3 fragment: one EntryExit record per Coverage index,
/// in Coverage order. Repeated anchor offsets materialize as one shared AnchorTable
/// instance, so recompilation reproduces the wire's body sharing.</summary>
public sealed class CursivePosTable
{
    private readonly EntryExitTable[] _records;

    internal CursivePosTable(CoverageTable coverage, EntryExitTable[] records, int numGlyphs,
        DataLocation location, int consumedLength)
    {
        Coverage = coverage; _records = records; NumGlyphs = numGlyphs;
        Location = location; ConsumedLength = consumedLength;
    }

    public CoverageTable Coverage { get; }
    public int NumGlyphs { get; }
    public int EntryExitCount => _records.Length;
    public ReadOnlySpan<EntryExitTable> Records => _records;
    public DataLocation Location { get; }
    public int ConsumedLength { get; }

    public ReadStatus TryGetEntryExit(int coverageOrdinal, out EntryExitTable record,
        out FontDiagnostic diagnostic)
    {
        record = default;
        if (coverageOrdinal < 0 || coverageOrdinal >= _records.Length)
            return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                Location.AtLocal(DataField.Count, CursivePosLayout.CountField, coverageOrdinal),
                out diagnostic, unchecked((ulong)(long)coverageOrdinal), (ulong)_records.Length);
        record = _records[coverageOrdinal]; diagnostic = default; return ReadStatus.Done;
    }
}

/// <summary>One Lookup span's shared read facts for the cursive chain: anchors dedupe
/// by physical position so shared bodies stay one instance (W5), devices dedupe like
/// the SinglePos facts cache.</summary>
internal sealed class CursivePosReadState(int numGlyphs, DataLocation origin, int length, bool materialize)
{
    internal readonly record struct Leaf(int Count, int Length);
    internal int NumGlyphs { get; } = numGlyphs;
    internal DataLocation Origin { get; } = origin;
    internal int Length { get; } = length;
    internal bool Materialize { get; } = materialize;
    internal Dictionary<int, Leaf> CoverageFacts { get; } = new();
    internal Dictionary<int, Leaf> DeviceFacts { get; } = new();
    internal Dictionary<int, Leaf> AnchorFacts { get; } = new();
    internal Dictionary<int, CoverageTable> Coverages { get; } = new();
    internal Dictionary<int, StaticDeviceTable> Devices { get; } = new();
    internal Dictionary<int, AnchorTable> Anchors { get; } = new();
    internal DataLocation At(int position) => Origin.AtLocal(DataField.None, (ulong)position);
}
