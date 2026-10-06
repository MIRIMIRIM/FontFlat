using FontWeave.Core;

namespace FontWeave.Tables.Layout;

/// <summary>The calling direction a shared TagList is opened under; it selects the
/// declared Script/Feature reference branch and is never a wire field.</summary>
internal enum LayoutTagListRole : byte
{
    ScriptList = 1,
    FeatureList = 2,
}

public sealed class LayoutLangSys
{
    private readonly ushort[] _features;

    internal LayoutLangSys(ushort required, ushort[] features, DataLocation location, int consumedLength)
    {
        RequiredFeatureIndex = required; _features = features;
        Location = location; ConsumedLength = consumedLength;
    }

    public ushort RequiredFeatureIndex { get; }
    public ReadOnlySpan<ushort> FeatureIndices => _features;
    public DataLocation Location { get; }
    public int ConsumedLength { get; }

    public ReadStatus TryWithFeatures(ushort requiredFeatureIndex, ReadOnlySpan<ushort> featureIndices,
        int featureCount, out LayoutLangSys? updated, out FontDiagnostic diagnostic, BuildLimits? limits = null) =>
        LayoutListsTableCompiler.TryCreateLangSys(requiredFeatureIndex, featureIndices, featureCount,
            out updated, out diagnostic, limits, Location);
}

public readonly record struct LayoutLanguage(uint Tag, LayoutLangSys Language);

public sealed class LayoutScript
{
    private readonly LayoutLanguage[] _languages;

    internal LayoutScript(uint tag, LayoutLangSys? defaultLanguage, LayoutLanguage[] languages,
        DataLocation location, int consumedLength, LayoutScript? payloadOwner = null,
        DataLocation listLocation = default, DataLocation recordLocation = default)
    {
        Tag = tag; DefaultLanguage = defaultLanguage; _languages = languages;
        Location = location; ConsumedLength = consumedLength; PayloadOwner = payloadOwner ?? this;
        ListLocation = listLocation == default ? location : listLocation;
        RecordLocation = recordLocation == default ? location.AtLocal(DataField.Tag, 0) : recordLocation;
    }

    public uint Tag { get; }
    public LayoutLangSys? DefaultLanguage { get; }
    public ReadOnlySpan<LayoutLanguage> Languages => _languages;
    public DataLocation Location { get; }
    public int ConsumedLength { get; }
    // Only source parsing can prove a shared payload under different record tags.
    internal LayoutScript PayloadOwner { get; }
    internal DataLocation ListLocation { get; }
    internal DataLocation RecordLocation { get; }

    internal LayoutScript AtRecord(uint tag, DataLocation listLocation, DataLocation recordLocation) =>
        new(tag, DefaultLanguage, _languages, Location, ConsumedLength, PayloadOwner, listLocation, recordLocation);

    public ReadStatus TryWithLanguages(LayoutLangSys? defaultLanguage, ReadOnlySpan<LayoutLanguage> languages,
        int featureCount, out LayoutScript? updated, out FontDiagnostic diagnostic, BuildLimits? limits = null) =>
        LayoutListsTableCompiler.TryCreateScript(Tag, defaultLanguage, languages, featureCount,
            out updated, out diagnostic, limits, Location);
}

public sealed class LayoutFeature
{
    private readonly ushort[] _lookups;

    internal LayoutFeature(uint tag, ushort paramsOffsetRaw, ushort[] lookups, DataLocation location,
        int consumedLength, LayoutFeature? payloadOwner = null, DataLocation listLocation = default,
        DataLocation recordLocation = default)
    {
        Tag = tag; ParamsOffsetRaw = paramsOffsetRaw; _lookups = lookups;
        Location = location; ConsumedLength = consumedLength; PayloadOwner = payloadOwner ?? this;
        ListLocation = listLocation == default ? location : listLocation;
        RecordLocation = recordLocation == default ? location.AtLocal(DataField.Tag, 0) : recordLocation;
    }

    public uint Tag { get; }
    /// <summary>Raw observation only; nonzero parameters are not decoded or typed-writable.</summary>
    public ushort ParamsOffsetRaw { get; }
    public ReadOnlySpan<ushort> LookupIndices => _lookups;
    public DataLocation Location { get; }
    public int ConsumedLength { get; }
    internal LayoutFeature PayloadOwner { get; }
    internal DataLocation ListLocation { get; }
    internal DataLocation RecordLocation { get; }

    internal LayoutFeature AtRecord(uint tag, DataLocation listLocation, DataLocation recordLocation) =>
        tag == Tag ? this : new(tag, ParamsOffsetRaw, _lookups, Location, ConsumedLength,
            PayloadOwner, listLocation, recordLocation);

    public ReadStatus TryWithLookups(ReadOnlySpan<ushort> lookupIndices, int lookupCount,
        out LayoutFeature? updated, out FontDiagnostic diagnostic, BuildLimits? limits = null) =>
        LayoutListsTableCompiler.TryCreateFeature(Tag, ParamsOffsetRaw, lookupIndices, lookupCount,
            out updated, out diagnostic, limits, Location);
}

/// <summary>The static list relationships in GSUB/GPOS, not a complete layout-table model.
/// Lookup payloads remain metadata until explicitly read through their supported typed entry.</summary>
public sealed class LayoutListsTable
{
    private readonly LayoutScript[] _scripts;
    private readonly LayoutFeature[] _features;
    private readonly LookupTable[] _lookups;

    internal LayoutListsTable(uint version, uint variations, LayoutScript[] scripts,
        LayoutFeature[] features, LookupTable[] lookups, int numGlyphs, LayoutLookupKind kind,
        DataLocation location)
    {
        Version = version; FeatureVariationsOffsetRaw = variations;
        _scripts = scripts; _features = features; _lookups = lookups;
        NumGlyphs = numGlyphs; Kind = kind; Location = location;
    }

    public uint Version { get; }
    public uint FeatureVariationsOffsetRaw { get; }
    public ReadOnlySpan<LayoutScript> Scripts => _scripts;
    public ReadOnlySpan<LayoutFeature> Features => _features;
    public ReadOnlySpan<LookupTable> Lookups => _lookups;
    public int NumGlyphs { get; }
    public LayoutLookupKind Kind { get; }
    public DataLocation Location { get; }

    public ReadStatus TryWithLists(ReadOnlySpan<LayoutScript> scripts, ReadOnlySpan<LayoutFeature> features,
        ReadOnlySpan<LookupTable> lookups, out LayoutListsTable? updated, out FontDiagnostic diagnostic,
        BuildLimits? limits = null) => LayoutListsTableCompiler.TryCreate(Version, FeatureVariationsOffsetRaw,
            scripts, features, lookups, NumGlyphs, Kind, out updated, out diagnostic, limits, Location);
}

/// <summary>Three separately owned fragments: the lists-compiler face that predates
/// the root writers. Whole-table output now exists for both layout roots
/// (GsubTableCompiler, GposTableCompiler); this fragments face stays the shared
/// three-fragment emission those roots' plans reuse, not a second root output.</summary>
public sealed class LayoutListFragments
{
    internal LayoutListFragments(byte[] scripts, byte[] features, byte[] lookups)
    { ScriptListBytes = scripts; FeatureListBytes = features; LookupListBytes = lookups; }

    public byte[] ScriptListBytes { get; }
    public byte[] FeatureListBytes { get; }
    public byte[] LookupListBytes { get; }
}
