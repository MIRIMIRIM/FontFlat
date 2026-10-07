using FontWeave.Core;

namespace FontWeave.Tables.Layout;

// GPOS type 9's semantic partial (T4-06 section 3.3): the finite one-level unwrap,
// the ExtensionSubst face with the GPOS leaf set - direct 1/2/3 (T4-05), the mark
// family 4/5/6 and the GSUB-common contextual pair 7/8 (T4-06, OQ-T4-06-3's
// single-view resolution). Wrapped=9 (the spec self-reference ban) and unknown
// values fall to the DefaultRaw remainder set, whose explicit typed read refuses
// here at the extension's own selector field; the Lookup layer owns the lookup-shaped
// signature and the whole-table metadata-only degradation. No recursion: exactly one
// level. The coverage cache and the subtable start follow the direct-leaf
// convention: keys are physical Lookup-local positions, so a wrapped body
// participates in the same per-Lookup cache as direct leaves.
public readonly ref partial struct ExtensionPosView
{
    // One-level unwrap validation, mirror of the ExtensionSubst face.
    internal ReadStatus Validate(int numGlyphs, int lookupCount, out FontDiagnostic diagnostic, BuildBudget budget)
    {
        ReadStatus status = TryRequireKnownVersion(out _, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = TryGetExtensionLookupType(out ushort wrappedType, out diagnostic);
        if (status != ReadStatus.Done) return status;
        switch (wrappedType)
        {
            case 1:
                status = TryOpenWrappedSinglePos(out var single, out _, out diagnostic);
                return status == ReadStatus.Done ? single.Validate(numGlyphs, out diagnostic, budget) : status;
            case 2:
                status = TryOpenWrappedPairPos(out var pair, out _, out diagnostic);
                return status == ReadStatus.Done ? pair.Validate(numGlyphs, out diagnostic, budget) : status;
            case 3:
                status = TryOpenWrappedCursivePos(out var cursive, out _, out diagnostic);
                return status == ReadStatus.Done ? cursive.Validate(numGlyphs, out diagnostic, budget) : status;
            case 4:
                status = TryOpenWrappedMarkBasePos(out var markBase, out _, out diagnostic);
                return status == ReadStatus.Done ? markBase.Validate(numGlyphs, out diagnostic, budget) : status;
            case 5:
                status = TryOpenWrappedMarkLigPos(out var markLig, out _, out diagnostic);
                return status == ReadStatus.Done ? markLig.Validate(numGlyphs, out diagnostic, budget) : status;
            case 6:
                status = TryOpenWrappedMarkMarkPos(out var markMark, out _, out diagnostic);
                return status == ReadStatus.Done ? markMark.Validate(numGlyphs, out diagnostic, budget) : status;
            case 7:
                status = TryOpenWrappedContextPos(out var contextual, out _, out diagnostic);
                return status == ReadStatus.Done
                    ? contextual.Validate(numGlyphs, lookupCount, out diagnostic, budget) : status;
            case 8:
                status = TryOpenWrappedChainedContextPos(out var chaining, out _, out diagnostic);
                return status == ReadStatus.Done
                    ? chaining.Validate(numGlyphs, lookupCount, out diagnostic, budget) : status;
            default:
                return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
                    Location.AtLocal(DataField.Tag, ExtensionPosLayout.TypeAt),
                    out diagnostic, wrappedType, 0);
        }
    }

    internal ReadStatus TryRead(int numGlyphs, int lookupCount, out ExtensionPosTable? model,
        out FontDiagnostic diagnostic, BuildBudget budget)
        => ReadOwned(numGlyphs, lookupCount, out model, out diagnostic, budget, null, 0, default);

    internal ReadStatus TryRead(int numGlyphs, int lookupCount, out ExtensionPosTable? model,
        out FontDiagnostic diagnostic, BuildBudget budget, Dictionary<int, CoverageTable>? coverageCache,
        int subtableStart, DataLocation lookupLocation)
        => ReadOwned(numGlyphs, lookupCount, out model, out diagnostic, budget, coverageCache,
            subtableStart, lookupLocation);

    private ReadStatus ReadOwned(int numGlyphs, int lookupCount, out ExtensionPosTable? model,
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
                Location.AtLocal(DataField.Offset, ExtensionPosLayout.OffsetAt), out diagnostic,
                extensionOffset, (ulong)(int.MaxValue - subtableStart));
        int wrappedStart = subtableStart + (int)extensionOffset;
        if (!budget.Buffer(16, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        switch (wrappedType)
        {
            case 1:
            {
                status = TryOpenWrappedSinglePos(out var single, out _, out diagnostic);
                if (status != ReadStatus.Done) return status;
                status = single.TryRead(numGlyphs, out var singleModel, out diagnostic, budget);
                if (status != ReadStatus.Done) return status;
                model = new(wrappedType, singleModel!, null, null, Location,
                    ExtensionPosLayout.HeaderLength + singleModel!.ConsumedLength);
                break;
            }
            case 2:
            {
                status = TryOpenWrappedPairPos(out var pair, out _, out diagnostic);
                if (status != ReadStatus.Done) return status;
                status = pair.TryRead(numGlyphs, out var pairModel, out diagnostic, budget);
                if (status != ReadStatus.Done) return status;
                model = new(wrappedType, null, pairModel!, null, Location,
                    ExtensionPosLayout.HeaderLength + pairModel!.ConsumedLength);
                break;
            }
            case 3:
            {
                status = TryOpenWrappedCursivePos(out var cursive, out _, out diagnostic);
                if (status != ReadStatus.Done) return status;
                status = cursive.TryRead(numGlyphs, out var cursiveModel, out diagnostic, budget);
                if (status != ReadStatus.Done) return status;
                model = new(wrappedType, null, null, cursiveModel!, Location,
                    ExtensionPosLayout.HeaderLength + cursiveModel!.ConsumedLength);
                break;
            }
            case 4:
            {
                status = TryOpenWrappedMarkBasePos(out var markBase, out _, out diagnostic);
                if (status != ReadStatus.Done) return status;
                status = markBase.TryRead(numGlyphs, out var markBaseModel, out diagnostic, budget);
                if (status != ReadStatus.Done) return status;
                model = new(wrappedType, null, null, null, Location,
                    ExtensionPosLayout.HeaderLength + markBaseModel!.ConsumedLength,
                    markBaseModel);
                break;
            }
            case 5:
            {
                status = TryOpenWrappedMarkLigPos(out var markLig, out _, out diagnostic);
                if (status != ReadStatus.Done) return status;
                status = markLig.TryRead(numGlyphs, out var markLigModel, out diagnostic, budget);
                if (status != ReadStatus.Done) return status;
                model = new(wrappedType, null, null, null, Location,
                    ExtensionPosLayout.HeaderLength + markLigModel!.ConsumedLength,
                    markLig: markLigModel);
                break;
            }
            case 6:
            {
                status = TryOpenWrappedMarkMarkPos(out var markMark, out _, out diagnostic);
                if (status != ReadStatus.Done) return status;
                status = markMark.TryRead(numGlyphs, out var markMarkModel, out diagnostic, budget);
                if (status != ReadStatus.Done) return status;
                model = new(wrappedType, null, null, null, Location,
                    ExtensionPosLayout.HeaderLength + markMarkModel!.ConsumedLength,
                    markMark: markMarkModel);
                break;
            }
            case 7:
            {
                status = TryOpenWrappedContextPos(out var wrappedContextual, out _, out diagnostic);
                if (status != ReadStatus.Done) return status;
                status = wrappedContextual.TryRead(numGlyphs, lookupCount, out var contextualModel,
                    out diagnostic, budget, coverageCache, wrappedStart, lookupLocation);
                if (status != ReadStatus.Done) return status;
                model = new(wrappedType, null, null, null, Location,
                    ExtensionPosLayout.HeaderLength + contextualModel!.ConsumedLength,
                    contextual: contextualModel);
                break;
            }
            case 8:
            {
                status = TryOpenWrappedChainedContextPos(out var wrappedChaining, out _, out diagnostic);
                if (status != ReadStatus.Done) return status;
                status = wrappedChaining.TryRead(numGlyphs, lookupCount, out var chainingModel,
                    out diagnostic, budget, coverageCache, wrappedStart, lookupLocation);
                if (status != ReadStatus.Done) return status;
                model = new(wrappedType, null, null, null, Location,
                    ExtensionPosLayout.HeaderLength + chainingModel!.ConsumedLength,
                    chaining: chainingModel);
                break;
            }
            default:
                return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
                    Location.AtLocal(DataField.Tag, ExtensionPosLayout.TypeAt),
                    out diagnostic, wrappedType, 0);
        }
        diagnostic = default;
        return ReadStatus.Done;
    }
}
