using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace OTFontFile
{
    public struct OTF2Dot14
    {
        private short valAsShort;

        public OTF2Dot14(short valAsShort)
        {
            this.valAsShort=valAsShort;
        }

        public short ValAsShort
        {
            get { return this.valAsShort; }
            set { this.valAsShort=value; }

        }

        public ushort Mantissa
        {
            get 
            {
                int sh=Math.Abs(this.valAsShort);
                int mantissa=(sh>>14);
                return (ushort)mantissa;
            }
        }
        public ushort Fraction
        {
            get 
            {
                int sh=Math.Abs(this.valAsShort);
                int fraction=sh&0x3fff;
                return (ushort)fraction;
            }
        }

        public static explicit operator double(OTF2Dot14 valAsShort)
        {
            return ((double)valAsShort.ValAsShort/(double)0x4000);
        }

        public override int GetHashCode()
        {
            return (int)(uint)this;
        }

        static public bool operator ==(OTF2Dot14 f1, OTF2Dot14 f2) => f1.valAsShort == f2.valAsShort;

        static public bool operator != (OTF2Dot14 f1, OTF2Dot14 f2)
        {
            return (!(f1 == f2));
        }

        public override bool Equals(object? obj)
        {
            return (this == (OTF2Dot14)obj!);
        }

    }

    public struct OTFixed
    {
        public short mantissa;
        public ushort fraction;

        public OTFixed(short Mantissa, ushort Fraction)
        {
            mantissa = Mantissa;
            fraction = Fraction;
        }

        public OTFixed(double fixValue)
        {
            mantissa = (short)Math.Round(fixValue, 0);
            fraction = (ushort)Math.Round((fixValue - mantissa) * 65536, 0);
        }
        
        /// <summary>
        /// Construct from raw 32-bit value (e.g., for OTTO signature 0x4F54544F)
        /// </summary>
        public OTFixed(uint rawValue)
        {
            mantissa = (short)(rawValue >> 16);
            fraction = (ushort)(rawValue & 0xFFFF);
        }

        public uint GetUint()
        {
            return (uint)(mantissa<<16 | fraction);
        }

        public double GetDouble()
        {
            return (double)mantissa + (double)fraction/65536.0;
        }

        public string GetHexString()
        {
            return "0x" + mantissa.ToString("X") + fraction.ToString("X");
        }

        public override string ToString()
        {
            double number = Math.Round(this.GetDouble(), 3);
            
            return number.ToString();
        }

        static public bool operator ==(OTFixed f1, OTFixed f2) => f1.GetUint() == f2.GetUint();

        static public bool operator != (OTFixed f1, OTFixed f2)
        {
            return (!(f1 == f2));
        }

        public override bool Equals(object? obj)
        {
            return (this == (OTFixed)obj!);
        }

        public override int GetHashCode()
        {
            return (int)this.GetUint();
        }
        
    }

    /// <summary>
    /// Well-known OpenType font signature and table tag constants.
    /// Values are stored as Big-Endian uint (network byte order).
    /// </summary>
    public static class OTTagConstants
    {
        // Font Signature Tags (sfntVersion)
        public const uint SFNT_VERSION_TRUETYPE = 0x00010000;  // TrueType outlines
        public const uint SFNT_OTTO = 0x4F54544F;              // 'OTTO' - CFF outlines
        public const uint SFNT_TRUE = 0x74727565;              // 'true' - Apple TrueType
        public const uint SFNT_TYP1 = 0x74797031;              // 'typ1' - Apple Type 1

        // Collection Tags
        public const uint TTC_TTCF = 0x74746366;               // 'ttcf' - TrueType Collection
        public const uint TTC_DSIG = 0x44534947;               // 'DSIG' - Digital Signature

        // Version Constants
        public const uint VERSION_1_0 = 0x00010000;
        public const uint VERSION_2_0 = 0x00020000;
        public const uint VERSION_0_5 = 0x00005000;            // maxp CFF version

        // Common Table Tags (for reference, can be used with OTTag comparison)
        public const uint TAG_GLYF = 0x676C7966;               // 'glyf'
        public const uint TAG_CFF  = 0x43464620;               // 'CFF '
        public const uint TAG_CFF2 = 0x43464632;               // 'CFF2'
        public const uint TAG_CBDT = 0x43424454;               // 'CBDT'
        public const uint TAG_EBDT = 0x45424454;               // 'EBDT'
        public const uint TAG_SVG  = 0x53564720;               // 'SVG '
        public const uint TAG_BLOC = 0x626C6F63;               // 'bloc'
        public const uint TAG_CBLC = 0x43424C43;               // 'CBLC'
        public const uint TAG_BDAT = 0x62646174;               // 'bdat'
        public const uint TAG_EBLC = 0x45424C43;               // 'EBLC'
    }


    [DebuggerDisplay("{ToString()}")]
    public readonly struct OTTag : IEquatable<OTTag>
    {
        /***************
         * constructors
         */

        public OTTag(ReadOnlySpan<byte> tagbuf)
        {
            if (tagbuf.Length < 4) throw new ArgumentException("Tag buffer too small");
            m_tag = (uint)(tagbuf[0] << 24 | tagbuf[1] << 16 | tagbuf[2] << 8 | tagbuf[3]);
        }

        public OTTag(ReadOnlySpan<byte> tagbuf, uint offset)
        {
            if (tagbuf.Length < offset + 4) throw new ArgumentException("Tag buffer too small");
            int o = (int)offset;
            m_tag = (uint)(tagbuf[o] << 24 | tagbuf[o+1] << 16 | tagbuf[o+2] << 8 | tagbuf[o+3]);
        }

        public OTTag(uint tagVal)
        {
            m_tag = tagVal;
        }

        // Keep this for string-based initialization
        public OTTag(string tagStr)
        {
             if (tagStr.Length != 4)
             {
                  // Handle shorter/longer strings if necessary, or throw. 
                  // Original code didn't check length explicitly but looped 4 times.
                  // Pad with space if short? Original code didn't.
                  // Let's assume input is correct size or handle safely.
             }
             
             uint val = 0;
             for (int i=0; i<4; i++)
             {
                 byte b = (i < tagStr.Length) ? (byte)tagStr[i] : (byte)0x20; 
                 val = (val << 8) | b;
             }
             m_tag = val;
        }

        /************************
         * operators
         */

        static public implicit operator byte[](OTTag tag)
        {
            return tag.GetBytes();
        }

        static public implicit operator OTTag(uint tagvalue) 
        {
            return new OTTag(tagvalue);
        }

        static public implicit operator uint(OTTag tag)
        {
            return tag.m_tag;
        }

        static public implicit operator OTTag(string value)
        {
            return new OTTag(value);
        }

        static public implicit operator string (OTTag tag)
        {
            return tag.ToString();
        }

        static public bool operator == (OTTag t1, OTTag t2)
        {
            return t1.m_tag == t2.m_tag;
        }

        static public bool operator != (OTTag t1, OTTag t2)
        {
            return t1.m_tag != t2.m_tag;
        }


        /*****************
         * public methods
         */
        
        
        public override bool Equals(object? obj)
        {
            if (obj is OTTag tag)
                return m_tag == tag.m_tag;
            return false;
        }
        
        public bool Equals(OTTag other)
        {
             return m_tag == other.m_tag;
        }

        public override int GetHashCode()
        {
            return (int)m_tag; // Direct mapping
        }
        
        public byte[] GetBytes()
        {
            byte[] buf = new byte[4];
            buf[0] = (byte)((m_tag >> 24) & 0xff);
            buf[1] = (byte)((m_tag >> 16) & 0xff);
            buf[2] = (byte)((m_tag >> 8) & 0xff);
            buf[3] = (byte)(m_tag & 0xff);
            return buf;
        }


        public bool IsValid()
        {
            for (int i=0; i<4; i++)
            {
                int shift = (3-i)*8;
                byte b = (byte)((m_tag >> shift) & 0xff);
                if (b < 32 || b > 126)
                {
                    return false;
                }
            }
            return true;
        }

        public override string ToString()
        {
             char[] c = new char[4];
             c[0] = (char)((m_tag >> 24) & 0xff);
             c[1] = (char)((m_tag >> 16) & 0xff);
             c[2] = (char)((m_tag >> 8) & 0xff);
             c[3] = (char)(m_tag & 0xff);
             return new string(c);
        }

        /**************
         * member data
         */
        readonly uint m_tag; // Stored as Big Endian uint
    }

    public class DirectoryEntry
    {
        public DirectoryEntry()
        {
            m_buf = new MBOBuffer(16);
        }

        public DirectoryEntry(MBOBuffer buf)
        {
            Debug.Assert(buf.GetLength() == 16);
            m_buf = buf;
        }

        public enum FieldOffsets
        {
            tag      = 0,
            checkSum = 4,
            offset   = 8,
            length   = 12
        }

        public DirectoryEntry(DirectoryEntry obj)
        {
            if (m_buf == null)
                m_buf = new MBOBuffer(16);
            // Copy buffer content instead of creating new objects
            // Or just use the property setters
            tag = obj.tag;
            checkSum = obj.checkSum;
            offset   = obj.offset;
            length   = obj.length;
        }

        public OTTag tag
        {
            get { return new OTTag(m_buf.GetUint((uint)FieldOffsets.tag)); }
            set { m_buf.SetUint((uint)value, (uint)FieldOffsets.tag); }
        }

        public uint checkSum
        {
            get {return m_buf.GetUint((uint)FieldOffsets.checkSum);}
            set {m_buf.SetUint(value, (uint)FieldOffsets.checkSum);}
        }

        public uint offset
        {
            get {return m_buf.GetUint((uint)FieldOffsets.offset);}
            set {m_buf.SetUint(value, (uint)FieldOffsets.offset);}
        }

        public uint length
        {
            get {return m_buf.GetUint((uint)FieldOffsets.length);}
            set {m_buf.SetUint(value, (uint)FieldOffsets.length);}
        }

        public MBOBuffer m_buf;
    }

    public class OffsetTable
    {
        // constructor
        public OffsetTable(MBOBuffer buf)
        {
            Debug.Assert(buf.GetLength() == 12);
            m_buf = buf;

            DirectoryEntries = [];
        }

        public OffsetTable(OTFixed version, ushort nTables)
        {
            m_buf = new MBOBuffer(12);

            sfntVersion = version;
            numTables = nTables;

            if (nTables != 0)
            {
                // these values are truly undefined when numTables is zero
                // since there is no power of 2 that is less that or equal to zero
                searchRange   = (ushort)(Util.MaxPower2LE(nTables) * 16);
                entrySelector = Util.Log2(Util.MaxPower2LE(nTables));
                rangeShift    = (ushort)(nTables*16 - searchRange);
            }

            DirectoryEntries = [];
        }

        public enum FieldOffsets
        {
            sfntVersion   = 0,
            numTables     = 4,
            searchRange   = 6,
            entrySelector = 8,
            rangeShift    = 10
        }

        public uint CalcOffsetTableChecksum()
        {
            return m_buf.CalcChecksum();
        }

        public uint CalcDirectoryEntriesChecksum()
        {
            uint sum = 0;

            for (int i=0; i<DirectoryEntries.Count; i++)
            {
                DirectoryEntry de = DirectoryEntries[i];
                sum += de.tag + de.checkSum + de.offset + de.length;
            }

            return sum;
        }

        // accessors

        public    OTFixed sfntVersion
        {
            get {return m_buf.GetFixed((uint)FieldOffsets.sfntVersion);}
            set {m_buf.SetFixed(value, (uint)FieldOffsets.sfntVersion);}
        }

        public    ushort  numTables
        {
            get {return m_buf.GetUshort((uint)FieldOffsets.numTables);}
            set {m_buf.SetUshort(value, (uint)FieldOffsets.numTables);}
        }

        public    ushort  searchRange
        {
            get {return m_buf.GetUshort((uint)FieldOffsets.searchRange);}
            set {m_buf.SetUshort(value, (uint)FieldOffsets.searchRange);}
        }

        public    ushort    entrySelector
        {
            get {return m_buf.GetUshort((uint)FieldOffsets.entrySelector);}
            set {m_buf.SetUshort(value, (uint)FieldOffsets.entrySelector);}
        }

        public    ushort    rangeShift
        {
            get {return m_buf.GetUshort((uint)FieldOffsets.rangeShift);}
            set {m_buf.SetUshort(value, (uint)FieldOffsets.rangeShift);}
        }


        // member data

        public MBOBuffer m_buf;
        public List<DirectoryEntry> DirectoryEntries;   // System.Collections.ArrayList
    }

}
