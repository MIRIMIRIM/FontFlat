namespace CJKCharacterCount.Core;

public static class UnicodeBlocks
{
    private static UnicodeBlock Create(string name, int start, int end)
    {
        return new UnicodeBlock
        {
            Name = name,
            StartCode = start,
            EndCode = end,
            AssignedRanges = new[] { (start, end) }
        };
    }

    private static UnicodeBlock CreateMultirange(string name, params (int, int)[] ranges)
    {
        int min = ranges[0].Item1;
        int max = ranges[0].Item2;
        foreach (var r in ranges)
        {
            if (r.Item1 < min) min = r.Item1;
            if (r.Item2 > max) max = r.Item2;
        }
        return new UnicodeBlock
        {
            Name = name,
            StartCode = min,
            EndCode = max,
            AssignedRanges = ranges
        };
    }

    public static readonly UnicodeBlock CjkZero = Create("〇", 0x3007, 0x3007);

    public static readonly UnicodeBlock KangxiRadicals = Create("Kangxi Radicals", 0x2F00, 0x2FDF);

    public static readonly UnicodeBlock CjkRadicalsSupplement = Create("CJK Radicals Supplement", 0x2E80, 0x2EFF);

    public static readonly UnicodeBlock CjkUnifiedIdeographs = Create("CJK Unified Ideographs", 0x4E00, 0x9FFF);

    public static readonly UnicodeBlock CjkExtensionA = Create("CJK Unified Ideographs Extension A", 0x3400, 0x4DBF);

    public static readonly UnicodeBlock CjkExtensionB = Create("CJK Unified Ideographs Extension B", 0x20000, 0x2A6DF);

    public static readonly UnicodeBlock CjkExtensionC = Create("CJK Unified Ideographs Extension C", 0x2A700, 0x2B73F);

    public static readonly UnicodeBlock CjkExtensionD = Create("CJK Unified Ideographs Extension D", 0x2B740, 0x2B81F);

    public static readonly UnicodeBlock CjkExtensionE = Create("CJK Unified Ideographs Extension E", 0x2B820, 0x2CEAF);

    public static readonly UnicodeBlock CjkExtensionF = Create("CJK Unified Ideographs Extension F", 0x2CEB0, 0x2EBEF);

    public static readonly UnicodeBlock CjkExtensionG = Create("CJK Unified Ideographs Extension G", 0x30000, 0x3134F);

    public static readonly UnicodeBlock CjkExtensionH = Create("CJK Unified Ideographs Extension H", 0x31350, 0x323AF);

    public static readonly UnicodeBlock CjkExtensionI = Create("CJK Unified Ideographs Extension I", 0x2EBF0, 0x2EE5F);

    public static readonly UnicodeBlock CjkExtensionJ = Create("CJK Unified Ideographs Extension J", 0x323B0, 0x3347F);

    public static readonly UnicodeBlock CjkCompatibilityIdeographs = Create("CJK Compatibility Ideographs", 0xF900, 0xFAFF);

    public static readonly UnicodeBlock CjkCompatibilityIdeographsSupplement = Create("CJK Compatibility Ideographs Supplement", 0x2F800, 0x2FA1F);

    // Special block from global_var.py: Non-Compatibility (Unified) Ideographs
    // These are characters in Compatibility block that are actually unified ideographs (canonical).
    private static readonly int[] NonCompatibilityList =
    [
        0xFA0E, 0xFA0F, 0xFA11, 0xFA13, 0xFA14, 0xFA1F,
        0xFA21, 0xFA23, 0xFA24, 0xFA27, 0xFA28, 0xFA29
    ];

    public static readonly UnicodeBlock CjkNonCompatibilityIdeographs = CreateMultirange(
        "Non-Compatibility (Unified) Ideographs",
        [.. NonCompatibilityList.Select(c => (c, c))]
    );

    // Block List dictionary for UI iteration
    public static readonly IReadOnlyList<UnicodeBlock> AllBlocks =
    [
        KangxiRadicals,
        CjkRadicalsSupplement,
        CjkZero,
        CjkUnifiedIdeographs,
        CjkExtensionA,
        CjkExtensionB,
        CjkExtensionC,
        CjkExtensionD,
        CjkExtensionE,
        CjkExtensionF,
        CjkExtensionG,
        CjkExtensionH,
        CjkExtensionI,
        CjkExtensionJ,
        CjkCompatibilityIdeographs,
        CjkCompatibilityIdeographsSupplement,
        CjkNonCompatibilityIdeographs
    ];

    // Total block for summary
    // Ranges from all above except "Non-Compatibility" which is a subset of Compatibility? 
    // Wait, global_var.py logic for Total includes everything.
    // Actually, `CjkNonCompatibilityIdeographs` are inside `CjkCompatibilityIdeographs`.
    // The Python code constructs `Total` by union of ranges.

    public static readonly UnicodeBlock Total;

    static UnicodeBlocks()
    {
        var ranges = new List<(int, int)>();
        foreach (var block in AllBlocks)
        {
            // Avoid double counting if blocks overlap?
            // "Non-Compatibility" overlaps with "Compatibility".
            // Python's `IDEO_BLOCKS` likely excludes Compatibility? No, let's check global_var.py
            // global_var.py `IDEO_BLOCKS` + `ZERO` + `NON_COMPATIBILITY`.
            // It seems standard Compatibility blocks are NOT in IDEO_BLOCKS?
            // `IDEO_BLOCKS` typically means Unified Ideographs.
            // If I look at `unicode_blocks` library source (assumed), it probably contains Extensions.

            // For now, I will include everything in AllBlocks for display, but for Total, I should act like Python.
            // Python Total = ZERO + NON_COMPATIBILITY + IDEO_BLOCKS (Unified + Extensions)
            // So Total DOES NOT include full Compatibility Block, only the 12 unified ones inside it?

            if (block.Name == "CJK Compatibility Ideographs" || block.Name == "CJK Compatibility Ideographs Supplement")
                continue; // Skip full compatibility blocks for Total (per Python logic assumption)

            ranges.AddRange(block.AssignedRanges.ToArray());
        }

        // Merge ranges? Or just keep them? 
        // Simple merge for Total
        // Actually for count, we overlap check with font.
        // We can just set ranges.
        Total = CreateMultirange("Total", [.. ranges]);
    }
}
