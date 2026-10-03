using FontWeave.Core;
using FontWeave.Schema;

namespace FontWeave.Tables.Layout;

internal static class GdefLayout
{
    internal const uint Tag = 0x47444546, Version10 = 0x00010000,
        Version12 = 0x00010002, Version13 = 0x00010003;
    internal const int Header10 = 12, Header12 = 14, Header13 = 18,
        GlyphClassesAt = 4, AttachListAt = 6, LigCaretListAt = 8,
        MarkAttachmentClassesAt = 10, MarkSetsAt = 12, VariationStoreAt = 14;
}

[BinaryTable(GdefLayout.Tag, GdefLayout.Header10, VersionField = "Version",
    SupportedVersions = new uint[] { GdefLayout.Version10, GdefLayout.Version12, GdefLayout.Version13 })]
[BinaryScalar("Version", WireType.UInt32, 0, DataField.Version)]
[BinaryScalar("GlyphClassDefOffset", WireType.UInt16, GdefLayout.GlyphClassesAt, DataField.Offset)]
[BinaryScalar("AttachListOffset", WireType.UInt16, GdefLayout.AttachListAt, DataField.Offset)]
[BinaryScalar("LigCaretListOffset", WireType.UInt16, GdefLayout.LigCaretListAt, DataField.Offset)]
[BinaryScalar("MarkAttachClassDefOffset", WireType.UInt16, GdefLayout.MarkAttachmentClassesAt, DataField.Offset)]
[BinaryRecord("MarkSetsHeader", 2)]
[BinaryElement("MarkSetsHeader", "MarkGlyphSetsDefOffset", WireType.UInt16, 0, DataField.Offset)]
[BinaryRecord("VariationStoreHeader", 4)]
[BinaryElement("VariationStoreHeader", "ItemVariationStoreOffset", WireType.UInt32, 0, DataField.Offset)]
public readonly ref partial struct GdefView { }
