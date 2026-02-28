using Microsoft.VisualStudio.TestTools.UnitTesting;
using OTFontFile2.Tables;

namespace OTFontFile2.Tests.UnitTests;

[TestClass]
public sealed class GsubChainContextSubstStructuredWritebackTests
{
    [TestMethod]
    public void FontModel_CanWriteStructuredGsub_WithChainContextSubstLookup_Format3()
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

        // Lookup 0: single substitution, referenced from the chaining-context subtable.
        var single = new GsubSingleSubstSubtableBuilder();
        single.AddOrReplace(fromGlyphId: 50, toGlyphId: 60);

        var singleLookup = gsubBuilder.Layout.Lookups.AddLookup(lookupType: 1, lookupFlag: 0);
        singleLookup.AddSubtable(single.ToMemory());

        // Lookup 1: chain-context subtable format 3 with 1 backtrack/input/lookahead position each.
        var back = new CoverageTableBuilder();
        back.AddGlyph(40);

        var input = new CoverageTableBuilder();
        input.AddGlyph(50);

        var look = new CoverageTableBuilder();
        look.AddGlyph(70);

        var chain = new GsubChainContextSubstSubtableBuilder();
        chain.AddBacktrackCoverage(back);
        chain.AddInputCoverage(input);
        chain.AddLookaheadCoverage(look);
        chain.AddSubstLookupRecord(sequenceIndex: 0, lookupListIndex: 0);

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

        Assert.IsTrue(lookupList.TryGetLookup(1, out var l1));
        Assert.AreEqual((ushort)6, l1.LookupType);
        Assert.AreEqual((ushort)1, l1.SubtableCount);
        Assert.IsTrue(l1.TryGetSubtableOffset(0, out ushort subtableRel));

        int subtableOffset = l1.Offset + subtableRel;
        Assert.IsTrue(GsubChainContextSubstSubtable.TryCreate(gsub.Table, subtableOffset, out var subtable));
        Assert.AreEqual((ushort)3, subtable.SubstFormat);

        Assert.IsTrue(subtable.TryGetFormat3(out var f3));
        Assert.AreEqual((ushort)1, f3.BacktrackGlyphCount);

        Assert.IsTrue(f3.TryGetBacktrackCoverage(0, out var backCov));
        Assert.IsTrue(backCov.TryGetCoverage(glyphId: 40, out bool backCovered, out ushort backIndex));
        Assert.IsTrue(backCovered);
        Assert.AreEqual((ushort)0, backIndex);

        Assert.IsTrue(f3.TryGetInputGlyphCount(out ushort inputCount));
        Assert.AreEqual((ushort)1, inputCount);

        Assert.IsTrue(f3.TryGetInputCoverage(0, out var inputCov));
        Assert.IsTrue(inputCov.TryGetCoverage(glyphId: 50, out bool inputCovered, out ushort inputIndex));
        Assert.IsTrue(inputCovered);
        Assert.AreEqual((ushort)0, inputIndex);

        Assert.IsTrue(f3.TryGetLookaheadGlyphCount(out ushort lookCount));
        Assert.AreEqual((ushort)1, lookCount);

        Assert.IsTrue(f3.TryGetLookaheadCoverage(0, out var lookCov));
        Assert.IsTrue(lookCov.TryGetCoverage(glyphId: 70, out bool lookCovered, out ushort lookIndex));
        Assert.IsTrue(lookCovered);
        Assert.AreEqual((ushort)0, lookIndex);

        Assert.IsTrue(f3.TryGetSubstCount(out ushort substCount));
        Assert.AreEqual((ushort)1, substCount);

        Assert.IsTrue(f3.TryGetSubstLookupRecord(0, out var record));
        Assert.AreEqual((ushort)0, record.SequenceIndex);
        Assert.AreEqual((ushort)0, record.LookupListIndex);
    }
}

