namespace OTFontFile2;

/// <summary>
/// Signed 2.14 fixed-point number used by OpenType (F2DOT14).
/// </summary>
public readonly struct F2Dot14 : IEquatable<F2Dot14>
{
    public F2Dot14(short rawValue) => RawValue = rawValue;

    public short RawValue { get; }

    public double ToDouble() => RawValue / 16384.0;

    public override string ToString() => ToDouble().ToString("0.####");

    public bool Equals(F2Dot14 other) => RawValue == other.RawValue;
    public override bool Equals(object? obj) => obj is F2Dot14 other && Equals(other);
    public override int GetHashCode() => RawValue.GetHashCode();

    public static bool operator ==(F2Dot14 left, F2Dot14 right) => left.Equals(right);
    public static bool operator !=(F2Dot14 left, F2Dot14 right) => !left.Equals(right);
}

