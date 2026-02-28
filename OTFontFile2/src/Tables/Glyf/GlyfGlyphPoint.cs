namespace OTFontFile2.Tables.Glyf;

public readonly struct GlyfGlyphPoint
{
    public short X { get; }
    public short Y { get; }
    public bool OnCurve { get; }

    public GlyfGlyphPoint(short x, short y, bool onCurve)
    {
        X = x;
        Y = y;
        OnCurve = onCurve;
    }
}

