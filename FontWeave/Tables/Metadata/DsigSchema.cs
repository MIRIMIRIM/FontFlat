using FontWeave.Core;
using FontWeave.Schema;

namespace FontWeave.Tables;

internal static class DsigLayout
{
    internal const uint Tag = 0x44534947;
    internal const int HeaderLength = 8, RecordStride = 12, BlockHeaderLength = 8;
}

[BinaryTable(DsigLayout.Tag, DsigLayout.HeaderLength, VersionField = "Version", SupportedVersions = new uint[] { 1 })]
[BinaryScalar("Version", WireType.UInt32, 0, DataField.Version)]
[BinaryScalar("NumSignatures", WireType.UInt16, 4, DataField.Count, Semantic = NumberSemantic.Count)]
[BinaryScalar("Flags", WireType.UInt16, 6, DataField.Flags)]
[BinaryDerived("DirectoryEnd", Constant = DsigLayout.HeaderLength, Count = "NumSignatures", Stride = DsigLayout.RecordStride)]
[BinaryArray("Signature", "NumSignatures", DsigLayout.HeaderLength, DsigLayout.RecordStride)]
[BinaryElement("Signature", "Format", WireType.UInt32, 0, DataField.Version)]
[BinaryElement("Signature", "Length", WireType.UInt32, 4, DataField.Length, Semantic = NumberSemantic.Count)]
[BinaryElement("Signature", "Offset", WireType.UInt32, 8, DataField.Offset, Semantic = NumberSemantic.Offset, Origin = WireOrigin.Table)]
[BinaryPayload("Signature", "Offset", "Length", "DirectoryEnd")]
[BinaryRecord("SignatureBlockHeader", DsigLayout.BlockHeaderLength)]
[BinaryElement("SignatureBlockHeader", "Reserved1", WireType.UInt16, 0, DataField.None)]
[BinaryElement("SignatureBlockHeader", "Reserved2", WireType.UInt16, 2, DataField.None)]
[BinaryElement("SignatureBlockHeader", "SignatureLength", WireType.UInt32, 4, DataField.Length, Semantic = NumberSemantic.Count)]
public readonly ref partial struct DsigView { }
