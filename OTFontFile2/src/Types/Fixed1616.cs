namespace OTFontFile2;

/// <summary>
/// 16.16 fixed-point number used by OpenType (Fixed).
/// Stored as signed 16-bit mantissa and unsigned 16-bit fraction.
/// </summary>
public readonly struct Fixed1616 : IEquatable<Fixed1616>
{
    public Fixed1616(uint rawValue) => RawValue = rawValue;

    public uint RawValue { get; }

    public short Mantissa => unchecked((short)(RawValue >> 16));

    public ushort Fraction => unchecked((ushort)RawValue);

    public double ToDouble() => Mantissa + (Fraction / 65536.0);

    public override string ToString() => ToDouble().ToString("0.###");

    public bool Equals(Fixed1616 other) => RawValue == other.RawValue;
    public override bool Equals(object? obj) => obj is Fixed1616 other && Equals(other);
    public override int GetHashCode() => (int)RawValue;

    public static bool operator ==(Fixed1616 left, Fixed1616 right) => left.Equals(right);
    public static bool operator !=(Fixed1616 left, Fixed1616 right) => !left.Equals(right);
}

