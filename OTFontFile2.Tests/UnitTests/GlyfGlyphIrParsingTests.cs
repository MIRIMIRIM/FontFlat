using Microsoft.VisualStudio.TestTools.UnitTesting;
using OTFontFile2.Tables;
using OTFontFile2.Tables.Glyf;

namespace OTFontFile2.Tests.UnitTests;

[TestClass]
public sealed class GlyfGlyphIrParsingTests
{
    [TestMethod]
    public void Glyf_SimpleGlyphBuilder_CanParseFromBytes()
    {
        var b = new GlyfSimpleGlyphBuilder();
        b.SetContours(
            endPointsOfContours: new ushort[] { 2 },
            points: new GlyfGlyphPoint[]
            {
                new(0, 0, onCurve: true),
                new(50, 0, onCurve: true),
                new(50, 50, onCurve: true),
            });
        b.SetInstructions(new byte[] { 0xAA, 0xBB });

        byte[] bytes = b.Build();

        Assert.IsTrue(GlyfSimpleGlyphBuilder.TryFrom(bytes, out var parsed));
        Assert.AreEqual(1, parsed.EndPointsOfContours.Length);
        Assert.AreEqual((ushort)2, parsed.EndPointsOfContours[0]);
        Assert.AreEqual(3, parsed.Points.Length);
        Assert.AreEqual(2, parsed.Instructions.Length);
        Assert.AreEqual((byte)0xAA, parsed.Instructions[0]);
        Assert.AreEqual((byte)0xBB, parsed.Instructions[1]);

        byte[] rebuilt = parsed.Build();
        Assert.IsTrue(GlyfTable.TryCreateSimpleGlyphPointEnumerator(rebuilt, out var e));
        Assert.AreEqual((ushort)3, e.PointCount);
    }

    [TestMethod]
    public void Glyf_CompositeGlyphBuilder_CanParseFromBytes()
    {
        var b = new GlyfCompositeGlyphBuilder();
        b.SetBoundingBox(0, 0, 50, 50);
        b.AddComponent(glyphIndex: 0, dx: -3, dy: 7, a: new F2Dot14(0x4000), b: default, c: default, d: new F2Dot14(0x4000));
        b.AddComponentByMatchingPoints(glyphIndex: 1, parentPoint: 10, childPoint: 11, a: new F2Dot14(0x4000), b: new F2Dot14(0x2000), c: new F2Dot14(unchecked((short)0xE000)), d: new F2Dot14(0x4000));
        b.SetInstructions(new byte[] { 1, 2, 3 });

        byte[] bytes = b.Build();

        Assert.IsTrue(GlyfCompositeGlyphBuilder.TryFrom(bytes, out var parsed));
        Assert.AreEqual(2, parsed.ComponentCount);
        Assert.AreEqual(3, parsed.Instructions.Length);

        byte[] rebuilt = parsed.Build();
        Assert.IsTrue(GlyfTable.TryReadGlyphHeader(rebuilt, out var h));
        Assert.IsTrue(h.IsComposite);

        Assert.IsTrue(GlyfTable.TryCreateCompositeGlyphComponentEnumerator(rebuilt, out var e));
        Assert.IsTrue(e.MoveNext());
        Assert.IsTrue(e.Current.TryGetTranslation(out short dx, out short dy));
        Assert.AreEqual((short)-3, dx);
        Assert.AreEqual((short)7, dy);

        Assert.IsTrue(e.MoveNext());
        Assert.IsTrue(e.Current.TryGetMatchingPoints(out ushort parent, out ushort child));
        Assert.AreEqual((ushort)10, parent);
        Assert.AreEqual((ushort)11, child);

        Assert.IsFalse(e.MoveNext());
        Assert.IsTrue(e.IsValid);
    }
}

