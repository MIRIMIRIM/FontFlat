using FontWeave.Core;

namespace FontWeave.Tables;

/// <summary>Kind of glyph description found at a loca range.</summary>
public enum GlyfGlyphKind : byte
{
    /// <summary>Zero-length loca range: no glyph description at all.</summary>
    Empty = 0,

    /// <summary>numberOfContours &gt;= 0.</summary>
    Simple = 1,

    /// <summary>numberOfContours &lt; 0. Internal structure is parsed by the composite slice.</summary>
    Composite = 2
}

/// <summary>The 10-byte glyph header shared by simple and composite descriptions.</summary>
public readonly record struct GlyfHeader(short NumberOfContours, short XMin, short YMin,
    short XMax, short YMax);

/// <summary>One glyph as located by loca.
///
/// Simple structure is decoded by the simple-glyph slice. Composite structure is decoded
/// by the composite slice, which reports it through <see cref="Composite"/>; when a caller
/// uses a simple-only entry point the composite stays unparsed and is reported as
/// not evaluated rather than being treated as simple or as valid.</summary>
public sealed class GlyfGlyph
{
    internal GlyfGlyph(int glyphId, GlyfHeader header, GlyfGlyphKind kind, uint start, uint end,
        SimpleGlyph? simple, GlyfCompositeGlyph? composite = null)
    {
        GlyphId = glyphId; Header = header; Kind = kind; Start = start; End = end;
        Simple = simple;
        Composite = composite;
    }

    public int GlyphId { get; }
    public GlyfHeader Header { get; }
    public GlyfGlyphKind Kind { get; }
    public uint Start { get; }
    public uint End { get; }
    public uint Length => End - Start;

    /// <summary>Decoded simple description; null for empty and composite glyphs.</summary>
    public SimpleGlyph? Simple { get; }

    /// <summary>Decoded composite description; null unless composite decoding was
    /// requested through a composite-aware entry point.</summary>
    public GlyfCompositeGlyph? Composite { get; }

    public bool IsEmpty => Kind == GlyfGlyphKind.Empty;

    /// <summary>True when this glyph is composite but its internal structure was
    /// deliberately not parsed by the caller's chosen entry point. Callers must treat it
    /// as NotEvaluated, never as valid.</summary>
    public bool StructureNotParsed => Kind == GlyfGlyphKind.Composite && Composite is null;

    /// <summary>Bytes of this glyph record, including any trailing padding.</summary>
    public int PaddingLength => Simple?.PaddingLength
        ?? (Composite is { } composite ? (int)(Length - (uint)composite.ConsumedLength) : 0);
}
