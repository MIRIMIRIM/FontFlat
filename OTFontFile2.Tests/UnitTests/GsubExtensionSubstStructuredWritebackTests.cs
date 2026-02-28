using Microsoft.VisualStudio.TestTools.UnitTesting;
using OTFontFile2.Tables;

namespace OTFontFile2.Tests.UnitTests;

[TestClass]
public sealed class GsubExtensionSubstStructuredWritebackTests
{
    [TestMethod]
    public void FontModel_CanWriteStructuredGsub_WithExtensionSubstLookup()
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

        var inner = new GsubSingleSubstSubtableBuilder();
        inner.AddOrReplace(fromGlyphId: 10, toGlyphId: 11);

        var ext = new GsubExtensionSubstSubtableBuilder();
        ext.SetSubtableData(extensionLookupType: 1, subtableData: inner.ToMemory());

        var lookup = gsubBuilder.Layout.Lookups.AddLookup(lookupType: 7, lookupFlag: 0);
        lookup.AddSubtable(ext.ToMemory());

        var feature = gsubBuilder.Layout.Features.GetOrAddFeature(testFeature);
        feature.AddLookup(lookup);

        var script = gsubBuilder.Layout.Scripts.GetOrAddScript(dflt);
        script.GetOrCreateDefaultLangSys().AddFeature(feature);

        byte[] editedBytes = model.ToArray();
        Assert.AreEqual(0xB1B0AFBAu, OpenTypeChecksum.Compute(editedBytes));

        using var editedFile = SfntFile.FromMemory(editedBytes);
        var editedFont = editedFile.GetFont(0);

        Assert.IsTrue(editedFont.TryGetGsub(out var gsub));
        Assert.IsTrue(gsub.TryGetLookupList(out var lookupList));
        Assert.AreEqual((ushort)1, lookupList.LookupCount);

        Assert.IsTrue(lookupList.TryGetLookup(0, out var lookupTable));
        Assert.AreEqual((ushort)7, lookupTable.LookupType);
        Assert.AreEqual((ushort)1, lookupTable.SubtableCount);
        Assert.IsTrue(lookupTable.TryGetSubtableOffset(0, out ushort subtableRel));

        int extOffset = lookupTable.Offset + subtableRel;
        Assert.IsTrue(GsubExtensionSubstSubtable.TryCreate(gsub.Table, extOffset, out var extTable));
        Assert.AreEqual((ushort)1, extTable.SubstFormat);
        Assert.AreEqual((ushort)1, extTable.ExtensionLookupType);
        Assert.IsTrue(extTable.TryResolve(out ushort resolvedType, out int resolvedOffset));
        Assert.AreEqual((ushort)1, resolvedType);

        Assert.IsTrue(GsubSingleSubstSubtable.TryCreate(gsub.Table, resolvedOffset, out var innerTable));
        Assert.AreEqual((ushort)1, innerTable.SubstFormat);
        Assert.IsTrue(innerTable.TrySubstituteGlyph(glyphId: 10, out bool substituted, out ushort outGlyph));
        Assert.IsTrue(substituted);
        Assert.AreEqual((ushort)11, outGlyph);
    }
}

