using FontWeave.Core;

namespace FontWeave.Tables;

/// <summary>Which transform encoding a component carries. The three encodings are
/// mutually exclusive on the wire, so exactly one applies.</summary>
public enum GlyfTransformKind : byte
{
    None = 0,
    Scale = 1,
    XAndYScale = 2,
    TwoByTwo = 3
}

/// <summary>A component transform in raw F2DOT14 units.
///
/// The raw int16 values are the authoritative wire form; <see cref="Scale"/> and
/// friends are conveniences. No transform arithmetic is applied to the component
/// offset here, and the scaled/unscaled offset flags are reported rather than acted on.
/// </summary>
public readonly record struct GlyfTransform(GlyfTransformKind Kind, short A, short B,
    short C, short D)
{
    public static GlyfTransform Identity => new(GlyfTransformKind.None, 0, 0, 0, 0);

    /// <summary>Uniform scale (F2DOT14) when Kind is Scale; 1 otherwise.</summary>
    public double Scale => Kind == GlyfTransformKind.Scale ? F2Dot14.ToDouble(A) : 1.0;

    public double XScale => Kind switch
    {
        GlyfTransformKind.Scale => F2Dot14.ToDouble(A),
        GlyfTransformKind.XAndYScale or GlyfTransformKind.TwoByTwo => F2Dot14.ToDouble(A),
        _ => 1.0
    };

    public double YScale => Kind switch
    {
        GlyfTransformKind.Scale => F2Dot14.ToDouble(A),
        GlyfTransformKind.XAndYScale or GlyfTransformKind.TwoByTwo => F2Dot14.ToDouble(D),
        _ => 1.0
    };
}

/// <summary>F2DOT14 fixed-point conversion. Two integer bits and fourteen fraction bits.</summary>
public static class F2Dot14
{
    public const int FractionBits = 14;
    public const int Scale = 1 << FractionBits;
    public const short Minimum = short.MinValue;
    public const short Maximum = short.MaxValue;

    public static double ToDouble(short raw) => raw / (double)Scale;

    /// <summary>Rounds a real value to the nearest representable F2DOT14 raw value.
    /// Returns false when the value cannot be represented at all.</summary>
    public static bool TryFromDouble(double value, out short raw)
    {
        raw = 0;
        if (double.IsNaN(value) || double.IsInfinity(value)) return false;
        double scaled = Math.Round(value * Scale, MidpointRounding.AwayFromZero);
        if (scaled < Minimum || scaled > Maximum) return false;
        raw = (short)scaled;
        return true;
    }
}

/// <summary>One component record of a composite glyph.</summary>
public sealed class GlyfComponent
{
    internal GlyfComponent(int ordinal, ushort flags, ushort glyphIndex, short arg1,
        short arg2, GlyfTransform transform)
    {
        Ordinal = ordinal;
        Flags = flags;
        GlyphIndex = glyphIndex;
        Arg1 = arg1;
        Arg2 = arg2;
        Transform = transform;
    }

    /// <summary>Zero-based position in the component sequence.</summary>
    public int Ordinal { get; }

    public ushort Flags { get; }
    public ushort GlyphIndex { get; }

    /// <summary>Raw argument 1. An x offset when <see cref="ArgsAreXyValues"/> is set and
    /// <see cref="Arg1And2AreWords"/> is set; a point number when it is clear.</summary>
    public short Arg1 { get; }

    public short Arg2 { get; }

    public GlyfTransform Transform { get; }

    public bool Arg1And2AreWords => (Flags & GlyfLayout.Arg1And2AreWords) != 0;
    public bool ArgsAreXyValues => (Flags & GlyfLayout.ArgsAreXyValues) != 0;
    public bool RoundXyToGrid => (Flags & GlyfLayout.RoundXyToGrid) != 0;
    public bool MoreComponents => (Flags & GlyfLayout.MoreComponents) != 0;
    public bool UseMyMetrics => (Flags & GlyfLayout.UseMyMetrics) != 0;
    public bool OverlapCompound => (Flags & GlyfLayout.OverlapCompound) != 0;
    public bool ScaledComponentOffset => (Flags & GlyfLayout.ScaledComponentOffset) != 0;
    public bool UnscaledComponentOffset => (Flags & GlyfLayout.UnscaledComponentOffset) != 0;
    public bool WeHaveInstructions => (Flags & GlyfLayout.WeHaveInstructions) != 0;

    /// <summary>X offset in design units when the arguments are xy values; null when they
    /// are point numbers, whose matching semantics this slice does not implement.</summary>
    public int? XOffset => ArgsAreXyValues ? Arg1 : null;

    public int? YOffset => ArgsAreXyValues ? Arg2 : null;
}

/// <summary>Decoded composite glyph structure.
///
/// Components are preserved in wire order and their flags are preserved verbatim; no
/// transform is merged, optimized, or applied, and the component offset is never
/// rescaled.</summary>
public sealed class GlyfCompositeGlyph
{
    private readonly GlyfComponent[] _components;
    private readonly byte[] _instructions;
    private readonly int[] _indexOffsets;

    internal GlyfCompositeGlyph(int glyphId, GlyfHeader header, GlyfComponent[] components,
        byte[] instructions, int consumedLength, uint start, int[]? indexOffsets = null)
    {
        GlyphId = glyphId;
        Header = header;
        _components = components;
        _instructions = instructions;
        _indexOffsets = indexOffsets ?? new int[components.Length];
        ConsumedLength = consumedLength;
        Start = start;
    }

    public int GlyphId { get; }
    public GlyfHeader Header { get; }
    public uint Start { get; }
    public int ConsumedLength { get; }

    public ReadOnlySpan<GlyfComponent> Components => _components;
    public int ComponentCount => _components.Length;

    /// <summary>Instruction payload carried by the composite, never interpreted.</summary>
    public ReadOnlySpan<byte> Instructions => _instructions;

    public bool HasInstructions => _instructions.Length > 0;

    /// <summary>Byte offset of each component's glyphIndex field inside the glyph
    /// record, in component order. A caller that relocates glyphs without re-encoding
    /// them (the byte-faithful compact splice) patches these two-byte fields in place
    /// instead of re-deriving the composite walk.</summary>
    internal ReadOnlySpan<int> ComponentIndexOffsets => _indexOffsets;

    public GlyfComponent this[int ordinal] => _components[ordinal];
}

/// <summary>Component hierarchy facts for one glyf image.
///
/// The graph is built from decoded composite records. It is read-only: nothing here
/// generates glyphs, flattens outlines, or rewrites the parent table.</summary>
public sealed class ComponentGraph
{
    private readonly int[][] _children;
    private readonly int[] _depth;
    private readonly bool[] _visited;

    internal ComponentGraph(int[][] children, int[] depth, bool[] visited, int edgeCount,
        int maxDepth)
    {
        _children = children;
        _depth = depth;
        _visited = visited;
        EdgeCount = edgeCount;
        MaxDepth = maxDepth;
    }

    public int NodeCount => _children.Length;

    /// <summary>Total number of parent-to-child references across the whole image.</summary>
    public int EdgeCount { get; }

    /// <summary>Largest resolved depth. A glyph with no components has depth 0.</summary>
    public int MaxDepth { get; }

    public int DepthOf(int glyphId) => _depth[glyphId];

    public bool HasComponents(int glyphId) => _children[glyphId].Length > 0;

    public ReadOnlySpan<int> ChildrenOf(int glyphId) => _children[glyphId];

    /// <summary>Every glyph reachable from <paramref name="glyphId"/>, excluding itself
    /// unless it is part of a cycle (which validation rejects before this is reachable).</summary>
    public IReadOnlyList<int> ClosureOf(int glyphId)
    {
        var seen = new HashSet<int>();
        var order = new List<int>();
        var stack = new Stack<int>(_children[glyphId]);
        while (stack.Count > 0)
        {
            int current = stack.Pop();
            if (!seen.Add(current)) continue;
            order.Add(current);
            foreach (int child in _children[current]) stack.Push(child);
        }
        return order;
    }

    /// <summary>Glyphs that are referenced as a component at least once, in glyph order.</summary>
    public IReadOnlyList<int> ReferencedGlyphs()
    {
        var seen = new bool[_children.Length];
        for (int gid = 0; gid < _children.Length; gid++)
            foreach (int child in _children[gid]) seen[child] = true;
        var referenced = new List<int>();
        for (int gid = 0; gid < seen.Length; gid++)
            if (seen[gid]) referenced.Add(gid);
        return referenced;
    }
}
