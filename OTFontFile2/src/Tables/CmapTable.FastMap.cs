namespace OTFontFile2.Tables;

public readonly partial struct CmapTable
{
    public readonly partial struct CmapSubtable
    {
        public bool TryCreateFastMap(out CmapFastMap fastMap)
        {
            fastMap = null!;

            ushort format = Format;
            if (format is 12 or 13)
                return TryCreateFormat12Or13FastMap(format, out fastMap);

            if (format == 4)
                return TryCreateFormat4FastMap(out fastMap);

            return false;
        }

        private bool TryCreateFormat12Or13FastMap(ushort format, out CmapFastMap fastMap)
        {
            fastMap = null!;

            var data = _cmap.Span;
            int offset = _offset;

            // format(2) reserved(2) length(4) language(4) nGroups(4) groups[n]
            if ((uint)offset > (uint)data.Length - 16)
                return false;

            uint nGroupsU = BigEndian.ReadUInt32(data, offset + 12);
            if (nGroupsU > int.MaxValue)
                return false;

            int nGroups = (int)nGroupsU;
            int groupsOffset = offset + 16;

            long required = (long)groupsOffset + ((long)nGroups * 12);
            if (required > data.Length)
                return false;

            var groups = new CmapFastMap.Group[nGroups];

            uint prevEnd = 0;
            for (int i = 0; i < nGroups; i++)
            {
                int g = groupsOffset + (i * 12);
                uint start = BigEndian.ReadUInt32(data, g);
                uint end = BigEndian.ReadUInt32(data, g + 4);
                uint value = BigEndian.ReadUInt32(data, g + 8);

                if (start > end)
                    return false;

                if (i != 0 && start <= prevEnd)
                    return false;

                groups[i] = new CmapFastMap.Group(start, end, value);
                prevEnd = end;
            }

            fastMap = new CmapFastMap(format, groups);
            return true;
        }

        private bool TryCreateFormat4FastMap(out CmapFastMap fastMap)
        {
            fastMap = null!;

            var data = _cmap.Span;
            int offset = _offset;

            // Need up to segCountX2 at offset 6 and range fields.
            if ((uint)offset > (uint)data.Length - 14)
                return false;

            ushort length = BigEndian.ReadUInt16(data, offset + 2);
            if (length < 16)
                return false;

            if ((uint)offset > (uint)data.Length - length)
                return false;

            ushort segCountX2 = BigEndian.ReadUInt16(data, offset + 6);
            if ((segCountX2 & 1) != 0)
                return false;

            int segCount = segCountX2 / 2;
            if (segCount == 0)
                return false;

            int endCodeOffset = offset + 14;
            int startCodeOffset = endCodeOffset + (segCount * 2) + 2; // reservedPad
            int idDeltaOffset = startCodeOffset + (segCount * 2);
            int idRangeOffsetOffset = idDeltaOffset + (segCount * 2);
            int glyphArrayOffset = idRangeOffsetOffset + (segCount * 2);

            int subEnd = offset + length;
            if (glyphArrayOffset > subEnd)
                return false;

            int glyphIdArrayBytes = subEnd - glyphArrayOffset;
            if ((glyphIdArrayBytes & 1) != 0)
                return false;

            int glyphIdCount = glyphIdArrayBytes / 2;
            var glyphIdArray = new ushort[glyphIdCount];
            for (int i = 0; i < glyphIdCount; i++)
            {
                glyphIdArray[i] = BigEndian.ReadUInt16(data, glyphArrayOffset + (i * 2));
            }

            var segments = new CmapFastMap.Format4Segment[segCount];

            ushort prevEndCode = 0;
            for (int s = 0; s < segCount; s++)
            {
                ushort endCode = BigEndian.ReadUInt16(data, endCodeOffset + (s * 2));
                ushort startCode = BigEndian.ReadUInt16(data, startCodeOffset + (s * 2));
                if (startCode > endCode)
                    return false;

                if (s != 0)
                {
                    if (endCode < prevEndCode)
                        return false;
                    if (startCode <= prevEndCode)
                        return false;
                }

                short idDelta = BigEndian.ReadInt16(data, idDeltaOffset + (s * 2));
                ushort idRangeOffset = BigEndian.ReadUInt16(data, idRangeOffsetOffset + (s * 2));

                int glyphArrayBaseIndex = -1;
                if (idRangeOffset != 0)
                {
                    if ((idRangeOffset & 1) != 0)
                        return false;

                    glyphArrayBaseIndex = (idRangeOffset / 2) + s - segCount;
                    if (glyphArrayBaseIndex < 0)
                        return false;

                    int maxIndex = glyphArrayBaseIndex + (endCode - startCode);
                    if ((uint)maxIndex >= (uint)glyphIdArray.Length)
                        return false;
                }

                segments[s] = new CmapFastMap.Format4Segment(startCode, endCode, idDelta, glyphArrayBaseIndex);
                prevEndCode = endCode;
            }

            fastMap = new CmapFastMap(segments, glyphIdArray);
            return true;
        }
    }
}
