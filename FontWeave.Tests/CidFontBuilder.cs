// Wire-level fixture builder for a minimal, valid CID-keyed CFF 1.0 table.
//
// The image is assembled from the wire format directly (TN5176), never by asking the product
// to write it, so a test that feeds it to Cff1View.TryOpen/TryReadCid is testing the reader
// against an independent expectation rather than against its own output.
//
// The layout this builder emits, and the reason for each position:
//
//   Header(4) | Name INDEX | Top DICT INDEX | String INDEX | Global Subrs INDEX
//   | charset | FDSelect | FDArray | the Private DICTs of the FDs that declare one | CharStrings
//
// A CID Font DICT owns its Private DICT, so a Font DICT whose FD has no Private DICT simply
// omits the Private entry. The reader tiles the region after the Global Subrs INDEX with
// exactly the blocks the FDs name and refuses a gap, so no unreferenced Private block may
// exist: absent Private entries must mean absent bytes, which is what this builder does.
//
// The Top DICT always occupies a fixed 32 bytes (every offset operand is written in the
// 5-byte DICT integer form), so the layout can be computed in two passes without guessing.
using System.Text;
using FontWeave.Tables;

namespace FontWeave.Tests;

/// <summary>Requested shape of one synthetic CID-keyed CFF 1.0 table.</summary>
public sealed class CidFont
{
    /// <summary>Number of Font DICTs in the FDArray INDEX. At least 1.</summary>
    public int FdCount { get; init; } = 1;

    /// <summary>Glyphs in the CharStrings INDEX. Index 0 is .notdef, as in every CFF font.</summary>
    public int NumGlyphs { get; init; } = 3;

    /// <summary>Per-FD Private DICT presence, indexed by FD ordinal and at least
    /// <see cref="FdCount"/> long. True emits a Private DICT for that FD; false omits the
    /// entry from its Font DICT and emits no bytes for it at all.</summary>
    public bool[]? HasPrivate { get; init; }

    /// <summary>CIDCount (operator 0x0c22). Must be at least <see cref="NumGlyphs"/>; when
    /// null the builder uses <see cref="NumGlyphs"/>.</summary>
    public int? CidCount { get; init; }

    /// <summary>Registry SID of the ROS triple. It must resolve in the String INDEX, so the
    /// builder rewrites it to the first custom SID (391).</summary>
    public int RegistrySid { get; init; } = 0;

    /// <summary>Ordering SID of the ROS triple, treated exactly like <see cref="RegistrySid"/>
    /// and rewritten to the second custom SID (392).</summary>
    public int OrderingSid { get; init; } = 0;

    /// <summary>Supplement of the ROS triple. Any non-negative value is legal.</summary>
    public int Supplement { get; init; } = 0;

    /// <summary>Private to the builder; the entry point is <see cref="CidFontBuilder.Build"/>.</summary>
    internal CidFont()
    {
    }
}

/// <summary>Where every emitted block landed, so a test can assert on the layout as well as
/// on what the reader made of it. All offsets are table-relative byte offsets.</summary>
public sealed class CidFontImage
{
    private readonly byte[] _bytes;
    private readonly bool[] _hasPrivate;

    internal CidFontImage(byte[] bytes, bool[] hasPrivate, int fdCount, int numGlyphs, int cidCount,
        int charsetOffset, int fdSelectOffset, int fdArrayOffset, int charStringsOffset,
        int[] privateOffsets, int[] privateSizes)
    {
        _bytes = bytes;
        _hasPrivate = hasPrivate;
        FdCount = fdCount;
        NumGlyphs = numGlyphs;
        CidCount = cidCount;
        CharsetOffset = charsetOffset;
        FdSelectOffset = fdSelectOffset;
        FdArrayOffset = fdArrayOffset;
        CharStringsOffset = charStringsOffset;
        PrivateOffsets = privateOffsets;
        PrivateSizes = privateSizes;
    }

    public byte[] Bytes => _bytes;

    public int Length => _bytes.Length;

    public int FdCount { get; }

    public int NumGlyphs { get; }

    public int CidCount { get; }

    public int CharsetOffset { get; }

    public int FdSelectOffset { get; }

    public int FdArrayOffset { get; }

    public int CharStringsOffset { get; }

    /// <summary>Private DICT offset per FD, or -1 for an FD that declares none.</summary>
    public int[] PrivateOffsets { get; }

    /// <summary>Private DICT byte length per FD, or -1 for an FD that declares none.</summary>
    public int[] PrivateSizes { get; }

    /// <summary>Copy of the requested per-FD Private presence, for comparison with the model's
    /// <c>HasPrivate</c>.</summary>
    public bool[] HasPrivate => (bool[])_hasPrivate.Clone();
}

/// <summary>Builds raw CID-keyed CFF 1.0 table bytes accepted by
/// <see cref="Cff1View.TryOpen"/> and <see cref="Cff1View.TryReadCid"/>.</summary>
public static class CidFontBuilder
{
    /// <summary>Header size emitted by the builder; the strict reader requires exactly the
    /// four-byte CFF1 header.</summary>
    public const int HeaderSize = 4;

    /// <summary>First SID of the String INDEX: SIDs below this are the standard strings, and
    /// the reader bounds every custom SID by 391 + String INDEX count.</summary>
    public const int FirstCustomSid = 391;

    /// <summary>Value of the Name INDEX entry.</summary>
    public const string FontName = "CidSynthetic";

    // The two strings the builder puts in the String INDEX, reached as SIDs 391 and 392. The
    // ROS registry/ordering operands are rewritten to point at them, because a SID outside the
    // string space (the standard strings start at 0, custom strings at 391) is rejected by the
    // reader's SID bound check.
    private const string RegistryName = "FontWeave";
    private const string OrderingName = "Synthetic";

    // The Private DICT the builder emits for an FD that declares one: DefaultWidthX 500,
    // NominalWidthX 400, each followed by its one-byte operator.
    private static readonly int[] PrivateDefaultWidthX = [500];
    private static readonly int[] PrivateNominalWidthX = [400];

    /// <summary>Measured, not assumed: the layout has to agree with what the emit path actually
    /// writes, so the size is taken from the encoder itself. A hand-maintained constant here
    /// silently desynchronises the block offsets from the emitted bytes.</summary>
    private static readonly int PrivateDictSize = MeasurePrivateDictSize();

    /// <summary>Size of a Font DICT whose FD declares no Private DICT: CIDFontName's operand (one
    /// or two bytes depending on the SID) plus its two-byte operator.</summary>
    private const int FontDictWithoutPrivateSize = 3;

    /// <summary>Bytes a Font DICT that declares a Private DICT adds at its end: the two operands
    /// of the Private operator (size, then offset), both in the fixed-width form, plus the
    /// one-byte operator itself. Private is operator 18 (0x12), which is not a 12-prefixed
    /// operator, so it costs one byte and not two.</summary>
    private const int PrivateEntrySize = FixedDictIntegerSize + FixedDictIntegerSize + 1;

    /// <summary>Bytes of one fixed-width DICT integer: the 0x1d form byte plus four operand
    /// bytes.</summary>
    private const int FixedDictIntegerSize = 5;

    // DICT operators not exposed by any public API: Cff1Layout is internal. Every value here
    // is copied from Adobe TN5176 (Appendix A) and from the operator constants the reader uses.
    private const int OpCharset = 15;
    private const int OpCharStrings = 17;
    private const int OpPrivate = 18;
    private const int OpRos = 0x0c1e;
    private const int OpCidCount = 0x0c22;
    private const int OpFdArray = 0x0c24;
    private const int OpFdSelect = 0x0c25;
    private const int OpCidFontName = 0x0c26;

    /// <summary>Exact length of the Top DICT emitted by <see cref="TopDict"/>: ROS is three
    /// fixed operands plus its two-byte operator (17), and the five remaining entries are one
    /// fixed operand each plus their operators (1 + 2 + 1 + 2 + 2 = 8 bytes of operators).</summary>
    private const int TopDictSize = 17 + 5 * FixedDictIntegerSize + 8;

    private const int FdSelectFormat3 = 3;

    /// <summary>Assembles one CID-keyed CFF 1.0 table image.</summary>
    /// <param name="font">The requested shape.</param>
    /// <returns>The image, its bytes and the block offsets that were chosen.</returns>
    /// <exception cref="ArgumentOutOfRangeException">A requested count is outside the range the
    /// wire format can express, or CIDCount is below the glyph count.</exception>
    public static CidFontImage Build(CidFont font)
    {
        ArgumentNullException.ThrowIfNull(font);

        int fdCount = font.FdCount;
        int numGlyphs = font.NumGlyphs;
        if (fdCount < 1)
            throw new ArgumentOutOfRangeException(nameof(font), fdCount, "FdCount must be at least 1.");
        if (numGlyphs < 1)
            throw new ArgumentOutOfRangeException(nameof(font), numGlyphs, "NumGlyphs must be at least 1.");
        // Glyph indices are written as u16 in the FDSelect and CIDs are written as u16 in the
        // charset, so the glyph count has to fit the 16-bit glyph space.
        if (numGlyphs > 0xFFFF)
            throw new ArgumentOutOfRangeException(nameof(font), numGlyphs,
                "NumGlyphs must fit in the 16-bit glyph space.");
        // Every FD has to be reached by the FDSelect, because the reader takes the FD count from
        // the greatest referenced index plus one and compares it with the FDArray count.
        if (fdCount > numGlyphs)
            throw new ArgumentOutOfRangeException(nameof(font), fdCount,
                "FdCount cannot exceed NumGlyphs: the FDSelect must reach every FD.");
        int cidCount = font.CidCount ?? numGlyphs;
        if (cidCount < numGlyphs)
            throw new ArgumentOutOfRangeException(nameof(font), cidCount,
                "CidCount must not be smaller than NumGlyphs.");
        if (cidCount > 0xFFFF)
            throw new ArgumentOutOfRangeException(nameof(font), cidCount,
                "CidCount must fit in the 16-bit CID space.");
        if (font.Supplement < 0)
            throw new ArgumentOutOfRangeException(nameof(font), font.Supplement,
                "Supplement must not be negative.");

        bool[] hasPrivate = ResolvePrivateFlags(font.HasPrivate, fdCount);

        int registrySid = FirstCustomSid;
        int orderingSid = FirstCustomSid + 1;

        // ---- fixed-size parts ------------------------------------------------------
        // TopDictSize is a compile-time constant because every Top DICT operand is written in
        // the fixed five-byte form. That keeps the layout a straight computation rather than a
        // fixpoint over offsets whose width depends on their own value.

        byte[] nameBytes = Encoding.Latin1.GetBytes(FontName);
        byte[] stringBytes = Encoding.Latin1.GetBytes(RegistryName + OrderingName);
        int expectedStringBytes = Encoding.Latin1.GetByteCount(RegistryName)
            + Encoding.Latin1.GetByteCount(OrderingName);
        if (stringBytes.Length != expectedStringBytes)
            throw new InvalidOperationException("custom string lengths disagree.");

        int headerEnd = HeaderSize;
        int nameIndexSize = IndexLength(1, nameBytes.Length);
        int topDictIndexSize = IndexLength(1, TopDictSize);
        int stringIndexSize = IndexLength(2, stringBytes.Length);
        int globalSubrsIndexSize = 2; // an INDEX with count 0 is just the two-byte count

        int nameIndexAt = headerEnd;
        int topDictIndexAt = nameIndexAt + nameIndexSize;
        int stringIndexAt = topDictIndexAt + topDictIndexSize;
        int globalSubrsAt = stringIndexAt + stringIndexSize;
        int globalsEnd = globalSubrsAt + globalSubrsIndexSize;

        // ---- variable-size parts ---------------------------------------------------
        // charset format 0: a format byte plus one 16-bit CID per non-.notdef glyph. CIDs are
        // the identity (glyph g maps to CID g), which CIDCount >= NumGlyphs always permits.
        int charsetSize = 1 + 2 * (numGlyphs - 1);

        // FDSelect format 3: format byte, u16 range count, 3 bytes per range, u16 sentinel. The
        // run count is fixed at FdCount so the block size is known before the runs are chosen.
        int rangeCount = fdCount;
        int fdSelectSize = 1 + 2 + 3 * rangeCount + 2;

        byte[][] fontDicts = new byte[fdCount][];
        int[] privateSizes = new int[fdCount];
        int fdArrayDataLength = 0;
        for (int fd = 0; fd < fdCount; fd++)
        {
            int size = hasPrivate[fd] ? PrivateDictSize : -1;
            privateSizes[fd] = size;
            int privateSize = size < 0 ? 0 : size;
            fontDicts[fd] = FontDict(FirstCustomSid + 2 + fd, privateSize);
            // The CIDFontName operand is one byte for a small SID and two for a large one, so the
            // minimum size is asserted here and the exact operand position is located by its
            // operator when the offset is patched.
            if (fontDicts[fd].Length < FontDictWithoutPrivateSize)
                throw new InvalidOperationException(
                    $"Font DICT {fd} is smaller than its CIDFontName entry.");
            fdArrayDataLength += fontDicts[fd].Length;
        }
        int fdArraySize = IndexLength(fdCount, fdArrayDataLength);

        int privateBytes = 0;
        for (int fd = 0; fd < fdCount; fd++)
            if (privateSizes[fd] > 0)
                privateBytes += privateSizes[fd];

        // CharStrings holds one 1-byte program per glyph; the count and offSize fields plus the
        // count+1 one-byte offsets add 3 + numGlyphs + 1 bytes.
        int charStringsDataLength = numGlyphs; // one endchar byte per glyph
        int charStringsSize = IndexLength(numGlyphs, charStringsDataLength);

        int charsetAt = globalsEnd;
        int fdSelectAt = charsetAt + charsetSize;
        int fdArrayAt = fdSelectAt + fdSelectSize;
        int privateAt = fdArrayAt + fdArraySize;
        int[] privateOffsets = new int[fdCount];
        int cursor = privateAt;
        for (int fd = 0; fd < fdCount; fd++)
        {
            if (privateSizes[fd] < 0)
            {
                privateOffsets[fd] = -1;
                continue;
            }
            privateOffsets[fd] = cursor;
            cursor += privateSizes[fd];
        }
        if (cursor != privateAt + privateBytes)
            throw new InvalidOperationException("the Private DICT region did not tile.");
        int charStringsAt = cursor;

        // ---- emit ------------------------------------------------------------------
        int total = charStringsAt + charStringsSize;
        var writer = new WireWriter(total);

        // Header: major 1, minor 0, hdrSize 4, offSize 1. The reader admits the name-keyed and
        // CID-keyed forms through this same four-byte gate.
        writer.Byte(1);
        writer.Byte(0);
        writer.Byte(HeaderSize);
        writer.Byte(1);

        WriteIndex(ref writer, [nameBytes]);
        WriteIndex(ref writer, [TopDict(cidCount, charsetAt, charStringsAt, fdArrayAt, fdSelectAt,
            registrySid, orderingSid, font.Supplement)]);
        WriteIndex(ref writer, [
            Encoding.Latin1.GetBytes(RegistryName),
            Encoding.Latin1.GetBytes(OrderingName)
        ]);
        WriteIndex(ref writer, []); // no Global Subrs

        WriteCharset(ref writer, numGlyphs);
        WriteFdSelect(ref writer, numGlyphs, fdCount);
        WriteIndex(ref writer, fontDicts);

        // Patch each Font DICT's Private offset now that the region is placed, then emit the
        // Private DICTs. The placeholder was written in the fixed five-byte DICT integer form, so
        // patching cannot change any block's size. The mutation has to happen through a ref
        // parameter: WireWriter is a struct holding its own array.
        PatchPrivateOffsets(ref writer, fdArrayAt, fdArrayDataLength, fontDicts, privateSizes,
            privateOffsets);

        for (int fd = 0; fd < fdCount; fd++)
        {
            if (privateSizes[fd] < 0)
                continue;
            WritePrivateDict(ref writer, PrivateDefaultWidthX, PrivateNominalWidthX);
        }

        WriteIndex(ref writer, NonDefCharStrings(numGlyphs));
        if (writer.Length != total)
            throw new InvalidOperationException(
                $"the CID CFF image was expected to be {total} bytes but is {writer.Length}.");

        return new CidFontImage(writer.ToArray(), hasPrivate, fdCount, numGlyphs, cidCount,
            charsetAt, fdSelectAt, fdArrayAt, charStringsAt, privateOffsets,
            (int[])privateSizes.Clone());
    }

    /// <summary>Convenience overload for the common "every FD has a Private DICT" shape.</summary>
    public static CidFontImage Build(int fdCount, int numGlyphs) =>
        Build(new CidFont { FdCount = fdCount, NumGlyphs = numGlyphs });

    /// <summary>The requested Private presence, defaulting to every FD having one.</summary>
    private static bool[] ResolvePrivateFlags(bool[]? requested, int fdCount)
    {
        var flags = new bool[fdCount];
        if (requested is null)
        {
            Array.Fill(flags, true);
            return flags;
        }
        if (requested.Length < fdCount)
            throw new ArgumentOutOfRangeException(nameof(requested), requested.Length,
                "HasPrivate must carry one entry per FD.");
        Array.Copy(requested, flags, fdCount);
        return flags;
    }

    /// <summary>TOP DICT: ROS, CIDCount, Charset, CharStrings, FDArray, FDSelect.
    ///
    /// Every operand is written in the fixed five-byte form so the entry's length is known
    /// before the block offsets it has to contain are computed. A variable-width operand would
    /// make the Top DICT's own size depend on the offsets it carries, which is a fixpoint the
    /// layout below deliberately avoids.</summary>
    private static byte[] TopDict(int cidCount, int charsetAt, int charStringsAt, int fdArrayAt,
        int fdSelectAt, int registrySid, int orderingSid, int supplement)
    {
        var writer = new WireWriter(TopDictSize);
        foreach (int value in (int[])[registrySid, orderingSid, supplement])
            WriteDictIntegerFixed(ref writer, value);
        WriteDictOperator(ref writer, OpRos);
        WriteDictIntegerFixed(ref writer, cidCount);
        WriteDictOperator(ref writer, OpCidCount);
        WriteDictIntegerFixed(ref writer, charsetAt);
        WriteDictOperator(ref writer, OpCharset);
        WriteDictIntegerFixed(ref writer, charStringsAt);
        WriteDictOperator(ref writer, OpCharStrings);
        WriteDictIntegerFixed(ref writer, fdArrayAt);
        WriteDictOperator(ref writer, OpFdArray);
        WriteDictIntegerFixed(ref writer, fdSelectAt);
        WriteDictOperator(ref writer, OpFdSelect);
        return writer.ToArray();
    }

    /// <summary>One Font DICT: CIDFontName, then Private (size then offset) when the FD has one.
    /// An FD without a Private DICT carries no Private entry and therefore no bytes.</summary>
    private static byte[] FontDict(int cidFontNameSid, int privateSize)
    {
        var writer = new WireWriter(12);
        WriteDictInteger(ref writer, cidFontNameSid);
        WriteDictOperator(ref writer, OpCidFontName);
        if (privateSize > 0)
        {
            // Both Private operands use the fixed five-byte form even when a shorter form would
            // fit, because the offset is not known until the layout is complete and patching must
            // never change a byte count. The operands are size first, then offset.
            WriteDictIntegerFixed(ref writer, privateSize);
            WriteDictIntegerFixed(ref writer, 0);
            WriteDictOperator(ref writer, OpPrivate);
        }
        return writer.ToArray();
    }

    /// <summary>Private DICT with DefaultWidthX and NominalWidthX, in that order.</summary>
    private static void WritePrivateDict(ref WireWriter writer, int[] defaultWidthX, int[] nominalWidthX)
    {
        WriteDictIntegers(ref writer, defaultWidthX);
        WriteDictOperator(ref writer, 20);
        WriteDictIntegers(ref writer, nominalWidthX);
        WriteDictOperator(ref writer, 21);
    }

    /// <summary>Encodes the Private DICT once to learn its length.</summary>
    private static int MeasurePrivateDictSize()
    {
        var writer = new WireWriter(16);
        WritePrivateDict(ref writer, PrivateDefaultWidthX, PrivateNominalWidthX);
        return writer.Length;
    }

    /// <summary>Fills in the Private offsets of the Font DICTs just written. A Font DICT is
    /// CIDFontName (operand + operator) followed, when the FD declares a Private DICT, by the
    /// Private operator with its size operand and then its offset operand, both in the fixed
    /// five-byte form. The CIDFontName operand is one or two bytes depending on the SID, so the
    /// Private entry is located from the end of the DICT rather than from a fixed position.</summary>
    private static void PatchPrivateOffsets(ref WireWriter writer, int fdArrayAt, int fdArrayDataLength,
        byte[][] fontDicts, int[] privateSizes, int[] privateOffsets)
    {
        int dictAt = fdArrayAt + 3 + (fontDicts.Length + 1) * OffsetSize(1 + fdArrayDataLength);
        for (int fd = 0; fd < fontDicts.Length; fd++)
        {
            int length = fontDicts[fd].Length;
            if (privateSizes[fd] >= 0)
            {
                // The Private entry closes the DICT, so its operands are located from the end:
                // size operand, then offset operand, then the two-byte operator.
                int sizeOperandAt = dictAt + length - PrivateEntrySize;
                int offsetOperandAt = sizeOperandAt + FixedDictIntegerSize;
                if (writer.ByteAt(sizeOperandAt) != 29 || writer.ByteAt(offsetOperandAt) != 29)
                    throw new InvalidOperationException(
                        $"Font DICT {fd} operands are not in the fixed-width form.");
                writer.PatchU32(offsetOperandAt + 1, unchecked((uint)privateOffsets[fd]));
            }
            dictAt += length;
        }
    }

    /// <summary>Charset format 0: a format byte then the CID of every glyph after .notdef.</summary>
    private static void WriteCharset(ref WireWriter writer, int numGlyphs)
    {
        writer.Byte(0);
        for (int gid = 1; gid < numGlyphs; gid++)
            writer.U16(gid);
    }

    /// <summary>FDSelect format 3. Glyph 0 belongs to FD 0 and the glyphs are split into
    /// <paramref name="fdCount"/> contiguous runs so that every FD is reached (the reader takes
    /// the FD count from the greatest referenced index plus one). The sentinel closes the last
    /// run at the glyph count, exactly as the reader requires.</summary>
    private static void WriteFdSelect(ref WireWriter writer, int numGlyphs, int fdCount)
    {
        writer.Byte(FdSelectFormat3);
        writer.U16(fdCount);
        for (int fd = 0; fd < fdCount; fd++)
        {
            // The runs are the whole glyph range cut into FdCount contiguous pieces. Floor
            // division is monotone, so run starts strictly increase, and because FdCount never
            // exceeds the glyph count every run covers at least one glyph.
            writer.U16(fd * numGlyphs / fdCount);
            writer.Byte(fd);
        }
        writer.U16(numGlyphs);
    }

    /// <summary>One endchar per glyph. The reader preserves charstrings verbatim and never
    /// interprets them, so the program only has to be a non-empty byte string.</summary>
    private static byte[][] NonDefCharStrings(int numGlyphs)
    {
        var items = new byte[numGlyphs][];
        for (int gid = 0; gid < numGlyphs; gid++)
            items[gid] = [14]; // endchar
        return items;
    }

    // ---- INDEX -------------------------------------------------------------------

    /// <summary>Length an INDEX with this count and total item bytes occupies: the u16 count,
    /// the offSize byte, count+1 offsets and the item data.</summary>
    private static int IndexLength(int count, int dataLength)
    {
        if (count == 0)
            return 2;
        return 3 + (count + 1) * OffsetSize(1 + dataLength) + dataLength;
    }

    /// <summary>Smallest offset size that can hold the largest offset in an INDEX, whose first
    /// offset is always 1.</summary>
    private static int OffsetSize(int maxOffset)
    {
        if (maxOffset <= 0xFF) return 1;
        if (maxOffset <= 0xFFFF) return 2;
        if (maxOffset <= 0xFF_FFFF) return 3;
        return 4;
    }

    private static void WriteIndex(ref WireWriter writer, byte[][] items)
    {
        if (items.Length == 0)
        {
            writer.U16(0);
            return;
        }
        int dataLength = 0;
        foreach (var item in items)
            dataLength += item.Length;
        int offSize = OffsetSize(1 + dataLength);
        writer.U16(items.Length);
        writer.Byte(offSize);
        int running = 1;
        WriteOffset(ref writer, running, offSize);
        foreach (var item in items)
        {
            running += item.Length;
            WriteOffset(ref writer, running, offSize);
        }
        foreach (var item in items)
            writer.Bytes(item);
    }

    private static void WriteOffset(ref WireWriter writer, int value, int offSize)
    {
        for (int shift = (offSize - 1) * 8; shift >= 0; shift -= 8)
            writer.Byte((value >> shift) & 0xFF);
    }

    // ---- DICT operands -----------------------------------------------------------

    private static void WriteDictIntegers(ref WireWriter writer, int[] values)
    {
        foreach (int value in values)
            WriteDictInteger(ref writer, value);
    }

    private static void WriteDictOperator(ref WireWriter writer, int op)
    {
        if (op > 0xFF)
        {
            writer.Byte(12);
            writer.Byte(op & 0xFF);
            return;
        }
        writer.Byte(op);
    }

    /// <summary>Encodes one DICT integer in the fixed five-byte form (0x1d + int32 big endian).
    /// The length must not depend on the value, because offsets are patched in after the layout
    /// is computed.</summary>
    private static void WriteDictIntegerFixed(ref WireWriter writer, int value)
    {
        writer.Byte(29);
        writer.U32(unchecked((uint)value));
    }

    /// <summary>Encodes one DICT integer. Values that fit the single-byte and 16-bit forms use
    /// them, so the Top DICT's counts stay compact.</summary>
    private static void WriteDictInteger(ref WireWriter writer, int value)
    {
        if (value is >= -107 and <= 107)
        {
            writer.Byte(value + 139);
            return;
        }
        if (value is > 107 and <= 1131)
        {
            int v = value - 108;
            writer.Byte((v >> 8) + 247);
            writer.Byte(v & 0xFF);
            return;
        }
        if (value is < -107 and >= -1131)
        {
            int v = -value - 108;
            writer.Byte((v >> 8) + 251);
            writer.Byte(v & 0xFF);
            return;
        }
        writer.Byte(29);
        writer.U32(unchecked((uint)value));
    }

    /// <summary>Append-only big-endian byte sink.</summary>
    private struct WireWriter(int capacity)
    {
        private byte[] _bytes = new byte[capacity];
        private int _length = 0;

        internal readonly int Length => _length;

        internal void Byte(int value)
        {
            Ensure(1);
            _bytes[_length] = (byte)(value & 0xFF);
            _length++;
        }

        internal void U16(int value)
        {
            Ensure(2);
            _bytes[_length] = (byte)((value >> 8) & 0xFF);
            _bytes[_length + 1] = (byte)(value & 0xFF);
            _length += 2;
        }

        internal void U32(uint value)
        {
            Ensure(4);
            _bytes[_length] = (byte)((value >> 24) & 0xFF);
            _bytes[_length + 1] = (byte)((value >> 16) & 0xFF);
            _bytes[_length + 2] = (byte)((value >> 8) & 0xFF);
            _bytes[_length + 3] = (byte)(value & 0xFF);
            _length += 4;
        }

        internal void Bytes(ReadOnlySpan<byte> source)
        {
            Ensure(source.Length);
            source.CopyTo(_bytes.AsSpan(_length));
            _length += source.Length;
        }

        /// <summary>Byte already written at <paramref name="at"/>, for placeholder checks.</summary>
        internal readonly byte ByteAt(int at) => _bytes[at];

        /// <summary>Overwrites four bytes already written, big endian.</summary>
        internal void PatchU32(int at, uint value)
        {
            _bytes[at] = (byte)((value >> 24) & 0xFF);
            _bytes[at + 1] = (byte)((value >> 16) & 0xFF);
            _bytes[at + 2] = (byte)((value >> 8) & 0xFF);
            _bytes[at + 3] = (byte)(value & 0xFF);
        }

        internal readonly byte[] ToArray() => _bytes.AsSpan(0, _length).ToArray();

        private void Ensure(int extra)
        {
            if (_length + extra <= _bytes.Length)
                return;
            int next = _bytes.Length == 0 ? 16 : _bytes.Length;
            while (next < _length + extra)
                next *= 2;
            Array.Resize(ref _bytes, next);
        }
    }
}
