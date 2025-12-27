using System.Collections.Generic;
using System.Text;
using OTFontFile;

namespace OTFontFile.Subsetting.Layout
{
    // ==========================================
    // Lightweight Object Models for Layout Graph
    // ==========================================

    public class LayoutTable
    {
        public ScriptList? ScriptList { get; set; }
        public FeatureList? FeatureList { get; set; }
        public LookupList? LookupList { get; set; }

        public static LayoutTable Parse(MBOBuffer buf, uint offset)
        {
            var table = new LayoutTable();
            // Check bounds? MBOBuffer usually throws if OOB.
            uint majorVersion = buf.GetUshort(offset);
            uint minorVersion = buf.GetUshort(offset + 2);
            uint scriptListOffset = buf.GetUshort(offset + 4);
            uint featureListOffset = buf.GetUshort(offset + 6);
            uint lookupListOffset = buf.GetUshort(offset + 8);

            // Handle v1.1 featureVariationsOffset (optional)
            // uint featureVariationsOffset = (majorVersion == 1 && minorVersion == 1) ? buf.GetUint(offset + 10) : 0; // GetUint? MBOBuffer has GetUint usually

            if (scriptListOffset != 0)
                table.ScriptList = ScriptList.Parse(buf, offset + scriptListOffset);
            
            if (featureListOffset != 0)
                table.FeatureList = FeatureList.Parse(buf, offset + featureListOffset);
            
            if (lookupListOffset != 0)
                table.LookupList = LookupList.Parse(buf, offset + lookupListOffset);

            return table;
        }
    }

    public class ScriptList
    {
        public Dictionary<string, Script> Scripts { get; } = new();

        public static ScriptList Parse(MBOBuffer buf, uint offset)
        {
            var list = new ScriptList();
            ushort count = buf.GetUshort(offset);
            
            for (int i = 0; i < count; i++)
            {
                uint recOffset = offset + 2 + (uint)i * 6;
                string tag = GetTag(buf, recOffset);
                uint scriptOffset = buf.GetUshort(recOffset + 4);
                
                if (scriptOffset != 0)
                {
                    list.Scripts[tag] = Script.Parse(buf, offset + scriptOffset);
                }
            }
            return list;
        }

        private static string GetTag(MBOBuffer buf, uint offset)
        {
            // MBOBuffer might not have GetTag, implementing manually
            byte[] bytes = new byte[4];
            bytes[0] = buf.GetByte(offset);
            bytes[1] = buf.GetByte(offset + 1);
            bytes[2] = buf.GetByte(offset + 2);
            bytes[3] = buf.GetByte(offset + 3);
            return Encoding.ASCII.GetString(bytes);
        }
    }

    public class Script
    {
        public LangSys? DefaultLangSys { get; set; }
        public Dictionary<string, LangSys> LangSysRecords { get; } = new();

        public static Script Parse(MBOBuffer buf, uint offset)
        {
            var script = new Script();
            uint defaultLangSysOffset = buf.GetUshort(offset);
            if (defaultLangSysOffset != 0)
            {
                script.DefaultLangSys = LangSys.Parse(buf, offset + defaultLangSysOffset);
            }

            ushort count = buf.GetUshort(offset + 2);
            for (int i = 0; i < count; i++)
            {
                uint recOffset = offset + 4 + (uint)i * 6;
                string tag = GetTag(buf, recOffset);
                uint langSysOffset = buf.GetUshort(recOffset + 4);
                
                if (langSysOffset != 0)
                {
                    script.LangSysRecords[tag] = LangSys.Parse(buf, offset + langSysOffset);
                }
            }
            return script;
        }
        
        private static string GetTag(MBOBuffer buf, uint offset)
        {
            byte[] bytes = new byte[4];
            bytes[0] = buf.GetByte(offset);
            bytes[1] = buf.GetByte(offset + 1);
            bytes[2] = buf.GetByte(offset + 2);
            bytes[3] = buf.GetByte(offset + 3);
            return Encoding.ASCII.GetString(bytes);
        }
    }

    public class LangSys
    {
        public ushort LookupOrderOffset { get; set; } // Usually NULL
        public ushort RequiredFeatureIndex { get; set; }
        public List<ushort> FeatureIndices { get; } = new();

        public static LangSys Parse(MBOBuffer buf, uint offset)
        {
            var langSys = new LangSys();
            langSys.LookupOrderOffset = buf.GetUshort(offset);
            langSys.RequiredFeatureIndex = buf.GetUshort(offset + 2);
            
            ushort count = buf.GetUshort(offset + 4);
            for (int i = 0; i < count; i++)
            {
                langSys.FeatureIndices.Add(buf.GetUshort(offset + 6 + (uint)i * 2));
            }
            return langSys;
        }
    }

    public class FeatureList
    {
        public List<FeatureRecord> Features { get; } = new();

        public static FeatureList Parse(MBOBuffer buf, uint offset)
        {
            var list = new FeatureList();
            ushort count = buf.GetUshort(offset);
            
            for (int i = 0; i < count; i++)
            {
                uint recOffset = offset + 2 + (uint)i * 6;
                string tag = GetTag(buf, recOffset);
                uint featureOffset = buf.GetUshort(recOffset + 4);
                
                if (featureOffset != 0)
                {
                    var feature = Feature.Parse(buf, offset + featureOffset);
                    list.Features.Add(new FeatureRecord(tag, feature, i));
                }
            }
            return list;
        }

        private static string GetTag(MBOBuffer buf, uint offset)
        {
            byte[] bytes = new byte[4];
            bytes[0] = buf.GetByte(offset);
            bytes[1] = buf.GetByte(offset + 1);
            bytes[2] = buf.GetByte(offset + 2);
            bytes[3] = buf.GetByte(offset + 3);
            return Encoding.ASCII.GetString(bytes);
        }
    }

    public record FeatureRecord(string Tag, Feature Feature, int OriginalIndex);

    public class Feature
    {
        public ushort FeatureParamsOffset { get; set; } // Usually NULL
        public List<ushort> LookupIndices { get; } = new();

        public static Feature Parse(MBOBuffer buf, uint offset)
        {
            var feature = new Feature();
            feature.FeatureParamsOffset = buf.GetUshort(offset);
            
            ushort count = buf.GetUshort(offset + 2);
            for (int i = 0; i < count; i++)
            {
                feature.LookupIndices.Add(buf.GetUshort(offset + 4 + (uint)i * 2));
            }
            return feature;
        }
    }

    public class LookupList
    {
        public List<Lookup> Lookups { get; } = new();

        public static LookupList Parse(MBOBuffer buf, uint offset)
        {
            var list = new LookupList();
            ushort count = buf.GetUshort(offset);
            
            for (int i = 0; i < count; i++)
            {
                uint lookupOffset = buf.GetUshort(offset + 2 + (uint)i * 2);
                if (lookupOffset != 0)
                {
                    list.Lookups.Add(Lookup.Parse(buf, offset + lookupOffset, i));
                }
            }
            return list;
        }
    }

    public class Lookup
    {
        public ushort LookupType { get; set; }
        public ushort LookupFlag { get; set; }
        public List<uint> SubtableOffsets { get; } = new();
        public uint BaseOffset { get; set; } // Base offset of this Lookup table in original file
        public int OriginalIndex { get; set; }

        public static Lookup Parse(MBOBuffer buf, uint offset, int index)
        {
            var lookup = new Lookup();
            lookup.BaseOffset = offset;
            lookup.OriginalIndex = index;
            lookup.LookupType = buf.GetUshort(offset);
            lookup.LookupFlag = buf.GetUshort(offset + 2);
            
            ushort count = buf.GetUshort(offset + 4);
            for (int i = 0; i < count; i++)
            {
                uint subtableOffset = buf.GetUshort(offset + 6 + (uint)i * 2);
                // Subtable offsets are relative to Lookup table
                lookup.SubtableOffsets.Add(offset + subtableOffset);
            }
            return lookup;
        }
    }
}
