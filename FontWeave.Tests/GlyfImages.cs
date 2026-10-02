// Wire-level fixture builders shared by the regression tests.
//
// Every image here is produced from the wire format directly, never by asking the product to
// write it, so a test that feeds one of these images to a reader is testing the reader against
// an independent expectation rather than against its own output.
using System.Buffers.Binary;
using FontWeave.Core;
using FontWeave.Tables;

namespace FontWeave.Tests;

/// <summary>Synthetic glyf/loca images and their head/maxp contexts.</summary>
internal static class GlyfImages
{
    // loca carries no header, so its element shape comes from head/maxp. The reader requires
    // all three to name the same immutable source face, hence one shared SourceId.
    internal static readonly DataLocation HeadLocation =
        DataLocation.Anonymous(0x68656164) with { SourceId = 7 };

    internal static readonly DataLocation MaxpLocation =
        DataLocation.Anonymous(0x6D617870) with { SourceId = 7 };

    internal static readonly DataLocation GlyfLocation =
        DataLocation.Anonymous(0x676C7966) with { SourceId = 7 };

    internal static readonly DataLocation LocaLocation =
        DataLocation.Anonymous(0x6C6F6361) with { SourceId = 7 };

    /// <summary>Builds the head/maxp models that describe <paramref name="numGlyphs"/>
    /// glyphs in the requested loca format.</summary>
    internal static (HeadTable Head, MaxpTable Maxp) Context(int numGlyphs, bool longFormat)
    {
        var headBytes = new byte[54];
        BinaryPrimitives.WriteUInt32BigEndian(headBytes.AsSpan(0), 0x00010000);
        BinaryPrimitives.WriteUInt32BigEndian(headBytes.AsSpan(12), 0x5F0F3CF5);
        BinaryPrimitives.WriteUInt16BigEndian(headBytes.AsSpan(18), 1000);
        BinaryPrimitives.WriteInt16BigEndian(headBytes.AsSpan(50), (short)(longFormat ? 1 : 0));
        var maxpBytes = new byte[32];
        BinaryPrimitives.WriteUInt32BigEndian(maxpBytes.AsSpan(0), 0x00010000);
        BinaryPrimitives.WriteUInt16BigEndian(maxpBytes.AsSpan(4), (ushort)numGlyphs);
        BinaryPrimitives.WriteUInt16BigEndian(maxpBytes.AsSpan(14), 1);
        _ = HeadView.TryOpen(headBytes, out var headView, out _, HeadLocation);
        _ = MaxpView.TryOpen(maxpBytes, out var maxpView, out _, MaxpLocation);
        _ = headView.TryRead(out var head, out _);
        _ = maxpView.TryRead(out var maxp, out _);
        return (head!, maxp!);
    }

    internal static byte[] LongLoca(IReadOnlyList<uint> offsets)
    {
        var bytes = new byte[4 * offsets.Count];
        for (int i = 0; i < offsets.Count; i++)
            BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(4 * i), offsets[i]);
        return bytes;
    }

    /// <summary>Opens a loca image through the production context and reader; null when the
    /// bytes disagree with the shape the context demands.</summary>
    internal static LocaTable? Loca(byte[] locaBytes, int numGlyphs, bool longFormat)
    {
        var (head, maxp) = Context(numGlyphs, longFormat);
        if (LocaContext.TryCreate(head, maxp, LocaLocation, out var context, out _) !=
            ReadStatus.Done) return null;
        if (LocaView.TryOpen(locaBytes, out var view, out _, LocaLocation) != ReadStatus.Done)
            return null;
        if (view.TryRead(context!, out var model, out _) != ReadStatus.Done) return null;
        return model;
    }

    internal static LocaTable LocaOrThrow(byte[] locaBytes, int numGlyphs) =>
        Loca(locaBytes, numGlyphs, true)
        ?? throw new InvalidOperationException("the fixture loca image is not readable");

    /// <summary>Builds a glyf image plus its matching long loca offsets. Every record has a
    /// non-empty slot, because a zero-length slot would describe an empty glyph.</summary>
    internal static (byte[] Loca, byte[] Glyf, int NumGlyphs) Assemble(List<byte[]> records)
    {
        var glyf = new List<byte>();
        var offsets = new List<uint> { 0 };
        foreach (var record in records)
        {
            glyf.AddRange(record);
            offsets.Add((uint)glyf.Count);
        }
        return (LongLoca(offsets), [.. glyf], records.Count);
    }

    /// <summary>One glyph whose point count is large while its record stays tiny: every point
    /// repeats ON_CURVE|X_SAME|Y_SAME|REPEAT, so a flag run costs one byte per 256 points and
    /// the coordinate streams are empty. The decoder still materializes one expanded flag byte
    /// and one point record per point, which is what the temporary budget must account for.
    /// </summary>
    internal static byte[] ExpandedSimpleGlyph(int points)
    {
        var body = new List<byte>();
        WriteI16(body, 1);                      // numberOfContours
        for (int i = 0; i < 4; i++) WriteI16(body, 0);
        WriteU16(body, (ushort)(points - 1));   // single contour ending at the last point
        WriteU16(body, 0);                      // instructionLength
        int remaining = points;
        while (remaining > 0)
        {
            body.Add(0x39);                     // ON_CURVE | X_SAME | Y_SAME | REPEAT
            int repeat = Math.Min(255, remaining - 1);
            if (repeat == 0) { remaining -= 1; continue; }
            body.Add((byte)repeat);
            remaining -= repeat + 1;
        }
        return [.. body];
    }

    /// <summary>Many copies of the compressed-but-huge glyph, so the decoded arrays dwarf the
    /// image bytes.</summary>
    internal static (byte[] Loca, byte[] Glyf, int NumGlyphs) RepeatedExpandedGlyph(
        int repeats, int pointsPerGlyph)
    {
        byte[] record = ExpandedSimpleGlyph(pointsPerGlyph);
        var records = new List<byte[]>();
        for (int i = 0; i < repeats; i++) records.Add(record);
        return Assemble(records);
    }

    /// <summary>A glyph with no contours and no instructions, used as a chain terminator.
    /// This is a simple glyph that materializes zero points, not an empty loca slot.</summary>
    internal static byte[] NoContourGlyphRecord()
    {
        var body = new List<byte>();
        WriteI16(body, 0);                      // numberOfContours
        for (int i = 0; i < 4; i++) WriteI16(body, 0);
        WriteU16(body, 0);                      // instructionLength
        return [.. body];
    }

    /// <summary>A composite with one component reference and an XY offset of (0, 0).</summary>
    internal static byte[] CompositeRecord(ushort child)
    {
        var body = new List<byte>();
        WriteI16(body, -1);                     // negative numberOfContours means composite
        for (int i = 0; i < 4; i++) WriteI16(body, 0);
        WriteU16(body, 0x0002);                 // ARGS_ARE_XY_VALUES
        WriteU16(body, child);
        body.Add(0);
        body.Add(0);
        return [.. body];
    }

    /// <summary>A chain of composites of the requested length. Glyph k points at k-1
    /// (<paramref name="ascending"/>) or at k+1, so both directions describe the same acyclic
    /// graph shape and differ only in how the glyphs are numbered.</summary>
    internal static (byte[] Loca, byte[] Glyf, int NumGlyphs) DepthChain(int length,
        bool ascending)
    {
        var records = new List<byte[]> { NoContourGlyphRecord() };
        for (int k = 1; k <= length; k++)
        {
            ushort child = ascending ? (ushort)(k - 1) : (ushort)(k + 1);
            records.Add(CompositeRecord(child));
        }
        // The descending chain references one glyph past the last composite.
        records.Add(NoContourGlyphRecord());
        return Assemble(records);
    }

    /// <summary>Glyph 0 is a plain leaf; glyphs 1 and 2 are composites over it.</summary>
    internal static (byte[] Loca, byte[] Glyf, int NumGlyphs) TwoComposites()
    {
        var records = new List<byte[]>
        {
            NoContourGlyphRecord(),
            CompositeRecord(0),
            CompositeRecord(0)
        };
        return Assemble(records);
    }

    /// <summary>One glyph holding a single contour of <paramref name="points"/> points, all
    /// ON_CURVE with zero deltas.</summary>
    internal static (byte[] Loca, byte[] Glyf, int NumGlyphs) SingleSimple(int points) =>
        Assemble([ExpandedSimpleGlyph(points)]);

    internal static List<byte> WriteI16(List<byte> body, short value)
    {
        // Reinterpreting the sign bit is intentional: mask rather than narrow, so the checked
        // build does not treat the conversion as an overflow.
        int raw = value & 0xFFFF;
        body.Add((byte)(raw >> 8));
        body.Add((byte)(raw & 0xFF));
        return body;
    }

    internal static List<byte> WriteU16(List<byte> body, ushort value)
    {
        int raw = value;
        body.Add((byte)(raw >> 8));
        body.Add((byte)(raw & 0xFF));
        return body;
    }
}
