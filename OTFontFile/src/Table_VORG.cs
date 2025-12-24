using System;
using System.Numerics;

namespace OTFontFile
{
    /// <summary>
    /// Summary description for Table_VORG.
    /// </summary>
    public class Table_VORG : OTTable
    {
        /************************
         * constructors
         */
        
        
        public Table_VORG(OTTag tag, MBOBuffer buf) : base(tag, buf)
        {
        }

        /************************
         * field offset values
         */

        
        public enum FieldOffsets
        {
            majorVersion          = 0,
            minorVersion          = 2,
            defaultVertOriginY    = 4,
            numVertOriginYMetrics = 6,
            StartOfArray          = 8
        }


        /************************
         * internal classes
         */

        public class vertOriginYMetrics
        {
            public ushort glyphIndex;
            public short  vertOriginY;
        }


        /************************
         * accessors
         */
        
        public ushort majorVersion
        {
            get {return m_bufTable.GetUshort((uint)FieldOffsets.majorVersion);}
        }

        public ushort minorVersion
        {
            get {return m_bufTable.GetUshort((uint)FieldOffsets.minorVersion);}
        }

        public short defaultVertOriginY
        {
            get {return m_bufTable.GetShort((uint)FieldOffsets.defaultVertOriginY);}
        }

        public ushort numVertOriginYMetrics
        {
            get {return m_bufTable.GetUshort((uint)FieldOffsets.numVertOriginYMetrics);}
        }

        public vertOriginYMetrics? GetVertOriginYMetrics(uint i)
        {
            vertOriginYMetrics? voym = null;

            if (i < numVertOriginYMetrics)
            {
                voym = new vertOriginYMetrics();

                voym.glyphIndex = m_bufTable.GetUshort((uint)FieldOffsets.StartOfArray + i*4);
                voym.vertOriginY = m_bufTable.GetShort((uint)FieldOffsets.StartOfArray + i*4 + 2);
            }

            return voym;
        }

        public vertOriginYMetrics[]? GetAllVertOriginYMetrics()
        {
            if (numVertOriginYMetrics == 0)
            {
                return null;
            }

            vertOriginYMetrics[] voymArray = new vertOriginYMetrics[numVertOriginYMetrics];

            if (Vector.IsHardwareAccelerated && numVertOriginYMetrics >= 8)
            {
                const int batchSize = 8;
                uint processed = 0;
                while (processed + batchSize <= numVertOriginYMetrics)
                {
                    for (int i = 0; i < batchSize; i++)
                    {
                        uint idx = processed + (uint)i;
                        voymArray[idx] = new vertOriginYMetrics();
                        voymArray[idx].glyphIndex = m_bufTable.GetUshort((uint)FieldOffsets.StartOfArray + idx * 4);
                        voymArray[idx].vertOriginY = m_bufTable.GetShort((uint)FieldOffsets.StartOfArray + idx * 4 + 2);
                    }
                    processed += (uint)batchSize;
                }
                for (uint i = processed; i < numVertOriginYMetrics; i++)
                {
                    voymArray[i] = new vertOriginYMetrics();
                    voymArray[i].glyphIndex = m_bufTable.GetUshort((uint)FieldOffsets.StartOfArray + i * 4);
                    voymArray[i].vertOriginY = m_bufTable.GetShort((uint)FieldOffsets.StartOfArray + i * 4 + 2);
                }
            }
            else
            {
                for (uint i = 0; i < numVertOriginYMetrics; i++)
                {
                    voymArray[i] = new vertOriginYMetrics();
                    voymArray[i].glyphIndex = m_bufTable.GetUshort((uint)FieldOffsets.StartOfArray + i * 4);
                    voymArray[i].vertOriginY = m_bufTable.GetShort((uint)FieldOffsets.StartOfArray + i * 4 + 2);
                }
            }

            return voymArray;
        }


        /************************
         * DataCache class
         */

        public override DataCache GetCache()
        {
            if (m_cache == null)
            {
                m_cache = new VORG_cache();
            }

            return m_cache;
        }
        
        public class VORG_cache : DataCache
        {
            public override OTTable GenerateTable()
            {
                // not yet implemented!
                //return null;
                throw new NotImplementedException();
            }
        }
        

    }
}
