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
    /// Default: DSIG (Digital Signature - invalid after modification).
    /// </summary>
    public HashSet<string> DropTables { get; set; } = new()
    {
        "DSIG",
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
