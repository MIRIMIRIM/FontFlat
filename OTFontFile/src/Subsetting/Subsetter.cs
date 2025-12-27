using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OTFontFile.Subsetting;

/// <summary>
/// Font subsetter that creates a reduced font containing only specified glyphs.
/// </summary>
public class Subsetter
{
    private readonly SubsetOptions _options;
    private readonly HashSet<int> _retainedGlyphs = new();
    private readonly Dictionary<int, int> _glyphIdMap = new(); // old → new
    private readonly HashSet<int> _retainedUnicodes = new();

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
        _retainedUnicodes.Clear();

        // Step 1: Collect all requested Unicode codepoints
        CollectRequestedUnicodes();

        // Step 2: Map Unicodes to glyph IDs via cmap
        MapUnicodesToGlyphs(font);

        // Step 3: Add explicit glyph IDs
        foreach (var gid in _options.GlyphIds)
        {
            _retainedGlyphs.Add(gid);
        }

        // Step 4: Always include .notdef if requested
        if (_options.IncludeNotdef)
        {
            _retainedGlyphs.Add(0);
        }

        // Step 5: Composite glyph closure (TrueType glyf table)
        CloseOverCompositeGlyphs(font);

        // Step 6: GSUB closure (if layout closure is enabled)
        if (_options.LayoutClosure)
        {
            CloseOverGSUB(font);
        }
    }

    private void CollectRequestedUnicodes()
    {
        // Add explicit unicodes
        foreach (var unicode in _options.Unicodes)
        {
            _retainedUnicodes.Add(unicode);
        }

        // Add unicodes from text
        if (!string.IsNullOrEmpty(_options.Text))
        {
            foreach (var rune in _options.Text.EnumerateRunes())
            {
                _retainedUnicodes.Add(rune.Value);
            }
        }

        // Add ASCII alphanumeric if requested
        if (_options.IncludeAsciiAlphanumeric)
        {
            AddAsciiAlphanumericUnicodes();
        }

        // Add vertical form mappings if requested
        if (_options.AddVerticalForms)
        {
            AddVerticalFormUnicodes();
        }
    }

    private void AddAsciiAlphanumericUnicodes()
    {
        // Uppercase Latin (A-Z and Ａ-Ｚ)
        for (int i = 0x0041; i <= 0x005A; i++)
        {
            _retainedUnicodes.Add(i);
            _retainedUnicodes.Add(i + 0xFEE0);
        }

        // Lowercase Latin (a-z and ａ-ｚ)
        for (int i = 0x0061; i <= 0x007A; i++)
        {
            _retainedUnicodes.Add(i);
            _retainedUnicodes.Add(i + 0xFEE0);
        }

        // Digits (0-9 and ０-９)
        for (int i = 0x0030; i <= 0x0039; i++)
        {
            _retainedUnicodes.Add(i);
            _retainedUnicodes.Add(i + 0xFEE0);
        }

        // Common punctuation
        _retainedUnicodes.Add(0xFF1F); // ？
        _retainedUnicodes.Add(0xFF20); // ＠
    }

    private void AddVerticalFormUnicodes()
    {
        // Vertical Forms (from FontConstant.VertMapping in AssFontSubset.Core)
        var verticalMappings = new Dictionary<int, int>
        {
            // Vertical Forms block (FE10-FE1F)
            { 0x002C, 0xFE10 }, // comma
            { 0x3001, 0xFE11 }, // ideographic comma
            { 0x3002, 0xFE12 }, // ideographic period
            { 0x003A, 0xFE13 }, // colon
            { 0x003B, 0xFE14 }, // semicolon
            { 0x0021, 0xFE15 }, // exclamation mark
            { 0x003F, 0xFE16 }, // question mark
            { 0x3016, 0xFE17 }, // left white lenticular bracket
            { 0x3017, 0xFE18 }, // right white lenticular bracket
            { 0x2026, 0xFE19 }, // horizontal ellipsis → vertical

            // CJK Compatibility Forms (FE30-FE4F)
            { 0x2014, 0xFE31 }, // em dash
            { 0x2013, 0xFE32 }, // en dash
            { 0x0028, 0xFE35 }, // left parenthesis
            { 0x0029, 0xFE36 }, // right parenthesis
            { 0x007B, 0xFE37 }, // left curly bracket
            { 0x007D, 0xFE38 }, // right curly bracket
            { 0x3014, 0xFE39 }, // left tortoise shell bracket
            { 0x3015, 0xFE3A }, // right tortoise shell bracket
            { 0x3010, 0xFE3B }, // left black lenticular bracket
            { 0x3011, 0xFE3C }, // right black lenticular bracket
            { 0x300A, 0xFE3D }, // left double angle bracket
            { 0x300B, 0xFE3E }, // right double angle bracket
            { 0x2329, 0xFE3F }, // left-pointing angle bracket
            { 0x232A, 0xFE40 }, // right-pointing angle bracket
            { 0x300C, 0xFE41 }, // left corner bracket
            { 0x300D, 0xFE42 }, // right corner bracket
            { 0x300E, 0xFE43 }, // left white corner bracket
            { 0x300F, 0xFE44 }, // right white corner bracket
            { 0x005B, 0xFE47 }, // left square bracket
            { 0x005D, 0xFE48 }, // right square bracket
        };

        // For each retained horizontal unicode, add its vertical form
        var toAdd = new List<int>();
        foreach (var unicode in _retainedUnicodes)
        {
            if (verticalMappings.TryGetValue(unicode, out var verticalForm))
            {
                toAdd.Add(verticalForm);
            }
        }

        foreach (var unicode in toAdd)
        {
            _retainedUnicodes.Add(unicode);
        }
    }

    private void MapUnicodesToGlyphs(OTFont font)
    {
        foreach (var unicode in _retainedUnicodes)
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
        // For now, create a simple copy with table filtering
        // Full table subsetting will be implemented incrementally

        var numTables = sourceFont.GetNumTables();
        var tablesToKeep = new List<OTTable>();

        for (ushort i = 0; i < numTables; i++)
        {
            var table = sourceFont.GetTable(i);
            if (table == null) continue;

            var tag = table.m_tag.ToString();

            // Skip tables marked for dropping
            if (_options.DropTables.Contains(tag))
                continue;

            // Skip hinting tables if not keeping hinting
            if (!_options.KeepHinting && (tag == "fpgm" || tag == "prep" || tag == "cvt "))
                continue;

            tablesToKeep.Add(table);
        }

        // Create new font from tables
        // Note: This is a placeholder - actual subsetting of table contents
        // will be implemented in subsequent phases
        var subsetFont = new OTFont();

        foreach (var table in tablesToKeep)
        {
            subsetFont.AddTable(table);
        }

        return subsetFont;
    }

    // ================== Static Factory Methods ==================

    /// <summary>
    /// Create a subset font for ASS subtitle embedding.
    /// </summary>
    public static OTFont SubsetForAss(OTFont font, string text, string? newName = null)
    {
        var options = SubsetOptions.ForAssSubtitle();
        options.Text = text;
        options.NewFamilyName = newName;

        var subsetter = new Subsetter(options);
        return subsetter.Subset(font);
    }

    /// <summary>
    /// Create a subset font with specific Unicode codepoints.
    /// </summary>
    public static OTFont SubsetUnicodes(OTFont font, IEnumerable<int> unicodes)
    {
        var options = new SubsetOptions();
        foreach (var unicode in unicodes)
        {
            options.Unicodes.Add(unicode);
        }

        var subsetter = new Subsetter(options);
        return subsetter.Subset(font);
    }
}
