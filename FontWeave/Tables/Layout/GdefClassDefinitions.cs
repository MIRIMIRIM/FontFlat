using FontWeave.Core;

namespace FontWeave.Tables.Layout;

/// <summary>The GDEF class-definition slice. Header references outside this slice are
/// raw observations, not parsed or compiled GDEF payloads.</summary>
public sealed class GdefClassDefinitions
{
    internal GdefClassDefinitions(uint version, int numGlyphs, ClassDefTable? glyphClasses,
        ClassDefTable? markAttachmentClasses, ushort attachListOffset, ushort ligCaretListOffset,
        ushort markGlyphSetsDefOffset, uint itemVariationStoreOffset, DataLocation location)
    {
        Version = version;
        NumGlyphs = numGlyphs;
        GlyphClasses = glyphClasses;
        MarkAttachmentClasses = markAttachmentClasses;
        AttachListOffset = attachListOffset;
        LigCaretListOffset = ligCaretListOffset;
        MarkGlyphSetsDefOffset = markGlyphSetsDefOffset;
        ItemVariationStoreOffset = itemVariationStoreOffset;
        Location = location;
    }

    public uint Version { get; }
    public int NumGlyphs { get; }
    public ClassDefTable? GlyphClasses { get; }
    public ClassDefTable? MarkAttachmentClasses { get; }
    public ushort AttachListOffset { get; }
    public ushort LigCaretListOffset { get; }
    public ushort MarkGlyphSetsDefOffset { get; }
    public uint ItemVariationStoreOffset { get; }
    public DataLocation Location { get; }

    /// <summary>Replaces the class consumers without silently substituting a model
    /// from another source face or glyph-count context. Does not rebuild the GDEF root.</summary>
    public ReadStatus TryWithClasses(ClassDefTable? glyphClasses, ClassDefTable? markAttachmentClasses,
        out GdefClassDefinitions? updated, out FontDiagnostic diagnostic, BuildLimits? limits = null) =>
        TryWithClasses(glyphClasses, markAttachmentClasses, out updated, out diagnostic, new BuildBudget(limits));

    internal ReadStatus TryWithClasses(ClassDefTable? glyphClasses, ClassDefTable? markAttachmentClasses,
        out GdefClassDefinitions? updated, out FontDiagnostic diagnostic, BuildBudget budget)
    {
        updated = null;
        if (!Matches(glyphClasses) || !Matches(markAttachmentClasses))
            return ReadWire.Fail(ReadStatus.InvalidSource, DiagnosticCode.WrongSourceFace,
                Location.AtLocal(DataField.None, 0), out diagnostic);
        if (glyphClasses is not null)
        {
            if (!budget.Work(glyphClasses.Ranges.Length, glyphClasses.Location, out diagnostic))
                return ReadStatus.ResourceLimit;
            for (int i = 0; i < glyphClasses.Ranges.Length; i++)
            {
                var range = glyphClasses.Ranges[i];
                if (range.ClassValue > 4)
                {
                    ulong at = glyphClasses.Format == 2
                        ? ClassDefLayout.SparseRangesOffset + (ulong)i * ClassDefLayout.RangeStride + ClassDefLayout.RangeClassOffset
                        : ClassDefLayout.DenseValuesOffset + (ulong)(range.StartGlyph - glyphClasses.Ranges[0].StartGlyph) * ClassDefLayout.ValueStride;
                    return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                        glyphClasses.Location.AtLocal(DataField.Payload, at, i), out diagnostic, range.ClassValue, 4);
                }
            }
        }
        updated = new(Version, NumGlyphs, glyphClasses, markAttachmentClasses,
            AttachListOffset, LigCaretListOffset, MarkGlyphSetsDefOffset, ItemVariationStoreOffset, Location);
        diagnostic = default;
        return ReadStatus.Done;
    }

    private bool Matches(ClassDefTable? model) => model is null ||
        model.NumGlyphs == NumGlyphs && model.Location.SourceId == Location.SourceId &&
        model.Location.FaceIndex == Location.FaceIndex && model.Location.TableTag == Location.TableTag;
}
