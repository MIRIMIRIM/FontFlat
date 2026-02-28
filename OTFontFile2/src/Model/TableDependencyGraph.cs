namespace OTFontFile2;

public enum TableDependencyKind
{
    /// <summary>
    /// If the dependency table is edited, the dependent table must be rebuilt (or explicitly overridden/removed)
    /// before writing a consistent font.
    /// </summary>
    RebuildRequired,

    /// <summary>
    /// Tables should remain consistent, but rebuilding may not be strictly required for correctness in all cases.
    /// </summary>
    ConsistencyRequired,
}

public sealed class TableDependencyGraph
{
    private readonly Dictionary<Tag, List<(Tag dependent, TableDependencyKind kind)>> _dependentsByDependency = new();

    public TableDependencyGraph Clone()
    {
        var clone = new TableDependencyGraph();
        foreach (var (dependency, list) in _dependentsByDependency)
        {
            var copied = new List<(Tag dependent, TableDependencyKind kind)>(list.Count);
            for (int i = 0; i < list.Count; i++)
                copied.Add(list[i]);

            clone._dependentsByDependency.Add(dependency, copied);
        }

        return clone;
    }

    public void AddDependency(Tag dependent, Tag dependency, TableDependencyKind kind)
    {
        if (!_dependentsByDependency.TryGetValue(dependency, out var list))
        {
            list = new List<(Tag dependent, TableDependencyKind kind)>();
            _dependentsByDependency.Add(dependency, list);
        }

        for (int i = 0; i < list.Count; i++)
        {
            var existing = list[i];
            if (existing.dependent == dependent && existing.kind == kind)
                return;
        }

        list.Add((dependent, kind));
    }

    public IEnumerable<Tag> GetDependents(Tag dependency, TableDependencyKind kind)
    {
        if (!_dependentsByDependency.TryGetValue(dependency, out var list))
            yield break;

        for (int i = 0; i < list.Count; i++)
        {
            var (dependent, k) = list[i];
            if (k == kind)
                yield return dependent;
        }
    }

    public IEnumerable<Tag> GetTransitiveDependents(Tag dependency, TableDependencyKind kind)
    {
        var visited = new HashSet<Tag>();
        var queue = new Queue<Tag>();

        queue.Enqueue(dependency);
        visited.Add(dependency);

        while (queue.Count != 0)
        {
            Tag current = queue.Dequeue();

            foreach (Tag dependent in GetDependents(current, kind))
            {
                if (!visited.Add(dependent))
                    continue;

                yield return dependent;
                queue.Enqueue(dependent);
            }
        }
    }

    public static TableDependencyGraph CreateDefault()
    {
        var g = new TableDependencyGraph();

        // Strong rebuild dependencies:
        // - loca is derived from glyf (offsets).
        g.AddDependency(dependent: KnownTags.loca, dependency: KnownTags.glyf, kind: TableDependencyKind.RebuildRequired);

        // - Many tables are sized by maxp.numGlyphs.
        g.AddDependency(dependent: KnownTags.hmtx, dependency: KnownTags.maxp, kind: TableDependencyKind.RebuildRequired);
        g.AddDependency(dependent: KnownTags.vmtx, dependency: KnownTags.maxp, kind: TableDependencyKind.RebuildRequired);
        g.AddDependency(dependent: KnownTags.hdmx, dependency: KnownTags.maxp, kind: TableDependencyKind.RebuildRequired);
        g.AddDependency(dependent: KnownTags.LTSH, dependency: KnownTags.maxp, kind: TableDependencyKind.RebuildRequired);
        g.AddDependency(dependent: KnownTags.post, dependency: KnownTags.maxp, kind: TableDependencyKind.RebuildRequired);
        g.AddDependency(dependent: KnownTags.cmap, dependency: KnownTags.maxp, kind: TableDependencyKind.RebuildRequired);
        g.AddDependency(dependent: KnownTags.Zapf, dependency: KnownTags.maxp, kind: TableDependencyKind.RebuildRequired);
        g.AddDependency(dependent: KnownTags.sbix, dependency: KnownTags.maxp, kind: TableDependencyKind.RebuildRequired);
        g.AddDependency(dependent: KnownTags.gvar, dependency: KnownTags.maxp, kind: TableDependencyKind.RebuildRequired);
        g.AddDependency(dependent: KnownTags.gvar, dependency: KnownTags.glyf, kind: TableDependencyKind.RebuildRequired);

        // - hmtx interpretation depends on hhea.numberOfHMetrics.
        g.AddDependency(dependent: KnownTags.hmtx, dependency: KnownTags.hhea, kind: TableDependencyKind.RebuildRequired);

        // - vmtx interpretation depends on vhea.numOfLongVerMetrics.
        g.AddDependency(dependent: KnownTags.vmtx, dependency: KnownTags.vhea, kind: TableDependencyKind.RebuildRequired);

        // - gvar/cvar depend on fvar axis count.
        g.AddDependency(dependent: KnownTags.gvar, dependency: KnownTags.fvar, kind: TableDependencyKind.RebuildRequired);
        g.AddDependency(dependent: KnownTags.cvar, dependency: KnownTags.fvar, kind: TableDependencyKind.RebuildRequired);
        g.AddDependency(dependent: KnownTags.cvar, dependency: KnownTags.cvt, kind: TableDependencyKind.RebuildRequired);

        // - Bitmap location tables reference bitmap data tables.
        g.AddDependency(dependent: KnownTags.EBLC, dependency: KnownTags.EBDT, kind: TableDependencyKind.RebuildRequired);
        g.AddDependency(dependent: KnownTags.CBLC, dependency: KnownTags.CBDT, kind: TableDependencyKind.RebuildRequired);
        g.AddDependency(dependent: KnownTags.BLOC, dependency: KnownTags.BDAT, kind: TableDependencyKind.RebuildRequired);

        return g;
    }
}
