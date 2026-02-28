using OTFontFile2.SourceGen;

namespace OTFontFile2.Tables;

[OtTable("glyf", 0, GenerateTryCreate = false)]
public readonly partial struct GlyfTable
{
    public static bool TryCreate(TableSlice table, out GlyfTable glyf)
    {
        glyf = new GlyfTable(table);
        return table.Length != 0;
    }

    public readonly struct GlyphHeader
    {
        public short NumberOfContours { get; }
        public short XMin { get; }
        public short YMin { get; }
        public short XMax { get; }
        public short YMax { get; }

        public GlyphHeader(short numberOfContours, short xMin, short yMin, short xMax, short yMax)
        {
            NumberOfContours = numberOfContours;
            XMin = xMin;
            YMin = yMin;
            XMax = xMax;
            YMax = yMax;
        }

        public bool IsComposite => NumberOfContours < 0;
    }

    public bool TryGetGlyphData(ushort glyphId, LocaTable loca, short indexToLocFormat, ushort numGlyphs, out ReadOnlySpan<byte> glyphData)
    {
        glyphData = default;

        if (!loca.TryGetGlyphOffsetLength(glyphId, indexToLocFormat, numGlyphs, out int offset, out int length))
            return false;

        if (length == 0)
        {
            glyphData = ReadOnlySpan<byte>.Empty;
            return true;
        }

        if ((uint)offset > (uint)_table.Length)
            return false;
        if ((uint)length > (uint)(_table.Length - offset))
            return false;

        glyphData = _table.Span.Slice(offset, length);
        return true;
    }

    public static bool TryReadGlyphHeader(ReadOnlySpan<byte> glyphData, out GlyphHeader header)
    {
        if (glyphData.Length < 10)
        {
            header = default;
            return false;
        }

        header = new GlyphHeader(
            numberOfContours: BigEndian.ReadInt16(glyphData, 0),
            xMin: BigEndian.ReadInt16(glyphData, 2),
            yMin: BigEndian.ReadInt16(glyphData, 4),
            xMax: BigEndian.ReadInt16(glyphData, 6),
            yMax: BigEndian.ReadInt16(glyphData, 8));
        return true;
    }

    public static CompositeComponentEnumerator EnumerateCompositeComponents(ReadOnlySpan<byte> glyphData)
        => new(glyphData);

    public static bool TryGetSimpleGlyphInstructions(ReadOnlySpan<byte> glyphData, out ReadOnlySpan<byte> instructions)
    {
        instructions = default;

        if (!TryReadGlyphHeader(glyphData, out var header))
            return false;

        short contours = header.NumberOfContours;
        if (contours < 0)
            return false;

        ushort contourCount = (ushort)contours;
        int endPtsOffset = 10;
        int endPtsBytes = checked(contourCount * 2);
        if ((uint)endPtsOffset > (uint)glyphData.Length - (uint)endPtsBytes)
            return false;

        int instrLenOffset = checked(endPtsOffset + endPtsBytes);
        if ((uint)instrLenOffset > (uint)glyphData.Length - 2)
            return false;

        ushort instructionLength = BigEndian.ReadUInt16(glyphData, instrLenOffset);
        int instructionsOffset = instrLenOffset + 2;
        if ((uint)instructionsOffset > (uint)glyphData.Length - instructionLength)
            return false;

        instructions = glyphData.Slice(instructionsOffset, instructionLength);
        return true;
    }

    public static bool TryGetCompositeGlyphInstructions(ReadOnlySpan<byte> glyphData, out ReadOnlySpan<byte> instructions)
    {
        instructions = default;

        if (!TryReadGlyphHeader(glyphData, out var header))
            return false;

        if (!header.IsComposite)
            return false;

        int pos = 10;
        bool hasInstructions = false;

        while (true)
        {
            // flags(2) + glyphIndex(2)
            if ((uint)pos > (uint)glyphData.Length - 4)
                return false;

            ushort flags = BigEndian.ReadUInt16(glyphData, pos);
            hasInstructions = (flags & 0x0100) != 0; // WE_HAVE_INSTRUCTIONS (valid only on last component)
            if (hasInstructions && (flags & 0x0020) != 0) // MORE_COMPONENTS
                return false;

            int transformFlags = flags & (0x0008 | 0x0040 | 0x0080);
            if (transformFlags != 0 && (transformFlags & (transformFlags - 1)) != 0)
                return false;

            int argSize = (flags & 0x0001) != 0 ? 4 : 2; // ARG_1_AND_2_ARE_WORDS
            int transformSize = 0;
            if ((flags & 0x0008) != 0) transformSize = 2;      // WE_HAVE_A_SCALE
            else if ((flags & 0x0040) != 0) transformSize = 4; // WE_HAVE_AN_X_AND_Y_SCALE
            else if ((flags & 0x0080) != 0) transformSize = 8; // WE_HAVE_A_TWO_BY_TWO

            int componentSize = 4 + argSize + transformSize;
            if ((uint)pos > (uint)glyphData.Length - componentSize)
                return false;

            pos += componentSize;

            if ((flags & 0x0020) != 0) // MORE_COMPONENTS
                continue;

            break;
        }

        if (!hasInstructions)
        {
            instructions = ReadOnlySpan<byte>.Empty;
            return true;
        }

        if ((uint)pos > (uint)glyphData.Length - 2)
            return false;

        ushort instructionLength = BigEndian.ReadUInt16(glyphData, pos);
        pos += 2;
        if ((uint)pos > (uint)glyphData.Length - instructionLength)
            return false;

        instructions = glyphData.Slice(pos, instructionLength);
        return true;
    }

    public static bool TryCreateSimpleGlyphPointEnumerator(ReadOnlySpan<byte> glyphData, out SimpleGlyphPointEnumerator enumerator)
    {
        enumerator = default;

        if (!TryValidateSimpleGlyphContourLayout(
            glyphData,
            out ushort contourCount,
            out ushort pointCount,
            out int endPtsOffset,
            out int endPtsBytes))
        {
            return false;
        }

        int instrLenOffset = checked(endPtsOffset + endPtsBytes);
        if ((uint)instrLenOffset > (uint)glyphData.Length - 2)
            return false;

        ushort instructionLength = BigEndian.ReadUInt16(glyphData, instrLenOffset);
        int instructionsOffset = instrLenOffset + 2;
        if ((uint)instructionsOffset > (uint)glyphData.Length - instructionLength)
            return false;

        int flagsOffset = checked(instructionsOffset + instructionLength);

        // Empty simple glyph.
        if (pointCount == 0)
        {
            enumerator = new SimpleGlyphPointEnumerator(
                glyphData,
                contourCount,
                pointCount,
                endPtsOffset,
                flagsOffset,
                xOffset: flagsOffset,
                yOffset: flagsOffset);
            return true;
        }

        if (flagsOffset >= glyphData.Length)
            return false;

        if (!TryComputeSimpleGlyphCoordinateLayout(
            glyphData,
            pointCount,
            flagsOffset,
            out int flagsByteLength,
            out int xByteLength,
            out int yByteLength))
        {
            return false;
        }

        int xOffset = checked(flagsOffset + flagsByteLength);
        int yOffset = checked(xOffset + xByteLength);

        int requiredEnd = checked(yOffset + yByteLength);
        if (requiredEnd > glyphData.Length)
            return false;

        enumerator = new SimpleGlyphPointEnumerator(glyphData, contourCount, pointCount, endPtsOffset, flagsOffset, xOffset, yOffset);
        return true;
    }

    public static bool TryCreateSimpleGlyphContourEnumerator(ReadOnlySpan<byte> glyphData, out SimpleGlyphContourEnumerator enumerator)
    {
        enumerator = default;

        if (!TryValidateSimpleGlyphContourLayout(
            glyphData,
            out ushort contourCount,
            out ushort pointCount,
            out int endPtsOffset,
            out _))
        {
            return false;
        }

        enumerator = new SimpleGlyphContourEnumerator(glyphData, contourCount, pointCount, endPtsOffset);
        return true;
    }

    public readonly struct SimpleGlyphContour
    {
        public ushort StartPointIndex { get; }
        public ushort EndPointIndex { get; }

        public SimpleGlyphContour(ushort startPointIndex, ushort endPointIndex)
        {
            StartPointIndex = startPointIndex;
            EndPointIndex = endPointIndex;
        }
    }

    public ref struct SimpleGlyphContourEnumerator
    {
        private ReadOnlySpan<byte> _data;
        private readonly ushort _contourCount;
        private readonly ushort _pointCount;
        private readonly int _endPtsOffset;
        private int _contourIndex;
        private int _prevEnd;

        internal SimpleGlyphContourEnumerator(ReadOnlySpan<byte> glyphData, ushort contourCount, ushort pointCount, int endPtsOffset)
        {
            _data = glyphData;
            _contourCount = contourCount;
            _pointCount = pointCount;
            _endPtsOffset = endPtsOffset;
            _contourIndex = 0;
            _prevEnd = -1;
            Current = default;
        }

        public ushort ContourCount => _contourCount;
        public ushort PointCount => _pointCount;
        public SimpleGlyphContour Current { get; private set; }

        public bool MoveNext()
        {
            if ((uint)_contourIndex >= _contourCount)
                return false;

            ushort endPt = BigEndian.ReadUInt16(_data, _endPtsOffset + (_contourIndex * 2));
            ushort startPt = (ushort)(_prevEnd + 1);

            Current = new SimpleGlyphContour(startPt, endPt);
            _prevEnd = endPt;
            _contourIndex++;
            return true;
        }
    }

    private static bool TryValidateSimpleGlyphContourLayout(
        ReadOnlySpan<byte> glyphData,
        out ushort contourCount,
        out ushort pointCount,
        out int endPtsOffset,
        out int endPtsBytes)
    {
        contourCount = 0;
        pointCount = 0;
        endPtsOffset = 10;
        endPtsBytes = 0;

        if (!TryReadGlyphHeader(glyphData, out var header))
            return false;

        short contours = header.NumberOfContours;
        if (contours < 0)
            return false;

        contourCount = (ushort)contours;
        endPtsBytes = checked(contourCount * 2);
        if ((uint)endPtsOffset > (uint)glyphData.Length - (uint)endPtsBytes)
            return false;

        if (contourCount == 0)
            return true;

        int lastEndPtOffset = checked(endPtsOffset + ((contourCount - 1) * 2));
        ushort lastEndPt = BigEndian.ReadUInt16(glyphData, lastEndPtOffset);
        if (lastEndPt == ushort.MaxValue)
            return false;

        pointCount = (ushort)(lastEndPt + 1);

        // Validate endPtsOfContours monotonicity and ensure no empty contours.
        int prevEnd = -1;
        for (int i = 0; i < contourCount; i++)
        {
            int endPt = BigEndian.ReadUInt16(glyphData, endPtsOffset + (i * 2));
            if (endPt <= prevEnd)
                return false;
            if ((uint)endPt >= pointCount)
                return false;
            prevEnd = endPt;
        }

        return true;
    }

    private static bool TryComputeSimpleGlyphCoordinateLayout(
        ReadOnlySpan<byte> glyphData,
        ushort pointCount,
        int flagsOffset,
        out int flagsByteLength,
        out int xByteLength,
        out int yByteLength)
    {
        flagsByteLength = 0;
        xByteLength = 0;
        yByteLength = 0;

        int pos = flagsOffset;
        int remaining = pointCount;

        while (remaining > 0)
        {
            if ((uint)pos >= (uint)glyphData.Length)
                return false;

            byte flag = glyphData[pos++];

            int run = 1;
            if ((flag & 0x08) != 0)
            {
                if ((uint)pos >= (uint)glyphData.Length)
                    return false;

                run += glyphData[pos++];
            }

            if (run > remaining)
                return false;

            int xLenPerPoint;
            if ((flag & 0x02) != 0) // X_SHORT_VECTOR
                xLenPerPoint = 1;
            else
                xLenPerPoint = (flag & 0x10) != 0 ? 0 : 2; // X_IS_SAME_OR_POSITIVE_X_SHORT_VECTOR

            int yLenPerPoint;
            if ((flag & 0x04) != 0) // Y_SHORT_VECTOR
                yLenPerPoint = 1;
            else
                yLenPerPoint = (flag & 0x20) != 0 ? 0 : 2; // Y_IS_SAME_OR_POSITIVE_Y_SHORT_VECTOR

            xByteLength = checked(xByteLength + (run * xLenPerPoint));
            yByteLength = checked(yByteLength + (run * yLenPerPoint));
            remaining -= run;
        }

        flagsByteLength = pos - flagsOffset;
        return true;
    }

    public readonly struct SimpleGlyphPoint
    {
        public short X { get; }
        public short Y { get; }
        public byte Flags { get; }
        public bool OnCurve { get; }
        public bool IsContourEnd { get; }

        public SimpleGlyphPoint(short x, short y, byte flags, bool onCurve, bool isContourEnd)
        {
            X = x;
            Y = y;
            Flags = flags;
            OnCurve = onCurve;
            IsContourEnd = isContourEnd;
        }
    }

    public ref struct SimpleGlyphPointEnumerator
    {
        private ReadOnlySpan<byte> _data;
        private readonly ushort _contourCount;
        private readonly ushort _pointCount;
        private readonly int _endPtsOffset;
        private readonly int _flagsOffset;
        private readonly int _xOffset;
        private readonly int _yOffset;

        private int _flagsPos;
        private int _xPos;
        private int _yPos;

        private int _repeatRemaining;
        private byte _repeatFlag;

        private int _pointIndex;
        private int _contourIndex;
        private ushort _nextContourEnd;
        private int _x;
        private int _y;
        private bool _invalid;

        internal SimpleGlyphPointEnumerator(ReadOnlySpan<byte> glyphData, ushort contourCount, ushort pointCount, int endPtsOffset, int flagsOffset, int xOffset, int yOffset)
        {
            _data = glyphData;
            _contourCount = contourCount;
            _pointCount = pointCount;
            _endPtsOffset = endPtsOffset;
            _flagsOffset = flagsOffset;
            _xOffset = xOffset;
            _yOffset = yOffset;

            _flagsPos = flagsOffset;
            _xPos = xOffset;
            _yPos = yOffset;

            _repeatRemaining = 0;
            _repeatFlag = 0;

            _pointIndex = 0;
            _contourIndex = 0;
            _nextContourEnd = contourCount == 0 ? (ushort)0 : BigEndian.ReadUInt16(glyphData, endPtsOffset);
            _x = 0;
            _y = 0;
            _invalid = false;

            Current = default;
        }

        public ushort ContourCount => _contourCount;
        public ushort PointCount => _pointCount;
        public ReadOnlySpan<byte> GlyphData => _data;
        public bool IsValid => !_invalid;
        public SimpleGlyphPoint Current { get; private set; }

        public bool MoveNext()
        {
            if ((uint)_pointIndex >= _pointCount)
                return false;

            byte flag = ReadNextFlag();

            int newX = _x + ReadCoordDelta(isX: true, flag);
            int newY = _y + ReadCoordDelta(isX: false, flag);
            if (newX < short.MinValue || newX > short.MaxValue || newY < short.MinValue || newY > short.MaxValue)
            {
                _invalid = true;
                _pointIndex = _pointCount;
                Current = default;
                return false;
            }

            _x = newX;
            _y = newY;

            bool isContourEnd = _contourCount != 0 && _pointIndex == _nextContourEnd;
            if (isContourEnd)
            {
                _contourIndex++;
                if (_contourIndex < _contourCount)
                    _nextContourEnd = BigEndian.ReadUInt16(_data, _endPtsOffset + (_contourIndex * 2));
            }

            Current = new SimpleGlyphPoint(
                x: (short)_x,
                y: (short)_y,
                flags: flag,
                onCurve: (flag & 0x01) != 0,
                isContourEnd: isContourEnd);

            _pointIndex++;
            return true;
        }

        private byte ReadNextFlag()
        {
            if (_repeatRemaining > 0)
            {
                _repeatRemaining--;
                return _repeatFlag;
            }

            byte flag = _data[_flagsPos++];
            if ((flag & 0x08) != 0)
            {
                int repeat = _data[_flagsPos++];
                // repeat=0 means the flag applies only to the current point (no additional repeats).
                _repeatRemaining = repeat;
                _repeatFlag = (byte)(flag & ~0x08);
            }

            return (byte)(flag & ~0x08);
        }

        private int ReadCoordDelta(bool isX, byte flag)
        {
            if (isX)
            {
                bool xShort = (flag & 0x02) != 0;
                bool xSameOrPos = (flag & 0x10) != 0;

                if (xShort)
                {
                    byte b = _data[_xPos++];
                    return xSameOrPos ? b : -b;
                }

                if (xSameOrPos)
                    return 0;

                short v = BigEndian.ReadInt16(_data, _xPos);
                _xPos += 2;
                return v;
            }
            else
            {
                bool yShort = (flag & 0x04) != 0;
                bool ySameOrPos = (flag & 0x20) != 0;

                if (yShort)
                {
                    byte b = _data[_yPos++];
                    return ySameOrPos ? b : -b;
                }

                if (ySameOrPos)
                    return 0;

                short v = BigEndian.ReadInt16(_data, _yPos);
                _yPos += 2;
                return v;
            }
        }
    }

    public readonly struct CompositeComponent
    {
        public ushort Flags { get; }
        public ushort GlyphIndex { get; }

        public CompositeComponent(ushort flags, ushort glyphIndex)
        {
            Flags = flags;
            GlyphIndex = glyphIndex;
        }

        public bool HasMoreComponents => (Flags & 0x0020) != 0; // MORE_COMPONENTS
    }

    public ref struct CompositeComponentEnumerator
    {
        private ReadOnlySpan<byte> _data;
        private int _offset;
        private bool _done;
        private bool _invalid;

        public CompositeComponentEnumerator(ReadOnlySpan<byte> glyphData)
        {
            _data = glyphData;
            _offset = 10; // glyph header
            _done = false;
            _invalid = false;
            Current = default;
        }

        public CompositeComponent Current { get; private set; }
        public bool IsValid => !_invalid;

        public bool MoveNext()
        {
            if (_done)
                return false;

            // flags(2) + glyphIndex(2)
            if ((uint)_offset > (uint)_data.Length - 4)
            {
                _done = true;
                return false;
            }

            ushort flags = BigEndian.ReadUInt16(_data, _offset);
            ushort glyphIndex = BigEndian.ReadUInt16(_data, _offset + 2);

            if ((flags & 0x0100) != 0 && (flags & 0x0020) != 0) // WE_HAVE_INSTRUCTIONS + MORE_COMPONENTS
            {
                _invalid = true;
                _done = true;
                return false;
            }

            int transformFlags = flags & (0x0008 | 0x0040 | 0x0080);
            if (transformFlags != 0 && (transformFlags & (transformFlags - 1)) != 0)
            {
                _invalid = true;
                _done = true;
                return false;
            }

            int argSize = (flags & 0x0001) != 0 ? 4 : 2; // ARG_1_AND_2_ARE_WORDS
            int transformSize = 0;
            if ((flags & 0x0008) != 0) transformSize = 2;      // WE_HAVE_A_SCALE
            else if ((flags & 0x0040) != 0) transformSize = 4; // WE_HAVE_AN_X_AND_Y_SCALE
            else if ((flags & 0x0080) != 0) transformSize = 8; // WE_HAVE_A_TWO_BY_TWO

            int componentSize = 4 + argSize + transformSize;
            if ((uint)_offset > (uint)_data.Length - componentSize)
            {
                _invalid = true;
                _done = true;
                return false;
            }

            Current = new CompositeComponent(flags, glyphIndex);
            _offset += componentSize;

            if ((flags & 0x0020) == 0) // MORE_COMPONENTS
                _done = true;

            return true;
        }
    }

    public static bool TryCreateCompositeGlyphComponentEnumerator(ReadOnlySpan<byte> glyphData, out CompositeGlyphComponentEnumerator enumerator)
    {
        enumerator = default;

        if (!TryReadGlyphHeader(glyphData, out var header))
            return false;

        if (!header.IsComposite)
            return false;

        if (glyphData.Length < 10)
            return false;

        enumerator = new CompositeGlyphComponentEnumerator(glyphData);
        return true;
    }

    public readonly struct CompositeGlyphComponent
    {
        public ushort Flags { get; }
        public ushort GlyphIndex { get; }

        public bool HasMoreComponents => (Flags & 0x0020) != 0; // MORE_COMPONENTS
        public bool HasInstructions => (Flags & 0x0100) != 0;   // WE_HAVE_INSTRUCTIONS

        public bool ArgsAreWords => (Flags & 0x0001) != 0;      // ARG_1_AND_2_ARE_WORDS
        public bool ArgsAreXYValues => (Flags & 0x0002) != 0;   // ARGS_ARE_XY_VALUES

        public bool HasScale => (Flags & 0x0008) != 0;          // WE_HAVE_A_SCALE
        public bool HasXYScale => (Flags & 0x0040) != 0;        // WE_HAVE_AN_X_AND_Y_SCALE
        public bool Has2x2 => (Flags & 0x0080) != 0;            // WE_HAVE_A_TWO_BY_TWO

        private readonly short _dx;
        private readonly short _dy;
        private readonly ushort _parentPoint;
        private readonly ushort _childPoint;

        public F2Dot14 A { get; }
        public F2Dot14 B { get; }
        public F2Dot14 C { get; }
        public F2Dot14 D { get; }

        internal CompositeGlyphComponent(
            ushort flags,
            ushort glyphIndex,
            short dx,
            short dy,
            ushort parentPoint,
            ushort childPoint,
            F2Dot14 a,
            F2Dot14 b,
            F2Dot14 c,
            F2Dot14 d)
        {
            Flags = flags;
            GlyphIndex = glyphIndex;
            _dx = dx;
            _dy = dy;
            _parentPoint = parentPoint;
            _childPoint = childPoint;
            A = a;
            B = b;
            C = c;
            D = d;
        }

        public bool TryGetTranslation(out short dx, out short dy)
        {
            dx = 0;
            dy = 0;
            if (!ArgsAreXYValues)
                return false;

            dx = _dx;
            dy = _dy;
            return true;
        }

        public bool TryGetMatchingPoints(out ushort parentPoint, out ushort childPoint)
        {
            parentPoint = 0;
            childPoint = 0;
            if (ArgsAreXYValues)
                return false;

            parentPoint = _parentPoint;
            childPoint = _childPoint;
            return true;
        }
    }

    public ref struct CompositeGlyphComponentEnumerator
    {
        private ReadOnlySpan<byte> _data;
        private int _offset;
        private bool _done;
        private bool _invalid;

        public CompositeGlyphComponentEnumerator(ReadOnlySpan<byte> glyphData)
        {
            _data = glyphData;
            _offset = 10; // glyph header
            _done = false;
            _invalid = false;
            Current = default;
        }

        public CompositeGlyphComponent Current { get; private set; }
        public bool IsValid => !_invalid;

        public bool MoveNext()
        {
            if (_done)
                return false;

            if ((uint)_offset > (uint)_data.Length - 4)
            {
                _invalid = true;
                _done = true;
                return false;
            }

            ushort flags = BigEndian.ReadUInt16(_data, _offset);
            ushort glyphIndex = BigEndian.ReadUInt16(_data, _offset + 2);
            int pos = _offset + 4;

            if ((flags & 0x0100) != 0 && (flags & 0x0020) != 0) // WE_HAVE_INSTRUCTIONS + MORE_COMPONENTS
            {
                _invalid = true;
                _done = true;
                return false;
            }

            int transformFlags = flags & (0x0008 | 0x0040 | 0x0080);
            if (transformFlags != 0 && (transformFlags & (transformFlags - 1)) != 0)
            {
                _invalid = true;
                _done = true;
                return false;
            }

            bool argsAreWords = (flags & 0x0001) != 0;
            bool argsAreXYValues = (flags & 0x0002) != 0;

            short dx = 0;
            short dy = 0;
            ushort parentPoint = 0;
            ushort childPoint = 0;

            if (argsAreWords)
            {
                if ((uint)pos > (uint)_data.Length - 4)
                {
                    _invalid = true;
                    _done = true;
                    return false;
                }

                ushort a1 = BigEndian.ReadUInt16(_data, pos);
                ushort a2 = BigEndian.ReadUInt16(_data, pos + 2);
                pos += 4;

                if (argsAreXYValues)
                {
                    dx = unchecked((short)a1);
                    dy = unchecked((short)a2);
                }
                else
                {
                    parentPoint = a1;
                    childPoint = a2;
                }
            }
            else
            {
                if ((uint)pos > (uint)_data.Length - 2)
                {
                    _invalid = true;
                    _done = true;
                    return false;
                }

                byte b1 = _data[pos];
                byte b2 = _data[pos + 1];
                pos += 2;

                if (argsAreXYValues)
                {
                    dx = unchecked((sbyte)b1);
                    dy = unchecked((sbyte)b2);
                }
                else
                {
                    parentPoint = b1;
                    childPoint = b2;
                }
            }

            F2Dot14 a = new(0x4000); // 1.0
            F2Dot14 b = default;
            F2Dot14 c = default;
            F2Dot14 d = new(0x4000); // 1.0

            if ((flags & 0x0008) != 0) // WE_HAVE_A_SCALE
            {
                if ((uint)pos > (uint)_data.Length - 2)
                {
                    _invalid = true;
                    _done = true;
                    return false;
                }

                short scale = BigEndian.ReadInt16(_data, pos);
                pos += 2;
                a = new F2Dot14(scale);
                d = new F2Dot14(scale);
            }
            else if ((flags & 0x0040) != 0) // WE_HAVE_AN_X_AND_Y_SCALE
            {
                if ((uint)pos > (uint)_data.Length - 4)
                {
                    _invalid = true;
                    _done = true;
                    return false;
                }

                a = new F2Dot14(BigEndian.ReadInt16(_data, pos));
                d = new F2Dot14(BigEndian.ReadInt16(_data, pos + 2));
                pos += 4;
            }
            else if ((flags & 0x0080) != 0) // WE_HAVE_A_TWO_BY_TWO
            {
                if ((uint)pos > (uint)_data.Length - 8)
                {
                    _invalid = true;
                    _done = true;
                    return false;
                }

                a = new F2Dot14(BigEndian.ReadInt16(_data, pos));
                b = new F2Dot14(BigEndian.ReadInt16(_data, pos + 2));
                c = new F2Dot14(BigEndian.ReadInt16(_data, pos + 4));
                d = new F2Dot14(BigEndian.ReadInt16(_data, pos + 6));
                pos += 8;
            }

            Current = new CompositeGlyphComponent(flags, glyphIndex, dx, dy, parentPoint, childPoint, a, b, c, d);
            _offset = pos;

            if ((flags & 0x0020) == 0) // MORE_COMPONENTS
                _done = true;

            return true;
        }
    }
}
