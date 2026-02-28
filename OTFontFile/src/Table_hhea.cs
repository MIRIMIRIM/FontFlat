using OTFontFile.Generators;

namespace OTFontFile;

/// <summary>
/// 'hhea' - Horizontal Header Table.
/// Partial class using source generation for property accessors and cache.
/// </summary>
[OTTable("hhea")]
[OTCacheable(FixedSize = 36)]
public partial class Table_hhea : OTTable
{
    // Field definitions for source generator
    [OTField(0, OTFieldType.Fixed)]   private OTFixed _TableVersionNumber;
    [OTField(4, OTFieldType.Short)]   private short _Ascender;
    [OTField(6, OTFieldType.Short)]   private short _Descender;
    [OTField(8, OTFieldType.Short)]   private short _LineGap;
    [OTField(10, OTFieldType.UShort)] private ushort _advanceWidthMax;
    [OTField(12, OTFieldType.Short)]  private short _minLeftSideBearing;
    [OTField(14, OTFieldType.Short)]  private short _minRightSideBearing;
    [OTField(16, OTFieldType.Short)]  private short _xMaxExtent;
    [OTField(18, OTFieldType.Short)]  private short _caretSlopeRise;
    [OTField(20, OTFieldType.Short)]  private short _caretSlopeRun;
    [OTField(22, OTFieldType.Short)]  private short _caretOffset;
    [OTField(24, OTFieldType.Short)]  private short _reserved1;
    [OTField(26, OTFieldType.Short)]  private short _reserved2;
    [OTField(28, OTFieldType.Short)]  private short _reserved3;
    [OTField(30, OTFieldType.Short)]  private short _reserved4;
    [OTField(32, OTFieldType.Short)]  private short _metricDataFormat;
    [OTField(34, OTFieldType.UShort)] private ushort _numberOfHMetrics;

    /// <summary>
    /// Constructor
    /// </summary>
    public Table_hhea(OTTag tag, MBOBuffer buf) : base(tag, buf)
    {
    }

    /************************
     * DataCache class accessor
     */
    public override DataCache GetCache()
    {
        if (m_cache == null)
        {
            m_cache = new hhea_cache(this);
        }
        return m_cache;
    }
}
