using FontWeave.Core;

namespace FontWeave.Tables.Layout;

public readonly ref partial struct LayoutLangSysView
{
    public ReadStatus TryGetFeatureIndex(int ordinal, out ushort index, out FontDiagnostic diagnostic)
    {
        index = 0;
        ReadStatus status = TryGetFeatureCount(out ushort count, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = LayoutReadWire.Ordinal(ordinal, count, Location, LayoutLangSysLayout.CountOffset, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = TryReadFeatureIndexAt(LayoutLangSysLayout.HeaderLength +
            (ulong)ordinal * LayoutLangSysLayout.IndexStride, ordinal, out var row, out diagnostic);
        if (status == ReadStatus.Done) index = row.Index;
        return status;
    }

    internal ReadStatus Process(int featureCount, int position, LayoutReadState state,
        out LayoutLangSys? model, out FontDiagnostic diagnostic, BuildBudget budget)
    {
        model = null;
        ReadStatus status = TryGetFeatureCount(out ushort count, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = LayoutListsTableCompiler.CheckCount(featureCount, Location, LayoutLangSysLayout.CountOffset, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (!budget.Work(1, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        if (state.Languages is not null && state.Languages.TryGetValue(position, out model))
        { diagnostic = default; return ReadStatus.Done; }
        if (LookupOrderOffset != 0)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.AtLocal(DataField.Offset, 0), out diagnostic, LookupOrderOffset, 0);
        if (RequiredFeatureIndex != ushort.MaxValue)
        {
            status = LayoutReadWire.Index(RequiredFeatureIndex, featureCount, Location,
                LayoutLangSysLayout.RequiredOffset, -1, out diagnostic);
            if (status != ReadStatus.Done) return status;
        }
        int consumed = LayoutLangSysLayout.HeaderLength + LayoutLangSysLayout.IndexStride * count;
        if (!budget.Work(consumed, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        ushort[]? indices = null;
        if (state.Materialize)
        {
            if (!budget.Buffer(2L * count, Location, out diagnostic)) return ReadStatus.ResourceLimit;
            indices = new ushort[count];
        }
        for (int i = 0; i < count; i++)
        {
            status = TryGetFeatureIndex(i, out ushort index, out diagnostic);
            if (status != ReadStatus.Done) return status;
            status = LayoutReadWire.Index(index, featureCount, Location,
                LayoutLangSysLayout.HeaderLength + (ulong)i * LayoutLangSysLayout.IndexStride, i, out diagnostic);
            if (status != ReadStatus.Done) return status;
            if (indices is not null) indices[i] = index;
        }
        if (indices is not null)
        {
            model = new(RequiredFeatureIndex, indices, Location, consumed);
            status = LayoutReadState.Store(state.Languages, position, model, Location, budget, out diagnostic);
            if (status != ReadStatus.Done) { model = null; return status; }
        }
        diagnostic = default;
        return ReadStatus.Done;
    }
}

public readonly ref partial struct LayoutFeatureView
{
    public ReadStatus TryGetLookupIndex(int ordinal, out ushort index, out FontDiagnostic diagnostic)
    {
        index = 0;
        ReadStatus status = TryGetLookupCount(out ushort count, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = LayoutReadWire.Ordinal(ordinal, count, Location, LayoutFeatureLayout.CountOffset, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = TryReadLookupIndexAt(LayoutFeatureLayout.HeaderLength +
            (ulong)ordinal * LayoutFeatureLayout.IndexStride, ordinal, out var row, out diagnostic);
        if (status == ReadStatus.Done) index = row.Index;
        return status;
    }

    internal ReadStatus Process(uint tag, int lookupCount, int position, LayoutReadState state,
        DataLocation listLocation, DataLocation recordLocation, out LayoutFeature? model,
        out FontDiagnostic diagnostic, BuildBudget budget)
    {
        model = null;
        ReadStatus status = TryGetLookupCount(out ushort count, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = LayoutListsTableCompiler.CheckCount(lookupCount, Location, LayoutFeatureLayout.CountOffset, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (!budget.Work(1, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        if (state.Features is not null && state.Features.TryGetValue(position, out var cached))
        { model = cached.AtRecord(tag, listLocation, recordLocation); diagnostic = default; return ReadStatus.Done; }
        int consumed = LayoutFeatureLayout.HeaderLength + LayoutFeatureLayout.IndexStride * count;
        if (!budget.Work(consumed, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        ushort[]? indices = null;
        if (state.Materialize)
        {
            if (!budget.Buffer(2L * count, Location, out diagnostic)) return ReadStatus.ResourceLimit;
            indices = new ushort[count];
        }
        for (int i = 0; i < count; i++)
        {
            status = TryGetLookupIndex(i, out ushort index, out diagnostic);
            if (status != ReadStatus.Done) return status;
            status = LayoutReadWire.Index(index, lookupCount, Location,
                LayoutFeatureLayout.HeaderLength + (ulong)i * LayoutFeatureLayout.IndexStride, i, out diagnostic);
            if (status != ReadStatus.Done) return status;
            if (indices is not null) indices[i] = index;
        }
        // FeatureParams has no supported decoder here: even a huge raw offset is
        // an observation, not a traversed or range-validated payload.
        if (indices is not null)
        {
            model = new(tag, ParamsOffsetRaw, indices, Location, consumed, null, listLocation, recordLocation);
            status = LayoutReadState.Store(state.Features, position, model, Location, budget, out diagnostic);
            if (status != ReadStatus.Done) { model = null; return status; }
        }
        diagnostic = default;
        return ReadStatus.Done;
    }
}

public readonly ref partial struct LayoutScriptView
{
    public ReadStatus TryGetLanguageRecord(int ordinal, out LanguageRecordData record, out FontDiagnostic diagnostic)
    {
        record = default;
        ReadStatus status = TryGetLanguageCount(out ushort count, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = LayoutReadWire.Ordinal(ordinal, count, Location, LayoutScriptLayout.CountOffset, out diagnostic);
        return status == ReadStatus.Done ? TryReadLanguageRecordAt(LayoutScriptLayout.HeaderLength +
            (ulong)ordinal * LayoutTagListLayout.RecordStride, ordinal, out record, out diagnostic) : status;
    }

    internal ReadStatus Process(uint tag, int featureCount, int position, LayoutReadState state,
        DataLocation listLocation, DataLocation recordLocation, out LayoutScript? model,
        out FontDiagnostic diagnostic, BuildBudget budget)
    {
        model = null;
        ReadStatus status = TryGetLanguageCount(out ushort count, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (!budget.Work(1, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        if (state.Scripts is not null && state.Scripts.TryGetValue(position, out var cached))
        {
            if (tag == LayoutScriptLayout.DefaultTag && cached.DefaultLanguage is null)
                return MissingDefault(out diagnostic);
            model = cached.AtRecord(tag, listLocation, recordLocation);
            diagnostic = default; return ReadStatus.Done;
        }
        if (tag == LayoutScriptLayout.DefaultTag && DefaultLanguageOffset == 0)
            return MissingDefault(out diagnostic);
        int headerEnd = LayoutScriptLayout.HeaderLength + LayoutTagListLayout.RecordStride * count;
        if (!budget.Work(headerEnd, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        LayoutLangSys? defaultLanguage = null;
        int consumed = headerEnd;
        if (DefaultLanguageOffset != 0)
        {
            status = ReadLanguage(DefaultLanguageOffset, LayoutScriptLayout.DefaultOffsetField,
                headerEnd, featureCount, position, state, out defaultLanguage, out diagnostic, budget);
            if (status != ReadStatus.Done) return status;
            if (defaultLanguage is not null) consumed = Math.Max(consumed, DefaultLanguageOffset + defaultLanguage.ConsumedLength);
        }
        LayoutLanguage[]? languages = null;
        if (state.Materialize)
        {
            if (!budget.Buffer(16L * count, Location, out diagnostic)) return ReadStatus.ResourceLimit;
            languages = new LayoutLanguage[count];
        }
        uint previous = 0;
        for (int i = 0; i < count; i++)
        {
            status = TryGetLanguageRecord(i, out var row, out diagnostic);
            if (status != ReadStatus.Done) return status;
            int at = LayoutScriptLayout.HeaderLength + i * LayoutTagListLayout.RecordStride;
            if (i > 0 && row.Tag <= previous)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    Location.AtLocal(DataField.Tag, (ulong)at, i), out diagnostic, row.Tag, previous);
            previous = row.Tag;
            status = ReadLanguage(row.Offset, at + LayoutTagListLayout.RecordOffsetField,
                headerEnd, featureCount, position, state, out var language, out diagnostic, budget);
            if (status != ReadStatus.Done) return status;
            if (languages is not null)
            {
                languages[i] = new(row.Tag, language!);
                consumed = Math.Max(consumed, row.Offset + language!.ConsumedLength);
            }
        }
        if (languages is not null)
        {
            model = new(tag, defaultLanguage, languages, Location, consumed, null, listLocation, recordLocation);
            status = LayoutReadState.Store(state.Scripts, position, model, Location, budget, out diagnostic);
            if (status != ReadStatus.Done) { model = null; return status; }
        }
        diagnostic = default;
        return ReadStatus.Done;
    }

    private ReadStatus ReadLanguage(ushort offset, int field, int recordsEnd, int featureCount,
        int position, LayoutReadState state, out LayoutLangSys? model, out FontDiagnostic diagnostic, BuildBudget budget)
    {
        model = null;
        ReadStatus status = LayoutReadWire.Target(_data, offset, recordsEnd, field, Location, out diagnostic);
        if (status != ReadStatus.Done) return status;
        // offset is inside this suffix of the operation's root, so this sum is bounded by root length.
        int target = position + offset;
        status = LayoutLangSysView.TryOpen(_data.Slice(offset), out var child, out diagnostic, state.At(target));
        return status == ReadStatus.Done ? child.Process(featureCount, target, state,
            out model, out diagnostic, budget) : status;
    }

    private ReadStatus MissingDefault(out FontDiagnostic diagnostic) =>
        ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
            Location.AtLocal(DataField.Offset, LayoutScriptLayout.DefaultOffsetField), out diagnostic, 0, 1);
}
