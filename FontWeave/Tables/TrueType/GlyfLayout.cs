using FontWeave.Core;

namespace FontWeave.Tables;

internal static class GlyfLayout
{
    internal const uint Tag = 0x676C7966;
    internal const int HeaderLength = 10;
    internal const int MinimumGlyph = 10;

    // Simple glyph flags.
    internal const byte OnCurve = 0x01, XShort = 0x02, YShort = 0x04, Repeat = 0x08,
        XSameOrPositive = 0x10, YSameOrPositive = 0x20, OverlapSimple = 0x40,
        ReservedFlag = 0x80;

    // Composite component flags accepted for name-keyed/generic TrueType outlines.
    internal const ushort Arg1And2AreWords = 0x0001, ArgsAreXyValues = 0x0002,
        RoundXyToGrid = 0x0004, WeHaveAScale = 0x0008, MoreComponents = 0x0020,
        WeHaveAnXAndYScale = 0x0040, WeHaveATwoByTwo = 0x0080,
        WeHaveInstructions = 0x0100, UseMyMetrics = 0x0200,
        OverlapCompound = 0x0400, ScaledComponentOffset = 0x0800,
        UnscaledComponentOffset = 0x1000;

    /// <summary>0x0010 plus 0xE000. Reserved in the specification and must be zero.</summary>
    internal const ushort ReservedComponentBits = 0x0010 | 0xE000;

    /// <summary>Explicit bound on distinct components in one composite glyph.</summary>
    internal const int MaxComponentsPerGlyph = 4096;

    /// <summary>Explicit bound on composite nesting depth visited by the graph walker.</summary>
    internal const int MaxComponentDepthBudget = 64;

    /// <summary>Explicit bound on total component edges visited for one glyph closure.</summary>
    internal const int MaxComponentEdgesBudget = 1 << 16;

    /// <summary>Trailing bytes beyond the decoded structure are accepted only when all zero.</summary>
    internal const int MaxPaddingBytes = 3;
}

/// <summary>One decoded point of a simple glyph, in absolute design units.</summary>
public readonly record struct GlyphPoint(int X, int Y, bool OnCurve);

/// <summary>Decoded simple glyph structure. Instruction bytes are retained opaquely.</summary>
public sealed class SimpleGlyph
{
    private readonly GlyphPoint[] _points;
    private readonly ushort[] _endPoints;
    private readonly byte[] _flags;
    private readonly byte[] _instructions;

    internal SimpleGlyph(ushort[] endPoints, byte[] flags, GlyphPoint[] points,
        byte[] instructions, int consumedLength, int paddingLength)
    {
        _endPoints = endPoints; _flags = flags; _points = points;
        _instructions = instructions; ConsumedLength = consumedLength;
        PaddingLength = paddingLength;
        HasInstructions = instructions.Length > 0;
    }

    /// <summary>Last point ordinal of each contour, strictly increasing.</summary>
    public ReadOnlySpan<ushort> EndPointsOfContours => _endPoints;

    /// <summary>Expanded logical flags, one per point, including the raw flag bits.</summary>
    public ReadOnlySpan<byte> Flags => _flags;

    public ReadOnlySpan<GlyphPoint> Points => _points;

    /// <summary>Opaque instruction payload; never interpreted as a program.</summary>
    public ReadOnlySpan<byte> Instructions => _instructions;

    public int ContourCount => _endPoints.Length;
    public int PointCount => _points.Length;

    /// <summary>Bytes actually decoded for this glyph (header through coordinates).</summary>
    public int ConsumedLength { get; }

    /// <summary>Trailing all-zero bytes after <see cref="ConsumedLength"/>, 0..3.</summary>
    public int PaddingLength { get; }

    public bool HasInstructions { get; }

    public ReadStatus TryGetPoint(int ordinal, out GlyphPoint point,
        out FontDiagnostic diagnostic, DataLocation location = default)
    {
        point = default;
        if (ordinal < 0 || ordinal >= _points.Length)
            return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                location.At(DataField.Payload, 0, ordinal), out diagnostic,
                unchecked((ulong)(long)ordinal), (ulong)_points.Length);
        point = _points[ordinal];
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>Contour ordinal holding a point, or -1 when the point ordinal is out of range.</summary>
    public int ContourOf(int pointOrdinal)
    {
        for (int i = 0; i < _endPoints.Length; i++)
            if (pointOrdinal <= _endPoints[i]) return i;
        return -1;
    }
}
