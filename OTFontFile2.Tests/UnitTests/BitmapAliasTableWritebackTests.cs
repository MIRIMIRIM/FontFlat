using Microsoft.VisualStudio.TestTools.UnitTesting;
using OTFontFile2.Tables;

namespace OTFontFile2.Tests.UnitTests;

[TestClass]
public sealed class BitmapAliasTableWritebackTests
{
    [TestMethod]
    public void FontModel_CanEditCblcCbdtBlocBdatAndWriteBack()
    {
        byte[] head = TestSfntTables.BuildValidHeadTable(unitsPerEm: 1000);

        var cblc = new CblcTableBuilder { Version = new Fixed1616(0x00020000u), BitmapSizeTableCount = 0 };
        var cbdt = new CbdtTableBuilder { Version = new Fixed1616(0x00020000u) };
        var bloc = new BlocTableBuilder { Version = new Fixed1616(0x00020000u), BitmapSizeTableCount = 0 };
        var bdat = new BdatTableBuilder { Version = new Fixed1616(0x00020000u) };

        var sfnt = new SfntBuilder { SfntVersion = 0x00010000u };
        sfnt.SetTable(KnownTags.head, head);
        sfnt.SetTable(cblc);
        sfnt.SetTable(cbdt);
        sfnt.SetTable(bloc);
        sfnt.SetTable(bdat);

        using var file = SfntFile.FromMemory(sfnt.ToArray());
        var font = file.GetFont(0);

        var model = new FontModel(font);
        Assert.IsTrue(model.TryEdit<CblcTableBuilder>(out var editCblc));
        Assert.IsTrue(model.TryEdit<CbdtTableBuilder>(out var editCbdt));
        Assert.IsTrue(model.TryEdit<BlocTableBuilder>(out var editBloc));
        Assert.IsTrue(model.TryEdit<BdatTableBuilder>(out var editBdat));

        editCblc.SetBody(bitmapSizeTableCount: 0, bodyBytes: new byte[] { 1, 2, 3 });
        editCbdt.SetPayload(new byte[] { 9, 8, 7 });
        editBloc.SetBody(bitmapSizeTableCount: 0, bodyBytes: new byte[] { 4, 5 });
        editBdat.SetPayload(new byte[] { 6 });

        byte[] editedBytes = model.ToArray();
        Assert.AreEqual(0xB1B0AFBAu, OpenTypeChecksum.Compute(editedBytes));

        using var editedFile = SfntFile.FromMemory(editedBytes);
        var editedFont = editedFile.GetFont(0);

        Assert.IsTrue(editedFont.TryGetTableSlice(KnownTags.CBLC, out var editedCblcSlice));
        CollectionAssert.AreEqual(editCblc.ToArray(), editedCblcSlice.Span.ToArray());

        Assert.IsTrue(editedFont.TryGetTableSlice(KnownTags.CBDT, out var editedCbdtSlice));
        CollectionAssert.AreEqual(editCbdt.ToArray(), editedCbdtSlice.Span.ToArray());

        Assert.IsTrue(editedFont.TryGetTableSlice(KnownTags.BLOC, out var editedBlocSlice));
        CollectionAssert.AreEqual(editBloc.ToArray(), editedBlocSlice.Span.ToArray());

        Assert.IsTrue(editedFont.TryGetTableSlice(KnownTags.BDAT, out var editedBdatSlice));
        CollectionAssert.AreEqual(editBdat.ToArray(), editedBdatSlice.Span.ToArray());
    }
}

