namespace OTFontFile2;

/// <summary>
/// 2x3 affine transform matrix used by COLR v1 (Affine2x3 / VarAffine2x3).
/// </summary>
public readonly struct Affine2x3 : IEquatable<Affine2x3>
{
    public Fixed1616 XX { get; }
    public Fixed1616 YX { get; }
    public Fixed1616 XY { get; }
    public Fixed1616 YY { get; }
    public Fixed1616 DX { get; }
    public Fixed1616 DY { get; }

    public Affine2x3(Fixed1616 xx, Fixed1616 yx, Fixed1616 xy, Fixed1616 yy, Fixed1616 dx, Fixed1616 dy)
    {
        XX = xx;
        YX = yx;
        XY = xy;
        YY = yy;
        DX = dx;
        DY = dy;
    }

    public bool Equals(Affine2x3 other)
        => XX == other.XX && YX == other.YX && XY == other.XY && YY == other.YY && DX == other.DX && DY == other.DY;

    public override bool Equals(object? obj) => obj is Affine2x3 other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(XX, YX, XY, YY, DX, DY);

    public static bool operator ==(Affine2x3 left, Affine2x3 right) => left.Equals(right);
    public static bool operator !=(Affine2x3 left, Affine2x3 right) => !left.Equals(right);
}

