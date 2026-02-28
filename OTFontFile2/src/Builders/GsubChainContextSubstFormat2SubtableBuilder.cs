namespace OTFontFile2.Tables;

/// <summary>
/// Mutable builder for GSUB ChainContextSubst subtables (lookup type 6), format 2 (class-based).
/// </summary>
public sealed class GsubChainContextSubstFormat2SubtableBuilder
{
    private readonly CoverageTableBuilder _coverage = new();
    private readonly ClassDefTableBuilder _backtrackClassDef = new();
    private readonly ClassDefTableBuilder _inputClassDef = new();
    private readonly ClassDefTableBuilder _lookaheadClassDef = new();
    private readonly List<ChainSubClassSet> _sets = new();

    private bool _dirty = true;
    private byte[]? _built;

    public void Clear()
    {
        _coverage.Clear();
        _backtrackClassDef.Clear();
        _inputClassDef.Clear();
        _lookaheadClassDef.Clear();
        _sets.Clear();
        MarkDirty();
    }

    public void ClearCoverage()
    {
        _coverage.Clear();
        MarkDirty();
    }

    public void AddCoverageGlyph(ushort glyphId)
    {
        _coverage.AddGlyph(glyphId);
        MarkDirty();
    }

    public void AddCoverageGlyphs(ReadOnlySpan<ushort> glyphIds)
    {
        _coverage.AddGlyphs(glyphIds);
        MarkDirty();
    }

    public void ClearBacktrackClassDef()
    {
        _backtrackClassDef.Clear();
        MarkDirty();
    }

    public void SetBacktrackClass(ushort glyphId, ushort classValue)
    {
        _backtrackClassDef.SetClass(glyphId, classValue);
        MarkDirty();
    }

    public void ClearInputClassDef()
    {
        _inputClassDef.Clear();
        MarkDirty();
    }

    public void SetInputClass(ushort glyphId, ushort classValue)
    {
        _inputClassDef.SetClass(glyphId, classValue);
        MarkDirty();
    }

    public void ClearLookaheadClassDef()
    {
        _lookaheadClassDef.Clear();
        MarkDirty();
    }

    public void SetLookaheadClass(ushort glyphId, ushort classValue)
    {
        _lookaheadClassDef.SetClass(glyphId, classValue);
        MarkDirty();
    }

    public void AddRule(
        ushort startClass,
        ReadOnlySpan<ushort> backtrackClasses,
        ReadOnlySpan<ushort> inputClasses,
        ReadOnlySpan<ushort> lookaheadClasses,
        ReadOnlySpan<SequenceLookupRecord> substLookupRecords)
    {
        var rule = new ChainSubClassRule(
            backtrackClasses.ToArray(),
            inputClasses.ToArray(),
            lookaheadClasses.ToArray(),
            substLookupRecords.ToArray());

        for (int i = 0; i < _sets.Count; i++)
        {
            if (_sets[i].StartClass != startClass)
                continue;

            _sets[i].Rules.Add(rule);
            MarkDirty();
            return;
        }

        var rules = new List<ChainSubClassRule>(capacity: 1) { rule };
        _sets.Add(new ChainSubClassSet(startClass, rules));
        MarkDirty();
    }

    public bool RemoveChainSubClassSet(ushort startClass)
    {
        bool removed = false;
        for (int i = _sets.Count - 1; i >= 0; i--)
        {
            if (_sets[i].StartClass == startClass)
            {
                _sets.RemoveAt(i);
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
        var sets = _sets.Count == 0 ? Array.Empty<ChainSubClassSet>() : _sets.ToArray();
        if (sets.Length != 0)
            Array.Sort(sets, static (a, b) => a.StartClass.CompareTo(b.StartClass));

        int chainSubClassSetCount = 0;
        if (sets.Length != 0)
        {
            ushort maxClass = sets[^1].StartClass;
            chainSubClassSetCount = checked(maxClass + 1);
        }

        if (chainSubClassSetCount > ushort.MaxValue)
            throw new InvalidOperationException("ChainSubClassSetCount must fit in uint16.");

        byte[] coverageBytes = _coverage.ToArray();
        byte[] backtrackClassDefBytes = _backtrackClassDef.ToArray();
        byte[] inputClassDefBytes = _inputClassDef.ToArray();
        byte[] lookaheadClassDefBytes = _lookaheadClassDef.ToArray();

        var w = new OTFontFile2.OffsetWriter();
        var coverageLabel = w.CreateLabel();
        var backtrackClassDefLabel = w.CreateLabel();
        var inputClassDefLabel = w.CreateLabel();
        var lookaheadClassDefLabel = w.CreateLabel();

        w.WriteUInt16(2);
        w.WriteOffset16(coverageLabel, baseOffset: 0);
        w.WriteOffset16(backtrackClassDefLabel, baseOffset: 0);
        w.WriteOffset16(inputClassDefLabel, baseOffset: 0);
        w.WriteOffset16(lookaheadClassDefLabel, baseOffset: 0);
        w.WriteUInt16(checked((ushort)chainSubClassSetCount));

        if (chainSubClassSetCount != 0)
        {
            int maxRuleCount = 0;
            for (int i = 0; i < sets.Length; i++)
            {
                int c = sets[i].Rules.Count;
                if (c > maxRuleCount) maxRuleCount = c;
            }

            Span<OTFontFile2.OffsetWriter.Label> ruleLabelScratch = maxRuleCount <= 64
                ? stackalloc OTFontFile2.OffsetWriter.Label[maxRuleCount]
                : new OTFontFile2.OffsetWriter.Label[maxRuleCount];

            Span<OTFontFile2.OffsetWriter.Label> setLabels = chainSubClassSetCount <= 128
                ? stackalloc OTFontFile2.OffsetWriter.Label[chainSubClassSetCount]
                : new OTFontFile2.OffsetWriter.Label[chainSubClassSetCount];

            Span<byte> hasSet = chainSubClassSetCount <= 128
                ? stackalloc byte[chainSubClassSetCount]
                : new byte[chainSubClassSetCount];

            for (int i = 0; i < sets.Length; i++)
            {
                ushort cls = sets[i].StartClass;
                var label = w.CreateLabel();
                setLabels[cls] = label;
                hasSet[cls] = 1;
            }

            for (int cls = 0; cls < chainSubClassSetCount; cls++)
            {
                if (hasSet[cls] != 0)
                    w.WriteOffset16(setLabels[cls], baseOffset: 0);
                else
                    w.WriteUInt16(0);
            }

            w.Align2();
            w.DefineLabelHere(coverageLabel);
            w.WriteBytes(coverageBytes);

            w.Align2();
            w.DefineLabelHere(backtrackClassDefLabel);
            w.WriteBytes(backtrackClassDefBytes);

            w.Align2();
            w.DefineLabelHere(inputClassDefLabel);
            w.WriteBytes(inputClassDefBytes);

            w.Align2();
            w.DefineLabelHere(lookaheadClassDefLabel);
            w.WriteBytes(lookaheadClassDefBytes);

            for (int i = 0; i < sets.Length; i++)
            {
                w.Align2();
                w.DefineLabelHere(setLabels[sets[i].StartClass]);
                int setStart = w.Position;

                var rules = sets[i].Rules;
                if (rules.Count > ushort.MaxValue)
                    throw new InvalidOperationException("ChainSubClassRuleCount must fit in uint16.");

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

                    int backCount = rule.BacktrackClasses.Length;
                    if (backCount > ushort.MaxValue)
                        throw new InvalidOperationException("BacktrackGlyphCount must fit in uint16.");

                    int inputCount = checked(rule.InputClasses.Length + 1);
                    if (inputCount > ushort.MaxValue)
                        throw new InvalidOperationException("InputGlyphCount must fit in uint16.");

                    int lookCount = rule.LookaheadClasses.Length;
                    if (lookCount > ushort.MaxValue)
                        throw new InvalidOperationException("LookaheadGlyphCount must fit in uint16.");

                    if (rule.Records.Length > ushort.MaxValue)
                        throw new InvalidOperationException("SubstCount must fit in uint16.");

                    int substCount = rule.Records.Length;

                    w.WriteUInt16(checked((ushort)backCount));
                    for (int b = 0; b < backCount; b++)
                        w.WriteUInt16(rule.BacktrackClasses[b]);

                    w.WriteUInt16(checked((ushort)inputCount));
                    for (int c = 0; c < rule.InputClasses.Length; c++)
                        w.WriteUInt16(rule.InputClasses[c]);

                    w.WriteUInt16(checked((ushort)lookCount));
                    for (int l = 0; l < lookCount; l++)
                        w.WriteUInt16(rule.LookaheadClasses[l]);

                    w.WriteUInt16(checked((ushort)substCount));
                    for (int s = 0; s < substCount; s++)
                    {
                        var rec = rule.Records[s];
                        if (rec.SequenceIndex >= inputCount)
                            throw new InvalidOperationException("SequenceLookupRecord.SequenceIndex must be < InputGlyphCount.");

                        w.WriteUInt16(rec.SequenceIndex);
                        w.WriteUInt16(rec.LookupListIndex);
                    }
                }
            }

            return w.ToArray();
        }

        // No class sets: still emit empty coverage/classDefs.
        w.Align2();
        w.DefineLabelHere(coverageLabel);
        w.WriteBytes(coverageBytes);

        w.Align2();
        w.DefineLabelHere(backtrackClassDefLabel);
        w.WriteBytes(backtrackClassDefBytes);

        w.Align2();
        w.DefineLabelHere(inputClassDefLabel);
        w.WriteBytes(inputClassDefBytes);

        w.Align2();
        w.DefineLabelHere(lookaheadClassDefLabel);
        w.WriteBytes(lookaheadClassDefBytes);

        return w.ToArray();
    }

    private readonly struct ChainSubClassSet
    {
        public ushort StartClass { get; }
        public List<ChainSubClassRule> Rules { get; }

        public ChainSubClassSet(ushort startClass, List<ChainSubClassRule> rules)
        {
            StartClass = startClass;
            Rules = rules;
        }
    }

    private readonly struct ChainSubClassRule
    {
        public ushort[] BacktrackClasses { get; }
        public ushort[] InputClasses { get; }
        public ushort[] LookaheadClasses { get; }
        public SequenceLookupRecord[] Records { get; }

        public ChainSubClassRule(ushort[] backtrackClasses, ushort[] inputClasses, ushort[] lookaheadClasses, SequenceLookupRecord[] records)
        {
            BacktrackClasses = backtrackClasses;
            InputClasses = inputClasses;
            LookaheadClasses = lookaheadClasses;
            Records = records;
        }
    }
}
