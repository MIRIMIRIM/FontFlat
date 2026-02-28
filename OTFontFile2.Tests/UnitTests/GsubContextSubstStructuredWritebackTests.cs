using Microsoft.VisualStudio.TestTools.UnitTesting;
using OTFontFile2.Tables;

namespace OTFontFile2.Tests.UnitTests;

[TestClass]
public sealed class GsubContextSubstStructuredWritebackTests
{
    [TestMethod]
    public void FontModel_CanWriteStructuredGsub_WithContextSubstLookup_Format3()
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

        // Lookup 0: single substitution, referenced from the context subtable.
        var single = new GsubSingleSubstSubtableBuilder();
        single.AddOrReplace(fromGlyphId: 50, toGlyphId: 60);

        var singleLookup = gsubBuilder.Layout.Lookups.AddLookup(lookupType: 1, lookupFlag: 0);
        singleLookup.AddSubtable(single.ToMemory());

        // Lookup 1: context subtable format 3 with 1 glyph position.
        var cov0 = new CoverageTableBuilder();
        cov0.AddGlyph(50);

        var context = new GsubContextSubstSubtableBuilder();
        context.AddCoverage(cov0);
        context.AddSubstLookupRecord(sequenceIndex: 0, lookupListIndex: 0);

        var contextLookup = gsubBuilder.Layout.Lookups.AddLookup(lookupType: 5, lookupFlag: 0);
        contextLookup.AddSubtable(context.ToMemory());

        var feature = gsubBuilder.Layout.Features.GetOrAddFeature(testFeature);
        feature.AddLookup(contextLookup);

        var script = gsubBuilder.Layout.Scripts.GetOrAddScript(dflt);
        script.GetOrCreateDefaultLangSys().AddFeature(feature);

        byte[] editedBytes = model.ToArray();
        Assert.AreEqual(0xB1B0AFBAu, OpenTypeChecksum.Compute(editedBytes));

        using var editedFile = SfntFile.FromMemory(editedBytes);
        var editedFont = editedFile.GetFont(0);

        Assert.IsTrue(editedFont.TryGetGsub(out var gsub));
        Assert.IsTrue(gsub.TryGetLookupList(out var lookupList));
        Assert.AreEqual((ushort)2, lookupList.LookupCount);

        Assert.IsTrue(lookupList.TryGetLookup(0, out var l0));
        Assert.AreEqual((ushort)1, l0.LookupType);

        Assert.IsTrue(lookupList.TryGetLookup(1, out var l1));
        Assert.AreEqual((ushort)5, l1.LookupType);
        Assert.AreEqual((ushort)1, l1.SubtableCount);
        Assert.IsTrue(l1.TryGetSubtableOffset(0, out ushort subtableRel));

        int subtableOffset = l1.Offset + subtableRel;
        Assert.IsTrue(GsubContextSubstSubtable.TryCreate(gsub.Table, subtableOffset, out var subtable));
        Assert.AreEqual((ushort)3, subtable.SubstFormat);

        Assert.IsTrue(subtable.TryGetFormat3(out var f3));
        Assert.AreEqual((ushort)1, f3.GlyphCount);
        Assert.AreEqual((ushort)1, f3.SubstCount);

        Assert.IsTrue(f3.TryGetCoverage(0, out var coverage));
        Assert.IsTrue(coverage.TryGetCoverage(glyphId: 50, out bool covered, out ushort coverageIndex));
        Assert.IsTrue(covered);
        Assert.AreEqual((ushort)0, coverageIndex);

        Assert.IsTrue(f3.TryGetSubstLookupRecord(0, out var record));
        Assert.AreEqual((ushort)0, record.SequenceIndex);
        Assert.AreEqual((ushort)0, record.LookupListIndex);
    }
}

