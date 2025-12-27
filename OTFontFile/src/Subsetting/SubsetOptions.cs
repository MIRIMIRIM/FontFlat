using System;
using System.Collections.Generic;
using System.Text;

namespace OTFontFile.Subsetting;

/// <summary>
/// Options for font subsetting operations.
/// This is a pure subsetting API - character preprocessing should be done by the caller.
/// </summary>
public class SubsetOptions
{
    // ================== Input Specification ==================

    /// <summary>
    /// Unicode codepoints to include in subset.
    /// </summary>
    public HashSet<int> Unicodes { get; set; } = new();

    /// <summary>
    /// Glyph IDs to include directly (bypass cmap lookup).
    /// </summary>
    public HashSet<int> GlyphIds { get; set; } = new();

    // ================== Glyph Handling ==================

    /// <summary>
    /// Include .notdef glyph (glyph ID 0). Default: true.
    /// </summary>
    public bool IncludeNotdef { get; set; } = true;

    /// <summary>
    /// Retain original glyph IDs (keep original numbering, fill gaps with empty glyphs).
    /// Default: false (compact glyph IDs).
    /// </summary>
    public bool RetainGids { get; set; } = false;

    /// <summary>
    /// Expand glyph set through OpenType layout feature substitutions (GSUB).
    /// Default: false.
    /// </summary>
    public bool LayoutClosure { get; set; } = false;

    // ================== Table Handling ==================

    /// <summary>
    /// Tables to drop entirely from the output.
    /// Default: DSIG (Digital Signature - invalid after modification),
    ///          GSUB/GPOS (OpenType layout - not needed for basic rendering),
    ///          GDEF, BASE (layout-related tables),
    ///          and other large non-essential tables.
    /// Set LayoutClosure to true and clear these if you need layout features.
    /// </summary>
    public HashSet<string> DropTables { get; set; } = new()
    {
        "DSIG",  // Digital signature - invalid after subsetting
        "GSUB",  // Substitution rules - often very large in CJK fonts
        "GPOS",  // Positioning rules - often very large
        "GDEF",  // Glyph definitions for layout
        "BASE",  // Baseline table
        "JSTF",  // Justification table
        "MATH",  // Math layout table
        "COLR",  // Color table
        "CPAL",  // Color palette
        "SVG ",  // SVG outlines
        "sbix",  // Apple bitmap images
        "CBDT",  // Color bitmap data
        "CBLC",  // Color bitmap location
        "EBDT",  // Embedded bitmap data
        "EBLC",  // Embedded bitmap location
    };

    /// <summary>
    /// Keep hinting instructions (fpgm, prep, cvt tables).
    /// Default: true.
    /// </summary>
    public bool KeepHinting { get; set; } = true;

    /// <summary>
    /// Suffix to add to font name for subset identification.
    /// If null, name table is copied as-is.
    /// Default: null.
    /// </summary>
    public string? NewFontNameSuffix { get; set; } = null;

    /// <summary>
    /// Preserve OS/2 ulCodePageRange bits even when subsetting.
    /// This is equivalent to fonttools' --no-prune-codepage-ranges option.
    /// Required for VSFilter compatibility.
    /// Default: true (preserve for compatibility).
    /// </summary>
    public bool PreserveCodePageRanges { get; set; } = true;

    // ================== Helper Methods ==================

    /// <summary>
    /// Add all codepoints from a text string.
    /// </summary>
    public SubsetOptions AddText(string text)
    {
        foreach (var rune in text.EnumerateRunes())
        {
            Unicodes.Add(rune.Value);
        }
        return this;
    }

    /// <summary>
    /// Add codepoints from a collection of Runes.
    /// </summary>
    public SubsetOptions AddRunes(IEnumerable<Rune> runes)
    {
        foreach (var rune in runes)
        {
            Unicodes.Add(rune.Value);
        }
        return this;
    }

    /// <summary>
    /// Add a range of Unicode codepoints.
    /// </summary>
    public SubsetOptions AddRange(int start, int end)
    {
        for (int i = start; i <= end; i++)
        {
            Unicodes.Add(i);
        }
        return this;
    }

    /// <summary>
    /// Add specific glyph IDs directly.
    /// </summary>
    public SubsetOptions AddGlyphIds(IEnumerable<int> glyphIds)
    {
        foreach (var gid in glyphIds)
        {
            GlyphIds.Add(gid);
        }
        return this;
    }
}
