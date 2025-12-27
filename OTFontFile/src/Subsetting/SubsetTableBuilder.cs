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
}
