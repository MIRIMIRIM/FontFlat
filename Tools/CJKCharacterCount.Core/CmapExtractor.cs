using OTFontFile;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using System.Numerics;

namespace CJKCharacterCount.Core;

public static class CmapExtractor
{
    /// <summary>
    /// Extract all mapped compiled Unicode code points from the font.
    /// Prioritizes Format 12 (UCS-4) then Format 4 (UCS-2).
    /// </summary>
    public static HashSet<int> ExtractCodePoints(OTFont font)
    {
        if (font.GetTable("cmap") is not Table_cmap cmap)
            return [];

        // Try to find UCS-4 (3, 10) or (0, 4) or (0, 6)
        // Format 12 is usually (3, 10).
        var subtable = FindSubtable(cmap, 12) ?? FindSubtable(cmap, 4);

        if (subtable is Table_cmap.Format12 fmt12)
        {
            return ExtractFromFormat12(fmt12);
        }
        else if (subtable is Table_cmap.Format4 fmt4)
        {
            return ExtractFromFormat4(fmt4);
        }

        // Fallback: try iteration if other formats (not implemented yet in this extractor)
        // OTFontFile's Table_cmap supports mostly 0, 2, 4, 12.

        return [];
    }

    private static Table_cmap.Subtable? FindSubtable(Table_cmap cmap, int format)
    {
        // Search specific specific platform/encoding preference
        // Windows Unicode (3, 1), (3, 10)
        // Unicode (0, 3), (0, 4)

        // Helper to check valid subtable format
        bool IsFormat(Table_cmap.Subtable? s, int f)
        {
            if (s == null) return false;
            // Hacky check via reflection or type check as base Subtable doesn't expose Format number directly easily
            if (f == 12 && s is Table_cmap.Format12) return true;
            if (f == 4 && s is Table_cmap.Format4) return true;
            return false;
        }

        // Try Windows UCS-4 (3, 10) for Format 12
        if (format == 12)
        {
            var s = cmap.GetSubtable(3, 10);
            if (IsFormat(s, 12)) return s;
            s = cmap.GetSubtable(0, 4); // Unicode 2.0+
            if (IsFormat(s, 12)) return s;
            s = cmap.GetSubtable(0, 6); // Unicode full
            if (IsFormat(s, 12)) return s;
        }

        // Try Windows BMP (3, 1) for Format 4
        if (format == 4)
        {
            var s = cmap.GetSubtable(3, 1);
            if (IsFormat(s, 4)) return s;
            s = cmap.GetSubtable(0, 3);
            if (IsFormat(s, 4)) return s;
        }

        return null;
    }

    private static HashSet<int> ExtractFromFormat12(Table_cmap.Format12 fmt12)
    {
        var set = new HashSet<int>((int)fmt12.nGroups * 10); // heuristic capacity

        uint nGroups = fmt12.nGroups;
        for (uint i = 0; i < nGroups; i++)
        {
            var group = fmt12.GetGroup(i);
            uint start = group.startCharCode;
            uint end = group.endCharCode;
            // startGlyphID is not checked against 0 because modern fonts usually put only mapped chars in groups.
            // But if we want to be paranoid:
            // if (group.startGlyphID == 0) ... loop and check? 
            // Usually group implies valid mapping. 

            int count = (int)(end - start + 1);
            for (int k = 0; k < count; k++)
            {
                set.Add((int)(start + k));
            }
        }
        return set;
    }

    private static HashSet<int> ExtractFromFormat4(Table_cmap.Format4 fmt4)
    {
        var set = new HashSet<int>();
        int segCount = fmt4.segCountX2 / 2;

        for (uint i = 0; i < segCount; i++)
        {
            ushort start = fmt4.GetStartCode(i);
            ushort end = fmt4.GetEndCode(i);
            if (start == 0xFFFF && end == 0xFFFF) break; // Sentinel

            ushort idRangeOffset = fmt4.GetIdRangeOffset(i);
            short idDelta = fmt4.GetIdDelta(i);

            if (idRangeOffset == 0)
            {
                // direct delta mapping
                for (int c = start; c <= end; c++)
                {
                    // glyph = (c + delta) % 65536
                    ushort glyph = (ushort)(c + idDelta);
                    if (glyph != 0)
                    {
                        set.Add(c);
                    }
                }
            }
            else
            {
                // range offset mapping
                // We utilize the MapCharToGlyph logic which is encapsulated in OTFontFile but exposed via helpers
                // However, doing it manually is faster if we avoid function calls
                // Re-implementing logic for speed:

                // uint AddressOfIdRangeOffset = (uint)Table_cmap.Format4.FieldOffsets.endCode + (uint)fmt4.segCountX2*3u + 2 + i*2;
                // BUT FieldOffsets is protected/nested private/internal? 
                // We can just call MapCharToGlyph(c) for these complex cases

                for (int c = start; c <= end; c++)
                {
                    uint glyph = fmt4.MapCharToGlyph((char)c);
                    if (glyph != 0)
                    {
                        set.Add(c);
                    }
                }
            }
        }
        return set;
    }

    public static Dictionary<int, HashSet<int>> ExtractFromCollection(OTFile file)
    {
        // TODO: Implement TTC iteration
        return [];
    }
}
