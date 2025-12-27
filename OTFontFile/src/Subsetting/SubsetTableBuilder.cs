using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OTFontFile.Subsetting;

/// <summary>
/// Builds subset tables from an original font.
/// </summary>
internal class SubsetTableBuilder
{
    private readonly OTFont _sourceFont;
    private readonly IReadOnlySet<int> _retainedGlyphs;
    private readonly IReadOnlyDictionary<int, int> _glyphIdMap;
    private readonly SubsetOptions _options;

    public SubsetTableBuilder(
        OTFont sourceFont,
        IReadOnlySet<int> retainedGlyphs,
        IReadOnlyDictionary<int, int> glyphIdMap,
        SubsetOptions options)
    {
        _sourceFont = sourceFont;
        _retainedGlyphs = retainedGlyphs;
        _glyphIdMap = glyphIdMap;
        _options = options;
    }

    /// <summary>
    /// Build the subset glyf table containing only retained glyphs.
    /// Returns both glyf and loca tables as they are interdependent.
    /// </summary>
    public (Table_glyf? glyf, Table_loca? loca, ushort newNumGlyphs) BuildGlyfLoca()
    {
        var sourceGlyf = _sourceFont.GetTable("glyf") as Table_glyf;
        var sourceLoca = _sourceFont.GetTable("loca") as Table_loca;
        var sourceHead = _sourceFont.GetTable("head") as Table_head;

        if (sourceGlyf == null || sourceLoca == null || sourceHead == null)
            return (null, null, 0);

        var numSourceGlyphs = _sourceFont.GetMaxpNumGlyphs();
        var sortedNewGlyphs = _glyphIdMap.OrderBy(kv => kv.Value).ToList();
        var newNumGlyphs = (ushort)sortedNewGlyphs.Count;

        // Collect glyph data bytes
        var glyphDataList = new List<byte[]>(newNumGlyphs);
        var offsets = new List<uint>(newNumGlyphs + 1);
        uint currentOffset = 0;

        foreach (var (oldGid, newGid) in sortedNewGlyphs)
        {
            byte[] glyphData;

            if (oldGid < 0 || oldGid >= numSourceGlyphs)
            {
                // Invalid glyph - use empty
                glyphData = Array.Empty<byte>();
            }
            else if (!sourceLoca.GetEntryGlyf(oldGid, out int offset, out int length, _sourceFont))
            {
                // Failed to get entry - use empty
                glyphData = Array.Empty<byte>();
            }
            else if (length == 0)
            {
                // Empty glyph (like space)
                glyphData = Array.Empty<byte>();
            }
            else
            {
                // Copy glyph data
                var header = sourceGlyf.GetGlyphHeader((uint)oldGid, _sourceFont);
                if (header == null)
                {
                    glyphData = Array.Empty<byte>();
                }
                else
                {
                    // Get raw glyph bytes from buffer
                    glyphData = new byte[length];
                    var buffer = sourceGlyf.GetBuffer();
                    for (int i = 0; i < length; i++)
                    {
                        glyphData[i] = buffer.GetByte((uint)(offset + i));
                    }

                    // For composite glyphs, we need to remap component glyph IDs
                    if (header.numberOfContours < 0)
                    {
                        RemapCompositeGlyphIds(glyphData);
                    }
                    
                    // Pad to word boundary for better compatibility
                    glyphData = PadToWordBoundary(glyphData);
                }
            }

            offsets.Add(currentOffset);
            glyphDataList.Add(glyphData);
            currentOffset += (uint)glyphData.Length;
        }

        // Add final offset
        offsets.Add(currentOffset);

        // Determine loca format (0 = short, 1 = long)
        // Use long format if any offset > 65535*2
        short locaFormat = currentOffset > 0x1FFFE ? (short)1 : (short)0;

        // Build glyf buffer
        var glyfBuffer = new MBOBuffer(currentOffset);
        uint writeOffset = 0;
        foreach (var glyphData in glyphDataList)
        {
            for (int i = 0; i < glyphData.Length; i++)
            {
                glyfBuffer.SetByte(glyphData[i], writeOffset++);
            }
        }

        // Build loca buffer
        uint locaSize = locaFormat == 0 
            ? (uint)(offsets.Count * 2) 
            : (uint)(offsets.Count * 4);
        var locaBuffer = new MBOBuffer(locaSize);

        for (int i = 0; i < offsets.Count; i++)
        {
            if (locaFormat == 0)
            {
                // Short format: store offset / 2
                locaBuffer.SetUshort((ushort)(offsets[i] / 2), (uint)(i * 2));
            }
            else
            {
                // Long format: store actual offset
                locaBuffer.SetUint(offsets[i], (uint)(i * 4));
            }
        }

        var newGlyf = new Table_glyf("glyf", glyfBuffer);
        var newLoca = new Table_loca("loca", locaBuffer);

        return (newGlyf, newLoca, newNumGlyphs);
    }

    /// <summary>
    /// Remap glyph IDs within composite glyph data.
    /// </summary>
    private void RemapCompositeGlyphIds(byte[] glyphData)
    {
        // Composite glyph structure:
        // Header: numberOfContours (2), xMin (2), yMin (2), xMax (2), yMax (2) = 10 bytes
        // Components: flags (2), glyphIndex (2), arguments (variable), transform (variable)

        int offset = 10; // Skip header

        while (offset < glyphData.Length - 4)
        {
            // Read flags
            ushort flags = (ushort)((glyphData[offset] << 8) | glyphData[offset + 1]);

            // Read glyph index
            ushort oldGlyphIndex = (ushort)((glyphData[offset + 2] << 8) | glyphData[offset + 3]);

            // Remap if possible
            if (_glyphIdMap.TryGetValue(oldGlyphIndex, out int newGlyphIndex))
            {
                glyphData[offset + 2] = (byte)(newGlyphIndex >> 8);
                glyphData[offset + 3] = (byte)(newGlyphIndex & 0xFF);
            }
            // else: component glyph not in subset - this shouldn't happen if closure is correct

            // Calculate component length to find next component
            int argSize = (flags & 0x0001) != 0 ? 4 : 2; // ARG_1_AND_2_ARE_WORDS

            int transformSize = 0;
            if ((flags & 0x0008) != 0) transformSize = 2;      // WE_HAVE_A_SCALE
            else if ((flags & 0x0040) != 0) transformSize = 4; // WE_HAVE_AN_X_AND_Y_SCALE
            else if ((flags & 0x0080) != 0) transformSize = 8; // WE_HAVE_A_TWO_BY_TWO

            offset += 4 + argSize + transformSize;

            // Check if more components
            if ((flags & 0x0020) == 0) // MORE_COMPONENTS
                break;
        }
    }

    /// <summary>
    /// Build subset hmtx table with only retained glyphs.
    /// </summary>
    public Table_hmtx? BuildHmtx()
    {
        var sourceHmtx = _sourceFont.GetTable("hmtx") as Table_hmtx;
        var sourceHhea = _sourceFont.GetTable("hhea") as Table_hhea;

        if (sourceHmtx == null || sourceHhea == null)
            return null;

        var sortedNewGlyphs = _glyphIdMap.OrderBy(kv => kv.Value).ToList();
        var newNumGlyphs = (ushort)sortedNewGlyphs.Count;

        // For simplicity, store all glyphs as full longHorMetric entries (4 bytes each)
        // A more optimized version could compress trailing entries with same advanceWidth
        var bufferSize = (uint)(newNumGlyphs * 4);
        var buffer = new MBOBuffer(bufferSize);

        uint offset = 0;
        foreach (var (oldGid, newGid) in sortedNewGlyphs)
        {
            var metric = sourceHmtx.GetOrMakeHMetric((ushort)oldGid, _sourceFont);
            buffer.SetUshort(metric.advanceWidth, offset);
            buffer.SetShort(metric.lsb, offset + 2);
            offset += 4;
        }

        return new Table_hmtx("hmtx", buffer, sourceHhea, newNumGlyphs);
    }

    /// <summary>
    /// Build subset maxp table with updated glyph count.
    /// </summary>
    public Table_maxp? BuildMaxp(ushort newNumGlyphs)
    {
        var sourceMaxp = _sourceFont.GetTable("maxp") as Table_maxp;
        if (sourceMaxp == null)
            return null;

        var cache = (Table_maxp.maxp_cache)sourceMaxp.GetCache();
        cache.NumGlyphs = newNumGlyphs;

        return (Table_maxp)cache.GenerateTable();
    }

    /// <summary>
    /// Build subset hhea table.
    /// </summary>
    public Table_hhea? BuildHhea(ushort newNumberOfHMetrics)
    {
        var sourceHhea = _sourceFont.GetTable("hhea") as Table_hhea;
        if (sourceHhea == null)
            return null;

        var cache = (Table_hhea.hhea_cache)sourceHhea.GetCache();
        cache.numberOfHMetrics = newNumberOfHMetrics;

        return (Table_hhea)cache.GenerateTable();
    }

    /// <summary>
    /// Build subset head table with updated indexToLocFormat.
    /// </summary>
    public Table_head? BuildHead(short locaFormat)
    {
        var sourceHead = _sourceFont.GetTable("head") as Table_head;
        if (sourceHead == null)
            return null;

        var cache = (Table_head.head_cache)sourceHead.GetCache();
        cache.indexToLocFormat = locaFormat;

        // Update modified timestamp
        DateTime dt = DateTime.UtcNow;
        cache.modified = sourceHead.DateTimeToSecondsSince1904(dt);

        return (Table_head)cache.GenerateTable();
    }

    /// <summary>
    /// Build subset cmap table with only retained Unicode mappings.
    /// Creates Format 4 for BMP and Format 12 for non-BMP characters.
    /// </summary>
    public Table_cmap? BuildCmap(IReadOnlyDictionary<int, int> unicodeToNewGid)
    {
        if (unicodeToNewGid.Count == 0)
            return null;

        // Separate BMP and non-BMP mappings
        var bmpMappings = new SortedDictionary<ushort, ushort>();
        var fullMappings = new SortedDictionary<uint, uint>();
        bool hasNonBmp = false;

        foreach (var kv in unicodeToNewGid)
        {
            int unicode = kv.Key;
            int newGid = kv.Value;

            if (unicode >= 0 && unicode <= 0xFFFF)
            {
                bmpMappings[(ushort)unicode] = (ushort)newGid;
            }
            
            if (unicode >= 0)
            {
                fullMappings[(uint)unicode] = (uint)newGid;
                if (unicode > 0xFFFF)
                    hasNonBmp = true;
            }
        }

        // Calculate subtable count and sizes
        // HarfBuzz #4980 workaround: Generate both Unicode (PID=0) and Windows (PID=3) entries
        int subtableCount = hasNonBmp ? 4 : 2; // (Unicode + Windows) * (BMP + optional full)
        
        // Generate Format 4 subtable (BMP)
        byte[] format4Data = GenerateFormat4Subtable(bmpMappings);
        
        // Generate Format 12 subtable if needed
        byte[]? format12Data = hasNonBmp ? GenerateFormat12Subtable(fullMappings) : null;

        // Calculate total cmap size
        uint headerSize = 4; // version + numTables
        uint encodingEntrySize = (uint)(8 * subtableCount);
        uint format4Offset = headerSize + encodingEntrySize;
        uint format12Offset = format4Offset + (uint)format4Data.Length;
        uint totalSize = format12Offset + (uint)(format12Data?.Length ?? 0);

        // Build cmap buffer
        var buffer = new MBOBuffer(totalSize);

        // Header
        buffer.SetUshort(0, 0); // version
        buffer.SetUshort((ushort)subtableCount, 2); // numTables

        uint eteOffset = 4;

        // Encoding table entry 1: Unicode BMP (platformID=0, encodingID=3)
        buffer.SetUshort(0, eteOffset);     // platformID = Unicode
        buffer.SetUshort(3, eteOffset + 2); // encodingID = Unicode 2.0 BMP
        buffer.SetUint(format4Offset, eteOffset + 4);
        eteOffset += 8;

        // Encoding table entry 2: Windows Unicode BMP (platformID=3, encodingID=1)
        buffer.SetUshort(3, eteOffset);     // platformID = Windows
        buffer.SetUshort(1, eteOffset + 2); // encodingID = Unicode BMP
        buffer.SetUint(format4Offset, eteOffset + 4);
        eteOffset += 8;

        // Encoding table entries for Format 12 if present (non-BMP support)
        if (hasNonBmp && format12Data != null)
        {
            // Unicode full (platformID=0, encodingID=4) - HarfBuzz #4980 fix
            buffer.SetUshort(0, eteOffset);      // platformID = Unicode
            buffer.SetUshort(4, eteOffset + 2);  // encodingID = Unicode 2.0 full
            buffer.SetUint(format12Offset, eteOffset + 4);
            eteOffset += 8;

            // Windows Unicode full (platformID=3, encodingID=10)
            buffer.SetUshort(3, eteOffset);     // platformID = Windows
            buffer.SetUshort(10, eteOffset + 2); // encodingID = Unicode full
            buffer.SetUint(format12Offset, eteOffset + 4);
        }

        // Copy subtable data
        byte[] tableBuffer = buffer.GetBuffer();
        Array.Copy(format4Data, 0, tableBuffer, format4Offset, format4Data.Length);
        if (format12Data != null)
        {
            Array.Copy(format12Data, 0, tableBuffer, format12Offset, format12Data.Length);
        }

        return new Table_cmap("cmap", buffer);
    }

    private byte[] GenerateFormat4Subtable(SortedDictionary<ushort, ushort> mappings)
    {
        // Build segments for Format 4
        var segments = new List<(ushort startCode, ushort endCode, short idDelta, ushort[] glyphIds)>();
        
        if (mappings.Count == 0)
        {
            // Add terminating segment only
            segments.Add((0xFFFF, 0xFFFF, 1, Array.Empty<ushort>()));
        }
        else
        {
            var codes = mappings.Keys.ToList();
            int i = 0;
            
            while (i < codes.Count)
            {
                ushort startCode = codes[i];
                ushort endCode = startCode;
                
                // Try to extend the range
                while (i + 1 < codes.Count && codes[i + 1] == codes[i] + 1)
                {
                    i++;
                    endCode = codes[i];
                }
                
                // Check if we can use idDelta (sequential glyph IDs)
                short delta = (short)(mappings[startCode] - startCode);
                bool canUseDelta = true;
                
                for (ushort c = startCode; c <= endCode; c++)
                {
                    if ((ushort)(c + delta) != mappings[c])
                    {
                        canUseDelta = false;
                        break;
                    }
                }
                
                if (canUseDelta)
                {
                    segments.Add((startCode, endCode, delta, Array.Empty<ushort>()));
                }
                else
                {
                    // Need to use glyphIdArray
                    var glyphIds = new ushort[endCode - startCode + 1];
                    for (int j = 0; j < glyphIds.Length; j++)
                    {
                        glyphIds[j] = mappings[(ushort)(startCode + j)];
                    }
                    segments.Add((startCode, endCode, 0, glyphIds));
                }
                
                i++;
            }
            
            // Add terminating segment
            segments.Add((0xFFFF, 0xFFFF, 1, Array.Empty<ushort>()));
        }

        // Calculate size
        int segCount = segments.Count;
        int glyphIdArraySize = segments.Sum(s => s.glyphIds.Length * 2);
        int size = 14 + segCount * 8 + 2 + glyphIdArraySize; // header + segments + reservedPad + glyphIds
        
        var buffer = new MBOBuffer((uint)size);

        // Format 4 header
        ushort segCountX2 = (ushort)(segCount * 2);
        ushort searchRange = (ushort)(2 * (1 << (int)Math.Floor(Math.Log2(segCount))));
        ushort entrySelector = (ushort)Math.Floor(Math.Log2(segCount));
        ushort rangeShift = (ushort)(segCountX2 - searchRange);

        buffer.SetUshort(4, 0);             // format
        buffer.SetUshort((ushort)size, 2);  // length
        buffer.SetUshort(0, 4);             // language
        buffer.SetUshort(segCountX2, 6);
        buffer.SetUshort(searchRange, 8);
        buffer.SetUshort(entrySelector, 10);
        buffer.SetUshort(rangeShift, 12);

        // Segment arrays
        uint endCodeOffset = 14;
        uint startCodeOffset = endCodeOffset + (uint)(segCount * 2) + 2; // +2 for reservedPad
        uint idDeltaOffset = startCodeOffset + (uint)(segCount * 2);
        uint idRangeOffset = idDeltaOffset + (uint)(segCount * 2);
        uint glyphIdOffset = idRangeOffset + (uint)(segCount * 2);

        int glyphIdPos = 0;
        for (int s = 0; s < segCount; s++)
        {
            var seg = segments[s];
            buffer.SetUshort(seg.endCode, endCodeOffset + (uint)(s * 2));
            buffer.SetUshort(seg.startCode, startCodeOffset + (uint)(s * 2));
            buffer.SetShort(seg.idDelta, idDeltaOffset + (uint)(s * 2));
            
            if (seg.glyphIds.Length > 0)
            {
                // Calculate idRangeOffset
                ushort rangeOffset = (ushort)((segCount - s + glyphIdPos) * 2);
                buffer.SetUshort(rangeOffset, idRangeOffset + (uint)(s * 2));
                
                // Write glyph IDs
                foreach (var gid in seg.glyphIds)
                {
                    buffer.SetUshort(gid, glyphIdOffset + (uint)(glyphIdPos * 2));
                    glyphIdPos++;
                }
            }
            else
            {
                buffer.SetUshort(0, idRangeOffset + (uint)(s * 2));
            }
        }

        return buffer.GetBuffer();
    }

    private byte[] GenerateFormat12Subtable(SortedDictionary<uint, uint> mappings)
    {
        // Build groups for Format 12
        var groups = new List<(uint startCharCode, uint endCharCode, uint startGlyphID)>();
        
        if (mappings.Count > 0)
        {
            var codes = mappings.Keys.ToList();
            int i = 0;
            
            while (i < codes.Count)
            {
                uint startCode = codes[i];
                uint endCode = startCode;
                uint startGid = mappings[startCode];
                
                // Extend range while codes and glyph IDs are sequential
                while (i + 1 < codes.Count && 
                       codes[i + 1] == codes[i] + 1 &&
                       mappings[codes[i + 1]] == mappings[codes[i]] + 1)
                {
                    i++;
                    endCode = codes[i];
                }
                
                groups.Add((startCode, endCode, startGid));
                i++;
            }
        }

        // Calculate size: header (16) + groups (12 each)
        uint size = 16 + (uint)(groups.Count * 12);
        var buffer = new MBOBuffer(size);

        // Format 12 header
        buffer.SetUshort(12, 0);            // format
        buffer.SetUshort(0, 2);             // reserved
        buffer.SetUint(size, 4);            // length
        buffer.SetUint(0, 8);               // language
        buffer.SetUint((uint)groups.Count, 12); // numGroups

        // Groups
        uint offset = 16;
        foreach (var group in groups)
        {
            buffer.SetUint(group.startCharCode, offset);
            buffer.SetUint(group.endCharCode, offset + 4);
            buffer.SetUint(group.startGlyphID, offset + 8);
            offset += 12;
        }

        return buffer.GetBuffer();
    }

    /// <summary>
    /// Build post table version 3.0 (no glyph names).
    /// This is the safest option for subset fonts.
    /// </summary>
    public Table_post? BuildPost()
    {
        var sourcePost = _sourceFont.GetTable("post") as Table_post;
        if (sourcePost == null)
            return null;

        // Create a version 3.0 post table (32 bytes, no glyph names)
        var buffer = new MBOBuffer(32);

        // Version 3.0
        buffer.SetUint(0x00030000, 0);
        
        // Copy other fields from source
        buffer.SetUint(sourcePost.italicAngle.GetUint(), 4);
        buffer.SetShort(sourcePost.underlinePosition, 8);
        buffer.SetShort(sourcePost.underlineThickness, 10);
        buffer.SetUint(sourcePost.isFixedPitch, 12);
        buffer.SetUint(sourcePost.minMemType42, 16);
        buffer.SetUint(sourcePost.maxMemType42, 20);
        buffer.SetUint(sourcePost.minMemType1, 24);
        buffer.SetUint(sourcePost.maxMemType1, 28);

        return new Table_post("post", buffer);
    }

    /// <summary>
    /// Pad glyph data to word boundary (2 bytes).
    /// </summary>
    private static byte[] PadToWordBoundary(byte[] data)
    {
        if (data.Length % 2 == 0)
            return data;
        
        var padded = new byte[data.Length + 1];
        Array.Copy(data, padded, data.Length);
        return padded;
    }

    /// <summary>
    /// Build subset OS/2 table with updated Unicode ranges and char indices.
    /// </summary>
    public Table_OS2? BuildOS2(IEnumerable<int> retainedUnicodes)
    {
        var sourceOS2 = _sourceFont.GetTable("OS/2") as Table_OS2;
        if (sourceOS2 == null)
            return null;

        var cache = (Table_OS2.OS2_cache)sourceOS2.GetCache();
        
        // Calculate Unicode ranges from retained unicodes
        var (range1, range2, range3, range4) = CalculateUnicodeRanges(retainedUnicodes);
        
        // Prune - only keep bits that were originally set AND are still needed
        cache.ulUnicodeRange1 = sourceOS2.ulUnicodeRange1 & range1;
        cache.ulUnicodeRange2 = sourceOS2.ulUnicodeRange2 & range2;
        cache.ulUnicodeRange3 = sourceOS2.ulUnicodeRange3 & range3;
        cache.ulUnicodeRange4 = sourceOS2.ulUnicodeRange4 & range4;

        // Update usFirstCharIndex and usLastCharIndex
        ushort minChar = 0xFFFF;
        ushort maxChar = 0;
        
        foreach (var unicode in retainedUnicodes)
        {
            if (unicode >= 0 && unicode <= 0xFFFF)
            {
                if (unicode < minChar) minChar = (ushort)unicode;
                if (unicode > maxChar) maxChar = (ushort)unicode;
            }
        }
        
        if (minChar <= maxChar)
        {
            cache.usFirstCharIndex = minChar;
            cache.usLastCharIndex = maxChar;
        }

        return (Table_OS2)cache.GenerateTable();
    }

    /// <summary>
    /// Calculate Unicode range bits based on retained unicodes.
    /// Based on OpenType spec 1.9 Unicode ranges.
    /// </summary>
    private static (uint range1, uint range2, uint range3, uint range4) CalculateUnicodeRanges(IEnumerable<int> unicodes)
    {
        uint range1 = 0, range2 = 0, range3 = 0, range4 = 0;

        foreach (var u in unicodes)
        {
            int bit = GetUnicodeRangeBit(u);
            if (bit < 0) continue;
            
            if (bit < 32)
                range1 |= 1u << bit;
            else if (bit < 64)
                range2 |= 1u << (bit - 32);
            else if (bit < 96)
                range3 |= 1u << (bit - 64);
            else if (bit < 128)
                range4 |= 1u << (bit - 96);
        }

        return (range1, range2, range3, range4);
    }

    /// <summary>
    /// Get the Unicode range bit for a codepoint.
    /// Returns -1 if no range applies.
    /// </summary>
    private static int GetUnicodeRangeBit(int unicode)
    {
        // Based on OpenType spec OS/2 ulUnicodeRange
        // This is a simplified mapping - full mapping would need 128 ranges
        return unicode switch
        {
            >= 0x0000 and <= 0x007F => 0,   // Basic Latin
            >= 0x0080 and <= 0x00FF => 1,   // Latin-1 Supplement
            >= 0x0100 and <= 0x017F => 2,   // Latin Extended-A
            >= 0x0180 and <= 0x024F => 3,   // Latin Extended-B
            >= 0x0250 and <= 0x02AF => 4,   // IPA Extensions
            >= 0x02B0 and <= 0x02FF => 5,   // Spacing Modifier Letters
            >= 0x0300 and <= 0x036F => 6,   // Combining Diacritical Marks
            >= 0x0370 and <= 0x03FF => 7,   // Greek and Coptic
            >= 0x0400 and <= 0x04FF => 9,   // Cyrillic
            >= 0x0530 and <= 0x058F => 10,  // Armenian
            >= 0x0590 and <= 0x05FF => 11,  // Hebrew
            >= 0x0600 and <= 0x06FF => 13,  // Arabic
            >= 0x0900 and <= 0x097F => 15,  // Devanagari
            >= 0x0980 and <= 0x09FF => 16,  // Bengali
            >= 0x0A00 and <= 0x0A7F => 17,  // Gurmukhi
            >= 0x0A80 and <= 0x0AFF => 18,  // Gujarati
            >= 0x0B00 and <= 0x0B7F => 19,  // Oriya
            >= 0x0B80 and <= 0x0BFF => 20,  // Tamil
            >= 0x0C00 and <= 0x0C7F => 21,  // Telugu
            >= 0x0C80 and <= 0x0CFF => 22,  // Kannada
            >= 0x0D00 and <= 0x0D7F => 23,  // Malayalam
            >= 0x0E00 and <= 0x0E7F => 24,  // Thai
            >= 0x0E80 and <= 0x0EFF => 25,  // Lao
            >= 0x10A0 and <= 0x10FF => 26,  // Georgian
            >= 0x1100 and <= 0x11FF => 28,  // Hangul Jamo
            >= 0x1E00 and <= 0x1EFF => 29,  // Latin Extended Additional
            >= 0x1F00 and <= 0x1FFF => 30,  // Greek Extended
            >= 0x2000 and <= 0x206F => 31,  // General Punctuation
            >= 0x2070 and <= 0x209F => 32,  // Superscripts and Subscripts
            >= 0x20A0 and <= 0x20CF => 33,  // Currency Symbols
            >= 0x20D0 and <= 0x20FF => 34,  // Combining Diacritical Marks for Symbols
            >= 0x2100 and <= 0x214F => 35,  // Letterlike Symbols
            >= 0x2150 and <= 0x218F => 36,  // Number Forms
            >= 0x2190 and <= 0x21FF => 37,  // Arrows
            >= 0x2200 and <= 0x22FF => 38,  // Mathematical Operators
            >= 0x2300 and <= 0x23FF => 39,  // Miscellaneous Technical
            >= 0x2400 and <= 0x243F => 40,  // Control Pictures
            >= 0x2440 and <= 0x245F => 41,  // Optical Character Recognition
            >= 0x2460 and <= 0x24FF => 42,  // Enclosed Alphanumerics
            >= 0x2500 and <= 0x257F => 43,  // Box Drawing
            >= 0x2580 and <= 0x259F => 44,  // Block Elements
            >= 0x25A0 and <= 0x25FF => 45,  // Geometric Shapes
            >= 0x2600 and <= 0x26FF => 46,  // Miscellaneous Symbols
            >= 0x2700 and <= 0x27BF => 47,  // Dingbats
            >= 0x3000 and <= 0x303F => 48,  // CJK Symbols and Punctuation
            >= 0x3040 and <= 0x309F => 49,  // Hiragana
            >= 0x30A0 and <= 0x30FF => 50,  // Katakana
            >= 0x3100 and <= 0x312F => 51,  // Bopomofo
            >= 0x3130 and <= 0x318F => 52,  // Hangul Compatibility Jamo
            >= 0x3190 and <= 0x319F => 53,  // Kanbun (Bopomofo Extended in bit 53)
            >= 0x31A0 and <= 0x31BF => 51,  // Bopomofo Extended (same as Bopomofo)
            >= 0x3200 and <= 0x32FF => 54,  // Enclosed CJK Letters and Months
            >= 0x3300 and <= 0x33FF => 55,  // CJK Compatibility
            >= 0x4E00 and <= 0x9FFF => 59,  // CJK Unified Ideographs
            >= 0xAC00 and <= 0xD7AF => 56,  // Hangul Syllables
            >= 0xE000 and <= 0xF8FF => 60,  // Private Use Area
            >= 0xF900 and <= 0xFAFF => 61,  // CJK Compatibility Ideographs
            >= 0xFB00 and <= 0xFB4F => 62,  // Alphabetic Presentation Forms
            >= 0xFB50 and <= 0xFDFF => 63,  // Arabic Presentation Forms-A
            >= 0xFE20 and <= 0xFE2F => 64,  // Combining Half Marks
            >= 0xFE30 and <= 0xFE4F => 65,  // CJK Compatibility Forms
            >= 0xFE50 and <= 0xFE6F => 66,  // Small Form Variants
            >= 0xFE70 and <= 0xFEFF => 67,  // Arabic Presentation Forms-B
            >= 0xFF00 and <= 0xFFEF => 68,  // Halfwidth and Fullwidth Forms
            >= 0xFFF0 and <= 0xFFFF => 69,  // Specials
            >= 0x10000 and <= 0x1007F => 101, // Linear B Syllabary (using non-BMP bits)
            >= 0x20000 and <= 0x2A6DF => 59,  // CJK Unified Ideographs Extension B (same as CJK)
            _ => -1
        };
    }

    /// <summary>
    /// Build subset vmtx table with only retained glyphs.
    /// </summary>
    public Table_vmtx? BuildVmtx()
    {
        var sourceVmtx = _sourceFont.GetTable("vmtx") as Table_vmtx;
        var sourceVhea = _sourceFont.GetTable("vhea") as Table_vhea;

        if (sourceVmtx == null || sourceVhea == null)
            return null;

        var sortedNewGlyphs = _glyphIdMap.OrderBy(kv => kv.Value).ToList();
        var newNumGlyphs = (ushort)sortedNewGlyphs.Count;

        // For simplicity, store all glyphs as full vMetric entries (4 bytes each)
        var bufferSize = (uint)(newNumGlyphs * 4);
        var buffer = new MBOBuffer(bufferSize);

        uint offset = 0;
        foreach (var (oldGid, newGid) in sortedNewGlyphs)
        {
            var metric = sourceVmtx.GetVMetric((ushort)oldGid, _sourceFont);
            buffer.SetUshort(metric.advanceHeight, offset);
            buffer.SetShort(metric.topSideBearing, offset + 2);
            offset += 4;
        }

        return new Table_vmtx("vmtx", buffer, sourceVhea, newNumGlyphs);
    }

    /// <summary>
    /// Build subset vhea table with updated numOfLongVerMetrics.
    /// </summary>
    public Table_vhea? BuildVhea(ushort newNumberOfVMetrics)
    {
        var sourceVhea = _sourceFont.GetTable("vhea") as Table_vhea;
        if (sourceVhea == null)
            return null;

        var cache = (Table_vhea.vhea_cache)sourceVhea.GetCache();
        cache.numOfLongVerMetrics = newNumberOfVMetrics;

        return (Table_vhea)cache.GenerateTable();
    }

    /// <summary>
    /// Build subset name table with renamed font.
    /// Updates family name, unique ID, full name, and PostScript name.
    /// </summary>
    public Table_name? BuildName(string? suffix = null)
    {
        var sourceName = _sourceFont.GetTable("name") as Table_name;
        if (sourceName == null)
            return null;

        // Generate a unique suffix if not provided
        suffix ??= "_subset";

        var cache = (Table_name.name_cache)sourceName.GetCache();

        // Name IDs to modify:
        // 1 = Font Family name
        // 3 = Unique font identifier
        // 4 = Full font name
        // 6 = PostScript name

        var nameIds = new ushort[] { 1, 3, 4, 6 };

        for (ushort i = 0; i < cache.count; i++)
        {
            var nrc = cache.getNameRecord(i);
            
            foreach (var nameId in nameIds)
            {
                if (nrc.nameID == nameId && !string.IsNullOrEmpty(nrc.sNameString))
                {
                    // Add suffix to the name
                    string newName = nrc.sNameString;
                    
                    // For PostScript name (ID 6), replace spaces with hyphens
                    if (nameId == 6)
                    {
                        newName = newName.Replace(" ", "-") + suffix.Replace(" ", "-");
                    }
                    else
                    {
                        newName += suffix;
                    }

                    try
                    {
                        cache.UpdateNameRecord(nrc.platformID, nrc.encodingID, nrc.languageID, nrc.nameID, newName);
                    }
                    catch
                    {
                        // Record may have already been updated in a previous iteration
                    }
                    break;
                }
            }
        }

        return (Table_name)cache.GenerateTable();
    }

    /// <summary>
    /// Build subset CFF table with de-subroutinized CharStrings.
    /// </summary>
    public Table_CFF? BuildCFF()
    {
        var sourceCFF = _sourceFont.GetTable("CFF ") as Table_CFF;
        if (sourceCFF == null)
            return null;

        // Trigger lazy loading by accessing a property
        // This ensures EnsureDataLoaded() is called internally
        _ = sourceCFF.major;

        // Get the original CFF data
        var originalData = sourceCFF.m_bufTable.GetBuffer();
        if (originalData == null || originalData.Length == 0)
            return null;

        // Get sorted retained glyphs (by new GID)
        var sortedRetained = _glyphIdMap
            .OrderBy(kv => kv.Value)
            .Select(kv => kv.Key)
            .ToList();

        // Build subset CFF
        var cffBuilder = new CFFBuilder();
        var subsetData = cffBuilder.BuildCFF(
            originalData,
            sortedRetained,
            (Dictionary<int, int>)_glyphIdMap);

        // Create new CFF table
        var newBuf = new MBOBuffer((uint)subsetData.Length);
        Array.Copy(subsetData, newBuf.GetBuffer(), subsetData.Length);

        return new Table_CFF("CFF ", newBuf);
    }

    /// <summary>
    /// Check if the font uses CFF outlines (OTF format).
    /// </summary>
    public bool IsCFFFont()
    {
        return _sourceFont.GetTable("CFF ") != null;
    }
}

