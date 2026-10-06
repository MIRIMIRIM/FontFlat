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
        return TryRead(numGlyphs, kind, context, out model, out diagnostic, budget);
    }

    /// <summary>Typed validation of the supported substitution kinds without
    /// materializing any owned model (T4-03 section 3). The metadata-only
    /// <see cref="ValidateHeader"/> keeps its signature and behavior.</summary>
    public ReadStatus Validate(int numGlyphs, LayoutLookupKind kind, GdefLookupContext? context,
        out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        var budget = new BuildBudget(limits);
        if (!budget.Input(_data.Length, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        return Validate(numGlyphs, kind, context, out diagnostic, budget);
    }

    internal ReadStatus Validate(int numGlyphs, LayoutLookupKind kind, GdefLookupContext? context,
        out FontDiagnostic diagnostic, BuildBudget budget)
    {
        // The typed validation entry is GSUB-only by card boundary (GPOS stays the
        // metadata-only face until T4-05); the refusal carries the kind like the
        // header's unknown-kind row.
        if (kind != LayoutLookupKind.Gsub)
            return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
                Location.AtLocal(DataField.Tag, LookupLayout.TypeAt), out diagnostic, (ulong)kind);
        ReadStatus status = ValidateHeader(numGlyphs, kind, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        if (!LookupTable.IsTypedSupported(kind, LookupType))
            return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
                Location.AtLocal(DataField.Tag, LookupLayout.TypeAt), out diagnostic, LookupType);
        status = LookupTable.ValidateGsubSource(Location, out diagnostic);
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
        return ValidateTyped(numGlyphs, kind, budget, out diagnostic);
    }

    internal ReadStatus TryRead(int numGlyphs, LayoutLookupKind kind, GdefLookupContext? context,
        out LookupTable? model, out FontDiagnostic diagnostic, BuildBudget budget)
    {
        model = null;
        ReadStatus status = TryReadHeader(numGlyphs, kind, out var header, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        LookupTable parsed = header!;
        // The typed gate is kind-aware (T4-03 section 3.3): GSUB decodes {1,2,3,4,7},
        // GPOS stays {1}. Position (after the header, before the source/flags checks),
        // status/code/field, Detail=type and the default Expected are unchanged; the
        // only new reachable behavior is GSUB {2,3,4,7} passing this gate.
        if (!LookupTable.IsTypedSupported(kind, parsed.LookupType))
            return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
                Location.AtLocal(DataField.Tag, LookupLayout.TypeAt), out diagnostic, parsed.LookupType);
        status = kind == LayoutLookupKind.Gsub ? LookupTable.ValidateGsubSource(Location, out diagnostic)
            : LookupTable.ValidateGposSource(Location, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = parsed.ValidateFlags(context, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        if (kind == LayoutLookupKind.Gpos) return ReadPositions(parsed, out model, out diagnostic, budget);
        switch (parsed.LookupType)
        {
            case 2: return ReadRules(parsed, out model, out diagnostic, budget, 2);
            case 3: return ReadRules(parsed, out model, out diagnostic, budget, 3);
            case 4: return ReadRules(parsed, out model, out diagnostic, budget, 4);
            case 7: return ReadExtensions(parsed, out model, out diagnostic, budget);
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

    // Types 2/3/4 share the type-1 subtable walk shape exactly: an alias cache keyed by
    // the physical parent-local offset, one shared Coverage cache per Lookup span and a
    // Buffer(16) per unique subtable, with the leaf chains owning their semantics.
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

    // Type 7: the finite one-level unwrap. A wrapped type outside {1..4} refuses at
    // THIS lookup's type field with Detail=the lookup type - byte-identical to the
    // typed gate shape (W7's layered refusal); the whole-table reader recognizes
    // exactly this signature and degrades the lookup to metadata-only (OQ-T4-03-4).
    private ReadStatus ReadExtensions(LookupTable parsed, out LookupTable? model,
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
                if (wrappedType is not (1 or 2 or 3 or 4))
                    return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
                        Location.AtLocal(DataField.Tag, LookupLayout.TypeAt), out diagnostic, parsed.LookupType);
                status = child.TryRead(parsed.NumGlyphs, out subtable, out diagnostic, budget,
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
    // dispatch; admissions stay Work-shaped (no Buffer, no owned arrays).
    private ReadStatus ValidateTyped(int numGlyphs, LayoutLookupKind kind, BuildBudget budget,
        out FontDiagnostic diagnostic)
    {
        ReadStatus status = TryGetLookupType(out ushort lookupType, out diagnostic);
        if (status != ReadStatus.Done) return status;
        int count = SubtableCount;
        if (!budget.Work(count, Location, out diagnostic)) return ReadStatus.ResourceLimit;
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
            default:
                for (int i = 0; i < count; i++)
                {
                    status = TryOpenExtensionSubst(kind, i, out var extension, out _, out diagnostic);
                    if (status != ReadStatus.Done) return status;
                    status = extension.TryGetExtensionLookupType(out ushort wrappedType, out diagnostic);
                    if (status != ReadStatus.Done) return status;
                    if (wrappedType is not (1 or 2 or 3 or 4))
                        return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
                            Location.AtLocal(DataField.Tag, LookupLayout.TypeAt), out diagnostic, lookupType);
                    status = extension.Validate(numGlyphs, out diagnostic, budget);
                    if (status != ReadStatus.Done) return status;
                }
                break;
        }
        diagnostic = default;
        return ReadStatus.Done;
    }

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
