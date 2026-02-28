using System.Buffers.Binary;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OTFontFile2.Tables;

namespace OTFontFile2.Tests.UnitTests;

[TestClass]
public sealed class ColrV1PaintTransformWritebackTests
{
    [TestMethod]
    public void ColrV1_CanWritePaintTransformAndVarTransform_AndByteLayoutMatchesSpec()
    {
        byte[] head = TestSfntTables.BuildValidHeadTable(unitsPerEm: 1000);

        var colrBuilder = new ColrTableBuilder();
        colrBuilder.ClearToVersion1();
        colrBuilder.SetVarIndexMapData(BuildDeltaSetIndexMapFormat0_EntrySize4_Inner16(new VarIdx(0, 0)));
        colrBuilder.SetMinimalItemVariationStore(axisCount: 0);

        var solid = ColrTableBuilder.Solid(paletteIndex: 1, alpha: new F2Dot14(0x4000));

        var t = new Affine2x3(
            xx: new Fixed1616(0x00010000u),
            yx: new Fixed1616(0x00000000u),
            xy: new Fixed1616(0x00000000u),
            yy: new Fixed1616(0x00010000u),
            dx: new Fixed1616(0x00020000u),
            dy: new Fixed1616(0xFFFF0000u)); // -1.0

        colrBuilder.SetBaseGlyphPaint(baseGlyphId: 1, paint: ColrTableBuilder.Transform(solid, t));
        colrBuilder.SetBaseGlyphPaint(baseGlyphId: 2, paint: ColrTableBuilder.VarTransform(solid, t, varIndexBase: 7));

        var sfnt = new SfntBuilder { SfntVersion = 0x00010000u };
        sfnt.SetTable(KnownTags.head, head);
        sfnt.SetTable(colrBuilder);

        using var file = SfntFile.FromMemory(sfnt.ToArray());
        var font = file.GetFont(0);

        Assert.IsTrue(font.TryGetColr(out var colr));
        Assert.IsTrue(colr.IsVersion1);

        Assert.IsTrue(colr.TryGetBaseGlyphPaint(baseGlyphId: 1, out var p1));
        Assert.AreEqual((byte)12, p1.Format);
        Assert.IsTrue(p1.TryGetPaintTransform(out var pt));
        Assert.IsTrue(pt.TryGetTransform(out var m1));
        Assert.AreEqual(t, m1);
        Assert.IsTrue(pt.TryGetPaint(out var p1Child));
        Assert.IsTrue(p1Child.TryGetPaintSolid(out var s1));
        Assert.AreEqual((ushort)1, s1.PaletteIndex);

        Assert.IsTrue(colr.TryGetBaseGlyphPaint(baseGlyphId: 2, out var p2));
        Assert.AreEqual((byte)13, p2.Format);
        Assert.IsTrue(p2.TryGetPaintVarTransform(out var pvt));
        Assert.IsTrue(pvt.TryGetTransform(out var m2, out uint varIndexBase));
        Assert.AreEqual(t, m2);
        Assert.AreEqual(7u, varIndexBase);

        // Ensure the embedded matrix offsets match the spec:
        // PaintTransform/PaintVarTransform: transformOffset is Offset24 at +4..+6; expected 7.
        var data = colr.Table.Span;
        Assert.AreEqual((byte)0, data[p1.Offset + 4]);
        Assert.AreEqual((byte)0, data[p1.Offset + 5]);
        Assert.AreEqual((byte)7, data[p1.Offset + 6]);

        Assert.AreEqual((byte)0, data[p2.Offset + 4]);
        Assert.AreEqual((byte)0, data[p2.Offset + 5]);
        Assert.AreEqual((byte)7, data[p2.Offset + 6]);
    }

    [TestMethod]
    public void ColrV1_TryFrom_ImportsStructuredV1_WhenPaintFormatsSupported()
    {
        byte[] head = TestSfntTables.BuildValidHeadTable(unitsPerEm: 1000);

        var original = new ColrTableBuilder();
        original.ClearToVersion1();
        original.SetVarIndexMapData(BuildDeltaSetIndexMapFormat0_EntrySize4_Inner16(new VarIdx(0, 0)));
        original.SetMinimalItemVariationStore(axisCount: 0);

        var solid = ColrTableBuilder.Solid(paletteIndex: 1, alpha: new F2Dot14(0x4000));
        original.SetBaseGlyphPaint(baseGlyphId: 1, paint: ColrTableBuilder.VarTranslate(solid, dx: 10, dy: -20, varIndexBase: 0));

        var sfnt = new SfntBuilder { SfntVersion = 0x00010000u };
        sfnt.SetTable(KnownTags.head, head);
        sfnt.SetTable(original);

        using var file = SfntFile.FromMemory(sfnt.ToArray());
        var font = file.GetFont(0);
        Assert.IsTrue(font.TryGetColr(out var colr));

        Assert.IsTrue(ColrTableBuilder.TryFrom(colr, out var imported));

        // If TryFrom fell back to raw, this would throw.
        imported.SetBaseGlyphPaint(baseGlyphId: 2, paint: ColrTableBuilder.Solid(2, new F2Dot14(0x4000)));

        var sfnt2 = new SfntBuilder { SfntVersion = 0x00010000u };
        sfnt2.SetTable(KnownTags.head, head);
        sfnt2.SetTable(imported);

        using var file2 = SfntFile.FromMemory(sfnt2.ToArray());
        var font2 = file2.GetFont(0);

        Assert.IsTrue(font2.TryGetColr(out var colr2));
        Assert.IsTrue(colr2.TryGetBaseGlyphPaint(baseGlyphId: 1, out var p1));
        Assert.AreEqual((byte)15, p1.Format);
        Assert.IsTrue(colr2.TryGetBaseGlyphPaint(baseGlyphId: 2, out var p2));
        Assert.AreEqual((byte)2, p2.Format);
    }

    [TestMethod]
    public void ColrV1_CanWriteVarGradientAndScaleRotateSkew()
    {
        byte[] head = TestSfntTables.BuildValidHeadTable(unitsPerEm: 1000);

        var colrBuilder = new ColrTableBuilder();
        colrBuilder.ClearToVersion1();
        colrBuilder.SetVarIndexMapData(BuildDeltaSetIndexMapFormat0_EntrySize4_Inner16(new VarIdx(0, 0)));
        colrBuilder.SetMinimalItemVariationStore(axisCount: 0);

        var varStops = new[]
        {
            new ColrTableBuilder.VarColorStopV1(new F2Dot14(0x0000), paletteIndex: 0, alpha: new F2Dot14(0x4000), varIndexBase: 0),
            new ColrTableBuilder.VarColorStopV1(new F2Dot14(0x4000), paletteIndex: 1, alpha: new F2Dot14(0x4000), varIndexBase: 0),
        };

        colrBuilder.SetBaseGlyphPaint(
            baseGlyphId: 3,
            paint: ColrTableBuilder.VarLinearGradient(
                extend: 0,
                x0: 0, y0: 0,
                x1: 100, y1: 0,
                x2: 0, y2: 100,
                varIndexBase: 0,
                stops: varStops));

        var solid2 = ColrTableBuilder.Solid(paletteIndex: 2, alpha: new F2Dot14(0x4000));
        colrBuilder.SetBaseGlyphPaint(
            baseGlyphId: 4,
            paint: ColrTableBuilder.VarScaleUniformAroundCenter(
                paint: solid2,
                scale: new F2Dot14(0x4000),
                centerX: 10,
                centerY: 20,
                varIndexBase: 0));

        var solid3 = ColrTableBuilder.Solid(paletteIndex: 3, alpha: new F2Dot14(0x4000));
        colrBuilder.SetBaseGlyphPaint(
            baseGlyphId: 5,
            paint: ColrTableBuilder.SkewAroundCenter(
                paint: solid3,
                xSkewAngle: new F2Dot14(0x2000),
                ySkewAngle: new F2Dot14(0x1000),
                centerX: -10,
                centerY: 15));

        var solid4 = ColrTableBuilder.Solid(paletteIndex: 4, alpha: new F2Dot14(0x4000));
        colrBuilder.SetBaseGlyphPaint(
            baseGlyphId: 6,
            paint: ColrTableBuilder.VarRotateAroundCenter(
                paint: solid4,
                angle: new F2Dot14(0x1000),
                centerX: 0,
                centerY: 0,
                varIndexBase: 0));

        var solid5 = ColrTableBuilder.Solid(paletteIndex: 5, alpha: new F2Dot14(0x4000));
        colrBuilder.SetBaseGlyphPaint(
            baseGlyphId: 7,
            paint: ColrTableBuilder.Scale(
                paint: solid5,
                scaleX: new F2Dot14(0x4000),
                scaleY: new F2Dot14(0x2000)));

        var sfnt = new SfntBuilder { SfntVersion = 0x00010000u };
        sfnt.SetTable(KnownTags.head, head);
        sfnt.SetTable(colrBuilder);

        using var file = SfntFile.FromMemory(sfnt.ToArray());
        var font = file.GetFont(0);

        Assert.IsTrue(font.TryGetColr(out var colr));

        Assert.IsTrue(colr.TryGetBaseGlyphPaint(baseGlyphId: 3, out var p3));
        Assert.AreEqual((byte)5, p3.Format);
        Assert.IsTrue(p3.TryGetPaintVarLinearGradient(out var vlg));
        Assert.AreEqual(0u, vlg.VarIndexBase);
        Assert.IsTrue(vlg.TryGetColorLine(out var vline));
        Assert.AreEqual((ushort)2, vline.StopCount);
        Assert.IsTrue(vline.TryGetStop(0, out var vs0));
        Assert.AreEqual((ushort)0, vs0.PaletteIndex);
        Assert.IsTrue(vline.TryGetStop(1, out var vs1));
        Assert.AreEqual((ushort)1, vs1.PaletteIndex);

        // VarLinearGradient: VarColorLineOffset is Offset24 at +1..+3; expected 20.
        Assert.AreEqual((byte)0, colr.Table.Span[p3.Offset + 1]);
        Assert.AreEqual((byte)0, colr.Table.Span[p3.Offset + 2]);
        Assert.AreEqual((byte)20, colr.Table.Span[p3.Offset + 3]);

        Assert.IsTrue(colr.TryGetBaseGlyphPaint(baseGlyphId: 4, out var p4));
        Assert.AreEqual((byte)23, p4.Format);
        Assert.IsTrue(p4.TryGetPaintVarScaleUniformAroundCenter(out var vsuac));
        Assert.AreEqual((short)0x4000, vsuac.Scale.RawValue);
        Assert.AreEqual((short)10, vsuac.CenterX);
        Assert.AreEqual((short)20, vsuac.CenterY);
        Assert.AreEqual(0u, vsuac.VarIndexBase);
        Assert.IsTrue(vsuac.TryGetPaint(out var p4Child));
        Assert.AreEqual((byte)2, p4Child.Format);

        Assert.IsTrue(colr.TryGetBaseGlyphPaint(baseGlyphId: 5, out var p5));
        Assert.AreEqual((byte)30, p5.Format);
        Assert.IsTrue(p5.TryGetPaintSkewAroundCenter(out var skewac));
        Assert.AreEqual((short)0x2000, skewac.XSkewAngle.RawValue);
        Assert.AreEqual((short)0x1000, skewac.YSkewAngle.RawValue);
        Assert.AreEqual((short)-10, skewac.CenterX);
        Assert.AreEqual((short)15, skewac.CenterY);
        Assert.IsTrue(skewac.TryGetPaint(out var p5Child));
        Assert.AreEqual((byte)2, p5Child.Format);

        Assert.IsTrue(colr.TryGetBaseGlyphPaint(baseGlyphId: 6, out var p6));
        Assert.AreEqual((byte)27, p6.Format);
        Assert.IsTrue(p6.TryGetPaintVarRotateAroundCenter(out var vrac));
        Assert.AreEqual((short)0x1000, vrac.Angle.RawValue);
        Assert.AreEqual(0u, vrac.VarIndexBase);
        Assert.IsTrue(vrac.TryGetPaint(out var p6Child));
        Assert.AreEqual((byte)2, p6Child.Format);

        Assert.IsTrue(colr.TryGetBaseGlyphPaint(baseGlyphId: 7, out var p7));
        Assert.AreEqual((byte)16, p7.Format);
        Assert.IsTrue(p7.TryGetPaintScale(out var sc));
        Assert.AreEqual((short)0x4000, sc.ScaleX.RawValue);
        Assert.AreEqual((short)0x2000, sc.ScaleY.RawValue);
        Assert.IsTrue(sc.TryGetPaint(out var p7Child));
        Assert.AreEqual((byte)2, p7Child.Format);
    }

    private static byte[] BuildDeltaSetIndexMapFormat0_EntrySize4_Inner16(params VarIdx[] entries)
    {
        if ((uint)entries.Length > ushort.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(entries));

        // format=0, entryFormat=0x3F => entrySize=4, innerIndexBitCount=16
        byte[] bytes = new byte[4 + (entries.Length * 4)];
        var span = bytes.AsSpan();
        span[0] = 0;
        span[1] = 0x3F;
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(2, 2), (ushort)entries.Length);

        int pos = 4;
        for (int i = 0; i < entries.Length; i++)
        {
            var e = entries[i];
            uint packed = ((uint)e.OuterIndex << 16) | e.InnerIndex;
            BinaryPrimitives.WriteUInt32BigEndian(span.Slice(pos, 4), packed);
            pos += 4;
        }

        return bytes;
    }
}
