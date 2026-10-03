using FontWeave.Core;

namespace FontWeave.Tables.Layout;

public readonly record struct ClassDefRange(ushort StartGlyph, ushort EndGlyph, ushort ClassValue);

/// <summary>Disjoint explicit membership ranges, including explicit class-zero membership.</summary>
public sealed class ClassDefTable
{
    private readonly ClassDefRange[] _ranges;

    internal ClassDefTable(ClassDefRange[] ranges, int numGlyphs, ushort format,
        DataLocation location, int consumedLength)
    {
        _ranges = ranges;
        NumGlyphs = numGlyphs;
        Format = format;
        Location = location;
        ConsumedLength = consumedLength;
    }

    public ReadOnlySpan<ClassDefRange> Ranges => _ranges;
    public int NumGlyphs { get; }
    public ushort Format { get; }
    public DataLocation Location { get; }
    public int ConsumedLength { get; }

    public ushort ClassOf(int glyphId)
    {
        int lo = 0, hi = _ranges.Length - 1;
        while (lo <= hi)
        {
            int mid = lo + (hi - lo) / 2;
            var range = _ranges[mid];
            if (glyphId < range.StartGlyph) hi = mid - 1;
            else if (glyphId > range.EndGlyph) lo = mid + 1;
            else return range.ClassValue;
        }
        return 0;
    }

    public ReadStatus TryGetClass(int glyphId, out ushort classValue, out FontDiagnostic diagnostic)
    {
        classValue = 0;
        if (glyphId < 0 || glyphId >= NumGlyphs)
            return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                Location.AtLocal(DataField.GlyphId, 0), out diagnostic,
                unchecked((ulong)(long)glyphId), (ulong)NumGlyphs);
        classValue = ClassOf(glyphId);
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>Preserves explicit membership. Format 1 cannot omit intervening slots;
    /// retain them with explicit class-zero ranges or create a format-2 model.</summary>
    public ReadStatus TryWithRanges(ReadOnlySpan<ClassDefRange> ranges, out ClassDefTable? updated,
        out FontDiagnostic diagnostic, BuildLimits? limits = null) =>
        ClassDefTableCompiler.TryCreate(ranges, NumGlyphs, Format, out updated, out diagnostic, limits, Location);
}
