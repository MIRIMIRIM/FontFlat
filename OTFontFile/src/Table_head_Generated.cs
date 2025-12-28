using OTFontFile.Generators;

namespace OTFontFile;

/// <summary>
/// Sample generated table for testing the source generator.
/// This file demonstrates the attribute-based approach.
/// </summary>
[OTTable("head")]
[OTCacheable(FixedSize = 54)]
public partial class Table_head_Generated : OTTable
{
    [OTField(0, OTFieldType.Fixed)]   private OTFixed _tableVersionNumber;
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

    public Table_head_Generated(OTTag tag, MBOBuffer buf) : base(tag, buf)
    {
    }
}
