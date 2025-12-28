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
    private readonly Dictionary<int, int> _unicodeToOldGid = new(); // unicode → old glyph ID

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
        _unicodeToOldGid.Clear();
        
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
                _unicodeToOldGid[unicode] = (int)gid;
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

        // Get the lookup list
        var lookupList = gsub.GetLookupListTable();
        if (lookupList == null) return;

        bool changed;
        do
        {
            changed = false;
            int previousCount = _retainedGlyphs.Count;

            // Iterate through all lookups
            for (uint i = 0; i < lookupList.LookupCount; i++)
            {
                var lookup = lookupList.GetLookupTable(i);
                if (lookup == null) continue;

                // Process each subtable
                for (uint st = 0; st < lookup.SubTableCount; st++)
                {
                    var subTable = lookup.GetSubTable(st);
                    if (subTable == null) continue;

                    ProcessGSUBSubtable(lookup.LookupType, subTable);
                }
            }

            changed = _retainedGlyphs.Count > previousCount;
        } while (changed);
    }

    private void ProcessGSUBSubtable(ushort lookupType, OTL.SubTable subTable)
    {
        switch (lookupType)
        {
            case 1: // Single Substitution
                ProcessSingleSubst((Table_GSUB.SingleSubst)subTable);
                break;
            case 2: // Multiple Substitution
                ProcessMultipleSubst((Table_GSUB.MultipleSubst)subTable);
                break;
            case 3: // Alternate Substitution
                ProcessAlternateSubst((Table_GSUB.AlternateSubst)subTable);
                break;
            case 4: // Ligature Substitution
                ProcessLigatureSubst((Table_GSUB.LigatureSubst)subTable);
                break;
            case 7: // Extension Substitution
                var extSubst = (Table_GSUB.ExtensionSubst)subTable;
                // Recursively handle the extended subtable
                // Note: Extension wraps another lookup type
                break;
            // Types 5, 6, 8 are context-based and more complex
            // For basic closure, we skip these as they require context matching
        }
    }

    private void ProcessSingleSubst(Table_GSUB.SingleSubst singleSubst)
    {
        if (singleSubst.SubstFormat == 1)
        {
            var format1 = singleSubst.GetSingleSubstFormat1();
            var coverage = format1.GetCoverageTable();
            short delta = (short)format1.DeltaGlyphID;
            
            foreach (var coverageGlyph in EnumerateCoverageGlyphs(coverage))
            {
                if (_retainedGlyphs.Contains(coverageGlyph))
                {
                    // Add the substitute glyph
                    int substituteGlyph = (coverageGlyph + delta) & 0xFFFF;
                    _retainedGlyphs.Add(substituteGlyph);
                }
            }
        }
        else if (singleSubst.SubstFormat == 2)
        {
            var format2 = singleSubst.GetSingleSubstFormat2();
            var coverage = format2.GetCoverageTable();
            
            uint index = 0;
            foreach (var coverageGlyph in EnumerateCoverageGlyphs(coverage))
            {
                if (_retainedGlyphs.Contains(coverageGlyph) && index < format2.GlyphCount)
                {
                    ushort substituteGlyph = format2.GetSubstituteGlyphID(index);
                    _retainedGlyphs.Add(substituteGlyph);
                }
                index++;
            }
        }
    }

    private void ProcessMultipleSubst(Table_GSUB.MultipleSubst multiSubst)
    {
        var coverage = multiSubst.GetCoverageTable();
        
        uint index = 0;
        foreach (var coverageGlyph in EnumerateCoverageGlyphs(coverage))
        {
            if (_retainedGlyphs.Contains(coverageGlyph) && index < multiSubst.SequenceCount)
            {
                var sequence = multiSubst.GetSequenceTable(index);
                if (sequence != null)
                {
                    for (uint g = 0; g < sequence.GlyphCount; g++)
                    {
                        _retainedGlyphs.Add(sequence.GetSubstituteGlyphID(g));
                    }
                }
            }
            index++;
        }
    }

    private void ProcessAlternateSubst(Table_GSUB.AlternateSubst alternateSubst)
    {
        var coverage = alternateSubst.GetCoverageTable();
        
        uint index = 0;
        foreach (var coverageGlyph in EnumerateCoverageGlyphs(coverage))
        {
            if (_retainedGlyphs.Contains(coverageGlyph) && index < alternateSubst.AlternateSetCount)
            {
                var altSet = alternateSubst.GetAlternateSetTable(index);
                if (altSet != null)
                {
                    for (uint g = 0; g < altSet.GlyphCount; g++)
                    {
                        _retainedGlyphs.Add(altSet.GetAlternateGlyphID(g));
                    }
                }
            }
            index++;
        }
    }

    private void ProcessLigatureSubst(Table_GSUB.LigatureSubst ligSubst)
    {
        var coverage = ligSubst.GetCoverageTable();
        
        uint index = 0;
        foreach (var coverageGlyph in EnumerateCoverageGlyphs(coverage))
        {
            if (_retainedGlyphs.Contains(coverageGlyph) && index < ligSubst.LigSetCount)
            {
                var ligSet = ligSubst.GetLigatureSetTable(index);
                if (ligSet != null)
                {
                    for (uint l = 0; l < ligSet.LigatureCount; l++)
                    {
                        var lig = ligSet.GetLigatureTable(l);
                        if (lig == null) continue;

                        // Check if all component glyphs are in retained set
                        bool allComponentsRetained = true;
                        for (uint c = 0; c < lig.CompCount - 1; c++)
                        {
                            if (!_retainedGlyphs.Contains(lig.GetComponentGlyphID(c)))
                            {
                                allComponentsRetained = false;
                                break;
                            }
                        }

                        // If first glyph and all components are retained, add the ligature glyph
                        if (allComponentsRetained)
                        {
                            _retainedGlyphs.Add(lig.LigGlyph);
                        }
                    }
                }
            }
            index++;
        }
    }

    private static IEnumerable<int> EnumerateCoverageGlyphs(OTL.CoverageTable coverage)
    {
        if (coverage.CoverageFormat == 1)
        {
            for (uint i = 0; i < coverage.F1GlyphCount; i++)
            {
                yield return coverage.F1GetGlyphID(i);
            }
        }
        else if (coverage.CoverageFormat == 2)
        {
            for (uint i = 0; i < coverage.F2RangeCount; i++)
            {
                var range = coverage.F2GetRangeRecord(i);
                if (range != null)
                {
                    for (int gid = range.Start; gid <= range.End; gid++)
                    {
                        yield return gid;
                    }
                }
            }
        }
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
        else if (builder.IsCFFFont())
        {
            // CFF/OTF font - use CFF subsetting
            var newCFF = builder.BuildCFF();
            if (newCFF != null)
            {
                subsetFont.AddTable(newCFF);

                // Build dependent tables for CFF
                var cffNumGlyphs = (ushort)_glyphIdMap.Count;

                var newMaxp = builder.BuildMaxp(cffNumGlyphs);
                if (newMaxp != null)
                    subsetFont.AddTable(newMaxp);

                var newHhea = builder.BuildHhea(cffNumGlyphs);
                if (newHhea != null)
                    subsetFont.AddTable(newHhea);

                var newHmtx = builder.BuildHmtx();
                if (newHmtx != null)
                    subsetFont.AddTable(newHmtx);

                // CFF uses indexToLocFormat = 0 (doesn't matter for CFF)
                var newHead = builder.BuildHead(0);
                if (newHead != null)
                    subsetFont.AddTable(newHead);
            }
        }

        // Build cmap table with only retained Unicode mappings
        var unicodeToNewGid = BuildUnicodeToNewGidMap();
        var newCmap = builder.BuildCmap(unicodeToNewGid);
        if (newCmap != null)
            subsetFont.AddTable(newCmap);

        // Build post table version 3.0 (no glyph names)
        var newPost = builder.BuildPost();
        if (newPost != null)
            subsetFont.AddTable(newPost);

        // Build OS/2 table with updated Unicode ranges
        var newOS2 = builder.BuildOS2(_options.Unicodes);
        if (newOS2 != null)
            subsetFont.AddTable(newOS2);

        // Build vmtx/vhea if source font has vertical metrics
        var sourceVmtx = sourceFont.GetTable("vmtx");
        var sourceVhea = sourceFont.GetTable("vhea");
        if (sourceVmtx != null && sourceVhea != null)
        {
            var (_, _, numGlyphs) = builder.BuildGlyfLoca();
            
            var newVhea = builder.BuildVhea(numGlyphs);
            if (newVhea != null)
                subsetFont.AddTable(newVhea);

            var newVmtx = builder.BuildVmtx();
            if (newVmtx != null)
                subsetFont.AddTable(newVmtx);
        }

        // Build VORG table for CFF fonts with vertical metrics
        var newVORG = builder.BuildVORG();
        if (newVORG != null)
            subsetFont.AddTable(newVORG);

        // Build name table
        bool nameHandled = false;
        if (_options.NewFontNameSuffix != null)
        {
            // If renaming is requested, use the renaming method
            var newName = builder.BuildName(_options.NewFontNameSuffix);
            if (newName != null)
            {
                subsetFont.AddTable(newName);
                nameHandled = true;
            }
        }
        else if (_options.SubsetNameTable)
        {
            // Subset name table to only retained name IDs, languages, and Unicode-only (matching fonttools default)
            var newName = builder.BuildSubsettedNameTable(
                _options.RetainedNameIds,
                _options.RetainedNameLanguages,
                !_options.NameLegacy  // unicodeOnly = !name_legacy
            );
            if (newName != null)
            {
                subsetFont.AddTable(newName);
                nameHandled = true;
            }
        }

        // Copy other tables that don't need subsetting
        var numTables = sourceFont.GetNumTables();
        var handledTables = new HashSet<string> { "glyf", "loca", "maxp", "hhea", "hmtx", "head", "cmap", "post", "OS/2", "vmtx", "vhea", "CFF ", "VORG" };
        
        // Add name to handled if we built a new one
        if (nameHandled)
            handledTables.Add("name");

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

            // Skip layout tables if DropLayoutTables is enabled
            if (_options.DropLayoutTables && IsLayoutTable(tag))
                continue;

            // Handle GSUB Subsetting
            if (tag == "GSUB" && !_options.DropLayoutTables)
            {
                var gsub = table as Table_GSUB;
                if (gsub != null)
                {
                    var plan = new OTFontFile.Subsetting.Layout.SubsetPlan(
                        _retainedGlyphs, 
                        _glyphIdMap
                    )
                    {
                        FeatureFilter = _options.LayoutFeatures ?? SubsetOptions.DefaultLayoutFeatures,
                        ScriptFilter = _options.LayoutScripts
                    };
                    
                    var layoutSubsetter = new OTFontFile.Subsetting.Layout.LayoutSubsetter();
                    var subsetGsub = layoutSubsetter.SubsetGsub(gsub, plan);

                    if (subsetGsub != null)
                    {
                        subsetFont.AddTable(subsetGsub);
                    }
                    // Either we added subsetted table OR we drop it (don't fall through to copy original)
                    continue;
                }
            }

            // Handle GPOS Subsetting (Kerning)
            if (tag == "GPOS" && !_options.DropLayoutTables)
            {
                var gpos = table as Table_GPOS;
                if (gpos != null)
                {
                    var plan = new OTFontFile.Subsetting.Layout.SubsetPlan(
                        _retainedGlyphs, 
                        _glyphIdMap
                    )
                    {
                        FeatureFilter = _options.LayoutFeatures ?? SubsetOptions.DefaultLayoutFeatures,
                        ScriptFilter = _options.LayoutScripts
                    };
                    
                    var layoutSubsetter = new OTFontFile.Subsetting.Layout.LayoutSubsetter();
                    var subsetGpos = layoutSubsetter.SubsetGpos(gpos, plan);

                    if (subsetGpos != null)
                    {
                        subsetFont.AddTable(subsetGpos);
                    }
                    // Either we added subsetted table OR we drop it (don't fall through to copy original)
                    continue;
                }
            }

            // Handle GDEF Subsetting
            if (tag == "GDEF" && !_options.DropLayoutTables)
            {
                var gdef = table as Table_GDEF;
                if (gdef != null)
                {
                    var subsetGdef = builder.BuildGDEF();
                    if (subsetGdef != null)
                    {
                        subsetFont.AddTable(subsetGdef);
                    }
                    continue;
                }
            }

            // Skip color/bitmap tables if DropColorBitmapTables is enabled
            if (_options.DropColorBitmapTables && IsColorBitmapTable(tag))
                continue;

            // Copy table as-is
            subsetFont.AddTable(table);
        }

        return subsetFont;
    }

    /// <summary>
    /// Build Unicode to new glyph ID mapping for cmap subsetting.
    /// </summary>
    private Dictionary<int, int> BuildUnicodeToNewGidMap()
    {
        var result = new Dictionary<int, int>();
        
        foreach (var kv in _unicodeToOldGid)
        {
            int unicode = kv.Key;
            int oldGid = kv.Value;
            
            if (_glyphIdMap.TryGetValue(oldGid, out int newGid))
            {
                result[unicode] = newGid;
            }
        }
        
        return result;
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

    // ================== Helper Methods ==================

    /// <summary>
    /// Check if a table tag is an OpenType layout table.
    /// </summary>
    private static bool IsLayoutTable(string tag)
    {
        return tag switch
        {
            "GSUB" => true,
            "GPOS" => true,
            "GDEF" => true,
            "BASE" => true,
            "JSTF" => true,
            "MATH" => true,
            _ => false
        };
    }

    /// <summary>
    /// Check if a table tag is a color or bitmap glyph table.
    /// </summary>
    private static bool IsColorBitmapTable(string tag)
    {
        return tag switch
        {
            "COLR" => true,
            "CPAL" => true,
            "SVG " => true,
            "sbix" => true,
            "CBDT" => true,
            "CBLC" => true,
            "EBDT" => true,
            "EBLC" => true,
            "EBSC" => true,
            _ => false
        };
    }
}
