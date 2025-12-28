using System;
using OTFontFile.Generators;

namespace OTFontFile;

/// <summary>
/// 'head' - Font Header Table.
/// This partial class uses source generation for property accessors.
/// </summary>
[OTTable("head")]
[OTCacheable(FixedSize = 54)]
public partial class Table_head : OTTable
{
    // Field definitions for source generator - names become property names after removing underscore
    [OTField(0, OTFieldType.Fixed)]   private OTFixed _TableVersionNumber;
    [OTField(4, OTFieldType.Fixed)]   private OTFixed _fontRevision;
    [OTField(8, OTFieldType.UInt)]    private uint _checkSumAdjustment;
    [OTField(12, OTFieldType.UInt)]   private uint _magicNumber;
    [OTField(16, OTFieldType.UShort)] private ushort _flags;
    [OTField(18, OTFieldType.UShort)] private ushort _unitsPerEm;
    [OTField(20, OTFieldType.Long)]   private long _created;
    [OTField(28, OTFieldType.Long)]   private long _modified;
    [OTField(36, OTFieldType.Short)]  private short _xMin;
    [OTField(38, OTFieldType.Short)]  private short _yMin;
    [OTField(40, OTFieldType.Short)]  private short _xMax;
    [OTField(42, OTFieldType.Short)]  private short _yMax;
    [OTField(44, OTFieldType.UShort)] private ushort _macStyle;
    [OTField(46, OTFieldType.UShort)] private ushort _lowestRecPPEM;
    [OTField(48, OTFieldType.Short)]  private short _fontDirectionHint;
    [OTField(50, OTFieldType.Short)]  private short _indexToLocFormat;
    [OTField(52, OTFieldType.Short)]  private short _glyphDataFormat;

    /// <summary>
    /// Constructor
    /// </summary>
    public Table_head(OTTag tag, MBOBuffer buf) : base(tag, buf)
    {
    }

    // head table checksum requires leaving out the checkSumAdjustment field
    public override uint CalcChecksum()
    {
        return m_bufTable.CalcChecksum() - checkSumAdjustment;
    }

    /************************
     * utility functions
     */

    public DateTime GetCreatedDateTime()
    {
        DateTime epoch = new DateTime(1904, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        return epoch.AddSeconds(created);
    }

    public DateTime GetModifiedDateTime()
    {
        DateTime epoch = new DateTime(1904, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        return epoch.AddSeconds(modified);
    }

    public long DateTimeToSecondsSince1904(DateTime dt)
    {
        DateTime epoch = new DateTime(1904, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        TimeSpan ts = dt.Subtract(epoch);
        return (long)ts.TotalSeconds;
    }

    /************************
     * DataCache class accessor
     */
    public override DataCache GetCache()
    {
        if (m_cache == null)
        {
            m_cache = new head_cache(this);
        }
        return m_cache;
    }
}
