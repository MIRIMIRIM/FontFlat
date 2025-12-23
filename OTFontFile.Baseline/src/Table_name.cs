using System;
using System.Diagnostics;
using System.Text;
using System.Collections.Generic;


namespace Baseline
{
    /// <summary>
    /// Summary description for Table_name.
    /// </summary>
    public class Table_name(OTTag tag, MBOBuffer buf) : OTTable(tag, buf)
    {

        /************************
         * field offset values
         */

        public enum FieldOffsets
        {
            FormatSelector    = 0,
            NumberNameRecords = 2,
            OffsetToStrings   = 4,
            NameRecords       = 6
        }


        /************************
         * name record class
         */

        public class NameRecord(ushort offset, MBOBuffer bufTable)
        {
            public enum FieldOffsets
            {
                PlatformID   = 0,
                EncodingID   = 2,
                LanguageID   = 4,
                NameID       = 6,
                StringLength = 8,
                StringOffset = 10
            }

            public ushort PlatformID
            {
                get {return m_bufTable.GetUshort(m_offsetNameRecord + (uint)FieldOffsets.PlatformID);}
            }

            public ushort EncodingID
            {
                get {return m_bufTable.GetUshort(m_offsetNameRecord + (uint)FieldOffsets.EncodingID);}
            }

            public ushort LanguageID
            {
                get {return m_bufTable.GetUshort(m_offsetNameRecord + (uint)FieldOffsets.LanguageID);}
            }

            public ushort NameID
            {
                get {return m_bufTable.GetUshort(m_offsetNameRecord + (uint)FieldOffsets.NameID);}
            }

            public ushort StringLength
            {
                get {return m_bufTable.GetUshort(m_offsetNameRecord + (uint)FieldOffsets.StringLength);}
            }

            public ushort StringOffset
            {
                get {return m_bufTable.GetUshort(m_offsetNameRecord + (uint)FieldOffsets.StringOffset);}
            }


            readonly ushort m_offsetNameRecord = offset;
            readonly MBOBuffer m_bufTable = bufTable;
        }


        /************************
         * name constant
         */

        public enum PlatformID : ushort
        {
            Unicode = 0,
            Macintosh = 1,  // Discouraged
            ISO = 2,        // Deprecated
            Windows = 3,
            Custom = 4,     // should not be used in new fonts
        }

        // Must include: Family (or Preferred Family), Style (or Preferred Style), Full, PostScript
        public enum NameID : ushort
        {
            copyright = 0,
            familyName = 1,
            subfamilyName = 2,
            uniqueSubfamilyIdentifier = 3,
            fullName = 4,
            versionString = 5,
            postScriptName = 6,
            trademark = 7,
            manufacturerName = 8,
            designer = 9,
            description = 10,
            vendorUri = 11,
            designerUri = 12,
            licenseDescription = 13,
            licenseInfoUri = 14,
            typographicFamilyName = 16, // Preferred Family
            typographicSubfamilyName = 17,  // Preferred Subfamily
            compatibleFullName = 18,    // MacOS Only
            sampleText = 19,
            postScriptCIDFindfontName = 20,
            wwsFamilyName = 21,
            wwsSubfamilyName = 22,
            lightBackgroundPalette = 23,
            darkBackgroundPalette = 24,
            variationsPostScriptNamePrefix = 25,
        }

        public enum EncodingIDMacintosh : ushort
        {
            Roman = 0,
            Japanese = 1,
            Traditional_Chinese = 2,
            Korean = 3,
            Arabic = 4,
            Hebrew = 5,
            Greek = 6,
            Russian = 7,
            RSymbol = 8,
            Devanagari = 9,
            Gurmukhi = 10,
            Gujarati = 11,
            Oriya = 12,
            Bengali = 13,
            Tamil = 14,
            Telugu = 15,
            Kannada = 16,
            Malayalam = 17,
            Sinhalese = 18,
            Burmese = 19,
            Khmer = 20,
            Thai = 21,
            Laotian = 22,
            Georgian = 23,
            Armenian = 24,
            Simplified_Chinese = 25,
            Tibetan = 26,
            Mongolian = 27,
            Geez = 28,
            Slavic = 29,
            Vietnamese = 30,
            Sindhi = 31,
            Uninterpreted = 32
        };

        public enum EncodingIDWindows
        {
            Symbol = 0,
            Unicode_BMP = 1,
            ShiftJIS = 2,
            PRC = 3,
            Big5 = 4,
            Wansung = 5,
            Johab = 6,
            Reserved = 7,   // 7-9 is Reserved
            Unicode_full_repertoire = 10
        }

        public enum LanguageIDMacintosh : ushort
        {
            en = 0,
            ja = 11,
            zh_Hans = 19,
            zh_Hant = 33
        }

        public enum LanguageIDWindows : ushort
        {
            // https://referencesource.microsoft.com/#mscorlib/system/globalization/regioninfo.cs,171
            // https://www.iana.org/assignments/language-subtag-registry/language-subtag-registry
            // https://fonttools.readthedocs.io/en/latest/_modules/fontTools/ttLib/tables/_n_a_m_e.html

            ca_ES = 0x0403, // Catalan (Catalan)
            zh_Hant_TW = 0x0404, // Chinese (Taiwan)
            cs_CZ = 0x0405, // Czech (Czech Republic)
            da_DK = 0x0406, // Danish (Denmark)
            de_DE = 0x0407, // German (Germany)
            el_GR = 0x0408, // Greek (Greece)
            en_US = 0x0409, // English (United States)
            es_ES = 0x040A, // Spanish (Traditional Sort) (Spain)
            fi_FI = 0x040B, // Finnish (Finland)
            fr_FR = 0x040C, // French (France)
            hu_HU = 0x040E, // Hungarian (Hungary)
            it_IT = 0x0410, // Italian (Italy)
            ja_JP = 0x0411, // Japanese (Japan)
            nl_NL = 0x0413, // Dutch (Netherlands)
            nb_NO = 0x0414, // Norwegian (Bokm?) (Norway)
            pl_PL = 0x0415, // Polish (Poland)
            pt_BR = 0x0416, // Portuguese (Brazil)
            ru_RU = 0x0419, // Russian (Russia)
            sk_SK = 0x041B, // Slovak (Slovakia)
            sv_SE = 0x041D, // Swedish (Sweden)
            sl_SI = 0x0424, // Slovenian (Slovenia)
            eu_ES = 0x042D, // Basque (Basque)
            zh_Hans_CN = 0x0804, // Chinese (People's Republic of China)
            es_MX = 0x080A, // Spanish (Mexico)
            pt_PT = 0x0816, // Portuguese (Portugal)
            zh_Hant_HK = 0x0C04, // Chinese (Hong Kong S.A.R.)
            fr_CA = 0x0C0C, // French (Canada)
            zh_Hant_MO = 0x1404, // Chinese (Macau S.A.R.)
            Any = 0xffff,
        }


        /************************
         * utility methods
         */

        static public int MacEncIdToCodePage(ushort MacEncodingID)
        {
            /*
                Q187858 INFO: Macintosh Code Pages Supported Under Windows NT
                
                10000 (MAC - Roman)
                10001 (MAC - Japanese)
                10002 (MAC - Traditional Chinese Big5)
                10003 (MAC - Korean)
                10004 (MAC - Arabic)
                10005 (MAC - Hebrew)
                10006 (MAC - Greek I)
                10007 (MAC - Cyrillic)
                10008 (MAC - Simplified Chinese GB 2312)
                10010 (MAC - Romania)
                10017 (MAC - Ukraine)
                10029 (MAC - Latin II)
                10079 (MAC - Icelandic)
                10081 (MAC - Turkish)
                10082 (MAC - Croatia) 
            */
            // NOTE: code pages 10010 through 10082
            // don't seem to map to Encoding IDs in the OT spec

            int MacCodePage = -1;


            switch (MacEncodingID)
            {
                case 0: // Roman
                    MacCodePage = 10000;
                    break;
                case 1: // Japanese
                    MacCodePage = 10001;
                    break;
                case 2: // Chinese (Traditional)
                    MacCodePage = 10002;
                    break;
                case 3: // Korean
                    MacCodePage = 10003;
                    break;
                case 4: // Arabic
                    MacCodePage = 10004;
                    break;
                case 5: // Hebrew
                    MacCodePage = 10005;
                    break;
                case 6: // Greek
                    MacCodePage = 10006;
                    break;
                case 7: // Russian
                    MacCodePage = 10007;
                    break;

                case 25: // Chinese (Simplified)
                    MacCodePage = 10008;
                    break;

                default:
                    Debug.Assert(false, "unsupported text encoding");
                    break;
            }

            return MacCodePage;

        }

        static public int MSEncIdToCodePage(ushort MSEncID)
        {
            int nCodePage = -1;

            switch(MSEncID)
            {
                case 2: // ShiftJIS
                    nCodePage = 932;
                    break;
                case 3: // PRC
                    nCodePage = 936;
                    break;
                case 4: // Big5
                    nCodePage = 950;
                    break;
                case 5: // Wansung
                    nCodePage = 949;
                    break;
                case 6: // Johab
                    nCodePage = 1361;
                    break;
            }

            return nCodePage;
        }

        static public int MacLangIdToCodePage(ushort MacLanguageID)
        {
            return MacLanguageID switch
            {
                (ushort)LanguageIDMacintosh.ja => 10001,
                (ushort)LanguageIDMacintosh.zh_Hans => 10008,
                (ushort)LanguageIDMacintosh.zh_Hant => 10002,
                _ => 10000,
            };
        }

        [Obsolete("Use Encoding.GetString() maybe better.")]
        public static string GetUnicodeStrFromBuf(byte[] buf, Encoding enc)
        {
            Decoder dec = enc.GetDecoder();
            int nChars = dec.GetCharCount(buf, 0, buf.Length);
            char[] destbuf = new char[nChars];
            dec.GetChars(buf, 0, buf.Length, destbuf, 0);
            return new string(destbuf);
        }

        static public string GetUnicodeStrFromCodePageBuf(byte[] buf, int codepage)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            Encoding enc = Encoding.GetEncoding(codepage);
            return enc.GetString(buf);
        }

        static protected byte[] GetCodePageBufFromUnicodeStr( string sNameString, int nCodepage )
        {
            byte[] bString;

            Encoding enc = Encoding.GetEncoding( nCodepage );
            bString = enc.GetBytes( sNameString );
            
            return bString;
        }

        static public string? DecodeString(ushort PlatID, ushort EncID, ushort LangID, byte[] EncodedStringBuf)
        {
            string? s = null;

            if (PlatID == (ushort)PlatformID.Unicode)
            {
                var ue = new UnicodeEncoding(true, false);
                s = ue.GetString(EncodedStringBuf);
            }
            else if (PlatID == (ushort)PlatformID.Macintosh)
            {
                // Some old fonts maybe use encoding = 0 encode cjk characters. Maybe can use UTF-Unknown.
                int nMacCodePage;
                if (EncID == 0)
                {
                    // old japanese fonts
                    nMacCodePage = MacLangIdToCodePage(LangID);
                }
                else
                {
                    nMacCodePage = MacEncIdToCodePage(EncID);
                }
                if (nMacCodePage != -1)
                {
                    s = GetUnicodeStrFromCodePageBuf(EncodedStringBuf, nMacCodePage);
                }
            }
            else if (PlatID == (ushort)PlatformID.Windows)
            {
                if (EncID == 0 || // symbol - strings identified as symbol encoded strings 
                                  // aren't symbol encoded, they're unicode encoded!!!
                    EncID == 1 || // unicode
                    EncID == 10 ) // unicode with surrogate support for UCS-4
                {
                    var ue = new UnicodeEncoding(true, false);
                    s = ue.GetString(EncodedStringBuf);
                }
                else if (EncID >= 2 && EncID <= 6)
                {
                    int nCodePage = MSEncIdToCodePage(EncID);
                    s = GetUnicodeStrFromCodePageBuf(EncodedStringBuf, nCodePage);
                }
                else
                {
                    //Debug.Assert(false, "unsupported text encoding");
                }
            }

            // Some old japanese fonts maybe have weird character in namestring
            if (s is not null && s.Contains('\0'))
            {
                s = s.Replace("\0", "");
            }

            return s;
        }

        static protected byte[]? EncodeString(string s, ushort PlatID, ushort EncID)
        {
            byte[]? buf = null;

            if(PlatID == 0) // unicode
            {
                var ue = new UnicodeEncoding( true, false );
                buf = ue.GetBytes( s );
            }
            else if (PlatID == 1 ) // Mac
            {
                int nCodePage = MacEncIdToCodePage(EncID);
                if( nCodePage != -1 )
                {
                    buf = GetCodePageBufFromUnicodeStr(s, nCodePage);
                }
            }
            else if (PlatID == 3 ) // MS
            {
                if (EncID == 0 || // symbol - strings identified as symbol encoded strings 
                                  // aren't symbol encoded, they're unicode encoded!!!
                    EncID == 1 || // unicode
                    EncID == 10 ) // unicode with surrogate support for UCS-4
                {
                    var ue = new UnicodeEncoding( true, false );
                    buf = ue.GetBytes(s);
                }
                else if (EncID >= 2 || EncID <= 6)
                {
                    int nCodePage = MSEncIdToCodePage(EncID);
                    if( nCodePage != -1 )
                    {
                        buf = GetCodePageBufFromUnicodeStr(s, nCodePage);
                    }
                }
                else
                {
                    //Debug.Assert(false, "unsupported text encoding");
                }
            }

            return buf;
        }

        public byte[]? GetBuffer(ushort PlatID, ushort EncID, ushort LangID, ushort NameID, out ushort curPlatID, out ushort curEncID, out ushort curLangID)
        {
            // !!! NOTE: a value of 0xffff for PlatID, EncID, or LangID is used !!!
            // !!! as a wildcard and will match any value found in the table    !!!

            curPlatID = ushort.MaxValue;
            curEncID  = ushort.MaxValue;
            curLangID = ushort.MaxValue;

            for (uint i = 0; i < NumberNameRecords; i++)
            {
                var nr = GetNameRecord(i);
                if (nr != null)
                {
                    if ((PlatID == 0xffff || nr.PlatformID == PlatID) &&
                        (EncID  == 0xffff || nr.EncodingID == EncID)  &&
                        (LangID == 0xffff || nr.LanguageID == LangID) &&
                        nr.NameID == NameID)
                    {
                        var buf = GetEncodedString(nr);
                        curPlatID = nr.PlatformID;
                        curEncID = nr.EncodingID;
                        curLangID = nr.LanguageID;
                        return buf;
                    }
                }
            }
            
            return null;
        }

        public string? GetString(ushort PlatID, ushort EncID, ushort LangID, ushort NameID)
        {
            var buf = GetBuffer(PlatID, EncID, LangID, NameID, out ushort curPlatID, out ushort curEncID, out ushort curLangID);
            return buf != null ? DecodeString(curPlatID, curEncID, curLangID, buf) : null;
        }

        public string? GetString(PlatformID PlatID, EncodingIDMacintosh EncID, LanguageIDMacintosh LangID, NameID NameID) => GetString((ushort)PlatID, (ushort)EncID, (ushort)LangID, (ushort)NameID);

        public string? GetString(PlatformID PlatID, EncodingIDWindows EncID, LanguageIDWindows LangID, NameID NameID) => GetString((ushort)PlatID, (ushort)EncID, (ushort)LangID, (ushort)NameID);

        [Obsolete("Please use GetFullNameString()")]
        public string? GetNameString() => GetFullNameString();

        public string? GetFullNameString() => GetGeneralStringByNameID(NameID.fullName, true);

        public string? GetVersionString() => GetGeneralStringByNameID(NameID.versionString, true);

        public string? GetStyleString() => GetGeneralStringByNameID(NameID.subfamilyName, false);

        /// <summary>
        /// Get general display string by NameID, platform_encoding_language priority is MS_any_English, MS_any_other, Mac_Roman_English.
        /// </summary>
        /// <param name="nameId">NameID</param>
        /// <param name="validate">Verify if there is a surrogate pair in the decoded string</param>
        /// <returns></returns>
        public string? GetGeneralStringByNameID(ushort nameId, bool validate)
        {
            string? str = null;
            try
            {
                str = GetString((ushort)PlatformID.Windows, 0xffff, (ushort)LanguageIDWindows.en_US, nameId);  // MS, any encoding, English, nameID
                str ??= GetString((ushort)PlatformID.Windows, 0xffff, 0xffff, nameId); // MS, any encoding, any language, nameID
                str ??= GetString((ushort)PlatformID.Macintosh, (ushort)EncodingIDMacintosh.Roman, (ushort)LanguageIDMacintosh.en, nameId); // mac, roman, English, nameID
                
                if (validate)
                {
                    var span = str!.AsSpan();

                    // validate surrogate content
                    for (int i = 0; i < span.Length - 1; i++)
                    {
                        if ((char.IsHighSurrogate(span[i]) && !char.IsLowSurrogate(span[i + 1]))
                            || (!char.IsHighSurrogate(span[i]) && char.IsLowSurrogate(span[i + 1])))
                        {
                            str = null;
                            break;
                        }
                    }
                }
            }
            catch
            {
            }
            return str;
        }

        public string? GetGeneralStringByNameID(NameID nameID, bool validate) => GetGeneralStringByNameID((ushort)nameID, validate);


        /************************
         * property accessors
         */

        public ushort FormatSelector
        {
            get {return m_bufTable.GetUshort((uint)FieldOffsets.FormatSelector);}
        }

        public ushort NumberNameRecords
        {
            get {return m_bufTable.GetUshort((uint)FieldOffsets.NumberNameRecords);}
        }

        public ushort OffsetToStrings
        {
            get    {return m_bufTable.GetUshort((uint)FieldOffsets.OffsetToStrings);}
        }

        public NameRecord? GetNameRecord(uint i)
        {
            if (i < NumberNameRecords)
            {
                uint offset = (uint)FieldOffsets.NameRecords + i*12;
                if (offset + 12 < m_bufTable.GetLength())
                {
                    return new NameRecord((ushort)offset, m_bufTable);
                }
            }

            return null;
        }

        public byte[]? GetEncodedString(NameRecord nr)
        {
            byte[]? buf = null;
            int offset = OffsetToStrings + nr.StringOffset;
            if (offset + nr.StringLength - 1 <= m_bufTable.GetLength())
            {
                buf = new byte[nr.StringLength];
                System.Buffer.BlockCopy(m_bufTable.GetBuffer(), offset, buf, 0, nr.StringLength);
            }
            return buf;
        }


        /************************
         * DataCache class
         */

        public override DataCache GetCache()
        {
            if (m_cache == null)
            {
                m_cache = new name_cache( this );
            }

            return m_cache;
        }
        
        public class name_cache : DataCache
        {
            // the cached data
            protected ushort m_format;
            // No need to store string offsets because we can determine these when we save the cache
            protected List<NameRecordCache> m_nameRecords; // NameRecordCache[] // ArrayList

            // constructor
            public name_cache(Table_name OwnerTable)
            {
            
                m_format = OwnerTable.FormatSelector;
                
                m_nameRecords = new List<NameRecordCache>( OwnerTable.NumberNameRecords );

                for( ushort i = 0; i < OwnerTable.NumberNameRecords; i++ )
                {
                    NameRecord nr = OwnerTable.GetNameRecord( i )!;                    
                    string sNameString = OwnerTable.GetString( nr.PlatformID, nr.EncodingID, nr.LanguageID, nr.NameID )!;                    
                    addNameRecord(nr.PlatformID, nr.EncodingID, nr.LanguageID, nr.NameID, sNameString);
                }
            }


            public ushort format
            {
                get{ return m_format; }
                set
                {
                    m_format = value;
                    m_bDirty = true;    
                }
            }

            public ushort count
            {
                get{ return (ushort)m_nameRecords.Count; }
            }

            public NameRecordCache getNameRecord( ushort nIndex )
            {
                if( nIndex >= m_nameRecords.Count )
                {                    
                    throw new ArgumentOutOfRangeException( "nIndex is greater than the amount of name records." );
                }
                else
                {
                    return (NameRecordCache)(m_nameRecords[nIndex]!).Clone();
                }
            }

            public NameRecordCache? getNameRecord(ushort platformID, ushort encodingID, ushort languageID, ushort nameID )
            {
                NameRecordCache? nrc = null;

                for (int i=0; i<m_nameRecords.Count; i++)
                {
                    NameRecordCache nrcTemp = m_nameRecords[i]!;
                    if (nrcTemp.platformID == platformID && 
                        nrcTemp.encodingID == encodingID && 
                        nrcTemp.languageID == languageID && 
                        nrcTemp.nameID == nameID)
                    {
                        nrc = (NameRecordCache)nrcTemp.Clone();
                        break;
                    }
                }

                return nrc;
            }

            public int GetNameRecordIndex(ushort platformID, ushort encodingID, ushort languageID, ushort nameID)
            {
                int nIndex = -1; // return -1 if not found

                for (int i=0; i<m_nameRecords.Count; i++)
                {
                    NameRecordCache nrcTemp = m_nameRecords[i]!;
                    if (nrcTemp.platformID == platformID && 
                        nrcTemp.encodingID == encodingID && 
                        nrcTemp.languageID == languageID && 
                        nrcTemp.nameID == nameID)
                    {
                        nIndex = i;
                        break;
                    }
                }

                return nIndex;
            }

            protected int GetInsertionPosition(ushort platformID, ushort encodingID, ushort languageID, ushort nameID)
            {
                int nIndex = 0;

                if (GetNameRecordIndex(platformID, encodingID, languageID, nameID) != -1)
                {
                    // can't insert because the specified IDs are already in the list
                    nIndex = -1;
                }
                else
                {
                    if (m_nameRecords.Count != 0)
                    {
                        // check for insertion before the first string
                        NameRecordCache nrcTemp = m_nameRecords[0]!;
                        int nCompare = nrcTemp.CompareNameRecordToIDs(platformID, encodingID, languageID, nameID);

                        if (nCompare == 1)
                        {
                            nIndex = 0;
                        }
                        else
                        {
                            // check for insertion between two other strings
                            for (int i=0; i<m_nameRecords.Count-1; i++)
                            {
                                NameRecordCache nrcCurr = m_nameRecords[i]!;
                                NameRecordCache nrcNext = m_nameRecords[i+1]!;
                                // check if specified IDs are greater than the current object and less than the next object
                                int nCmp1 = nrcCurr.CompareNameRecordToIDs(platformID, encodingID, languageID, nameID);
                                int nCmp2 = nrcNext.CompareNameRecordToIDs(platformID, encodingID, languageID, nameID);
                                if (nCmp1 == -1 && nCmp2 == 1)
                                {
                                    nIndex = i+1;
                                    break;
                                }
                            }
                            // if not found yet then insertion position is at end of list
                            if (nIndex == 0)
                            {
                                nIndex = m_nameRecords.Count;
                            }
                        }
                    }
                }

                return nIndex;
            }

            public void UpdateNameRecord(ushort platformID, ushort encodingID, ushort languageID, ushort nameID, string sNameString)
            {
                int nIndex = GetNameRecordIndex(platformID, encodingID, languageID, nameID);


                if (nIndex != -1)
                {
                    setNameRecord((ushort)nIndex, platformID, encodingID, languageID, nameID, sNameString);
                }
                else
                {
                    throw new ApplicationException("Name Record not found");
                }
            }

            protected bool setNameRecord( ushort nIndex, ushort platformID, ushort encodingID, ushort languageID, ushort nameID, string sNameString )
            {
                bool bResult = true;

                if( nIndex >= m_nameRecords.Count )
                {
                    bResult = false;
                    throw new ArgumentOutOfRangeException( "nIndex is greater than the amount of name records." );
                }
                else
                {
                    m_nameRecords[nIndex] = new NameRecordCache( platformID, encodingID, languageID, nameID, sNameString );
                    m_bDirty = true;
                }

                return bResult;
            }

            public void addNameRecord(ushort platformID, ushort encodingID, ushort languageID, ushort nameID, string sNameString)
            {
                int nIndex = GetInsertionPosition(platformID, encodingID, languageID, nameID);

                if (nIndex != -1)
                {
                    addNameRecord((ushort)nIndex, platformID, encodingID, languageID, nameID, sNameString);
                }
                else
                {
                    throw new ApplicationException("string already exists in the table");
                }
            }


            protected bool addNameRecord( ushort nIndex, ushort platformID, ushort encodingID, ushort languageID, ushort nameID, string sNameString )
            {
                bool bResult = true;

                if( nIndex > m_nameRecords.Count )
                {
                    bResult = false;
                    throw new ArgumentOutOfRangeException( "nIndex is greater than the amount of name records + 1." );
                }
                else
                {                    
                    m_nameRecords.Insert( nIndex, new NameRecordCache( platformID, encodingID, languageID, nameID, sNameString ));
                    m_bDirty = true;                            
                }

                return bResult;
            }

            // removes the corresponding name string

            public void removeNameRecord(ushort platformID, ushort encodingID, ushort languageID, ushort nameID)
            {
                int nIndex = GetNameRecordIndex(platformID, encodingID, languageID, nameID);
                if (nIndex != -1)
                {
                    removeNameRecord((ushort)nIndex);
                }
                else
                {
                    throw new ApplicationException("NameRecord not found");
                }
            }

            public bool removeNameRecord( ushort nIndex )
            {
                bool bResult = true;

                if( nIndex >= m_nameRecords.Count )
                {
                    bResult = false;
                    throw new ArgumentOutOfRangeException( "nIndex is greater than the amount of name records." );
                }
                else
                {                    
                    m_nameRecords.RemoveAt( nIndex );
                    m_bDirty = true;                    
                }

                return bResult;
            }

            public override OTTable GenerateTable()
            {
                List<byte[]> bytesNameString = [];
                ushort nLengthOfStrings = 0;
                ushort nStartOfStringStorage = (ushort)(6 + (m_nameRecords.Count * 12));

                for( ushort i = 0; i < m_nameRecords.Count; i++ )
                {
                    NameRecordCache nrc = m_nameRecords[i];
                    var byteString = EncodeString(nrc.sNameString!, nrc.platformID, nrc.encodingID)!;
                    bytesNameString.Add( byteString );
                    nLengthOfStrings += (ushort)byteString.Length;
                }

                // create a Motorola Byte Order buffer for the new table
                MBOBuffer newbuf = new MBOBuffer( (uint)(Table_name.FieldOffsets.NameRecords + (m_nameRecords.Count * 12) + nLengthOfStrings));

                // populate the buffer                
                newbuf.SetUshort( m_format,                        (uint)FieldOffsets.FormatSelector );
                newbuf.SetUshort( (ushort)m_nameRecords.Count,    (uint)FieldOffsets.NumberNameRecords );
                newbuf.SetUshort( nStartOfStringStorage,        (uint)FieldOffsets.OffsetToStrings );

                ushort nOffset = 0;
                // Write the NameRecords and Strings
                for( ushort i = 0; i < m_nameRecords.Count; i++ )
                {    
                    byte[] bString = bytesNameString[i];
                    
                    newbuf.SetUshort(m_nameRecords[i].platformID,    (uint)(FieldOffsets.NameRecords + (i * 12)));
                    newbuf.SetUshort(m_nameRecords[i].encodingID,    (uint)(FieldOffsets.NameRecords + (i * 12) + 2));
                    newbuf.SetUshort(m_nameRecords[i].languageID,    (uint)(FieldOffsets.NameRecords + (i * 12) + 4));
                    newbuf.SetUshort(m_nameRecords[i].nameID,        (uint)(FieldOffsets.NameRecords + (i * 12) + 6));
                    newbuf.SetUshort( (ushort)bString.Length,        (uint)(FieldOffsets.NameRecords + (i * 12) + 8));
                    newbuf.SetUshort( nOffset,                       (uint)(FieldOffsets.NameRecords + (i * 12) + 10));
                    
                    //Write the string to the buffer
                    for( int ii = 0; ii < bString.Length; ii++ )
                    {
                        newbuf.SetByte( bString[ii], (uint)(nStartOfStringStorage + nOffset + ii));
                    }

                    nOffset += (ushort)bString.Length;
                }

                // put the buffer into a Table_name object and return it
                return new Table_name("name", newbuf);
            }

            public class NameRecordCache : ICloneable
            {
                protected ushort m_platformID;
                protected ushort m_encodingID;
                protected ushort m_languageID;
                protected ushort m_nameID;
                // Instead of the offset we will store the actual string 
                protected string? m_sNameString;

                public NameRecordCache( ushort nPlatformID, ushort nEncodingID, ushort nLanguageID, ushort nNameID, string? NameString )
                {
                    platformID = nPlatformID;
                    encodingID = nEncodingID;
                    languageID = nLanguageID;
                    nameID = nNameID;
                    sNameString = NameString;
                }

                public ushort platformID
                {
                    get{ return m_platformID; }
                    set{ m_platformID = value; }
                }

                public ushort encodingID
                {
                    get{ return m_encodingID; }
                    set{ m_encodingID = value; }
                }

                public ushort languageID
                {
                    get{ return m_languageID; }
                    set{ m_languageID = value; }
                }

                public ushort nameID
                {
                    get{ return m_nameID; }
                    set{ m_nameID = value; }
                }

                public string? sNameString
                {
                    get{ return m_sNameString; }
                    set{ m_sNameString = value; }
                }

                public object Clone()
                {
                    return new NameRecordCache( platformID, encodingID, languageID, nameID, sNameString );
                }

                public int CompareNameRecordToIDs( ushort nPlatformID, ushort nEncodingID, ushort nLanguageID, ushort nNameID)
                {
                    // return
                    // -1 if this object is less than the specified IDs,
                    // 0 if this object is equal to the specified IDs,
                    // 1 if this object is greater than the specified IDs

                    int nResult = 0;

                    if (m_platformID < nPlatformID)
                    {
                        nResult = -1;
                    }
                    else if (m_platformID > nPlatformID)
                    {
                        nResult = 1;
                    }
                    else
                    {
                        if (m_encodingID < nEncodingID)
                        {
                            nResult = -1;
                        }
                        else if (m_encodingID > nEncodingID)
                        {
                            nResult = 1;
                        }
                        else
                        {
                            if (m_languageID < nLanguageID)
                            {
                                nResult = -1;
                            }
                            else if (m_languageID > nLanguageID)
                            {
                                nResult = 1;
                            }
                            else
                            {
                                if (m_nameID < nNameID)
                                {
                                    nResult = -1;
                                }
                                else if (m_nameID > nNameID)
                                {
                                    nResult = 1;
                                }
                                else
                                {
                                    // they are equal!
                                }
                            }
                        }
                    }

                    return nResult;
                }

            }
        }
    }
}
