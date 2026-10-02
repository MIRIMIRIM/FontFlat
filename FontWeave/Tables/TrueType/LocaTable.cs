using FontWeave.Core;

namespace FontWeave.Tables;

/// <summary>Half-open glyph range inside glyf; start == end means an empty glyph.</summary>
public readonly record struct LocaRange(int GlyphId, uint Start, uint End)
{
    public uint Length => End - Start;
    public bool IsEmpty => Start == End;
}

/// <summary>Owned loca offsets plus the format that produced them. Offsets are the
/// scaled glyph offsets; the raw stored short value is not retained because the
/// format is preserved on the model, so it can be reconstructed exactly.</summary>
public sealed class LocaTable
{
    private readonly uint[] _offsets;

    internal LocaTable(uint[] offsets, bool longFormat, DataLocation location,
        DataLocation headLocation, DataLocation maxpLocation)
    {
        _offsets = offsets; LongFormat = longFormat; Location = location;
        HeadLocation = headLocation; MaxpLocation = maxpLocation;
    }

    public bool LongFormat { get; }
    public DataLocation Location { get; }
    public DataLocation HeadLocation { get; }
    public DataLocation MaxpLocation { get; }

    /// <summary>numGlyphs; the offset array holds NumGlyphs + 1 entries.</summary>
    public ushort NumGlyphs => (ushort)(_offsets.Length - 1);
    public ReadOnlySpan<uint> Offsets => _offsets;

    public ReadStatus TryGetGlyphRange(int glyphId, out LocaRange range,
        out FontDiagnostic diagnostic)
    {
        range = default;
        if (glyphId < 0 || glyphId >= NumGlyphs)
            return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                Location.At(DataField.GlyphId, 0, glyphId), out diagnostic,
                unchecked((ulong)(long)glyphId), NumGlyphs);
        range = new(glyphId, _offsets[glyphId], _offsets[glyphId + 1]);
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>True when the stored offsets are non-decreasing; empty glyphs compare equal.</summary>
    public bool IsMonotonic
    {
        get
        {
            for (int i = 1; i < _offsets.Length; i++)
                if (_offsets[i] < _offsets[i - 1]) return false;
            return true;
        }
    }

    /// <summary>Total glyf payload implied by the final offset.</summary>
    public uint GlyfLength => _offsets[^1];

    public int EmptyGlyphCount
    {
        get
        {
            int count = 0;
            for (int i = 0; i < _offsets.Length - 1; i++)
                if (_offsets[i] == _offsets[i + 1]) count++;
            return count;
        }
    }
}
