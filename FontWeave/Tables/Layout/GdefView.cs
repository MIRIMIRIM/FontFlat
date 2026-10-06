using FontWeave.Core;

namespace FontWeave.Tables.Layout;

public readonly ref partial struct GdefView
{
    /// <summary>Reads class definitions and MarkGlyphSets for Lookup-flag validation.
    /// The AttachList and LigCaretList references are declared but not traversed by this
    /// reader; the variation store stays a raw observation.</summary>
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
    /// Other header offsets keep their declared references unopened here; the variation
    /// store stays a raw observation.</summary>
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

    /// <summary>Validates the complete declared static slice without materializing any
    /// owned model: version tree, all five references and the whole-table semantics.
    /// The variation store is not traversed (T5-01 prerequisite, OQ-T4-02-5).</summary>
    public ReadStatus ValidateTable(int numGlyphs, out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        var budget = new BuildBudget(limits);
        if (!budget.Input(_data.Length, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        return ValidateTable(numGlyphs, out diagnostic, budget);
    }

    internal ReadStatus ValidateTable(int numGlyphs, out FontDiagnostic diagnostic, BuildBudget budget)
    {
        ReadStatus status = TryRequireKnownVersion(out ulong version, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = CoverageTableCompiler.CheckNumGlyphs(numGlyphs, Location, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = RequireVersionRecords(version, budget, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = TryOpenGlyphClassDef(out var glyphView, out bool glyphPresent, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (glyphPresent)
        {
            status = glyphView.ValidateClassLimit(4, budget, out diagnostic);
            if (status != ReadStatus.Done) return status;
            status = glyphView.Validate(numGlyphs, out diagnostic, budget);
            if (status != ReadStatus.Done) return status;
        }
        status = TryOpenMarkAttachClassDef(out var markView, out bool markPresent, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (markPresent)
        {
            status = markView.Validate(numGlyphs, out diagnostic, budget);
            if (status != ReadStatus.Done) return status;
        }
        if (version >= GdefLayout.Version12)
        {
            status = TryOpenMarkGlyphSetsAt(GdefLayout.MarkSetsAt, -1, out var setsView,
                out bool setsPresent, out diagnostic);
            if (status != ReadStatus.Done) return status;
            if (setsPresent)
            {
                status = setsView.Validate(numGlyphs, out diagnostic, budget);
                if (status != ReadStatus.Done) return status;
            }
        }
        return ValidateDeclaredChains(numGlyphs, budget, out diagnostic);
    }

    /// <summary>Reads the complete declared static slice into an owned
    /// <see cref="GdefTable"/>. The existing Lookup-flag readers keep their signatures;
    /// this is the additional whole-table entry, not a replacement.</summary>
    public ReadStatus TryReadTable(int numGlyphs, out GdefTable? model,
        out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        model = null;
        var budget = new BuildBudget(limits);
        if (!budget.Input(_data.Length, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        return TryReadTable(numGlyphs, out model, out diagnostic, budget);
    }

    internal ReadStatus TryReadTable(int numGlyphs, out GdefTable? model,
        out FontDiagnostic diagnostic, BuildBudget budget)
    {
        model = null;
        ReadStatus status = TryRequireKnownVersion(out ulong version, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = RequireVersionRecords(version, budget, out diagnostic);
        if (status != ReadStatus.Done) return status;
        // The classes reader owns the two ClassDef consumers, the glyph-side class
        // domain, the shared-body identity and the raw store value.
        status = TryReadClasses(numGlyphs, out var classes, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        MarkGlyphSetsTable? sets = null;
        ushort markSetsOffset = classes!.MarkGlyphSetsDefOffset;
        if (markSetsOffset != 0)
        {
            status = TryOpenMarkGlyphSetsAt(GdefLayout.MarkSetsAt, -1, out var setsView,
                out bool setsPresent, out diagnostic);
            if (status != ReadStatus.Done) return status;
            if (setsPresent)
            {
                status = setsView.TryRead(numGlyphs, out sets, out diagnostic, budget);
                if (status != ReadStatus.Done) return status;
            }
        }
        AttachListTable? attach = null;
        if (AttachListOffset != 0)
        {
            status = TryOpenAttachList(out var attachView, out bool attachPresent, out diagnostic);
            if (status != ReadStatus.Done) return status;
            if (attachPresent)
            {
                status = attachView.TryRead(numGlyphs, out attach, out diagnostic, budget);
                if (status != ReadStatus.Done) return status;
            }
        }
        LigCaretListTable? lig = null;
        if (LigCaretListOffset != 0)
        {
            status = TryOpenLigCaretList(out var ligView, out bool ligPresent, out diagnostic);
            if (status != ReadStatus.Done) return status;
            if (ligPresent)
            {
                status = ligView.TryRead(numGlyphs, out lig, out diagnostic, budget);
                if (status != ReadStatus.Done) return status;
            }
        }
        int headerEnd = classes.Version == GdefLayout.Version13 ? GdefLayout.Header13
            : classes.Version >= GdefLayout.Version12 ? GdefLayout.Header12 : GdefLayout.Header10;
        int consumed = headerEnd;
        if (classes.GlyphClasses is not null)
            consumed = Math.Max(consumed, GlyphClassDefOffset + classes.GlyphClasses.ConsumedLength);
        if (classes.MarkAttachmentClasses is not null)
            consumed = Math.Max(consumed, MarkAttachClassDefOffset + classes.MarkAttachmentClasses.ConsumedLength);
        if (sets is not null) consumed = Math.Max(consumed, markSetsOffset + sets.ConsumedLength);
        if (attach is not null) consumed = Math.Max(consumed, AttachListOffset + attach.ConsumedLength);
        if (lig is not null) consumed = Math.Max(consumed, LigCaretListOffset + lig.ConsumedLength);
        model = new(classes.Version, numGlyphs, classes.GlyphClasses, classes.MarkAttachmentClasses,
            sets, attach, lig, classes.ItemVariationStoreOffset, Location, consumed);
        diagnostic = default;
        return ReadStatus.Done;
    }

    // The v1.0 header physically ends at 12, so the conditional record readers own the
    // existence domain of the marksets/store slots (v1.0 refuses inactive before
    // touching bytes 12..14, A4 OQ-6 precedent).
    private ReadStatus RequireVersionRecords(ulong version, BuildBudget budget, out FontDiagnostic diagnostic)
    {
        ReadStatus status = version >= GdefLayout.Version12
            ? TryReadMarkSetsHeaderAt(GdefLayout.MarkSetsAt, -1, out _, out diagnostic)
            : Done(out diagnostic);
        return status;
    }

    private static ReadStatus Done(out FontDiagnostic diagnostic)
    {
        diagnostic = default;
        return ReadStatus.Done;
    }

    private ReadStatus ValidateDeclaredChains(int numGlyphs, BuildBudget budget, out FontDiagnostic diagnostic)
    {
        ReadStatus status;
        if (AttachListOffset != 0)
        {
            status = TryOpenAttachList(out var attachView, out bool attachPresent, out diagnostic);
            if (status != ReadStatus.Done) return status;
            if (attachPresent)
            {
                status = attachView.Validate(numGlyphs, out diagnostic, budget);
                if (status != ReadStatus.Done) return status;
            }
        }
        if (LigCaretListOffset != 0)
        {
            status = TryOpenLigCaretList(out var ligView, out bool ligPresent, out diagnostic);
            if (status != ReadStatus.Done) return status;
            if (ligPresent)
            {
                status = ligView.Validate(numGlyphs, out diagnostic, budget);
                if (status != ReadStatus.Done) return status;
            }
        }
        diagnostic = default;
        return ReadStatus.Done;
    }
}
