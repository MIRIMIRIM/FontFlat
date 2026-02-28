using System.Buffers.Binary;

namespace OTFontFile2;

public sealed class SfntFile : IDisposable
{
    private readonly FontBuffer _buffer;
    private readonly bool _isTtc;
    private readonly int _ttcOffsetsOffset;
    private readonly int _fontCount;

    private SfntFile(FontBuffer buffer, bool isTtc, int ttcOffsetsOffset, int fontCount)
    {
        _buffer = buffer;
        _isTtc = isTtc;
        _ttcOffsetsOffset = ttcOffsetsOffset;
        _fontCount = fontCount;
    }

    public int FontCount => _fontCount;
    public bool IsTtc => _isTtc;

    public static SfntFile Open(string path)
    {
        if (!TryOpen(path, out var file, out var error))
            throw new FontParseException(error);
        return file;
    }

    public static bool TryOpen(string path, out SfntFile file, out FontParseError error)
    {
        FontBuffer? buffer = null;
        try
        {
            buffer = FontBuffer.MapReadOnlyFile(path);
            if (!TryParse(buffer, out file, out error))
            {
                buffer.Dispose();
                return false;
            }

            return true;
        }
        catch
        {
            buffer?.Dispose();
            file = null!;
            error = new FontParseError(FontParseErrorKind.Unsupported);
            return false;
        }
    }

    public static SfntFile FromMemory(ReadOnlyMemory<byte> memory)
    {
        if (!TryFromMemory(memory, out var file, out var error))
            throw new FontParseException(error);
        return file;
    }

    public static bool TryFromMemory(ReadOnlyMemory<byte> memory, out SfntFile file, out FontParseError error)
    {
        var buffer = FontBuffer.FromMemory(memory);
        if (!TryParse(buffer, out file, out error))
        {
            buffer.Dispose();
            return false;
        }

        return true;
    }

    private static bool TryParse(FontBuffer buffer, out SfntFile file, out FontParseError error)
    {
        var data = buffer.Span;
        if (data.Length < 4)
        {
            file = null!;
            error = new FontParseError(FontParseErrorKind.EndOfData, offset: 0);
            return false;
        }

        uint tagOrVersion = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(0, 4));
        if (tagOrVersion == TagConstants.TtcTag)
        {
            return TryParseTtc(buffer, out file, out error);
        }

        if (!IsValidSfntVersion(tagOrVersion))
        {
            file = null!;
            error = new FontParseError(FontParseErrorKind.InvalidSfntVersion, offset: 0);
            return false;
        }

        // Single-font sfnt file
        file = new SfntFile(buffer, isTtc: false, ttcOffsetsOffset: 0, fontCount: 1);
        error = default;
        return true;
    }

    private static bool TryParseTtc(FontBuffer buffer, out SfntFile file, out FontParseError error)
    {
        var data = buffer.Span;
        if (data.Length < 16)
        {
            file = null!;
            error = new FontParseError(FontParseErrorKind.InvalidTtcHeader, offset: 0);
            return false;
        }

        // TTC header:
        // 0..3  'ttcf'
        // 4..7  version
        // 8..11 numFonts
        // 12..  offsets[numFonts]
        uint version = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(4, 4));
        _ = version; // reserved for future use (v1.0 / v2.0)

        uint numFonts = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(8, 4));
        if (numFonts == 0 || numFonts > int.MaxValue)
        {
            file = null!;
            error = new FontParseError(FontParseErrorKind.InvalidTtcHeader, offset: 8);
            return false;
        }

        int offsetsOffset = 12;
        long needed = offsetsOffset + (long)numFonts * 4;
        if (needed > data.Length)
        {
            file = null!;
            error = new FontParseError(FontParseErrorKind.InvalidTtcHeader, offset: offsetsOffset);
            return false;
        }

        file = new SfntFile(buffer, isTtc: true, ttcOffsetsOffset: offsetsOffset, fontCount: (int)numFonts);
        error = default;
        return true;
    }

    public SfntFont GetFont(int index)
    {
        if ((uint)index >= (uint)_fontCount)
            throw new ArgumentOutOfRangeException(nameof(index));

        int offsetTableOffset = 0;
        if (_isTtc)
        {
            var data = _buffer.Span;
            uint offset = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(_ttcOffsetsOffset + (index * 4), 4));
            if (offset > int.MaxValue)
                throw new NotSupportedException("Fonts larger than 2GB are not supported by span-based APIs.");

            offsetTableOffset = (int)offset;
        }

        if (!SfntFont.TryCreate(_buffer, offsetTableOffset, out var font, out var error))
            throw new FontParseException(error);

        return font;
    }

    public void Dispose() => _buffer.Dispose();

    private static bool IsValidSfntVersion(uint value)
    {
        return value == TagConstants.TrueTypeVersion
            || value == TagConstants.OttoTag
            || value == TagConstants.TrueTag
            || value == TagConstants.Typ1Tag;
    }

    private static class TagConstants
    {
        public const uint TrueTypeVersion = 0x00010000;
        public const uint TtcTag = 0x74746366;  // 'ttcf'
        public const uint OttoTag = 0x4F54544F; // 'OTTO'
        public const uint TrueTag = 0x74727565; // 'true'
        public const uint Typ1Tag = 0x74797031; // 'typ1'
    }
}
