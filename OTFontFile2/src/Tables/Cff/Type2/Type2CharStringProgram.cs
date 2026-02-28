using System.Buffers;

namespace OTFontFile2.Tables.Cff;

public sealed class Type2CharStringProgram
{
    private readonly List<Type2Token> _tokens = new();

    public IReadOnlyList<Type2Token> Tokens => _tokens;

    public void Clear() => _tokens.Clear();

    public void Add(Type2Token token) => _tokens.Add(token);

    public byte[] ToBytes()
    {
        var w = new ArrayBufferWriter<byte>(_tokens.Count * 2);
        for (int i = 0; i < _tokens.Count; i++)
        {
            var t = _tokens[i];
            if (t.Kind == Type2TokenKind.Number)
            {
                WriteNumber(ref w, t.Number);
            }
            else
            {
                WriteOperator(ref w, t.Operator);
            }
        }

        return w.WrittenSpan.ToArray();
    }

    public static bool TryParse(ReadOnlySpan<byte> data, out Type2CharStringProgram program)
    {
        program = null!;

        var p = new Type2CharStringProgram();
        int pos = 0;
        while ((uint)pos < (uint)data.Length)
        {
            byte b0 = data[pos];

            // Numbers.
            if (b0 >= 32)
            {
                if (!TryReadNumber(data, ref pos, out var num))
                    return false;
                p.Add(Type2Token.FromNumber(num));
                continue;
            }

            if (b0 == 28 || b0 == 255)
            {
                if (!TryReadNumber(data, ref pos, out var num))
                    return false;
                p.Add(Type2Token.FromNumber(num));
                continue;
            }

            // Operators.
            if (b0 == 12)
            {
                if ((uint)pos >= (uint)data.Length - 1)
                    return false;
                ushort op = (ushort)(0x0C00 | data[pos + 1]);
                p.Add(Type2Token.FromOperator(op));
                pos += 2;
                continue;
            }

            p.Add(Type2Token.FromOperator(b0));
            pos++;
        }

        program = p;
        return true;
    }

    private static bool TryReadNumber(ReadOnlySpan<byte> data, ref int pos, out Type2Number number)
    {
        number = default;

        if ((uint)pos >= (uint)data.Length)
            return false;

        byte b0 = data[pos];

        if (b0 is >= 32 and <= 246)
        {
            number = Type2Number.Integer(b0 - 139);
            pos++;
            return true;
        }

        if (b0 is >= 247 and <= 250)
        {
            if ((uint)pos > (uint)data.Length - 2)
                return false;
            int value = ((b0 - 247) * 256) + data[pos + 1] + 108;
            number = Type2Number.Integer(value);
            pos += 2;
            return true;
        }

        if (b0 is >= 251 and <= 254)
        {
            if ((uint)pos > (uint)data.Length - 2)
                return false;
            int value = -(((b0 - 251) * 256) + data[pos + 1] + 108);
            number = Type2Number.Integer(value);
            pos += 2;
            return true;
        }

        if (b0 == 28)
        {
            if ((uint)pos > (uint)data.Length - 3)
                return false;
            number = Type2Number.Integer(BigEndian.ReadInt16(data, pos + 1));
            pos += 3;
            return true;
        }

        if (b0 == 255)
        {
            if ((uint)pos > (uint)data.Length - 5)
                return false;
            number = Type2Number.Fixed1616(BigEndian.ReadInt32(data, pos + 1));
            pos += 5;
            return true;
        }

        return false;
    }

    private static void WriteOperator(ref ArrayBufferWriter<byte> w, ushort op)
    {
        if (op <= 0xFF)
        {
            w.GetSpan(1)[0] = (byte)op;
            w.Advance(1);
            return;
        }

        Span<byte> s = w.GetSpan(2);
        s[0] = 12;
        s[1] = (byte)(op & 0xFF);
        w.Advance(2);
    }

    private static void WriteNumber(ref ArrayBufferWriter<byte> w, Type2Number n)
    {
        if (n.Kind == Type2NumberKind.Fixed1616)
        {
            Span<byte> s = w.GetSpan(5);
            s[0] = 255;
            BigEndian.WriteUInt32(s, 1, unchecked((uint)n.Value));
            w.Advance(5);
            return;
        }

        int value = n.Value;

        if (value >= -107 && value <= 107)
        {
            w.GetSpan(1)[0] = (byte)(value + 139);
            w.Advance(1);
            return;
        }

        if (value >= 108 && value <= 1131)
        {
            int v = value - 108;
            Span<byte> s = w.GetSpan(2);
            s[0] = (byte)(247 + (v >> 8));
            s[1] = (byte)v;
            w.Advance(2);
            return;
        }

        if (value <= -108 && value >= -1131)
        {
            int v = -value - 108;
            Span<byte> s = w.GetSpan(2);
            s[0] = (byte)(251 + (v >> 8));
            s[1] = (byte)v;
            w.Advance(2);
            return;
        }

        if (value >= short.MinValue && value <= short.MaxValue)
        {
            Span<byte> s = w.GetSpan(3);
            s[0] = 28;
            BigEndian.WriteInt16(s, 1, (short)value);
            w.Advance(3);
            return;
        }

        // Type2 does not define the DICT 29 longint operand form; for safety, clamp to int16.
        Span<byte> t = w.GetSpan(3);
        t[0] = 28;
        BigEndian.WriteInt16(t, 1, value < short.MinValue ? short.MinValue : value > short.MaxValue ? short.MaxValue : (short)value);
        w.Advance(3);
    }
}

public enum Type2TokenKind
{
    Number,
    Operator,
}

public readonly struct Type2Token
{
    public Type2TokenKind Kind { get; }
    public Type2Number Number { get; }
    public ushort Operator { get; }

    private Type2Token(Type2TokenKind kind, Type2Number number, ushort op)
    {
        Kind = kind;
        Number = number;
        Operator = op;
    }

    public static Type2Token FromNumber(Type2Number number) => new(Type2TokenKind.Number, number, 0);
    public static Type2Token FromOperator(ushort op) => new(Type2TokenKind.Operator, default, op);
}

public enum Type2NumberKind
{
    Integer,
    Fixed1616,
}

public readonly struct Type2Number
{
    public Type2NumberKind Kind { get; }
    public int Value { get; }

    private Type2Number(Type2NumberKind kind, int value)
    {
        Kind = kind;
        Value = value;
    }

    public static Type2Number Integer(int value) => new(Type2NumberKind.Integer, value);
    public static Type2Number Fixed1616(int raw1616) => new(Type2NumberKind.Fixed1616, raw1616);
}
