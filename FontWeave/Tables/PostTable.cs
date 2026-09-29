using System.Collections.ObjectModel;
using FontWeave.Core;

namespace FontWeave.Tables;

public enum PostNameKind { Standard258, IndexedNames, SignedOffsets, NoNames }

/// <summary>Wire values are preserved, including non-Boolean isFixedPitch and memory usage.</summary>
public readonly record struct PostHeaderValues(int ItalicAngleRaw, short UnderlinePosition,
    short UnderlineThickness, uint IsFixedPitch, uint MinMemType42, uint MaxMemType42,
    uint MinMemType1, uint MaxMemType1);

/// <summary>A strict, source-independent post model. Only validated readers construct models.</summary>
public sealed class PostTable
{
    private readonly ushort[] _indices;
    private readonly sbyte[] _offsets;
    private readonly ReadOnlyCollection<string> _names;

    internal PostTable(uint version, PostHeaderValues header, ushort[] indices,
        sbyte[] offsets, string[] names, DataLocation location)
    {
        Version = version; Header = header; _indices = indices; _offsets = offsets;
        _names = Array.AsReadOnly(names); Location = location;
    }

    public uint Version { get; }
    public PostHeaderValues Header { get; }
    public DataLocation Location { get; }
    public PostNameKind NameKind => Version switch
    {
        PostLayout.Version10 => PostNameKind.Standard258,
        PostLayout.Version20 => PostNameKind.IndexedNames,
        PostLayout.Version25 => PostNameKind.SignedOffsets,
        _ => PostNameKind.NoNames
    };
    public ushort? NumGlyphs => NameKind switch
    {
        PostNameKind.Standard258 => 258,
        PostNameKind.IndexedNames => (ushort)_indices.Length,
        PostNameKind.SignedOffsets => (ushort)_offsets.Length,
        _ => null
    };
    public ReadOnlySpan<ushort> GlyphNameIndices => _indices;
    public ReadOnlySpan<sbyte> SignedOffsets => _offsets;
    public IReadOnlyList<string> CustomNames => _names;

    public string? GetGlyphName(int glyphId)
    {
        if (NameKind == PostNameKind.NoNames) return null;
        ArgumentOutOfRangeException.ThrowIfNegative(glyphId);
        if (glyphId >= NumGlyphs!.Value) throw new ArgumentOutOfRangeException(nameof(glyphId));
        int index = NameKind switch
        {
            PostNameKind.Standard258 => glyphId,
            PostNameKind.SignedOffsets => glyphId + _offsets[glyphId],
            _ => _indices[glyphId]
        };
        return index < 258 ? MacintoshGlyphNames.GetName(index) : _names[index - 258];
    }
}

/// <summary>Exactly one of StandardIndex and CustomName must be supplied.</summary>
public readonly record struct PostNameReplacement(int GlyphId, ushort? StandardIndex = null,
    string? CustomName = null);

public sealed record PostEditPlan
{
    public short? UnderlinePosition { get; init; }
    public IReadOnlyList<PostNameReplacement> Names { get; init; } = Array.Empty<PostNameReplacement>();
}
