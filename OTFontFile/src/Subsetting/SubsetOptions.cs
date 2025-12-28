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

    /// <summary>
    /// Layout feature tags to retain.
    /// null = use DefaultLayoutFeatures, empty = drop all, contains "*" = keep all.
    /// Example: new() { "kern", "liga" } to keep only kerning and ligatures.
    /// </summary>
    public HashSet<string>? LayoutFeatures { get; set; } = null;

    /// <summary>
    /// Layout script tags to retain.
    /// null or contains "*" = keep all scripts.
    /// Example: new() { "latn", "cyrl" } to keep only Latin and Cyrillic.
    /// </summary>
    public HashSet<string>? LayoutScripts { get; set; } = null;

    /// <summary>
    /// Default layout features to retain (matches pyftsubset defaults).
    /// </summary>
    public static readonly HashSet<string> DefaultLayoutFeatures = new()
    {
        "calt", "ccmp", "clig", "curs", "dnom", "frac",
        "kern", "liga", "locl", "mark", "mkmk", "numr",
        "rclt", "rlig", "rvrn"
    };

    // ================== Table Handling ==================

    /// <summary>
    /// Tables to always drop from the output.
    /// Matches fonttools defaults.
    /// </summary>
    public HashSet<string> DropTables { get; set; } = new()
    {
        "DSIG",  // Digital signature - invalid after subsetting
        "JSTF",  // Justification - rarely used
        "PCLT",  // PCL 5 - legacy
        "LTSH",  // Linear Threshold - legacy hinting
        "BASE",  // Baseline table - rarely needed after subsetting
        // Graphite tables
        "Feat", "Glat", "Gloc", "Silf", "Sill",
        // FontForge metadata - not needed
        "FFTM",
    };

    /// <summary>
    /// Drop OpenType layout tables (GSUB/GPOS/GDEF/BASE) entirely.
    /// Set to true for minimal file size at the cost of losing layout features.
    /// Default: false (tables are subset, not dropped).
    /// </summary>
    public bool DropLayoutTables { get; set; } = false;

    /// <summary>
    /// Drop color and bitmap tables (COLR/CPAL/SVG/sbix/EBDT/EBLC/CBDT/CBLC).
    /// Default: true (most subset fonts don't need these).
    /// </summary>
    public bool DropColorBitmapTables { get; set; } = true;

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

    /// <summary>
    /// Preserve OS/2 ulUnicodeRange bits even when subsetting.
    /// This is equivalent to hb-subset --no-prune-unicode-ranges option.
    /// Default: false (prune ranges to match retained unicodes).
    /// </summary>
    public bool PreserveUnicodeRanges { get; set; } = false;


    /// <summary>
    /// Subset name table to only essential records (name IDs 1, 2).
    /// This matches fonttools/pyftsubset default behavior.
    /// Set to false to keep all name records.
    /// Default: true.
    /// </summary>
    public bool SubsetNameTable { get; set; } = true;

    /// <summary>
    /// Name IDs to keep when SubsetNameTable is true.
    /// Default: {0,1,2,3,4,5,6} matching fonttools default.
    /// 0=Copyright, 1=Family, 2=Subfamily, 3=UniqueID, 4=FullName, 5=Version, 6=PostScript
    /// </summary>
    public HashSet<int> RetainedNameIds { get; set; } = new() { 0, 1, 2, 3, 4, 5, 6 };

    /// <summary>
    /// Language IDs to keep when SubsetNameTable is true.
    /// Default: {0x0409} (English US) matching fonttools default.
    /// Set to null to keep all languages.
    /// </summary>
    public HashSet<int>? RetainedNameLanguages { get; set; } = new() { 0x0409 };

    /// <summary>
    /// Keep non-Unicode name records (legacy Mac/platform-specific).
    /// Default: false (only keep Unicode records, matching fonttools name_legacy=False).
    /// </summary>
    public bool NameLegacy { get; set; } = false;

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
