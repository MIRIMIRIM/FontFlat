using FontWeave.Core;

namespace FontWeave.Tables.Layout;

// The GSUB family root layout constants. The value equals SingleSubstLayout.Tag; the
// leaf views declared by T4-03 name this family constant for the same reason GDEF
// children name GdefLayout.Tag. The root header surface itself is LayoutListsView
// (the declared GSUB/GPOS root: identical v1.0 header layout, version tree and the
// three Offset16 slots, whose generated writers and slot fixup helpers the root
// writer consumes); no second root view exists, so the read face and the write face
// stay one structure definition.
internal static class GsubLayout
{
    internal const uint Tag = 0x47535542;
    internal const uint Version10 = 0x00010000, Version11 = 0x00010001;
    internal const int Header10 = 10, Header11 = 14;
    internal const int ScriptListOffsetField = 4, FeatureListOffsetField = 6,
        LookupListOffsetField = 8, VariationsOffsetField = 10;
}
