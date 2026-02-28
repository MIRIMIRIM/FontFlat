using Microsoft.VisualStudio.TestTools.UnitTesting;
using OTFontFile2.Tables;
using System.Buffers.Binary;
using Legacy = OTFontFile;

namespace OTFontFile2.Tests.UnitTests;

[TestClass]
public sealed class PostTableTests
{
    [TestMethod]
    public void SyntheticPostV2Table_ParsesAndMatchesLegacy()
    {
        byte[] postBytes = BuildPostV2Table();

        var builder = new SfntBuilder();
        builder.SetTable(KnownTags.post, postBytes);
        byte[] fontBytes = builder.ToArray();

        using var file = SfntFile.FromMemory(fontBytes);
        var font = file.GetFont(0);

        Assert.IsTrue(font.TryGetPost(out var post));
        Assert.IsTrue(post.IsVersion2);

        Assert.IsTrue(post.TryGetNumberOfGlyphs(out ushort numGlyphs));
        Assert.AreEqual((ushort)2, numGlyphs);

        Assert.IsTrue(post.TryGetGlyphNameIndex(0, out ushort idx0));
        Assert.IsTrue(post.TryGetGlyphNameIndex(1, out ushort idx1));
        Assert.AreEqual((ushort)0, idx0);   // .notdef
        Assert.AreEqual((ushort)258, idx1); // first custom string

        Assert.IsTrue(post.TryGetGlyphNameString(0, out string n0));
        Assert.IsTrue(post.TryGetGlyphNameString(1, out string n1));
        Assert.AreEqual(".notdef", n0);
        Assert.AreEqual("foo", n1);

        string tempPath = Path.Combine(Path.GetTempPath(), $"synthetic-post-{Guid.NewGuid():N}.ttf");
        try
        {
            File.WriteAllBytes(tempPath, fontBytes);

            using var legacyFile = new Legacy.OTFile();
            Assert.IsTrue(legacyFile.open(tempPath));
            var legacyFont = legacyFile.GetFont(0)!;

            var legacyPost = (Legacy.Table_post)legacyFont.GetTable("post")!;
            Assert.AreEqual(legacyPost.Version.GetUint(), post.Version.RawValue);

            Assert.AreEqual(".notdef", legacyPost.GetGlyphName(0));
            Assert.AreEqual("foo", legacyPost.GetGlyphName(1));
        }
        finally
        {
            try { File.Delete(tempPath); } catch { }
        }
    }

    private static byte[] BuildPostV2Table()
    {
        // post header (32) + numberOfGlyphs (2) + glyphNameIndex[2] (4) + stringData ("foo") (4)
        byte[] table = new byte[42];
        var span = table.AsSpan();

        BinaryPrimitives.WriteUInt32BigEndian(span.Slice(0, 4), 0x00020000u); // formatType 2.0
        BinaryPrimitives.WriteUInt32BigEndian(span.Slice(4, 4), 0u);          // italicAngle
        BinaryPrimitives.WriteInt16BigEndian(span.Slice(8, 2), 0);            // underlinePosition
        BinaryPrimitives.WriteInt16BigEndian(span.Slice(10, 2), 0);           // underlineThickness
        BinaryPrimitives.WriteUInt32BigEndian(span.Slice(12, 4), 0u);         // isFixedPitch
        BinaryPrimitives.WriteUInt32BigEndian(span.Slice(16, 4), 0u);         // minMemType42
        BinaryPrimitives.WriteUInt32BigEndian(span.Slice(20, 4), 0u);         // maxMemType42
        BinaryPrimitives.WriteUInt32BigEndian(span.Slice(24, 4), 0u);         // minMemType1
        BinaryPrimitives.WriteUInt32BigEndian(span.Slice(28, 4), 0u);         // maxMemType1

        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(32, 2), 2); // numberOfGlyphs
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(34, 2), 0); // glyph0 -> standard .notdef
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(36, 2), 258); // glyph1 -> first custom string

        span[38] = 3; // pascal length
        span[39] = (byte)'f';
        span[40] = (byte)'o';
        span[41] = (byte)'o';

        return table;
    }
}

