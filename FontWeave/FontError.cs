namespace FontWeave;

public enum FontError
{
    Truncated, InvalidFormat, UnsupportedFormat, InvalidOffset, InvalidIndex,
    DuplicateTable, MissingTable, InvalidEncoding, InvalidName, InvalidState,
    ResourceLimit, Unrepresentable, UnknownTable, SignedFont
}

public sealed class FontDataException : Exception
{
    public FontError Error { get; }
    public FontDataException(FontError error, string message) : base(message) => Error = error;
}

/// <summary>Explicit resource ceilings for the M1 input/output profile.</summary>
public sealed record FontLimits
{
    public int MaxInputBytes { get; init; } = 64 * 1024 * 1024;
    public int MaxOutputBytes { get; init; } = 64 * 1024 * 1024;
    public int MaxFaces { get; init; } = 128;
    public int MaxTables { get; init; } = 4095;
    public int MaxNameRecords { get; init; } = 4096;
    public int MaxNameBytes { get; init; } = 8 * 1024 * 1024;

    public void Validate()
    {
        if (MaxInputBytes < 0 || MaxOutputBytes < 0 || MaxFaces < 0 ||
            MaxTables < 0 || MaxNameRecords < 0 || MaxNameBytes < 0)
            throw new ArgumentOutOfRangeException(nameof(FontLimits), "Budgets must be nonnegative.");
    }
}
