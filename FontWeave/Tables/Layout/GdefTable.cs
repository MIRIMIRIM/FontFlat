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

    /// <summary>Replaces the two class consumers without substituting a model from
    /// another source face or glyph-count context; the root itself is not rebuilt.</summary>
    public ReadStatus TryWithClasses(ClassDefTable? glyphClasses, ClassDefTable? markAttachmentClasses,
        out GdefTable? updated, out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        updated = null;
        if (!Matches(glyphClasses) || !Matches(markAttachmentClasses))
            return ReadWire.Fail(ReadStatus.InvalidSource, DiagnosticCode.WrongSourceFace,
                Location.AtLocal(DataField.None, 0), out diagnostic);
        updated = new(Version, NumGlyphs, glyphClasses, markAttachmentClasses, MarkGlyphSets,
            AttachList, LigCaretList, ItemVariationStoreOffset, Location, ConsumedLength);
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>Replaces the MarkGlyphSets consumer under the same source guards.</summary>
    public ReadStatus TryWithMarkGlyphSets(MarkGlyphSetsTable? markGlyphSets, out GdefTable? updated,
        out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        updated = null;
        if (!Matches(markGlyphSets))
            return ReadWire.Fail(ReadStatus.InvalidSource, DiagnosticCode.WrongSourceFace,
                Location.AtLocal(DataField.None, 0), out diagnostic);
        updated = new(Version, NumGlyphs, GlyphClasses, MarkAttachmentClasses, markGlyphSets,
            AttachList, LigCaretList, ItemVariationStoreOffset, Location, ConsumedLength);
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>Replaces one glyph's AttachPoint list through the attach owner; the
    /// source context and numGlyphs are preserved (factory parameters stay explicit).</summary>
    public ReadStatus TryWithAttachPoints(int glyphIndex, ReadOnlySpan<ushort> points,
        out GdefTable? updated, out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        updated = null;
        if (AttachList is null)
            return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
                Location.AtLocal(DataField.Offset, GdefLayout.AttachListAt), out diagnostic, 0, 1);
        ReadStatus status = AttachListTableCompiler.TryWithPoints(AttachList, glyphIndex, points,
            out var attach, out diagnostic, limits);
        if (status != ReadStatus.Done) return status;
        updated = new(Version, NumGlyphs, GlyphClasses, MarkAttachmentClasses, MarkGlyphSets,
            attach, LigCaretList, ItemVariationStoreOffset, Location, ConsumedLength);
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>Replaces one ligated glyph's caret list through the ligcaret owner;
    /// entries are discriminated records (formats 1/2/3, OQ-T4-02-3).</summary>
    public ReadStatus TryWithCarets(int ligIndex, ReadOnlySpan<CaretValueEntry> carets,
        out GdefTable? updated, out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        updated = null;
        if (LigCaretList is null)
            return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
                Location.AtLocal(DataField.Offset, GdefLayout.LigCaretListAt), out diagnostic, 0, 1);
        ReadStatus status = LigCaretListTableCompiler.TryWithCarets(LigCaretList, ligIndex, carets,
            out var lig, out diagnostic, limits);
        if (status != ReadStatus.Done) return status;
        updated = new(Version, NumGlyphs, GlyphClasses, MarkAttachmentClasses, MarkGlyphSets,
            AttachList, lig, ItemVariationStoreOffset, Location, ConsumedLength);
        diagnostic = default;
        return ReadStatus.Done;
    }

    private bool Matches(ClassDefTable? model) => model is null ||
        model.NumGlyphs == NumGlyphs && model.Location.SourceId == Location.SourceId &&
        model.Location.FaceIndex == Location.FaceIndex && model.Location.TableTag == Location.TableTag;

    private bool Matches(MarkGlyphSetsTable? model) => model is null ||
        model.NumGlyphs == NumGlyphs && model.Location.SourceId == Location.SourceId &&
        model.Location.FaceIndex == Location.FaceIndex && model.Location.TableTag == Location.TableTag;
}
