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

    internal ReadStatus TryRead(int numGlyphs, LayoutLookupKind kind, GdefLookupContext? context,
        out LookupTable? model, out FontDiagnostic diagnostic, BuildBudget budget)
    {
        model = null;
        ReadStatus status = TryReadHeader(numGlyphs, kind, out var header, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        LookupTable parsed = header!;
        if (kind != LayoutLookupKind.Gsub || parsed.LookupType != 1)
            return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
                Location.AtLocal(DataField.Tag, LookupLayout.TypeAt), out diagnostic, parsed.LookupType);
        status = LookupTable.ValidateGsubSource(Location, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = parsed.ValidateFlags(context, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
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
                status = SingleSubstView.TryOpen(_data.Slice(offset), out var child, out diagnostic,
                    Location.AtLocal(DataField.None, offset));
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
        int prefix = offsetsEnd + (useSet ? 2 : 0);
        if (!budget.Work(prefix, Location, out diagnostic)) return ReadStatus.ResourceLimit;
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
