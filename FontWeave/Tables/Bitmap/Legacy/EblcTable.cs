using FontWeave.Core;

namespace FontWeave.Tables;

public readonly record struct EblcIndexSubtableRecord(int Ordinal,
    ushort FirstGlyphIndex, ushort LastGlyphIndex, uint IndexSubtableOffset);

public readonly record struct EblcGlyphLocation(ushort ImageFormat,
    ulong PositionInEbdt, ulong ByteLength, byte BitDepth = 0, ushort IndexFormat = 0)
{
    internal ulong ImageFormatPosition { get; init; }
}

public abstract class EblcIndexSubtable
{
    protected EblcIndexSubtable(int ordinal, ushort indexFormat, ushort imageFormat,
        uint imageDataOffset, ushort firstGlyphIndex, ushort lastGlyphIndex)
    {
        Ordinal = ordinal;
        IndexFormat = indexFormat;
        ImageFormat = imageFormat;
        ImageDataOffset = imageDataOffset;
        FirstGlyphIndex = firstGlyphIndex;
        LastGlyphIndex = lastGlyphIndex;
    }

    public int Ordinal { get; }
    public ushort IndexFormat { get; }
    public ushort ImageFormat { get; }
    public uint ImageDataOffset { get; }
    public ushort FirstGlyphIndex { get; }
    public ushort LastGlyphIndex { get; }
}

public sealed class EblcOffsetSubtable : EblcIndexSubtable
{
    public EblcOffsetSubtable(int ordinal, ushort indexFormat, ushort imageFormat,
        uint imageDataOffset, ushort firstGlyphIndex, ushort lastGlyphIndex, uint[] sbitOffsets)
        : base(ordinal, indexFormat, imageFormat, imageDataOffset, firstGlyphIndex,
              lastGlyphIndex) => SbitOffsets = sbitOffsets;

    public uint[] SbitOffsets { get; }
}

public sealed class EblcImageSizeSubtable : EblcIndexSubtable
{
    public EblcImageSizeSubtable(int ordinal, ushort imageFormat, uint imageDataOffset,
        ushort firstGlyphIndex, ushort lastGlyphIndex, uint imageSize, byte[] bigMetrics)
        : base(ordinal, EblcLayout.IndexFormat2, imageFormat, imageDataOffset,
              firstGlyphIndex, lastGlyphIndex)
    {
        ImageSize = imageSize;
        BigMetrics = bigMetrics;
    }

    public uint ImageSize { get; }

    public byte[] BigMetrics { get; }
}

public sealed class EblcPairSubtable : EblcIndexSubtable
{
    public EblcPairSubtable(int ordinal, ushort imageFormat, uint imageDataOffset,
        ushort firstGlyphIndex, ushort lastGlyphIndex, ushort[] glyphIds, ushort[] sbitOffsets)
        : base(ordinal, EblcLayout.IndexFormat4, imageFormat, imageDataOffset,
              firstGlyphIndex, lastGlyphIndex)
    {
        GlyphIds = glyphIds;
        SbitOffsets = sbitOffsets;
    }

    public ushort[] GlyphIds { get; }

    public ushort[] SbitOffsets { get; }
}

public sealed class EblcGlyphIdArraySubtable : EblcIndexSubtable
{
    public EblcGlyphIdArraySubtable(int ordinal, ushort imageFormat, uint imageDataOffset,
        ushort firstGlyphIndex, ushort lastGlyphIndex, uint imageSize, byte[] bigMetrics,
        ushort[] glyphIds)
        : base(ordinal, EblcLayout.IndexFormat5, imageFormat, imageDataOffset,
              firstGlyphIndex, lastGlyphIndex)
    {
        ImageSize = imageSize;
        BigMetrics = bigMetrics;
        GlyphIds = glyphIds;
    }

    public uint ImageSize { get; }

    public byte[] BigMetrics { get; }

    public ushort[] GlyphIds { get; }
}

public sealed class EblcStrike
{
    public uint IndexSubtableListOffset { get; }
    public uint IndexSubtableListSize { get; }
    public uint ColorRef { get; }
    public byte[] HoriMetrics { get; }
    public byte[] VertMetrics { get; }
    public ushort StartGlyphIndex { get; }
    public ushort EndGlyphIndex { get; }
    public byte PpemX { get; }
    public byte PpemY { get; }
    public byte BitDepth { get; }
    public sbyte Flags { get; }
    public IReadOnlyList<EblcIndexSubtable> Subtables => Array.AsReadOnly(_subtables);
    private readonly EblcIndexSubtable[] _subtables;

    public EblcStrike(uint indexSubtableListOffset, uint indexSubtableListSize,
        uint colorRef, byte[] horiMetrics, byte[] vertMetrics, ushort startGlyphIndex,
        ushort endGlyphIndex, byte ppemX, byte ppemY, byte bitDepth, sbyte flags,
        EblcIndexSubtable[] subtables)
    {
        IndexSubtableListOffset = indexSubtableListOffset;
        IndexSubtableListSize = indexSubtableListSize;
        ColorRef = colorRef;
        HoriMetrics = horiMetrics;
        VertMetrics = vertMetrics;
        StartGlyphIndex = startGlyphIndex;
        EndGlyphIndex = endGlyphIndex;
        PpemX = ppemX;
        PpemY = ppemY;
        BitDepth = bitDepth;
        Flags = flags;
        _subtables = subtables;
    }

    public bool IsMonochromeStrike => EblcLayout.IsMonochrome(BitDepth);
}

public sealed class EblcTable
{
    private readonly EblcStrike[] _sizes;

    internal EblcTable(ushort majorVersion, ushort minorVersion, EblcStrike[] sizes,
        DataLocation location, int consumedLength)
    {
        MajorVersion = majorVersion;
        MinorVersion = minorVersion;
        _sizes = sizes;
        Location = location;
        ConsumedLength = consumedLength;
    }

    public ushort MajorVersion { get; }
    public ushort MinorVersion { get; }
    public int SizeCount => _sizes.Length;
    public IReadOnlyList<EblcStrike> Sizes => Array.AsReadOnly(_sizes);

    public DataLocation Location { get; }
    public int ConsumedLength { get; }

    public ReadStatus TryGetStrike(int sizeIndex, out EblcStrike strike,
        out FontDiagnostic diagnostic)
    {
        strike = null!;
        if (sizeIndex < 0 || (uint)sizeIndex >= (uint)_sizes.Length)
            return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                Location.AtLocal(DataField.Count, EblcLayout.BitmapSizeAt, sizeIndex),
                out diagnostic, unchecked((ulong)(long)sizeIndex), (ulong)_sizes.Length);
        strike = _sizes[sizeIndex];
        diagnostic = default;
        return ReadStatus.Done;
    }

    public ReadStatus TryWithStrikeMetrics(int sizeIndex, ReadOnlySpan<byte> hori,
        ReadOnlySpan<byte> vert, out EblcTable? updated, out FontDiagnostic diagnostic)
    {
        updated = null;
        if (sizeIndex < 0 || (uint)sizeIndex >= (uint)_sizes.Length)
            return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                Location.AtLocal(DataField.Count, EblcLayout.BitmapSizeAt, sizeIndex),
                out diagnostic, unchecked((ulong)(long)sizeIndex), (ulong)_sizes.Length);
        if (hori.Length != EblcLayout.SbitLineMetricsLength)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.AtLocal(DataField.Length, EblcLayout.BitmapSizeAt +
                    (ulong)sizeIndex * EblcLayout.BitmapSizeStride + EblcLayout.HoriMetricsAt, sizeIndex), out diagnostic,
                (ulong)hori.Length, (ulong)EblcLayout.SbitLineMetricsLength);
        if (vert.Length != EblcLayout.SbitLineMetricsLength)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.AtLocal(DataField.Length, EblcLayout.BitmapSizeAt +
                    (ulong)sizeIndex * EblcLayout.BitmapSizeStride + EblcLayout.VertMetricsAt, sizeIndex), out diagnostic,
                (ulong)vert.Length, (ulong)EblcLayout.SbitLineMetricsLength);
        var source = _sizes[sizeIndex];
        var sizes = new EblcStrike[_sizes.Length];
        Array.Copy(_sizes, sizes, _sizes.Length);
        sizes[sizeIndex] = new EblcStrike(source.IndexSubtableListOffset,
            source.IndexSubtableListSize, source.ColorRef, hori.ToArray(), vert.ToArray(),
            source.StartGlyphIndex, source.EndGlyphIndex, source.PpemX, source.PpemY,
            source.BitDepth, source.Flags, CloneSubtables(source));
        updated = new(MajorVersion, MinorVersion, sizes, Location, ConsumedLength);
        diagnostic = default;
        return ReadStatus.Done;
    }

    public ReadStatus TryWithBitmapSize(int sizeIndex, byte ppemX, byte ppemY,
        byte bitDepth, sbyte flags, out EblcTable? updated, out FontDiagnostic diagnostic)
    {
        updated = null;
        if (sizeIndex < 0 || (uint)sizeIndex >= (uint)_sizes.Length)
            return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                Location.AtLocal(DataField.Count, EblcLayout.BitmapSizeAt, sizeIndex),
                out diagnostic, unchecked((ulong)(long)sizeIndex), (ulong)_sizes.Length);
        if (!EblcLayout.IsBitDepthDefined(bitDepth))
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.AtLocal(DataField.Payload, 0, sizeIndex), out diagnostic,
                bitDepth, 0UL);
        if ((flags & EblcLayout.FlagsReservedMask) != 0)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.AtLocal(DataField.Flags, 0, sizeIndex), out diagnostic,
                (ulong)(flags & EblcLayout.FlagsReservedMask), 0UL);
        var source = _sizes[sizeIndex];
        var sizes = new EblcStrike[_sizes.Length];
        Array.Copy(_sizes, sizes, _sizes.Length);
        sizes[sizeIndex] = new EblcStrike(source.IndexSubtableListOffset,
            source.IndexSubtableListSize, source.ColorRef, source.HoriMetrics,
            source.VertMetrics, source.StartGlyphIndex, source.EndGlyphIndex,
            ppemX, ppemY, bitDepth, flags, CloneSubtables(source));
        updated = new(MajorVersion, MinorVersion, sizes, Location, ConsumedLength);
        diagnostic = default;
        return ReadStatus.Done;
    }

    private static EblcIndexSubtable[] CloneSubtables(EblcStrike strike)
    {
        var copies = new EblcIndexSubtable[strike.Subtables.Count];
        for (int r = 0; r < copies.Length; r++)
            copies[r] = strike.Subtables[r];
        return copies;
    }
}
