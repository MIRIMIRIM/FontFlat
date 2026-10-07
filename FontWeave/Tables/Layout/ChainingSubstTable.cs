using FontWeave.Core;

namespace FontWeave.Tables.Layout;

/// <summary>One chaining rule body (types 6 formats 1/2): the backtrack, input and
/// lookahead sequences in wire order plus the replacement records. The first input
/// value comes from the subtable's coverage/class, so the stored input sequence
/// carries inputGlyphCount-1 entries; the backtrack and lookahead sequences are
/// complete (their coverage/class sources are the trailing arrays and the class
/// definitions respectively).</summary>
public sealed class ChainingRule
{
    private readonly ushort[] _backtrack;
    private readonly ushort[] _input;
    private readonly ushort[] _lookahead;
    private readonly SequenceLookupRecord[] _records;

    internal ChainingRule(ushort[] backtrack, ushort[] input, ushort[] lookahead,
        SequenceLookupRecord[] records, DataLocation location, int consumedLength)
    {
        _backtrack = backtrack; _input = input; _lookahead = lookahead; _records = records;
        Location = location; ConsumedLength = consumedLength;
    }

    /// <summary>The spec's inputGlyphCount: one coverage/class first input plus the
    /// stored sequence. The declared-underflow shape (inputGlyphCount=0) refuses at
    /// the walk.</summary>
    public int InputCount => _input.Length + 1;

    public ReadOnlySpan<ushort> BacktrackSequence => _backtrack;

    /// <summary>The inputGlyphCount-1 input values after the first (glyph IDs under
    /// format 1, class values under format 2).</summary>
    public ReadOnlySpan<ushort> InputSequence => _input;

    public ReadOnlySpan<ushort> LookaheadSequence => _lookahead;

    public ReadOnlySpan<SequenceLookupRecord> LookupRecords => _records;

    public DataLocation Location { get; }

    public int ConsumedLength { get; }
}

/// <summary>One chaining rule set (the shared declared header opened under the
/// ChainedSequence or ChainedClassSequence role): the rules in wire order. A
/// spec-NULL set never produces this model - the owning table carries a null entry.</summary>
public sealed class ChainingRuleSet
{
    private readonly ChainingRule[] _rules;

    internal ChainingRuleSet(ChainingRule[] rules, DataLocation location, int consumedLength)
    {
        _rules = rules;
        Location = location; ConsumedLength = consumedLength;
    }

    public ReadOnlySpan<ChainingRule> Rules => _rules;

    public int RuleCount => _rules.Length;

    public DataLocation Location { get; }

    public int ConsumedLength { get; }
}

/// <summary>Owned GSUB type-6 subtable (ChainingContextSubst formats 1/2/3): the
/// format-1/2 rule sets over their coverage (class: over the three class
/// definitions, which may legally share one address); a null set entry is the
/// spec's legal NULL offset. Format 3 carries the three coverage chains - the
/// declared backtrack array plus the hand-walked input/lookahead chains - and the
/// flat seqLookup records. Rules are fully materialized (the witness scale is
/// bounded: the largest real fmt3 subtable carries 281 rule sets, frozen facts
/// R5).</summary>
public sealed class ChainingSubstTable
{
    private readonly ChainingRuleSet?[] _ruleSets;
    private readonly CoverageTable[]? _backtrackCoverages;
    private readonly CoverageTable[]? _inputCoverages;
    private readonly CoverageTable[]? _lookaheadCoverages;
    private readonly SequenceLookupRecord[]? _records;

    internal ChainingSubstTable(ushort format, CoverageTable? coverage,
        ClassDefTable? backtrackClassDef, ClassDefTable? inputClassDef,
        ClassDefTable? lookaheadClassDef, ChainingRuleSet?[] ruleSets,
        CoverageTable[]? backtrackCoverages, CoverageTable[]? inputCoverages,
        CoverageTable[]? lookaheadCoverages, SequenceLookupRecord[]? records,
        int numGlyphs, DataLocation location, int consumedLength)
    {
        Format = format; Coverage = coverage;
        BacktrackClassDef = backtrackClassDef; InputClassDef = inputClassDef;
        LookaheadClassDef = lookaheadClassDef;
        _ruleSets = ruleSets;
        _backtrackCoverages = backtrackCoverages; _inputCoverages = inputCoverages;
        _lookaheadCoverages = lookaheadCoverages; _records = records;
        NumGlyphs = numGlyphs; Location = location; ConsumedLength = consumedLength;
    }

    public ushort Format { get; }

    /// <summary>The single coverage of formats 1/2; null under format 3.</summary>
    public CoverageTable? Coverage { get; }

    /// <summary>The format-2 class definitions; null under the other formats. Two
    /// slots may legally reference one shared ClassDef body (the W12 precedent) -
    /// identity sharing is never assumed from equal addresses.</summary>
    public ClassDefTable? BacktrackClassDef { get; }
    public ClassDefTable? InputClassDef { get; }
    public ClassDefTable? LookaheadClassDef { get; }

    /// <summary>Formats 1/2: the rule sets in wire order, null entries marking
    /// spec-NULL offsets. Null under format 3.</summary>
    public IReadOnlyList<ChainingRuleSet?>? RuleSets => _ruleSets;

    /// <summary>Format 3: the three coverage chains (the backtrack chain is the
    /// declared array; the input/lookahead chains are the hand-walked computed-start
    /// arrays). Null under the other formats.</summary>
    public IReadOnlyList<CoverageTable>? BacktrackCoverages => _backtrackCoverages;
    public IReadOnlyList<CoverageTable>? InputCoverages => _inputCoverages;
    public IReadOnlyList<CoverageTable>? LookaheadCoverages => _lookaheadCoverages;

    /// <summary>Format 3: the seqLookup records. Null under the other formats.</summary>
    public IReadOnlyList<SequenceLookupRecord>? LookupRecords => _records;

    public int NumGlyphs { get; }

    public DataLocation Location { get; }

    public int ConsumedLength { get; }
}
