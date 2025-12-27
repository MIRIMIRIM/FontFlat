using System;
using System.Collections.Generic;
using OTFontFile;

namespace OTFontFile.Subsetting.Layout
{
    public class LayoutSubsetter
    {
        private readonly Dictionary<ushort, ISubtableSubsetter> _gsubSubsetters = new();
        private readonly Dictionary<ushort, ISubtableSubsetter> _gposSubsetters = new();

        public LayoutSubsetter()
        {
            // Register GSUB subsetters (GSUB lookup types)
            _gsubSubsetters[1] = new SingleSubstSubsetter();   // Type 1: SingleSubst
            _gsubSubsetters[4] = new LigatureSubstSubsetter(); // Type 4: LigatureSubst
            
            // Register GPOS subsetters (GPOS lookup types)
            _gposSubsetters[1] = new SinglePosSubsetter();     // Type 1: SinglePos
            _gposSubsetters[2] = new PairPosSubsetter();       // Type 2: PairPos (Kerning)
        }

        public Table_GSUB? SubsetGsub(Table_GSUB source, SubsetPlan plan)
        {
            if (source == null) return null;
            return SubsetLayoutTableInternal(source, 0, plan, "GSUB", _gsubSubsetters) as Table_GSUB;
        }

        public Table_GPOS? SubsetGpos(Table_GPOS source, SubsetPlan plan)
        {
            if (source == null) return null;
            return SubsetLayoutTableInternal(source, 0, plan, "GPOS", _gposSubsetters) as Table_GPOS;
        }

        // Ideally we would return a generic LayoutTable or byte[], but Subsetter expects Table keys.
        // Since OTFontFile structures are read-only wrappers around OTFile, we need to creating 
        // a "virtual" table or just writing to the output stream directly?
        // Subsetter.cs expects to pull tables from "subsetFont" which are then written.
        // We can create a "Table_GSUB" that wraps a byte array?
        // OTFontFile doesn't support that easily (Table classes take OTFile + offset).
        
        // Strategy: LayoutSubsetter returns byte[]. 
        // Subsetter.cs will add this byte[] to the OTFontBuilder (if expanded to support raw bytes)
        // OR we write to a memory stream, construct a temp OTFile? No that's slow.
        
        // Actually Subsetter.cs uses "subsetFont.AddTable(table)".
        // OTFont doesn't usually hold "new" tables in memory unless using DataCache?
        // The project has been moving to DataCache (GenerateTable).
        // Does Table_GSUB interact with DataCache?
        // If not, we might need a wrapper.
        
        // For now, let's assume I return byte[] and Subsetter.cs handles wrapping it 
        // or we modify Table_GSUB to support "FromData".
        
        // Wait, Subsetter.cs creates "OTFont subsetFont".
        // subsetFont.AddTable(tag, dataCache) is possible?
        // Or subsetFont.AddTable(DirectoryEntry)?
        
        // Checking OTFont.cs... it usually just manages directory.
        // Writing happens in OTFile.WriteSfntFile.
        // It checks if table implements IDataCache or comes from file.
        
        // So LayoutSubsetter should probably return a "RawDataCache" : IDataCache.
        
        public byte[]? SubsetLayoutTableBytes(OTTable source, uint offset, SubsetPlan plan, string tag,
                                              Dictionary<ushort, ISubtableSubsetter> subsetters)
        {
             // 1. Parse Structure
            var layout = LayoutTable.Parse(source.GetBuffer(), offset);
            
            // 2. Subset Lookups
            var lookupList = layout.LookupList;
            if (lookupList == null) return null;

            var newLookupsModel = new List<Lookup>();
            var newLookupSubtables = new List<List<byte[]>>();
            
            // Map OldLookupIndex -> NewLookupIndex
            plan.LookupIndexMap = new Dictionary<int, int>();
            
            int newLookupIndex = 0;
            for (int i = 0; i < lookupList.Lookups.Count; i++)
            {
                var lookup = lookupList.Lookups[i];
                var newSubtables = new List<byte[]>();
                
                // If we have a subsetter for this type
                if (subsetters.TryGetValue(lookup.LookupType, out var subsetter))
                {
                    foreach (uint subtableOffset in lookup.SubtableOffsets)
                    {
                        var data = subsetter.Subset(source.GetBuffer(), subtableOffset, plan);
                        if (data != null && data.Length > 0)
                        {
                            newSubtables.Add(data);
                        }
                    }
                }
                else
                {
                    // Unknown/Unhandled Lookup Type -> Drop or Keep?
                    // Strategy: Prune unhandled types to save space (since we can't subset them properly)
                    // OR: Keep as-is if coverage permits?
                    // If we keep "as-is", we assume all glyphs in it are retained? 
                    // No, that's dangerous. Unhandled tables referring to missing glyphs might remain valid 
                    // if those glyphs are just "not in cmap", but if internal structure is broken?
                    // Safest: Drop unhandled tables.
                }

                if (newSubtables.Count > 0)
                {
                    // Keep this lookup
                    plan.LookupIndexMap[i] = newLookupIndex;
                    plan.RetainedLookupIndices.Add(i);
                    newLookupsModel.Add(lookup);
                    newLookupSubtables.Add(newSubtables);
                    newLookupIndex++;
                }
            }
            
            // If no lookups survive, drop table? 
            if (newLookupsModel.Count == 0 && tag == "GSUB") return null; 
            // GPOS might allow empty lookup list but still have features? No, features point to lookups.

            // 3. Prune Features
            var newFeatureList = new FeatureList();
            plan.FeatureIndexMap = new Dictionary<int, int>();
            int newFeatureIndex = 0;

            if (layout.FeatureList != null)
            {
                for (int i = 0; i < layout.FeatureList.Features.Count; i++)
                {
                    var featRec = layout.FeatureList.Features[i];
                    var newIndices = new List<ushort>();
                    bool keepFeature = false;

                    foreach (var oldLookupIdx in featRec.Feature.LookupIndices)
                    {
                        if (plan.LookupIndexMap.TryGetValue(oldLookupIdx, out int newIdx))
                        {
                            newIndices.Add((ushort)newIdx);
                            keepFeature = true;
                        }
                    }

                    if (keepFeature)
                    {
                        plan.FeatureIndexMap[i] = newFeatureIndex;
                        plan.RetainedFeatureIndices.Add(i);
                        
                        // Update model
                        featRec.Feature.LookupIndices.Clear();
                        featRec.Feature.LookupIndices.AddRange(newIndices);
                        newFeatureList.Features.Add(featRec);
                        
                        newFeatureIndex++;
                    }
                }
            }
            layout.FeatureList = newFeatureList;

            // 4. Prune Scripts
            var newScriptList = new ScriptList();
            if (layout.ScriptList != null)
            {
                foreach (var scriptPair in layout.ScriptList.Scripts)
                {
                    var script = scriptPair.Value;
                    var newLangSysRecs = new Dictionary<string, LangSys>();
                    bool keepScript = false;

                    // Update DefaultLangSys
                    if (script.DefaultLangSys != null)
                    {
                        if (PruneLangSys(script.DefaultLangSys, plan))
                        {
                            keepScript = true;
                        }
                        else
                        {
                            script.DefaultLangSys = null; // Drop empty default?
                        }
                    }

                    // Update LangSysRecords
                    foreach (var langPair in script.LangSysRecords)
                    {
                        if (PruneLangSys(langPair.Value, plan))
                        {
                            newLangSysRecs[langPair.Key] = langPair.Value;
                            keepScript = true;
                        }
                    }
                    script.LangSysRecords.Clear();
                    foreach (var kvp in newLangSysRecs) script.LangSysRecords[kvp.Key] = kvp.Value;

                    if (keepScript)
                    {
                        newScriptList.Scripts[scriptPair.Key] = script;
                    }
                }
            }
            layout.ScriptList = newScriptList;
            
            // If no scripts/features/lookups survived, drop the table entirely
            // This matches reference tools behavior (pyftsubset, hb-subset)
            if (newFeatureList.Features.Count == 0 || newScriptList.Scripts.Count == 0)
            {
                return null;
            }

            // 5. Serialize
            return LayoutSerializer.Serialize(layout, newLookupsModel, newLookupSubtables, tag);
        }

        private bool PruneLangSys(LangSys langSys, SubsetPlan plan)
        {
            var newIndices = new List<ushort>();
            bool hasFeatures = false;

            // Required Feature
            if (langSys.RequiredFeatureIndex != 0xFFFF)
            {
                if (plan.FeatureIndexMap.TryGetValue(langSys.RequiredFeatureIndex, out int newReq))
                {
                    langSys.RequiredFeatureIndex = (ushort)newReq;
                    hasFeatures = true;
                }
                else
                {
                    langSys.RequiredFeatureIndex = 0xFFFF;
                }
            }

            // Other Features
            foreach (var oldFeatIdx in langSys.FeatureIndices)
            {
                if (plan.FeatureIndexMap.TryGetValue(oldFeatIdx, out int newIdx))
                {
                    newIndices.Add((ushort)newIdx);
                    hasFeatures = true;
                }
            }
            
            langSys.FeatureIndices.Clear();
            langSys.FeatureIndices.AddRange(newIndices);

            return hasFeatures;
        }

        // Helper to allow generic call
        private object? SubsetLayoutTableInternal(OTTable source, uint offset, SubsetPlan plan, string tag,
                                                   Dictionary<ushort, ISubtableSubsetter> subsetters)
        {
            byte[]? bytes = SubsetLayoutTableBytes(source, offset, plan, tag, subsetters);
            if (bytes == null) return null;
            
            uint mboLen = (uint)bytes.Length;
            var mbo = new MBOBuffer(mboLen);
            Array.Copy(bytes, mbo.GetBuffer(), bytes.Length);
            
            if (tag == "GSUB") 
            {
                var gsubTag = new OTTag("GSUB");
                return new Table_GSUB(gsubTag, mbo);
            }
            else if (tag == "GPOS")
            {
                var gposTag = new OTTag("GPOS");
                return new Table_GPOS(gposTag, mbo);
            }
            
            return null;
        }
    }
}
