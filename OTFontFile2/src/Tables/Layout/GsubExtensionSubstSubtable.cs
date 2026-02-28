using OTFontFile2.SourceGen;

namespace OTFontFile2.Tables;

[OtSubTable(8)]
[OtField("SubstFormat", OtFieldKind.UInt16, 0)]
[OtField("ExtensionLookupType", OtFieldKind.UInt16, 2)]
[OtField("ExtensionOffset", OtFieldKind.UInt32, 4)]
public readonly partial struct GsubExtensionSubstSubtable
{
    public bool TryResolve(out ushort lookupType, out int subtableOffset)
    {
        lookupType = ExtensionLookupType;
        subtableOffset = 0;

        uint rel = ExtensionOffset;
        if (rel > int.MaxValue)
            return false;

        int offset = _offset + (int)rel;
        if ((uint)offset >= (uint)_table.Length)
            return false;

        subtableOffset = offset;
        return true;
    }
}
