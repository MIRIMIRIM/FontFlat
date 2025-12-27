using System;
using System.Collections.Generic;
using System.Text;
using OTFontFile;

namespace OTFontFile.Subsetting.Layout
{
    public class LayoutSerializer
    {
        // Rebuilds the entire GSUB/GPOS table from the subsets.
        // Returns the byte array of the new table.
        public static byte[] Serialize(
            LayoutTable layout, 
            List<Lookup> newLookupsModel, 
            List<List<byte[]>> newLookupSubtables,
            string tableName) // GSUB or GPOS
        {
            // 1. Calculate sizes and plan offsets
            // Structure:
            // Header (10 bytes for v1.0)
            // ScriptList
            // FeatureList
            // LookupList
            // Coverage tables (if shared? usually not shared across lookups in this simple implementation)
            // Features
            // Scripts
            // LangSys
            // Lookups
            // Subtables

            // We need a dedicated buffer builder helper that tracks offsets.
            // Since offsets are 16-bit, we must be careful with ordering.
            // Usually: 
            // - Header
            // - ScriptList
            // - FeatureList
            // - LookupList
            // - Scripts...
            // - Features...
            // - Lookups...
            // - Subtables...
            
            // This is "Graph Serialization". HARFBUZZ uses a complex "Repacker".
            // FontTools serializes in logical order.
            // We will use a simple "Append" strategy:
            // - Reserve space for top-level lists.
            // - Append children after parents.
            // - Fill offsets.
            
            var builder = new LayoutBinaryBuilder();
            
            // ================= HEADER =================
            int headerStart = builder.CurrentPosition;
            builder.WriteUshort(1); // Major Version
            builder.WriteUshort(0); // Minor Version
            
            // Placeholders for ScriptList, FeatureList, LookupList offsets
            int scriptListOffsetPos = builder.CurrentPosition;
            builder.WriteUshort(0);
            int featureListOffsetPos = builder.CurrentPosition;
            builder.WriteUshort(0);
            int lookupListOffsetPos = builder.CurrentPosition;
            builder.WriteUshort(0);

            // ================= SCRIPT LIST =================
            if (layout.ScriptList != null && layout.ScriptList.Scripts.Count > 0)
            {
                int scriptListStart = builder.CurrentPosition;
                builder.PatchUshort(scriptListOffsetPos, (ushort)(scriptListStart - headerStart));
                
                var scriptList = layout.ScriptList;
                var scripts = new List<(string Tag, Script Script)>(scriptList.Scripts.Count);
                foreach (var kvp in scriptList.Scripts) scripts.Add((kvp.Key, kvp.Value));
                scripts.Sort((a, b) => string.Compare(a.Tag, b.Tag, StringComparison.Ordinal)); // Must be tag sorted

                builder.WriteUshort((ushort)scripts.Count);
                
                // ScriptRecords
                var scriptOffsetsToFill = new List<(int Pos, Script Script)>();
                foreach (var s in scripts)
                {
                    builder.WriteTag(s.Tag);
                    int offsetPos = builder.CurrentPosition;
                    builder.WriteUshort(0); // Placeholder
                    scriptOffsetsToFill.Add((offsetPos, s.Script));
                }

                // Script Tables
                foreach (var item in scriptOffsetsToFill)
                {
                    int scriptStart = builder.CurrentPosition;
                    builder.PatchUshort(item.Pos, (ushort)(scriptStart - scriptListStart));
                    
                    var script = item.Script;
                    // DefaultLangSys
                    int defLangSysOffsetPos = builder.CurrentPosition;
                    builder.WriteUshort(0);
                    
                    // LangSysRecords
                    builder.WriteUshort((ushort)script.LangSysRecords.Count);
                    var langSysOffsetsToFill = new List<(int Pos, LangSys LangSys)>();
                    
                    // Sorted LangSys
                    var langs = new List<(string Tag, LangSys LangSys)>();
                    foreach(var kvp in script.LangSysRecords) langs.Add((kvp.Key, kvp.Value));
                    langs.Sort((a, b) => string.Compare(a.Tag, b.Tag, StringComparison.Ordinal));

                    foreach (var l in langs)
                    {
                        builder.WriteTag(l.Tag);
                        int offsetPos = builder.CurrentPosition;
                        builder.WriteUshort(0);
                        langSysOffsetsToFill.Add((offsetPos, l.LangSys));
                    }

                    // Write DefaultLangSys
                    if (script.DefaultLangSys != null)
                    {
                        int defPos = builder.CurrentPosition;
                        builder.PatchUshort(defLangSysOffsetPos, (ushort)(defPos - scriptStart));
                        SerializeLangSys(builder, script.DefaultLangSys);
                    }

                    // Write LangSys Tables
                    foreach (var lItem in langSysOffsetsToFill)
                    {
                        int lPos = builder.CurrentPosition;
                        builder.PatchUshort(lItem.Pos, (ushort)(lPos - scriptStart));
                        SerializeLangSys(builder, lItem.LangSys);
                    }
                }
            }

            // ================= FEATURE LIST =================
            if (layout.FeatureList != null && layout.FeatureList.Features.Count > 0)
            {
                int featureListStart = builder.CurrentPosition;
                builder.PatchUshort(featureListOffsetPos, (ushort)(featureListStart - headerStart));

                var features = layout.FeatureList.Features; // Already sorted by Tag usually? Fonttools re-sorts? 
                // Spec says: "Alphabetical order by FeatureTag"
                // But FeatureIndex is referenced by LangSys. 
                // If we reorder features, we must have updated indices in LangSys!
                // LayoutSubsetter MUST ensure FeatureIndices are mapped to the FINAL sorted order.
                // Assuming `layout.FeatureList` is already in correct Final Order.
                
                builder.WriteUshort((ushort)features.Count);
                
                var featureOffsetsToFill = new List<(int Pos, Feature Feature)>();
                foreach (var f in features)
                {
                    builder.WriteTag(f.Tag);
                    int offsetPos = builder.CurrentPosition;
                    builder.WriteUshort(0);
                    featureOffsetsToFill.Add((offsetPos, f.Feature));
                }

                foreach (var item in featureOffsetsToFill)
                {
                    int featureStart = builder.CurrentPosition;
                    builder.PatchUshort(item.Pos, (ushort)(featureStart - featureListStart));
                    
                    var feat = item.Feature;
                    builder.WriteUshort(feat.FeatureParamsOffset); // Usually 0
                    builder.WriteUshort((ushort)feat.LookupIndices.Count);
                    foreach (var li in feat.LookupIndices)
                    {
                        builder.WriteUshort(li);
                    }
                }
            }

            // ================= LOOKUP LIST =================
            if (newLookupsModel.Count > 0)
            {
                int lookupListStart = builder.CurrentPosition;
                builder.PatchUshort(lookupListOffsetPos, (ushort)(lookupListStart - headerStart));

                builder.WriteUshort((ushort)newLookupsModel.Count);
                
                var lookupOffsetsToFill = new List<int>();
                for (int i = 0; i < newLookupsModel.Count; i++)
                {
                    int offsetPos = builder.CurrentPosition;
                    builder.WriteUshort(0);
                    lookupOffsetsToFill.Add(offsetPos);
                }

                // Write Lookups and Subtables
                for (int i = 0; i < newLookupsModel.Count; i++)
                {
                    int lookupStart = builder.CurrentPosition;
                    builder.PatchUshort(lookupOffsetsToFill[i], (ushort)(lookupStart - lookupListStart));
                    
                    var lookupModel = newLookupsModel[i];
                    var subtablesRaw = newLookupSubtables[i];

                    builder.WriteUshort(lookupModel.LookupType);
                    builder.WriteUshort(lookupModel.LookupFlag);
                    builder.WriteUshort((ushort)subtablesRaw.Count);
                    
                    var subtableOffsetsToFill = new List<int>();
                    for (int s = 0; s < subtablesRaw.Count; s++)
                    {
                        int offsetPos = builder.CurrentPosition;
                        builder.WriteUshort(0);
                        subtableOffsetsToFill.Add(offsetPos);
                    }
                    
                    // Filtering mark sets (LookupFlag & 0x0010) ? Not doing complex GDEF/MarkSet handling yet.

                    // Write Subtable Data
                    for (int s = 0; s < subtablesRaw.Count; s++)
                    {
                        int subtableStart = builder.CurrentPosition;
                        
                        // Check Offset 16-bit Overflow
                        int offset = subtableStart - lookupStart;
                        if (offset > 65535)
                        {
                            throw new Exception("Lookup Subtable Offset Overflow > 65535. Extension not supported yet.");
                        }

                        builder.PatchUshort(subtableOffsetsToFill[s], (ushort)offset);
                        
                        // Write bytes
                        builder.WriteBytes(subtablesRaw[s]);
                    }
                }
            }

            return builder.ToArray();
        }

        private static void SerializeLangSys(LayoutBinaryBuilder builder, LangSys langSys)
        {
            builder.WriteUshort(langSys.LookupOrderOffset); 
            builder.WriteUshort(langSys.RequiredFeatureIndex);
            builder.WriteUshort((ushort)langSys.FeatureIndices.Count);
            foreach (var fi in langSys.FeatureIndices)
            {
                builder.WriteUshort(fi);
            }
        }
    }

    public class LayoutBinaryBuilder
    {
        private List<byte> _data = new();
        public int CurrentPosition => _data.Count;

        public void WriteUshort(ushort value)
        {
            _data.Add((byte)(value >> 8));
            _data.Add((byte)value);
        }

        public void WriteTag(string tag)
        {
            if (tag.Length != 4) tag = tag.PadRight(4).Substring(0, 4);
            byte[] bytes = Encoding.ASCII.GetBytes(tag);
            _data.AddRange(bytes);
        }

        public void WriteBytes(byte[] bytes)
        {
            _data.AddRange(bytes);
        }

        public void PatchUshort(int offset, ushort value)
        {
            _data[offset] = (byte)(value >> 8);
            _data[offset + 1] = (byte)value;
        }

        public byte[] ToArray() => _data.ToArray();
    }
}
