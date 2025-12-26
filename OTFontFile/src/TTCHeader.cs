using System;
using System.Collections.Generic;

namespace OTFontFile
{
    /// <summary>
    /// Summary description for TTCHeader.
    /// </summary>
    public class TTCHeader
    {
        /***************
         * constructors
         */
        public TTCHeader()
        {
            //DirectoryOffsets = new System.Collections.ArrayList();
            DirectoryOffsets = [];
        }

        /************************
         * public static methods
         */
        public static TTCHeader? ReadTTCHeader(OTFile file)
        {
            TTCHeader? ttc = null;

            const int SIZEOF_FIRSTTHREEFIELDS = 12;
            const int SIZEOF_UINT = 4;

            // read the first three fields of the TTC Header
            // starting at the beginning of the file

            var buf = file.ReadPaddedBuffer(0, SIZEOF_FIRSTTHREEFIELDS);
            
            OTTag? tag = null;
            uint version = 0;
            uint DirectoryCount = 0;

            if (buf != null)
            {
                tag = buf.GetTag(0);
                version = buf.GetUint(4);
                DirectoryCount = buf.GetUint(8);
            }
            

            // if the tag and the version and the dir count seem correct then
            // allocate a TTCHeader object and try to read the rest of the header
            // if the tag and the version and the dir count seem correct then
            // allocate a TTCHeader object and try to read the rest of the header
            if (tag.HasValue && tag.Value == OTTagConstants.TTC_TTCF &&
                (version == OTTagConstants.VERSION_1_0 || version == OTTagConstants.VERSION_2_0) &&
                12 + DirectoryCount * SIZEOF_UINT < file.GetFileLength())
            {
                ttc = new TTCHeader
                {
                    TTCTag = tag,
                    version = version,
                    DirectoryCount = DirectoryCount
                };

                // Directory offsets
                buf = file.ReadPaddedBuffer(SIZEOF_FIRSTTHREEFIELDS, DirectoryCount*SIZEOF_UINT);
                if (buf != null)
                {
                    for (uint i=0; i<ttc.DirectoryCount; i++)
                    {
                        uint offset = buf.GetUint(i*SIZEOF_UINT);
                        ttc.DirectoryOffsets.Add(offset);
                    }
                }

                // only read Dsig fields if version 2.0 and last buffer was successfully read
                if ((version == OTTagConstants.VERSION_1_0 || version == OTTagConstants.VERSION_2_0) && buf != null)
                {
                    uint filepos = SIZEOF_FIRSTTHREEFIELDS + DirectoryCount*SIZEOF_UINT;
                    buf = file.ReadPaddedBuffer(filepos, 3*SIZEOF_UINT);
                    if (buf != null)
                    {
                        // DsigTag
                        ttc.DsigTag = buf.GetTag(0);
                        if ( ( version == OTTagConstants.VERSION_1_0 ) && ( ttc.DsigTag.Value != OTTagConstants.TTC_DSIG ) )
                        {
                            // failed v1 trial - reset & bail
                            ttc.DsigTag = null;
                            return ttc;
                        }

                        // DsigLength
                        ttc.DsigLength = buf.GetUint(4);

                        // DsigOffset
                        ttc.DsigOffset = buf.GetUint(8);
                    }
                }
            }

            return ttc;
        }

        /******************
         * member data
         */
        public OTTag? TTCTag;
        public uint version;
        public uint DirectoryCount;
        //public System.Collections.ArrayList DirectoryOffsets;
        public List<uint> DirectoryOffsets;
        // OpenType spec defines three DSIG fields for TTC 1.0 headers,
        // but then states that 1.0 is only used for TTC files WITHOUT digital signatures.
        // So, the code only populates the Dsig fields for version 2.0
        public OTTag? DsigTag;
        public uint  DsigLength; // code only uses this field if version is 2.0!
        public uint  DsigOffset; // code only uses this field if version is 2.0!
    }
}
