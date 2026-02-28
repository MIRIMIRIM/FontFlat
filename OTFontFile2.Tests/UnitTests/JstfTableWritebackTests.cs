using System.Buffers.Binary;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OTFontFile2.Tables;

namespace OTFontFile2.Tests.UnitTests;

[TestClass]
public sealed class JstfTableWritebackTests
{
    [TestMethod]
    public void JstfTable_CanEditAndWriteBack_WithSfntEditor()
    {
        byte[] jstfBytes = BuildSyntheticJstfTable();

        byte[] head = TestSfntTables.BuildValidHeadTable(unitsPerEm: 1000);

        var sfnt = new SfntBuilder { SfntVersion = 0x00010000u };
        sfnt.SetTable(KnownTags.head, head);
        sfnt.SetTable(KnownTags.JSTF, jstfBytes);
        byte[] originalFontBytes = sfnt.ToArray();

        using var originalFile = SfntFile.FromMemory(originalFontBytes);
        var originalFont = originalFile.GetFont(0);

        Assert.IsTrue(originalFont.TryGetJstf(out var originalJstf));
        Assert.AreEqual(0x00010000u, originalJstf.Version.RawValue);

        Assert.IsTrue(JstfTableBuilder.TryFrom(originalJstf, out var edit));

        byte[] editedJstf = edit.ToArray();
        BinaryPrimitives.WriteUInt32BigEndian(editedJstf.AsSpan(0, 4), 0x00020000u);
        edit.SetTableData(editedJstf);

        var editor = new SfntEditor(originalFont);
        editor.SetTable(edit);
        byte[] editedFontBytes = editor.ToArray();

        Assert.AreEqual(0xB1B0AFBAu, OpenTypeChecksum.Compute(editedFontBytes));

        using var editedFile = SfntFile.FromMemory(editedFontBytes);
        var editedFont = editedFile.GetFont(0);
        Assert.IsTrue(editedFont.TryGetJstf(out var editedJstfTable));
        Assert.AreEqual(0x00020000u, editedJstfTable.Version.RawValue);
    }

    private static byte[] BuildSyntheticJstfTable()
    {
        // A minimal JSTF with one script ("latn"), one default langsys, one priority:
        // - ShrinkageEnableGSUBModList: [3, 7]
        // - ShrinkageEnableGPOSModList: [5]
        // - ShrinkageJstfMax: one lookup table (type=1, one subtable offset=8)
        byte[] bytes = new byte[66];
        var span = bytes.AsSpan();

        WriteU32(span, 0, 0x00010000u); // version
        WriteU16(span, 4, 1); // scriptCount

        WriteTag(span, 6, "latn");
        WriteU16(span, 10, 12); // scriptOffset

        int script = 12;
        WriteU16(span, script + 0, 0); // extenderGlyphOffset
        WriteU16(span, script + 2, 6); // defLangSysOffset (script+6 = 18)
        WriteU16(span, script + 4, 0); // langSysCount

        int langSys = script + 6; // 18
        WriteU16(span, langSys + 0, 1); // priorityCount
        WriteU16(span, langSys + 2, 4); // priorityOffset[0] (langSys+4 = 22)

        int pri = langSys + 4; // 22
        WriteU16(span, pri + 0, 20); // shrinkEnableGSUB (pri+20 = 42)
        WriteU16(span, pri + 2, 0);
        WriteU16(span, pri + 4, 26); // shrinkEnableGPOS (pri+26 = 48)
        WriteU16(span, pri + 6, 0);
        WriteU16(span, pri + 8, 30); // shrinkJstfMax (pri+30 = 52)
        WriteU16(span, pri + 10, 0);
        WriteU16(span, pri + 12, 0);
        WriteU16(span, pri + 14, 0);
        WriteU16(span, pri + 16, 0);
        WriteU16(span, pri + 18, 0);

        int gsubMod = pri + 20; // 42
        WriteU16(span, gsubMod + 0, 2); // lookupCount
        WriteU16(span, gsubMod + 2, 3);
        WriteU16(span, gsubMod + 4, 7);

        int gposMod = gsubMod + 6; // 48
        WriteU16(span, gposMod + 0, 1); // lookupCount
        WriteU16(span, gposMod + 2, 5);

        int max = gposMod + 4; // 52
        WriteU16(span, max + 0, 1); // lookupCount
        WriteU16(span, max + 2, 4); // lookupOffset[0] (max+4 = 56)

        int lookup = max + 4; // 56
        WriteU16(span, lookup + 0, 1); // lookupType
        WriteU16(span, lookup + 2, 0); // lookupFlag
        WriteU16(span, lookup + 4, 1); // subtableCount
        WriteU16(span, lookup + 6, 8); // subtableOffset[0]
        WriteU16(span, lookup + 8, 1); // dummy subtable data

        return bytes;
    }

    private static void WriteU16(Span<byte> data, int offset, ushort value)
        => BinaryPrimitives.WriteUInt16BigEndian(data.Slice(offset, 2), value);

    private static void WriteU32(Span<byte> data, int offset, uint value)
        => BinaryPrimitives.WriteUInt32BigEndian(data.Slice(offset, 4), value);

    private static void WriteTag(Span<byte> data, int offset, string tag)
    {
        Assert.AreEqual(4, tag.Length);
        data[offset + 0] = (byte)tag[0];
        data[offset + 1] = (byte)tag[1];
        data[offset + 2] = (byte)tag[2];
        data[offset + 3] = (byte)tag[3];
    }
}

