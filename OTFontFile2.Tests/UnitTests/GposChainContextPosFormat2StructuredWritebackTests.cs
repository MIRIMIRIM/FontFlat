using Microsoft.VisualStudio.TestTools.UnitTesting;
using OTFontFile2.Tables;

namespace OTFontFile2.Tests.UnitTests;

[TestClass]
public sealed class GposChainContextPosFormat2StructuredWritebackTests
{
    [TestMethod]
    public void FontModel_CanWriteStructuredGpos_WithChainContextPosLookup_Format2()
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

        var chain = new GposChainContextPosFormat2SubtableBuilder();
        chain.AddCoverageGlyph(50);

        chain.SetInputClass(glyphId: 50, classValue: 1);
        chain.SetInputClass(glyphId: 51, classValue: 2);

        chain.SetBacktrackClass(glyphId: 40, classValue: 1);
        chain.SetLookaheadClass(glyphId: 70, classValue: 1);

        chain.AddRule(
            startClass: 1,
            backtrackClasses: new ushort[] { 1 },
            inputClasses: new ushort[] { 2 },
            lookaheadClasses: new ushort[] { 1 },
            posLookupRecords: new[] { new SequenceLookupRecord(sequenceIndex: 0, lookupListIndex: 0) });

        var chainLookup = gposBuilder.Layout.Lookups.AddLookup(lookupType: 8, lookupFlag: 0);
        chainLookup.AddSubtable(chain.ToMemory());

        var feature = gposBuilder.Layout.Features.GetOrAddFeature(testFeature);
        feature.AddLookup(chainLookup);

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
        Assert.AreEqual((ushort)8, lookup.LookupType);
        Assert.AreEqual((ushort)1, lookup.SubtableCount);
        Assert.IsTrue(lookup.TryGetSubtableOffset(0, out ushort subtableRel));

        int subtableOffset = lookup.Offset + subtableRel;
        Assert.IsTrue(GposChainContextPosSubtable.TryCreate(gpos.Table, subtableOffset, out var subtable));
        Assert.AreEqual((ushort)2, subtable.PosFormat);

        Assert.IsTrue(subtable.TryGetFormat2(out var f2));
        Assert.AreEqual((ushort)2, f2.ChainPosClassSetCount);

        Assert.IsTrue(f2.TryGetCoverage(out var coverage));
        Assert.IsTrue(coverage.TryGetCoverage(glyphId: 50, out bool covered, out ushort coverageIndex));
        Assert.IsTrue(covered);
        Assert.AreEqual((ushort)0, coverageIndex);

        Assert.IsTrue(f2.TryGetBacktrackClassDef(out var backDef));
        Assert.IsTrue(backDef.TryGetClass(glyphId: 40, out ushort backCls));
        Assert.AreEqual((ushort)1, backCls);

        Assert.IsTrue(f2.TryGetInputClassDef(out var inputDef));
        Assert.IsTrue(inputDef.TryGetClass(glyphId: 50, out ushort in0));
        Assert.AreEqual((ushort)1, in0);
        Assert.IsTrue(inputDef.TryGetClass(glyphId: 51, out ushort in1));
        Assert.AreEqual((ushort)2, in1);

        Assert.IsTrue(f2.TryGetLookaheadClassDef(out var lookDef));
        Assert.IsTrue(lookDef.TryGetClass(glyphId: 70, out ushort lookCls));
        Assert.AreEqual((ushort)1, lookCls);

        Assert.IsTrue(f2.TryGetChainPosClassSet(1, out var set));
        Assert.AreEqual((ushort)1, set.ChainPosClassRuleCount);

        Assert.IsTrue(set.TryGetChainPosClassRule(0, out var rule));
        Assert.AreEqual((ushort)1, rule.BacktrackGlyphCount);
        Assert.IsTrue(rule.TryGetBacktrackClass(0, out ushort ruleBackCls));
        Assert.AreEqual((ushort)1, ruleBackCls);

        Assert.IsTrue(rule.TryGetInputGlyphCount(out ushort inputCount));
        Assert.AreEqual((ushort)2, inputCount);
        Assert.IsTrue(rule.TryGetInputClass(0, out ushort ruleInCls));
        Assert.AreEqual((ushort)2, ruleInCls);

        Assert.IsTrue(rule.TryGetLookaheadGlyphCount(out ushort lookCount));
        Assert.AreEqual((ushort)1, lookCount);
        Assert.IsTrue(rule.TryGetLookaheadClass(0, out ushort ruleLookCls));
        Assert.AreEqual((ushort)1, ruleLookCls);

        Assert.IsTrue(rule.TryGetPosCount(out ushort posCount));
        Assert.AreEqual((ushort)1, posCount);

        Assert.IsTrue(rule.TryGetPosLookupRecord(0, out var rec));
        Assert.AreEqual((ushort)0, rec.SequenceIndex);
        Assert.AreEqual((ushort)0, rec.LookupListIndex);
    }
}

