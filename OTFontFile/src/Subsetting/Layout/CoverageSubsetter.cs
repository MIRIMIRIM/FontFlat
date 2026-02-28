using System;
using System.Collections.Generic;
using OTFontFile;

namespace OTFontFile.Subsetting.Layout
{
    /// <summary>
    /// Helper to subset Coverage tables (Format 1 and 2).
    /// </summary>
    public static class CoverageSubsetter
    {
        /// <summary>
        /// Reads a Coverage table from offset and returns a new coverage table logic suitable for subsetting.
        /// Returns sorted list of retained glyphs and their old coverage indices.
        /// </summary>
        /// <returns>
        /// List of (NewGlyphID, OldGlyphID, OldCoverageIndex). 
        /// Use OldGlyphID to look up substitution/positioning values.
        /// </returns>
        public static List<(ushort NewGid, ushort OldGid, int OldCovIndex)> Subset(
            MBOBuffer file, 
            uint offset, 
            SubsetPlan plan)
        {
            var result = new List<(ushort NewGid, ushort OldGid, int OldCovIndex)>();
            
            ushort format = file.GetUshort(offset);

            if (format == 1)
            {
                // Format 1: List of glyph indices
                ushort glyphCount = file.GetUshort(offset + 2);
                for (int i = 0; i < glyphCount; i++)
                {
                    ushort oldGid = file.GetUshort(offset + 4 + (uint)i * 2);
                    if (plan.TryGetNewGid(oldGid, out ushort newGid))
                    {
                        result.Add((newGid, oldGid, i));
                    }
                }
            }
            else if (format == 2)
            {
                // Format 2: Ranges
                ushort rangeCount = file.GetUshort(offset + 2);
                uint currentPos = offset + 4;

                for (int i = 0; i < rangeCount; i++)
                {
                    ushort start = file.GetUshort(currentPos);
                    ushort end = file.GetUshort(currentPos + 2);
                    ushort startCoverageIndex = file.GetUshort(currentPos + 4);
                    
                    for (int gid = start; gid <= end; gid++)
                    {
                        if (plan.TryGetNewGid((ushort)gid, out ushort newGid))
                        {
                            int oldCovIndex = startCoverageIndex + (gid - start);
                            result.Add((newGid, (ushort)gid, oldCovIndex));
                        }
                    }

                    currentPos += 6;
                }
            }
            else
            {
                throw new NotSupportedException($"Unknown Coverage Format: {format}");
            }

            // Valid Coverage must be sorted by New Glyph ID
            result.Sort((a, b) => a.NewGid.CompareTo(b.NewGid));

            return result;
        }

        /// <summary>
        /// Writes a new Coverage table (Format 1 or 2, whichever is smaller) to the buffer.
        /// </summary>
        /// <param name="data">List of Glyph IDs to cover</param>
        /// <returns>Bytes of the new Coverage table</returns>
        public static byte[] BuildCoverage(List<ushort> glyphs)
        {
            if (glyphs == null || glyphs.Count == 0) return Array.Empty<byte>();

            // Ensure sorted
            glyphs.Sort();

            // Calculate size for Format 1
            int sizeF1 = 2 + 2 + (glyphs.Count * 2);

            // Calculate size for Format 2
            var ranges = new List<(ushort Start, ushort End, ushort StartIndex)>();
            if (glyphs.Count > 0)
            {
                ushort start = glyphs[0];
                ushort end = start;
                ushort startIndex = 0;

                for (int i = 1; i < glyphs.Count; i++)
                {
                    if (glyphs[i] == end + 1)
                    {
                        end = glyphs[i];
                    }
                    else
                    {
                        ranges.Add((start, end, startIndex));
                        startIndex += (ushort)(end - start + 1);
                        start = glyphs[i];
                        end = start;
                    }
                }
                ranges.Add((start, end, startIndex));
            }
            int sizeF2 = 2 + 2 + (ranges.Count * 6);

            // Choose smaller format
            if (sizeF1 < sizeF2)
            {
                // Write Format 1
                var buf = new byte[sizeF1];
                int pos = 0;
                WriteUshort(buf, ref pos, 1);
                WriteUshort(buf, ref pos, (ushort)glyphs.Count);
                foreach (var g in glyphs)
                {
                    WriteUshort(buf, ref pos, g);
                }
                return buf;
            }
            else
            {
                // Write Format 2
                var buf = new byte[sizeF2];
                int pos = 0;
                WriteUshort(buf, ref pos, 2);
                WriteUshort(buf, ref pos, (ushort)ranges.Count);
                foreach (var r in ranges)
                {
                    WriteUshort(buf, ref pos, r.Start);
                    WriteUshort(buf, ref pos, r.End);
                    WriteUshort(buf, ref pos, r.StartIndex);
                }
                return buf;
            }
        }

        private static void WriteUshort(byte[] buf, ref int pos, ushort val)
        {
            buf[pos++] = (byte)(val >> 8);
            buf[pos++] = (byte)val;
        }
    }
}
