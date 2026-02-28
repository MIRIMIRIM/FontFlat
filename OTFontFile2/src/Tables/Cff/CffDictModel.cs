using System.Buffers;
using System.Text;

namespace OTFontFile2.Tables;

internal sealed class CffDictModel
{
    private readonly List<CffDictEntry> _entries = new();

    public IReadOnlyList<CffDictEntry> Entries => _entries;

    public void Clear() => _entries.Clear();

    public void Add(CffDictEntry entry) => _entries.Add(entry);

    public bool RemoveOperator(ushort op)
    {
        bool removed = false;
        for (int i = _entries.Count - 1; i >= 0; i--)
        {
            if (_entries[i].Operator == op)
            {
                _entries.RemoveAt(i);
                removed = true;
            }
        }
        return removed;
    }

    public void SetInt(ushort op, int value)
        => SetOperands(op, new[] { CffDictOperand.Integer(value) });

    public void SetInt2(ushort op, int v0, int v1)
        => SetOperands(op, new[] { CffDictOperand.Integer(v0), CffDictOperand.Integer(v1) });

    public void SetOperands(ushort op, CffDictOperand[] operands)
    {
        for (int i = 0; i < _entries.Count; i++)
        {
            if (_entries[i].Operator == op)
            {
                _entries[i] = new CffDictEntry(op, operands);
                return;
            }
        }

        _entries.Add(new CffDictEntry(op, operands));
    }

    public byte[] BuildDeterministic()
    {
        if (_entries.Count == 0)
            return Array.Empty<byte>();

        var list = new List<CffDictEntry>(_entries);
        list.Sort(static (a, b) => a.Operator.CompareTo(b.Operator));

        var w = new ArrayBufferWriter<byte>(256);
        for (int i = 0; i < list.Count; i++)
        {
            var e = list[i];

            var ops = e.Operands;
            for (int j = 0; j < ops.Length; j++)
                WriteOperand(ref w, ops[j]);

            WriteOperator(ref w, e.Operator);
        }

        return w.WrittenSpan.ToArray();
    }

    public static bool TryParse(ReadOnlySpan<byte> dictData, out CffDictModel model)
    {
        model = null!;

        var m = new CffDictModel();
        var operands = new List<CffDictOperand>(8);

        int pos = 0;
        while ((uint)pos < (uint)dictData.Length)
        {
            byte b0 = dictData[pos];

            if (IsOperatorByte(b0))
            {
                ushort op;
                if (b0 == 12)
                {
                    if ((uint)pos >= (uint)dictData.Length - 1)
                        return false;
                    op = (ushort)(0x0C00 | dictData[pos + 1]);
                    pos += 2;
                }
                else
                {
                    op = b0;
                    pos++;
                }

                m.Add(new CffDictEntry(op, operands.ToArray()));
                operands.Clear();
                continue;
            }

            if (!TryReadOperand(dictData, ref pos, out var operand))
                return false;

            operands.Add(operand);
        }

        if (operands.Count != 0)
            return false;

        model = m;
        return true;
    }

    private static bool IsOperatorByte(byte b0)
        => b0 <= 27 && b0 is not (28 or 29 or 30 or 255);

    private static bool TryReadOperand(ReadOnlySpan<byte> data, ref int pos, out CffDictOperand operand)
    {
        operand = default;

        if ((uint)pos >= (uint)data.Length)
            return false;

        byte b0 = data[pos];

        if (b0 is >= 32 and <= 246)
        {
            operand = CffDictOperand.Integer(b0 - 139);
            pos++;
            return true;
        }

        if (b0 is >= 247 and <= 250)
        {
            if ((uint)pos > (uint)data.Length - 2)
                return false;
            int value = ((b0 - 247) * 256) + data[pos + 1] + 108;
            operand = CffDictOperand.Integer(value);
            pos += 2;
            return true;
        }

        if (b0 is >= 251 and <= 254)
        {
            if ((uint)pos > (uint)data.Length - 2)
                return false;
            int value = -(((b0 - 251) * 256) + data[pos + 1] + 108);
            operand = CffDictOperand.Integer(value);
            pos += 2;
            return true;
        }

        switch (b0)
        {
            case 28:
                if ((uint)pos > (uint)data.Length - 3)
                    return false;
                operand = CffDictOperand.Integer(BigEndian.ReadInt16(data, pos + 1));
                pos += 3;
                return true;

            case 29:
                if ((uint)pos > (uint)data.Length - 5)
                    return false;
                operand = CffDictOperand.Integer(BigEndian.ReadInt32(data, pos + 1));
                pos += 5;
                return true;

            case 30:
                return TryReadReal(data, ref pos, out operand);

            case 255:
                if ((uint)pos > (uint)data.Length - 5)
                    return false;
                operand = CffDictOperand.Fixed1616(BigEndian.ReadInt32(data, pos + 1));
                pos += 5;
                return true;

            default:
                return false;
        }
    }

    private static bool TryReadReal(ReadOnlySpan<byte> data, ref int pos, out CffDictOperand operand)
    {
        operand = default;

        if ((uint)pos >= (uint)data.Length || data[pos] != 30)
            return false;

        pos++;

        var sb = new StringBuilder(16);
        while ((uint)pos < (uint)data.Length)
        {
            byte b = data[pos++];
            int hi = (b >> 4) & 0xF;
            if (hi == 0xF)
            {
                operand = CffDictOperand.Real(sb.ToString());
                return true;
            }

            AppendRealNibble(sb, hi);

            int lo = b & 0xF;
            if (lo == 0xF)
            {
                operand = CffDictOperand.Real(sb.ToString());
                return true;
            }

            AppendRealNibble(sb, lo);
        }

        return false;
    }

    private static void AppendRealNibble(StringBuilder sb, int nibble)
    {
        if (nibble <= 9)
        {
            sb.Append((char)('0' + nibble));
            return;
        }

        switch (nibble)
        {
            case 0xA: sb.Append('.'); return;
            case 0xB: sb.Append('E'); return;
            case 0xC: sb.Append("E-"); return;
            case 0xE: sb.Append('-'); return;
            default: return;
        }
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

    private static void WriteOperand(ref ArrayBufferWriter<byte> w, CffDictOperand operand)
    {
        switch (operand.Kind)
        {
            case CffDictOperandKind.Integer:
                WriteInt(ref w, operand.Int32Value);
                return;

            case CffDictOperandKind.Fixed1616:
            {
                Span<byte> s = w.GetSpan(5);
                s[0] = 255;
                BigEndian.WriteUInt32(s, 1, unchecked((uint)operand.Int32Value));
                w.Advance(5);
                return;
            }

            case CffDictOperandKind.Real:
                WriteReal(ref w, operand.RealString);
                return;

            default:
                throw new InvalidOperationException("Unknown operand kind.");
        }
    }

    private static void WriteInt(ref ArrayBufferWriter<byte> w, int value)
    {
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

        Span<byte> t = w.GetSpan(5);
        t[0] = 29;
        BigEndian.WriteUInt32(t, 1, unchecked((uint)value));
        w.Advance(5);
    }

    private static void WriteReal(ref ArrayBufferWriter<byte> w, string real)
    {
        if (string.IsNullOrEmpty(real))
        {
            w.GetSpan(2)[0] = 30;
            w.GetSpan(2)[1] = 0xFF;
            w.Advance(2);
            return;
        }

        w.GetSpan(1)[0] = 30;
        w.Advance(1);

        int nibbleCount = 0;
        byte current = 0;

        for (int i = 0; i < real.Length; i++)
        {
            char ch = real[i];
            if (ch >= '0' && ch <= '9')
            {
                EmitNibble(ref w, ref nibbleCount, ref current, ch - '0');
                continue;
            }

            switch (ch)
            {
                case '.':
                    EmitNibble(ref w, ref nibbleCount, ref current, 0xA);
                    break;
                case 'E':
                case 'e':
                    if (i + 1 < real.Length && real[i + 1] == '-')
                    {
                        EmitNibble(ref w, ref nibbleCount, ref current, 0xC);
                        i++;
                    }
                    else
                    {
                        EmitNibble(ref w, ref nibbleCount, ref current, 0xB);
                    }
                    break;
                case '-':
                    EmitNibble(ref w, ref nibbleCount, ref current, 0xE);
                    break;
            }
        }

        EmitNibble(ref w, ref nibbleCount, ref current, 0xF);
        if ((nibbleCount & 1) != 0)
        {
            // Termination nibble written in high half; pad low with 0xF.
            current |= 0xF;
            w.GetSpan(1)[0] = current;
            w.Advance(1);
        }
    }

    private static void EmitNibble(ref ArrayBufferWriter<byte> w, ref int nibbleCount, ref byte current, int n)
    {
        if ((nibbleCount & 1) == 0)
        {
            current = (byte)(n << 4);
        }
        else
        {
            current |= (byte)n;
            w.GetSpan(1)[0] = current;
            w.Advance(1);
            current = 0;
        }

        nibbleCount++;
    }
}

internal readonly struct CffDictEntry
{
    public ushort Operator { get; }
    public CffDictOperand[] Operands { get; }

    public CffDictEntry(ushort op, CffDictOperand[] operands)
    {
        Operator = op;
        Operands = operands ?? Array.Empty<CffDictOperand>();
    }
}

internal enum CffDictOperandKind
{
    Integer,
    Real,
    Fixed1616,
}

internal readonly struct CffDictOperand
{
    public CffDictOperandKind Kind { get; }
    public int Int32Value { get; }
    public string RealString { get; }

    private CffDictOperand(CffDictOperandKind kind, int int32Value, string realString)
    {
        Kind = kind;
        Int32Value = int32Value;
        RealString = realString;
    }

    public static CffDictOperand Integer(int value) => new(CffDictOperandKind.Integer, value, "");
    public static CffDictOperand Fixed1616(int raw1616) => new(CffDictOperandKind.Fixed1616, raw1616, "");
    public static CffDictOperand Real(string value) => new(CffDictOperandKind.Real, 0, value ?? "");
}
