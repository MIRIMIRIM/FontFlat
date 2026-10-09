using FontWeave.Core;
using FontWeave.Schema;

namespace FontWeave.Tables;

/// <summary>Mechanical CFF2 wire constants (OpenType 1.9.1 chapter "CFF2", the
/// 43464632.md snapshot). The header is exactly five bytes; the GlobalSubrINDEX
/// position is a runtime derivation from two header field values, not a declared
/// constant (card T5-05 §2).</summary>
internal static class Cff2Layout
{
    internal const uint Tag = 0x43464632u; // 'CFF2'
    internal const int HeaderLength = 5;
    internal const int MinorVersionAt = 1;
    internal const int HeaderSizeAt = 2;
    internal const int TopDictSizeAt = 3;
    internal const byte MajorVersionValue = 2;
}

// CFF2 2.0 (OT 1.9.1): the five-byte header is the whole declared face - u8
// majorVersion ("set to 2", L303), u8 minorVersion ("set to 0", L304), u8
// headerSize ("set to 5", L305) and u16 topDictSize (L306). The generator owns
// storage, binding and the mechanical version gate; everything past the header
// (TopDICT, the four INDEX families, FDSelect, the DICT pair, charstrings, the
// variation store) is codec-level variable-length structure outside the flat
// Schema expressiveness, so per the T5-03 OQ-2 discipline it lives in the same
// readonly ref partial struct as hand-written walks (card T5-05 §2).
//
// minorVersion is declared raw and hard-refused at validation (OQ-T5-05-2 lean:
// "set to 0" with no 2.1 definition in the frozen 1.9.1 spec). headerSize is
// declared raw and is deliberately NOT pinned to ==5 (OQ-T5-05-3 lean: L308
// "The headerSize field must be used when locating the start of the TopDICT" is
// explicit forward-compatibility intent; the read side addresses the TopDICT
// through it and the write side always emits 5).
[BinaryTable(Cff2Layout.Tag, Cff2Layout.HeaderLength, ContractVersion = 1,
    VersionField = "MajorVersion", SupportedVersions = new uint[] { Cff2Layout.MajorVersionValue })]
[BinaryScalar("MajorVersion", WireType.UInt8, 0, DataField.Version)]
[BinaryScalar("MinorVersion", WireType.UInt8, Cff2Layout.MinorVersionAt, DataField.Payload)]
[BinaryScalar("HeaderSize", WireType.UInt8, Cff2Layout.HeaderSizeAt, DataField.Payload)]
[BinaryScalar("TopDictSize", WireType.UInt16, Cff2Layout.TopDictSizeAt, DataField.Length)]
// The GlobalSubrINDEX follows the TopDICT: "headerSize + topDictSize" (L310). Both
// addends are header field values, so the position is a runtime derivation - the
// declared BinaryDerived face resolves Base and Count through their scalar getters
// with Stride 1 (prechecked at commit time; no DSL extension).
[BinaryDerived("GlobalSubrsAt", Base = "HeaderSize", Count = "TopDictSize", Stride = 1)]
public readonly ref partial struct Cff2View { }
