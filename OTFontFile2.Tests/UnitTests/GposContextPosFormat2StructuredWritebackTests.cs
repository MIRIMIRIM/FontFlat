using Microsoft.VisualStudio.TestTools.UnitTesting;
using OTFontFile2.Tables;

namespace OTFontFile2.Tests.UnitTests;

[TestClass]
public sealed class GposContextPosFormat2StructuredWritebackTests
{
    [TestMethod]
    public void FontModel_CanWriteStructuredGpos_WithContextPosLookup_Format2()
    {
        byte[] head = TestSfntTables.BuildValidHeadTable(unitsPerEm: 1000);

        var sfnt = new SfntBuilder { SfntVersion = 0x00010000u };
        sfnt.SetTable(KnownTags.head, head);

        using var file = SfntFile.FromMemory(sfnt.ToArray());
        var font = file.GetFont(0);

        var model = new FontModel(font);
        Assert.IsTrue(model.TryEdit<GposTableBuilder>(out var gposBuilder));

        Assert.IsTrue(Tag.TryParse("DFLT", out var dflt));
        Assert.IsTrue(Tag.TryParse("TEST", out var testFeature));

        var singlePos = new GposSinglePosSubtableBuilder();
        singlePos.AddOrReplace(glyphId: 50, value: new GposValueRecordBuilder { XAdvance = 120 });

        var singleLookup = gposBuilder.Layout.Lookups.AddLookup(lookupType: 1, lookupFlag: 0);
        singleLookup.AddSubtable(singlePos.ToMemory());

        var ctx = new GposContextPosFormat2SubtableBuilder();
        ctx.AddCoverageGlyph(50);
        ctx.SetClass(glyphId: 50, classValue: 1);
        ctx.SetClass(glyphId: 51, classValue: 2);
        ctx.AddRule(
            startClass: 1,
            inputClasses: new ushort[] { 2 },
            posLookupRecords: new[] { new SequenceLookupRecord(sequenceIndex: 0, lookupListIndex: 0) });

        var ctxLookup = gposBuilder.Layout.Lookups.AddLookup(lookupType: 7, lookupFlag: 0);
        ctxLookup.AddSubtable(ctx.ToMemory());

        var feature = gposBuilder.Layout.Features.GetOrAddFeature(testFeature);
        feature.AddLookup(ctxLookup);

        var script = gposBuilder.Layout.Scripts.GetOrAddScript(dflt);
        script.GetOrCreateDefaultLangSys().AddFeature(feature);

        byte[] editedBytes = model.ToArray();
        Assert.AreEqual(0xB1B0AFBAu, OpenTypeChecksum.Compute(editedBytes));

        using var editedFile = SfntFile.FromMemory(editedBytes);
        var editedFont = editedFile.GetFont(0);

        Assert.IsTrue(editedFont.TryGetGpos(out var gpos));
        Assert.IsTrue(gpos.TryGetLookupList(out var lookupList));
        Assert.AreEqual((ushort)2, lookupList.LookupCount);

        Assert.IsTrue(lookupList.TryGetLookup(1, out var lookup));
        Assert.AreEqual((ushort)7, lookup.LookupType);
        Assert.AreEqual((ushort)1, lookup.SubtableCount);
        Assert.IsTrue(lookup.TryGetSubtableOffset(0, out ushort subtableRel));

        int subtableOffset = lookup.Offset + subtableRel;
        Assert.IsTrue(GposContextPosSubtable.TryCreate(gpos.Table, subtableOffset, out var subtable));
        Assert.AreEqual((ushort)2, subtable.PosFormat);

        Assert.IsTrue(subtable.TryGetFormat2(out var f2));
        Assert.AreEqual((ushort)2, f2.PosClassSetCount);

        Assert.IsTrue(f2.TryGetCoverage(out var coverage));
        Assert.IsTrue(coverage.TryGetCoverage(glyphId: 50, out bool covered, out ushort coverageIndex));
        Assert.IsTrue(covered);
        Assert.AreEqual((ushort)0, coverageIndex);

        Assert.IsTrue(f2.TryGetClassDef(out var classDef));
        Assert.IsTrue(classDef.TryGetClass(glyphId: 50, out ushort cls50));
        Assert.AreEqual((ushort)1, cls50);
        Assert.IsTrue(classDef.TryGetClass(glyphId: 51, out ushort cls51));
        Assert.AreEqual((ushort)2, cls51);

        Assert.IsTrue(f2.TryGetPosClassSet(1, out var set));
        Assert.AreEqual((ushort)1, set.PosClassRuleCount);

        Assert.IsTrue(set.TryGetPosClassRule(0, out var rule));
        Assert.AreEqual((ushort)2, rule.GlyphCount);
        Assert.AreEqual((ushort)1, rule.PosCount);

        Assert.IsTrue(rule.TryGetInputClassCount(out ushort inputCount));
        Assert.AreEqual((ushort)1, inputCount);
        Assert.IsTrue(rule.TryGetInputClass(0, out ushort inputCls));
        Assert.AreEqual((ushort)2, inputCls);

        Assert.IsTrue(rule.TryGetPosLookupRecord(0, out var rec));
        Assert.AreEqual((ushort)0, rec.SequenceIndex);
        Assert.AreEqual((ushort)0, rec.LookupListIndex);
    }
}

