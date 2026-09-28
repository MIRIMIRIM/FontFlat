namespace FontWeave.Core;

/// <summary>Immutable managed snapshot. No I/O, disposal, pooling or mutable-memory escape.</summary>
public sealed class FontSource
{
    private static long _nextId;
    private readonly byte[] _bytes;
    private FontSource(byte[] bytes)
    {
        _bytes = bytes;
        SourceId = Interlocked.Increment(ref _nextId);
    }

    public long SourceId { get; }
    public ReadOnlySpan<byte> Bytes => _bytes;

    // Copying establishes ownership, not validity of any container or table.
    public static ReadStatus TryCopy(ReadOnlySpan<byte> bytes, out FontSource? source,
        out FontDiagnostic diagnostic, int maxInputBytes = 64 * 1024 * 1024)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxInputBytes);
        source = null;
        diagnostic = default;
        if (bytes.Length > maxInputBytes)
            return ReadWire.Fail(ReadStatus.ResourceLimit, DiagnosticCode.BudgetExceeded,
                DataLocation.Anonymous().At(DataField.Length, 0) with { Basis = OffsetBasis.File },
                out diagnostic, (ulong)bytes.Length, (ulong)maxInputBytes);
        source = new FontSource(bytes.ToArray());
        return ReadStatus.Done;
    }
}

/// <summary>Long-lived identity is (snapshot reference, collection ordinal), not directory offset.</summary>
public readonly struct FontFaceRef : IEquatable<FontFaceRef>
{
    internal FontSource? Source { get; }
    internal FontFaceRef(FontSource source, int index, uint directoryOffset, bool collection)
    {
        Source = source;
        FaceIndex = index;
        DirectoryFileOffset = directoryOffset;
        IsCollection = collection;
    }
    public bool IsBound => Source is not null;
    public long SourceId => Source?.SourceId ?? 0;
    public int FaceIndex { get; }
    public uint DirectoryFileOffset { get; }
    public bool IsCollection { get; }
    public DataLocation Location => new(SourceId, IsBound ? FaceIndex : -1, 0, -1,
        DataField.None, OffsetBasis.File, DirectoryFileOffset);

    public bool Equals(FontFaceRef other) => ReferenceEquals(Source, other.Source) && FaceIndex == other.FaceIndex;
    public override bool Equals(object? obj) => obj is FontFaceRef other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Source, FaceIndex);
    public static bool operator ==(FontFaceRef left, FontFaceRef right) => left.Equals(right);
    public static bool operator !=(FontFaceRef left, FontFaceRef right) => !left.Equals(right);
}
