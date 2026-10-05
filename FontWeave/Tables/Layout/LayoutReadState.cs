using FontWeave.Core;

namespace FontWeave.Tables.Layout;

// One fixed-depth traversal over one root window. Separate typed caches prevent
// address-only reuse across layouts; feature/lookup counts are fixed by the caller.
internal sealed class LayoutReadState
{
    // Context sentinel: the slot has not been used yet (spec 6.2 keeps every semantic
    // context in the facts' domain; each slot carries its value once, lazily).
    internal const int UnboundContext = -1;

    internal LayoutReadState(DataLocation origin, int length, bool materialize,
        int featureCount = UnboundContext, int lookupCount = UnboundContext,
        int numGlyphs = UnboundContext, int kind = UnboundContext)
    {
        Origin = origin; Length = length; Materialize = materialize;
        _featureCountContext = featureCount; _lookupCountContext = lookupCount;
        _numGlyphsContext = numGlyphs; _kindContext = kind;
        if (materialize)
        {
            Scripts = new(); Languages = new(); Features = new(); Lookups = new();
        }
    }

    internal DataLocation Origin { get; }
    internal int Length { get; }
    internal bool Materialize { get; }
    internal Dictionary<int, LayoutScript>? Scripts { get; }
    internal Dictionary<int, LayoutLangSys>? Languages { get; }
    internal Dictionary<int, LayoutFeature>? Features { get; }
    internal Dictionary<int, LookupTable>? Lookups { get; }

    // Validated facts for shared subtrees (spec 6.2): only scalars proven by one
    // completed strict validation of a body at a root-relative position — never a
    // model, never an owned array (V1-12). The dictionaries are created on first
    // publication (A5 scan-scope shape), so an operation without shared subtrees
    // never allocates one, and never in materialize mode, where the model
    // dictionaries above remain the only caches.
    private Dictionary<int, LayoutScriptFact>? _scriptFacts;
    private Dictionary<int, LayoutSemanticFact>? _languageFacts;
    private Dictionary<int, LayoutSemanticFact>? _featureFacts;
    private Dictionary<int, LayoutSemanticFact>? _lookupFacts;

    // Context binding slots: a fact published under one caller context must not be
    // reused under another. The public entries pin their contexts at construction;
    // any slot left out binds on the first child that uses it, and a later different
    // value is refused outright instead of trusting caller discipline (spec 6.2).
    private int _featureCountContext = UnboundContext;
    private int _lookupCountContext = UnboundContext;
    private int _numGlyphsContext = UnboundContext;
    private int _kindContext = UnboundContext;
    internal int FeatureCountContext => _featureCountContext;
    internal int LookupCountContext => _lookupCountContext;
    internal int NumGlyphsContext => _numGlyphsContext;
    internal int KindContext => _kindContext;

    // Published-fact counts for the sanctioned source-closure validation probes;
    // mechanical observability only, no schema or behavior surface.
    internal int ScriptFactCount => _scriptFacts?.Count ?? 0;
    internal int LanguageFactCount => _languageFacts?.Count ?? 0;
    internal int FeatureFactCount => _featureFacts?.Count ?? 0;
    internal int LookupFactCount => _lookupFacts?.Count ?? 0;

    internal DataLocation At(int position) => Origin.AtLocal(DataField.None, (ulong)position);

    internal static ReadStatus BindContext(ref int slot, int value, DataLocation location,
        out FontDiagnostic diagnostic)
    {
        if (slot != UnboundContext && slot != value)
            return ReadWire.Fail(ReadStatus.InvalidSource, DiagnosticCode.WrongSourceFace,
                location, out diagnostic);
        slot = value;
        diagnostic = default;
        return ReadStatus.Done;
    }

    internal ReadStatus BindFeatureCount(int featureCount, DataLocation location,
        out FontDiagnostic diagnostic) =>
        BindContext(ref _featureCountContext, featureCount, location, out diagnostic);

    internal ReadStatus BindLookupCount(int lookupCount, DataLocation location,
        out FontDiagnostic diagnostic) =>
        BindContext(ref _lookupCountContext, lookupCount, location, out diagnostic);

    internal ReadStatus BindNumGlyphs(int numGlyphs, DataLocation location,
        out FontDiagnostic diagnostic) =>
        BindContext(ref _numGlyphsContext, numGlyphs, location, out diagnostic);

    internal ReadStatus BindKind(LayoutLookupKind kind, DataLocation location,
        out FontDiagnostic diagnostic) =>
        BindContext(ref _kindContext, (int)kind, location, out diagnostic);

    // Facts hit and publication. Publication charges one Buffer(16) facts slot at the
    // physical target exactly like the materialize Store; a failed admission publishes
    // nothing, so a failed or unfinished body never owns a facts slot (spec 6.2).
    // First publication wins per position: a facts hit is the only path that skips a
    // body, so one position cannot be published twice inside one operation.
    internal bool TryGetScriptFact(int position, out LayoutScriptFact fact)
    {
        if (_scriptFacts is { } facts && facts.TryGetValue(position, out fact)) return true;
        fact = default;
        return false;
    }

    internal bool TryGetLanguageFact(int position) =>
        _languageFacts is { } facts && facts.ContainsKey(position);

    internal bool TryGetFeatureFact(int position) =>
        _featureFacts is { } facts && facts.ContainsKey(position);

    internal bool TryGetLookupFact(int position) =>
        _lookupFacts is { } facts && facts.ContainsKey(position);

    internal ReadStatus PublishScriptFact(int position, ushort defaultLanguageOffset,
        DataLocation location, BuildBudget budget, out FontDiagnostic diagnostic)
    {
        if (!budget.Buffer(16, location, out diagnostic)) return ReadStatus.ResourceLimit;
        (_scriptFacts ??= new Dictionary<int, LayoutScriptFact>()).Add(position,
            new LayoutScriptFact(defaultLanguageOffset));
        diagnostic = default;
        return ReadStatus.Done;
    }

    internal ReadStatus PublishLanguageFact(int position, DataLocation location,
        BuildBudget budget, out FontDiagnostic diagnostic)
    {
        if (!budget.Buffer(16, location, out diagnostic)) return ReadStatus.ResourceLimit;
        (_languageFacts ??= new Dictionary<int, LayoutSemanticFact>()).Add(position, default);
        diagnostic = default;
        return ReadStatus.Done;
    }

    internal ReadStatus PublishFeatureFact(int position, DataLocation location,
        BuildBudget budget, out FontDiagnostic diagnostic)
    {
        if (!budget.Buffer(16, location, out diagnostic)) return ReadStatus.ResourceLimit;
        (_featureFacts ??= new Dictionary<int, LayoutSemanticFact>()).Add(position, default);
        diagnostic = default;
        return ReadStatus.Done;
    }

    internal ReadStatus PublishLookupFact(int position, DataLocation location,
        BuildBudget budget, out FontDiagnostic diagnostic)
    {
        if (!budget.Buffer(16, location, out diagnostic)) return ReadStatus.ResourceLimit;
        (_lookupFacts ??= new Dictionary<int, LayoutSemanticFact>()).Add(position, default);
        diagnostic = default;
        return ReadStatus.Done;
    }

    internal static ReadStatus Store<T>(Dictionary<int, T>? cache, int position, T model,
        DataLocation location, BuildBudget budget, out FontDiagnostic diagnostic) where T : class
    {
        if (cache is not null)
        {
            if (!budget.Buffer(16, location, out diagnostic)) return ReadStatus.ResourceLimit;
            cache.Add(position, model);
        }
        diagnostic = default;
        return ReadStatus.Done;
    }
}

// The validated shape of one Script body: the DFLT rule's per-alias recheck needs
// only whether a default LangSys exists on the validated wire. A scalar, never a
// model (spec 6.2, V1-12).
internal readonly record struct LayoutScriptFact(ushort DefaultLanguageOffset);

// Marker fact: the body at this root-relative position completed every check of its
// kind under the bound contexts. It carries no payload and no model reference; a
// different kind at the same address publishes into its own dictionary and never
// reuses this one (spec 6.2).
internal readonly record struct LayoutSemanticFact;

internal static class LayoutReadWire
{
    internal static ReadStatus Index(ushort value, int count, DataLocation location, ulong position,
        int ordinal, out FontDiagnostic diagnostic)
    {
        if (value >= count)
            return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                location.AtLocal(DataField.Payload, position, ordinal), out diagnostic, value, (ulong)count);
        diagnostic = default;
        return ReadStatus.Done;
    }
}
