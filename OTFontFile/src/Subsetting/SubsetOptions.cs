using System;
using System.Collections.Generic;
using System.Text;

namespace OTFontFile.Subsetting;

/// <summary>
/// Options for font subsetting operations.
/// Compatible with AssFontSubset.Core usage patterns.
/// </summary>
public class SubsetOptions
{
    // ================== Input Specification ==================

    /// <summary>
    /// Unicode codepoints to include in subset.
    /// </summary>
    public HashSet<int> Unicodes { get; set; } = new();

    /// <summary>
    /// Text string - all characters will be included.
    /// Processed as UTF-32 codepoints to handle surrogate pairs.
    /// </summary>
    public string? Text { get; set; }

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
    /// Default: true.
    /// </summary>
    public bool LayoutClosure { get; set; } = true;

    // ================== ASS-Specific Options ==================

    /// <summary>
    /// Always include ASCII alphanumeric characters (A-Za-z0-9) and their fullwidth equivalents.
    /// This is required for proper fallback behavior in ASS subtitle rendering.
    /// Default: true.
    /// </summary>
    public bool IncludeAsciiAlphanumeric { get; set; } = true;

    /// <summary>
    /// Add vertical form mappings for vertical text layout.
    /// Maps horizontal punctuation to their vertical equivalents (e.g., comma → vertical comma).
    /// Default: true.
    /// </summary>
    public bool AddVerticalForms { get; set; } = true;

    // ================== Layout Features ==================

    /// <summary>
    /// OpenType layout features to preserve.
    /// Default: vertical-related features (vert, vrt2, vkna, vrtr).
    /// </summary>
    public HashSet<string> LayoutFeatures { get; set; } = new()
    {
        "vert",  // Vertical Alternates
        "vrt2",  // Vertical Alternates and Rotation
        "vkna",  // Vertical Kana Alternates
        "vrtr",  // Vertical Alternates for Rotation
    };

    // ================== Compatibility Options ==================

    /// <summary>
    /// Preserve OS/2 ulCodePageRange fields.
    /// Set to false to match fonttools --no-prune-codepage-ranges behavior.
    /// Required for VSFilter compatibility.
    /// Default: true (do not prune).
    /// </summary>
    public bool PreserveCodepageRanges { get; set; } = true;

    /// <summary>
    /// Fix non-compliant cmap format=12 subtables.
    /// Workaround for HarfBuzz issue #4980: generates proper platformID/encodingID pairs.
    /// Default: true.
    /// </summary>
    public bool FixNonCompliantCmap { get; set; } = true;

    // ================== Font Renaming ==================

    /// <summary>
    /// New family name for the subset font.
    /// If null, original name is preserved.
    /// </summary>
    public string? NewFamilyName { get; set; }

    /// <summary>
    /// Copyright notice for the subset font.
    /// If null, a default notice is generated.
    /// </summary>
    public string? Copyright { get; set; }

    /// <summary>
    /// Name IDs to modify when renaming.
    /// Default: 0 (Copyright), 1 (Family), 3 (Unique ID), 4 (Full Name), 6 (PostScript Name).
    /// </summary>
    public HashSet<int> RenameNameIds { get; set; } = new() { 0, 1, 3, 4, 6 };

    // ================== Table Handling ==================

    /// <summary>
    /// Tables to drop entirely from the output.
    /// Default: DSIG, JSTF, LTSH, PCLT.
    /// </summary>
    public HashSet<string> DropTables { get; set; } = new()
    {
        "DSIG",  // Digital Signature - invalid after modification
        "JSTF",  // Justification - rarely needed
        "LTSH",  // Linear Threshold - for low-res rendering
        "PCLT",  // PCL 5 - printer specific
    };

    /// <summary>
    /// Keep hinting instructions (fpgm, prep, cvt tables).
    /// Default: true.
    /// </summary>
    public bool KeepHinting { get; set; } = true;

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
    /// Add ASCII alphanumeric characters and their fullwidth equivalents.
    /// </summary>
    public SubsetOptions AddAsciiAlphanumeric()
    {
        // Uppercase Latin (A-Z and Ａ-Ｚ)
        for (int i = 0x0041; i <= 0x005A; i++)
        {
            Unicodes.Add(i);
            Unicodes.Add(i + 0xFEE0); // Fullwidth
        }

        // Lowercase Latin (a-z and ａ-ｚ)
        for (int i = 0x0061; i <= 0x007A; i++)
        {
            Unicodes.Add(i);
            Unicodes.Add(i + 0xFEE0); // Fullwidth
        }

        // Digits (0-9 and ０-９)
        for (int i = 0x0030; i <= 0x0039; i++)
        {
            Unicodes.Add(i);
            Unicodes.Add(i + 0xFEE0); // Fullwidth
        }

        // Common punctuation needed for fallback (from AssFontSubset)
        Unicodes.Add(0xFF1F); // ？ Fullwidth Question Mark
        Unicodes.Add(0xFF20); // ＠ Fullwidth Commercial At

        return this;
    }

    /// <summary>
    /// Create default options optimized for ASS subtitle embedding.
    /// </summary>
    public static SubsetOptions ForAssSubtitle()
    {
        return new SubsetOptions
        {
            IncludeNotdef = true,
            IncludeAsciiAlphanumeric = true,
            AddVerticalForms = true,
            LayoutClosure = true,
            PreserveCodepageRanges = true,
            FixNonCompliantCmap = true,
            KeepHinting = true,
        };
    }

    /// <summary>
    /// Create minimal options for web font optimization.
    /// </summary>
    public static SubsetOptions ForWebFont()
    {
        return new SubsetOptions
        {
            IncludeNotdef = true,
            IncludeAsciiAlphanumeric = false,
            AddVerticalForms = false,
            LayoutClosure = false,
            PreserveCodepageRanges = false,
            FixNonCompliantCmap = true,
            KeepHinting = false,
            DropTables = new() { "DSIG", "JSTF", "LTSH", "PCLT", "hdmx", "VDMX" },
        };
    }
}
