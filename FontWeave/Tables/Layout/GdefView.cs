using FontWeave.Core;

namespace FontWeave.Tables.Layout;

public readonly ref partial struct GdefView
{
    /// <summary>Reads class definitions and MarkGlyphSets for Lookup-flag validation.
    /// AttachList, LigCaretList and variation data remain untraversed raw observations.</summary>
    public ReadStatus TryReadContext(int numGlyphs, out GdefLookupContext? context,
        out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        context = null;
        var budget = new BuildBudget(limits);
        if (!budget.Input(_data.Length, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        return TryReadContext(numGlyphs, out context, out diagnostic, budget);
    }

    internal ReadStatus TryReadContext(int numGlyphs, out GdefLookupContext? context,
        out FontDiagnostic diagnostic, BuildBudget budget)
    {
        context = null;
        ReadStatus status = TryReadClasses(numGlyphs, out var classes, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        MarkGlyphSetsTable? sets = null;
        ushort offset = classes!.MarkGlyphSetsDefOffset;
        if (offset != 0)
        {
            // The declared envelope owns the raw-value guard (record existence domain,
            // v1.0 refuses inactive before touching bytes 12..14), the zero policy, the
            // HeaderEnd floor, the source bounds and the child TryOpen.
            status = TryOpenMarkGlyphSetsAt(GdefLayout.MarkSetsAt, -1, out var setsView,
                out bool setsPresent, out diagnostic);
            if (status != ReadStatus.Done) return status;
            if (setsPresent)
            {
                status = setsView.TryRead(numGlyphs, out sets, out diagnostic, budget);
                if (status != ReadStatus.Done) return status;
            }
        }
        return GdefLookupContext.TryCreate(classes, sets, out context, out diagnostic, budget);
    }

    /// <summary>Reads the two class-definition consumers, not the complete GDEF table.
    /// Other header offsets are retained as raw values without traversing their targets.</summary>
    public ReadStatus TryReadClasses(int numGlyphs, out GdefClassDefinitions? model,
        out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        model = null;
        var budget = new BuildBudget(limits);
        if (!budget.Input(_data.Length, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        return TryReadClasses(numGlyphs, out model, out diagnostic, budget);
    }

    internal ReadStatus TryReadClasses(int numGlyphs, out GdefClassDefinitions? model,
        out FontDiagnostic diagnostic, BuildBudget budget)
    {
        model = null;
        ReadStatus status = TryRequireKnownVersion(out ulong version, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = CoverageTableCompiler.CheckNumGlyphs(numGlyphs, Location, out diagnostic);
        if (status != ReadStatus.Done) return status;
        int headerLength = GdefLayout.Header10;
        ushort markSetsOffset = 0;
        uint variationStoreOffset = 0;
        if (version >= GdefLayout.Version12)
        {
            status = TryReadMarkSetsHeaderAt(GdefLayout.MarkSetsAt, -1, out var header, out diagnostic);
            if (status != ReadStatus.Done) return status;
            markSetsOffset = header.MarkGlyphSetsDefOffset;
            headerLength = GdefLayout.Header12;
        }
        if (version == GdefLayout.Version13)
        {
            status = TryReadVariationStoreHeaderAt(GdefLayout.VariationStoreAt, -1, out var header, out diagnostic);
            if (status != ReadStatus.Done) return status;
            variationStoreOffset = header.ItemVariationStoreOffset;
            headerLength = GdefLayout.Header13;
        }
        if (!budget.Work(headerLength, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        // The declared openers own the zero policy, the HeaderEnd floor, the source
        // bounds and the child TryOpen; class limits and the child reads stay here.
        status = TryOpenGlyphClassDef(out var glyphView, out bool glyphPresent, out diagnostic);
        if (status != ReadStatus.Done) return status;
        ClassDefTable? glyphClasses = null;
        if (glyphPresent)
        {
            status = glyphView.ValidateClassLimit(4, budget, out diagnostic);
            if (status != ReadStatus.Done) return status;
            status = glyphView.TryRead(numGlyphs, out glyphClasses, out diagnostic, budget);
            if (status != ReadStatus.Done) return status;
        }
        // The same ClassDef layout and glyph context is compatible in both roles.
        // The stricter 0..4 check above entails the generic mark-attachment value limit.
        ClassDefTable? markAttachmentClasses;
        if (glyphClasses is not null && MarkAttachClassDefOffset == GlyphClassDefOffset)
            markAttachmentClasses = glyphClasses;
        else
        {
            status = TryOpenMarkAttachClassDef(out var markView, out bool markPresent, out diagnostic);
            if (status != ReadStatus.Done) return status;
            markAttachmentClasses = null;
            if (markPresent)
            {
                status = markView.TryRead(numGlyphs, out markAttachmentClasses, out diagnostic, budget);
                if (status != ReadStatus.Done) return status;
            }
        }
        model = new((uint)version, numGlyphs, glyphClasses, markAttachmentClasses,
            AttachListOffset, LigCaretListOffset, markSetsOffset, variationStoreOffset, Location);
        diagnostic = default;
        return ReadStatus.Done;
    }
}
