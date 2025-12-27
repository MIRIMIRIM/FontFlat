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
        int subtableCount = hasNonBmp ? 2 : 1; // Format 4 + optional Format 12
        
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

        // Encoding table entry for Format 4 (Windows Unicode BMP)
        uint eteOffset = 4;
        buffer.SetUshort(3, eteOffset);     // platformID = Windows
        buffer.SetUshort(1, eteOffset + 2); // encodingID = Unicode BMP
        buffer.SetUint(format4Offset, eteOffset + 4);

        // Encoding table entry for Format 12 if present
        if (hasNonBmp && format12Data != null)
        {
            eteOffset += 8;
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
}
