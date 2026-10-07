using FontWeave.Core;
using FontWeave.Tables.Variation;

namespace FontWeave.Tables.Layout;

/// <summary>The GDEF structures required by declared Lookup flags. This is not a GDEF root writer.</summary>
public sealed class GdefLookupContext
{
    private GdefLookupContext(GdefClassDefinitions classes, MarkGlyphSetsTable? markGlyphSets,
        VariationStoreTable? variationStore)
    {
        Classes = classes;
        MarkGlyphSets = markGlyphSets;
        VariationStore = variationStore;
    }

    public GdefClassDefinitions Classes { get; }
    public MarkGlyphSetsTable? MarkGlyphSets { get; }
    /// <summary>The optional cross-table VariationDevice dereference target (T4-06
    /// section 3.4, OQ-T4-06-4's single carrier): the GDEF item variation store the
    /// GPOS device chain resolves 0x8000 (outer, inner) rows against. No default
    /// value - the caller supplies the store it read from the same face, or none.</summary>
    public VariationStoreTable? VariationStore { get; }
    public uint Version => Classes.Version;
    public int NumGlyphs => Classes.NumGlyphs;
    public DataLocation Location => Classes.Location;
    public ClassDefTable? GlyphClasses => Classes.GlyphClasses;
    public ClassDefTable? MarkAttachmentClasses => Classes.MarkAttachmentClasses;

    public static ReadStatus TryCreate(GdefClassDefinitions classes, MarkGlyphSetsTable? markGlyphSets,
        out GdefLookupContext? context, out FontDiagnostic diagnostic, BuildLimits? limits = null) =>
        TryCreate(classes, markGlyphSets, null, out context, out diagnostic, limits);

    // The store-taking form (T4-06 section 3.4): the store must come from the same
    // face as the classes (the markGlyphSets identity discipline; the shared GDEF tag
    // is part of the same-table identity, numGlyphs does not apply to a store).
    public static ReadStatus TryCreate(GdefClassDefinitions classes, MarkGlyphSetsTable? markGlyphSets,
        VariationStoreTable? variationStore, out GdefLookupContext? context,
        out FontDiagnostic diagnostic, BuildLimits? limits = null) =>
        TryCreate(classes, markGlyphSets, variationStore, out context, out diagnostic, new BuildBudget(limits));

    internal static ReadStatus TryCreate(GdefClassDefinitions classes, MarkGlyphSetsTable? markGlyphSets,
        VariationStoreTable? variationStore, out GdefLookupContext? context,
        out FontDiagnostic diagnostic, BuildBudget budget)
    {
        ArgumentNullException.ThrowIfNull(classes);
        context = null;
        if (variationStore is not null &&
            (classes.Location.SourceId != variationStore.Location.SourceId ||
             classes.Location.FaceIndex != variationStore.Location.FaceIndex ||
             classes.Location.TableTag != variationStore.Location.TableTag))
            return ReadWire.Fail(ReadStatus.InvalidSource, DiagnosticCode.WrongSourceFace,
                classes.Location.AtLocal(DataField.None, 0), out diagnostic);
        if (markGlyphSets is not null)
        {
            if (classes.NumGlyphs != markGlyphSets.NumGlyphs ||
                classes.Location.SourceId != markGlyphSets.Location.SourceId ||
                classes.Location.FaceIndex != markGlyphSets.Location.FaceIndex ||
                classes.Location.TableTag != markGlyphSets.Location.TableTag)
                return ReadWire.Fail(ReadStatus.InvalidSource, DiagnosticCode.WrongSourceFace,
                    classes.Location.AtLocal(DataField.None, 0), out diagnostic);
            if (classes.Version < GdefLayout.Version12)
                return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
                    classes.Location.AtLocal(DataField.Version, 0), out diagnostic, classes.Version, GdefLayout.Version12);
        }
        if (!budget.Work(1, classes.Location, out diagnostic)) return ReadStatus.ResourceLimit;
        context = new(classes, markGlyphSets, variationStore);
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>Atomically re-binds the VariationDevice dereference target (T4-06
    /// section 3.4's explicit-input form, the markFilteringSet discipline).</summary>
    public ReadStatus TryWithVariationStore(VariationStoreTable? variationStore,
        out GdefLookupContext? updated, out FontDiagnostic diagnostic, BuildLimits? limits = null) =>
        TryCreate(Classes, MarkGlyphSets, variationStore, out updated, out diagnostic, limits);

    public ReadStatus TryWithMarkGlyphSets(MarkGlyphSetsTable? markGlyphSets,
        out GdefLookupContext? updated, out FontDiagnostic diagnostic, BuildLimits? limits = null) =>
        TryCreate(Classes, markGlyphSets, out updated, out diagnostic, limits);

    public ReadStatus TryWithClasses(ClassDefTable? glyphClasses, ClassDefTable? markAttachmentClasses,
        out GdefLookupContext? updated, out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        updated = null;
        var budget = new BuildBudget(limits);
        ReadStatus status = Classes.TryWithClasses(glyphClasses, markAttachmentClasses,
            out var classes, out diagnostic, budget);
        return status == ReadStatus.Done
            ? TryCreate(classes!, MarkGlyphSets, VariationStore, out updated, out diagnostic, budget) : status;
    }
}
