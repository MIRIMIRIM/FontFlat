namespace OTFontFile2;

/// <summary>
/// Optional cache that accelerates cmap mapping by decoding subtable records to native-endian arrays.
/// Supports format 4 (segments + glyphIdArray) and format 12/13 (groups).
/// </summary>
public sealed class CmapFastMap
{
    private readonly ushort _format;
    private readonly Group[]? _groups;
    private readonly Format4Segment[]? _format4Segments;
    private readonly ushort[]? _format4GlyphIdArray;

    internal CmapFastMap(ushort format, Group[] groups)
    {
        _format = format;
        _groups = groups;
        _format4Segments = null;
        _format4GlyphIdArray = null;
    }

    internal CmapFastMap(Format4Segment[] segments, ushort[] glyphIdArray)
    {
        _format = 4;
        _groups = null;
        _format4Segments = segments;
        _format4GlyphIdArray = glyphIdArray;
    }

    public ushort Format => _format;

    public int GroupCount => _groups?.Length ?? 0;
    public int SegmentCount => _format4Segments?.Length ?? 0;

    public bool TryMapCodePoint(uint codePoint, out uint glyphId)
    {
        glyphId = 0;

        if (_format is 12 or 13)
        {
            var groups = _groups!;
            bool isFormat13 = _format == 13;

            int lo = 0;
            int hi = groups.Length - 1;
            while (lo <= hi)
            {
                int mid = (lo + hi) >> 1;
                ref readonly Group g = ref groups[mid];

                if (codePoint < g.StartCharCode)
                {
                    hi = mid - 1;
                    continue;
                }

                if (codePoint > g.EndCharCode)
                {
                    lo = mid + 1;
                    continue;
                }

                glyphId = isFormat13 ? g.Value : g.Value + (codePoint - g.StartCharCode);
                return true;
            }

            return true;
        }

        if (_format != 4)
            return false;

        if (codePoint > 0xFFFFu)
            return false;

        ushort code = (ushort)codePoint;
        var segments = _format4Segments!;

        int loSeg = 0;
        int hiSeg = segments.Length - 1;
        int found = -1;
        while (loSeg <= hiSeg)
        {
            int mid = (loSeg + hiSeg) >> 1;
            ushort endCode = segments[mid].EndCode;

            if (code > endCode)
            {
                loSeg = mid + 1;
                continue;
            }

            found = mid;
            hiSeg = mid - 1;
        }

        if (found < 0)
            return true; // not mapped

        ref readonly Format4Segment seg = ref segments[found];
        if (code < seg.StartCode)
            return true;

        if (seg.GlyphArrayBaseIndex < 0)
        {
            glyphId = unchecked((ushort)(code + seg.IdDelta));
            return true;
        }

        int index = seg.GlyphArrayBaseIndex + (code - seg.StartCode);
        var glyphIdArray = _format4GlyphIdArray!;
        if ((uint)index >= (uint)glyphIdArray.Length)
            return false;

        ushort raw = glyphIdArray[index];
        if (raw == 0)
            return true;

        glyphId = unchecked((ushort)(raw + seg.IdDelta));
        return true;
    }

    internal readonly struct Group
    {
        public uint StartCharCode { get; }
        public uint EndCharCode { get; }
        public uint Value { get; }

        public Group(uint startCharCode, uint endCharCode, uint value)
        {
            StartCharCode = startCharCode;
            EndCharCode = endCharCode;
            Value = value;
        }
    }

    internal readonly struct Format4Segment
    {
        public ushort StartCode { get; }
        public ushort EndCode { get; }
        public short IdDelta { get; }

        /// <summary>
        /// Index into <c>glyphIdArray</c> for <see cref="StartCode"/>, or -1 for idRangeOffset==0 (delta mapping).
        /// </summary>
        public int GlyphArrayBaseIndex { get; }

        public Format4Segment(ushort startCode, ushort endCode, short idDelta, int glyphArrayBaseIndex)
        {
            StartCode = startCode;
            EndCode = endCode;
            IdDelta = idDelta;
            GlyphArrayBaseIndex = glyphArrayBaseIndex;
        }
    }
}
