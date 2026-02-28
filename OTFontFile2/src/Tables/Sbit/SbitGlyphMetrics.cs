namespace OTFontFile2.Tables;

public readonly struct SbitSmallGlyphMetrics
{
    public byte Height { get; }
    public byte Width { get; }
    public sbyte BearingX { get; }
    public sbyte BearingY { get; }
    public byte Advance { get; }

    public SbitSmallGlyphMetrics(byte height, byte width, sbyte bearingX, sbyte bearingY, byte advance)
    {
        Height = height;
        Width = width;
        BearingX = bearingX;
        BearingY = bearingY;
        Advance = advance;
    }

    public static bool TryRead(ReadOnlySpan<byte> data, int offset, out SbitSmallGlyphMetrics metrics)
    {
        metrics = default;

        if ((uint)offset > (uint)data.Length - 5)
            return false;

        metrics = new SbitSmallGlyphMetrics(
            height: data[offset + 0],
            width: data[offset + 1],
            bearingX: unchecked((sbyte)data[offset + 2]),
            bearingY: unchecked((sbyte)data[offset + 3]),
            advance: data[offset + 4]);
        return true;
    }
}

public readonly struct SbitBigGlyphMetrics
{
    public byte Height { get; }
    public byte Width { get; }
    public sbyte HoriBearingX { get; }
    public sbyte HoriBearingY { get; }
    public byte HoriAdvance { get; }
    public sbyte VertBearingX { get; }
    public sbyte VertBearingY { get; }
    public byte VertAdvance { get; }

    public SbitBigGlyphMetrics(
        byte height,
        byte width,
        sbyte horiBearingX,
        sbyte horiBearingY,
        byte horiAdvance,
        sbyte vertBearingX,
        sbyte vertBearingY,
        byte vertAdvance)
    {
        Height = height;
        Width = width;
        HoriBearingX = horiBearingX;
        HoriBearingY = horiBearingY;
        HoriAdvance = horiAdvance;
        VertBearingX = vertBearingX;
        VertBearingY = vertBearingY;
        VertAdvance = vertAdvance;
    }

    public static bool TryRead(ReadOnlySpan<byte> data, int offset, out SbitBigGlyphMetrics metrics)
    {
        metrics = default;

        if ((uint)offset > (uint)data.Length - 8)
            return false;

        metrics = new SbitBigGlyphMetrics(
            height: data[offset + 0],
            width: data[offset + 1],
            horiBearingX: unchecked((sbyte)data[offset + 2]),
            horiBearingY: unchecked((sbyte)data[offset + 3]),
            horiAdvance: data[offset + 4],
            vertBearingX: unchecked((sbyte)data[offset + 5]),
            vertBearingY: unchecked((sbyte)data[offset + 6]),
            vertAdvance: data[offset + 7]);
        return true;
    }
}

public readonly struct SbitComponent
{
    public ushort GlyphId { get; }
    public sbyte XOffset { get; }
    public sbyte YOffset { get; }

    public SbitComponent(ushort glyphId, sbyte xOffset, sbyte yOffset)
    {
        GlyphId = glyphId;
        XOffset = xOffset;
        YOffset = yOffset;
    }
}
