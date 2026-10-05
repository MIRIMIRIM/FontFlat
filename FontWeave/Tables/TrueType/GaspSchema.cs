using FontWeave.Core;
using FontWeave.Schema;

namespace FontWeave.Tables;

internal static class GaspLayout
{
    internal const uint Tag = 0x67617370;
    internal const int HeaderLength = 4, RangeStride = 4;
    internal const int VersionAt = 0, NumRangesAt = 2;
    internal const int RangeMaxPpemFieldAt = 0, RangeBehaviorFieldAt = 2;

    internal const uint Version0 = 0, Version1 = 1;

    /// <summary>Grid fitting; meaningful in both versions.</summary>
    internal const ushort FlagGridFit = 0x0001;

    /// <summary>Grayscale; meaningful in both versions.</summary>
    internal const ushort FlagGrayscale = 0x0002;

    /// <summary>Symmetric grid fitting. Version 1 only.</summary>
    internal const ushort FlagSymmetricGridFit = 0x0004;

    /// <summary>Symmetric smoothing. Version 1 only.</summary>
    internal const ushort FlagSymmetricSmoothing = 0x0008;

    /// <summary>The four behaviour bits defined by version 1.</summary>
    internal const ushort Version1Mask = FlagGridFit | FlagGrayscale | FlagSymmetricGridFit |
        FlagSymmetricSmoothing;

    /// <summary>Version 0 defines only the first two bits; the rest must be zero.</summary>
    internal const ushort Version0Mask = FlagGridFit | FlagGrayscale;

    /// <summary>The last range must end here, acting as a sentinel for the largest size.</summary>
    internal const ushort SentinelPpem = 0xFFFF;

    /// <summary>Declared range budget, so an implausible count is refused rather than read.</summary>
    internal const int MaxRanges = 1 << 10;
}

// gasp is a versioned header followed by a counted array of ranges. Both the version and
// the count live on the wire, so the counted array is declared here (contract version 1:
// the generated accessor owns the ordinal gate and row addressing) and the version-dependent
// flag mask is applied by the partial.
[BinaryTable(GaspLayout.Tag, GaspLayout.HeaderLength, ContractVersion = 1, VersionField = "Version",
    SupportedVersions = new uint[] { GaspLayout.Version0, GaspLayout.Version1 })]
[BinaryScalar("Version", WireType.UInt16, GaspLayout.VersionAt, DataField.Version)]
[BinaryScalar("NumRanges", WireType.UInt16, GaspLayout.NumRangesAt, DataField.Count,
    Semantic = NumberSemantic.Count)]
[BinaryArray("Range", "NumRanges", GaspLayout.HeaderLength, GaspLayout.RangeStride)]
[BinaryElement("Range", "RangeMaxPpem", WireType.UInt16, GaspLayout.RangeMaxPpemFieldAt,
    DataField.Count, Semantic = NumberSemantic.Plain)]
[BinaryElement("Range", "RangeGaspBehavior", WireType.UInt16, GaspLayout.RangeBehaviorFieldAt,
    DataField.Flags, Semantic = NumberSemantic.Plain)]
public readonly ref partial struct GaspView { }

/// <summary>One gasp range: the largest ppem it applies to and its behaviour bits.</summary>
public readonly record struct GaspRange(int Ordinal, ushort RangeMaxPpem, ushort RangeGaspBehavior)
{
    public bool GridFit => (RangeGaspBehavior & GaspLayout.FlagGridFit) != 0;
    public bool Grayscale => (RangeGaspBehavior & GaspLayout.FlagGrayscale) != 0;
    public bool SymmetricGridFit => (RangeGaspBehavior & GaspLayout.FlagSymmetricGridFit) != 0;
    public bool SymmetricSmoothing => (RangeGaspBehavior & GaspLayout.FlagSymmetricSmoothing) != 0;
}

/// <summary>Owned gasp contents.
///
/// The behaviour bits are carried as data and interpreted only as far as the version's
/// legal mask. No rasterizer preference is applied and MVAR threshold adjustment is not
/// implemented, so the sentinel range is reported rather than treated as adjustable.</summary>
public sealed class GaspTable
{
    private readonly GaspRange[] _ranges;

    internal GaspTable(uint version, GaspRange[] ranges, DataLocation location)
    {
        Version = version;
        _ranges = ranges;
        Location = location;
    }

    public DataLocation Location { get; }

    /// <summary>Wire version, either 0 or 1.</summary>
    public uint Version { get; }

    public int RangeCount => _ranges.Length;

    /// <summary>Byte length as encoded: the four-byte header plus four bytes per range.</summary>
    public int EncodedLength => GaspLayout.HeaderLength + _ranges.Length * GaspLayout.RangeStride;

    /// <summary>The behaviour bits this version accepts.</summary>
    public ushort LegalMask => Version == GaspLayout.Version0
        ? GaspLayout.Version0Mask : GaspLayout.Version1Mask;

    public ReadOnlySpan<GaspRange> Ranges => _ranges;

    public GaspRange this[int ordinal] => _ranges[ordinal];

    /// <summary>True when the version defines the symmetric behaviour bits at all.</summary>
    public bool SupportsSymmetric => Version >= GaspLayout.Version1;

    /// <summary>The final range, whose ppem is the 0xFFFF sentinel. MVAR may adjust only the
    /// ranges before it; that adjustment is deliberately not implemented here.</summary>
    public GaspRange Sentinel => _ranges[^1];

    /// <summary>Ranges MVAR would be permitted to adjust, frozen as a boundary note rather
    /// than applied.</summary>
    public ReadOnlySpan<GaspRange> AdjustableRanges => _ranges[..^1];

    public ReadStatus TryGetRange(int ordinal, out GaspRange range,
        out FontDiagnostic diagnostic)
    {
        range = default;
        if (ordinal < 0 || ordinal >= _ranges.Length)
            return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                Location.At(DataField.Count, 0, ordinal), out diagnostic,
                unchecked((ulong)(long)ordinal), (ulong)_ranges.Length);
        range = _ranges[ordinal];
        diagnostic = default;
        return ReadStatus.Done;
    }
}
