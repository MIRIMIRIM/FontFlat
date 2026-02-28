namespace OTFontFile2.Tables;

/// <summary>
/// Mutable builder for GPOS ContextPos subtables (lookup type 7), format 2 (class-based).
/// </summary>
public sealed class GposContextPosFormat2SubtableBuilder
{
    private readonly CoverageTableBuilder _coverage = new();
    private readonly ClassDefTableBuilder _classDef = new();
    private readonly List<PosClassSet> _sets = new();

    private bool _dirty = true;
    private byte[]? _built;

    public void Clear()
    {
        _coverage.Clear();
        _classDef.Clear();
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

    public void ClearClassDef()
    {
        _classDef.Clear();
        MarkDirty();
    }

    public void SetClass(ushort glyphId, ushort classValue)
    {
        _classDef.SetClass(glyphId, classValue);
        MarkDirty();
    }

    public void AddRule(
        ushort startClass,
        ReadOnlySpan<ushort> inputClasses,
        ReadOnlySpan<SequenceLookupRecord> posLookupRecords)
    {
        for (int i = 0; i < _sets.Count; i++)
        {
            if (_sets[i].StartClass != startClass)
                continue;

            _sets[i].Rules.Add(new PosClassRule(inputClasses.ToArray(), posLookupRecords.ToArray()));
            MarkDirty();
            return;
        }

        var rules = new List<PosClassRule>(capacity: 1) { new PosClassRule(inputClasses.ToArray(), posLookupRecords.ToArray()) };
        _sets.Add(new PosClassSet(startClass, rules));
        MarkDirty();
    }

    public bool RemovePosClassSet(ushort startClass)
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
        var sets = _sets.Count == 0 ? Array.Empty<PosClassSet>() : _sets.ToArray();
        if (sets.Length != 0)
            Array.Sort(sets, static (a, b) => a.StartClass.CompareTo(b.StartClass));

        int posClassSetCount = 0;
        if (sets.Length != 0)
        {
            ushort maxClass = sets[^1].StartClass;
            posClassSetCount = checked(maxClass + 1);
        }

        if (posClassSetCount > ushort.MaxValue)
            throw new InvalidOperationException("PosClassSetCount must fit in uint16.");

        byte[] coverageBytes = _coverage.ToArray();
        byte[] classDefBytes = _classDef.ToArray();

        var w = new OTFontFile2.OffsetWriter();
        var coverageLabel = w.CreateLabel();
        var classDefLabel = w.CreateLabel();

        w.WriteUInt16(2);
        w.WriteOffset16(coverageLabel, baseOffset: 0);
        w.WriteOffset16(classDefLabel, baseOffset: 0);
        w.WriteUInt16(checked((ushort)posClassSetCount));

        if (posClassSetCount != 0)
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

            Span<OTFontFile2.OffsetWriter.Label> setLabels = posClassSetCount <= 128
                ? stackalloc OTFontFile2.OffsetWriter.Label[posClassSetCount]
                : new OTFontFile2.OffsetWriter.Label[posClassSetCount];

            Span<byte> hasSet = posClassSetCount <= 128
                ? stackalloc byte[posClassSetCount]
                : new byte[posClassSetCount];

            for (int i = 0; i < sets.Length; i++)
            {
                ushort cls = sets[i].StartClass;
                var label = w.CreateLabel();
                setLabels[cls] = label;
                hasSet[cls] = 1;
            }

            for (int cls = 0; cls < posClassSetCount; cls++)
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
            w.DefineLabelHere(classDefLabel);
            w.WriteBytes(classDefBytes);

            for (int i = 0; i < sets.Length; i++)
            {
                w.Align2();
                w.DefineLabelHere(setLabels[sets[i].StartClass]);
                int setStart = w.Position;

                var rules = sets[i].Rules;
                if (rules.Count > ushort.MaxValue)
                    throw new InvalidOperationException("PosClassRuleCount must fit in uint16.");

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
                    int glyphCount = checked(rule.InputClasses.Length + 1);
                    if (glyphCount > ushort.MaxValue)
                        throw new InvalidOperationException("PosClassRule glyphCount must fit in uint16.");
                    if (rule.Records.Length > ushort.MaxValue)
                        throw new InvalidOperationException("PosClassRule posCount must fit in uint16.");

                    int posCount = rule.Records.Length;

                    w.WriteUInt16(checked((ushort)glyphCount));
                    w.WriteUInt16(checked((ushort)posCount));

                    for (int c = 0; c < rule.InputClasses.Length; c++)
                        w.WriteUInt16(rule.InputClasses[c]);

                    for (int p = 0; p < posCount; p++)
                    {
                        var rec = rule.Records[p];
                        if (rec.SequenceIndex >= glyphCount)
                            throw new InvalidOperationException("SequenceLookupRecord.SequenceIndex must be < glyphCount.");

                        w.WriteUInt16(rec.SequenceIndex);
                        w.WriteUInt16(rec.LookupListIndex);
                    }
                }
            }

            return w.ToArray();
        }

        w.Align2();
        w.DefineLabelHere(coverageLabel);
        w.WriteBytes(coverageBytes);

        w.Align2();
        w.DefineLabelHere(classDefLabel);
        w.WriteBytes(classDefBytes);

        return w.ToArray();
    }

    private readonly struct PosClassSet
    {
        public ushort StartClass { get; }
        public List<PosClassRule> Rules { get; }

        public PosClassSet(ushort startClass, List<PosClassRule> rules)
        {
            StartClass = startClass;
            Rules = rules;
        }
    }

    private readonly struct PosClassRule
    {
        public ushort[] InputClasses { get; }
        public SequenceLookupRecord[] Records { get; }

        public PosClassRule(ushort[] inputClasses, SequenceLookupRecord[] records)
        {
            InputClasses = inputClasses;
            Records = records;
        }
    }
}

