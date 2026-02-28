using System.Buffers;

namespace OTFontFile2.Tables.Glyf;

public sealed class GlyfCompositeGlyphBuilder
{
    private readonly List<Component> _components = new();
    private byte[] _instructions = Array.Empty<byte>();
    private short _xMin;
    private short _yMin;
    private short _xMax;
    private short _yMax;
    private bool _bboxSet;

    public int ComponentCount => _components.Count;
    public ReadOnlySpan<byte> Instructions => _instructions;

    public void SetBoundingBox(short xMin, short yMin, short xMax, short yMax)
    {
        _xMin = xMin;
        _yMin = yMin;
        _xMax = xMax;
        _yMax = yMax;
        _bboxSet = true;
    }

    public void ClearComponents() => _components.Clear();

    public void AddComponent(ushort glyphIndex, short dx, short dy, F2Dot14 a, F2Dot14 b, F2Dot14 c, F2Dot14 d)
        => _components.Add(Component.CreateTranslate(glyphIndex, dx, dy, a, b, c, d));

    public void AddComponentByMatchingPoints(ushort glyphIndex, ushort parentPoint, ushort childPoint, F2Dot14 a, F2Dot14 b, F2Dot14 c, F2Dot14 d)
        => _components.Add(Component.CreateMatchPoints(glyphIndex, parentPoint, childPoint, a, b, c, d));

    public void SetInstructions(ReadOnlySpan<byte> instructions)
    {
        if (instructions.Length > ushort.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(instructions));

        _instructions = instructions.ToArray();
    }

    public static bool TryFrom(ReadOnlySpan<byte> glyphData, out GlyfCompositeGlyphBuilder builder)
    {
        builder = null!;

        if (!GlyfTable.TryReadGlyphHeader(glyphData, out var header))
            return false;

        if (!header.IsComposite)
            return false;

        if (!GlyfTable.TryCreateCompositeGlyphComponentEnumerator(glyphData, out var e))
            return false;

        if (!GlyfTable.TryGetCompositeGlyphInstructions(glyphData, out var instr))
            return false;

        var b = new GlyfCompositeGlyphBuilder();
        b.SetBoundingBox(header.XMin, header.YMin, header.XMax, header.YMax);
        if (!instr.IsEmpty)
            b.SetInstructions(instr);

        while (e.MoveNext())
        {
            var c = e.Current;

            if (c.TryGetTranslation(out short dx, out short dy))
            {
                b.AddComponent(c.GlyphIndex, dx, dy, c.A, c.B, c.C, c.D);
            }
            else if (c.TryGetMatchingPoints(out ushort parentPoint, out ushort childPoint))
            {
                b.AddComponentByMatchingPoints(c.GlyphIndex, parentPoint, childPoint, c.A, c.B, c.C, c.D);
            }
            else
            {
                return false;
            }
        }

        if (!e.IsValid)
            return false;

        if (b.ComponentCount == 0)
            return false;

        builder = b;
        return true;
    }

    public byte[] Build()
    {
        if (_components.Count == 0)
            throw new InvalidOperationException("Composite glyph must have at least one component.");

        var w = new ArrayBufferWriter<byte>(_components.Count * 16);

        for (int i = 0; i < _components.Count; i++)
        {
            bool isLast = i == _components.Count - 1;
            var c = _components[i];

            ushort flags = 0;
            if (!isLast)
                flags |= 0x0020; // MORE_COMPONENTS

            if (_instructions.Length != 0 && isLast)
                flags |= 0x0100; // WE_HAVE_INSTRUCTIONS

            bool argsAreWords;
            if (c.ArgsAreXYValues)
            {
                flags |= 0x0002; // ARGS_ARE_XY_VALUES
                argsAreWords = c.Dx < sbyte.MinValue || c.Dx > sbyte.MaxValue || c.Dy < sbyte.MinValue || c.Dy > sbyte.MaxValue;
            }
            else
            {
                argsAreWords = c.ParentPoint > byte.MaxValue || c.ChildPoint > byte.MaxValue;
            }

            if (argsAreWords)
                flags |= 0x0001; // ARG_1_AND_2_ARE_WORDS

            flags |= ComputeTransformFlags(c.A, c.B, c.C, c.D, out TransformKind tk);

            Span<byte> header = w.GetSpan(4);
            BigEndian.WriteUInt16(header, 0, flags);
            BigEndian.WriteUInt16(header, 2, c.GlyphIndex);
            w.Advance(4);

            if (argsAreWords)
            {
                Span<byte> args = w.GetSpan(4);
                if (c.ArgsAreXYValues)
                {
                    BigEndian.WriteInt16(args, 0, c.Dx);
                    BigEndian.WriteInt16(args, 2, c.Dy);
                }
                else
                {
                    BigEndian.WriteUInt16(args, 0, c.ParentPoint);
                    BigEndian.WriteUInt16(args, 2, c.ChildPoint);
                }
                w.Advance(4);
            }
            else
            {
                Span<byte> args = w.GetSpan(2);
                if (c.ArgsAreXYValues)
                {
                    args[0] = unchecked((byte)(sbyte)c.Dx);
                    args[1] = unchecked((byte)(sbyte)c.Dy);
                }
                else
                {
                    args[0] = (byte)c.ParentPoint;
                    args[1] = (byte)c.ChildPoint;
                }
                w.Advance(2);
            }

            WriteTransform(w, tk, c.A, c.B, c.C, c.D);
        }

        if (_instructions.Length != 0)
        {
            Span<byte> il = w.GetSpan(2);
            BigEndian.WriteUInt16(il, 0, (ushort)_instructions.Length);
            w.Advance(2);

            _instructions.CopyTo(w.GetSpan(_instructions.Length));
            w.Advance(_instructions.Length);
        }

        short xMin = _bboxSet ? _xMin : (short)0;
        short yMin = _bboxSet ? _yMin : (short)0;
        short xMax = _bboxSet ? _xMax : (short)0;
        short yMax = _bboxSet ? _yMax : (short)0;

        int length = checked(10 + w.WrittenCount);
        byte[] data = new byte[length];
        var span = data.AsSpan();

        BigEndian.WriteInt16(span, 0, -1);
        BigEndian.WriteInt16(span, 2, xMin);
        BigEndian.WriteInt16(span, 4, yMin);
        BigEndian.WriteInt16(span, 6, xMax);
        BigEndian.WriteInt16(span, 8, yMax);

        w.WrittenSpan.CopyTo(span.Slice(10));
        return data;
    }

    private static ushort ComputeTransformFlags(F2Dot14 a, F2Dot14 b, F2Dot14 c, F2Dot14 d, out TransformKind kind)
    {
        bool b0 = b.RawValue == 0;
        bool c0 = c.RawValue == 0;

        if (b0 && c0)
        {
            if (a.RawValue == d.RawValue)
            {
                if (a.RawValue == 0x4000) // 1.0
                {
                    kind = TransformKind.None;
                    return 0;
                }

                kind = TransformKind.UniformScale;
                return 0x0008; // WE_HAVE_A_SCALE
            }

            kind = TransformKind.XYScale;
            return 0x0040; // WE_HAVE_AN_X_AND_Y_SCALE
        }

        kind = TransformKind.Matrix2x2;
        return 0x0080; // WE_HAVE_A_TWO_BY_TWO
    }

    private static void WriteTransform(ArrayBufferWriter<byte> w, TransformKind kind, F2Dot14 a, F2Dot14 b, F2Dot14 c, F2Dot14 d)
    {
        if (kind == TransformKind.None)
            return;

        if (kind == TransformKind.UniformScale)
        {
            Span<byte> s = w.GetSpan(2);
            BigEndian.WriteInt16(s, 0, a.RawValue);
            w.Advance(2);
            return;
        }

        if (kind == TransformKind.XYScale)
        {
            Span<byte> s = w.GetSpan(4);
            BigEndian.WriteInt16(s, 0, a.RawValue);
            BigEndian.WriteInt16(s, 2, d.RawValue);
            w.Advance(4);
            return;
        }

        Span<byte> m = w.GetSpan(8);
        BigEndian.WriteInt16(m, 0, a.RawValue);
        BigEndian.WriteInt16(m, 2, b.RawValue);
        BigEndian.WriteInt16(m, 4, c.RawValue);
        BigEndian.WriteInt16(m, 6, d.RawValue);
        w.Advance(8);
    }

    private enum TransformKind
    {
        None,
        UniformScale,
        XYScale,
        Matrix2x2
    }

    private readonly struct Component
    {
        public ushort GlyphIndex { get; }
        public bool ArgsAreXYValues { get; }
        public short Dx { get; }
        public short Dy { get; }
        public ushort ParentPoint { get; }
        public ushort ChildPoint { get; }
        public F2Dot14 A { get; }
        public F2Dot14 B { get; }
        public F2Dot14 C { get; }
        public F2Dot14 D { get; }

        private Component(
            ushort glyphIndex,
            bool argsAreXYValues,
            short dx,
            short dy,
            ushort parentPoint,
            ushort childPoint,
            F2Dot14 a,
            F2Dot14 b,
            F2Dot14 c,
            F2Dot14 d)
        {
            GlyphIndex = glyphIndex;
            ArgsAreXYValues = argsAreXYValues;
            Dx = dx;
            Dy = dy;
            ParentPoint = parentPoint;
            ChildPoint = childPoint;
            A = a;
            B = b;
            C = c;
            D = d;
        }

        public static Component CreateTranslate(ushort glyphIndex, short dx, short dy, F2Dot14 a, F2Dot14 b, F2Dot14 c, F2Dot14 d)
            => new(glyphIndex, argsAreXYValues: true, dx, dy, parentPoint: 0, childPoint: 0, a, b, c, d);

        public static Component CreateMatchPoints(ushort glyphIndex, ushort parentPoint, ushort childPoint, F2Dot14 a, F2Dot14 b, F2Dot14 c, F2Dot14 d)
            => new(glyphIndex, argsAreXYValues: false, dx: 0, dy: 0, parentPoint, childPoint, a, b, c, d);
    }
}
