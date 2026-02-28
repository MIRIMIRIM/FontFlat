namespace OTFontFile2.Tables;

/// <summary>
/// Value type for writing <see cref="SbitLineMetrics"/>-shaped records (12 bytes) into sbit-related tables.
/// </summary>
public readonly struct SbitLineMetricsData
{
    public sbyte Ascender { get; }
    public sbyte Descender { get; }
    public byte WidthMax { get; }
    public sbyte CaretSlopeNumerator { get; }
    public sbyte CaretSlopeDenominator { get; }
    public sbyte CaretOffset { get; }
    public sbyte MinOriginSb { get; }
    public sbyte MinAdvanceSb { get; }
    public sbyte MaxBeforeBl { get; }
    public sbyte MinAfterBl { get; }
    public sbyte Pad1 { get; }
    public sbyte Pad2 { get; }

    public SbitLineMetricsData(
        sbyte ascender,
        sbyte descender,
        byte widthMax,
        sbyte caretSlopeNumerator,
        sbyte caretSlopeDenominator,
        sbyte caretOffset,
        sbyte minOriginSb,
        sbyte minAdvanceSb,
        sbyte maxBeforeBl,
        sbyte minAfterBl,
        sbyte pad1 = 0,
        sbyte pad2 = 0)
    {
        Ascender = ascender;
        Descender = descender;
        WidthMax = widthMax;
        CaretSlopeNumerator = caretSlopeNumerator;
        CaretSlopeDenominator = caretSlopeDenominator;
        CaretOffset = caretOffset;
        MinOriginSb = minOriginSb;
        MinAdvanceSb = minAdvanceSb;
        MaxBeforeBl = maxBeforeBl;
        MinAfterBl = minAfterBl;
        Pad1 = pad1;
        Pad2 = pad2;
    }

    public static SbitLineMetricsData From(SbitLineMetrics metrics)
        => new(
            metrics.Ascender,
            metrics.Descender,
            metrics.WidthMax,
            metrics.CaretSlopeNumerator,
            metrics.CaretSlopeDenominator,
            metrics.CaretOffset,
            metrics.MinOriginSb,
            metrics.MinAdvanceSb,
            metrics.MaxBeforeBl,
            metrics.MinAfterBl,
            metrics.Pad1,
            metrics.Pad2);

    public void WriteTo(Span<byte> destination, int offset)
    {
        destination[offset + 0] = unchecked((byte)Ascender);
        destination[offset + 1] = unchecked((byte)Descender);
        destination[offset + 2] = WidthMax;
        destination[offset + 3] = unchecked((byte)CaretSlopeNumerator);
        destination[offset + 4] = unchecked((byte)CaretSlopeDenominator);
        destination[offset + 5] = unchecked((byte)CaretOffset);
        destination[offset + 6] = unchecked((byte)MinOriginSb);
        destination[offset + 7] = unchecked((byte)MinAdvanceSb);
        destination[offset + 8] = unchecked((byte)MaxBeforeBl);
        destination[offset + 9] = unchecked((byte)MinAfterBl);
        destination[offset + 10] = unchecked((byte)Pad1);
        destination[offset + 11] = unchecked((byte)Pad2);
    }
}

