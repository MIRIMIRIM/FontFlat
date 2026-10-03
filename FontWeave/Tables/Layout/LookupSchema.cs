using FontWeave.Core;
using FontWeave.Schema;

namespace FontWeave.Tables.Layout;

internal static class LookupLayout
{
    internal const int HeaderLength = 6, TypeAt = 0, FlagsAt = 2, CountAt = 4, OffsetStride = 2;
    internal const ushort IgnoreMask = 0x000E, IgnoreMarks = 0x0008,
        UseMarkFilteringSet = 0x0010, ReservedMask = 0x00E0, AttachmentMask = 0xFF00;
}

[BinaryTable(0, LookupLayout.HeaderLength)]
[BinaryScalar("LookupType", WireType.UInt16, LookupLayout.TypeAt, DataField.Tag)]
[BinaryScalar("Flags", WireType.UInt16, LookupLayout.FlagsAt, DataField.Flags)]
[BinaryScalar("SubtableCount", WireType.UInt16, LookupLayout.CountAt, DataField.Count, Semantic = NumberSemantic.Count)]
[BinaryArray("SubtableOffsetRecord", "SubtableCount", LookupLayout.HeaderLength, LookupLayout.OffsetStride)]
[BinaryElement("SubtableOffsetRecord", "SubtableOffset", WireType.UInt16, 0, DataField.Offset,
    Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table, ZeroAllowed = false)]
[BinaryRecord("FilterWord", 2)]
[BinaryElement("FilterWord", "MarkFilteringSet", WireType.UInt16, 0, DataField.Count)]
public readonly ref partial struct LookupView { }
