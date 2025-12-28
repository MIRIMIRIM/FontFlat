using OTFontFile.Generators;

namespace OTFontFile;

/// <summary>
/// 'vhea' - Vertical Header Table.
/// Partial class using source generation for property accessors and cache.
/// </summary>
[OTTable("vhea")]
[OTCacheable(FixedSize = 36)]
public partial class Table_vhea : OTTable
{
    // Field definitions for source generator
    [OTField(0, OTFieldType.Fixed)]   private OTFixed _version;
    [OTField(4, OTFieldType.Short)]   private short _vertTypoAscender;
    [OTField(6, OTFieldType.Short)]   private short _vertTypoDescender;
    [OTField(8, OTFieldType.Short)]   private short _vertTypoLineGap;
    [OTField(10, OTFieldType.Short)]  private short _advanceHeightMax;
    [OTField(12, OTFieldType.Short)]  private short _minTopSideBearing;
    [OTField(14, OTFieldType.Short)]  private short _minBottomSideBearing;
    [OTField(16, OTFieldType.Short)]  private short _yMaxExtent;
    [OTField(18, OTFieldType.Short)]  private short _caretSlopeRise;
    [OTField(20, OTFieldType.Short)]  private short _caretSlopeRun;
    [OTField(22, OTFieldType.Short)]  private short _caretOffset;
    [OTField(24, OTFieldType.Short)]  private short _reserved1;
    [OTField(26, OTFieldType.Short)]  private short _reserved2;
    [OTField(28, OTFieldType.Short)]  private short _reserved3;
    [OTField(30, OTFieldType.Short)]  private short _reserved4;
    [OTField(32, OTFieldType.Short)]  private short _metricDataFormat;
    [OTField(34, OTFieldType.UShort)] private ushort _numOfLongVerMetrics;

    /// <summary>
    /// Constructor
    /// </summary>
    public Table_vhea(OTTag tag, MBOBuffer buf) : base(tag, buf)
    {
    }

    /************************
     * DataCache class accessor
     */
    public override DataCache GetCache()
    {
        if (m_cache == null)
        {
            m_cache = new vhea_cache(this);
        }
        return m_cache;
    }
}
