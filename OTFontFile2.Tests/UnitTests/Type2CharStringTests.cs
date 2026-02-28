using Microsoft.VisualStudio.TestTools.UnitTesting;
using OTFontFile2.Tables.Cff;

namespace OTFontFile2.Tests.UnitTests;

[TestClass]
public sealed class Type2CharStringTests
{
    [TestMethod]
    public void Type2_ParseAndEncode_RoundTripsTokens()
    {
        var p = new Type2CharStringProgram();
        p.Add(Type2Token.FromNumber(Type2Number.Integer(0)));
        p.Add(Type2Token.FromNumber(Type2Number.Integer(100)));
        p.Add(Type2Token.FromOperator(21)); // rmoveto
        p.Add(Type2Token.FromOperator(0x0C0C)); // 12 12 (div)
        p.Add(Type2Token.FromOperator(14)); // endchar

        byte[] bytes = p.ToBytes();
        Assert.IsTrue(Type2CharStringProgram.TryParse(bytes, out var parsed));

        Assert.AreEqual(p.Tokens.Count, parsed.Tokens.Count);
        for (int i = 0; i < p.Tokens.Count; i++)
        {
            Assert.AreEqual(p.Tokens[i].Kind, parsed.Tokens[i].Kind);
            if (p.Tokens[i].Kind == Type2TokenKind.Number)
                Assert.AreEqual(p.Tokens[i].Number.Value, parsed.Tokens[i].Number.Value);
            else
                Assert.AreEqual(p.Tokens[i].Operator, parsed.Tokens[i].Operator);
        }
    }

    [TestMethod]
    public void Type2_Subroutines_ExpandsCallGSubr()
    {
        // With 1 global subr, bias is 107, so to call subr index 0 we push -107.
        byte[] gsubr0 = new byte[] { (byte)(42 + 139), 11 }; // 42, return
        var global = new TestSubrProvider(new[] { gsubr0 });

        // Base charstring: -107 callgsubr endchar
        byte[] cs = new byte[] { (byte)(-107 + 139), 29, 14 };

        Assert.IsTrue(Type2Subroutines.TryExpand(cs, global, new EmptySubrProvider(), maxDepth: 8, out var expanded));

        // Expect: 42 return is not included, only 42 then endchar.
        Assert.AreEqual(2, expanded.Tokens.Count);
        Assert.AreEqual(Type2TokenKind.Number, expanded.Tokens[0].Kind);
        Assert.AreEqual(42, expanded.Tokens[0].Number.Value);
        Assert.AreEqual(Type2TokenKind.Operator, expanded.Tokens[1].Kind);
        Assert.AreEqual((ushort)14, expanded.Tokens[1].Operator);
    }

    [TestMethod]
    public void Type2_Subroutines_ExpandsCallSubr()
    {
        // With 1 local subr, bias is 107, so to call subr index 0 we push -107.
        byte[] subr0 = new byte[] { (byte)(7 + 139), 11 }; // 7, return
        var local = new TestSubrProvider(new[] { subr0 });

        // Base charstring: -107 callsubr endchar
        byte[] cs = new byte[] { (byte)(-107 + 139), 10, 14 };

        Assert.IsTrue(Type2Subroutines.TryExpand(cs, new EmptySubrProvider(), local, maxDepth: 8, out var expanded));

        Assert.AreEqual(2, expanded.Tokens.Count);
        Assert.AreEqual(Type2TokenKind.Number, expanded.Tokens[0].Kind);
        Assert.AreEqual(7, expanded.Tokens[0].Number.Value);
        Assert.AreEqual(Type2TokenKind.Operator, expanded.Tokens[1].Kind);
        Assert.AreEqual((ushort)14, expanded.Tokens[1].Operator);
    }

    private readonly struct TestSubrProvider : IType2SubrProvider
    {
        private readonly byte[][] _subrs;
        public TestSubrProvider(byte[][] subrs) => _subrs = subrs;
        public int Count => _subrs.Length;
        public bool TryGetSubrBytes(int index, out ReadOnlySpan<byte> bytes)
        {
            if ((uint)index >= (uint)_subrs.Length)
            {
                bytes = default;
                return false;
            }

            bytes = _subrs[index];
            return true;
        }
    }
}
