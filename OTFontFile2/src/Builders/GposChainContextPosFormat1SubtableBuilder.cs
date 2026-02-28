namespace OTFontFile2.Tables;

/// <summary>
/// Mutable builder for GPOS ChainContextPos subtables (lookup type 8), format 1 (rule-based).
/// </summary>
public sealed class GposChainContextPosFormat1SubtableBuilder
{
    private readonly List<RuleSet> _ruleSets = new();

    private bool _dirty = true;
    private byte[]? _built;

    public int RuleSetCount => _ruleSets.Count;

    public void Clear()
    {
        if (_ruleSets.Count == 0)
            return;

        _ruleSets.Clear();
        MarkDirty();
    }

    public void AddRule(
        ushort startGlyphId,
        ReadOnlySpan<ushort> backtrackGlyphIds,
        ReadOnlySpan<ushort> inputGlyphIds,
        ReadOnlySpan<ushort> lookaheadGlyphIds,
        ReadOnlySpan<SequenceLookupRecord> posLookupRecords)
    {
        var rule = new Rule(
            backtrackGlyphIds.ToArray(),
            inputGlyphIds.ToArray(),
            lookaheadGlyphIds.ToArray(),
            posLookupRecords.ToArray());

        for (int i = 0; i < _ruleSets.Count; i++)
        {
            if (_ruleSets[i].StartGlyphId != startGlyphId)
                continue;

            _ruleSets[i].Rules.Add(rule);
            MarkDirty();
            return;
        }

        var rules = new List<Rule>(capacity: 1) { rule };
        _ruleSets.Add(new RuleSet(startGlyphId, rules));
        MarkDirty();
    }

    public bool RemoveRuleSet(ushort startGlyphId)
    {
        bool removed = false;
        for (int i = _ruleSets.Count - 1; i >= 0; i--)
        {
            if (_ruleSets[i].StartGlyphId == startGlyphId)
            {
                _ruleSets.RemoveAt(i);
                removed = true;
            }
        }

        if (removed)
            MarkDirty();

        return removed;
    }

    public byte[] ToArray()
    {
        EnsureBuilt();
        return _built!;
    }

    public ReadOnlyMemory<byte> ToMemory() => EnsureBuilt();

    private void MarkDirty()
    {
        _dirty = true;
        _built = null;
    }

    private ReadOnlyMemory<byte> EnsureBuilt()
    {
        if (!_dirty && _built is not null)
            return _built;

        _built = BuildBytes();
        _dirty = false;
        return _built;
    }

    private byte[] BuildBytes()
    {
        var sets = _ruleSets.Count == 0 ? Array.Empty<RuleSet>() : _ruleSets.ToArray();
        if (sets.Length != 0)
            Array.Sort(sets, static (a, b) => a.StartGlyphId.CompareTo(b.StartGlyphId));

        int setCount = sets.Length;
        if (setCount > ushort.MaxValue)
            throw new InvalidOperationException("ChainPosRuleSetCount must fit in uint16.");

        var coverage = new CoverageTableBuilder();
        for (int i = 0; i < setCount; i++)
            coverage.AddGlyph(sets[i].StartGlyphId);
        byte[] coverageBytes = coverage.ToArray();

        int maxRuleCount = 0;
        for (int i = 0; i < setCount; i++)
        {
            int c = sets[i].Rules.Count;
            if (c > maxRuleCount) maxRuleCount = c;
        }

        Span<OTFontFile2.OffsetWriter.Label> ruleLabelScratch = maxRuleCount <= 64
            ? stackalloc OTFontFile2.OffsetWriter.Label[maxRuleCount]
            : new OTFontFile2.OffsetWriter.Label[maxRuleCount];

        var w = new OTFontFile2.OffsetWriter();
        var coverageLabel = w.CreateLabel();

        w.WriteUInt16(1);
        w.WriteOffset16(coverageLabel, baseOffset: 0);
        w.WriteUInt16(checked((ushort)setCount));

        Span<OTFontFile2.OffsetWriter.Label> setLabels = setCount <= 64
            ? stackalloc OTFontFile2.OffsetWriter.Label[setCount]
            : new OTFontFile2.OffsetWriter.Label[setCount];

        for (int i = 0; i < setCount; i++)
        {
            var label = w.CreateLabel();
            setLabels[i] = label;
            w.WriteOffset16(label, baseOffset: 0);
        }

        w.Align2();
        w.DefineLabelHere(coverageLabel);
        w.WriteBytes(coverageBytes);

        for (int i = 0; i < setCount; i++)
        {
            w.Align2();
            w.DefineLabelHere(setLabels[i]);
            int setStart = w.Position;

            var rules = sets[i].Rules;
            if (rules.Count > ushort.MaxValue)
                throw new InvalidOperationException("ChainPosRuleCount must fit in uint16.");

            int ruleCount = rules.Count;
            w.WriteUInt16(checked((ushort)ruleCount));

            var ruleLabels = ruleLabelScratch.Slice(0, ruleCount);

            for (int r = 0; r < ruleCount; r++)
            {
                var label = w.CreateLabel();
                ruleLabels[r] = label;
                w.WriteOffset16(label, baseOffset: setStart);
            }

            for (int r = 0; r < ruleCount; r++)
            {
                w.Align2();
                w.DefineLabelHere(ruleLabels[r]);

                var rule = rules[r];

                int backCount = rule.BacktrackGlyphIds.Length;
                if (backCount > ushort.MaxValue)
                    throw new InvalidOperationException("BacktrackGlyphCount must fit in uint16.");

                int inputCount = checked(rule.InputGlyphIds.Length + 1);
                if (inputCount > ushort.MaxValue)
                    throw new InvalidOperationException("InputGlyphCount must fit in uint16.");

                int lookCount = rule.LookaheadGlyphIds.Length;
                if (lookCount > ushort.MaxValue)
                    throw new InvalidOperationException("LookaheadGlyphCount must fit in uint16.");

                if (rule.Records.Length > ushort.MaxValue)
                    throw new InvalidOperationException("PosCount must fit in uint16.");

                int posCount = rule.Records.Length;

                w.WriteUInt16(checked((ushort)backCount));
                for (int b = 0; b < backCount; b++)
                    w.WriteUInt16(rule.BacktrackGlyphIds[b]);

                w.WriteUInt16(checked((ushort)inputCount));
                for (int g = 0; g < rule.InputGlyphIds.Length; g++)
                    w.WriteUInt16(rule.InputGlyphIds[g]);

                w.WriteUInt16(checked((ushort)lookCount));
                for (int l = 0; l < lookCount; l++)
                    w.WriteUInt16(rule.LookaheadGlyphIds[l]);

                w.WriteUInt16(checked((ushort)posCount));
                for (int p = 0; p < posCount; p++)
                {
                    var rec = rule.Records[p];
                    if (rec.SequenceIndex >= inputCount)
                        throw new InvalidOperationException("SequenceLookupRecord.SequenceIndex must be < InputGlyphCount.");

                    w.WriteUInt16(rec.SequenceIndex);
                    w.WriteUInt16(rec.LookupListIndex);
                }
            }
        }

        return w.ToArray();
    }

    private readonly struct RuleSet
    {
        public ushort StartGlyphId { get; }
        public List<Rule> Rules { get; }

        public RuleSet(ushort startGlyphId, List<Rule> rules)
        {
            StartGlyphId = startGlyphId;
            Rules = rules;
        }
    }

    private readonly struct Rule
    {
        public ushort[] BacktrackGlyphIds { get; }
        public ushort[] InputGlyphIds { get; }
        public ushort[] LookaheadGlyphIds { get; }
        public SequenceLookupRecord[] Records { get; }

        public Rule(ushort[] backtrackGlyphIds, ushort[] inputGlyphIds, ushort[] lookaheadGlyphIds, SequenceLookupRecord[] records)
        {
            BacktrackGlyphIds = backtrackGlyphIds;
            InputGlyphIds = inputGlyphIds;
            LookaheadGlyphIds = lookaheadGlyphIds;
            Records = records;
        }
    }
}

