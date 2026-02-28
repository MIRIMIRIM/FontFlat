using OTFontFile2;

namespace OTFontFile2.Tables;

public readonly partial struct CmapTable
{
    public readonly partial struct CmapSubtable
    {
        public bool TryCreateReverseMap(ushort numGlyphs, out CmapReverseMap reverseMap)
        {
            reverseMap = null!;

            if (numGlyphs == 0)
                return false;

            var glyphToCodePoint = new uint[numGlyphs];
            Array.Fill(glyphToCodePoint, uint.MaxValue);

            ushort format = Format;
            bool ok = format switch
            {
                4 => TryFillReverseFormat4(glyphToCodePoint),
                12 => TryFillReverseFormat12Or13(format, glyphToCodePoint),
                13 => TryFillReverseFormat12Or13(format, glyphToCodePoint),
                10 => TryFillReverseFormat10(glyphToCodePoint),
                6 => TryFillReverseFormat6(glyphToCodePoint),
                0 => TryFillReverseFormat0(glyphToCodePoint),
                _ => false
            };

            if (!ok)
                return false;

            reverseMap = new CmapReverseMap(PlatformId, EncodingId, format, numGlyphs, glyphToCodePoint);
            return true;
        }

        private bool TryFillReverseFormat0(uint[] glyphToCodePoint)
        {
            var data = _cmap.Span;
            int offset = _offset;

            // format(2) length(2) language(2) glyphIdArray[256]
            if ((uint)offset > (uint)data.Length - 262)
                return false;

            int glyphArrayOffset = offset + 6;
            for (int code = 0; code < 256; code++)
            {
                ushort glyphId = data[glyphArrayOffset + code];
                if (glyphId == 0 || glyphId >= glyphToCodePoint.Length)
                    continue;

                uint cp = (uint)code;
                if (cp < glyphToCodePoint[glyphId])
                    glyphToCodePoint[glyphId] = cp;
            }

            return true;
        }

        private bool TryFillReverseFormat6(uint[] glyphToCodePoint)
        {
            var data = _cmap.Span;
            int offset = _offset;

            // format(2) length(2) language(2) firstCode(2) entryCount(2) glyphIdArray[entryCount]
            if ((uint)offset > (uint)data.Length - 10)
                return false;

            ushort firstCode = BigEndian.ReadUInt16(data, offset + 6);
            ushort entryCount = BigEndian.ReadUInt16(data, offset + 8);

            int glyphArrayOffset = offset + 10;
            int required = glyphArrayOffset + (entryCount * 2);
            if (required > data.Length)
                return false;

            for (int i = 0; i < entryCount; i++)
            {
                uint cp = (uint)(firstCode + i);
                if (!IsUnicodeScalarValue(cp))
                    continue;

                ushort glyphId = BigEndian.ReadUInt16(data, glyphArrayOffset + (i * 2));
                if (glyphId == 0 || glyphId >= glyphToCodePoint.Length)
                    continue;

                if (cp < glyphToCodePoint[glyphId])
                    glyphToCodePoint[glyphId] = cp;
            }

            return true;
        }

        private bool TryFillReverseFormat10(uint[] glyphToCodePoint)
        {
            var data = _cmap.Span;
            int offset = _offset;

            // format(2) reserved(2) length(4) language(4) startCharCode(4) numChars(4) glyphIdArray[numChars]
            if ((uint)offset > (uint)data.Length - 20)
                return false;

            uint startCharCode = BigEndian.ReadUInt32(data, offset + 12);
            uint numCharsU = BigEndian.ReadUInt32(data, offset + 16);
            if (numCharsU > int.MaxValue)
                return false;

            int numChars = (int)numCharsU;
            int glyphArrayOffset = offset + 20;
            long required = (long)glyphArrayOffset + ((long)numChars * 2);
            if (required > data.Length)
                return false;

            for (int i = 0; i < numChars; i++)
            {
                uint cp = startCharCode + (uint)i;
                if (!IsUnicodeScalarValue(cp))
                    continue;

                ushort glyphId = BigEndian.ReadUInt16(data, glyphArrayOffset + (i * 2));
                if (glyphId == 0 || glyphId >= glyphToCodePoint.Length)
                    continue;

                if (cp < glyphToCodePoint[glyphId])
                    glyphToCodePoint[glyphId] = cp;
            }

            return true;
        }

        private bool TryFillReverseFormat12Or13(ushort format, uint[] glyphToCodePoint)
        {
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

            for (int i = 0; i < nGroups; i++)
            {
                int g = groupsOffset + (i * 12);
                uint start = BigEndian.ReadUInt32(data, g);
                uint end = BigEndian.ReadUInt32(data, g + 4);
                uint value = BigEndian.ReadUInt32(data, g + 8);

                if (start > end)
                    return false;

                if (start > 0x10FFFFu || end > 0x10FFFFu)
                    return false;

                if (value > ushort.MaxValue)
                    return false;

                ushort glyphId = (ushort)value;
                if (glyphId == 0 || glyphId >= glyphToCodePoint.Length)
                {
                    if (format == 13)
                        continue;

                    // Format 12 uses sequential glyph IDs; a group starting beyond numGlyphs can't contribute.
                    if (glyphId >= glyphToCodePoint.Length)
                        continue;
                }

                if (format == 13)
                {
                    if (!IsUnicodeScalarValue(start))
                        continue;

                    if (start < glyphToCodePoint[glyphId])
                        glyphToCodePoint[glyphId] = start;

                    continue;
                }

                int startGlyphId = glyphId;
                uint span = end - start;
                int mappingCount = span >= int.MaxValue ? int.MaxValue : (int)span + 1;
                int maxByGlyph = glyphToCodePoint.Length - startGlyphId;
                if (maxByGlyph <= 0)
                    continue;

                int take = mappingCount < maxByGlyph ? mappingCount : maxByGlyph;
                for (int j = 0; j < take; j++)
                {
                    ushort gid = (ushort)(startGlyphId + j);
                    if (gid == 0)
                        continue;

                    uint cp = start + (uint)j;
                    if (!IsUnicodeScalarValue(cp))
                        continue;

                    if (cp < glyphToCodePoint[gid])
                        glyphToCodePoint[gid] = cp;
                }
            }

            return true;
        }

        private bool TryFillReverseFormat4(uint[] glyphToCodePoint)
        {
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

            int glyphBytes = subEnd - glyphArrayOffset;
            if ((glyphBytes & 1) != 0)
                return false;

            int glyphCount = glyphBytes / 2;
            var glyphIdArray = new ushort[glyphCount];
            for (int i = 0; i < glyphCount; i++)
            {
                glyphIdArray[i] = BigEndian.ReadUInt16(data, glyphArrayOffset + (i * 2));
            }

            for (int s = 0; s < segCount; s++)
            {
                ushort endCode = BigEndian.ReadUInt16(data, endCodeOffset + (s * 2));
                ushort startCode = BigEndian.ReadUInt16(data, startCodeOffset + (s * 2));
                short idDelta = BigEndian.ReadInt16(data, idDeltaOffset + (s * 2));
                ushort idRangeOffset = BigEndian.ReadUInt16(data, idRangeOffsetOffset + (s * 2));

                if (startCode > endCode)
                    return false;

                // Skip sentinel.
                if (startCode == 0xFFFF && endCode == 0xFFFF)
                    continue;

                if (idRangeOffset == 0)
                {
                    for (int code = startCode; code <= endCode; code++)
                    {
                        if (!IsUnicodeScalarValue((uint)code))
                            continue;

                        ushort gid = unchecked((ushort)(code + idDelta));
                        if (gid == 0 || gid >= glyphToCodePoint.Length)
                            continue;

                        uint cp = (uint)code;
                        if (cp < glyphToCodePoint[gid])
                            glyphToCodePoint[gid] = cp;
                    }

                    continue;
                }

                if ((idRangeOffset & 1) != 0)
                    return false;

                int baseIndex = (idRangeOffset / 2) + s - segCount;
                if (baseIndex < 0)
                    return false;

                int maxIndex = baseIndex + (endCode - startCode);
                if ((uint)maxIndex >= (uint)glyphIdArray.Length)
                    return false;

                for (int code = startCode; code <= endCode; code++)
                {
                    if (!IsUnicodeScalarValue((uint)code))
                        continue;

                    int index = baseIndex + (code - startCode);
                    ushort raw = glyphIdArray[index];
                    if (raw == 0)
                        continue;

                    ushort gid = unchecked((ushort)(raw + idDelta));
                    if (gid == 0 || gid >= glyphToCodePoint.Length)
                        continue;

                    uint cp = (uint)code;
                    if (cp < glyphToCodePoint[gid])
                        glyphToCodePoint[gid] = cp;
                }
            }

            return true;
        }

        private static bool IsUnicodeScalarValue(uint codePoint)
            => codePoint <= 0x10FFFFu && (codePoint < 0xD800u || codePoint > 0xDFFFu);
    }
}

