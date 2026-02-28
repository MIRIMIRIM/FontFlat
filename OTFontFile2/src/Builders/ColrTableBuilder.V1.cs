namespace OTFontFile2.Tables;

public sealed partial class ColrTableBuilder
{
    public readonly struct ColorStopV1
    {
        public F2Dot14 StopOffset { get; }
        public ushort PaletteIndex { get; }
        public F2Dot14 Alpha { get; }

        public ColorStopV1(F2Dot14 stopOffset, ushort paletteIndex, F2Dot14 alpha)
        {
            StopOffset = stopOffset;
            PaletteIndex = paletteIndex;
            Alpha = alpha;
        }
    }

    public readonly struct VarColorStopV1
    {
        public F2Dot14 StopOffset { get; }
        public ushort PaletteIndex { get; }
        public F2Dot14 Alpha { get; }
        public uint VarIndexBase { get; }

        public VarColorStopV1(F2Dot14 stopOffset, ushort paletteIndex, F2Dot14 alpha, uint varIndexBase)
        {
            StopOffset = stopOffset;
            PaletteIndex = paletteIndex;
            Alpha = alpha;
            VarIndexBase = varIndexBase;
        }
    }

    public readonly struct ClipBoxV1
    {
        public short XMin { get; }
        public short YMin { get; }
        public short XMax { get; }
        public short YMax { get; }

        public ClipBoxV1(short xMin, short yMin, short xMax, short yMax)
        {
            XMin = xMin;
            YMin = yMin;
            XMax = xMax;
            YMax = yMax;
        }
    }

    public abstract class PaintV1
    {
        internal abstract byte Format { get; }
        internal abstract int ByteLength { get; }
        internal virtual int ChildCount => 0;
        internal virtual PaintV1 GetChild(int index) => throw new ArgumentOutOfRangeException(nameof(index));
        internal abstract void Write(Span<byte> destination, int selfAbsOffset, Dictionary<PaintV1, int> absOffsets);
    }

    public sealed class PaintVarSolidV1 : PaintV1
    {
        public ushort PaletteIndex { get; }
        public F2Dot14 Alpha { get; }
        public uint VarIndexBase { get; }

        public PaintVarSolidV1(ushort paletteIndex, F2Dot14 alpha, uint varIndexBase)
        {
            PaletteIndex = paletteIndex;
            Alpha = alpha;
            VarIndexBase = varIndexBase;
        }

        internal override byte Format => 3;
        internal override int ByteLength => 9;

        internal override void Write(Span<byte> destination, int selfAbsOffset, Dictionary<PaintV1, int> absOffsets)
        {
            destination[0] = 3;
            BigEndian.WriteUInt16(destination, 1, PaletteIndex);
            BigEndian.WriteInt16(destination, 3, Alpha.RawValue);
            BigEndian.WriteUInt32(destination, 5, VarIndexBase);
        }
    }

    public sealed class PaintSolidV1 : PaintV1
    {
        public ushort PaletteIndex { get; }
        public F2Dot14 Alpha { get; }

        public PaintSolidV1(ushort paletteIndex, F2Dot14 alpha)
        {
            PaletteIndex = paletteIndex;
            Alpha = alpha;
        }

        internal override byte Format => 2;
        internal override int ByteLength => 5;

        internal override void Write(Span<byte> destination, int selfAbsOffset, Dictionary<PaintV1, int> absOffsets)
        {
            destination[0] = 2;
            BigEndian.WriteUInt16(destination, 1, PaletteIndex);
            BigEndian.WriteInt16(destination, 3, Alpha.RawValue);
        }
    }

    public sealed class PaintColrGlyphV1 : PaintV1
    {
        public ushort GlyphId { get; }

        public PaintColrGlyphV1(ushort glyphId) => GlyphId = glyphId;

        internal override byte Format => 11;
        internal override int ByteLength => 3;

        internal override void Write(Span<byte> destination, int selfAbsOffset, Dictionary<PaintV1, int> absOffsets)
        {
            destination[0] = 11;
            BigEndian.WriteUInt16(destination, 1, GlyphId);
        }
    }

    public sealed class PaintGlyphV1 : PaintV1
    {
        public PaintV1 Paint { get; }
        public ushort GlyphId { get; }

        public PaintGlyphV1(PaintV1 paint, ushort glyphId)
        {
            Paint = paint ?? throw new ArgumentNullException(nameof(paint));
            GlyphId = glyphId;
        }

        internal override byte Format => 10;
        internal override int ByteLength => 6;
        internal override int ChildCount => 1;
        internal override PaintV1 GetChild(int index) => index == 0 ? Paint : throw new ArgumentOutOfRangeException(nameof(index));

        internal override void Write(Span<byte> destination, int selfAbsOffset, Dictionary<PaintV1, int> absOffsets)
        {
            destination[0] = 10;

            if (!absOffsets.TryGetValue(Paint, out int childAbs))
                throw new InvalidOperationException("Missing child paint offset.");

            int rel = checked(childAbs - selfAbsOffset);
            if ((uint)rel > 0x00FFFFFFu)
                throw new InvalidOperationException("PaintGlyph paintOffset must fit in Offset24.");

            destination[1] = (byte)(rel >> 16);
            destination[2] = (byte)(rel >> 8);
            destination[3] = (byte)rel;
            BigEndian.WriteUInt16(destination, 4, GlyphId);
        }
    }

    public sealed class PaintColrLayersV1 : PaintV1
    {
        private readonly PaintV1[] _layers;

        internal uint FirstLayerIndex { get; set; }

        public PaintColrLayersV1(PaintV1[] layers)
        {
            _layers = layers ?? throw new ArgumentNullException(nameof(layers));
            if (layers.Length > byte.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(layers), "PaintColrLayers numLayers must fit in uint8.");
        }

        public ReadOnlySpan<PaintV1> Layers => _layers;

        internal override byte Format => 1;
        internal override int ByteLength => 6;

        internal override void Write(Span<byte> destination, int selfAbsOffset, Dictionary<PaintV1, int> absOffsets)
        {
            destination[0] = 1;
            destination[1] = (byte)_layers.Length;
            BigEndian.WriteUInt32(destination, 2, FirstLayerIndex);
        }
    }

    public sealed class PaintLinearGradientV1 : PaintV1
    {
        private readonly ColorStopV1[] _stops;

        public byte Extend { get; }
        public short X0 { get; }
        public short Y0 { get; }
        public short X1 { get; }
        public short Y1 { get; }
        public short X2 { get; }
        public short Y2 { get; }

        public PaintLinearGradientV1(byte extend, short x0, short y0, short x1, short y1, short x2, short y2, ColorStopV1[] stops)
        {
            Extend = extend;
            X0 = x0;
            Y0 = y0;
            X1 = x1;
            Y1 = y1;
            X2 = x2;
            Y2 = y2;
            _stops = stops ?? throw new ArgumentNullException(nameof(stops));
            if (_stops.Length > ushort.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(stops));
        }

        internal override byte Format => 4;
        internal override int ByteLength => checked(16 + 3 + (_stops.Length * 6));

        internal override void Write(Span<byte> destination, int selfAbsOffset, Dictionary<PaintV1, int> absOffsets)
        {
            destination[0] = 4;
            // ColorLine immediately follows the fixed header (offset 16).
            destination[1] = 0;
            destination[2] = 0;
            destination[3] = 16;

            BigEndian.WriteInt16(destination, 4, X0);
            BigEndian.WriteInt16(destination, 6, Y0);
            BigEndian.WriteInt16(destination, 8, X1);
            BigEndian.WriteInt16(destination, 10, Y1);
            BigEndian.WriteInt16(destination, 12, X2);
            BigEndian.WriteInt16(destination, 14, Y2);

            int p = 16;
            destination[p + 0] = Extend;
            BigEndian.WriteUInt16(destination, p + 1, checked((ushort)_stops.Length));
            p += 3;

            for (int i = 0; i < _stops.Length; i++)
            {
                var s = _stops[i];
                BigEndian.WriteInt16(destination, p + 0, s.StopOffset.RawValue);
                BigEndian.WriteUInt16(destination, p + 2, s.PaletteIndex);
                BigEndian.WriteInt16(destination, p + 4, s.Alpha.RawValue);
                p += 6;
            }
        }
    }

    public sealed class PaintRadialGradientV1 : PaintV1
    {
        private readonly ColorStopV1[] _stops;

        public byte Extend { get; }
        public short X0 { get; }
        public short Y0 { get; }
        public ushort Radius0 { get; }
        public short X1 { get; }
        public short Y1 { get; }
        public ushort Radius1 { get; }

        public PaintRadialGradientV1(byte extend, short x0, short y0, ushort radius0, short x1, short y1, ushort radius1, ColorStopV1[] stops)
        {
            Extend = extend;
            X0 = x0;
            Y0 = y0;
            Radius0 = radius0;
            X1 = x1;
            Y1 = y1;
            Radius1 = radius1;
            _stops = stops ?? throw new ArgumentNullException(nameof(stops));
            if (_stops.Length > ushort.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(stops));
        }

        internal override byte Format => 6;
        internal override int ByteLength => checked(16 + 3 + (_stops.Length * 6));

        internal override void Write(Span<byte> destination, int selfAbsOffset, Dictionary<PaintV1, int> absOffsets)
        {
            destination[0] = 6;
            destination[1] = 0;
            destination[2] = 0;
            destination[3] = 16;

            BigEndian.WriteInt16(destination, 4, X0);
            BigEndian.WriteInt16(destination, 6, Y0);
            BigEndian.WriteUInt16(destination, 8, Radius0);
            BigEndian.WriteInt16(destination, 10, X1);
            BigEndian.WriteInt16(destination, 12, Y1);
            BigEndian.WriteUInt16(destination, 14, Radius1);

            int p = 16;
            destination[p + 0] = Extend;
            BigEndian.WriteUInt16(destination, p + 1, checked((ushort)_stops.Length));
            p += 3;

            for (int i = 0; i < _stops.Length; i++)
            {
                var s = _stops[i];
                BigEndian.WriteInt16(destination, p + 0, s.StopOffset.RawValue);
                BigEndian.WriteUInt16(destination, p + 2, s.PaletteIndex);
                BigEndian.WriteInt16(destination, p + 4, s.Alpha.RawValue);
                p += 6;
            }
        }
    }

    public sealed class PaintSweepGradientV1 : PaintV1
    {
        private readonly ColorStopV1[] _stops;

        public byte Extend { get; }
        public short CenterX { get; }
        public short CenterY { get; }
        public F2Dot14 StartAngle { get; }
        public F2Dot14 EndAngle { get; }

        public PaintSweepGradientV1(byte extend, short centerX, short centerY, F2Dot14 startAngle, F2Dot14 endAngle, ColorStopV1[] stops)
        {
            Extend = extend;
            CenterX = centerX;
            CenterY = centerY;
            StartAngle = startAngle;
            EndAngle = endAngle;
            _stops = stops ?? throw new ArgumentNullException(nameof(stops));
            if (_stops.Length > ushort.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(stops));
        }

        internal override byte Format => 8;
        internal override int ByteLength => checked(12 + 3 + (_stops.Length * 6));

        internal override void Write(Span<byte> destination, int selfAbsOffset, Dictionary<PaintV1, int> absOffsets)
        {
            destination[0] = 8;
            destination[1] = 0;
            destination[2] = 0;
            destination[3] = 12;

            BigEndian.WriteInt16(destination, 4, CenterX);
            BigEndian.WriteInt16(destination, 6, CenterY);
            BigEndian.WriteInt16(destination, 8, StartAngle.RawValue);
            BigEndian.WriteInt16(destination, 10, EndAngle.RawValue);

            int p = 12;
            destination[p + 0] = Extend;
            BigEndian.WriteUInt16(destination, p + 1, checked((ushort)_stops.Length));
            p += 3;

            for (int i = 0; i < _stops.Length; i++)
            {
                var s = _stops[i];
                BigEndian.WriteInt16(destination, p + 0, s.StopOffset.RawValue);
                BigEndian.WriteUInt16(destination, p + 2, s.PaletteIndex);
                BigEndian.WriteInt16(destination, p + 4, s.Alpha.RawValue);
                p += 6;
            }
        }
    }

    public sealed class PaintVarLinearGradientV1 : PaintV1
    {
        private readonly VarColorStopV1[] _stops;

        public byte Extend { get; }
        public short X0 { get; }
        public short Y0 { get; }
        public short X1 { get; }
        public short Y1 { get; }
        public short X2 { get; }
        public short Y2 { get; }
        public uint VarIndexBase { get; }

        public PaintVarLinearGradientV1(byte extend, short x0, short y0, short x1, short y1, short x2, short y2, uint varIndexBase, VarColorStopV1[] stops)
        {
            Extend = extend;
            X0 = x0;
            Y0 = y0;
            X1 = x1;
            Y1 = y1;
            X2 = x2;
            Y2 = y2;
            VarIndexBase = varIndexBase;
            _stops = stops ?? throw new ArgumentNullException(nameof(stops));
            if (_stops.Length > ushort.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(stops));
        }

        internal override byte Format => 5;
        internal override int ByteLength => checked(20 + 3 + (_stops.Length * 10));

        internal override void Write(Span<byte> destination, int selfAbsOffset, Dictionary<PaintV1, int> absOffsets)
        {
            destination[0] = 5;
            // VarColorLine immediately follows the fixed header (offset 20).
            destination[1] = 0;
            destination[2] = 0;
            destination[3] = 20;

            BigEndian.WriteInt16(destination, 4, X0);
            BigEndian.WriteInt16(destination, 6, Y0);
            BigEndian.WriteInt16(destination, 8, X1);
            BigEndian.WriteInt16(destination, 10, Y1);
            BigEndian.WriteInt16(destination, 12, X2);
            BigEndian.WriteInt16(destination, 14, Y2);
            BigEndian.WriteUInt32(destination, 16, VarIndexBase);

            int p = 20;
            destination[p + 0] = Extend;
            BigEndian.WriteUInt16(destination, p + 1, checked((ushort)_stops.Length));
            p += 3;

            for (int i = 0; i < _stops.Length; i++)
            {
                var s = _stops[i];
                BigEndian.WriteInt16(destination, p + 0, s.StopOffset.RawValue);
                BigEndian.WriteUInt16(destination, p + 2, s.PaletteIndex);
                BigEndian.WriteInt16(destination, p + 4, s.Alpha.RawValue);
                BigEndian.WriteUInt32(destination, p + 6, s.VarIndexBase);
                p += 10;
            }
        }
    }

    public sealed class PaintVarRadialGradientV1 : PaintV1
    {
        private readonly VarColorStopV1[] _stops;

        public byte Extend { get; }
        public short X0 { get; }
        public short Y0 { get; }
        public ushort Radius0 { get; }
        public short X1 { get; }
        public short Y1 { get; }
        public ushort Radius1 { get; }
        public uint VarIndexBase { get; }

        public PaintVarRadialGradientV1(byte extend, short x0, short y0, ushort radius0, short x1, short y1, ushort radius1, uint varIndexBase, VarColorStopV1[] stops)
        {
            Extend = extend;
            X0 = x0;
            Y0 = y0;
            Radius0 = radius0;
            X1 = x1;
            Y1 = y1;
            Radius1 = radius1;
            VarIndexBase = varIndexBase;
            _stops = stops ?? throw new ArgumentNullException(nameof(stops));
            if (_stops.Length > ushort.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(stops));
        }

        internal override byte Format => 7;
        internal override int ByteLength => checked(20 + 3 + (_stops.Length * 10));

        internal override void Write(Span<byte> destination, int selfAbsOffset, Dictionary<PaintV1, int> absOffsets)
        {
            destination[0] = 7;
            destination[1] = 0;
            destination[2] = 0;
            destination[3] = 20;

            BigEndian.WriteInt16(destination, 4, X0);
            BigEndian.WriteInt16(destination, 6, Y0);
            BigEndian.WriteUInt16(destination, 8, Radius0);
            BigEndian.WriteInt16(destination, 10, X1);
            BigEndian.WriteInt16(destination, 12, Y1);
            BigEndian.WriteUInt16(destination, 14, Radius1);
            BigEndian.WriteUInt32(destination, 16, VarIndexBase);

            int p = 20;
            destination[p + 0] = Extend;
            BigEndian.WriteUInt16(destination, p + 1, checked((ushort)_stops.Length));
            p += 3;

            for (int i = 0; i < _stops.Length; i++)
            {
                var s = _stops[i];
                BigEndian.WriteInt16(destination, p + 0, s.StopOffset.RawValue);
                BigEndian.WriteUInt16(destination, p + 2, s.PaletteIndex);
                BigEndian.WriteInt16(destination, p + 4, s.Alpha.RawValue);
                BigEndian.WriteUInt32(destination, p + 6, s.VarIndexBase);
                p += 10;
            }
        }
    }

    public sealed class PaintVarSweepGradientV1 : PaintV1
    {
        private readonly VarColorStopV1[] _stops;

        public byte Extend { get; }
        public short CenterX { get; }
        public short CenterY { get; }
        public F2Dot14 StartAngle { get; }
        public F2Dot14 EndAngle { get; }
        public uint VarIndexBase { get; }

        public PaintVarSweepGradientV1(byte extend, short centerX, short centerY, F2Dot14 startAngle, F2Dot14 endAngle, uint varIndexBase, VarColorStopV1[] stops)
        {
            Extend = extend;
            CenterX = centerX;
            CenterY = centerY;
            StartAngle = startAngle;
            EndAngle = endAngle;
            VarIndexBase = varIndexBase;
            _stops = stops ?? throw new ArgumentNullException(nameof(stops));
            if (_stops.Length > ushort.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(stops));
        }

        internal override byte Format => 9;
        internal override int ByteLength => checked(16 + 3 + (_stops.Length * 10));

        internal override void Write(Span<byte> destination, int selfAbsOffset, Dictionary<PaintV1, int> absOffsets)
        {
            destination[0] = 9;
            destination[1] = 0;
            destination[2] = 0;
            destination[3] = 16;

            BigEndian.WriteInt16(destination, 4, CenterX);
            BigEndian.WriteInt16(destination, 6, CenterY);
            BigEndian.WriteInt16(destination, 8, StartAngle.RawValue);
            BigEndian.WriteInt16(destination, 10, EndAngle.RawValue);
            BigEndian.WriteUInt32(destination, 12, VarIndexBase);

            int p = 16;
            destination[p + 0] = Extend;
            BigEndian.WriteUInt16(destination, p + 1, checked((ushort)_stops.Length));
            p += 3;

            for (int i = 0; i < _stops.Length; i++)
            {
                var s = _stops[i];
                BigEndian.WriteInt16(destination, p + 0, s.StopOffset.RawValue);
                BigEndian.WriteUInt16(destination, p + 2, s.PaletteIndex);
                BigEndian.WriteInt16(destination, p + 4, s.Alpha.RawValue);
                BigEndian.WriteUInt32(destination, p + 6, s.VarIndexBase);
                p += 10;
            }
        }
    }

    public sealed class PaintTransformV1 : PaintV1
    {
        public PaintV1 Paint { get; }
        public Affine2x3 Transform { get; }

        public PaintTransformV1(PaintV1 paint, Affine2x3 transform)
        {
            Paint = paint ?? throw new ArgumentNullException(nameof(paint));
            Transform = transform;
        }

        internal override byte Format => 12;
        internal override int ByteLength => 31;
        internal override int ChildCount => 1;
        internal override PaintV1 GetChild(int index) => index == 0 ? Paint : throw new ArgumentOutOfRangeException(nameof(index));

        internal override void Write(Span<byte> destination, int selfAbsOffset, Dictionary<PaintV1, int> absOffsets)
        {
            destination[0] = 12;

            if (!absOffsets.TryGetValue(Paint, out int childAbs))
                throw new InvalidOperationException("Missing child paint offset.");

            int rel = checked(childAbs - selfAbsOffset);
            if ((uint)rel > 0x00FFFFFFu)
                throw new InvalidOperationException("PaintTransform paintOffset must fit in Offset24.");

            destination[1] = (byte)(rel >> 16);
            destination[2] = (byte)(rel >> 8);
            destination[3] = (byte)rel;

            // Affine2x3 immediately follows the fixed header (offset 7).
            destination[4] = 0;
            destination[5] = 0;
            destination[6] = 7;

            int p = 7;
            BigEndian.WriteUInt32(destination, p + 0, Transform.XX.RawValue);
            BigEndian.WriteUInt32(destination, p + 4, Transform.YX.RawValue);
            BigEndian.WriteUInt32(destination, p + 8, Transform.XY.RawValue);
            BigEndian.WriteUInt32(destination, p + 12, Transform.YY.RawValue);
            BigEndian.WriteUInt32(destination, p + 16, Transform.DX.RawValue);
            BigEndian.WriteUInt32(destination, p + 20, Transform.DY.RawValue);
        }
    }

    public sealed class PaintVarTransformV1 : PaintV1
    {
        public PaintV1 Paint { get; }
        public Affine2x3 Transform { get; }
        public uint VarIndexBase { get; }

        public PaintVarTransformV1(PaintV1 paint, Affine2x3 transform, uint varIndexBase)
        {
            Paint = paint ?? throw new ArgumentNullException(nameof(paint));
            Transform = transform;
            VarIndexBase = varIndexBase;
        }

        internal override byte Format => 13;
        internal override int ByteLength => 35;
        internal override int ChildCount => 1;
        internal override PaintV1 GetChild(int index) => index == 0 ? Paint : throw new ArgumentOutOfRangeException(nameof(index));

        internal override void Write(Span<byte> destination, int selfAbsOffset, Dictionary<PaintV1, int> absOffsets)
        {
            destination[0] = 13;

            if (!absOffsets.TryGetValue(Paint, out int childAbs))
                throw new InvalidOperationException("Missing child paint offset.");

            int rel = checked(childAbs - selfAbsOffset);
            if ((uint)rel > 0x00FFFFFFu)
                throw new InvalidOperationException("PaintVarTransform paintOffset must fit in Offset24.");

            destination[1] = (byte)(rel >> 16);
            destination[2] = (byte)(rel >> 8);
            destination[3] = (byte)rel;

            destination[4] = 0;
            destination[5] = 0;
            destination[6] = 7;

            int p = 7;
            BigEndian.WriteUInt32(destination, p + 0, Transform.XX.RawValue);
            BigEndian.WriteUInt32(destination, p + 4, Transform.YX.RawValue);
            BigEndian.WriteUInt32(destination, p + 8, Transform.XY.RawValue);
            BigEndian.WriteUInt32(destination, p + 12, Transform.YY.RawValue);
            BigEndian.WriteUInt32(destination, p + 16, Transform.DX.RawValue);
            BigEndian.WriteUInt32(destination, p + 20, Transform.DY.RawValue);
            BigEndian.WriteUInt32(destination, p + 24, VarIndexBase);
        }
    }

    public sealed class PaintTranslateV1 : PaintV1
    {
        public PaintV1 Paint { get; }
        public short Dx { get; }
        public short Dy { get; }

        public PaintTranslateV1(PaintV1 paint, short dx, short dy)
        {
            Paint = paint ?? throw new ArgumentNullException(nameof(paint));
            Dx = dx;
            Dy = dy;
        }

        internal override byte Format => 14;
        internal override int ByteLength => 8;
        internal override int ChildCount => 1;
        internal override PaintV1 GetChild(int index) => index == 0 ? Paint : throw new ArgumentOutOfRangeException(nameof(index));

        internal override void Write(Span<byte> destination, int selfAbsOffset, Dictionary<PaintV1, int> absOffsets)
        {
            destination[0] = 14;

            if (!absOffsets.TryGetValue(Paint, out int childAbs))
                throw new InvalidOperationException("Missing child paint offset.");

            int rel = checked(childAbs - selfAbsOffset);
            if ((uint)rel > 0x00FFFFFFu)
                throw new InvalidOperationException("PaintTranslate paintOffset must fit in Offset24.");

            destination[1] = (byte)(rel >> 16);
            destination[2] = (byte)(rel >> 8);
            destination[3] = (byte)rel;

            BigEndian.WriteInt16(destination, 4, Dx);
            BigEndian.WriteInt16(destination, 6, Dy);
        }
    }

    public sealed class PaintVarTranslateV1 : PaintV1
    {
        public PaintV1 Paint { get; }
        public short Dx { get; }
        public short Dy { get; }
        public uint VarIndexBase { get; }

        public PaintVarTranslateV1(PaintV1 paint, short dx, short dy, uint varIndexBase)
        {
            Paint = paint ?? throw new ArgumentNullException(nameof(paint));
            Dx = dx;
            Dy = dy;
            VarIndexBase = varIndexBase;
        }

        internal override byte Format => 15;
        internal override int ByteLength => 12;
        internal override int ChildCount => 1;
        internal override PaintV1 GetChild(int index) => index == 0 ? Paint : throw new ArgumentOutOfRangeException(nameof(index));

        internal override void Write(Span<byte> destination, int selfAbsOffset, Dictionary<PaintV1, int> absOffsets)
        {
            destination[0] = 15;

            if (!absOffsets.TryGetValue(Paint, out int childAbs))
                throw new InvalidOperationException("Missing child paint offset.");

            int rel = checked(childAbs - selfAbsOffset);
            if ((uint)rel > 0x00FFFFFFu)
                throw new InvalidOperationException("PaintVarTranslate paintOffset must fit in Offset24.");

            destination[1] = (byte)(rel >> 16);
            destination[2] = (byte)(rel >> 8);
            destination[3] = (byte)rel;

            BigEndian.WriteInt16(destination, 4, Dx);
            BigEndian.WriteInt16(destination, 6, Dy);
            BigEndian.WriteUInt32(destination, 8, VarIndexBase);
        }
    }

    public sealed class PaintScaleV1 : PaintV1
    {
        public PaintV1 Paint { get; }
        public F2Dot14 ScaleX { get; }
        public F2Dot14 ScaleY { get; }

        public PaintScaleV1(PaintV1 paint, F2Dot14 scaleX, F2Dot14 scaleY)
        {
            Paint = paint ?? throw new ArgumentNullException(nameof(paint));
            ScaleX = scaleX;
            ScaleY = scaleY;
        }

        internal override byte Format => 16;
        internal override int ByteLength => 8;
        internal override int ChildCount => 1;
        internal override PaintV1 GetChild(int index) => index == 0 ? Paint : throw new ArgumentOutOfRangeException(nameof(index));

        internal override void Write(Span<byte> destination, int selfAbsOffset, Dictionary<PaintV1, int> absOffsets)
        {
            destination[0] = 16;

            if (!absOffsets.TryGetValue(Paint, out int childAbs))
                throw new InvalidOperationException("Missing child paint offset.");

            int rel = checked(childAbs - selfAbsOffset);
            if ((uint)rel > 0x00FFFFFFu)
                throw new InvalidOperationException("PaintScale paintOffset must fit in Offset24.");

            destination[1] = (byte)(rel >> 16);
            destination[2] = (byte)(rel >> 8);
            destination[3] = (byte)rel;

            BigEndian.WriteInt16(destination, 4, ScaleX.RawValue);
            BigEndian.WriteInt16(destination, 6, ScaleY.RawValue);
        }
    }

    public sealed class PaintVarScaleV1 : PaintV1
    {
        public PaintV1 Paint { get; }
        public F2Dot14 ScaleX { get; }
        public F2Dot14 ScaleY { get; }
        public uint VarIndexBase { get; }

        public PaintVarScaleV1(PaintV1 paint, F2Dot14 scaleX, F2Dot14 scaleY, uint varIndexBase)
        {
            Paint = paint ?? throw new ArgumentNullException(nameof(paint));
            ScaleX = scaleX;
            ScaleY = scaleY;
            VarIndexBase = varIndexBase;
        }

        internal override byte Format => 17;
        internal override int ByteLength => 12;
        internal override int ChildCount => 1;
        internal override PaintV1 GetChild(int index) => index == 0 ? Paint : throw new ArgumentOutOfRangeException(nameof(index));

        internal override void Write(Span<byte> destination, int selfAbsOffset, Dictionary<PaintV1, int> absOffsets)
        {
            destination[0] = 17;

            if (!absOffsets.TryGetValue(Paint, out int childAbs))
                throw new InvalidOperationException("Missing child paint offset.");

            int rel = checked(childAbs - selfAbsOffset);
            if ((uint)rel > 0x00FFFFFFu)
                throw new InvalidOperationException("PaintVarScale paintOffset must fit in Offset24.");

            destination[1] = (byte)(rel >> 16);
            destination[2] = (byte)(rel >> 8);
            destination[3] = (byte)rel;

            BigEndian.WriteInt16(destination, 4, ScaleX.RawValue);
            BigEndian.WriteInt16(destination, 6, ScaleY.RawValue);
            BigEndian.WriteUInt32(destination, 8, VarIndexBase);
        }
    }

    public sealed class PaintScaleAroundCenterV1 : PaintV1
    {
        public PaintV1 Paint { get; }
        public F2Dot14 ScaleX { get; }
        public F2Dot14 ScaleY { get; }
        public short CenterX { get; }
        public short CenterY { get; }

        public PaintScaleAroundCenterV1(PaintV1 paint, F2Dot14 scaleX, F2Dot14 scaleY, short centerX, short centerY)
        {
            Paint = paint ?? throw new ArgumentNullException(nameof(paint));
            ScaleX = scaleX;
            ScaleY = scaleY;
            CenterX = centerX;
            CenterY = centerY;
        }

        internal override byte Format => 18;
        internal override int ByteLength => 12;
        internal override int ChildCount => 1;
        internal override PaintV1 GetChild(int index) => index == 0 ? Paint : throw new ArgumentOutOfRangeException(nameof(index));

        internal override void Write(Span<byte> destination, int selfAbsOffset, Dictionary<PaintV1, int> absOffsets)
        {
            destination[0] = 18;

            if (!absOffsets.TryGetValue(Paint, out int childAbs))
                throw new InvalidOperationException("Missing child paint offset.");

            int rel = checked(childAbs - selfAbsOffset);
            if ((uint)rel > 0x00FFFFFFu)
                throw new InvalidOperationException("PaintScaleAroundCenter paintOffset must fit in Offset24.");

            destination[1] = (byte)(rel >> 16);
            destination[2] = (byte)(rel >> 8);
            destination[3] = (byte)rel;

            BigEndian.WriteInt16(destination, 4, ScaleX.RawValue);
            BigEndian.WriteInt16(destination, 6, ScaleY.RawValue);
            BigEndian.WriteInt16(destination, 8, CenterX);
            BigEndian.WriteInt16(destination, 10, CenterY);
        }
    }

    public sealed class PaintVarScaleAroundCenterV1 : PaintV1
    {
        public PaintV1 Paint { get; }
        public F2Dot14 ScaleX { get; }
        public F2Dot14 ScaleY { get; }
        public short CenterX { get; }
        public short CenterY { get; }
        public uint VarIndexBase { get; }

        public PaintVarScaleAroundCenterV1(PaintV1 paint, F2Dot14 scaleX, F2Dot14 scaleY, short centerX, short centerY, uint varIndexBase)
        {
            Paint = paint ?? throw new ArgumentNullException(nameof(paint));
            ScaleX = scaleX;
            ScaleY = scaleY;
            CenterX = centerX;
            CenterY = centerY;
            VarIndexBase = varIndexBase;
        }

        internal override byte Format => 19;
        internal override int ByteLength => 16;
        internal override int ChildCount => 1;
        internal override PaintV1 GetChild(int index) => index == 0 ? Paint : throw new ArgumentOutOfRangeException(nameof(index));

        internal override void Write(Span<byte> destination, int selfAbsOffset, Dictionary<PaintV1, int> absOffsets)
        {
            destination[0] = 19;

            if (!absOffsets.TryGetValue(Paint, out int childAbs))
                throw new InvalidOperationException("Missing child paint offset.");

            int rel = checked(childAbs - selfAbsOffset);
            if ((uint)rel > 0x00FFFFFFu)
                throw new InvalidOperationException("PaintVarScaleAroundCenter paintOffset must fit in Offset24.");

            destination[1] = (byte)(rel >> 16);
            destination[2] = (byte)(rel >> 8);
            destination[3] = (byte)rel;

            BigEndian.WriteInt16(destination, 4, ScaleX.RawValue);
            BigEndian.WriteInt16(destination, 6, ScaleY.RawValue);
            BigEndian.WriteInt16(destination, 8, CenterX);
            BigEndian.WriteInt16(destination, 10, CenterY);
            BigEndian.WriteUInt32(destination, 12, VarIndexBase);
        }
    }

    public sealed class PaintScaleUniformV1 : PaintV1
    {
        public PaintV1 Paint { get; }
        public F2Dot14 Scale { get; }

        public PaintScaleUniformV1(PaintV1 paint, F2Dot14 scale)
        {
            Paint = paint ?? throw new ArgumentNullException(nameof(paint));
            Scale = scale;
        }

        internal override byte Format => 20;
        internal override int ByteLength => 6;
        internal override int ChildCount => 1;
        internal override PaintV1 GetChild(int index) => index == 0 ? Paint : throw new ArgumentOutOfRangeException(nameof(index));

        internal override void Write(Span<byte> destination, int selfAbsOffset, Dictionary<PaintV1, int> absOffsets)
        {
            destination[0] = 20;

            if (!absOffsets.TryGetValue(Paint, out int childAbs))
                throw new InvalidOperationException("Missing child paint offset.");

            int rel = checked(childAbs - selfAbsOffset);
            if ((uint)rel > 0x00FFFFFFu)
                throw new InvalidOperationException("PaintScaleUniform paintOffset must fit in Offset24.");

            destination[1] = (byte)(rel >> 16);
            destination[2] = (byte)(rel >> 8);
            destination[3] = (byte)rel;

            BigEndian.WriteInt16(destination, 4, Scale.RawValue);
        }
    }

    public sealed class PaintVarScaleUniformV1 : PaintV1
    {
        public PaintV1 Paint { get; }
        public F2Dot14 Scale { get; }
        public uint VarIndexBase { get; }

        public PaintVarScaleUniformV1(PaintV1 paint, F2Dot14 scale, uint varIndexBase)
        {
            Paint = paint ?? throw new ArgumentNullException(nameof(paint));
            Scale = scale;
            VarIndexBase = varIndexBase;
        }

        internal override byte Format => 21;
        internal override int ByteLength => 10;
        internal override int ChildCount => 1;
        internal override PaintV1 GetChild(int index) => index == 0 ? Paint : throw new ArgumentOutOfRangeException(nameof(index));

        internal override void Write(Span<byte> destination, int selfAbsOffset, Dictionary<PaintV1, int> absOffsets)
        {
            destination[0] = 21;

            if (!absOffsets.TryGetValue(Paint, out int childAbs))
                throw new InvalidOperationException("Missing child paint offset.");

            int rel = checked(childAbs - selfAbsOffset);
            if ((uint)rel > 0x00FFFFFFu)
                throw new InvalidOperationException("PaintVarScaleUniform paintOffset must fit in Offset24.");

            destination[1] = (byte)(rel >> 16);
            destination[2] = (byte)(rel >> 8);
            destination[3] = (byte)rel;

            BigEndian.WriteInt16(destination, 4, Scale.RawValue);
            BigEndian.WriteUInt32(destination, 6, VarIndexBase);
        }
    }

    public sealed class PaintScaleUniformAroundCenterV1 : PaintV1
    {
        public PaintV1 Paint { get; }
        public F2Dot14 Scale { get; }
        public short CenterX { get; }
        public short CenterY { get; }

        public PaintScaleUniformAroundCenterV1(PaintV1 paint, F2Dot14 scale, short centerX, short centerY)
        {
            Paint = paint ?? throw new ArgumentNullException(nameof(paint));
            Scale = scale;
            CenterX = centerX;
            CenterY = centerY;
        }

        internal override byte Format => 22;
        internal override int ByteLength => 10;
        internal override int ChildCount => 1;
        internal override PaintV1 GetChild(int index) => index == 0 ? Paint : throw new ArgumentOutOfRangeException(nameof(index));

        internal override void Write(Span<byte> destination, int selfAbsOffset, Dictionary<PaintV1, int> absOffsets)
        {
            destination[0] = 22;

            if (!absOffsets.TryGetValue(Paint, out int childAbs))
                throw new InvalidOperationException("Missing child paint offset.");

            int rel = checked(childAbs - selfAbsOffset);
            if ((uint)rel > 0x00FFFFFFu)
                throw new InvalidOperationException("PaintScaleUniformAroundCenter paintOffset must fit in Offset24.");

            destination[1] = (byte)(rel >> 16);
            destination[2] = (byte)(rel >> 8);
            destination[3] = (byte)rel;

            BigEndian.WriteInt16(destination, 4, Scale.RawValue);
            BigEndian.WriteInt16(destination, 6, CenterX);
            BigEndian.WriteInt16(destination, 8, CenterY);
        }
    }

    public sealed class PaintVarScaleUniformAroundCenterV1 : PaintV1
    {
        public PaintV1 Paint { get; }
        public F2Dot14 Scale { get; }
        public short CenterX { get; }
        public short CenterY { get; }
        public uint VarIndexBase { get; }

        public PaintVarScaleUniformAroundCenterV1(PaintV1 paint, F2Dot14 scale, short centerX, short centerY, uint varIndexBase)
        {
            Paint = paint ?? throw new ArgumentNullException(nameof(paint));
            Scale = scale;
            CenterX = centerX;
            CenterY = centerY;
            VarIndexBase = varIndexBase;
        }

        internal override byte Format => 23;
        internal override int ByteLength => 14;
        internal override int ChildCount => 1;
        internal override PaintV1 GetChild(int index) => index == 0 ? Paint : throw new ArgumentOutOfRangeException(nameof(index));

        internal override void Write(Span<byte> destination, int selfAbsOffset, Dictionary<PaintV1, int> absOffsets)
        {
            destination[0] = 23;

            if (!absOffsets.TryGetValue(Paint, out int childAbs))
                throw new InvalidOperationException("Missing child paint offset.");

            int rel = checked(childAbs - selfAbsOffset);
            if ((uint)rel > 0x00FFFFFFu)
                throw new InvalidOperationException("PaintVarScaleUniformAroundCenter paintOffset must fit in Offset24.");

            destination[1] = (byte)(rel >> 16);
            destination[2] = (byte)(rel >> 8);
            destination[3] = (byte)rel;

            BigEndian.WriteInt16(destination, 4, Scale.RawValue);
            BigEndian.WriteInt16(destination, 6, CenterX);
            BigEndian.WriteInt16(destination, 8, CenterY);
            BigEndian.WriteUInt32(destination, 10, VarIndexBase);
        }
    }

    public sealed class PaintRotateV1 : PaintV1
    {
        public PaintV1 Paint { get; }
        public F2Dot14 Angle { get; }

        public PaintRotateV1(PaintV1 paint, F2Dot14 angle)
        {
            Paint = paint ?? throw new ArgumentNullException(nameof(paint));
            Angle = angle;
        }

        internal override byte Format => 24;
        internal override int ByteLength => 6;
        internal override int ChildCount => 1;
        internal override PaintV1 GetChild(int index) => index == 0 ? Paint : throw new ArgumentOutOfRangeException(nameof(index));

        internal override void Write(Span<byte> destination, int selfAbsOffset, Dictionary<PaintV1, int> absOffsets)
        {
            destination[0] = 24;

            if (!absOffsets.TryGetValue(Paint, out int childAbs))
                throw new InvalidOperationException("Missing child paint offset.");

            int rel = checked(childAbs - selfAbsOffset);
            if ((uint)rel > 0x00FFFFFFu)
                throw new InvalidOperationException("PaintRotate paintOffset must fit in Offset24.");

            destination[1] = (byte)(rel >> 16);
            destination[2] = (byte)(rel >> 8);
            destination[3] = (byte)rel;

            BigEndian.WriteInt16(destination, 4, Angle.RawValue);
        }
    }

    public sealed class PaintVarRotateV1 : PaintV1
    {
        public PaintV1 Paint { get; }
        public F2Dot14 Angle { get; }
        public uint VarIndexBase { get; }

        public PaintVarRotateV1(PaintV1 paint, F2Dot14 angle, uint varIndexBase)
        {
            Paint = paint ?? throw new ArgumentNullException(nameof(paint));
            Angle = angle;
            VarIndexBase = varIndexBase;
        }

        internal override byte Format => 25;
        internal override int ByteLength => 10;
        internal override int ChildCount => 1;
        internal override PaintV1 GetChild(int index) => index == 0 ? Paint : throw new ArgumentOutOfRangeException(nameof(index));

        internal override void Write(Span<byte> destination, int selfAbsOffset, Dictionary<PaintV1, int> absOffsets)
        {
            destination[0] = 25;

            if (!absOffsets.TryGetValue(Paint, out int childAbs))
                throw new InvalidOperationException("Missing child paint offset.");

            int rel = checked(childAbs - selfAbsOffset);
            if ((uint)rel > 0x00FFFFFFu)
                throw new InvalidOperationException("PaintVarRotate paintOffset must fit in Offset24.");

            destination[1] = (byte)(rel >> 16);
            destination[2] = (byte)(rel >> 8);
            destination[3] = (byte)rel;

            BigEndian.WriteInt16(destination, 4, Angle.RawValue);
            BigEndian.WriteUInt32(destination, 6, VarIndexBase);
        }
    }

    public sealed class PaintRotateAroundCenterV1 : PaintV1
    {
        public PaintV1 Paint { get; }
        public F2Dot14 Angle { get; }
        public short CenterX { get; }
        public short CenterY { get; }

        public PaintRotateAroundCenterV1(PaintV1 paint, F2Dot14 angle, short centerX, short centerY)
        {
            Paint = paint ?? throw new ArgumentNullException(nameof(paint));
            Angle = angle;
            CenterX = centerX;
            CenterY = centerY;
        }

        internal override byte Format => 26;
        internal override int ByteLength => 10;
        internal override int ChildCount => 1;
        internal override PaintV1 GetChild(int index) => index == 0 ? Paint : throw new ArgumentOutOfRangeException(nameof(index));

        internal override void Write(Span<byte> destination, int selfAbsOffset, Dictionary<PaintV1, int> absOffsets)
        {
            destination[0] = 26;

            if (!absOffsets.TryGetValue(Paint, out int childAbs))
                throw new InvalidOperationException("Missing child paint offset.");

            int rel = checked(childAbs - selfAbsOffset);
            if ((uint)rel > 0x00FFFFFFu)
                throw new InvalidOperationException("PaintRotateAroundCenter paintOffset must fit in Offset24.");

            destination[1] = (byte)(rel >> 16);
            destination[2] = (byte)(rel >> 8);
            destination[3] = (byte)rel;

            BigEndian.WriteInt16(destination, 4, Angle.RawValue);
            BigEndian.WriteInt16(destination, 6, CenterX);
            BigEndian.WriteInt16(destination, 8, CenterY);
        }
    }

    public sealed class PaintVarRotateAroundCenterV1 : PaintV1
    {
        public PaintV1 Paint { get; }
        public F2Dot14 Angle { get; }
        public short CenterX { get; }
        public short CenterY { get; }
        public uint VarIndexBase { get; }

        public PaintVarRotateAroundCenterV1(PaintV1 paint, F2Dot14 angle, short centerX, short centerY, uint varIndexBase)
        {
            Paint = paint ?? throw new ArgumentNullException(nameof(paint));
            Angle = angle;
            CenterX = centerX;
            CenterY = centerY;
            VarIndexBase = varIndexBase;
        }

        internal override byte Format => 27;
        internal override int ByteLength => 14;
        internal override int ChildCount => 1;
        internal override PaintV1 GetChild(int index) => index == 0 ? Paint : throw new ArgumentOutOfRangeException(nameof(index));

        internal override void Write(Span<byte> destination, int selfAbsOffset, Dictionary<PaintV1, int> absOffsets)
        {
            destination[0] = 27;

            if (!absOffsets.TryGetValue(Paint, out int childAbs))
                throw new InvalidOperationException("Missing child paint offset.");

            int rel = checked(childAbs - selfAbsOffset);
            if ((uint)rel > 0x00FFFFFFu)
                throw new InvalidOperationException("PaintVarRotateAroundCenter paintOffset must fit in Offset24.");

            destination[1] = (byte)(rel >> 16);
            destination[2] = (byte)(rel >> 8);
            destination[3] = (byte)rel;

            BigEndian.WriteInt16(destination, 4, Angle.RawValue);
            BigEndian.WriteInt16(destination, 6, CenterX);
            BigEndian.WriteInt16(destination, 8, CenterY);
            BigEndian.WriteUInt32(destination, 10, VarIndexBase);
        }
    }

    public sealed class PaintSkewV1 : PaintV1
    {
        public PaintV1 Paint { get; }
        public F2Dot14 XSkewAngle { get; }
        public F2Dot14 YSkewAngle { get; }

        public PaintSkewV1(PaintV1 paint, F2Dot14 xSkewAngle, F2Dot14 ySkewAngle)
        {
            Paint = paint ?? throw new ArgumentNullException(nameof(paint));
            XSkewAngle = xSkewAngle;
            YSkewAngle = ySkewAngle;
        }

        internal override byte Format => 28;
        internal override int ByteLength => 8;
        internal override int ChildCount => 1;
        internal override PaintV1 GetChild(int index) => index == 0 ? Paint : throw new ArgumentOutOfRangeException(nameof(index));

        internal override void Write(Span<byte> destination, int selfAbsOffset, Dictionary<PaintV1, int> absOffsets)
        {
            destination[0] = 28;

            if (!absOffsets.TryGetValue(Paint, out int childAbs))
                throw new InvalidOperationException("Missing child paint offset.");

            int rel = checked(childAbs - selfAbsOffset);
            if ((uint)rel > 0x00FFFFFFu)
                throw new InvalidOperationException("PaintSkew paintOffset must fit in Offset24.");

            destination[1] = (byte)(rel >> 16);
            destination[2] = (byte)(rel >> 8);
            destination[3] = (byte)rel;

            BigEndian.WriteInt16(destination, 4, XSkewAngle.RawValue);
            BigEndian.WriteInt16(destination, 6, YSkewAngle.RawValue);
        }
    }

    public sealed class PaintVarSkewV1 : PaintV1
    {
        public PaintV1 Paint { get; }
        public F2Dot14 XSkewAngle { get; }
        public F2Dot14 YSkewAngle { get; }
        public uint VarIndexBase { get; }

        public PaintVarSkewV1(PaintV1 paint, F2Dot14 xSkewAngle, F2Dot14 ySkewAngle, uint varIndexBase)
        {
            Paint = paint ?? throw new ArgumentNullException(nameof(paint));
            XSkewAngle = xSkewAngle;
            YSkewAngle = ySkewAngle;
            VarIndexBase = varIndexBase;
        }

        internal override byte Format => 29;
        internal override int ByteLength => 12;
        internal override int ChildCount => 1;
        internal override PaintV1 GetChild(int index) => index == 0 ? Paint : throw new ArgumentOutOfRangeException(nameof(index));

        internal override void Write(Span<byte> destination, int selfAbsOffset, Dictionary<PaintV1, int> absOffsets)
        {
            destination[0] = 29;

            if (!absOffsets.TryGetValue(Paint, out int childAbs))
                throw new InvalidOperationException("Missing child paint offset.");

            int rel = checked(childAbs - selfAbsOffset);
            if ((uint)rel > 0x00FFFFFFu)
                throw new InvalidOperationException("PaintVarSkew paintOffset must fit in Offset24.");

            destination[1] = (byte)(rel >> 16);
            destination[2] = (byte)(rel >> 8);
            destination[3] = (byte)rel;

            BigEndian.WriteInt16(destination, 4, XSkewAngle.RawValue);
            BigEndian.WriteInt16(destination, 6, YSkewAngle.RawValue);
            BigEndian.WriteUInt32(destination, 8, VarIndexBase);
        }
    }

    public sealed class PaintSkewAroundCenterV1 : PaintV1
    {
        public PaintV1 Paint { get; }
        public F2Dot14 XSkewAngle { get; }
        public F2Dot14 YSkewAngle { get; }
        public short CenterX { get; }
        public short CenterY { get; }

        public PaintSkewAroundCenterV1(PaintV1 paint, F2Dot14 xSkewAngle, F2Dot14 ySkewAngle, short centerX, short centerY)
        {
            Paint = paint ?? throw new ArgumentNullException(nameof(paint));
            XSkewAngle = xSkewAngle;
            YSkewAngle = ySkewAngle;
            CenterX = centerX;
            CenterY = centerY;
        }

        internal override byte Format => 30;
        internal override int ByteLength => 12;
        internal override int ChildCount => 1;
        internal override PaintV1 GetChild(int index) => index == 0 ? Paint : throw new ArgumentOutOfRangeException(nameof(index));

        internal override void Write(Span<byte> destination, int selfAbsOffset, Dictionary<PaintV1, int> absOffsets)
        {
            destination[0] = 30;

            if (!absOffsets.TryGetValue(Paint, out int childAbs))
                throw new InvalidOperationException("Missing child paint offset.");

            int rel = checked(childAbs - selfAbsOffset);
            if ((uint)rel > 0x00FFFFFFu)
                throw new InvalidOperationException("PaintSkewAroundCenter paintOffset must fit in Offset24.");

            destination[1] = (byte)(rel >> 16);
            destination[2] = (byte)(rel >> 8);
            destination[3] = (byte)rel;

            BigEndian.WriteInt16(destination, 4, XSkewAngle.RawValue);
            BigEndian.WriteInt16(destination, 6, YSkewAngle.RawValue);
            BigEndian.WriteInt16(destination, 8, CenterX);
            BigEndian.WriteInt16(destination, 10, CenterY);
        }
    }

    public sealed class PaintVarSkewAroundCenterV1 : PaintV1
    {
        public PaintV1 Paint { get; }
        public F2Dot14 XSkewAngle { get; }
        public F2Dot14 YSkewAngle { get; }
        public short CenterX { get; }
        public short CenterY { get; }
        public uint VarIndexBase { get; }

        public PaintVarSkewAroundCenterV1(PaintV1 paint, F2Dot14 xSkewAngle, F2Dot14 ySkewAngle, short centerX, short centerY, uint varIndexBase)
        {
            Paint = paint ?? throw new ArgumentNullException(nameof(paint));
            XSkewAngle = xSkewAngle;
            YSkewAngle = ySkewAngle;
            CenterX = centerX;
            CenterY = centerY;
            VarIndexBase = varIndexBase;
        }

        internal override byte Format => 31;
        internal override int ByteLength => 16;
        internal override int ChildCount => 1;
        internal override PaintV1 GetChild(int index) => index == 0 ? Paint : throw new ArgumentOutOfRangeException(nameof(index));

        internal override void Write(Span<byte> destination, int selfAbsOffset, Dictionary<PaintV1, int> absOffsets)
        {
            destination[0] = 31;

            if (!absOffsets.TryGetValue(Paint, out int childAbs))
                throw new InvalidOperationException("Missing child paint offset.");

            int rel = checked(childAbs - selfAbsOffset);
            if ((uint)rel > 0x00FFFFFFu)
                throw new InvalidOperationException("PaintVarSkewAroundCenter paintOffset must fit in Offset24.");

            destination[1] = (byte)(rel >> 16);
            destination[2] = (byte)(rel >> 8);
            destination[3] = (byte)rel;

            BigEndian.WriteInt16(destination, 4, XSkewAngle.RawValue);
            BigEndian.WriteInt16(destination, 6, YSkewAngle.RawValue);
            BigEndian.WriteInt16(destination, 8, CenterX);
            BigEndian.WriteInt16(destination, 10, CenterY);
            BigEndian.WriteUInt32(destination, 12, VarIndexBase);
        }
    }

    public sealed class PaintCompositeV1 : PaintV1
    {
        public PaintV1 SourcePaint { get; }
        public byte CompositeMode { get; }
        public PaintV1 BackdropPaint { get; }

        public PaintCompositeV1(PaintV1 sourcePaint, byte compositeMode, PaintV1 backdropPaint)
        {
            SourcePaint = sourcePaint ?? throw new ArgumentNullException(nameof(sourcePaint));
            CompositeMode = compositeMode;
            BackdropPaint = backdropPaint ?? throw new ArgumentNullException(nameof(backdropPaint));
        }

        internal override byte Format => 32;
        internal override int ByteLength => 8;
        internal override int ChildCount => 2;
        internal override PaintV1 GetChild(int index) => index == 0 ? SourcePaint : index == 1 ? BackdropPaint : throw new ArgumentOutOfRangeException(nameof(index));

        internal override void Write(Span<byte> destination, int selfAbsOffset, Dictionary<PaintV1, int> absOffsets)
        {
            destination[0] = 32;

            if (!absOffsets.TryGetValue(SourcePaint, out int sourceAbs))
                throw new InvalidOperationException("Missing source paint offset.");
            if (!absOffsets.TryGetValue(BackdropPaint, out int backdropAbs))
                throw new InvalidOperationException("Missing backdrop paint offset.");

            int sourceRel = checked(sourceAbs - selfAbsOffset);
            int backdropRel = checked(backdropAbs - selfAbsOffset);

            if ((uint)sourceRel > 0x00FFFFFFu)
                throw new InvalidOperationException("PaintComposite sourcePaintOffset must fit in Offset24.");
            if ((uint)backdropRel > 0x00FFFFFFu)
                throw new InvalidOperationException("PaintComposite backdropPaintOffset must fit in Offset24.");

            destination[1] = (byte)(sourceRel >> 16);
            destination[2] = (byte)(sourceRel >> 8);
            destination[3] = (byte)sourceRel;
            destination[4] = CompositeMode;
            destination[5] = (byte)(backdropRel >> 16);
            destination[6] = (byte)(backdropRel >> 8);
            destination[7] = (byte)backdropRel;
        }
    }

    public static PaintV1 Solid(ushort paletteIndex, F2Dot14 alpha)
        => new PaintSolidV1(paletteIndex, alpha);

    public static PaintV1 VarSolid(ushort paletteIndex, F2Dot14 alpha, uint varIndexBase)
        => new PaintVarSolidV1(paletteIndex, alpha, varIndexBase);

    public static PaintV1 ColrGlyph(ushort glyphId)
        => new PaintColrGlyphV1(glyphId);

    public static PaintV1 Glyph(ushort glyphId, PaintV1 paint)
        => new PaintGlyphV1(paint, glyphId);

    public static PaintV1 ColrLayers(ReadOnlySpan<PaintV1> layerPaints)
    {
        var copied = layerPaints.ToArray();
        return new PaintColrLayersV1(copied);
    }

    public static PaintV1 LinearGradient(byte extend, short x0, short y0, short x1, short y1, short x2, short y2, ReadOnlySpan<ColorStopV1> stops)
        => new PaintLinearGradientV1(extend, x0, y0, x1, y1, x2, y2, stops.ToArray());

    public static PaintV1 RadialGradient(byte extend, short x0, short y0, ushort radius0, short x1, short y1, ushort radius1, ReadOnlySpan<ColorStopV1> stops)
        => new PaintRadialGradientV1(extend, x0, y0, radius0, x1, y1, radius1, stops.ToArray());

    public static PaintV1 SweepGradient(byte extend, short centerX, short centerY, F2Dot14 startAngle, F2Dot14 endAngle, ReadOnlySpan<ColorStopV1> stops)
        => new PaintSweepGradientV1(extend, centerX, centerY, startAngle, endAngle, stops.ToArray());

    public static PaintV1 VarLinearGradient(byte extend, short x0, short y0, short x1, short y1, short x2, short y2, uint varIndexBase, ReadOnlySpan<VarColorStopV1> stops)
        => new PaintVarLinearGradientV1(extend, x0, y0, x1, y1, x2, y2, varIndexBase, stops.ToArray());

    public static PaintV1 VarRadialGradient(byte extend, short x0, short y0, ushort radius0, short x1, short y1, ushort radius1, uint varIndexBase, ReadOnlySpan<VarColorStopV1> stops)
        => new PaintVarRadialGradientV1(extend, x0, y0, radius0, x1, y1, radius1, varIndexBase, stops.ToArray());

    public static PaintV1 VarSweepGradient(byte extend, short centerX, short centerY, F2Dot14 startAngle, F2Dot14 endAngle, uint varIndexBase, ReadOnlySpan<VarColorStopV1> stops)
        => new PaintVarSweepGradientV1(extend, centerX, centerY, startAngle, endAngle, varIndexBase, stops.ToArray());

    public static PaintV1 Transform(PaintV1 paint, Affine2x3 transform)
        => new PaintTransformV1(paint, transform);

    public static PaintV1 VarTransform(PaintV1 paint, Affine2x3 transform, uint varIndexBase)
        => new PaintVarTransformV1(paint, transform, varIndexBase);

    public static PaintV1 Translate(PaintV1 paint, short dx, short dy)
        => new PaintTranslateV1(paint, dx, dy);

    public static PaintV1 VarTranslate(PaintV1 paint, short dx, short dy, uint varIndexBase)
        => new PaintVarTranslateV1(paint, dx, dy, varIndexBase);

    public static PaintV1 Scale(PaintV1 paint, F2Dot14 scaleX, F2Dot14 scaleY)
        => new PaintScaleV1(paint, scaleX, scaleY);

    public static PaintV1 VarScale(PaintV1 paint, F2Dot14 scaleX, F2Dot14 scaleY, uint varIndexBase)
        => new PaintVarScaleV1(paint, scaleX, scaleY, varIndexBase);

    public static PaintV1 ScaleAroundCenter(PaintV1 paint, F2Dot14 scaleX, F2Dot14 scaleY, short centerX, short centerY)
        => new PaintScaleAroundCenterV1(paint, scaleX, scaleY, centerX, centerY);

    public static PaintV1 VarScaleAroundCenter(PaintV1 paint, F2Dot14 scaleX, F2Dot14 scaleY, short centerX, short centerY, uint varIndexBase)
        => new PaintVarScaleAroundCenterV1(paint, scaleX, scaleY, centerX, centerY, varIndexBase);

    public static PaintV1 ScaleUniform(PaintV1 paint, F2Dot14 scale)
        => new PaintScaleUniformV1(paint, scale);

    public static PaintV1 VarScaleUniform(PaintV1 paint, F2Dot14 scale, uint varIndexBase)
        => new PaintVarScaleUniformV1(paint, scale, varIndexBase);

    public static PaintV1 ScaleUniformAroundCenter(PaintV1 paint, F2Dot14 scale, short centerX, short centerY)
        => new PaintScaleUniformAroundCenterV1(paint, scale, centerX, centerY);

    public static PaintV1 VarScaleUniformAroundCenter(PaintV1 paint, F2Dot14 scale, short centerX, short centerY, uint varIndexBase)
        => new PaintVarScaleUniformAroundCenterV1(paint, scale, centerX, centerY, varIndexBase);

    public static PaintV1 Rotate(PaintV1 paint, F2Dot14 angle)
        => new PaintRotateV1(paint, angle);

    public static PaintV1 VarRotate(PaintV1 paint, F2Dot14 angle, uint varIndexBase)
        => new PaintVarRotateV1(paint, angle, varIndexBase);

    public static PaintV1 RotateAroundCenter(PaintV1 paint, F2Dot14 angle, short centerX, short centerY)
        => new PaintRotateAroundCenterV1(paint, angle, centerX, centerY);

    public static PaintV1 VarRotateAroundCenter(PaintV1 paint, F2Dot14 angle, short centerX, short centerY, uint varIndexBase)
        => new PaintVarRotateAroundCenterV1(paint, angle, centerX, centerY, varIndexBase);

    public static PaintV1 Skew(PaintV1 paint, F2Dot14 xSkewAngle, F2Dot14 ySkewAngle)
        => new PaintSkewV1(paint, xSkewAngle, ySkewAngle);

    public static PaintV1 VarSkew(PaintV1 paint, F2Dot14 xSkewAngle, F2Dot14 ySkewAngle, uint varIndexBase)
        => new PaintVarSkewV1(paint, xSkewAngle, ySkewAngle, varIndexBase);

    public static PaintV1 SkewAroundCenter(PaintV1 paint, F2Dot14 xSkewAngle, F2Dot14 ySkewAngle, short centerX, short centerY)
        => new PaintSkewAroundCenterV1(paint, xSkewAngle, ySkewAngle, centerX, centerY);

    public static PaintV1 VarSkewAroundCenter(PaintV1 paint, F2Dot14 xSkewAngle, F2Dot14 ySkewAngle, short centerX, short centerY, uint varIndexBase)
        => new PaintVarSkewAroundCenterV1(paint, xSkewAngle, ySkewAngle, centerX, centerY, varIndexBase);

    public static PaintV1 Composite(PaintV1 sourcePaint, byte compositeMode, PaintV1 backdropPaint)
        => new PaintCompositeV1(sourcePaint, compositeMode, backdropPaint);

    public ReadOnlyMemory<byte> VarIndexMapBytes => _v1VarIndexMap;

    public ReadOnlyMemory<byte> ItemVariationStoreBytes => _v1ItemVariationStore;

    public void SetVarIndexMapData(ReadOnlyMemory<byte> deltaSetIndexMapBytes)
    {
        EnsureStructuredV1();
        _v1VarIndexMap = deltaSetIndexMapBytes;
        MarkDirty();
    }

    public void ClearVarIndexMap()
    {
        EnsureStructuredV1();
        if (_v1VarIndexMap.IsEmpty)
            return;

        _v1VarIndexMap = ReadOnlyMemory<byte>.Empty;
        MarkDirty();
    }

    public void SetItemVariationStoreData(ReadOnlyMemory<byte> itemVariationStoreBytes)
    {
        EnsureStructuredV1();
        _v1ItemVariationStore = itemVariationStoreBytes;
        MarkDirty();
    }

    public void SetMinimalItemVariationStore(ushort axisCount)
    {
        EnsureStructuredV1();

        // Minimal ItemVariationStore:
        // format=1, variationRegionListOffset=8, itemVariationDataCount=0
        // VariationRegionList: axisCount, regionCount=0
        byte[] store = new byte[12];
        var span = store.AsSpan();
        BigEndian.WriteUInt16(span, 0, 1);
        BigEndian.WriteUInt32(span, 2, 8);
        BigEndian.WriteUInt16(span, 6, 0);
        BigEndian.WriteUInt16(span, 8, axisCount);
        BigEndian.WriteUInt16(span, 10, 0);
        _v1ItemVariationStore = store;
        MarkDirty();
    }

    public void ClearItemVariationStore()
    {
        EnsureStructuredV1();
        if (_v1ItemVariationStore.IsEmpty)
            return;

        _v1ItemVariationStore = ReadOnlyMemory<byte>.Empty;
        MarkDirty();
    }

    public void SetBaseGlyphPaint(ushort baseGlyphId, PaintV1 paint)
    {
        if (paint is null) throw new ArgumentNullException(nameof(paint));

        EnsureStructuredV1();

        for (int i = _v1BaseGlyphs.Count - 1; i >= 0; i--)
        {
            if (_v1BaseGlyphs[i].baseGlyphId == baseGlyphId)
                _v1BaseGlyphs.RemoveAt(i);
        }

        _v1BaseGlyphs.Add((baseGlyphId, paint));
        MarkDirty();
    }

    public bool RemoveBaseGlyphPaint(ushort baseGlyphId)
    {
        EnsureStructuredV1();

        bool removed = false;
        for (int i = _v1BaseGlyphs.Count - 1; i >= 0; i--)
        {
            if (_v1BaseGlyphs[i].baseGlyphId == baseGlyphId)
            {
                _v1BaseGlyphs.RemoveAt(i);
                removed = true;
            }
        }

        if (removed)
            MarkDirty();

        return removed;
    }

    public void SetClipBoxRange(ushort startGlyphId, ushort endGlyphId, ClipBoxV1 clipBox)
    {
        EnsureStructuredV1();

        if (endGlyphId < startGlyphId)
            throw new ArgumentOutOfRangeException(nameof(endGlyphId));

        for (int i = _v1Clips.Count - 1; i >= 0; i--)
        {
            if (_v1Clips[i].startGlyphId == startGlyphId && _v1Clips[i].endGlyphId == endGlyphId)
                _v1Clips.RemoveAt(i);
        }

        _v1Clips.Add((startGlyphId, endGlyphId, clipBox));
        MarkDirty();
    }

    public bool RemoveClipBoxRange(ushort startGlyphId, ushort endGlyphId)
    {
        EnsureStructuredV1();

        bool removed = false;
        for (int i = _v1Clips.Count - 1; i >= 0; i--)
        {
            if (_v1Clips[i].startGlyphId == startGlyphId && _v1Clips[i].endGlyphId == endGlyphId)
            {
                _v1Clips.RemoveAt(i);
                removed = true;
            }
        }

        if (removed)
            MarkDirty();

        return removed;
    }

    private void EnsureStructuredV1()
    {
        if (_isRaw || !_isStructuredV1)
            throw new InvalidOperationException("COLR is not in structured v1 mode. Call ClearToVersion1() to switch to structured v1 editing.");
    }

    private byte[] BuildV1Bytes()
    {
        if (!_isStructuredV1)
            throw new InvalidOperationException("COLR is not in structured v1 mode.");

        var baseEntries = _v1BaseGlyphs.ToArray();
        Array.Sort(baseEntries, static (a, b) => a.baseGlyphId.CompareTo(b.baseGlyphId));

        // Deduplicate (keep last).
        int uniqueCount = 0;
        for (int i = 0; i < baseEntries.Length; i++)
        {
            if (uniqueCount != 0 && baseEntries[i].baseGlyphId == baseEntries[uniqueCount - 1].baseGlyphId)
            {
                baseEntries[uniqueCount - 1] = baseEntries[i];
                continue;
            }

            baseEntries[uniqueCount++] = baseEntries[i];
        }

        int baseGlyphCount = uniqueCount;

        // Collect PaintColrLayers nodes (to build LayerList and assign FirstLayerIndex).
        var layerNodes = new List<PaintColrLayersV1>();
        var visitedForLayerNodes = new HashSet<PaintV1>(ReferenceEqualityComparer.Instance);
        for (int i = 0; i < baseGlyphCount; i++)
            CollectLayerNodes(baseEntries[i].paint, layerNodes, visitedForLayerNodes);

        var layerPaints = new List<PaintV1>();
        for (int i = 0; i < layerNodes.Count; i++)
        {
            var n = layerNodes[i];
            n.FirstLayerIndex = checked((uint)layerPaints.Count);

            var layers = n.Layers;
            for (int j = 0; j < layers.Length; j++)
                layerPaints.Add(layers[j]);
        }

        // Collect unique paints in post-order so child offsets are known.
        var ordered = new List<PaintV1>(baseGlyphCount + layerPaints.Count + 16);
        var visited = new HashSet<PaintV1>(ReferenceEqualityComparer.Instance);

        for (int i = 0; i < baseGlyphCount; i++)
            VisitPostOrder(baseEntries[i].paint, visited, ordered);
        for (int i = 0; i < layerPaints.Count; i++)
            VisitPostOrder(layerPaints[i], visited, ordered);

        bool usesVarPaint = false;
        for (int i = 0; i < ordered.Count; i++)
        {
            byte fmt = ordered[i].Format;
            if (fmt is 3 or 5 or 7 or 9 or 13 or 15 or 17 or 19 or 21 or 23 or 25 or 27 or 29 or 31)
            {
                usesVarPaint = true;
                break;
            }
        }

        if (usesVarPaint)
        {
            if (_v1VarIndexMap.IsEmpty)
                throw new InvalidOperationException("COLR v1 uses variable paint formats but VarIndexMap bytes are missing. Call SetVarIndexMapData(...).");
            if (_v1ItemVariationStore.IsEmpty)
                throw new InvalidOperationException("COLR v1 uses variable paint formats but ItemVariationStore bytes are missing. Call SetItemVariationStoreData(...) or SetMinimalItemVariationStore(...).");
        }

        const int headerLen = 34;

        int baseGlyphListOffset = headerLen;
        int baseGlyphListLen = checked(4 + (baseGlyphCount * 6));

        int layerListOffset = checked(baseGlyphListOffset + baseGlyphListLen);
        int layerListLen = checked(4 + (layerPaints.Count * 4));

        int paintOffset = checked(layerListOffset + layerListLen);
        paintOffset = Align2(paintOffset);

        // Assign absolute offsets to paints.
        // PaintGlyph offsets are Offset24 relative to the PaintGlyph record and are treated as unsigned, so
        // referenced paints must appear *after* the referencing record. We therefore lay out paints in
        // reverse post-order (roots first, children later).
        var absOffsets = new Dictionary<PaintV1, int>(ordered.Count, ReferenceEqualityComparer.Instance);
        int pos = paintOffset;
        for (int i = ordered.Count - 1; i >= 0; i--)
        {
            var paint = ordered[i];
            absOffsets.Add(paint, pos);
            pos = checked(pos + paint.ByteLength);
        }

        // ClipList (optional)
        var clipEntries = _v1Clips.ToArray();
        Array.Sort(clipEntries, static (a, b) =>
        {
            int c = a.startGlyphId.CompareTo(b.startGlyphId);
            return c != 0 ? c : a.endGlyphId.CompareTo(b.endGlyphId);
        });

        // Deduplicate exact ranges (keep last).
        int clipCount = 0;
        for (int i = 0; i < clipEntries.Length; i++)
        {
            if (clipCount != 0 &&
                clipEntries[i].startGlyphId == clipEntries[clipCount - 1].startGlyphId &&
                clipEntries[i].endGlyphId == clipEntries[clipCount - 1].endGlyphId)
            {
                clipEntries[clipCount - 1] = clipEntries[i];
                continue;
            }

            clipEntries[clipCount++] = clipEntries[i];
        }

        int clipListOffset = 0;
        int clipListLen = 0;
        if (clipCount != 0)
        {
            // Validate sorted, non-overlapping ranges.
            ushort prevEnd = clipEntries[0].endGlyphId;
            if (prevEnd < clipEntries[0].startGlyphId)
                throw new InvalidOperationException("COLR ClipList ranges must satisfy startGlyphId <= endGlyphId.");

            for (int i = 1; i < clipCount; i++)
            {
                var e = clipEntries[i];
                if (e.endGlyphId < e.startGlyphId)
                    throw new InvalidOperationException("COLR ClipList ranges must satisfy startGlyphId <= endGlyphId.");

                if (e.startGlyphId <= prevEnd)
                    throw new InvalidOperationException("COLR ClipList ranges must be sorted and non-overlapping.");

                prevEnd = e.endGlyphId;
            }

            clipListOffset = pos;
            // ClipList format 1: header(5) + Clip records(7 each) + ClipBox format 1 (9 each)
            clipListLen = checked(5 + (clipCount * 7) + (clipCount * 9));
        }

        int end = clipListLen == 0 ? pos : checked(pos + clipListLen);

        int varIndexMapOffset = 0;
        if (!_v1VarIndexMap.IsEmpty)
        {
            end = Align4(end);
            varIndexMapOffset = end;
            end = checked(end + _v1VarIndexMap.Length);
        }

        int itemVariationStoreOffset = 0;
        if (!_v1ItemVariationStore.IsEmpty)
        {
            end = Align4(end);
            itemVariationStoreOffset = end;
            end = checked(end + _v1ItemVariationStore.Length);
        }

        byte[] table = new byte[end];
        var span = table.AsSpan();

        // Header (v1), with optional v0 fields set to 0.
        BigEndian.WriteUInt16(span, 0, 1); // version
        BigEndian.WriteUInt16(span, 2, 0); // numBaseGlyphRecords (v0)
        BigEndian.WriteUInt32(span, 4, 0u); // baseGlyphRecordsOffset (v0)
        BigEndian.WriteUInt32(span, 8, 0u); // layerRecordsOffset (v0)
        BigEndian.WriteUInt16(span, 12, 0); // numLayerRecords (v0)

        BigEndian.WriteUInt32(span, 14, checked((uint)baseGlyphListOffset));
        BigEndian.WriteUInt32(span, 18, checked((uint)layerListOffset));
        BigEndian.WriteUInt32(span, 22, clipListOffset == 0 ? 0u : checked((uint)clipListOffset)); // clipListOffset
        BigEndian.WriteUInt32(span, 26, varIndexMapOffset == 0 ? 0u : checked((uint)varIndexMapOffset)); // varIndexMapOffset
        BigEndian.WriteUInt32(span, 30, itemVariationStoreOffset == 0 ? 0u : checked((uint)itemVariationStoreOffset)); // itemVariationStoreOffset

        // BaseGlyphList
        BigEndian.WriteUInt32(span, baseGlyphListOffset + 0, checked((uint)baseGlyphCount));
        int bpos = baseGlyphListOffset + 4;
        for (int i = 0; i < baseGlyphCount; i++)
        {
            var e = baseEntries[i];
            BigEndian.WriteUInt16(span, bpos + 0, e.baseGlyphId);

            int paintAbs = absOffsets[e.paint];
            uint rel = checked((uint)(paintAbs - baseGlyphListOffset));
            BigEndian.WriteUInt32(span, bpos + 2, rel);
            bpos += 6;
        }

        // LayerList
        BigEndian.WriteUInt32(span, layerListOffset + 0, checked((uint)layerPaints.Count));
        int lpos = layerListOffset + 4;
        for (int i = 0; i < layerPaints.Count; i++)
        {
            int paintAbs = absOffsets[layerPaints[i]];
            uint rel = checked((uint)(paintAbs - layerListOffset));
            BigEndian.WriteUInt32(span, lpos, rel);
            lpos += 4;
        }

        // Paint records
        for (int i = 0; i < ordered.Count; i++)
        {
            var paint = ordered[i];
            int abs = absOffsets[paint];
            paint.Write(span.Slice(abs, paint.ByteLength), abs, absOffsets);
        }

        if (clipListOffset != 0)
            WriteClipList(span, clipListOffset, clipEntries, clipCount);

        if (varIndexMapOffset != 0)
            _v1VarIndexMap.Span.CopyTo(span.Slice(varIndexMapOffset, _v1VarIndexMap.Length));

        if (itemVariationStoreOffset != 0)
            _v1ItemVariationStore.Span.CopyTo(span.Slice(itemVariationStoreOffset, _v1ItemVariationStore.Length));

        return table;
    }

    private static void WriteClipList(Span<byte> destination, int clipListOffset, (ushort startGlyphId, ushort endGlyphId, ClipBoxV1 box)[] entries, int count)
    {
        destination[clipListOffset + 0] = 1; // format
        BigEndian.WriteUInt32(destination, clipListOffset + 1, checked((uint)count));

        int recordBase = clipListOffset + 5;
        int boxCursor = checked(recordBase + (count * 7));

        for (int i = 0; i < count; i++)
        {
            var e = entries[i];
            int recordOffset = recordBase + (i * 7);

            BigEndian.WriteUInt16(destination, recordOffset + 0, e.startGlyphId);
            BigEndian.WriteUInt16(destination, recordOffset + 2, e.endGlyphId);

            int rel = checked(boxCursor - clipListOffset);
            if ((uint)rel > 0x00FFFFFFu)
                throw new InvalidOperationException("COLR Clip clipBoxOffset must fit in Offset24.");

            destination[recordOffset + 4] = (byte)(rel >> 16);
            destination[recordOffset + 5] = (byte)(rel >> 8);
            destination[recordOffset + 6] = (byte)rel;

            // ClipBox format 1
            destination[boxCursor + 0] = 1;
            BigEndian.WriteInt16(destination, boxCursor + 1, e.box.XMin);
            BigEndian.WriteInt16(destination, boxCursor + 3, e.box.YMin);
            BigEndian.WriteInt16(destination, boxCursor + 5, e.box.XMax);
            BigEndian.WriteInt16(destination, boxCursor + 7, e.box.YMax);

            boxCursor = checked(boxCursor + 9);
        }
    }

    private static int Align4(int value) => (value + 3) & ~3;

    private static void CollectLayerNodes(PaintV1 paint, List<PaintColrLayersV1> nodes, HashSet<PaintV1> visited)
    {
        if (!visited.Add(paint))
            return;

        if (paint is PaintColrLayersV1 layers)
            nodes.Add(layers);

        int childCount = paint.ChildCount;
        for (int i = 0; i < childCount; i++)
            CollectLayerNodes(paint.GetChild(i), nodes, visited);
    }

    private static void VisitPostOrder(PaintV1 paint, HashSet<PaintV1> visited, List<PaintV1> ordered)
    {
        if (!visited.Add(paint))
            return;

        int childCount = paint.ChildCount;
        for (int i = 0; i < childCount; i++)
            VisitPostOrder(paint.GetChild(i), visited, ordered);

        ordered.Add(paint);
    }

    private static int Align2(int offset) => (offset + 1) & ~1;
}
