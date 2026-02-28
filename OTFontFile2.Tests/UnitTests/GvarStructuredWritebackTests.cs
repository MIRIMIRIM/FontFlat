using Microsoft.VisualStudio.TestTools.UnitTesting;
using OTFontFile2.Tables;
using OTFontFile2.Tables.Glyf;
using System.Buffers.Binary;

namespace OTFontFile2.Tests.UnitTests;

[TestClass]
public sealed class GvarStructuredWritebackTests
{
    [TestMethod]
    public void FontModel_CanImportStructuredGvar_EditAndWriteBack()
    {
        byte[] head = TestSfntTables.BuildValidHeadTable(unitsPerEm: 1000);

        var maxp = new MaxpTableBuilder { NumGlyphs = 1 };

        // Build a simple glyph with 2 points => pointCountWithPhantoms = 6.
        var simple = new GlyfSimpleGlyphBuilder();
        simple.SetContours(
            endPointsOfContours: new ushort[] { 1 },
            points: new[]
            {
                new GlyfGlyphPoint(0, 0, onCurve: true),
                new GlyfGlyphPoint(50, 0, onCurve: true),
            });
        byte[] glyph0 = simple.Build();
        if ((glyph0.Length & 1) != 0)
        {
            var padded = new byte[glyph0.Length + 1];
            glyph0.CopyTo(padded, 0);
            glyph0 = padded;
        }

        byte[] glyf = glyph0;
        byte[] loca = BuildLocaFormat0ForSingleGlyph(glyf.Length);

        var fvar = new FvarTableBuilder();
        fvar.AddAxis(new Tag(0x77676874u), minValue: new Fixed1616(0), defaultValue: new Fixed1616(0), maxValue: new Fixed1616(0), flags: 0, axisNameId: 256); // 'wght'

        // Build a gvar with 1 tuple variation affecting point 0 only.
        var gv = new GvarTableBuilder.GlyphVariationData(axisCount: 1, pointCountWithPhantoms: 6);
        gv.AddTupleVariation(new GvarTableBuilder.TupleVariation(
            peakTupleRaw: new short[] { 0 },
            intermediateStartRaw: null,
            intermediateEndRaw: null,
            selectionKind: GvarTableBuilder.PointSelectionKind.Private,
            privatePointNumbers: new ushort[] { 0 },
            xDeltas: new short[] { 5 },
            yDeltas: new short[] { -2 }));

        byte[] glyphVarRecord = gv.BuildGlyphVariationDataRecord();
        byte[] gvar = BuildGvarTable(axisCount: 1, glyphCount: 1, glyphVariationDataRecord: glyphVarRecord);

        var sfnt = new SfntBuilder { SfntVersion = 0x00010000u };
        sfnt.SetTable(KnownTags.head, head);
        sfnt.SetTable(maxp);
        sfnt.SetTable(KnownTags.loca, loca);
        sfnt.SetTable(KnownTags.glyf, glyf);
        sfnt.SetTable(fvar);
        sfnt.SetTable(KnownTags.gvar, gvar);

        using var file = SfntFile.FromMemory(sfnt.ToArray());
        var font = file.GetFont(0);

        var model = new FontModel(font);
        Assert.IsTrue(model.TryEdit<GvarTableBuilder>(out var edit));
        Assert.IsTrue(edit.IsLinkedBaseFont);

        Assert.IsTrue(edit.TryGetGlyphVariations(0, out var parsed));
        Assert.AreEqual(1, parsed.TupleVariationCount);
        Assert.IsTrue(parsed.TryGetTupleVariation(0, out var tv0));
        Assert.AreEqual(GvarTableBuilder.PointSelectionKind.Private, tv0.SelectionKind);
        CollectionAssert.AreEqual(new ushort[] { 0 }, tv0.PrivatePointNumbers);
        CollectionAssert.AreEqual(new short[] { 5 }, tv0.XDeltas);
        CollectionAssert.AreEqual(new short[] { -2 }, tv0.YDeltas);

        var newXDeltas = (short[])tv0.XDeltas.Clone();
        newXDeltas[0] = -7;

        var replacement = new GvarTableBuilder.TupleVariation(
            peakTupleRaw: tv0.PeakTupleRaw,
            intermediateStartRaw: tv0.IntermediateStartRaw,
            intermediateEndRaw: tv0.IntermediateEndRaw,
            selectionKind: tv0.SelectionKind,
            privatePointNumbers: tv0.PrivatePointNumbers,
            xDeltas: newXDeltas,
            yDeltas: tv0.YDeltas);

        parsed.ReplaceTupleVariation(0, replacement);
        edit.SetGlyphVariations(0, parsed);

        byte[] editedBytes = model.ToArray();
        Assert.AreEqual(0xB1B0AFBAu, OpenTypeChecksum.Compute(editedBytes));

        using var editedFile = SfntFile.FromMemory(editedBytes);
        var editedFont = editedFile.GetFont(0);

        var model2 = new FontModel(editedFont);
        Assert.IsTrue(model2.TryEdit<GvarTableBuilder>(out var imported));
        Assert.IsTrue(imported.TryGetGlyphVariations(0, out var importedVar));
        Assert.IsTrue(importedVar.TryGetTupleVariation(0, out var importedTv0));
        CollectionAssert.AreEqual(new short[] { -7 }, importedTv0.XDeltas);
    }

    private static byte[] BuildLocaFormat0ForSingleGlyph(int glyfLength)
    {
        ushort endWords = checked((ushort)(glyfLength >> 1));
        byte[] bytes = new byte[4];
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(0, 2), 0);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(2, 2), endWords);
        return bytes;
    }

    private static byte[] BuildGvarTable(ushort axisCount, ushort glyphCount, ReadOnlySpan<byte> glyphVariationDataRecord)
    {
        // Minimal gvar with:
        // sharedTupleCount=0, short offsets, single glyph record.
        int headerLen = 20;
        int offsetsBytes = (glyphCount + 1) * 2;
        int dataOffset = headerLen + offsetsBytes;
        dataOffset = (dataOffset + 1) & ~1;

        int recordLen = glyphVariationDataRecord.Length;
        int recordLenAligned = (recordLen + 1) & ~1;
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

        // offsets array (in words)
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(20, 2), 0);
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(22, 2), endWords);

        glyphVariationDataRecord.CopyTo(span.Slice(dataOffset, recordLen));
        return table;
    }
}
