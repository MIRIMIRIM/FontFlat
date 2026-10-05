using FontWeave.Core;

namespace FontWeave.Tables.Layout;

public readonly ref partial struct LayoutLangSysView
{
    // The generated contract version 1 array accessors (TryGetFeatureIndex record form,
    // TryGetFeatureIndexExtent and TryRequireFeatureIndexExtent) own the ordinal, range
    // and truncation rules; the ushort getter below is the compatibility projection over
    // the record accessor, and this partial keeps only the semantic checks and traversal.

    public ReadStatus TryGetFeatureIndex(int ordinal, out ushort index, out FontDiagnostic diagnostic)
    {
        index = 0;
        ReadStatus status = TryGetFeatureIndex(ordinal, out FeatureIndexData row, out diagnostic);
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
        if (!state.Materialize)
        {
            status = state.BindFeatureCount(featureCount, Location, out diagnostic);
            if (status != ReadStatus.Done) return status;
            if (state.TryGetLanguageFact(position)) { diagnostic = default; return ReadStatus.Done; }
        }
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
        else
        {
            // Validate mode publishes the completed success only after every row and
            // scalar above passed; a failed admission publishes nothing.
            status = state.PublishLanguageFact(position, state.At(position), budget, out diagnostic);
            if (status != ReadStatus.Done) return status;
        }
        diagnostic = default;
        return ReadStatus.Done;
    }
}

public readonly ref partial struct LayoutFeatureView
{
    // Same projection shape as LangSys: the generated record accessor owns the ordinal,
    // range and truncation rules; the ushort getter is its compatibility projection.

    public ReadStatus TryGetLookupIndex(int ordinal, out ushort index, out FontDiagnostic diagnostic)
    {
        index = 0;
        ReadStatus status = TryGetLookupIndex(ordinal, out LookupIndexData row, out diagnostic);
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
        if (!state.Materialize)
        {
            status = state.BindLookupCount(lookupCount, Location, out diagnostic);
            if (status != ReadStatus.Done) return status;
            if (state.TryGetFeatureFact(position)) { diagnostic = default; return ReadStatus.Done; }
        }
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
        else
        {
            // Validate-mode facts publish, same admission and placement as LangSys.
            status = state.PublishFeatureFact(position, state.At(position), budget, out diagnostic);
            if (status != ReadStatus.Done) return status;
        }
        diagnostic = default;
        return ReadStatus.Done;
    }
}

public readonly ref partial struct LayoutScriptView
{
    // The generated contract version 1 accessors own the ordinal, range, truncation and
    // reference rules (TryGetLanguageRecord family plus the DefaultLanguage/Language
    // openers); this partial keeps the DFLT rule, tag ordering and the traversal below.

    internal ReadStatus Process(uint tag, int featureCount, int position, LayoutReadState state,
        DataLocation listLocation, DataLocation recordLocation, out LayoutScript? model,
        out FontDiagnostic diagnostic, BuildBudget budget)
    {
        model = null;
        ReadStatus status = TryGetLanguageCount(out ushort count, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (!budget.Work(1, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        if (!state.Materialize)
        {
            status = state.BindFeatureCount(featureCount, Location, out diagnostic);
            if (status != ReadStatus.Done) return status;
        }
        if (state.Scripts is not null && state.Scripts.TryGetValue(position, out var cached))
        {
            if (tag == LayoutScriptLayout.DefaultTag && cached.DefaultLanguage is null)
                return MissingDefault(out diagnostic);
            model = cached.AtRecord(tag, listLocation, recordLocation);
            diagnostic = default; return ReadStatus.Done;
        }
        if (!state.Materialize && state.TryGetScriptFact(position, out LayoutScriptFact fact))
        {
            // Facts keep every per-alias condition observable: DFLT depends on this
            // record's tag, not on the shared body, so the recheck survives the hit.
            if (tag == LayoutScriptLayout.DefaultTag && fact.DefaultLanguageOffset == 0)
                return MissingDefault(out diagnostic);
            diagnostic = default; return ReadStatus.Done;
        }
        if (tag == LayoutScriptLayout.DefaultTag && DefaultLanguageOffset == 0)
            return MissingDefault(out diagnostic);
        status = TryGetLanguageRecordExtent(out _, out _, out ulong headerEndExtent, out diagnostic);
        if (status != ReadStatus.Done) return status;
        int headerEnd = (int)headerEndExtent;
        if (!budget.Work(headerEnd, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        LayoutLangSys? defaultLanguage = null;
        int consumed = headerEnd;
        if (DefaultLanguageOffset != 0)
        {
            status = ReadLanguage(-1, DefaultLanguageOffset, featureCount, position, state,
                out defaultLanguage, out diagnostic, budget);
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
            status = LayoutListsTableCompiler.CheckTagEncoding(row.Tag,
                Location.AtLocal(DataField.Tag, (ulong)at, i), out diagnostic);
            if (status != ReadStatus.Done) return status;
            if (i > 0 && row.Tag <= previous)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    Location.AtLocal(DataField.Tag, (ulong)at, i), out diagnostic, row.Tag, previous);
            previous = row.Tag;
            status = ReadLanguage(i, row.Offset, featureCount, position, state,
                out var language, out diagnostic, budget);
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
        else
        {
            // Inner-first facts order (LangSys before Script) and publish only after
            // the whole body validated, mirroring the materialize Store placement.
            status = state.PublishScriptFact(position, DefaultLanguageOffset,
                state.At(position), budget, out diagnostic);
            if (status != ReadStatus.Done) return status;
        }
        diagnostic = default;
        return ReadStatus.Done;
    }

    private ReadStatus ReadLanguage(int ordinal, ushort offset, int featureCount,
        int position, LayoutReadState state, out LayoutLangSys? model, out FontDiagnostic diagnostic, BuildBudget budget)
    {
        model = null;
        LayoutLangSysView child;
        ReadStatus status;
        if (ordinal < 0)
            status = TryOpenDefaultLanguage(out child, out _, out diagnostic);
        else
            status = TryOpenLanguage(ordinal, out child, out _, out diagnostic);
        // offset is inside this suffix of the operation's root, so this sum is bounded by root length.
        int target = position + offset;
        return status == ReadStatus.Done ? child.Process(featureCount, target, state,
            out model, out diagnostic, budget) : status;
    }

    private ReadStatus MissingDefault(out FontDiagnostic diagnostic) =>
        ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
            Location.AtLocal(DataField.Offset, LayoutScriptLayout.DefaultOffsetField), out diagnostic, 0, 1);
}
