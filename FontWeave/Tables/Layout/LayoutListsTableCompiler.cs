using FontWeave.Core;

namespace FontWeave.Tables.Layout;

public static partial class LayoutListsTableCompiler
{
    public static ReadStatus TryCreateLangSys(ushort requiredFeatureIndex, ReadOnlySpan<ushort> featureIndices,
        int featureCount, out LayoutLangSys? model, out FontDiagnostic diagnostic, BuildLimits? limits = null,
        DataLocation location = default) => TryCreateLangSys(requiredFeatureIndex, featureIndices, featureCount,
            out model, out diagnostic, new BuildBudget(limits), location);

    internal static ReadStatus TryCreateLangSys(ushort requiredFeatureIndex, ReadOnlySpan<ushort> featureIndices,
        int featureCount, out LayoutLangSys? model, out FontDiagnostic diagnostic, BuildBudget budget,
        DataLocation location = default)
    {
        model = null;
        ReadStatus status = CheckCount(featureIndices.Length, location, LayoutLangSysLayout.CountOffset, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = CheckCount(featureCount, location, LayoutLangSysLayout.CountOffset, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (!budget.Work(2L * featureIndices.Length + 1, location, out diagnostic)) return ReadStatus.ResourceLimit;
        status = ValidateFeatureIndices(requiredFeatureIndex, featureIndices, featureCount, location, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (!budget.Buffer(2L * featureIndices.Length, location, out diagnostic)) return ReadStatus.ResourceLimit;
        model = new(requiredFeatureIndex, featureIndices.ToArray(), location,
            LayoutLangSysLayout.HeaderLength + LayoutLangSysLayout.IndexStride * featureIndices.Length);
        diagnostic = default; return ReadStatus.Done;
    }

    public static ReadStatus TryCreateScript(uint tag, LayoutLangSys? defaultLanguage,
        ReadOnlySpan<LayoutLanguage> languages, int featureCount, out LayoutScript? model,
        out FontDiagnostic diagnostic, BuildLimits? limits = null, DataLocation location = default) =>
        TryCreateScript(tag, defaultLanguage, languages, featureCount, out model, out diagnostic,
            new BuildBudget(limits), location);

    internal static ReadStatus TryCreateScript(uint tag, LayoutLangSys? defaultLanguage,
        ReadOnlySpan<LayoutLanguage> languages, int featureCount, out LayoutScript? model,
        out FontDiagnostic diagnostic, BuildBudget budget, DataLocation location = default)
    {
        model = null;
        ReadStatus status = ValidateScript(tag, defaultLanguage, languages, featureCount, location, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        if (!budget.Work(languages.Length, location, out diagnostic) ||
            !budget.Buffer(16L * languages.Length, location, out diagnostic)) return ReadStatus.ResourceLimit;
        model = new(tag, defaultLanguage, languages.ToArray(), location,
            LayoutScriptLayout.HeaderLength + LayoutTagListLayout.RecordStride * languages.Length);
        diagnostic = default; return ReadStatus.Done;
    }

    public static ReadStatus TryCreateFeature(uint tag, ushort paramsOffsetRaw, ReadOnlySpan<ushort> lookupIndices,
        int lookupCount, out LayoutFeature? model, out FontDiagnostic diagnostic, BuildLimits? limits = null,
        DataLocation location = default) => TryCreateFeature(tag, paramsOffsetRaw, lookupIndices, lookupCount,
            out model, out diagnostic, new BuildBudget(limits), location);

    internal static ReadStatus TryCreateFeature(uint tag, ushort paramsOffsetRaw, ReadOnlySpan<ushort> lookupIndices,
        int lookupCount, out LayoutFeature? model, out FontDiagnostic diagnostic, BuildBudget budget,
        DataLocation location = default)
    {
        model = null;
        ReadStatus status = CheckCount(lookupIndices.Length, location, LayoutFeatureLayout.CountOffset, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = CheckCount(lookupCount, location, LayoutFeatureLayout.CountOffset, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (!budget.Work(2L * lookupIndices.Length + 1, location, out diagnostic)) return ReadStatus.ResourceLimit;
        status = ValidateLookupIndices(lookupIndices, lookupCount, location, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (!budget.Buffer(2L * lookupIndices.Length, location, out diagnostic)) return ReadStatus.ResourceLimit;
        model = new(tag, paramsOffsetRaw, lookupIndices.ToArray(), location,
            LayoutFeatureLayout.HeaderLength + LayoutFeatureLayout.IndexStride * lookupIndices.Length);
        diagnostic = default; return ReadStatus.Done;
    }

    public static ReadStatus TryCreate(uint version, uint featureVariationsOffsetRaw,
        ReadOnlySpan<LayoutScript> scripts, ReadOnlySpan<LayoutFeature> features, ReadOnlySpan<LookupTable> lookups,
        int numGlyphs, LayoutLookupKind kind, out LayoutListsTable? model, out FontDiagnostic diagnostic,
        BuildLimits? limits = null, DataLocation location = default) => TryCreate(version, featureVariationsOffsetRaw,
            scripts, features, lookups, numGlyphs, kind, out model, out diagnostic, new BuildBudget(limits), location);

    internal static ReadStatus TryCreate(uint version, uint featureVariationsOffsetRaw,
        ReadOnlySpan<LayoutScript> scripts, ReadOnlySpan<LayoutFeature> features, ReadOnlySpan<LookupTable> lookups,
        int numGlyphs, LayoutLookupKind kind, out LayoutListsTable? model, out FontDiagnostic diagnostic,
        BuildBudget budget, DataLocation location = default)
    {
        model = null;
        if (version is not (LayoutListsLayout.Version10 or LayoutListsLayout.Version11))
            return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedVersion,
                location.AtLocal(DataField.Version, 0), out diagnostic, version);
        ReadStatus status = CoverageTableCompiler.CheckNumGlyphs(numGlyphs, location, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = CheckKind(kind, location, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = ValidateScripts(scripts, features.Length, location, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        status = ValidateFeatures(features, lookups.Length, location, false, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        status = CheckCount(lookups.Length, location, 0, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (!budget.Work(lookups.Length, location, out diagnostic)) return ReadStatus.ResourceLimit;
        for (int i = 0; i < lookups.Length; i++)
        {
            LookupTable? lookup = lookups[i];
            if (lookup is null)
                return InvalidModel(location, i, out diagnostic);
            if (!SameSource(location, lookup.Location) || lookup.NumGlyphs != numGlyphs || lookup.Kind != kind)
                return ReadWire.Fail(ReadStatus.InvalidSource, DiagnosticCode.WrongSourceFace,
                    lookup.Location, out diagnostic, (ulong)lookup.NumGlyphs, (ulong)numGlyphs);
        }
        long copied = (long)scripts.Length + features.Length + lookups.Length;
        if (!budget.Work(copied, location, out diagnostic) ||
            !budget.Buffer(8 * copied, location, out diagnostic)) return ReadStatus.ResourceLimit;
        model = new(version, featureVariationsOffsetRaw, scripts.ToArray(), features.ToArray(), lookups.ToArray(),
            numGlyphs, kind, location);
        diagnostic = default; return ReadStatus.Done;
    }

    internal static ReadStatus CheckCount(int count, DataLocation location, ulong fieldPosition, out FontDiagnostic diagnostic)
    {
        if (count is < 0 or > ushort.MaxValue)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                location.AtLocal(DataField.Count, fieldPosition), out diagnostic,
                unchecked((ulong)(long)count), ushort.MaxValue);
        diagnostic = default; return ReadStatus.Done;
    }

    internal static ReadStatus CheckKind(LayoutLookupKind kind, DataLocation location, out FontDiagnostic diagnostic)
    {
        if (kind is not (LayoutLookupKind.Gsub or LayoutLookupKind.Gpos))
            return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
                location.AtLocal(DataField.Tag, 0), out diagnostic, unchecked((ulong)(long)kind));
        diagnostic = default; return ReadStatus.Done;
    }

    private static ReadStatus ValidateFeatureIndices(ushort required, ReadOnlySpan<ushort> indices,
        int count, DataLocation location, out FontDiagnostic diagnostic)
    {
        ReadStatus status;
        if (required != ushort.MaxValue)
        {
            status = LayoutReadWire.Index(required, count, location, LayoutLangSysLayout.RequiredOffset, -1, out diagnostic);
            if (status != ReadStatus.Done) return status;
        }
        for (int i = 0; i < indices.Length; i++)
        {
            status = LayoutReadWire.Index(indices[i], count, location,
                LayoutLangSysLayout.HeaderLength + (ulong)i * LayoutLangSysLayout.IndexStride, i, out diagnostic);
            if (status != ReadStatus.Done) return status;
        }
        diagnostic = default; return ReadStatus.Done;
    }

    private static ReadStatus ValidateLookupIndices(ReadOnlySpan<ushort> indices, int count,
        DataLocation location, out FontDiagnostic diagnostic)
    {
        for (int i = 0; i < indices.Length; i++)
        {
            ReadStatus status = LayoutReadWire.Index(indices[i], count, location,
                LayoutFeatureLayout.HeaderLength + (ulong)i * LayoutFeatureLayout.IndexStride, i, out diagnostic);
            if (status != ReadStatus.Done) return status;
        }
        diagnostic = default; return ReadStatus.Done;
    }

    private static ReadStatus ValidateLanguage(LayoutLangSys language, int featureCount,
        DataLocation owner, out FontDiagnostic diagnostic, BuildBudget budget)
    {
        if (!SameSource(owner, language.Location))
            return ReadWire.Fail(ReadStatus.InvalidSource, DiagnosticCode.WrongSourceFace, language.Location, out diagnostic);
        if (!budget.Work(language.FeatureIndices.Length + 1L, language.Location, out diagnostic)) return ReadStatus.ResourceLimit;
        return ValidateFeatureIndices(language.RequiredFeatureIndex, language.FeatureIndices, featureCount, language.Location, out diagnostic);
    }

    private static ReadStatus ValidateScript(uint tag, LayoutLangSys? defaultLanguage,
        ReadOnlySpan<LayoutLanguage> languages, int featureCount, DataLocation location,
        out FontDiagnostic diagnostic, BuildBudget budget)
    {
        ReadStatus status = CheckCount(languages.Length, location, LayoutScriptLayout.CountOffset, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = CheckCount(featureCount, location, LayoutScriptLayout.CountOffset, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (tag == LayoutScriptLayout.DefaultTag && defaultLanguage is null)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                location.AtLocal(DataField.Offset, LayoutScriptLayout.DefaultOffsetField), out diagnostic, 0, 1);
        if (!budget.Work(languages.Length + 1L, location, out diagnostic)) return ReadStatus.ResourceLimit;
        if (defaultLanguage is not null)
        {
            status = ValidateLanguage(defaultLanguage, featureCount, location, out diagnostic, budget);
            if (status != ReadStatus.Done) return status;
        }
        uint previous = 0;
        for (int i = 0; i < languages.Length; i++)
        {
            var item = languages[i];
            if (item.Language is null) return InvalidModel(location, i, out diagnostic);
            if (i > 0 && item.Tag <= previous)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    location.AtLocal(DataField.Tag, LayoutScriptLayout.HeaderLength +
                        (ulong)i * LayoutTagListLayout.RecordStride, i), out diagnostic, item.Tag, previous);
            previous = item.Tag;
            status = ValidateLanguage(item.Language, featureCount, location, out diagnostic, budget);
            if (status != ReadStatus.Done) return status;
        }
        diagnostic = default; return ReadStatus.Done;
    }

    private static ReadStatus ValidateScripts(ReadOnlySpan<LayoutScript> scripts, int featureCount,
        DataLocation location, out FontDiagnostic diagnostic, BuildBudget budget)
    {
        ReadStatus status = CheckCount(scripts.Length, location, 0, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = CheckCount(featureCount, location, 0, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (!budget.Work(scripts.Length, location, out diagnostic)) return ReadStatus.ResourceLimit;
        uint previous = 0;
        for (int i = 0; i < scripts.Length; i++)
        {
            LayoutScript? script = scripts[i];
            if (script is null) return InvalidModel(location, i, out diagnostic);
            if (!SameSource(location, script.Location))
                return ReadWire.Fail(ReadStatus.InvalidSource, DiagnosticCode.WrongSourceFace, script.Location, out diagnostic);
            if (i > 0 && script.Tag <= previous)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    script.RecordLocation, out diagnostic, script.Tag, previous);
            previous = script.Tag;
            status = ValidateScript(script.Tag, script.DefaultLanguage, script.Languages, featureCount,
                script.Location, out diagnostic, budget);
            if (status != ReadStatus.Done) return status;
        }
        diagnostic = default; return ReadStatus.Done;
    }

    private static ReadStatus ValidateFeatures(ReadOnlySpan<LayoutFeature> features, int lookupCount,
        DataLocation location, bool writing, out FontDiagnostic diagnostic, BuildBudget budget)
    {
        ReadStatus status = CheckCount(features.Length, location, 0, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = CheckCount(lookupCount, location, 0, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (!budget.Work(features.Length, location, out diagnostic)) return ReadStatus.ResourceLimit;
        uint previous = 0;
        for (int i = 0; i < features.Length; i++)
        {
            LayoutFeature? feature = features[i];
            if (feature is null) return InvalidModel(location, i, out diagnostic);
            if (!SameSource(location, feature.Location))
                return ReadWire.Fail(ReadStatus.InvalidSource, DiagnosticCode.WrongSourceFace, feature.Location, out diagnostic);
            if (i > 0 && feature.Tag < previous)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    feature.RecordLocation, out diagnostic, feature.Tag, previous);
            previous = feature.Tag;
            if (writing && feature.ParamsOffsetRaw != 0)
                return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
                    feature.Location.AtLocal(DataField.Offset, 0), out diagnostic, feature.ParamsOffsetRaw, 0);
            if (!budget.Work(feature.LookupIndices.Length, feature.Location, out diagnostic)) return ReadStatus.ResourceLimit;
            status = ValidateLookupIndices(feature.LookupIndices, lookupCount, feature.Location, out diagnostic);
            if (status != ReadStatus.Done) return status;
        }
        diagnostic = default; return ReadStatus.Done;
    }

    private static bool SameSource(DataLocation owner, DataLocation child) =>
        owner.SourceId == child.SourceId && owner.FaceIndex == child.FaceIndex && owner.TableTag == child.TableTag;

    private static ReadStatus InvalidModel(DataLocation location, int ordinal, out FontDiagnostic diagnostic) =>
        ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
            location.AtLocal(DataField.Payload, 0, ordinal), out diagnostic, (ulong)ordinal);
}
