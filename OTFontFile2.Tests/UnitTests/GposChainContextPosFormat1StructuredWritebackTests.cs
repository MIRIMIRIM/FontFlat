using Microsoft.VisualStudio.TestTools.UnitTesting;
using OTFontFile2.Tables;

namespace OTFontFile2.Tests.UnitTests;

[TestClass]
public sealed class GposChainContextPosFormat1StructuredWritebackTests
{
    [TestMethod]
    public void FontModel_CanWriteStructuredGpos_WithChainContextPosLookup_Format1()
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

        var chain = new GposChainContextPosFormat1SubtableBuilder();
        chain.AddRule(
            startGlyphId: 50,
            backtrackGlyphIds: new ushort[] { 40 },
            inputGlyphIds: new ushort[] { 51 },
            lookaheadGlyphIds: new ushort[] { 70 },
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
        Assert.AreEqual((ushort)1, subtable.PosFormat);

        Assert.IsTrue(subtable.TryGetFormat1(out var f1));
        Assert.AreEqual((ushort)1, f1.ChainPosRuleSetCount);

        Assert.IsTrue(f1.TryGetCoverage(out var coverage));
        Assert.IsTrue(coverage.TryGetCoverage(glyphId: 50, out bool covered, out ushort coverageIndex));
        Assert.IsTrue(covered);
        Assert.AreEqual((ushort)0, coverageIndex);

        Assert.IsTrue(f1.TryGetChainPosRuleSet(0, out var set));
        Assert.AreEqual((ushort)1, set.ChainPosRuleCount);

        Assert.IsTrue(set.TryGetChainPosRule(0, out var rule));
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

        Assert.IsTrue(rule.TryGetPosCount(out ushort posCount));
        Assert.AreEqual((ushort)1, posCount);

        Assert.IsTrue(rule.TryGetPosLookupRecord(0, out var rec));
        Assert.AreEqual((ushort)0, rec.SequenceIndex);
        Assert.AreEqual((ushort)0, rec.LookupListIndex);
    }
}

