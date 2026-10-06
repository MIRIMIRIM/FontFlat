using FontWeave.Core;

namespace FontWeave.Tables.Layout;

/// <summary>Owned whole-GDEF slice (static T4-02): versions 1.0/1.2/1.3 with every
/// non-store field typed. The variation store stays a raw observation: its offset is
/// carried untraversed and unvalidated (hard prerequisite T5-01, OQ-T4-02-5).</summary>
public sealed class GdefTable
{
    internal GdefTable(uint version, int numGlyphs, ClassDefTable? glyphClasses,
        ClassDefTable? markAttachmentClasses, MarkGlyphSetsTable? markGlyphSets,
        AttachListTable? attachList, LigCaretListTable? ligCaretList,
        uint itemVariationStoreOffset, DataLocation location, int consumedLength)
    {
        Version = version;
        NumGlyphs = numGlyphs;
        GlyphClasses = glyphClasses;
        MarkAttachmentClasses = markAttachmentClasses;
        MarkGlyphSets = markGlyphSets;
        AttachList = attachList;
        LigCaretList = ligCaretList;
        ItemVariationStoreOffset = itemVariationStoreOffset;
        Location = location;
        ConsumedLength = consumedLength;
    }

    public uint Version { get; }
    public int NumGlyphs { get; }
    public ClassDefTable? GlyphClasses { get; }
    public ClassDefTable? MarkAttachmentClasses { get; }
    public MarkGlyphSetsTable? MarkGlyphSets { get; }
    public AttachListTable? AttachList { get; }
    public LigCaretListTable? LigCaretList { get; }
    /// <summary>Raw ItemVariationStore header value under its v1.3 existence domain;
    /// zero for earlier versions. Never traversed on the static path.</summary>
    public uint ItemVariationStoreOffset { get; }
    public DataLocation Location { get; }
    public int ConsumedLength { get; }
}
