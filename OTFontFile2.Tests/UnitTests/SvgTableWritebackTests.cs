using Microsoft.VisualStudio.TestTools.UnitTesting;
using OTFontFile2.Tables;

namespace OTFontFile2.Tests.UnitTests;

[TestClass]
public sealed class SvgTableWritebackTests
{
    [TestMethod]
    public void SvgTable_CanEditAndWriteBack_WithSfntEditor()
    {
        var svgBuilder = new SvgTableBuilder
        {
            Version = 0,
            Reserved = 0
        };
        svgBuilder.AddDocument(startGlyphId: 5, endGlyphId: 5, documentBytes: new byte[] { 1, 2, 3 });

        byte[] head = TestSfntTables.BuildValidHeadTable(unitsPerEm: 1000);

        var sfnt = new SfntBuilder { SfntVersion = 0x00010000u };
        sfnt.SetTable(KnownTags.head, head);
        sfnt.SetTable(svgBuilder);
        byte[] originalFontBytes = sfnt.ToArray();

        using var originalFile = SfntFile.FromMemory(originalFontBytes);
        var originalFont = originalFile.GetFont(0);

        Assert.IsTrue(originalFont.TryGetSvg(out var originalSvg));
        Assert.AreEqual((ushort)0, originalSvg.Version);
        Assert.IsTrue(originalSvg.TryGetDocumentIndex(out var index));
        Assert.AreEqual((ushort)1, index.RecordCount);

        Assert.IsTrue(originalSvg.TryGetDocumentRecord(0, out var record));
        Assert.AreEqual((ushort)5, record.StartGlyphId);
        Assert.AreEqual((ushort)5, record.EndGlyphId);
        Assert.IsTrue(originalSvg.TryGetDocumentSpan(record, out var docBytes));
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, docBytes.ToArray());

        Assert.IsTrue(SvgTableBuilder.TryFrom(originalSvg, out var edit));
        edit.Clear();
        edit.Reserved = 123;
        edit.AddDocument(startGlyphId: 10, endGlyphId: 20, documentBytes: new byte[] { 9, 8 });

        var editor = new SfntEditor(originalFont);
        editor.SetTable(edit);
        byte[] editedFontBytes = editor.ToArray();

        Assert.AreEqual(0xB1B0AFBAu, OpenTypeChecksum.Compute(editedFontBytes));

        using var editedFile = SfntFile.FromMemory(editedFontBytes);
        var editedFont = editedFile.GetFont(0);

        Assert.IsTrue(editedFont.TryGetSvg(out var editedSvg));
        Assert.AreEqual((uint)123, editedSvg.Reserved);
        Assert.IsTrue(editedSvg.TryGetDocumentIndex(out var editedIndex));
        Assert.AreEqual((ushort)1, editedIndex.RecordCount);

        Assert.IsTrue(editedSvg.TryGetDocumentRecord(0, out var editedRecord));
        Assert.AreEqual((ushort)10, editedRecord.StartGlyphId);
        Assert.AreEqual((ushort)20, editedRecord.EndGlyphId);
        Assert.IsTrue(editedSvg.TryGetDocumentSpan(editedRecord, out var editedDocBytes));
        CollectionAssert.AreEqual(new byte[] { 9, 8 }, editedDocBytes.ToArray());
    }
}

