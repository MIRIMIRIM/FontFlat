using System.Buffers;
using OTFontFile2.Tables;

namespace OTFontFile2.Tables.Glyf;

public sealed class GlyfSimpleGlyphBuilder
{
    private ushort[] _endPts = Array.Empty<ushort>();
    private GlyfGlyphPoint[] _points = Array.Empty<GlyfGlyphPoint>();
    private byte[] _instructions = Array.Empty<byte>();

    public ReadOnlySpan<ushort> EndPointsOfContours => _endPts;
    public ReadOnlySpan<GlyfGlyphPoint> Points => _points;
    public ReadOnlySpan<byte> Instructions => _instructions;

    public void SetContours(ReadOnlySpan<ushort> endPointsOfContours, ReadOnlySpan<GlyfGlyphPoint> points)
    {
        if (endPointsOfContours.Length > short.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(endPointsOfContours));
        if (points.Length > ushort.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(points));

        ValidateEndPts(endPointsOfContours, points.Length);

        _endPts = endPointsOfContours.ToArray();
        _points = points.ToArray();
    }

    public void SetInstructions(ReadOnlySpan<byte> instructions)
    {
        if (instructions.Length > ushort.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(instructions));

        _instructions = instructions.ToArray();
    }

    public static bool TryFrom(ReadOnlySpan<byte> glyphData, out GlyfSimpleGlyphBuilder builder)
    {
        builder = null!;

        if (!GlyfTable.TryReadGlyphHeader(glyphData, out var header))
            return false;

        if (header.IsComposite)
            return false;

        if (!GlyfTable.TryCreateSimpleGlyphContourEnumerator(glyphData, out var contoursEnum))
            return false;

        ushort contourCount = contoursEnum.ContourCount;
        ushort pointCount = contoursEnum.PointCount;

        if (!GlyfTable.TryGetSimpleGlyphInstructions(glyphData, out var instr))
            return false;

        var endPts = new ushort[contourCount];
        for (int i = 0; i < contourCount; i++)
        {
            if (!contoursEnum.MoveNext())
                return false;
            endPts[i] = contoursEnum.Current.EndPointIndex;
        }

        if (contoursEnum.MoveNext())
            return false;

        if (!GlyfTable.TryCreateSimpleGlyphPointEnumerator(glyphData, out var pointsEnum))
            return false;

        var points = new GlyfGlyphPoint[pointCount];
        for (int i = 0; i < pointCount; i++)
        {
            if (!pointsEnum.MoveNext())
                return false;
            points[i] = new GlyfGlyphPoint(pointsEnum.Current.X, pointsEnum.Current.Y, pointsEnum.Current.OnCurve);
        }

        if (pointsEnum.MoveNext())
            return false;
        if (!pointsEnum.IsValid)
            return false;

        var b = new GlyfSimpleGlyphBuilder();
        b.SetContours(endPts, points);
        b.SetInstructions(instr);
        builder = b;
        return true;
    }

    public byte[] Build()
    {
        ValidateEndPts(_endPts, _points.Length);

        short numberOfContours = checked((short)_endPts.Length);
        short xMin = 0;
        short yMin = 0;
        short xMax = 0;
        short yMax = 0;

        if (_points.Length != 0)
        {
            xMin = xMax = _points[0].X;
            yMin = yMax = _points[0].Y;
            for (int i = 1; i < _points.Length; i++)
            {
                var pt = _points[i];
                if (pt.X < xMin) xMin = pt.X;
                if (pt.X > xMax) xMax = pt.X;
                if (pt.Y < yMin) yMin = pt.Y;
                if (pt.Y > yMax) yMax = pt.Y;
            }
        }

        var flags = new ArrayBufferWriter<byte>(_points.Length);
        var x = new ArrayBufferWriter<byte>(_points.Length * 2);
        var y = new ArrayBufferWriter<byte>(_points.Length * 2);

        int prevX = 0;
        int prevY = 0;

        byte runFlag = 0;
        int runRepeat = 0; // count of additional repeats
        bool hasRun = false;

        for (int i = 0; i < _points.Length; i++)
        {
            var pt = _points[i];
            int dx = pt.X - prevX;
            int dy = pt.Y - prevY;

            byte f = 0;
            if (pt.OnCurve) f |= 0x01;

            f |= EncodeDelta(dx, shortBit: 0x02, sameBit: 0x10, x);
            f |= EncodeDelta(dy, shortBit: 0x04, sameBit: 0x20, y);

            prevX = pt.X;
            prevY = pt.Y;

            if (!hasRun)
            {
                runFlag = f;
                runRepeat = 0;
                hasRun = true;
                continue;
            }

            if (f == runFlag && runRepeat < 255)
            {
                runRepeat++;
                continue;
            }

            FlushFlagRun(flags, runFlag, runRepeat);
            runFlag = f;
            runRepeat = 0;
        }

        if (hasRun)
            FlushFlagRun(flags, runFlag, runRepeat);

        int headerBytes = 10;
        int endPtsBytes = _endPts.Length * 2;
        int instructionsBytes = _instructions.Length;

        int length = checked(headerBytes + endPtsBytes + 2 + instructionsBytes + flags.WrittenCount + x.WrittenCount + y.WrittenCount);
        byte[] data = new byte[length];
        var span = data.AsSpan();

        BigEndian.WriteInt16(span, 0, numberOfContours);
        BigEndian.WriteInt16(span, 2, xMin);
        BigEndian.WriteInt16(span, 4, yMin);
        BigEndian.WriteInt16(span, 6, xMax);
        BigEndian.WriteInt16(span, 8, yMax);

        int p = 10;
        for (int i = 0; i < _endPts.Length; i++)
        {
            BigEndian.WriteUInt16(span, p, _endPts[i]);
            p += 2;
        }

        BigEndian.WriteUInt16(span, p, (ushort)_instructions.Length);
        p += 2;

        _instructions.AsSpan().CopyTo(span.Slice(p, _instructions.Length));
        p += _instructions.Length;

        flags.WrittenSpan.CopyTo(span.Slice(p, flags.WrittenCount));
        p += flags.WrittenCount;

        x.WrittenSpan.CopyTo(span.Slice(p, x.WrittenCount));
        p += x.WrittenCount;

        y.WrittenSpan.CopyTo(span.Slice(p, y.WrittenCount));
        p += y.WrittenCount;

        return data;
    }

    private static void ValidateEndPts(ReadOnlySpan<ushort> endPtsOfContours, int pointCount)
    {
        if (endPtsOfContours.Length == 0)
        {
            if (pointCount != 0)
                throw new ArgumentException("Simple glyph with 0 contours must have 0 points.", nameof(pointCount));
            return;
        }

        if (pointCount == 0)
            throw new ArgumentException("Simple glyph with contours must have points.", nameof(pointCount));

        int prev = -1;
        for (int i = 0; i < endPtsOfContours.Length; i++)
        {
            int endPt = endPtsOfContours[i];
            if (endPt <= prev)
                throw new ArgumentException("endPtsOfContours must be strictly increasing.", nameof(endPtsOfContours));
            prev = endPt;
        }

        if (endPtsOfContours[^1] != pointCount - 1)
            throw new ArgumentException("Last endPt must equal points.Length-1.", nameof(endPtsOfContours));
    }

    private static void FlushFlagRun(ArrayBufferWriter<byte> flags, byte flag, int repeat)
    {
        if (repeat == 0)
        {
            flags.GetSpan(1)[0] = flag;
            flags.Advance(1);
            return;
        }

        Span<byte> s = flags.GetSpan(2);
        s[0] = (byte)(flag | 0x08);
        s[1] = (byte)repeat;
        flags.Advance(2);
    }

    private static byte EncodeDelta(int delta, byte shortBit, byte sameBit, ArrayBufferWriter<byte> coords)
    {
        if (delta == 0)
            return sameBit;

        if (delta >= -255 && delta <= 255)
        {
            byte abs = (byte)(delta < 0 ? -delta : delta);
            coords.GetSpan(1)[0] = abs;
            coords.Advance(1);
            return (byte)(shortBit | (delta > 0 ? sameBit : 0));
        }

        Span<byte> s = coords.GetSpan(2);
        BigEndian.WriteInt16(s, 0, checked((short)delta));
        coords.Advance(2);
        return 0;
    }
}
