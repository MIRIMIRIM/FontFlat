using FontWeave.Core;
using FontWeave.Schema;

namespace FontWeave.Tables;

internal static class CmapFormat8Layout
{
    internal const int HeaderLength = 8208, GroupStride = 12;
    internal const int Is32Offset = 12, Is32Length = 8192, NumGroupsOffset = 8204;

    /// <summary>is32 bit for one 16-bit value: byte value/8, bit 1 &lt;&lt; (7 - value%8).</summary>
    internal static bool GetIs32Bit(ReadOnlySpan<byte> is32, uint wordIndex)
    {
        uint byteIndex = wordIndex >> 3;
        return byteIndex < (uint)is32.Length &&
            (is32[(int)byteIndex] & (1 << (7 - (int)(wordIndex & 7)))) != 0;
    }
}

// The is32 bitmap at 12..8204 has no record semantics and is intentionally absent from the
// schema; the handwritten partial reads it with bounded accessors on the same single View.
[BinaryTable(CmapLayout.Tag, CmapFormat8Layout.HeaderLength, VersionField = "Format", SupportedVersions = new uint[] { 8 })]
[BinaryScalar("Format", WireType.UInt16, 0, DataField.Version)]
[BinaryScalar("Reserved", WireType.UInt16, 2, DataField.Payload)]
[BinaryScalar("Length", WireType.UInt32, 4, DataField.Length)]
[BinaryScalar("Language", WireType.UInt32, 8, DataField.LanguageId)]
[BinaryScalar("NumGroups", WireType.UInt32, CmapFormat8Layout.NumGroupsOffset, DataField.Count, Semantic = NumberSemantic.Count)]
[BinaryRecord("Group", CmapFormat8Layout.GroupStride)]
[BinaryElement("Group", "StartCharCode", WireType.UInt32, 0, DataField.CharCode)]
[BinaryElement("Group", "EndCharCode", WireType.UInt32, 4, DataField.CharCode)]
[BinaryElement("Group", "StartGlyphID", WireType.UInt32, 8, DataField.GlyphId)]
public readonly ref partial struct CmapFormat8View { }
