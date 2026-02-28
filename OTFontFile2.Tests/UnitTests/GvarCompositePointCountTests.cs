using Microsoft.VisualStudio.TestTools.UnitTesting;
using OTFontFile2.Tables;
using OTFontFile2.Tables.Glyf;
using System.Buffers.Binary;

namespace OTFontFile2.Tests.UnitTests;

[TestClass]
public sealed class GvarCompositePointCountTests
{
    [TestMethod]
    public void GvarBuilder_ComputesCompositeGlyphPointCountWithPhantoms()
    {
        byte[] head = TestSfntTables.BuildValidHeadTable(unitsPerEm: 1000);

        var maxp = new MaxpTableBuilder { NumGlyphs = 3 };

        var fvar = new FvarTableBuilder();
        fvar.AddAxis(new Tag(0x77676874u), minValue: new Fixed1616(0), defaultValue: new Fixed1616(0), maxValue: new Fixed1616(0), flags: 0, axisNameId: 256); // 'wght'

        // Glyph 0: 2 points
        var g0 = new GlyfSimpleGlyphBuilder();
        g0.SetContours(
            endPointsOfContours: new ushort[] { 1 },
            points: new[]
            {
                new GlyfGlyphPoint(0, 0, onCurve: true),
                new GlyfGlyphPoint(50, 0, onCurve: true),
            });
        byte[] glyph0 = Pad2(g0.Build());

        // Glyph 1: 1 point
        var g1 = new GlyfSimpleGlyphBuilder();
        g1.SetContours(
            endPointsOfContours: new ushort[] { 0 },
            points: new[]
            {
                new GlyfGlyphPoint(0, 0, onCurve: true),
            });
        byte[] glyph1 = Pad2(g1.Build());

        // Glyph 2: composite of glyph0 + glyph1
        var g2 = new GlyfCompositeGlyphBuilder();
        g2.AddComponent(glyphIndex: 0, dx: 0, dy: 0, a: new F2Dot14(0x4000), b: default, c: default, d: new F2Dot14(0x4000));
        g2.AddComponent(glyphIndex: 1, dx: 0, dy: 0, a: new F2Dot14(0x4000), b: default, c: default, d: new F2Dot14(0x4000));
        byte[] glyph2 = Pad2(g2.Build());

        byte[] glyf = new byte[glyph0.Length + glyph1.Length + glyph2.Length];
        int p = 0;
        glyph0.CopyTo(glyf, p); p += glyph0.Length;
        glyph1.CopyTo(glyf, p); p += glyph1.Length;
        glyph2.CopyTo(glyf, p);

        byte[] loca = BuildLocaFormat0(new[] { 0, glyph0.Length, glyph0.Length + glyph1.Length, glyf.Length });

        // Composite point count: (2 + 1) + 4 phantom = 7.
        var gv = new GvarTableBuilder.GlyphVariationData(axisCount: 1, pointCountWithPhantoms: 7);
        gv.AddTupleVariation(new GvarTableBuilder.TupleVariation(
            peakTupleRaw: new short[] { 0 },
            intermediateStartRaw: null,
            intermediateEndRaw: null,
            selectionKind: GvarTableBuilder.PointSelectionKind.Private,
            privatePointNumbers: new ushort[] { 0 },
            xDeltas: new short[] { 1 },
            yDeltas: new short[] { 2 }));
        byte[] record = gv.BuildGlyphVariationDataRecord();
        byte[] gvar = BuildGvarTable(axisCount: 1, glyphCount: 3, recordForGlyph2: record);

        var sfnt = new SfntBuilder { SfntVersion = 0x00010000u };
        sfnt.SetTable(KnownTags.head, head);
        sfnt.SetTable(maxp);
        sfnt.SetTable(KnownTags.glyf, glyf);
        sfnt.SetTable(KnownTags.loca, loca);
        sfnt.SetTable(fvar);
        sfnt.SetTable(KnownTags.gvar, gvar);

        using var file = SfntFile.FromMemory(sfnt.ToArray());
        var font = file.GetFont(0);

        var model = new FontModel(font);
        Assert.IsTrue(model.TryEdit<GvarTableBuilder>(out var gvarBuilder));
        Assert.IsTrue(gvarBuilder.IsLinkedBaseFont);

        Assert.IsTrue(gvarBuilder.TryGetGlyphPointCountWithPhantoms(2, out int pc));
        Assert.AreEqual(7, pc);

        Assert.IsTrue(gvarBuilder.TryGetGlyphVariations(2, out var parsed));
        Assert.AreEqual(7, parsed.PointCountWithPhantoms);
        Assert.AreEqual(1, parsed.TupleVariationCount);
    }

    private static byte[] Pad2(byte[] bytes)
    {
        if ((bytes.Length & 1) == 0)
            return bytes;

        var padded = new byte[bytes.Length + 1];
        bytes.CopyTo(padded, 0);
        return padded;
    }

    private static byte[] BuildLocaFormat0(int[] offsets)
    {
        byte[] bytes = new byte[offsets.Length * 2];
        var span = bytes.AsSpan();
        for (int i = 0; i < offsets.Length; i++)
        {
            ushort words = checked((ushort)(offsets[i] >> 1));
            BinaryPrimitives.WriteUInt16BigEndian(span.Slice(i * 2, 2), words);
        }
        return bytes;
    }

    private static byte[] BuildGvarTable(ushort axisCount, ushort glyphCount, ReadOnlySpan<byte> recordForGlyph2)
    {
        int headerLen = 20;
        int offsetsBytes = (glyphCount + 1) * 2;
        int dataOffset = headerLen + offsetsBytes;
        dataOffset = (dataOffset + 1) & ~1;

        int recordLenAligned = (recordForGlyph2.Length + 1) & ~1;
        ushort endWords = checked((ushort)(recordLenAligned >> 1));

        byte[] table = new byte[checked(dataOffset + recordLenAligned)];
        var span = table.AsSpan();

        BinaryPrimitives.WriteUInt32BigEndian(span.Slice(0, 4), 0x00010000u); // version
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(4, 2), axisCount);
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(6, 2), 0); // sharedTupleCount
        BinaryPrimitives.WriteUInt32BigEndian(span.Slice(8, 4), 0u); // sharedTuplesOffset
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(12, 2), glyphCount);
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(14, 2), 0); // flags (short offsets)
        BinaryPrimitives.WriteUInt32BigEndian(span.Slice(16, 4), (uint)dataOffset);

        // offsets array (in words): glyph0/1/2 start all 0, end is record length.
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(20, 2), 0);
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(22, 2), 0);
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(24, 2), 0);
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(26, 2), endWords);

        recordForGlyph2.CopyTo(span.Slice(dataOffset, recordForGlyph2.Length));
        return table;
    }
}

