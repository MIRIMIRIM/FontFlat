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

    internal ChainingRule WithRecord(int index, SequenceLookupRecord record)
    {
        var copy = (SequenceLookupRecord[])_records.Clone();
        copy[index] = record;
        return new(_backtrack, _input, _lookahead, copy, Location, ConsumedLength);
    }
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

    internal ChainingRuleSet WithRule(int index, ChainingRule rule)
    {
        var copy = (ChainingRule[])_rules.Clone();
        copy[index] = rule;
        long consumed = ContextualRuleSetLayout.HeaderLength +
            ContextualRuleSetLayout.OffsetStride * copy.Length;
        foreach (var item in copy)
            consumed += item.ConsumedLength;
        return new(copy, Location, (int)consumed);
    }
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

    // The T4-04 edit APIs: one record slot per chaining rule, re-run through the rule
    // factory so every domain check re-fires (the write invariant: an edited model
    // must be spec-conforming). Format 3's records are the owner's flat list, not a
    // rule body, so the rule edits address formats 1/2 only.
    public ReadStatus TryWithSequenceLookupIndex(int ruleSetIndex, int ruleIndex, int recordIndex,
        ushort lookupListIndex, int lookupCount, out ChainingSubstTable? updated,
        out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        updated = null;
        ReadStatus status = Locate(ruleSetIndex, ruleIndex, recordIndex, out var rule,
            out var ruleSet, out diagnostic);
        if (status != ReadStatus.Done) return status;
        return Replace(rule!, ruleSet!, ruleIndex, recordIndex,
            new SequenceLookupRecord(rule!.LookupRecords[recordIndex].SequenceIndex, lookupListIndex),
            ruleSetIndex, lookupCount, out updated, out diagnostic, limits);
    }

    public ReadStatus TryWithSequenceIndex(int ruleSetIndex, int ruleIndex, int recordIndex,
        ushort sequenceIndex, out ChainingSubstTable? updated, out FontDiagnostic diagnostic,
        BuildLimits? limits = null)
    {
        updated = null;
        ReadStatus status = Locate(ruleSetIndex, ruleIndex, recordIndex, out var rule,
            out var ruleSet, out diagnostic);
        if (status != ReadStatus.Done) return status;
        return Replace(rule!, ruleSet!, ruleIndex, recordIndex,
            new SequenceLookupRecord(sequenceIndex, rule!.LookupRecords[recordIndex].LookupListIndex),
            ruleSetIndex, lookupCount: -1, out updated, out diagnostic, limits);
    }

    private ReadStatus Locate(int ruleSetIndex, int ruleIndex, int recordIndex,
        out ChainingRule? rule, out ChainingRuleSet? ruleSet, out FontDiagnostic diagnostic)
    {
        rule = null;
        ruleSet = null;
        var sets = RuleSets;
        if (sets is null || ruleSetIndex < 0 || ruleSetIndex >= sets.Count)
            return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                Location.AtLocal(DataField.Count, Format == 2 ? 10u : 4u), out diagnostic,
                unchecked((ulong)(long)ruleSetIndex), (ulong)(sets?.Count ?? 0));
        var set = sets[ruleSetIndex];
        if (set is null)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.AtLocal(DataField.Offset, 0, ruleSetIndex), out diagnostic);
        if (ruleIndex < 0 || ruleIndex >= set.Rules.Length)
            return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                Location.AtLocal(DataField.Count, 0), out diagnostic,
                unchecked((ulong)(long)ruleIndex), (ulong)set.Rules.Length);
        var candidate = set.Rules[ruleIndex];
        if (recordIndex < 0 || recordIndex >= candidate.LookupRecords.Length)
            return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                candidate.Location.AtLocal(DataField.Count, 2), out diagnostic,
                unchecked((ulong)(long)recordIndex), (ulong)candidate.LookupRecords.Length);
        rule = candidate;
        ruleSet = set;
        diagnostic = default;
        return ReadStatus.Done;
    }

    private ReadStatus Replace(ChainingRule rule, ChainingRuleSet set, int ruleIndex,
        int recordIndex, SequenceLookupRecord record, int ruleSetIndex, int lookupCount,
        out ChainingSubstTable? updated, out FontDiagnostic diagnostic, BuildLimits? limits)
    {
        updated = null;
        var budget = new BuildBudget(limits);
        if (!budget.Input(rule.ConsumedLength, rule.Location, out diagnostic))
            return ReadStatus.ResourceLimit;
        // Re-run the rule factory on the replacement record: the domain checks
        // re-fire and the shape (record count) cannot change.
        var candidate = new SequenceLookupRecord[rule.LookupRecords.Length];
        for (int i = 0; i < candidate.Length; i++)
            candidate[i] = i == recordIndex ? record : rule.LookupRecords[i];
        ReadStatus status = ChainingSubstTableCompiler.TryCreateRule(rule.BacktrackSequence,
            rule.InputSequence, rule.LookaheadSequence, candidate, lookupCount, out _,
            out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        var updatedRule = rule.WithRecord(recordIndex, record);
        var updatedSet = set.WithRule(ruleIndex, updatedRule);
        updated = WithRuleSet(ruleSetIndex, updatedSet);
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>Single-slot replacement for the rule-set edit (null keeps the
    /// spec-NULL offset).</summary>
    internal ChainingSubstTable WithRuleSet(int index, ChainingRuleSet? ruleSet)
    {
        var copy = (ChainingRuleSet?[])_ruleSets.Clone();
        copy[index] = ruleSet;
        return new(Format, Coverage, BacktrackClassDef, InputClassDef, LookaheadClassDef, copy,
            _backtrackCoverages, _inputCoverages, _lookaheadCoverages, _records, NumGlyphs,
            Location, ConsumedLength);
    }
}
