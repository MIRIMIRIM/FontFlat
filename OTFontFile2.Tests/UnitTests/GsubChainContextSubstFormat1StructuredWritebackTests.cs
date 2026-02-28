using Microsoft.VisualStudio.TestTools.UnitTesting;
using OTFontFile2.Tables;

namespace OTFontFile2.Tests.UnitTests;

[TestClass]
public sealed class GsubChainContextSubstFormat1StructuredWritebackTests
{
    [TestMethod]
    public void FontModel_CanWriteStructuredGsub_WithChainContextSubstLookup_Format1()
    {
        byte[] head = TestSfntTables.BuildValidHeadTable(unitsPerEm: 1000);

        var sfnt = new SfntBuilder { SfntVersion = 0x00010000u };
        sfnt.SetTable(KnownTags.head, head);

        using var file = SfntFile.FromMemory(sfnt.ToArray());
        var font = file.GetFont(0);

        var model = new FontModel(font);
        Assert.IsTrue(model.TryEdit<GsubTableBuilder>(out var gsubBuilder));

        Assert.IsTrue(Tag.TryParse("DFLT", out var dflt));
        Assert.IsTrue(Tag.TryParse("TEST", out var testFeature));

        var single = new GsubSingleSubstSubtableBuilder();
        single.AddOrReplace(fromGlyphId: 50, toGlyphId: 60);

        var singleLookup = gsubBuilder.Layout.Lookups.AddLookup(lookupType: 1, lookupFlag: 0);
        singleLookup.AddSubtable(single.ToMemory());

        var chain = new GsubChainContextSubstFormat1SubtableBuilder();
        chain.AddRule(
            startGlyphId: 50,
            backtrackGlyphIds: new ushort[] { 40 },
            inputGlyphIds: new ushort[] { 51 },
            lookaheadGlyphIds: new ushort[] { 70 },
            substLookupRecords: new[] { new SequenceLookupRecord(sequenceIndex: 0, lookupListIndex: 0) });

        var chainLookup = gsubBuilder.Layout.Lookups.AddLookup(lookupType: 6, lookupFlag: 0);
        chainLookup.AddSubtable(chain.ToMemory());

        var feature = gsubBuilder.Layout.Features.GetOrAddFeature(testFeature);
        feature.AddLookup(chainLookup);

        var script = gsubBuilder.Layout.Scripts.GetOrAddScript(dflt);
        script.GetOrCreateDefaultLangSys().AddFeature(feature);

        byte[] editedBytes = model.ToArray();
        Assert.AreEqual(0xB1B0AFBAu, OpenTypeChecksum.Compute(editedBytes));

        using var editedFile = SfntFile.FromMemory(editedBytes);
        var editedFont = editedFile.GetFont(0);

        Assert.IsTrue(editedFont.TryGetGsub(out var gsub));
        Assert.IsTrue(gsub.TryGetLookupList(out var lookupList));
        Assert.AreEqual((ushort)2, lookupList.LookupCount);

        Assert.IsTrue(lookupList.TryGetLookup(1, out var lookup));
        Assert.AreEqual((ushort)6, lookup.LookupType);
        Assert.AreEqual((ushort)1, lookup.SubtableCount);
        Assert.IsTrue(lookup.TryGetSubtableOffset(0, out ushort subtableRel));

        int subtableOffset = lookup.Offset + subtableRel;
        Assert.IsTrue(GsubChainContextSubstSubtable.TryCreate(gsub.Table, subtableOffset, out var subtable));
        Assert.AreEqual((ushort)1, subtable.SubstFormat);

        Assert.IsTrue(subtable.TryGetFormat1(out var f1));
        Assert.AreEqual((ushort)1, f1.ChainSubRuleSetCount);

        Assert.IsTrue(f1.TryGetCoverage(out var coverage));
        Assert.IsTrue(coverage.TryGetCoverage(glyphId: 50, out bool covered, out ushort coverageIndex));
        Assert.IsTrue(covered);
        Assert.AreEqual((ushort)0, coverageIndex);

        Assert.IsTrue(f1.TryGetChainSubRuleSet(0, out var set));
        Assert.AreEqual((ushort)1, set.ChainSubRuleCount);

        Assert.IsTrue(set.TryGetChainSubRule(0, out var rule));
        Assert.AreEqual((ushort)1, rule.BacktrackGlyphCount);
        Assert.IsTrue(rule.TryGetBacktrackGlyphId(0, out ushort backGid));
        Assert.AreEqual((ushort)40, backGid);

        Assert.IsTrue(rule.TryGetInputGlyphCount(out ushort inputGlyphCount));
        Assert.AreEqual((ushort)2, inputGlyphCount);
        Assert.IsTrue(rule.TryGetInputGlyphId(0, out ushort inputGid));
        Assert.AreEqual((ushort)51, inputGid);

        Assert.IsTrue(rule.TryGetLookaheadGlyphCount(out ushort lookaheadGlyphCount));
        Assert.AreEqual((ushort)1, lookaheadGlyphCount);
        Assert.IsTrue(rule.TryGetLookaheadGlyphId(0, out ushort lookaheadGid));
        Assert.AreEqual((ushort)70, lookaheadGid);

        Assert.IsTrue(rule.TryGetSubstCount(out ushort substCount));
        Assert.AreEqual((ushort)1, substCount);

        Assert.IsTrue(rule.TryGetSubstLookupRecord(0, out var rec));
        Assert.AreEqual((ushort)0, rec.SequenceIndex);
        Assert.AreEqual((ushort)0, rec.LookupListIndex);
    }
}

