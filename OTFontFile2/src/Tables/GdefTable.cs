using OTFontFile2.SourceGen;

namespace OTFontFile2.Tables;

[OtTable("GDEF", 12)]
[OtField("Version", OtFieldKind.Fixed1616, 0)]
[OtField("GlyphClassDefOffset", OtFieldKind.UInt16, 4)]
[OtField("AttachListOffset", OtFieldKind.UInt16, 6)]
[OtField("LigCaretListOffset", OtFieldKind.UInt16, 8)]
[OtField("MarkAttachClassDefOffset", OtFieldKind.UInt16, 10)]
[OtSubTableOffset("GlyphClassDef", nameof(GlyphClassDefOffset), typeof(ClassDefTable), OutParameterName = "classDef")]
[OtSubTableOffset("MarkAttachClassDef", nameof(MarkAttachClassDefOffset), typeof(ClassDefTable), OutParameterName = "classDef")]
[OtSubTableOffset("AttachList", nameof(AttachListOffset), typeof(GdefAttachListTable))]
[OtSubTableOffset("LigCaretList", nameof(LigCaretListOffset), typeof(GdefLigCaretListTable))]
public readonly partial struct GdefTable
{
    public ushort MarkGlyphSetsDefOffset
    {
        get
        {
            if (Version.RawValue <= 0x00010000u)
                return 0;

            return _table.Length >= 14 ? BigEndian.ReadUInt16(_table.Span, 12) : (ushort)0;
        }
    }

    public uint ItemVarStoreOffset
    {
        get
        {
            if (Version.RawValue < 0x00010003u)
                return 0;

            return _table.Length >= 18 ? BigEndian.ReadUInt32(_table.Span, 14) : 0;
        }
    }

    public bool TryGetMarkGlyphSetsDef(out GdefMarkGlyphSetsDefTable markGlyphSetsDef)
    {
        markGlyphSetsDef = default;

        int offset = MarkGlyphSetsDefOffset;
        if (offset == 0)
            return false;

        return GdefMarkGlyphSetsDefTable.TryCreate(_table, offset, out markGlyphSetsDef);
    }

    public bool TryGetItemVariationStore(out ItemVariationStore store)
    {
        store = default;

        uint offsetU = ItemVarStoreOffset;
        if (offsetU == 0)
            return false;
        if (offsetU > int.MaxValue)
            return false;

        return ItemVariationStore.TryCreate(_table, (int)offsetU, out store);
    }
}
