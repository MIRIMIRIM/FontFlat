using FontWeave.Core;
using FontWeave.Tables.Variation;

namespace FontWeave.Tables.Layout;

/// <summary>One BaseCoord leaf (formats 1/2/3). Format 2's referenceGlyph keeps the
/// read-time numGlyphs domain proof as a value; baseCoordPoint is opaque (the outline
/// domain, Anchor fmt2's registered contrast). Format 3's Device is the owned static
/// or VariationIndex body (0x8000 reinterprets Start/End as the outer/inner delta-set
/// indices, T4-06 3.4); a null model covers both the absent slot and the present-null
/// offset 0. The consumed length covers the closed subtree (the Device leaf), while
/// placement measures use the format body alone (the MeasureAnchorBody lesson).</summary>
public sealed class BaseCoordTable
{
    internal BaseCoordTable(ushort format, short coordinate, ushort referenceGlyph,
        ushort baseCoordPoint, StaticDeviceTable? device, DataLocation location, int consumedLength)
    {
        Format = format; Coordinate = coordinate; ReferenceGlyph = referenceGlyph;
        BaseCoordPoint = baseCoordPoint; Device = device;
        Location = location; ConsumedLength = consumedLength;
    }

    public ushort Format { get; }
    /// <summary>The baseline coordinate; negative values are real witnesses (R6).</summary>
    public short Coordinate { get; }
    /// <summary>Format 2's glyph ID (domain-checked against numGlyphs, OQ-5); zero elsewhere.</summary>
    public ushort ReferenceGlyph { get; }
    /// <summary>Format 2's contour point index, carried opaquely (OQ-T4-05-5 shape); zero elsewhere.</summary>
    public ushort BaseCoordPoint { get; }
    public StaticDeviceTable? Device { get; }
    /// <summary>True when this coord's Device is a VariationIndex reference (0x8000);
    /// the delta row resolves against this BASE table's own item variation store
    /// (OQ-6: no cross-table carrier) through the store's TryGetDeltaSet.</summary>
    public bool HasVariationDevice => Device is { IsVariationDevice: true };
    public DataLocation Location { get; }
    public int ConsumedLength { get; }
}

/// <summary>One BaseValues: the default baseline index (domain-checked against the
/// owning axis's baseTagCount; a NULL tagList narrows the check away, OQ-4) and the
/// coord list in wire slot order - the order is an observation, never re-sorted
/// (OQ-2; HAN's vert sequence is tag-ordered, not value-ordered).</summary>
public sealed class BaseValuesTable
{
    private readonly BaseCoordTable[] _coords;

    internal BaseValuesTable(int defaultBaselineIndex, BaseCoordTable[] coords,
        DataLocation location, int consumedLength)
    {
        DefaultBaselineIndex = defaultBaselineIndex; _coords = coords;
        Location = location; ConsumedLength = consumedLength;
    }

    public int DefaultBaselineIndex { get; }
    public int CoordCount => _coords.Length;
    public IReadOnlyList<BaseCoordTable> Coords => Array.AsReadOnly(_coords);
    public DataLocation Location { get; }
    public int ConsumedLength { get; }
}

/// <summary>One FeatMinMax row: the feature tag and its two Optional coord slots
/// (Example 4B's both-NULL form is the legal shape).</summary>
public readonly record struct FeatMinMaxRecord(uint Tag, BaseCoordTable? MinCoord, BaseCoordTable? MaxCoord);

/// <summary>spec MinMax under the family-prefixed name (OQ-7): the default min/max
/// slots plus the FeatMinMax rows in wire order.</summary>
public sealed class BaseMinMaxTable
{
    private readonly FeatMinMaxRecord[] _featMinMax;

    internal BaseMinMaxTable(BaseCoordTable? minCoord, BaseCoordTable? maxCoord,
        FeatMinMaxRecord[] featMinMax, DataLocation location, int consumedLength)
    {
        MinCoord = minCoord; MaxCoord = maxCoord; _featMinMax = featMinMax;
        Location = location; ConsumedLength = consumedLength;
    }

    public BaseCoordTable? MinCoord { get; }
    public BaseCoordTable? MaxCoord { get; }
    public int FeatMinMaxCount => _featMinMax.Length;
    public IReadOnlyList<FeatMinMaxRecord> FeatMinMax => Array.AsReadOnly(_featMinMax);
    public DataLocation Location { get; }
    public int ConsumedLength { get; }
}

/// <summary>One BaseLangSys row: the language-system tag and its Required MinMax.</summary>
public readonly record struct BaseLangSysRecord(uint Tag, BaseMinMaxTable MinMax);

/// <summary>One BaseScript: both Optional slots (a NULL BaseValues is expressible with
/// zero witnesses, spec "may be NULL") and the BaseLangSys rows in wire order
/// (alphabetical order is an observation, OQ-2).</summary>
public sealed class BaseScriptTable
{
    private readonly BaseLangSysRecord[] _langSys;

    internal BaseScriptTable(BaseValuesTable? baseValues, BaseMinMaxTable? defaultMinMax,
        BaseLangSysRecord[] langSys, DataLocation location, int consumedLength)
    {
        BaseValues = baseValues; DefaultMinMax = defaultMinMax; _langSys = langSys;
        Location = location; ConsumedLength = consumedLength;
    }

    public BaseValuesTable? BaseValues { get; }
    public BaseMinMaxTable? DefaultMinMax { get; }
    public int BaseLangSysCount => _langSys.Length;
    public IReadOnlyList<BaseLangSysRecord> LangSys => Array.AsReadOnly(_langSys);
    public DataLocation Location { get; }
    public int ConsumedLength { get; }
}

/// <summary>The baseline tag list: raw tags in wire order (OQ-2 observation face).</summary>
public sealed class BaseTagListTable
{
    private readonly uint[] _tags;

    internal BaseTagListTable(uint[] tags, DataLocation location, int consumedLength)
    {
        _tags = tags; Location = location; ConsumedLength = consumedLength;
    }

    public int TagCount => _tags.Length;
    public IReadOnlyList<uint> Tags => Array.AsReadOnly(_tags);
    public DataLocation Location { get; }
    public int ConsumedLength { get; }
}

/// <summary>One BaseScriptList row: the script tag and its Required BaseScript body
/// (repeated offsets materialize one shared instance, R7's 4->1 / 7->2 shapes).</summary>
public readonly record struct BaseScriptRecord(uint Tag, BaseScriptTable Script);

/// <summary>One axis direction: the Optional tag list and the Required script list.
/// The direction's value-domain interpretation (Y vs X) is a comment-level fact.</summary>
public sealed class BaseAxisTable
{
    private readonly BaseScriptRecord[] _scripts;

    internal BaseAxisTable(BaseTagListTable? tagList, BaseScriptRecord[] scripts,
        DataLocation location, int consumedLength)
    {
        TagList = tagList; _scripts = scripts;
        Location = location; ConsumedLength = consumedLength;
    }

    public BaseTagListTable? TagList { get; }
    public int ScriptCount => _scripts.Length;
    public IReadOnlyList<BaseScriptRecord> Scripts => Array.AsReadOnly(_scripts);
    public DataLocation Location { get; }
    public int ConsumedLength { get; }
}

/// <summary>Owned whole-BASE slice (versions 1.0/1.1): both axis trees, and the item
/// variation store mounted as an owned <see cref="VariationStoreTable"/> when the v1.1
/// slot carries one. The raw <see cref="ItemVariationStoreOffset"/> stays alongside it
/// for the consumed-length accounting and the header-slot write. The witnesses' whole
/// span is materialized (70-240 bytes, R9: maxBlockEnd == table length); baseline
/// selection and delta weighting stay outside this model (T0 ban).</summary>
public sealed class BaseTable
{
    internal BaseTable(uint version, int numGlyphs, BaseAxisTable? horizAxis, BaseAxisTable? vertAxis,
        VariationStoreTable? itemVariationStore, uint itemVariationStoreOffset,
        DataLocation location, int consumedLength)
    {
        Version = version; NumGlyphs = numGlyphs;
        HorizAxis = horizAxis; VertAxis = vertAxis;
        ItemVariationStore = itemVariationStore; ItemVariationStoreOffset = itemVariationStoreOffset;
        Location = location; ConsumedLength = consumedLength;
    }

    public uint Version { get; }
    public int NumGlyphs { get; }
    /// <summary>The horizontal axis tree; null is the real-witness vert-NULL shape (R4)
    /// and both-null is the OQ-3 legal empty table.</summary>
    public BaseAxisTable? HorizAxis { get; }
    public BaseAxisTable? VertAxis { get; }
    /// <summary>The owned, fully decoded item variation store (null when the v1.1
    /// offset is zero or the version is earlier). A fmt3 coord's 0x8000 device resolves
    /// its (outer, inner) row against this store - the own-store direct path (OQ-6).</summary>
    public VariationStoreTable? ItemVariationStore { get; }
    /// <summary>Raw ItemVariationStore header value under its v1.1 existence domain;
    /// zero for earlier versions (consumed accounting and the header-slot write).</summary>
    public uint ItemVariationStoreOffset { get; }
    public DataLocation Location { get; }
    public int ConsumedLength { get; }
}
