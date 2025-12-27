using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OTFontFile.Subsetting;

/// <summary>
/// Font subsetter that creates a reduced font containing only specified glyphs.
/// This is a pure subsetting API - character preprocessing should be done by the caller.
/// </summary>
public class Subsetter
{
    private readonly SubsetOptions _options;
    private readonly HashSet<int> _retainedGlyphs = new();
    private readonly Dictionary<int, int> _glyphIdMap = new(); // old → new

    public Subsetter(SubsetOptions options)
    {
        _options = options;
    }

    /// <summary>
    /// Create a subset of the font containing only the specified glyphs.
    /// </summary>
    /// <param name="sourceFont">Source font to subset.</param>
    /// <returns>A new OTFont containing only the retained glyphs.</returns>
    public OTFont Subset(OTFont sourceFont)
    {
        // Phase 1: Compute glyph closure
        ComputeGlyphClosure(sourceFont);

        // Phase 2: Build glyph ID remapping
        BuildGlyphIdMap(sourceFont);

        // Phase 3: Create subset font with modified tables
        var subsetFont = CreateSubsetFont(sourceFont);

        return subsetFont;
    }

    /// <summary>
    /// Get the retained glyph IDs after closure computation.
    /// </summary>
    public IReadOnlySet<int> RetainedGlyphs => _retainedGlyphs;

    /// <summary>
    /// Get the glyph ID mapping (old → new).
    /// </summary>
    public IReadOnlyDictionary<int, int> GlyphIdMap => _glyphIdMap;

    // ================== Phase 1: Glyph Closure ==================

    private void ComputeGlyphClosure(OTFont font)
    {
        _retainedGlyphs.Clear();

        // Step 1: Map Unicodes to glyph IDs via cmap
        MapUnicodesToGlyphs(font);

        // Step 2: Add explicit glyph IDs
        foreach (var gid in _options.GlyphIds)
        {
            _retainedGlyphs.Add(gid);
        }

        // Step 3: Always include .notdef if requested
        if (_options.IncludeNotdef)
        {
            _retainedGlyphs.Add(0);
        }

        // Step 4: Composite glyph closure (TrueType glyf table)
        CloseOverCompositeGlyphs(font);

        // Step 5: GSUB closure (if layout closure is enabled)
        if (_options.LayoutClosure)
        {
            CloseOverGSUB(font);
        }
    }

    private void MapUnicodesToGlyphs(OTFont font)
    {
        foreach (var unicode in _options.Unicodes)
        {
            uint gid;
            if (unicode <= 0xFFFF)
            {
                // BMP characters
                gid = font.FastMapUnicodeToGlyphID((char)unicode);
            }
            else
            {
                // Non-BMP characters (supplementary planes)
                gid = font.FastMapUnicode32ToGlyphID((uint)unicode);
            }

            if (gid != 0 || unicode == 0)
            {
                _retainedGlyphs.Add((int)gid);
            }
        }
    }

    private void CloseOverCompositeGlyphs(OTFont font)
    {
        var glyf = font.GetTable("glyf") as Table_glyf;
        if (glyf == null) return;

        // Fixed-point iteration until no new glyphs are added
        bool changed;
        do
        {
            changed = false;
            foreach (var gid in _retainedGlyphs.ToList())
            {
                if (gid < 0) continue;

                var header = glyf.GetGlyphHeader((uint)gid, font);
                if (header == null) continue;

                // Check if this is a composite glyph (numberOfContours < 0)
                if (header.numberOfContours < 0)
                {
                    var compositeGlyph = header.GetCompositeGlyph();
                    
                    // Iterate through all components
                    while (compositeGlyph != null)
                    {
                        var componentGid = compositeGlyph.glyphIndex;
                        if (_retainedGlyphs.Add((int)componentGid))
                        {
                            changed = true;
                        }
                        
                        // Get next component if MORE_COMPONENTS flag is set
                        compositeGlyph = compositeGlyph.GetNextCompositeGlyph();
                    }
                }
            }
        } while (changed);
    }

    private void CloseOverGSUB(OTFont font)
    {
        var gsub = font.GetTable("GSUB") as Table_GSUB;
        if (gsub == null) return;

        // TODO: Implement GSUB closure
        // For now, we skip GSUB closure as it requires deep traversal of lookup tables
        // This is a future enhancement - the basic subsetting will still work
    }

    // ================== Phase 2: Glyph ID Mapping ==================

    private void BuildGlyphIdMap(OTFont font)
    {
        _glyphIdMap.Clear();

        if (_options.RetainGids)
        {
            // Keep original glyph IDs
            foreach (var gid in _retainedGlyphs)
            {
                _glyphIdMap[gid] = gid;
            }
        }
        else
        {
            // Compact glyph IDs: sort and renumber from 0
            var sortedGlyphs = _retainedGlyphs.OrderBy(g => g).ToList();
            for (int newGid = 0; newGid < sortedGlyphs.Count; newGid++)
            {
                _glyphIdMap[sortedGlyphs[newGid]] = newGid;
            }
        }
    }

    // ================== Phase 3: Create Subset Font ==================

    private OTFont CreateSubsetFont(OTFont sourceFont)
    {
        var subsetFont = new OTFont();
        var builder = new SubsetTableBuilder(sourceFont, _retainedGlyphs, _glyphIdMap, _options);

        // Build core tables that need subsetting
        var (newGlyf, newLoca, newNumGlyphs) = builder.BuildGlyfLoca();
        
        // If TrueType font, add subset glyf/loca
        if (newGlyf != null && newLoca != null && newNumGlyphs > 0)
        {
            subsetFont.AddTable(newGlyf);
            subsetFont.AddTable(newLoca);

            // Build dependent tables
            var newMaxp = builder.BuildMaxp(newNumGlyphs);
            if (newMaxp != null)
                subsetFont.AddTable(newMaxp);

            var newHhea = builder.BuildHhea(newNumGlyphs);
            if (newHhea != null)
                subsetFont.AddTable(newHhea);

            var newHmtx = builder.BuildHmtx();
            if (newHmtx != null)
                subsetFont.AddTable(newHmtx);

            // Determine loca format from BuildGlyfLoca result
            var sourceHead = sourceFont.GetTable("head") as Table_head;
            short locaFormat = 0;
            if (sourceHead != null && newLoca != null)
            {
                // Calculate based on glyf table size
                var glyfSize = newGlyf.GetLength();
                locaFormat = glyfSize > 0x1FFFE ? (short)1 : (short)0;
            }

            var newHead = builder.BuildHead(locaFormat);
            if (newHead != null)
                subsetFont.AddTable(newHead);
        }

        // Copy other tables that don't need subsetting
        var numTables = sourceFont.GetNumTables();
        var handledTables = new HashSet<string> { "glyf", "loca", "maxp", "hhea", "hmtx", "head" };

        for (ushort i = 0; i < numTables; i++)
        {
            var table = sourceFont.GetTable(i);
            if (table == null) continue;

            var tag = table.m_tag.ToString();

            // Skip already handled tables
            if (handledTables.Contains(tag))
                continue;

            // Skip tables marked for dropping
            if (_options.DropTables.Contains(tag))
                continue;

            // Skip hinting tables if not keeping hinting
            if (!_options.KeepHinting && (tag == "fpgm" || tag == "prep" || tag == "cvt "))
                continue;

            // Copy table as-is (cmap, name, etc. will be subset in future phases)
            subsetFont.AddTable(table);
        }

        return subsetFont;
    }

    // ================== Static Factory Methods ==================

    /// <summary>
    /// Create a subset font with specified Unicode codepoints.
    /// </summary>
    public static OTFont SubsetByUnicodes(OTFont font, IEnumerable<int> unicodes)
    {
        var options = new SubsetOptions();
        foreach (var unicode in unicodes)
        {
            options.Unicodes.Add(unicode);
        }

        var subsetter = new Subsetter(options);
        return subsetter.Subset(font);
    }

    /// <summary>
    /// Create a subset font with specified text.
    /// </summary>
    public static OTFont SubsetByText(OTFont font, string text)
    {
        var options = new SubsetOptions().AddText(text);
        var subsetter = new Subsetter(options);
        return subsetter.Subset(font);
    }

    /// <summary>
    /// Create a subset font with specified glyph IDs.
    /// </summary>
    public static OTFont SubsetByGlyphIds(OTFont font, IEnumerable<int> glyphIds)
    {
        var options = new SubsetOptions();
        foreach (var gid in glyphIds)
        {
            options.GlyphIds.Add(gid);
        }

        var subsetter = new Subsetter(options);
        return subsetter.Subset(font);
    }
}
