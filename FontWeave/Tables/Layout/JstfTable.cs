using FontWeave.Core;

namespace FontWeave.Tables.Layout;

/// <summary>Which neighbor LookupList a JstfModList's indices address. The wire
/// carries no kind field: the domain is fixed by the referencing JstfPriority slot's
/// position (section 0's slot matrix) and rides the owned model so the edit and
/// compile faces re-answer the right authority count.</summary>
public enum JstfLookupDomain : byte { Gsub, Gpos }

/// <summary>One ExtenderGlyph list: the extender glyph ids in wire order (the
/// increasing order is the OQ-2 observation, never re-sorted). Every id carried the
/// read-time numGlyphs domain proof (the kern OQ-4 hard half).</summary>
public sealed class JstfExtenderGlyphTable
{
    private readonly ushort[] _glyphs;

    internal JstfExtenderGlyphTable(ushort[] glyphs, DataLocation location, int consumedLength)
    {
        _glyphs = glyphs; Location = location; ConsumedLength = consumedLength;
    }

    public int GlyphCount => _glyphs.Length;
    public IReadOnlyList<ushort> Glyphs => Array.AsReadOnly(_glyphs);
    public DataLocation Location { get; }
    public int ConsumedLength { get; }
}

/// <summary>One JstfModList: the raw lookup indices in wire order (the increasing
/// order is the OQ-2 observation). Single view over both the GSUB and the GPOS mod
/// list (OQ-1); the domain names the neighbor table whose LookupList count bounded
/// every index at read time and bounds every edit at write time. The indices stay
/// reserved - resolving or applying them is the Operations domain (T0 ban).</summary>
public sealed class JstfModListTable
{
    private readonly ushort[] _indices;

    internal JstfModListTable(JstfLookupDomain domain, ushort[] indices,
        DataLocation location, int consumedLength)
    {
        Domain = domain; _indices = indices; Location = location; ConsumedLength = consumedLength;
    }

    public JstfLookupDomain Domain { get; }
    public int LookupCount => _indices.Length;
    public IReadOnlyList<ushort> LookupIndices => Array.AsReadOnly(_indices);
    public DataLocation Location { get; }
    public int ConsumedLength { get; }
}

/// <summary>One JstfMax: the owned GPOS-shaped inline lookups in wire (design)
/// order - design order is never checked (the spec names no order). The lookups are
/// the existing typed <see cref="LookupTable"/> models (OQ-6's zero parallel reader):
/// types 1-6 are the typed full spectrum, types 7/8 refused at the OQ-3 exclusion
/// face, type 9 through the one-level extension unwrap with the wrapped {7,8} ban.</summary>
public sealed class JstfMaxTable
{
    private readonly LookupTable[] _lookups;

    internal JstfMaxTable(LookupTable[] lookups, DataLocation location, int consumedLength)
    {
        _lookups = lookups; Location = location; ConsumedLength = consumedLength;
    }

    public int LookupCount => _lookups.Length;
    public IReadOnlyList<LookupTable> Lookups => Array.AsReadOnly(_lookups);
    public DataLocation Location { get; }
    public int ConsumedLength { get; }
}

/// <summary>One JstfPriority: the ten Optional slots in wire order. The slot position
/// is the kind (section 0's matrix) - a null slot is the spec's "may be NULL" and the
/// all-NULL priority is the OQ-5 legal positive. The enable/disable combination is
/// the client's application semantics and stays outside this model (section 0's
/// third correction; the T0 no-justification ban).</summary>
public sealed class JstfPriorityTable
{
    internal JstfPriorityTable(JstfModListTable? gsubShrinkageEnable,
        JstfModListTable? gsubShrinkageDisable, JstfModListTable? gposShrinkageEnable,
        JstfModListTable? gposShrinkageDisable, JstfMaxTable? shrinkageJstfMax,
        JstfModListTable? gsubExtensionEnable, JstfModListTable? gsubExtensionDisable,
        JstfModListTable? gposExtensionEnable, JstfModListTable? gposExtensionDisable,
        JstfMaxTable? extensionJstfMax, DataLocation location, int consumedLength)
    {
        GsubShrinkageEnable = gsubShrinkageEnable;
        GsubShrinkageDisable = gsubShrinkageDisable;
        GposShrinkageEnable = gposShrinkageEnable;
        GposShrinkageDisable = gposShrinkageDisable;
        ShrinkageJstfMax = shrinkageJstfMax;
        GsubExtensionEnable = gsubExtensionEnable;
        GsubExtensionDisable = gsubExtensionDisable;
        GposExtensionEnable = gposExtensionEnable;
        GposExtensionDisable = gposExtensionDisable;
        ExtensionJstfMax = extensionJstfMax;
        Location = location; ConsumedLength = consumedLength;
    }

    public JstfModListTable? GsubShrinkageEnable { get; }
    public JstfModListTable? GsubShrinkageDisable { get; }
    public JstfModListTable? GposShrinkageEnable { get; }
    public JstfModListTable? GposShrinkageDisable { get; }
    public JstfMaxTable? ShrinkageJstfMax { get; }
    public JstfModListTable? GsubExtensionEnable { get; }
    public JstfModListTable? GsubExtensionDisable { get; }
    public JstfModListTable? GposExtensionEnable { get; }
    public JstfModListTable? GposExtensionDisable { get; }
    public JstfMaxTable? ExtensionJstfMax { get; }
    public DataLocation Location { get; }
    public int ConsumedLength { get; }
}

/// <summary>One JstfLangSys row: the language-system tag and its Required priority
/// list. The wire order is what this model records (alphabetical order is the OQ-2
/// observation, never re-sorted).</summary>
public readonly record struct JstfLangSysRecord(uint Tag, JstfLangSysTable LangSys);

/// <summary>One JstfLangSys: the priority tables in array order (the array order is
/// the semantic priority order; a zero count is the OQ-5 legal empty form).</summary>
public sealed class JstfLangSysTable
{
    private readonly JstfPriorityTable[] _priorities;

    internal JstfLangSysTable(JstfPriorityTable[] priorities, DataLocation location,
        int consumedLength)
    {
        _priorities = priorities; Location = location; ConsumedLength = consumedLength;
    }

    public int PriorityCount => _priorities.Length;
    public IReadOnlyList<JstfPriorityTable> Priorities => Array.AsReadOnly(_priorities);
    public DataLocation Location { get; }
    public int ConsumedLength { get; }
}

/// <summary>One JstfScript row: the script tag and its Required body (repeated
/// offsets materialize one shared instance - the BASE R7 shared-identity rule).</summary>
public readonly record struct JstfScriptRecord(uint Tag, JstfScriptTable Script);

/// <summary>One JstfScript: both Optional slots (the spec's double "may be NULL";
/// both NULL with a zero langsys count is the OQ-5 legal empty script) and the
/// JstfLangSys rows in wire order.</summary>
public sealed class JstfScriptTable
{
    private readonly JstfLangSysRecord[] _langSys;

    internal JstfScriptTable(JstfExtenderGlyphTable? extenderGlyphs,
        JstfLangSysTable? defaultLangSys, JstfLangSysRecord[] langSys,
        DataLocation location, int consumedLength)
    {
        ExtenderGlyphs = extenderGlyphs; DefaultLangSys = defaultLangSys;
        _langSys = langSys; Location = location; ConsumedLength = consumedLength;
    }

    public JstfExtenderGlyphTable? ExtenderGlyphs { get; }
    public JstfLangSysTable? DefaultLangSys { get; }
    public int LangSysCount => _langSys.Length;
    public IReadOnlyList<JstfLangSysRecord> LangSys => Array.AsReadOnly(_langSys);
    public DataLocation Location { get; }
    public int ConsumedLength { get; }
}

/// <summary>Owned whole-JSTF slice (version 1.0): the script tree materialized under
/// the shared-identity rule (a wire position materializes once and keeps model
/// identity - LangSys, ModList, Max and inline lookup caches; the ModList cache keys
/// the domain too, so a cross-kind shared modlist reads once per authority while
/// same-kind shares stay one instance). numGlyphs and the two neighbor lookup counts
/// are the explicit authority inputs the walk was answered with; the GDEF context
/// (nullable, first-class) is the JstfMax lookup chain's flags/store dependency.
/// The consumed length is the max end over every walked block (the BASE R9
/// accounting, the whole span for the witnesses' trailing bodies). Lookup-index
/// resolution, justification application and script-tag correspondence with the
/// GSUB/GPOS ScriptLists stay outside this model (the T0 Operations ban).</summary>
public sealed class JstfTable
{
    private readonly JstfScriptRecord[] _scripts;

    internal JstfTable(uint version, int numGlyphs, int gsubLookupCount, int gposLookupCount,
        GdefLookupContext? context, JstfScriptRecord[] scripts, DataLocation location,
        int consumedLength)
    {
        Version = version; NumGlyphs = numGlyphs;
        GsubLookupCount = gsubLookupCount; GposLookupCount = gposLookupCount;
        Context = context; _scripts = scripts;
        Location = location; ConsumedLength = consumedLength;
    }

    public uint Version { get; }
    /// <summary>The explicit numGlyphs input the extender GID domain was answered with.</summary>
    public int NumGlyphs { get; }
    /// <summary>The explicit GSUB LookupList count the GSUB-slot index domain was answered with.</summary>
    public int GsubLookupCount { get; }
    /// <summary>The explicit GPOS LookupList count the GPOS-slot index domain and the
    /// inline lookup chain were answered with.</summary>
    public int GposLookupCount { get; }
    /// <summary>The caller-supplied GDEF context the inline lookup flags contract was
    /// answered under; null is the first-class no-GDEF input (the MissingTable family
    /// for context-dependent flags, the T4-02/T4-06 shapes).</summary>
    public GdefLookupContext? Context { get; }
    public int ScriptCount => _scripts.Length;
    public IReadOnlyList<JstfScriptRecord> Scripts => Array.AsReadOnly(_scripts);
    public DataLocation Location { get; }
    public int ConsumedLength { get; }
}
