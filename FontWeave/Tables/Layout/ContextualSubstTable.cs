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

    /// <summary>Single-slot replacement for the record edit; the shape cannot change
    /// (the record count is the rule's wire layout).</summary>
    internal ContextualRule WithRecord(int index, SequenceLookupRecord record)
    {
        var copy = (SequenceLookupRecord[])_records.Clone();
        copy[index] = record;
        return new(_input, copy, Location, ConsumedLength);
    }
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

    /// <summary>Single-slot replacement for the rule edit; each slot carries its own
    /// consumed length, so the set's extent is recomputed from the rules.</summary>
    internal ContextualRuleSet WithRule(int index, ContextualRule rule)
    {
        var copy = (ContextualRule[])_rules.Clone();
        copy[index] = rule;
        long consumed = ContextualRuleSetLayout.HeaderLength +
            ContextualRuleSetLayout.OffsetStride * copy.Length;
        foreach (var item in copy)
            consumed += item.ConsumedLength;
        return new(copy, Location, (int)consumed);
    }
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

    // The T4-04 edit APIs: one record slot per rule, re-run through the rule factory
    // so every domain check re-fires (the write invariant: an edited model must be
    // spec-conforming).
    public ReadStatus TryWithSequenceLookupIndex(int ruleSetIndex, int ruleIndex, int recordIndex,
        ushort lookupListIndex, int lookupCount, out ContextualSubstTable? updated,
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
        ushort sequenceIndex, out ContextualSubstTable? updated, out FontDiagnostic diagnostic,
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
        out ContextualRule? rule, out ContextualRuleSet? ruleSet, out FontDiagnostic diagnostic)
    {
        rule = null;
        ruleSet = null;
        var sets = RuleSets;
        if (sets is null || ruleSetIndex < 0 || ruleSetIndex >= sets.Count)
            return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                Location.AtLocal(DataField.Count, 4), out diagnostic,
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

    private ReadStatus Replace(ContextualRule rule, ContextualRuleSet set, int ruleIndex,
        int recordIndex, SequenceLookupRecord record, int ruleSetIndex, int lookupCount,
        out ContextualSubstTable? updated, out FontDiagnostic diagnostic, BuildLimits? limits)
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
        ReadStatus status = ContextualSubstTableCompiler.TryCreateRule(rule.InputSequence,
            candidate, lookupCount, out _, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        var updatedRule = rule.WithRecord(recordIndex, record);
        var updatedSet = set.WithRule(ruleIndex, updatedRule);
        updated = WithRuleSet(ruleSetIndex, updatedSet);
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>Single-slot replacement for the rule-set edit (null keeps the
    /// spec-NULL offset).</summary>
    internal ContextualSubstTable WithRuleSet(int index, ContextualRuleSet? ruleSet)
    {
        var copy = (ContextualRuleSet?[])_ruleSets.Clone();
        copy[index] = ruleSet;
        return new(Format, Coverage, ClassDef, copy, _coverages, _records, NumGlyphs,
            Location, ConsumedLength);
    }
}
