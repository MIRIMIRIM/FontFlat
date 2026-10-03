using FontWeave.Core;

namespace FontWeave.Tables.Layout;

/// <summary>The GDEF structures required by declared Lookup flags. This is not a GDEF root writer.</summary>
public sealed class GdefLookupContext
{
    private GdefLookupContext(GdefClassDefinitions classes, MarkGlyphSetsTable? markGlyphSets)
    {
        Classes = classes;
        MarkGlyphSets = markGlyphSets;
    }

    public GdefClassDefinitions Classes { get; }
    public MarkGlyphSetsTable? MarkGlyphSets { get; }
    public uint Version => Classes.Version;
    public int NumGlyphs => Classes.NumGlyphs;
    public DataLocation Location => Classes.Location;
    public ClassDefTable? GlyphClasses => Classes.GlyphClasses;
    public ClassDefTable? MarkAttachmentClasses => Classes.MarkAttachmentClasses;

    public static ReadStatus TryCreate(GdefClassDefinitions classes, MarkGlyphSetsTable? markGlyphSets,
        out GdefLookupContext? context, out FontDiagnostic diagnostic, BuildLimits? limits = null) =>
        TryCreate(classes, markGlyphSets, out context, out diagnostic, new BuildBudget(limits));

    internal static ReadStatus TryCreate(GdefClassDefinitions classes, MarkGlyphSetsTable? markGlyphSets,
        out GdefLookupContext? context, out FontDiagnostic diagnostic, BuildBudget budget)
    {
        ArgumentNullException.ThrowIfNull(classes);
        context = null;
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
        context = new(classes, markGlyphSets);
        diagnostic = default;
        return ReadStatus.Done;
    }

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
            ? TryCreate(classes!, MarkGlyphSets, out updated, out diagnostic, budget) : status;
    }
}
