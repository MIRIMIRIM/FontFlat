using FontWeave.Core;

namespace FontWeave.Tables;

public readonly record struct VdmxEntry(ushort YPelHeight, short YMax, short YMin);

/// <summary>GroupIndex refers to a physical group; ratio order is first-match order, not a set.</summary>
public readonly record struct VdmxRatio(byte CharSet, byte XRatio, byte YStartRatio, byte YEndRatio, int GroupIndex);

public enum VdmxCharacterSetScope : byte { AllGlyphs, WindowsAnsiSubset }

public sealed class VdmxGroup
{
    private readonly VdmxEntry[] _entries;

    internal VdmxGroup(ulong sourceOffset, byte startSize, byte endSize, VdmxEntry[] entries)
    { SourceOffset = sourceOffset; StartSize = startSize; EndSize = endSize; _entries = entries; }

    public ulong SourceOffset { get; }
    public byte StartSize { get; }
    public byte EndSize { get; }
    public ReadOnlySpan<VdmxEntry> Entries => _entries;
}

/// <summary>Owned ratios and physical groups, including shared and unreferenced groups.</summary>
public sealed class VdmxTable
{
    private readonly VdmxRatio[] _ratios;
    private readonly VdmxGroup[] _groups;

    internal VdmxTable(ushort version, VdmxRatio[] ratios, VdmxGroup[] groups, DataLocation location)
    { Version = version; _ratios = ratios; _groups = groups; Location = location; }

    public ushort Version { get; }
    public ushort NumRatios => (ushort)_ratios.Length;
    public ushort NumGroups => (ushort)_groups.Length;
    public ReadOnlySpan<VdmxRatio> Ratios => _ratios;
    public ReadOnlySpan<VdmxGroup> Groups => _groups;
    public DataLocation Location { get; }

    public ReadStatus TryGetCharacterSetScope(int ratioOrdinal, out VdmxCharacterSetScope scope,
        out FontDiagnostic diagnostic)
    {
        scope = default;
        if (ratioOrdinal < 0 || ratioOrdinal >= NumRatios)
            return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                Location.At(DataField.Count, 4, ratioOrdinal), out diagnostic,
                unchecked((ulong)(long)ratioOrdinal), NumRatios);
        scope = Version == 0 && _ratios[ratioOrdinal].CharSet == 1
            ? VdmxCharacterSetScope.WindowsAnsiSubset : VdmxCharacterSetScope.AllGlyphs;
        diagnostic = default;
        return ReadStatus.Done;
    }
}

public readonly record struct VdmxRatioEdit(int RatioOrdinal, int? CharSet = null, int? XRatio = null,
    int? YStartRatio = null, int? YEndRatio = null, int? GroupIndex = null);

public readonly record struct VdmxGroupEdit(int GroupIndex, int? StartSize = null, int? EndSize = null);

public readonly record struct VdmxEntryEdit(int GroupIndex, int EntryOrdinal, int? YPelHeight = null,
    int? YMax = null, int? YMin = null);

public sealed record VdmxEditPlan
{
    public IReadOnlyList<VdmxRatioEdit> Ratios { get; init; } = [];
    public IReadOnlyList<VdmxGroupEdit> Groups { get; init; } = [];
    public IReadOnlyList<VdmxEntryEdit> Entries { get; init; } = [];
}
