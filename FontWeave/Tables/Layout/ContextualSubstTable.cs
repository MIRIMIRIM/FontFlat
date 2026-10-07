using FontWeave.Core;

namespace FontWeave.Tables.Layout;

/// <summary>One SeqLookupRecord: the input slot the replacement applies at and the
/// lookup that performs it. Both indices are the lookup-index graph's domain checks
/// (SequenceIndex &lt; the owning rule's glyphCount, LookupListIndex &lt; the caller's
/// lookup count, OQ-4's structural-corruption refusals).</summary>
public readonly record struct SequenceLookupRecord(ushort SequenceIndex, ushort LookupListIndex);

/// <summary>One contextual rule body (types 5 formats 1/2): the input length (the
/// spec's glyphCount; the first input comes from the subtable's coverage/class, so
/// the stored sequence carries glyphCount-1 entries) and the replacement records in
/// wire order. The interpretation of the input values (glyph IDs or class values)
/// follows the owning subtable's format, not the rule.</summary>
public sealed class ContextualRule
{
    private readonly ushort[] _input;
    private readonly SequenceLookupRecord[] _records;

    internal ContextualRule(ushort[] input, SequenceLookupRecord[] records,
        DataLocation location, int consumedLength)
    {
        _input = input; _records = records;
        Location = location; ConsumedLength = consumedLength;
    }

    /// <summary>The spec's glyphCount: one coverage/class first input plus the stored
    /// sequence. The declared-underflow shape (glyphCount=0) refuses at the walk.</summary>
    public int InputCount => _input.Length + 1;

    /// <summary>The glyphCount-1 input values after the first (glyph IDs under
    /// format 1, class values under format 2).</summary>
    public ReadOnlySpan<ushort> InputSequence => _input;

    public ReadOnlySpan<SequenceLookupRecord> LookupRecords => _records;

    public DataLocation Location { get; }

    public int ConsumedLength { get; }
}

/// <summary>One contextual rule set (the shared declared header opened under the
/// Sequence or ClassSequence role): the rules in wire order. A spec-NULL set never
/// produces this model - the owning table carries a null entry instead.</summary>
public sealed class ContextualRuleSet
{
    private readonly ContextualRule[] _rules;

    internal ContextualRuleSet(ContextualRule[] rules, DataLocation location, int consumedLength)
    {
        _rules = rules;
        Location = location; ConsumedLength = consumedLength;
    }

    public ReadOnlySpan<ContextualRule> Rules => _rules;

    public int RuleCount => _rules.Length;

    public DataLocation Location { get; }

    public int ConsumedLength { get; }
}

/// <summary>Owned GSUB type-5 subtable (ContextualSubst formats 1/2/3): format 1/2
/// carry the coverage (class: plus the ClassDef) and the rule sets whose ordinals
/// correspond to coverage entries (format 1) or class values (format 2); a null set
/// entry is the spec's legal NULL offset. Format 3 is count-first and carries the
/// glyphCount coverage array plus the flat seqLookup records. Rules are fully
/// materialized - the witness scale is bounded (1365 rules over 5 real fmt2
/// subtables, 79 fmt3 subtables of at most 2 records, frozen facts R5) - with the
/// same whole-walk discipline as the direct leaves.</summary>
public sealed class ContextualSubstTable
{
    private readonly ContextualRuleSet?[] _ruleSets;
    private readonly CoverageTable[]? _coverages;
    private readonly SequenceLookupRecord[]? _records;

    internal ContextualSubstTable(ushort format, CoverageTable coverage, ClassDefTable? classDef,
        ContextualRuleSet?[] ruleSets, CoverageTable[]? coverages, SequenceLookupRecord[]? records,
        int numGlyphs, DataLocation location, int consumedLength)
    {
        Format = format; Coverage = coverage; ClassDef = classDef;
        _ruleSets = ruleSets; _coverages = coverages; _records = records;
        NumGlyphs = numGlyphs; Location = location; ConsumedLength = consumedLength;
    }

    public ushort Format { get; }

    /// <summary>The single coverage of formats 1/2. Format 3 aggregates its coverage
    /// array here in wire order (the per-glyph slots stay in <see cref="Coverages"/>
    /// for ordinal-addressed consumers).</summary>
    public CoverageTable Coverage { get; }

    /// <summary>The format-2 class definition; null under the other formats.</summary>
    public ClassDefTable? ClassDef { get; }

    /// <summary>Formats 1/2: the rule sets, one entry per coverage entry (format 1)
    /// or class value (format 2), null entries marking spec-NULL offsets. Null under
    /// format 3.</summary>
    public IReadOnlyList<ContextualRuleSet?>? RuleSets => _ruleSets;

    /// <summary>Format 3: the glyphCount coverage tables in wire order. Null under
    /// the other formats.</summary>
    public IReadOnlyList<CoverageTable>? Coverages => _coverages;

    /// <summary>Format 3: the seqLookup records. Null under the other formats.</summary>
    public IReadOnlyList<SequenceLookupRecord>? LookupRecords => _records;

    public int NumGlyphs { get; }

    public DataLocation Location { get; }

    public int ConsumedLength { get; }
}
