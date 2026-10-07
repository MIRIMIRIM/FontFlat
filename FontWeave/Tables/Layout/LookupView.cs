using FontWeave.Core;

namespace FontWeave.Tables.Layout;

public readonly ref partial struct LookupView
{
    /// <summary>Checks metadata and parent-relative references without interpreting any subtable type or GDEF context.</summary>
    public ReadStatus ValidateHeader(int numGlyphs, LayoutLookupKind kind,
        out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        var budget = new BuildBudget(limits);
        if (!budget.Input(_data.Length, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        return ValidateHeader(numGlyphs, kind, out diagnostic, budget);
    }

    internal ReadStatus ValidateHeader(int numGlyphs, LayoutLookupKind kind,
        out FontDiagnostic diagnostic, BuildBudget budget) =>
        ReadHeader(numGlyphs, kind, false, out _, out diagnostic, budget);

    /// <summary>Observes even unsupported lookup types. This model is not authority for opaque typed writing.</summary>
    public ReadStatus TryReadHeader(int numGlyphs, LayoutLookupKind kind, out LookupTable? model,
        out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        model = null;
        var budget = new BuildBudget(limits);
        if (!budget.Input(_data.Length, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        return TryReadHeader(numGlyphs, kind, out model, out diagnostic, budget);
    }

    internal ReadStatus TryReadHeader(int numGlyphs, LayoutLookupKind kind, out LookupTable? model,
        out FontDiagnostic diagnostic, BuildBudget budget) =>
        ReadHeader(numGlyphs, kind, true, out model, out diagnostic, budget);

    public ReadStatus TryRead(int numGlyphs, LayoutLookupKind kind, GdefLookupContext? context,
        out LookupTable? model, out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        model = null;
        var budget = new BuildBudget(limits);
        if (!budget.Input(_data.Length, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        return TryRead(numGlyphs, kind, context, -1, out model, out diagnostic, budget);
    }

    /// <summary>The lookup-count-taking typed read: lookupCount is the LookupList
    /// count in wire order and feeds the contextual lookup-index graph's
    /// LookupListIndex domain (OQ-4's explicit input); a negative value keeps only
    /// the owner-local SequenceIndex domain (the conditional-check first-class form).</summary>
    public ReadStatus TryRead(int numGlyphs, LayoutLookupKind kind, GdefLookupContext? context,
        int lookupCount, out LookupTable? model, out FontDiagnostic diagnostic,
        BuildLimits? limits = null)
    {
        model = null;
        var budget = new BuildBudget(limits);
        if (!budget.Input(_data.Length, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        return TryRead(numGlyphs, kind, context, lookupCount, out model, out diagnostic, budget);
    }

    /// <summary>Typed validation of the supported substitution kinds without
    /// materializing any owned model (T4-03 section 3, extended by T4-04's
    /// contextual families). The metadata-only <see cref="ValidateHeader"/> keeps
    /// its signature and behavior.</summary>
    public ReadStatus Validate(int numGlyphs, LayoutLookupKind kind, GdefLookupContext? context,
        out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        var budget = new BuildBudget(limits);
        if (!budget.Input(_data.Length, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        return Validate(numGlyphs, kind, context, -1, out diagnostic, budget);
    }

    /// <summary>The lookup-count-taking validation: lookupCount is the LookupList
    /// count in wire order and feeds the contextual lookup-index graph's
    /// LookupListIndex domain (OQ-4's explicit input); a negative value keeps only
    /// the owner-local SequenceIndex domain.</summary>
    public ReadStatus Validate(int numGlyphs, LayoutLookupKind kind, GdefLookupContext? context,
        int lookupCount, out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        var budget = new BuildBudget(limits);
        if (!budget.Input(_data.Length, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        return Validate(numGlyphs, kind, context, lookupCount, out diagnostic, budget);
    }

    internal ReadStatus Validate(int numGlyphs, LayoutLookupKind kind, GdefLookupContext? context,
        int lookupCount, out FontDiagnostic diagnostic, BuildBudget budget)
    {
        // The typed validation entry serves both layout kinds (T4-05 extended the
        // former GSUB-only face); a value outside the two-kind domain keeps the
        // header's unknown-kind refusal shape.
        if (kind is not (LayoutLookupKind.Gsub or LayoutLookupKind.Gpos))
            return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
                Location.AtLocal(DataField.Tag, LookupLayout.TypeAt), out diagnostic, (ulong)kind);
        ReadStatus status = ValidateHeader(numGlyphs, kind, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        if (!LookupTable.IsTypedSupported(kind, LookupType))
            return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
                Location.AtLocal(DataField.Tag, LookupLayout.TypeAt), out diagnostic, LookupType);
        status = kind == LayoutLookupKind.Gsub ? LookupTable.ValidateGsubSource(Location, out diagnostic)
            : LookupTable.ValidateGposSource(Location, out diagnostic);
        if (status != ReadStatus.Done) return status;
        // The filter word is read again here because ValidateHeader (metadata-only)
        // does not retain it; the shared static core keeps one flags contract body.
        ushort? filteringSet = null;
        if ((Flags & LookupLayout.UseMarkFilteringSet) != 0)
        {
            int offsetsEnd = LookupLayout.HeaderLength + LookupLayout.OffsetStride * SubtableCount;
            status = TryReadFilterWordAt((ulong)offsetsEnd, -1, out var record, out diagnostic);
            if (status != ReadStatus.Done) return status;
            filteringSet = record.MarkFilteringSet;
        }
        status = LookupTable.ValidateFlags(numGlyphs, Flags, filteringSet, SubtableCount,
            context, Location, out diagnostic);
        if (status != ReadStatus.Done) return status;
        return ValidateTyped(numGlyphs, kind, lookupCount, budget, out diagnostic);
    }

    internal ReadStatus TryRead(int numGlyphs, LayoutLookupKind kind, GdefLookupContext? context,
        int lookupCount, out LookupTable? model, out FontDiagnostic diagnostic, BuildBudget budget)
    {
        model = null;
        ReadStatus status = TryReadHeader(numGlyphs, kind, out var header, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        LookupTable parsed = header!;
        // The typed gate is kind-aware (T4-03 section 3.3, extended by T4-05): GSUB
        // decodes {1,2,3,4,7}, GPOS decodes {1,2,3}. Position (after the header, before
        // the source/flags checks), status/code/field, Detail=type and the default
        // Expected are unchanged; the newly reachable pass set is GPOS {2,3} joining
        // GPOS type 1 behind the same gate (W11 pins the 4-9 refusal shape).
        if (!LookupTable.IsTypedSupported(kind, parsed.LookupType))
            return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
                Location.AtLocal(DataField.Tag, LookupLayout.TypeAt), out diagnostic, parsed.LookupType);
        status = kind == LayoutLookupKind.Gsub ? LookupTable.ValidateGsubSource(Location, out diagnostic)
            : LookupTable.ValidateGposSource(Location, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = parsed.ValidateFlags(context, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        if (kind == LayoutLookupKind.Gpos)
            return parsed.LookupType switch
            {
                2 => ReadPairPositions(parsed, out model, out diagnostic, budget),
                3 => ReadCursiveRules(parsed, out model, out diagnostic, budget),
                4 => ReadMarkBases(parsed, out model, out diagnostic, budget),
                5 => ReadMarkLigs(parsed, out model, out diagnostic, budget),
                6 => ReadMarkMarks(parsed, out model, out diagnostic, budget),
                _ => ReadPositions(parsed, out model, out diagnostic, budget),
            };
        switch (parsed.LookupType)
        {
            case 2: return ReadRules(parsed, out model, out diagnostic, budget, 2);
            case 3: return ReadRules(parsed, out model, out diagnostic, budget, 3);
            case 4: return ReadRules(parsed, out model, out diagnostic, budget, 4);
            case 5: return ReadContextualRules(parsed, lookupCount, out model, out diagnostic, budget);
            case 6: return ReadChainingRules(parsed, lookupCount, out model, out diagnostic, budget);
            case 7: return ReadExtensions(parsed, lookupCount, out model, out diagnostic, budget);
            case 8: return ReadReverseChainRules(parsed, out model, out diagnostic, budget);
            default: break;
        }
        if (!budget.Work(parsed.SubtableCount, Location, out diagnostic) ||
            !budget.Buffer(8L * parsed.SubtableCount, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        SingleSubstTable[] subtables = parsed.SubtableCount == 0 ? [] : new SingleSubstTable[parsed.SubtableCount];
        var cache = new Dictionary<ushort, SingleSubstTable>();
        // One cache per Lookup span, glyph count, source face and supported subtable kind.
        // Keys are physical parent-local positions, never diagnostic coordinates.
        var coverageCache = new Dictionary<int, CoverageTable>();
        for (int i = 0; i < subtables.Length; i++)
        {
            ushort offset = parsed.SourceSubtableOffsets[i];
            if (!cache.TryGetValue(offset, out SingleSubstTable? subtable))
            {
                // The generated role/selector opener re-reads the offset field itself, so
                // the cache key and the child location stay byte-identical to the previous
                // hand-written Slice+TryOpen pair (no billing, no behavior difference).
                status = TryOpenSingleSubst(kind, i, out var child, out _, out diagnostic);
                if (status != ReadStatus.Done) return status;
                status = child.TryRead(numGlyphs, out subtable, out diagnostic, budget,
                    coverageCache, offset, Location);
                if (status != ReadStatus.Done) return status;
                if (!budget.Buffer(16, Location, out diagnostic)) return ReadStatus.ResourceLimit;
                cache.Add(offset, subtable!);
            }
            subtables[i] = subtable!;
        }
        model = parsed.WithDecodedSubtables(subtables);
        diagnostic = default;
        return ReadStatus.Done;
    }

    private ReadStatus ReadPositions(LookupTable header, out LookupTable? model,
        out FontDiagnostic diagnostic, BuildBudget budget)
    {
        model = null;
        int count = header.SubtableCount;
        if (!budget.Work(count, Location, out diagnostic) ||
            !budget.Buffer(8L * count, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        SinglePosTable[] subtables = new SinglePosTable[count];
        var children = new Dictionary<ushort, SinglePosTable>();
        var state = new SinglePosReadState(header.NumGlyphs, Location, _data.Length, true);
        for (int i = 0; i < count; i++)
        {
            ushort offset = header.SourceSubtableOffsets[i];
            if (!children.TryGetValue(offset, out var childModel))
            {
                ReadStatus status = TryOpenSinglePos(header.Kind, i, out var child,
                    out _, out diagnostic);
                if (status != ReadStatus.Done) return status;
                status = child.TryRead(header.NumGlyphs, out childModel, out diagnostic, budget, state, offset);
                if (status != ReadStatus.Done) return status;
                if (!budget.Buffer(16, Location, out diagnostic)) return ReadStatus.ResourceLimit;
                children.Add(offset, childModel!);
            }
            subtables[i] = childModel!;
        }
        model = header.WithDecodedPositions(subtables); diagnostic = default; return ReadStatus.Done;
    }

    // Types 2/3 GPOS walks keep the type-1 subtable walk shape exactly: an alias cache
    // keyed by the physical parent-local offset, one shared read state per Lookup span
    // (its facts caches dedupe Coverage/ClassDef/Device/Anchor bodies) and a Buffer(16)
    // per unique subtable, with the leaf chains owning their semantics.
    private ReadStatus ReadPairPositions(LookupTable header, out LookupTable? model,
        out FontDiagnostic diagnostic, BuildBudget budget)
    {
        model = null;
        int count = header.SubtableCount;
        if (!budget.Work(count, Location, out diagnostic) ||
            !budget.Buffer(8L * count, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        PairPosTable[] subtables = new PairPosTable[count];
        var children = new Dictionary<ushort, PairPosTable>();
        var state = new PairPosReadState(header.NumGlyphs, Location, _data.Length, true);
        for (int i = 0; i < count; i++)
        {
            ushort offset = header.SourceSubtableOffsets[i];
            if (!children.TryGetValue(offset, out var childModel))
            {
                ReadStatus status = TryOpenPairPos(header.Kind, i, out var child, out _, out diagnostic);
                if (status != ReadStatus.Done) return status;
                status = child.TryRead(header.NumGlyphs, out childModel, out diagnostic, budget, state, offset);
                if (status != ReadStatus.Done) return status;
                if (!budget.Buffer(16, Location, out diagnostic)) return ReadStatus.ResourceLimit;
                children.Add(offset, childModel!);
            }
            subtables[i] = childModel!;
        }
        model = header.WithDecodedPairPositions(subtables); diagnostic = default; return ReadStatus.Done;
    }

    private ReadStatus ReadCursiveRules(LookupTable header, out LookupTable? model,
        out FontDiagnostic diagnostic, BuildBudget budget)
    {
        model = null;
        int count = header.SubtableCount;
        if (!budget.Work(count, Location, out diagnostic) ||
            !budget.Buffer(8L * count, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        CursivePosTable[] subtables = new CursivePosTable[count];
        var children = new Dictionary<ushort, CursivePosTable>();
        var state = new CursivePosReadState(header.NumGlyphs, Location, _data.Length, true);
        for (int i = 0; i < count; i++)
        {
            ushort offset = header.SourceSubtableOffsets[i];
            if (!children.TryGetValue(offset, out var childModel))
            {
                ReadStatus status = TryOpenCursivePos(header.Kind, i, out var child, out _, out diagnostic);
                if (status != ReadStatus.Done) return status;
                status = child.TryRead(header.NumGlyphs, out childModel, out diagnostic, budget, state, offset);
                if (status != ReadStatus.Done) return status;
                if (!budget.Buffer(16, Location, out diagnostic)) return ReadStatus.ResourceLimit;
                children.Add(offset, childModel!);
            }
            subtables[i] = childModel!;
        }
        model = header.WithDecodedCursive(subtables); diagnostic = default; return ReadStatus.Done;
    }

    // The T4-06 mark-family walks keep the cursive walk's shape exactly: an alias
    // cache keyed by the physical parent-local offset, one shared per-Lookup read
    // state per Lookup span (its facts caches dedupe Coverage/Anchor/Device bodies
    // across the whole mark side) and a Buffer(16) per unique subtable.
    private ReadStatus ReadMarkBases(LookupTable header, out LookupTable? model,
        out FontDiagnostic diagnostic, BuildBudget budget)
    {
        model = null;
        int count = header.SubtableCount;
        if (!budget.Work(count, Location, out diagnostic) ||
            !budget.Buffer(8L * count, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        MarkBasePosTable[] subtables = new MarkBasePosTable[count];
        var children = new Dictionary<ushort, MarkBasePosTable>();
        var state = new CursivePosReadState(header.NumGlyphs, Location, _data.Length, true);
        for (int i = 0; i < count; i++)
        {
            ushort offset = header.SourceSubtableOffsets[i];
            if (!children.TryGetValue(offset, out var childModel))
            {
                ReadStatus status = TryOpenMarkBasePos(header.Kind, i, out var child,
                    out _, out diagnostic);
                if (status != ReadStatus.Done) return status;
                status = child.TryRead(header.NumGlyphs, out childModel, out diagnostic, budget,
                    state, offset);
                if (status != ReadStatus.Done) return status;
                if (!budget.Buffer(16, Location, out diagnostic)) return ReadStatus.ResourceLimit;
                children.Add(offset, childModel!);
            }
            subtables[i] = childModel!;
        }
        model = header.WithDecodedMarkBases(subtables); diagnostic = default; return ReadStatus.Done;
    }

    private ReadStatus ReadMarkLigs(LookupTable header, out LookupTable? model,
        out FontDiagnostic diagnostic, BuildBudget budget)
    {
        model = null;
        int count = header.SubtableCount;
        if (!budget.Work(count, Location, out diagnostic) ||
            !budget.Buffer(8L * count, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        MarkLigPosTable[] subtables = new MarkLigPosTable[count];
        var children = new Dictionary<ushort, MarkLigPosTable>();
        var state = new CursivePosReadState(header.NumGlyphs, Location, _data.Length, true);
        for (int i = 0; i < count; i++)
        {
            ushort offset = header.SourceSubtableOffsets[i];
            if (!children.TryGetValue(offset, out var childModel))
            {
                ReadStatus status = TryOpenMarkLigPos(header.Kind, i, out var child,
                    out _, out diagnostic);
                if (status != ReadStatus.Done) return status;
                status = child.TryRead(header.NumGlyphs, out childModel, out diagnostic, budget,
                    state, offset);
                if (status != ReadStatus.Done) return status;
                if (!budget.Buffer(16, Location, out diagnostic)) return ReadStatus.ResourceLimit;
                children.Add(offset, childModel!);
            }
            subtables[i] = childModel!;
        }
        model = header.WithDecodedMarkLigs(subtables); diagnostic = default; return ReadStatus.Done;
    }

    private ReadStatus ReadMarkMarks(LookupTable header, out LookupTable? model,
        out FontDiagnostic diagnostic, BuildBudget budget)
    {
        model = null;
        int count = header.SubtableCount;
        if (!budget.Work(count, Location, out diagnostic) ||
            !budget.Buffer(8L * count, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        MarkMarkPosTable[] subtables = new MarkMarkPosTable[count];
        var children = new Dictionary<ushort, MarkMarkPosTable>();
        var state = new CursivePosReadState(header.NumGlyphs, Location, _data.Length, true);
        for (int i = 0; i < count; i++)
        {
            ushort offset = header.SourceSubtableOffsets[i];
            if (!children.TryGetValue(offset, out var childModel))
            {
                ReadStatus status = TryOpenMarkMarkPos(header.Kind, i, out var child,
                    out _, out diagnostic);
                if (status != ReadStatus.Done) return status;
                status = child.TryRead(header.NumGlyphs, out childModel, out diagnostic, budget,
                    state, offset);
                if (status != ReadStatus.Done) return status;
                if (!budget.Buffer(16, Location, out diagnostic)) return ReadStatus.ResourceLimit;
                children.Add(offset, childModel!);
            }
            subtables[i] = childModel!;
        }
        model = header.WithDecodedMarkMarks(subtables); diagnostic = default; return ReadStatus.Done;
    }

    private ReadStatus ReadRules(LookupTable parsed, out LookupTable? model,
        out FontDiagnostic diagnostic, BuildBudget budget, ushort lookupType)
    {
        model = null;
        if (!budget.Work(parsed.SubtableCount, Location, out diagnostic) ||
            !budget.Buffer(8L * parsed.SubtableCount, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        var coverageCache = new Dictionary<int, CoverageTable>();
        ReadStatus status;
        switch (lookupType)
        {
            case 2:
            {
                MultipleSubstTable[] subtables = parsed.SubtableCount == 0
                    ? [] : new MultipleSubstTable[parsed.SubtableCount];
                var cache = new Dictionary<ushort, MultipleSubstTable>();
                for (int i = 0; i < subtables.Length; i++)
                {
                    ushort offset = parsed.SourceSubtableOffsets[i];
                    if (!cache.TryGetValue(offset, out MultipleSubstTable? subtable))
                    {
                        status = TryOpenMultipleSubst(LayoutLookupKind.Gsub, i, out var child, out _, out diagnostic);
                        if (status != ReadStatus.Done) return status;
                        status = child.TryRead(parsed.NumGlyphs, out subtable, out diagnostic, budget,
                            coverageCache, offset, Location);
                        if (status != ReadStatus.Done) return status;
                        if (!budget.Buffer(16, Location, out diagnostic)) return ReadStatus.ResourceLimit;
                        cache.Add(offset, subtable!);
                    }
                    subtables[i] = subtable!;
                }
                model = parsed.WithDecodedMultiples(subtables);
                break;
            }
            case 3:
            {
                AlternateSubstTable[] subtables = parsed.SubtableCount == 0
                    ? [] : new AlternateSubstTable[parsed.SubtableCount];
                var cache = new Dictionary<ushort, AlternateSubstTable>();
                for (int i = 0; i < subtables.Length; i++)
                {
                    ushort offset = parsed.SourceSubtableOffsets[i];
                    if (!cache.TryGetValue(offset, out AlternateSubstTable? subtable))
                    {
                        status = TryOpenAlternateSubst(LayoutLookupKind.Gsub, i, out var child, out _, out diagnostic);
                        if (status != ReadStatus.Done) return status;
                        status = child.TryRead(parsed.NumGlyphs, out subtable, out diagnostic, budget,
                            coverageCache, offset, Location);
                        if (status != ReadStatus.Done) return status;
                        if (!budget.Buffer(16, Location, out diagnostic)) return ReadStatus.ResourceLimit;
                        cache.Add(offset, subtable!);
                    }
                    subtables[i] = subtable!;
                }
                model = parsed.WithDecodedAlternates(subtables);
                break;
            }
            default:
            {
                LigatureSubstTable[] subtables = parsed.SubtableCount == 0
                    ? [] : new LigatureSubstTable[parsed.SubtableCount];
                var cache = new Dictionary<ushort, LigatureSubstTable>();
                for (int i = 0; i < subtables.Length; i++)
                {
                    ushort offset = parsed.SourceSubtableOffsets[i];
                    if (!cache.TryGetValue(offset, out LigatureSubstTable? subtable))
                    {
                        status = TryOpenLigatureSubst(LayoutLookupKind.Gsub, i, out var child, out _, out diagnostic);
                        if (status != ReadStatus.Done) return status;
                        status = child.TryRead(parsed.NumGlyphs, out subtable, out diagnostic, budget,
                            coverageCache, offset, Location);
                        if (status != ReadStatus.Done) return status;
                        if (!budget.Buffer(16, Location, out diagnostic)) return ReadStatus.ResourceLimit;
                        cache.Add(offset, subtable!);
                    }
                    subtables[i] = subtable!;
                }
                model = parsed.WithDecodedLigatures(subtables);
                break;
            }
        }
        diagnostic = default;
        return ReadStatus.Done;
    }

    // The T4-04 contextual walks share the direct-leaf walk shape exactly: an alias
    // cache keyed by the physical parent-local offset, one shared Coverage cache per
    // Lookup span and a Buffer(16) per unique subtable, with the leaf chains owning
    // their semantics and the graph domains.
    private ReadStatus ReadContextualRules(LookupTable parsed, int lookupCount,
        out LookupTable? model, out FontDiagnostic diagnostic, BuildBudget budget)
    {
        model = null;
        if (!budget.Work(parsed.SubtableCount, Location, out diagnostic) ||
            !budget.Buffer(8L * parsed.SubtableCount, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        ContextualSubstTable[] subtables = parsed.SubtableCount == 0
            ? [] : new ContextualSubstTable[parsed.SubtableCount];
        var cache = new Dictionary<ushort, ContextualSubstTable>();
        var coverageCache = new Dictionary<int, CoverageTable>();
        for (int i = 0; i < subtables.Length; i++)
        {
            ushort offset = parsed.SourceSubtableOffsets[i];
            if (!cache.TryGetValue(offset, out ContextualSubstTable? subtable))
            {
                ReadStatus status = TryOpenContextualSubst(LayoutLookupKind.Gsub, i, out var child,
                    out _, out diagnostic);
                if (status != ReadStatus.Done) return status;
                status = child.TryRead(parsed.NumGlyphs, lookupCount, out subtable, out diagnostic, budget,
                    coverageCache, offset, Location);
                if (status != ReadStatus.Done) return status;
                if (!budget.Buffer(16, Location, out diagnostic)) return ReadStatus.ResourceLimit;
                cache.Add(offset, subtable!);
            }
            subtables[i] = subtable!;
        }
        model = parsed.WithDecodedContextuals(subtables);
        diagnostic = default;
        return ReadStatus.Done;
    }

    private ReadStatus ReadChainingRules(LookupTable parsed, int lookupCount,
        out LookupTable? model, out FontDiagnostic diagnostic, BuildBudget budget)
    {
        model = null;
        if (!budget.Work(parsed.SubtableCount, Location, out diagnostic) ||
            !budget.Buffer(8L * parsed.SubtableCount, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        ChainingSubstTable[] subtables = parsed.SubtableCount == 0
            ? [] : new ChainingSubstTable[parsed.SubtableCount];
        var cache = new Dictionary<ushort, ChainingSubstTable>();
        var coverageCache = new Dictionary<int, CoverageTable>();
        for (int i = 0; i < subtables.Length; i++)
        {
            ushort offset = parsed.SourceSubtableOffsets[i];
            if (!cache.TryGetValue(offset, out ChainingSubstTable? subtable))
            {
                ReadStatus status = TryOpenChainingSubst(LayoutLookupKind.Gsub, i, out var child,
                    out _, out diagnostic);
                if (status != ReadStatus.Done) return status;
                status = child.TryRead(parsed.NumGlyphs, lookupCount, out subtable, out diagnostic, budget,
                    coverageCache, offset, Location);
                if (status != ReadStatus.Done) return status;
                if (!budget.Buffer(16, Location, out diagnostic)) return ReadStatus.ResourceLimit;
                cache.Add(offset, subtable!);
            }
            subtables[i] = subtable!;
        }
        model = parsed.WithDecodedChainings(subtables);
        diagnostic = default;
        return ReadStatus.Done;
    }

    private ReadStatus ReadReverseChainRules(LookupTable parsed, out LookupTable? model,
        out FontDiagnostic diagnostic, BuildBudget budget)
    {
        model = null;
        if (!budget.Work(parsed.SubtableCount, Location, out diagnostic) ||
            !budget.Buffer(8L * parsed.SubtableCount, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        ReverseChainSubstTable[] subtables = parsed.SubtableCount == 0
            ? [] : new ReverseChainSubstTable[parsed.SubtableCount];
        var cache = new Dictionary<ushort, ReverseChainSubstTable>();
        var coverageCache = new Dictionary<int, CoverageTable>();
        for (int i = 0; i < subtables.Length; i++)
        {
            ushort offset = parsed.SourceSubtableOffsets[i];
            if (!cache.TryGetValue(offset, out ReverseChainSubstTable? subtable))
            {
                ReadStatus status = TryOpenReverseChainSubst(LayoutLookupKind.Gsub, i, out var child,
                    out _, out diagnostic);
                if (status != ReadStatus.Done) return status;
                status = child.TryRead(parsed.NumGlyphs, out subtable, out diagnostic, budget,
                    coverageCache, offset, Location);
                if (status != ReadStatus.Done) return status;
                if (!budget.Buffer(16, Location, out diagnostic)) return ReadStatus.ResourceLimit;
                cache.Add(offset, subtable!);
            }
            subtables[i] = subtable!;
        }
        model = parsed.WithDecodedReverseChains(subtables);
        diagnostic = default;
        return ReadStatus.Done;
    }

    // Type 7: the finite one-level unwrap.
    // (T4-04 extended the set; spec forbids only wrapped=7) refuses at THIS lookup's
    // type field with Detail=the lookup type - byte-identical to the typed gate shape
    // (W7's layered refusal); the whole-table reader recognizes exactly this
    // signature and degrades the lookup to metadata-only (OQ-T4-03-4).
    private ReadStatus ReadExtensions(LookupTable parsed, int lookupCount, out LookupTable? model,
        out FontDiagnostic diagnostic, BuildBudget budget)
    {
        model = null;
        if (!budget.Work(parsed.SubtableCount, Location, out diagnostic) ||
            !budget.Buffer(8L * parsed.SubtableCount, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        ExtensionSubstTable[] subtables = parsed.SubtableCount == 0
            ? [] : new ExtensionSubstTable[parsed.SubtableCount];
        var cache = new Dictionary<ushort, ExtensionSubstTable>();
        var coverageCache = new Dictionary<int, CoverageTable>();
        for (int i = 0; i < subtables.Length; i++)
        {
            ushort offset = parsed.SourceSubtableOffsets[i];
            if (!cache.TryGetValue(offset, out ExtensionSubstTable? subtable))
            {
                ReadStatus status = TryOpenExtensionSubst(LayoutLookupKind.Gsub, i, out var child,
                    out _, out diagnostic);
                if (status != ReadStatus.Done) return status;
                status = child.TryGetExtensionLookupType(out ushort wrappedType, out diagnostic);
                if (status != ReadStatus.Done) return status;
                if (wrappedType is not (1 or 2 or 3 or 4 or 5 or 6 or 8))
                    return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
                        Location.AtLocal(DataField.Tag, LookupLayout.TypeAt), out diagnostic, parsed.LookupType);
                status = child.TryRead(parsed.NumGlyphs, lookupCount, out subtable, out diagnostic, budget,
                    coverageCache, offset, Location);
                if (status != ReadStatus.Done) return status;
                if (!budget.Buffer(16, Location, out diagnostic)) return ReadStatus.ResourceLimit;
                cache.Add(offset, subtable!);
            }
            subtables[i] = subtable!;
        }
        model = parsed.WithDecodedExtensions(subtables);
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>The exact signature of the kind-aware typed gate and the W7 layered
    /// refusal; the whole-table reader degrades a lookup to metadata-only on this and
    /// only this outcome (every other failure is a real whole-table failure).</summary>
    internal static bool IsTypedRefusal(LayoutLookupKind kind, ushort lookupType, ReadStatus status,
        FontDiagnostic diagnostic) =>
        status == ReadStatus.UnsupportedFormat && diagnostic.Code == DiagnosticCode.UnsupportedOperation &&
        diagnostic.Location.Field == DataField.Tag && diagnostic.Detail == lookupType &&
        diagnostic.Expected == 0 && LookupTable.IsTypedSupported(kind, lookupType);

    // Typed validation without materialization, sharing the read path's per-type
    // dispatch; admissions stay Work-shaped (no Buffer, no owned arrays). The GPOS
    // branch (T4-05) only sees types 1/2/3 - the kind-aware gate refused the rest.
    private ReadStatus ValidateTyped(int numGlyphs, LayoutLookupKind kind, int lookupCount,
        BuildBudget budget, out FontDiagnostic diagnostic)
    {
        ReadStatus status = TryGetLookupType(out ushort lookupType, out diagnostic);
        if (status != ReadStatus.Done) return status;
        int count = SubtableCount;
        if (!budget.Work(count, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        if (kind == LayoutLookupKind.Gpos)
        {
            for (int i = 0; i < count; i++)
            {
                status = lookupType switch
                {
                    2 => OpenAndValidatePairPos(i, numGlyphs, budget, out diagnostic),
                    3 => OpenAndValidateCursivePos(i, numGlyphs, budget, out diagnostic),
                    4 => OpenAndValidateMarkBase(i, numGlyphs, budget, out diagnostic),
                    5 => OpenAndValidateMarkLig(i, numGlyphs, budget, out diagnostic),
                    6 => OpenAndValidateMarkMark(i, numGlyphs, budget, out diagnostic),
                    _ => OpenAndValidateSinglePos(i, kind, numGlyphs, budget, out diagnostic),
                };
                if (status != ReadStatus.Done) return status;
            }
            diagnostic = default;
            return ReadStatus.Done;
        }
        switch (lookupType)
        {
            case 1:
                for (int i = 0; i < count; i++)
                {
                    status = TryOpenSingleSubst(kind, i, out var single, out _, out diagnostic);
                    if (status != ReadStatus.Done) return status;
                    status = single.Validate(numGlyphs, out diagnostic, budget);
                    if (status != ReadStatus.Done) return status;
                }
                break;
            case 2:
                for (int i = 0; i < count; i++)
                {
                    status = TryOpenMultipleSubst(kind, i, out var multiple, out _, out diagnostic);
                    if (status != ReadStatus.Done) return status;
                    status = multiple.Validate(numGlyphs, out diagnostic, budget);
                    if (status != ReadStatus.Done) return status;
                }
                break;
            case 3:
                for (int i = 0; i < count; i++)
                {
                    status = TryOpenAlternateSubst(kind, i, out var alternate, out _, out diagnostic);
                    if (status != ReadStatus.Done) return status;
                    status = alternate.Validate(numGlyphs, out diagnostic, budget);
                    if (status != ReadStatus.Done) return status;
                }
                break;
            case 4:
                for (int i = 0; i < count; i++)
                {
                    status = TryOpenLigatureSubst(kind, i, out var ligature, out _, out diagnostic);
                    if (status != ReadStatus.Done) return status;
                    status = ligature.Validate(numGlyphs, out diagnostic, budget);
                    if (status != ReadStatus.Done) return status;
                }
                break;
            case 5:
                for (int i = 0; i < count; i++)
                {
                    status = TryOpenContextualSubst(kind, i, out var contextual, out _, out diagnostic);
                    if (status != ReadStatus.Done) return status;
                    status = contextual.Validate(numGlyphs, lookupCount, out diagnostic, budget);
                    if (status != ReadStatus.Done) return status;
                }
                break;
            case 6:
                for (int i = 0; i < count; i++)
                {
                    status = TryOpenChainingSubst(kind, i, out var chaining, out _, out diagnostic);
                    if (status != ReadStatus.Done) return status;
                    status = chaining.Validate(numGlyphs, lookupCount, out diagnostic, budget);
                    if (status != ReadStatus.Done) return status;
                }
                break;
            case 8:
                for (int i = 0; i < count; i++)
                {
                    status = TryOpenReverseChainSubst(kind, i, out var reverse, out _, out diagnostic);
                    if (status != ReadStatus.Done) return status;
                    status = reverse.Validate(numGlyphs, out diagnostic, budget);
                    if (status != ReadStatus.Done) return status;
                }
                break;
            default:
                for (int i = 0; i < count; i++)
                {
                    status = TryOpenExtensionSubst(kind, i, out var extension, out _, out diagnostic);
                    if (status != ReadStatus.Done) return status;
                    status = extension.TryGetExtensionLookupType(out ushort wrappedType, out diagnostic);
                    if (status != ReadStatus.Done) return status;
                    if (wrappedType is not (1 or 2 or 3 or 4 or 5 or 6 or 8))
                        return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
                            Location.AtLocal(DataField.Tag, LookupLayout.TypeAt), out diagnostic, lookupType);
                    status = extension.Validate(numGlyphs, lookupCount, out diagnostic, budget);
                    if (status != ReadStatus.Done) return status;
                }
                break;
        }
        diagnostic = default;
        return ReadStatus.Done;
    }

    private ReadStatus OpenAndValidateSinglePos(int ordinal, LayoutLookupKind kind, int numGlyphs,
        BuildBudget budget, out FontDiagnostic diagnostic)
    {
        ReadStatus status = TryOpenSinglePos(kind, ordinal, out var single, out _, out diagnostic);
        return status == ReadStatus.Done ? single.Validate(numGlyphs, out diagnostic, budget) : status;
    }

    private ReadStatus OpenAndValidatePairPos(int ordinal, int numGlyphs,
        BuildBudget budget, out FontDiagnostic diagnostic)
    {
        ReadStatus status = TryOpenPairPos(LayoutLookupKind.Gpos, ordinal, out var pair, out _, out diagnostic);
        return status == ReadStatus.Done ? pair.Validate(numGlyphs, out diagnostic, budget) : status;
    }

    private ReadStatus OpenAndValidateCursivePos(int ordinal, int numGlyphs,
        BuildBudget budget, out FontDiagnostic diagnostic)
    {
        ReadStatus status = TryOpenCursivePos(LayoutLookupKind.Gpos, ordinal, out var cursive, out _, out diagnostic);
        return status == ReadStatus.Done ? cursive.Validate(numGlyphs, out diagnostic, budget) : status;
    }

    // The T4-06 mark family's validate-only admissions: same shape as the cursive
    // one - the leaf chains own every semantic check, the walk only opens.
    private ReadStatus OpenAndValidateMarkBase(int ordinal, int numGlyphs,
        BuildBudget budget, out FontDiagnostic diagnostic)
    {
        ReadStatus status = TryOpenMarkBasePos(LayoutLookupKind.Gpos, ordinal, out var markBase, out _, out diagnostic);
        return status == ReadStatus.Done ? markBase.Validate(numGlyphs, out diagnostic, budget) : status;
    }

    private ReadStatus OpenAndValidateMarkLig(int ordinal, int numGlyphs,
        BuildBudget budget, out FontDiagnostic diagnostic)
    {
        ReadStatus status = TryOpenMarkLigPos(LayoutLookupKind.Gpos, ordinal, out var markLig, out _, out diagnostic);
        return status == ReadStatus.Done ? markLig.Validate(numGlyphs, out diagnostic, budget) : status;
    }

    private ReadStatus OpenAndValidateMarkMark(int ordinal, int numGlyphs,
        BuildBudget budget, out FontDiagnostic diagnostic)
    {
        ReadStatus status = TryOpenMarkMarkPos(LayoutLookupKind.Gpos, ordinal, out var markMark, out _, out diagnostic);
        return status == ReadStatus.Done ? markMark.Validate(numGlyphs, out diagnostic, budget) : status;
    }

    // The T4-06 contextual/extension admissions: the GPOS branches open the reused
    // GSUB-common contextual views and the GPOS-family extension view; the wrapped
    // leaf's own semantic face owns every check.
    private ReadStatus ReadHeader(int numGlyphs, LayoutLookupKind kind, bool materialize,
        out LookupTable? model, out FontDiagnostic diagnostic, BuildBudget budget)
    {
        model = null;
        ReadStatus status = TryGetLookupType(out ushort type, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = CoverageTableCompiler.CheckNumGlyphs(numGlyphs, Location, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (kind is not (LayoutLookupKind.Gsub or LayoutLookupKind.Gpos))
            return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
                Location.AtLocal(DataField.Tag, LookupLayout.TypeAt), out diagnostic, (ulong)kind);
        if ((Flags & LookupLayout.ReservedMask) != 0)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.AtLocal(DataField.Flags, LookupLayout.FlagsAt), out diagnostic, Flags, 0xFF1F);
        int offsetsEnd = LookupLayout.HeaderLength + LookupLayout.OffsetStride * SubtableCount;
        bool useSet = (Flags & LookupLayout.UseMarkFilteringSet) != 0;
        // The declared computed replaces the hand-written prefix arithmetic; the value fed
        // to Work is identical (6 + 2*count + useSet), so the billing stays byte-identical.
        status = TryGetSubtablePrefix(out ulong prefixValue, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (!budget.Work((long)prefixValue, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        int prefix = (int)prefixValue;
        if (_data.Length < offsetsEnd)
        {
            int firstIncomplete = (_data.Length - LookupLayout.HeaderLength) / LookupLayout.OffsetStride;
            status = TryGetSubtableOffsetRecord(firstIncomplete, out _, out diagnostic);
            // The generated counted-array guard locates the incomplete whole row.
            // This semantic row consists solely of its parent-relative offset.
            if (status == ReadStatus.Truncated)
                diagnostic = diagnostic with { Location = diagnostic.Location with { Field = DataField.Offset } };
            return status;
        }
        ushort? filteringSet = null;
        if (useSet)
        {
            status = TryReadFilterWordAt((ulong)offsetsEnd, -1, out var record, out diagnostic);
            if (status != ReadStatus.Done) return status;
            filteringSet = record.MarkFilteringSet;
        }
        if (materialize && !budget.Buffer(2L * SubtableCount, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        ushort[] offsets = materialize && SubtableCount != 0 ? new ushort[SubtableCount] : [];
        for (int i = 0; i < SubtableCount; i++)
        {
            status = TryGetSubtableOffsetRecord(i, out var record, out diagnostic);
            if (status != ReadStatus.Done) return status;
            ushort offset = record.SubtableOffset;
            ulong field = LookupLayout.HeaderLength + (ulong)i * LookupLayout.OffsetStride;
            if (offset == 0 || offset >= _data.Length)
                return ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.RangeOutsideSource,
                    Location.AtLocal(DataField.Offset, field, i), out diagnostic, offset, (ulong)_data.Length);
            if (offset < prefix)
                return ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.StorageBeforeRecords,
                    Location.AtLocal(DataField.Offset, field, i), out diagnostic, offset, (ulong)prefix);
            if (materialize) offsets[i] = offset;
        }
        if (materialize) model = new(kind, numGlyphs, type, Flags, filteringSet, offsets, null, Location);
        diagnostic = default;
        return ReadStatus.Done;
    }
}
