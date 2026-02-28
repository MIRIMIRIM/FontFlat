using Microsoft.VisualStudio.TestTools.UnitTesting;
using OTFontFile2.Tables;

namespace OTFontFile2.Tests.UnitTests;

[TestClass]
public sealed class BaseTableWritebackTests
{
    [TestMethod]
    public void BaseTable_CanEditAndWriteBack_WithSfntEditor()
    {
        string path = GetFontPath("SourceHanSansCN-Regular.otf");

        using var originalFile = SfntFile.Open(path);
        var originalFont = originalFile.GetFont(0);

        Assert.IsTrue(originalFont.TryGetBase(out var originalBase));
        Assert.IsTrue(BaseTableBuilder.TryFrom(originalBase, out var edit));

        // Make the vertical axis absent (valid, though it may leave unused body bytes).
        edit.VertAxisOffset = 0;

        var editor = new SfntEditor(originalFont);
        editor.SetTable(edit);
        byte[] editedFontBytes = editor.ToArray();

        Assert.AreEqual(0xB1B0AFBAu, OpenTypeChecksum.Compute(editedFontBytes));

        using var editedFile = SfntFile.FromMemory(editedFontBytes);
        var editedFont = editedFile.GetFont(0);
        Assert.IsTrue(editedFont.TryGetBase(out var editedBase));
        Assert.AreEqual((ushort)0, editedBase.VertAxisOffset);
        Assert.IsFalse(editedBase.TryGetVertAxis(out _));
    }

    private static string GetFontPath(string fileName)
        => Path.Combine(AppContext.BaseDirectory, "TestResources", "SampleFonts", fileName);
}

