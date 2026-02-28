namespace OTFontFile2;

public enum FontParseErrorKind
{
    None = 0,
    EndOfData,
    InvalidSfntVersion,
    InvalidTtcHeader,
    InvalidOffsetTable,
    InvalidTableDirectory,
    InvalidTableBounds,
    Unsupported
}

public readonly struct FontParseError
{
    public FontParseErrorKind Kind { get; }
    public int Offset { get; }

    public bool IsNone => Kind == FontParseErrorKind.None;

    public FontParseError(FontParseErrorKind kind, int offset = -1)
    {
        Kind = kind;
        Offset = offset;
    }

    public override string ToString()
        => Offset >= 0 ? $"{Kind} (offset={Offset})" : Kind.ToString();

    public static FontParseError None => default;
}

public sealed class FontParseException : Exception
{
    public FontParseError Error { get; }

    public FontParseException(FontParseError error)
        : base(error.ToString())
    {
        Error = error;
    }
}

