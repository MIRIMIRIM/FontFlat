namespace OTFontFile2.Tables.Cff;

public static class Type2Subroutines
{
    public static int ComputeBias(int subrCount)
    {
        if (subrCount < 1240) return 107;
        if (subrCount < 33900) return 1131;
        return 32768;
    }

    public static bool TryExpand(
        ReadOnlySpan<byte> charString,
        IType2SubrProvider globalSubrs,
        IType2SubrProvider localSubrs,
        int maxDepth,
        out Type2CharStringProgram program)
    {
        program = null!;

        if (maxDepth < 0)
            return false;

        if (!Type2CharStringProgram.TryParse(charString, out var baseProgram))
            return false;

        var outTokens = new List<Type2Token>(baseProgram.Tokens.Count);
        int globalBias = ComputeBias(globalSubrs.Count);
        int localBias = ComputeBias(localSubrs.Count);

        if (!TryExpandTokens(baseProgram.Tokens, globalSubrs, localSubrs, globalBias, localBias, depth: 0, maxDepth, outTokens))
            return false;

        var outProgram = new Type2CharStringProgram();
        for (int i = 0; i < outTokens.Count; i++)
            outProgram.Add(outTokens[i]);

        program = outProgram;
        return true;
    }

    private static bool TryExpandTokens(
        IReadOnlyList<Type2Token> tokens,
        IType2SubrProvider globalSubrs,
        IType2SubrProvider localSubrs,
        int globalBias,
        int localBias,
        int depth,
        int maxDepth,
        List<Type2Token> output)
    {
        if (depth > maxDepth)
            return false;

        // Minimal stack tracking for callsubr/callgsubr operand extraction.
        Span<int> stack = stackalloc int[48];
        int sp = 0;

        for (int i = 0; i < tokens.Count; i++)
        {
            var t = tokens[i];

            if (t.Kind == Type2TokenKind.Number)
            {
                if (sp < stack.Length)
                    stack[sp++] = t.Number.Value;
                else
                    sp = stack.Length;

                output.Add(t);
                continue;
            }

            ushort op = t.Operator;
            if (op == 10) // callsubr
            {
                if (sp == 0)
                    return false;

                int subr = stack[sp - 1];
                sp--;

                // Remove the subr number operand token.
                if (output.Count == 0 || output[^1].Kind != Type2TokenKind.Number || output[^1].Number.Value != subr)
                    return false;
                output.RemoveAt(output.Count - 1);

                int index = subr + localBias;
                if ((uint)index >= (uint)localSubrs.Count)
                    return false;

                if (!localSubrs.TryGetSubrBytes(index, out var bytes))
                    return false;

                if (!Type2CharStringProgram.TryParse(bytes, out var subrProgram))
                    return false;

                if (!TryExpandTokens(subrProgram.Tokens, globalSubrs, localSubrs, globalBias, localBias, depth + 1, maxDepth, output))
                    return false;

                continue;
            }

            if (op == 29) // callgsubr
            {
                if (sp == 0)
                    return false;

                int subr = stack[sp - 1];
                sp--;

                if (output.Count == 0 || output[^1].Kind != Type2TokenKind.Number || output[^1].Number.Value != subr)
                    return false;
                output.RemoveAt(output.Count - 1);

                int index = subr + globalBias;
                if ((uint)index >= (uint)globalSubrs.Count)
                    return false;

                if (!globalSubrs.TryGetSubrBytes(index, out var bytes))
                    return false;

                if (!Type2CharStringProgram.TryParse(bytes, out var subrProgram))
                    return false;

                if (!TryExpandTokens(subrProgram.Tokens, globalSubrs, localSubrs, globalBias, localBias, depth + 1, maxDepth, output))
                    return false;

                continue;
            }

            // Return: end current expansion frame.
            if (op == 11)
                return true;

            // Operators clear the operand stack (Type2 semantics); keep stack small.
            sp = 0;

            output.Add(t);
        }

        return true;
    }
}

public interface IType2SubrProvider
{
    int Count { get; }
    bool TryGetSubrBytes(int index, out ReadOnlySpan<byte> bytes);
}
