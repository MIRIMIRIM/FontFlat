using OTFontFile2.Tables;

namespace OTFontFile2;

/// <summary>
/// Optional cache that provides a best-effort reverse mapping (glyphId -&gt; Unicode code point),
/// and optionally non-default UVS mappings (glyphId -&gt; (unicodeValue, variationSelector)).
/// </summary>
public sealed class CmapReverseMap
{
    private readonly ushort _platformId;
    private readonly ushort _encodingId;
    private readonly ushort _format;
    private readonly ushort _numGlyphs;
    private readonly uint[] _glyphToCodePoint;

    // Packed as: (unicodeValue << 32) | variationSelector. ulong.MaxValue means "none".
    private ulong[]? _glyphToNonDefaultUvs;

    internal CmapReverseMap(ushort platformId, ushort encodingId, ushort format, ushort numGlyphs, uint[] glyphToCodePoint)
    {
        _platformId = platformId;
        _encodingId = encodingId;
        _format = format;
        _numGlyphs = numGlyphs;
        _glyphToCodePoint = glyphToCodePoint;
    }

    public ushort PlatformId => _platformId;
    public ushort EncodingId => _encodingId;
    public ushort Format => _format;
    public ushort NumGlyphs => _numGlyphs;

    public bool HasNonDefaultUvs => _glyphToNonDefaultUvs is not null;

    public bool TryGetCodePoint(ushort glyphId, out uint codePoint)
    {
        codePoint = 0;

        if (glyphId >= _numGlyphs)
            return false;

        uint value = _glyphToCodePoint[glyphId];
        if (value == uint.MaxValue)
            return false;

        codePoint = value;
        return true;
    }

    public bool TryGetNonDefaultVariationSequence(ushort glyphId, out uint unicodeValue, out uint variationSelector)
    {
        unicodeValue = 0;
        variationSelector = 0;

        if (_glyphToNonDefaultUvs is null)
            return false;

        if (glyphId >= _numGlyphs)
            return false;

        ulong packed = _glyphToNonDefaultUvs[glyphId];
        if (packed == ulong.MaxValue)
            return false;

        unicodeValue = (uint)(packed >> 32);
        variationSelector = (uint)packed;
        return true;
    }

    internal void AddNonDefaultUvsMapping(ushort glyphId, uint unicodeValue, uint variationSelector)
    {
        if (glyphId == 0 || glyphId >= _numGlyphs)
            return;

        EnsureNonDefaultUvs();

        ref ulong slot = ref _glyphToNonDefaultUvs![glyphId];
        if (slot != ulong.MaxValue)
            return; // keep first mapping

        slot = ((ulong)unicodeValue << 32) | variationSelector;
    }

    private void EnsureNonDefaultUvs()
    {
        if (_glyphToNonDefaultUvs is not null)
            return;

        var arr = new ulong[_glyphToCodePoint.Length];
        Array.Fill(arr, ulong.MaxValue);
        _glyphToNonDefaultUvs = arr;
    }

    public static bool TryCreate(SfntFont font, out CmapReverseMap reverseMap)
    {
        reverseMap = null!;

        if (!font.TryGetMaxp(out var maxp))
            return false;

        if (!CmapUnicodeMap.TryCreate(font, out var unicodeMap))
            return false;

        return unicodeMap.TryCreateReverseMap(maxp.NumGlyphs, out reverseMap);
    }
}

