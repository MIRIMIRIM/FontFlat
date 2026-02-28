using Microsoft.VisualStudio.TestTools.UnitTesting;
using OTFontFile2.Tables;

namespace OTFontFile2.Tests.UnitTests;

[TestClass]
public sealed class GposContextPosStructuredWritebackTests
{
    [TestMethod]
    public void FontModel_CanWriteStructuredGpos_WithContextPosFormat3()
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

        // Lookup 0: SinglePos (type 1), used by the contextual lookup.
        var single = new GposSinglePosSubtableBuilder();
        single.AddOrReplace(glyphId: 10, value: new GposValueRecordBuilder { XAdvance = 123 });

        var lookup0 = gposBuilder.Layout.Lookups.AddLookup(lookupType: 1, lookupFlag: 0);
        lookup0.AddSubtable(single.ToMemory());

        // Lookup 1: ContextPos (type 7) format 3, matching glyph 10 and applying lookup 0 to it.
        var cov0 = new CoverageTableBuilder();
        cov0.AddGlyph(10);

        var context = new GposContextPosSubtableBuilder();
        context.AddCoverage(cov0);
        context.AddPosLookupRecord(sequenceIndex: 0, lookupListIndex: 0);

        var lookup1 = gposBuilder.Layout.Lookups.AddLookup(lookupType: 7, lookupFlag: 0);
        lookup1.AddSubtable(context.ToMemory());

        var feature = gposBuilder.Layout.Features.GetOrAddFeature(testFeature);
        feature.AddLookup(lookup0);
        feature.AddLookup(lookup1);

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
        Assert.IsTrue(lookup.TryGetSubtableOffset(0, out ushort rel));

        int subtableOffset = lookup.Offset + rel;
        Assert.IsTrue(GposContextPosSubtable.TryCreate(gpos.Table, subtableOffset, out var subtable));
        Assert.AreEqual((ushort)3, subtable.PosFormat);

        Assert.IsTrue(subtable.TryGetFormat3(out var fmt3));
        Assert.AreEqual((ushort)1, fmt3.GlyphCount);
        Assert.AreEqual((ushort)1, fmt3.PosCount);

        Assert.IsTrue(fmt3.TryGetCoverage(0, out var cov));
        Assert.IsTrue(cov.TryGetCoverage(glyphId: 10, out bool covered10, out ushort covIndex));
        Assert.IsTrue(covered10);
        Assert.AreEqual((ushort)0, covIndex);

        Assert.IsTrue(fmt3.TryGetPosLookupRecord(0, out var record));
        Assert.AreEqual((ushort)0, record.SequenceIndex);
        Assert.AreEqual((ushort)0, record.LookupListIndex);
    }
}

