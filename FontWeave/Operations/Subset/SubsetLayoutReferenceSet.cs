using FontWeave.Tables.Layout;

namespace FontWeave.Operations.Subset;

/// <summary>The layout reference union's working set (S2a card §2.3/§2.4).
///
/// The enumerators used to accumulate into a plain <see cref="HashSet{T}"/>, which
/// made two costs multiply: a class-0 reference means "the whole unassigned domain",
/// so every occurrence re-scanned the glyph range and re-inserted every GID, and the
/// same lookup object (JSTF's inline JstfMax tables alias the GSUB/GPOS lookups by
/// wire position) was re-walked once per alias. Neither cost is charged by the work
/// budget, which counts newly retained GIDs, so a table with many class-0 references
/// burned CPU far beyond what the budget reported.
///
/// This set keeps both facts explicit: reaching the whole domain collapses to one
/// flag instead of numGlyphs insertions, a class definition expands its ranges once
/// and then answers per class value, and a lookup is walked once per instance. The
/// merge and the verification share one instance type, so the two consumers still
/// cannot drift apart.</summary>
internal sealed class LayoutReferenceSet
{
    private readonly HashSet<int> _glyphs = [];
    private readonly Dictionary<ClassDefTable, ClassExpansion> _classes =
        new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<LookupTable> _lookups = new(ReferenceEqualityComparer.Instance);

    internal LayoutReferenceSet(int numGlyphs)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(numGlyphs);
        NumGlyphs = numGlyphs;
    }

    /// <summary>The caller's glyph domain (the same authority the enumerators pass).</summary>
    internal int NumGlyphs { get; }

    /// <summary>True once a reference meant the whole glyph domain (a class-0 cell or
    /// a class-matrix domain): the set then carries no explicit members.</summary>
    internal bool AllGlyphs { get; private set; }

    internal void Add(int glyphId)
    {
        if (AllGlyphs) return;
        _glyphs.Add(glyphId);
    }

    internal void Add(ushort glyphId) => Add((int)glyphId);

    /// <summary>Saturates to the whole domain. The caller's own domain is the upper
    /// bound the OQ-S2a-6 lean names, so the census reports it honestly.</summary>
    internal void AddAll()
    {
        AllGlyphs = true;
        _glyphs.Clear();
        _classes.Clear();
    }

    /// <summary>The reference count the census reports (the domain itself once the
    /// set saturated).</summary>
    internal int Count => AllGlyphs ? NumGlyphs : _glyphs.Count;

    /// <summary>Empties the set for the next table's collection (the merge and the
    /// verification walk one table at a time so each census row counts only its own
    /// references).</summary>
    internal void Clear()
    {
        AllGlyphs = false;
        _glyphs.Clear();
        _classes.Clear();
        _lookups.Clear();
    }

    internal IEnumerable<int> Enumerate() =>
        AllGlyphs ? Enumerable.Range(0, NumGlyphs) : _glyphs;

    /// <summary>One lookup instance is one walk: JSTF's inline JstfMax tables alias the
    /// GSUB/GPOS lookup objects, and the same object always enumerates the same
    /// references.</summary>
    internal bool TryMarkLookup(LookupTable lookup) => _lookups.Add(lookup);

    /// <summary>Expands one referenced class value through its class definition. A
    /// zero class value is the unassigned domain, whose merge upper bound is the whole
    /// glyph domain (OQ-S2a-6); any other value reads the definition's ranges through
    /// the per-definition expansion built on first use.</summary>
    internal void AddClassValue(ClassDefTable classDef, ushort classValue)
    {
        if (AllGlyphs) return;
        if (classValue == 0)
        {
            AddAll();
            return;
        }
        if (!_classes.TryGetValue(classDef, out var expansion))
        {
            expansion = new ClassExpansion(classDef);
            _classes.Add(classDef, expansion);
        }
        foreach (ushort glyph in expansion.GlyphsOf(classValue)) _glyphs.Add(glyph);
    }

    /// <summary>One class definition's ranges grouped by class value, built once per
    /// collection run instead of re-scanned per reference.</summary>
    private sealed class ClassExpansion
    {
        private readonly Dictionary<ushort, List<ushort>> _byValue = [];

        internal ClassExpansion(ClassDefTable classDef)
        {
            foreach (var range in classDef.Ranges)
            {
                if (!_byValue.TryGetValue(range.ClassValue, out var glyphs))
                    _byValue.Add(range.ClassValue, glyphs = []);
                for (int glyph = range.StartGlyph; glyph <= range.EndGlyph; glyph++)
                    glyphs.Add((ushort)glyph);
            }
        }

        internal IReadOnlyList<ushort> GlyphsOf(ushort classValue) =>
            _byValue.TryGetValue(classValue, out var glyphs) ? glyphs : [];
    }
}
