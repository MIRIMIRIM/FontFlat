using System.Collections.Generic;
using System.Linq;

namespace OTFontFile.Subsetting.Layout
{
    /// <summary>
    /// Holds the subsetting plan for OpenType Layout tables (GSUB/GPOS).
    /// Tracks glyph mapping and which lookups/features should be retained.
    /// </summary>
    public class SubsetPlan
    {
        /// <summary>
        /// Sorted list of original Glyph IDs to be retained.
        /// </summary>
        public List<ushort> RetainedGlyphs { get; }

        /// <summary>
        /// Map from original Glyph ID to new Glyph ID.
        /// </summary>
        public Dictionary<ushort, ushort> OldToNewGidMap { get; }

        /// <summary>
        /// Set of original Glyph IDs (for O(1) lookup).
        /// </summary>
        public HashSet<ushort> OldGidSet { get; }

        /// <summary>
        /// Indices of lookups that should be retained in the new font.
        /// Populated during the closure/pruning phase.
        /// </summary>
        public HashSet<int> RetainedLookupIndices { get; } = new();

        /// <summary>
        /// Indices of features that should be retained.
        /// </summary>
        public HashSet<int> RetainedFeatureIndices { get; } = new();

        /// <summary>
        /// Mapping from old LookupList index to new LookupList index.
        /// Populated after pruning.
        /// </summary>
        public Dictionary<int, int>? LookupIndexMap { get; set; }

        /// <summary>
        /// Mapping from old FeatureList index to new FeatureList index.
        /// Populated after pruning.
        /// </summary>
        public Dictionary<int, int>? FeatureIndexMap { get; set; }

        /// <summary>
        /// Feature tags to retain. null = all, empty = none, {"*"} = all.
        /// </summary>
        public HashSet<string>? FeatureFilter { get; set; }

        /// <summary>
        /// Script tags to retain. null or {"*"} = all.
        /// </summary>
        public HashSet<string>? ScriptFilter { get; set; }

        /// <summary>
        /// Check if a feature tag should be kept.
        /// </summary>
        public bool ShouldKeepFeature(string tag)
        {
            if (FeatureFilter == null) return true;
            if (FeatureFilter.Contains("*")) return true;
            return FeatureFilter.Contains(tag);
        }

        /// <summary>
        /// Check if a script tag should be kept.
        /// </summary>
        public bool ShouldKeepScript(string tag)
        {
            if (ScriptFilter == null) return true;
            if (ScriptFilter.Contains("*")) return true;
            return ScriptFilter.Contains(tag);
        }

        public SubsetPlan(HashSet<ushort> retainedGlyphs, Dictionary<int, int> oldToNewGid)
        {
            // Convert to consistent types
            OldGidSet = retainedGlyphs;
            RetainedGlyphs = retainedGlyphs.OrderBy(g => g).ToList();
            
            OldToNewGidMap = new Dictionary<ushort, ushort>(oldToNewGid.Count);
            foreach (var kvp in oldToNewGid)
            {
                OldToNewGidMap[(ushort)kvp.Key] = (ushort)kvp.Value;
            }
        }

        public SubsetPlan(HashSet<int> retainedGlyphs, Dictionary<int, int> oldToNewGid)
        {
            OldGidSet = new HashSet<ushort>(retainedGlyphs.Count);
            RetainedGlyphs = new List<ushort>(retainedGlyphs.Count);
            
            foreach (int g in retainedGlyphs)
            {
                OldGidSet.Add((ushort)g);
                RetainedGlyphs.Add((ushort)g);
            }
            RetainedGlyphs.Sort();

            OldToNewGidMap = new Dictionary<ushort, ushort>(oldToNewGid.Count);
            foreach (var kvp in oldToNewGid)
            {
                OldToNewGidMap[(ushort)kvp.Key] = (ushort)kvp.Value;
            }
        }

        /// <summary>
        /// Check if a glyph is retained.
        /// </summary>
        public bool IsGlyphRetained(ushort oldGid)
        {
            return OldGidSet.Contains(oldGid);
        }

        /// <summary>
        /// Get the new Glyph ID for an original Glyph ID.
        /// </summary>
        public bool TryGetNewGid(ushort oldGid, out ushort newGid)
        {
            return OldToNewGidMap.TryGetValue(oldGid, out newGid);
        }
    }
}
