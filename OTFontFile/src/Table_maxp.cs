using OTFontFile.Generators;

namespace OTFontFile;

/// <summary>
/// 'maxp' - Maximum Profile Table.
/// Partial class using source generation for property accessors.
/// Version 0.5 (0x00005000) for CFF fonts has only 2 fields.
/// Version 1.0 (0x00010000) for TrueType fonts has all 15 fields.
/// </summary>
[OTTable("maxp")]
[OTCacheable(FixedSize = 32)]  // TrueType version size
public partial class Table_maxp : OTTable
{
    // ==================== All versions (0.5 and 1.0) ====================
    [OTField(0, OTFieldType.Fixed)]   private OTFixed _TableVersionNumber;
    [OTField(4, OTFieldType.UShort)]  private ushort _NumGlyphs;

    // ==================== Version 1.0 (TrueType) only ====================
    [OTField(6, OTFieldType.UShort, MinVersion = 0x00010000)]  private ushort _maxPoints;
    [OTField(8, OTFieldType.UShort, MinVersion = 0x00010000)]  private ushort _maxContours;
    [OTField(10, OTFieldType.UShort, MinVersion = 0x00010000)] private ushort _maxCompositePoints;
    [OTField(12, OTFieldType.UShort, MinVersion = 0x00010000)] private ushort _maxCompositeContours;
    [OTField(14, OTFieldType.UShort, MinVersion = 0x00010000)] private ushort _maxZones;
    [OTField(16, OTFieldType.UShort, MinVersion = 0x00010000)] private ushort _maxTwilightPoints;
    [OTField(18, OTFieldType.UShort, MinVersion = 0x00010000)] private ushort _maxStorage;
    [OTField(20, OTFieldType.UShort, MinVersion = 0x00010000)] private ushort _maxFunctionDefs;
    [OTField(22, OTFieldType.UShort, MinVersion = 0x00010000)] private ushort _maxInstructionDefs;
    [OTField(24, OTFieldType.UShort, MinVersion = 0x00010000)] private ushort _maxStackElements;
    [OTField(26, OTFieldType.UShort, MinVersion = 0x00010000)] private ushort _maxSizeOfInstructions;
    [OTField(28, OTFieldType.UShort, MinVersion = 0x00010000)] private ushort _maxComponentElements;
    [OTField(30, OTFieldType.UShort, MinVersion = 0x00010000)] private ushort _maxComponentDepth;

    /// <summary>
    /// Constructor
    /// </summary>
    public Table_maxp(OTTag tag, MBOBuffer buf) : base(tag, buf)
    {
    }

    /// <summary>
    /// Version alias for source generator compatibility.
    /// Returns TableVersionNumber as uint for version checks.
    /// </summary>
    public uint version => TableVersionNumber.GetUint();

    /************************
     * DataCache class accessor
     */
    public override DataCache GetCache()
    {
        if (m_cache == null)
        {
            m_cache = new maxp_cache(this);
        }
        return m_cache;
    }
}
