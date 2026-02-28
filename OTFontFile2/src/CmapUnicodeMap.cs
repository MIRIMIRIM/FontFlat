using OTFontFile2.Tables;

namespace OTFontFile2;

/// <summary>
/// Convenience wrapper that selects a Unicode cmap subtable (and optional UVS/format 14 subtable)
/// and provides mapping helpers without re-scanning encoding records.
/// </summary>
public readonly struct CmapUnicodeMap
{
    private readonly CmapTable.CmapSubtable _base;
    private readonly CmapTable.CmapSubtable.Format14Subtable _format14;
    private readonly bool _hasFormat14;

    private CmapUnicodeMap(CmapTable.CmapSubtable baseSubtable, bool hasFormat14, CmapTable.CmapSubtable.Format14Subtable format14)
    {
        _base = baseSubtable;
        _hasFormat14 = hasFormat14;
        _format14 = format14;
    }

    public ushort PlatformId => _base.PlatformId;
    public ushort EncodingId => _base.EncodingId;
    public ushort Format => _base.Format;

    public bool HasVariationSequences => _hasFormat14;

    public bool TryCreateFastMap(out CmapFastMap fastMap)
        => _base.TryCreateFastMap(out fastMap);

    public bool TryCreateReverseMap(ushort numGlyphs, out CmapReverseMap reverseMap)
    {
        reverseMap = null!;

        if (!_base.TryCreateReverseMap(numGlyphs, out reverseMap))
            return false;

        if (!_hasFormat14)
            return true;

        uint recordCount = _format14.VarSelectorRecordCount;
        if (recordCount > int.MaxValue)
            return false;

        for (int i = 0; i < (int)recordCount; i++)
        {
            if (!_format14.TryGetVarSelectorRecord(i, out var rec))
                return false;

            if (!rec.TryGetNonDefaultUvsTable(out var nd))
                continue;

            uint mappingCount = nd.UvsMappingCount;
            if (mappingCount > int.MaxValue)
                return false;

            uint variationSelector = rec.VarSelector;
            for (int m = 0; m < (int)mappingCount; m++)
            {
                if (!nd.TryGetMapping(m, out var mapping))
                    return false;

                uint unicodeValue = mapping.UnicodeValue;
                if (!IsUnicodeScalarValue(unicodeValue))
                    continue;

                ushort glyphId = mapping.GlyphId;
                if (glyphId == 0 || glyphId >= numGlyphs)
                    continue;

                reverseMap.AddNonDefaultUvsMapping(glyphId, unicodeValue, variationSelector);
            }
        }

        return true;
    }

    public static bool TryCreate(SfntFont font, out CmapUnicodeMap map)
    {
        map = default;
        return font.TryGetCmap(out var cmap) && TryCreate(cmap, out map);
    }

    public static bool TryCreate(CmapTable cmap, out CmapUnicodeMap map)
    {
        map = default;

        bool hasBase = false;
        CmapTable.CmapSubtable baseSubtable = default;
        int bestScore = int.MinValue;

        int recordCount = cmap.EncodingRecordCount;
        for (int i = 0; i < recordCount; i++)
        {
            if (!cmap.TryGetEncodingRecord(i, out var record))
                continue;

            if (!IsUnicodeEncoding(record.PlatformId, record.EncodingId))
                continue;

            if (!cmap.TryGetSubtable(record, out var subtable))
                continue;

            ushort format = subtable.Format;
            if (format == 14)
                continue;

            int score = ScoreUnicodeSubtable(record.PlatformId, record.EncodingId, format);
            if (score <= bestScore)
                continue;

            bestScore = score;
            baseSubtable = subtable;
            hasBase = true;
        }

        if (!hasBase)
            return false;

        bool hasFormat14 = TryGetFormat14(cmap, out var format14);
        map = new CmapUnicodeMap(baseSubtable, hasFormat14, format14);
        return true;
    }

    public bool TryMapCodePoint(uint codePoint, out uint glyphId)
        => _base.TryMapCodePoint(codePoint, out glyphId);

    /// <summary>
    /// Maps a Unicode Variation Sequence (UVS). If the font has a cmap format 14 subtable and it
    /// provides a non-default mapping for the sequence, that glyph is returned. Otherwise this
    /// falls back to the base cmap mapping (equivalent to ignoring the variation selector).
    /// </summary>
    public bool TryMapVariationSequence(uint unicodeValue, uint variationSelector, out uint glyphId)
    {
        glyphId = 0;

        if (variationSelector == 0)
            return _base.TryMapCodePoint(unicodeValue, out glyphId);

        if (_hasFormat14)
        {
            if (_format14.TryGetNonDefaultGlyphId(unicodeValue, variationSelector, out ushort uvsGlyph))
            {
                glyphId = uvsGlyph;
                return true;
            }

            if (_format14.IsDefaultVariationSequence(unicodeValue, variationSelector))
                return _base.TryMapCodePoint(unicodeValue, out glyphId);
        }

        return _base.TryMapCodePoint(unicodeValue, out glyphId);
    }

    private static bool TryGetFormat14(CmapTable cmap, out CmapTable.CmapSubtable.Format14Subtable format14)
    {
        format14 = default;

        // Preferred location: platform 0 / encoding 5.
        if (cmap.TryGetSubtable(platformId: 0, encodingId: 5, out var sub) && sub.TryGetFormat14(out format14))
            return true;

        // Fallback: first format 14 subtable found.
        int recordCount = cmap.EncodingRecordCount;
        for (int i = 0; i < recordCount; i++)
        {
            if (!cmap.TryGetEncodingRecord(i, out var record))
                continue;

            if (!cmap.TryGetSubtable(record, out var s))
                continue;

            if (s.TryGetFormat14(out format14))
                return true;
        }

        return false;
    }

    private static bool IsUnicodeEncoding(ushort platformId, ushort encodingId)
    {
        if (platformId == 0)
            return encodingId != 5; // encoding 5 is UVS-only (format 14)

        // Windows Unicode
        return platformId == 3 && (encodingId == 1 || encodingId == 10);
    }

    private static bool IsUnicodeScalarValue(uint codePoint)
        => codePoint <= 0x10FFFFu && (codePoint < 0xD800u || codePoint > 0xDFFFu);

    private static int ScoreUnicodeSubtable(ushort platformId, ushort encodingId, ushort format)
    {
        // Higher is better.
        int score = 0;

        // Prefer Unicode platform 0 over Windows platform 3.
        score += platformId == 0 ? 1000 : 900;

        // Prefer "full repertoire" encodings when available.
        if (platformId == 0 && (encodingId == 4 || encodingId == 6))
            score += 20;
        if (platformId == 3 && encodingId == 10)
            score += 20;

        // Prefer 32-bit capable formats.
        score += format switch
        {
            12 => 300,
            13 => 290,
            10 => 280,
            4 => 200,
            8 => 190,
            6 => 160,
            2 => 150,
            0 => 100,
            _ => 0
        };

        return score;
    }
}
