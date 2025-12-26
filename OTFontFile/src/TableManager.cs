using System;
using System.Collections.Generic;

namespace OTFontFile
{
    /// <summary>
    /// Summary description for TableManager.
    /// </summary>
    public class TableManager(OTFile file)
    {
        /************************
         * constructors
         */
        
        
        //public TableManager(OTFile file)
        //{
        //    m_file = file;
        //    CachedTables = new System.Collections.ArrayList();
        //}


        /************************
         * public methods
         */
        
        
        private static readonly HashSet<uint> s_largeTableTags = new()
        {
            0x676C7966, // glyf
            0x43464620, // CFF 
            0x43464632, // CFF2
            0x43424454, // CBDT
            0x45424454, // EBDT
            0x53564720  // SVG 
        };

        private static bool ShouldUsePooledBuffer(DirectoryEntry de)
        {
            uint tag = de.tag;
            if (s_largeTableTags.Contains(tag))
                return true;
            if (de.length > 64 * 1024)
                return true;
            return false;
        }

        private static bool ShouldUseLazyLoad(DirectoryEntry de)
        {
            uint tag = de.tag;
            if (s_largeTableTags.Contains(tag))
                return true;
            return false;
        }

        public OTTable? GetTable(DirectoryEntry de)
        {
            OTTable? table = GetTableFromCache(de);

            if (table == null)
            {
                if (   de.length != 0 
                    && de.offset != 0 
                    && de.offset < m_file.GetFileLength()
                    && de.offset + de.length <= m_file.GetFileLength())
                {
                    if (ShouldUseLazyLoad(de))
                    {
                        table = CreateTableObjectLazy(de.tag, de);
                    }
                    else
                    {
                        var buf = ShouldUsePooledBuffer(de)
                            ? m_file.ReadPooledBuffer(de.offset, de.length)
                            : m_file.ReadPaddedBuffer(de.offset, de.length);

                        if (buf != null)
                        {
                            table = CreateTableObject(de.tag, buf);
                        }
                    }

                    if (table != null)
                    {
                        CachedTables.Add(table);
                    }
                }
            }

            return table;
        }

        public static string GetUnaliasedTableName(OTTag? tag)
        {
            if (tag is null) return "";

            string sName = tag;
            if (sName == "bloc" || sName == "CBLC" )
            {
                sName = "EBLC";
            }
            else if (sName == "bdat" || sName == "CBDT" )
            {
                sName = "EBDT";
            }
            
            return sName;
        }

        static public string [] GetKnownOTTableTypes()
        {
            string [] sTables =
                [
                    "avar",
                    "BASE",
                    "CBDT",
                    "CBLC",
                    "CFF ",
                    "CFF2",
                    "cmap",
                    "COLR",
                    "CPAL",
                    "cvar",
                    "cvt ",
                    "DSIG",
                    "EBDT",
                    "EBLC",
                    "EBSC",
                    "fpgm",
                    "fvar",
                    "gasp",
                    "GDEF",
                    "glyf",
                    "GPOS",
                    "GSUB",
                    "gvar",
                    "hdmx",
                    "head",
                    "hhea",
                    "hmtx",
                    "HVAR",
                    "JSTF",
                    "kern",
                    "loca",
                    "LTSH",
                    "MATH",
                    "maxp",
                    "meta",
                    "MERG",
                    "MVAR",
                    "name",
                    "OS/2",
                    "PCLT",
                    "post",
                    "prep",
                    "sbix",
                    "STAT",
                    "SVG ",
                    "VDMX",
                    "vhea",
                    "vmtx",
                    "VORG",
                    "VVAR"
                ];

            return sTables;
        }

        static public bool IsKnownOTTableType(OTTag tag)
        {
            bool bFound = false;

            string [] sTables = GetKnownOTTableTypes();
            for (uint i=0; i<sTables.Length; i++)
            {
                if (sTables[i] == (string)tag)
                {
                    bFound = true;
                    break;
                }
            }

            return bFound;
        }

        public virtual OTTable CreateTableObject(OTTag tag, MBOBuffer buf)
        {
            //OTTable? table = null;

            string sName = GetUnaliasedTableName(tag);

            OTTable table = sName switch
            {
                //case "avar": table = new Table_GenericOT(tag, buf); break;
                "BASE" => new Table_BASE(tag, buf),
                "CFF " => new Table_CFF(tag, buf),
                //case "CFF2": table = new Table_GenericOT(tag, buf); break;
                "cmap" => new Table_cmap(tag, buf),
                //case "COLR": table = new Table_GenericOT(tag, buf); break;
                //case "CPAL": table = new Table_GenericOT(tag, buf); break;
                //case "cvar": table = new Table_GenericOT(tag, buf); break;
                "cvt " => new Table_cvt(tag, buf),
                "DSIG" => new Table_DSIG(tag, buf),
                "EBDT" => new Table_EBDT(tag, buf),
                "EBLC" => new Table_EBLC(tag, buf),
                "EBSC" => new Table_EBSC(tag, buf),
                "fpgm" => new Table_fpgm(tag, buf),
                //case "fvar": table = new Table_GenericOT(tag, buf); break;
                "gasp" => new Table_gasp(tag, buf),
                "GDEF" => new Table_GDEF(tag, buf),
                "glyf" => new Table_glyf(tag, buf),
                "GPOS" => new Table_GPOS(tag, buf),
                "GSUB" => new Table_GSUB(tag, buf),
                //case "gvar": table = new Table_GenericOT(tag, buf); break;
                "hdmx" => new Table_hdmx(tag, buf),
                "head" => new Table_head(tag, buf),
                "hhea" => new Table_hhea(tag, buf),
                "hmtx" => new Table_hmtx(tag, buf),
                //case "HVAR": table = new Table_GenericOT(tag, buf); break;
                "JSTF" => new Table_JSTF(tag, buf),
                "kern" => new Table_kern(tag, buf),
                "loca" => new Table_loca(tag, buf),
                "LTSH" => new Table_LTSH(tag, buf),
                //case "MATH": table = new Table_GenericOT(tag, buf); break;
                "maxp" => new Table_maxp(tag, buf),
                //case "MERG": table = new Table_GenericOT(tag, buf); break;
                "meta" => new Table_meta(tag, buf),
                //case "MVAR": table = new Table_GenericOT(tag, buf); break;
                "name" => new Table_name(tag, buf),
                "OS/2" => new Table_OS2(tag, buf),
                "PCLT" => new Table_PCLT(tag, buf),
                "post" => new Table_post(tag, buf),
                "prep" => new Table_prep(tag, buf),
                //case "sbix": table = new Table_GenericOT(tag, buf); break;
                //case "STAT": table = new Table_GenericOT(tag, buf); break;
                "SVG " => new Table_SVG(tag, buf),
                "VDMX" => new Table_VDMX(tag, buf),
                "vhea" => new Table_vhea(tag, buf),
                "vmtx" => new Table_vmtx(tag, buf),
                "VORG" => new Table_VORG(tag, buf),
                //case "VVAR": table = new Table_GenericOT(tag, buf); break;
                //case "Zapf": table = new Table_Zapf(tag, buf); break;
                _ => new Table__Unknown(tag, buf),
            };
            return table;
        }

        public virtual OTTable CreateTableObjectLazy(OTTag tag, DirectoryEntry de)
        {
            string sName = GetUnaliasedTableName(tag);

            return sName switch
            {
                "glyf" => new Table_glyf(de, m_file),
                "CFF " => new Table_CFF(de, m_file),
                // CFF2 暂未实现，跳过
                "SVG " => new Table_SVG(de, m_file),
                "EBDT" => new Table_EBDT(de, m_file),
                // CBDT 别名为 EBDT，已在 GetUnaliasedTableName 中处理
                _ => throw new NotSupportedException($"Lazy loading not supported for table: {sName}"),
            };
        }

        /************************
         * protected methods
         */

        protected OTTable? GetTableFromCache(DirectoryEntry de)
        {
            OTTable? ot = null;

            for (int i=0; i<CachedTables.Count; i++)
            {
                OTTable temp = CachedTables[i];
                if (temp.MatchFileOffsetLength(de.offset, de.length))
                {
                    ot = temp;
                    break;
                }
            }

            return ot;
        }

        /************************
         * member data
         */
        
        OTFile m_file = file;

        //System.Collections.ArrayList CachedTables;
        List<OTTable> CachedTables = [];
    }
}
