using FontWeave.Core;

namespace FontWeave.Tables.Layout;

public readonly ref partial struct ExtensionSubstView
{
    // One-level unwrap validation: wrapped types 1..4 (T4-03) and the contextual
    // family 5/6/8 (T4-04) open their typed direct leaf; the nested/self type 7 and
    // unknown values fall to the DefaultRaw remainder set, whose explicit typed read
    // refuses here at the extension's own selector field (the Lookup layer owns the
    // lookup-shaped W7 signature and the whole-table metadata-only degradation,
    // OQ-T4-03-4). No recursion: exactly one level.
    internal ReadStatus Validate(int numGlyphs, int lookupCount, out FontDiagnostic diagnostic, BuildBudget budget)
    {
        ReadStatus status = TryRequireKnownVersion(out _, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = TryGetExtensionLookupType(out ushort wrappedType, out diagnostic);
        if (status != ReadStatus.Done) return status;
        switch (wrappedType)
        {
            case 1:
                status = TryOpenWrappedSingle(out var single, out _, out diagnostic);
                return status == ReadStatus.Done ? single.Validate(numGlyphs, out diagnostic, budget) : status;
            case 2:
                status = TryOpenWrappedMultiple(out var multiple, out _, out diagnostic);
                return status == ReadStatus.Done ? multiple.Validate(numGlyphs, out diagnostic, budget) : status;
            case 3:
                status = TryOpenWrappedAlternate(out var alternate, out _, out diagnostic);
                return status == ReadStatus.Done ? alternate.Validate(numGlyphs, out diagnostic, budget) : status;
            case 4:
                status = TryOpenWrappedLigature(out var ligature, out _, out diagnostic);
                return status == ReadStatus.Done ? ligature.Validate(numGlyphs, out diagnostic, budget) : status;
            case 5:
                status = TryOpenWrappedContextual(out var contextual, out _, out diagnostic);
                return status == ReadStatus.Done
                    ? contextual.Validate(numGlyphs, lookupCount, out diagnostic, budget) : status;
            case 6:
                status = TryOpenWrappedChaining(out var chaining, out _, out diagnostic);
                return status == ReadStatus.Done
                    ? chaining.Validate(numGlyphs, lookupCount, out diagnostic, budget) : status;
            case 8:
                status = TryOpenWrappedReverse(out var reverse, out _, out diagnostic);
                return status == ReadStatus.Done
                    ? reverse.Validate(numGlyphs, out diagnostic, budget) : status;
            default:
                return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
                    Location.AtLocal(DataField.Tag, ExtensionSubstLayout.TypeAt),
                    out diagnostic, wrappedType, 0);
        }
    }

    // Materializes the wrapped leaf into an ExtensionSubstTable. The coverage cache
    // and the subtable start follow the SingleSubstView convention: keys are physical
    // Lookup-local positions, so a wrapped body participates in the same per-Lookup
    // cache as direct leaves.
    internal ReadStatus TryRead(int numGlyphs, int lookupCount, out ExtensionSubstTable? model,
        out FontDiagnostic diagnostic, BuildBudget budget)
        => ReadOwned(numGlyphs, lookupCount, out model, out diagnostic, budget, null, 0, default);

    internal ReadStatus TryRead(int numGlyphs, int lookupCount, out ExtensionSubstTable? model,
        out FontDiagnostic diagnostic, BuildBudget budget, Dictionary<int, CoverageTable>? coverageCache,
        int subtableStart, DataLocation lookupLocation)
        => ReadOwned(numGlyphs, lookupCount, out model, out diagnostic, budget, coverageCache,
            subtableStart, lookupLocation);

    private ReadStatus ReadOwned(int numGlyphs, int lookupCount, out ExtensionSubstTable? model,
        out FontDiagnostic diagnostic, BuildBudget budget, Dictionary<int, CoverageTable>? coverageCache,
        int subtableStart, DataLocation lookupLocation)
    {
        model = null;
        ReadStatus status = TryRequireKnownVersion(out _, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = TryGetExtensionLookupType(out ushort wrappedType, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = TryGetExtensionOffset(out uint extensionOffset, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (subtableStart < 0 || extensionOffset > (ulong)(int.MaxValue - subtableStart))
            return ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.RangeOutsideSource,
                Location.AtLocal(DataField.Offset, ExtensionSubstLayout.OffsetAt), out diagnostic,
                extensionOffset, (ulong)(int.MaxValue - subtableStart));
        int wrappedStart = subtableStart + (int)extensionOffset;
        if (!budget.Buffer(16, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        switch (wrappedType)
        {
            case 1:
                {
                    status = TryOpenWrappedSingle(out var single, out _, out diagnostic);
                    if (status != ReadStatus.Done) return status;
                    // The frozen SingleSubstView cached overload annotates the cache
                    // non-null; a cacheless call keeps its own overload.
                    status = coverageCache is null
                        ? single.TryRead(numGlyphs, out var singleModel, out diagnostic, budget)
                        : single.TryRead(numGlyphs, out singleModel, out diagnostic, budget,
                            coverageCache, wrappedStart, lookupLocation);
                    if (status != ReadStatus.Done) return status;
                    model = new(wrappedType, singleModel!, null, null, null, Location,
                        ExtensionSubstLayout.HeaderLength + singleModel!.ConsumedLength);
                    break;
                }
            case 2:
                {
                    status = TryOpenWrappedMultiple(out var multiple, out _, out diagnostic);
                    if (status != ReadStatus.Done) return status;
                    status = multiple.TryRead(numGlyphs, out var multipleModel, out diagnostic, budget,
                        coverageCache, wrappedStart, lookupLocation);
                    if (status != ReadStatus.Done) return status;
                    model = new(wrappedType, null, multipleModel!, null, null, Location,
                        ExtensionSubstLayout.HeaderLength + multipleModel!.ConsumedLength);
                    break;
                }
            case 3:
                {
                    status = TryOpenWrappedAlternate(out var alternate, out _, out diagnostic);
                    if (status != ReadStatus.Done) return status;
                    status = alternate.TryRead(numGlyphs, out var alternateModel, out diagnostic, budget,
                        coverageCache, wrappedStart, lookupLocation);
                    if (status != ReadStatus.Done) return status;
                    model = new(wrappedType, null, null, alternateModel!, null, Location,
                        ExtensionSubstLayout.HeaderLength + alternateModel!.ConsumedLength);
                    break;
                }
            case 4:
                {
                    status = TryOpenWrappedLigature(out var ligature, out _, out diagnostic);
                    if (status != ReadStatus.Done) return status;
                    status = ligature.TryRead(numGlyphs, out var ligatureModel, out diagnostic, budget,
                        coverageCache, wrappedStart, lookupLocation);
                    if (status != ReadStatus.Done) return status;
                    model = new(wrappedType, null, null, null, ligatureModel!, Location,
                        ExtensionSubstLayout.HeaderLength + ligatureModel!.ConsumedLength);
                    break;
                }
            case 5:
                {
                    status = TryOpenWrappedContextual(out var wrappedContextual, out _, out diagnostic);
                    if (status != ReadStatus.Done) return status;
                    status = wrappedContextual.TryRead(numGlyphs, lookupCount, out var contextualModel,
                        out diagnostic, budget, coverageCache, wrappedStart, lookupLocation);
                    if (status != ReadStatus.Done) return status;
                    model = new(wrappedType, null, null, null, null, Location,
                        ExtensionSubstLayout.HeaderLength + contextualModel!.ConsumedLength,
                        contextualModel, null, null);
                    break;
                }
            case 6:
                {
                    status = TryOpenWrappedChaining(out var wrappedChaining, out _, out diagnostic);
                    if (status != ReadStatus.Done) return status;
                    status = wrappedChaining.TryRead(numGlyphs, lookupCount, out var chainingModel,
                        out diagnostic, budget, coverageCache, wrappedStart, lookupLocation);
                    if (status != ReadStatus.Done) return status;
                    model = new(wrappedType, null, null, null, null, Location,
                        ExtensionSubstLayout.HeaderLength + chainingModel!.ConsumedLength,
                        null, chainingModel, null);
                    break;
                }
            case 8:
                {
                    status = TryOpenWrappedReverse(out var wrappedReverse, out _, out diagnostic);
                    if (status != ReadStatus.Done) return status;
                    status = wrappedReverse.TryRead(numGlyphs, out var reverseModel,
                        out diagnostic, budget, coverageCache, wrappedStart, lookupLocation);
                    if (status != ReadStatus.Done) return status;
                    model = new(wrappedType, null, null, null, null, Location,
                        ExtensionSubstLayout.HeaderLength + reverseModel!.ConsumedLength,
                        null, null, reverseModel);
                    break;
                }
            default:
                return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
                    Location.AtLocal(DataField.Tag, ExtensionSubstLayout.TypeAt),
                    out diagnostic, wrappedType, 0);
        }
        diagnostic = default;
        return ReadStatus.Done;
    }
}
