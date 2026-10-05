using FontWeave.Core;

namespace FontWeave.Tables.Layout;

public readonly ref partial struct LayoutTagListView
{
    // The generated contract version 1 accessors own the ordinal, range, truncation and
    // typed-reference rules (TryGetTagRecord family plus the role-dispatched Script and
    // Feature openers); this partial keeps only the semantic checks and the traversal.

    public ReadStatus TryReadScripts(int featureCount, out LayoutScript[]? scripts,
        out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        scripts = null;
        var budget = new BuildBudget(limits);
        if (!budget.Input(_data.Length, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        return ProcessScripts(featureCount, 0, new(Location, _data.Length, true), out scripts, out diagnostic, budget);
    }

    public ReadStatus ValidateScripts(int featureCount, out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        var budget = new BuildBudget(limits);
        if (!budget.Input(_data.Length, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        return ProcessScripts(featureCount, 0, new(Location, _data.Length, false), out _, out diagnostic, budget);
    }

    public ReadStatus TryReadFeatures(int lookupCount, out LayoutFeature[]? features,
        out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        features = null;
        var budget = new BuildBudget(limits);
        if (!budget.Input(_data.Length, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        return ProcessFeatures(lookupCount, 0, new(Location, _data.Length, true), out features, out diagnostic, budget);
    }

    public ReadStatus ValidateFeatures(int lookupCount, out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        var budget = new BuildBudget(limits);
        if (!budget.Input(_data.Length, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        return ProcessFeatures(lookupCount, 0, new(Location, _data.Length, false), out _, out diagnostic, budget);
    }

    internal ReadStatus ProcessScripts(int featureCount, int position, LayoutReadState state,
        out LayoutScript[]? scripts, out FontDiagnostic diagnostic, BuildBudget budget)
    {
        scripts = null;
        ReadStatus status = TryGetCount(out ushort count, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = LayoutListsTableCompiler.CheckCount(featureCount, Location, 0, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = TryGetTagRecordExtent(out _, out _, out ulong recordsEndExtent, out diagnostic);
        if (status != ReadStatus.Done) return status;
        int recordsEnd = (int)recordsEndExtent;
        if (!budget.Work(recordsEnd, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        LayoutScript[]? result = null;
        if (state.Materialize)
        {
            if (!budget.Buffer(8L * count, Location, out diagnostic)) return ReadStatus.ResourceLimit;
            result = new LayoutScript[count];
        }
        uint previous = 0;
        for (int i = 0; i < count; i++)
        {
            status = TryGetTagRecord(i, out var record, out diagnostic);
            if (status != ReadStatus.Done) return status;
            int at = LayoutTagListLayout.HeaderLength + i * LayoutTagListLayout.RecordStride;
            status = LayoutListsTableCompiler.CheckTagEncoding(record.Tag,
                Location.AtLocal(DataField.Tag, (ulong)at, i), out diagnostic);
            if (status != ReadStatus.Done) return status;
            if (i > 0 && record.Tag <= previous)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    Location.AtLocal(DataField.Tag, (ulong)at, i), out diagnostic, record.Tag, previous);
            previous = record.Tag;
            status = TryOpenScript(LayoutTagListRole.ScriptList, i, out var child, out _, out diagnostic);
            if (status != ReadStatus.Done) return status;
            int target = position + record.Offset;
            status = child.Process(record.Tag, featureCount, target, state, Location,
                Location.AtLocal(DataField.Tag, (ulong)at, i), out var script, out diagnostic, budget);
            if (status != ReadStatus.Done) return status;
            if (result is not null) result[i] = script!;
        }
        scripts = result;
        diagnostic = default;
        return ReadStatus.Done;
    }

    internal ReadStatus ProcessFeatures(int lookupCount, int position, LayoutReadState state,
        out LayoutFeature[]? features, out FontDiagnostic diagnostic, BuildBudget budget)
    {
        features = null;
        ReadStatus status = TryGetCount(out ushort count, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = LayoutListsTableCompiler.CheckCount(lookupCount, Location, 0, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = TryGetTagRecordExtent(out _, out _, out ulong recordsEndExtent, out diagnostic);
        if (status != ReadStatus.Done) return status;
        int recordsEnd = (int)recordsEndExtent;
        if (!budget.Work(recordsEnd, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        LayoutFeature[]? result = null;
        if (state.Materialize)
        {
            if (!budget.Buffer(8L * count, Location, out diagnostic)) return ReadStatus.ResourceLimit;
            result = new LayoutFeature[count];
        }
        uint previous = 0;
        for (int i = 0; i < count; i++)
        {
            status = TryGetTagRecord(i, out var record, out diagnostic);
            if (status != ReadStatus.Done) return status;
            int at = LayoutTagListLayout.HeaderLength + i * LayoutTagListLayout.RecordStride;
            status = LayoutListsTableCompiler.CheckTagEncoding(record.Tag,
                Location.AtLocal(DataField.Tag, (ulong)at, i), out diagnostic);
            if (status != ReadStatus.Done) return status;
            if (i > 0 && record.Tag < previous)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    Location.AtLocal(DataField.Tag, (ulong)at, i), out diagnostic, record.Tag, previous);
            previous = record.Tag;
            status = TryOpenFeature(LayoutTagListRole.FeatureList, i, out var child, out _, out diagnostic);
            if (status != ReadStatus.Done) return status;
            int target = position + record.Offset;
            status = child.Process(record.Tag, lookupCount, target, state, Location,
                Location.AtLocal(DataField.Tag, (ulong)at, i), out var feature, out diagnostic, budget);
            if (status != ReadStatus.Done) return status;
            if (result is not null) result[i] = feature!;
        }
        features = result;
        diagnostic = default;
        return ReadStatus.Done;
    }
}

public readonly ref partial struct LayoutLookupListView
{
    // The generated contract version 1 accessors own the ordinal, range, truncation and
    // reference rules (TryGetLookupOffset record form, the extent family and the
    // role-dispatched Lookup opener); the ushort getter is the compatibility projection.

    public ReadStatus TryGetLookupOffset(int ordinal, out ushort offset, out FontDiagnostic diagnostic)
    {
        offset = 0;
        ReadStatus status = TryGetLookupOffset(ordinal, out LookupOffsetData row, out diagnostic);
        if (status == ReadStatus.Done) offset = row.Offset;
        return status;
    }

    public ReadStatus TryRead(int numGlyphs, LayoutLookupKind kind, out LookupTable[]? lookups,
        out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        lookups = null;
        var budget = new BuildBudget(limits);
        if (!budget.Input(_data.Length, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        return Process(numGlyphs, kind, 0, new(Location, _data.Length, true), out lookups, out diagnostic, budget);
    }

    public ReadStatus Validate(int numGlyphs, LayoutLookupKind kind,
        out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        var budget = new BuildBudget(limits);
        if (!budget.Input(_data.Length, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        return Process(numGlyphs, kind, 0, new(Location, _data.Length, false), out _, out diagnostic, budget);
    }

    internal ReadStatus Process(int numGlyphs, LayoutLookupKind kind, int position, LayoutReadState state,
        out LookupTable[]? lookups, out FontDiagnostic diagnostic, BuildBudget budget)
    {
        lookups = null;
        ReadStatus status = TryGetCount(out ushort count, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = CoverageTableCompiler.CheckNumGlyphs(numGlyphs, Location, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = LayoutListsTableCompiler.CheckKind(kind, Location, out diagnostic);
        if (status != ReadStatus.Done) return status;
        int recordsEnd = LayoutLookupListLayout.HeaderLength + LayoutLookupListLayout.OffsetStride * count;
        if (!budget.Work(recordsEnd, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        LookupTable[]? result = null;
        if (state.Materialize)
        {
            if (!budget.Buffer(8L * count, Location, out diagnostic)) return ReadStatus.ResourceLimit;
            result = new LookupTable[count];
        }
        for (int i = 0; i < count; i++)
        {
            status = TryGetLookupOffset(i, out ushort offset, out diagnostic);
            if (status != ReadStatus.Done) return status;
            status = TryOpenLookup(kind, i, out var child, out _, out diagnostic);
            if (status != ReadStatus.Done) return status;
            int target = position + offset;
            if (!budget.Work(1, Location, out diagnostic)) return ReadStatus.ResourceLimit;
            if (state.Lookups is not null && state.Lookups.TryGetValue(target, out var cached))
            { result![i] = cached; continue; }
            if (state.Materialize)
            {
                status = child.TryReadHeader(numGlyphs, kind, out var lookup, out diagnostic, budget);
                if (status != ReadStatus.Done) return status;
                status = LayoutReadState.Store(state.Lookups, target, lookup!, state.At(target),
                    budget, out diagnostic);
                if (status != ReadStatus.Done) return status;
                result![i] = lookup!;
            }
            else
            {
                status = child.ValidateHeader(numGlyphs, kind, out diagnostic, budget);
                if (status != ReadStatus.Done) return status;
            }
        }
        lookups = result;
        diagnostic = default;
        return ReadStatus.Done;
    }
}

public readonly ref partial struct LayoutListsView
{
    // The generated contract version 1 accessors own the three child references (the
    // TargetRole-dispatched ScriptList/FeatureList openers and the ForwardRole LookupList
    // opener, each against the declared ChildBase computed); this partial keeps version
    // selection, the raw variations observation and the traversal below.

    // 10 bytes for a v1.0 root and 14 once the variations header exists: one of the
    // explicitly allowed bounded selections (spec 2.3). The computed feeds the reference
    // Minimum on demand and never re-prices the Work below, which keeps its local value.
    private static ReadStatus ChildBaseOf(uint version, DataLocation diagnosticLocation,
        out ulong value, out FontDiagnostic diagnostic)
    {
        value = version == LayoutListsLayout.Version11 ? 14UL : 10UL;
        diagnostic = default;
        return ReadStatus.Done;
    }

    public ReadStatus TryRead(int numGlyphs, LayoutLookupKind kind, out LayoutListsTable? model,
        out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        model = null;
        var budget = new BuildBudget(limits);
        if (!budget.Input(_data.Length, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        return TryRead(numGlyphs, kind, out model, out diagnostic, budget);
    }

    internal ReadStatus TryRead(int numGlyphs, LayoutLookupKind kind, out LayoutListsTable? model,
        out FontDiagnostic diagnostic, BuildBudget budget) =>
        Process(numGlyphs, kind, true, out model, out diagnostic, budget);

    public ReadStatus Validate(int numGlyphs, LayoutLookupKind kind,
        out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        var budget = new BuildBudget(limits);
        if (!budget.Input(_data.Length, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        return Validate(numGlyphs, kind, out diagnostic, budget);
    }

    internal ReadStatus Validate(int numGlyphs, LayoutLookupKind kind,
        out FontDiagnostic diagnostic, BuildBudget budget) =>
        Process(numGlyphs, kind, false, out _, out diagnostic, budget);

    private ReadStatus Process(int numGlyphs, LayoutLookupKind kind, bool materialize,
        out LayoutListsTable? model, out FontDiagnostic diagnostic, BuildBudget budget)
    {
        model = null;
        ReadStatus status = TryRequireKnownVersion(out ulong version, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = CoverageTableCompiler.CheckNumGlyphs(numGlyphs, Location, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = LayoutListsTableCompiler.CheckKind(kind, Location, out diagnostic);
        if (status != ReadStatus.Done) return status;
        uint variations = 0;
        int headerEnd = LayoutListsLayout.Header10;
        if (version == LayoutListsLayout.Version11)
        {
            status = TryReadVariationsHeaderAt(LayoutListsLayout.VariationsOffsetField, -1, out var header, out diagnostic);
            if (status != ReadStatus.Done) return status;
            variations = header.OffsetRaw; headerEnd = LayoutListsLayout.Header11;
        }
        if (!budget.Work(headerEnd, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        var state = new LayoutReadState(Location, _data.Length, materialize);
        status = TryOpenScriptList(kind, out var scriptsView, out _, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = TryOpenFeatureList(kind, out var featuresView, out _, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = TryOpenLookupList(kind, out var lookupsView, out _, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = scriptsView.ProcessScripts(featuresView.Count, ScriptListOffset, state, out var scripts, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        status = featuresView.ProcessFeatures(lookupsView.Count, FeatureListOffset, state, out var features, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        status = lookupsView.Process(numGlyphs, kind, LookupListOffset, state, out var lookups, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        if (materialize) model = new((uint)version, variations, scripts!, features!, lookups!, numGlyphs, kind, Location);
        diagnostic = default;
        return ReadStatus.Done;
    }
}
