using Microsoft.VisualStudio.TestTools.UnitTesting;
using OTFontFile2.Tables;

namespace OTFontFile2.Tests.UnitTests;

[TestClass]
public sealed class GlyfCompositeValidationTests
{
    [TestMethod]
    public void Glyf_CompositeComponentEnumerator_RejectsMultipleTransformFlags()
    {
        // Composite glyph with invalid flags: WE_HAVE_A_SCALE + WE_HAVE_A_TWO_BY_TWO both set.
        byte[] glyph = new byte[]
        {
            0xFF, 0xFF, // numberOfContours = -1
            0x00, 0x00,
            0x00, 0x00,
            0x00, 0x00,
            0x00, 0x00,

            0x00, 0x88, // flags: WE_HAVE_A_SCALE (0x0008) + WE_HAVE_A_TWO_BY_TWO (0x0080)
            0x00, 0x01, // glyphIndex
            0x00, 0x00, // args (byte)
        };

        Assert.IsTrue(GlyfTable.TryCreateCompositeGlyphComponentEnumerator(glyph, out var e));
        Assert.IsFalse(e.MoveNext());
        Assert.IsFalse(e.IsValid);
    }

    [TestMethod]
    public void Glyf_CompositeInstructions_RejectsInstructionsFlagOnNonLastComponent()
    {
        // 2 components; first component sets WE_HAVE_INSTRUCTIONS + MORE_COMPONENTS (invalid).
        byte[] glyph = new byte[]
        {
            0xFF, 0xFF, // numberOfContours = -1
            0x00, 0x00,
            0x00, 0x00,
            0x00, 0x00,
            0x00, 0x00,

            // Component 1: WE_HAVE_INSTRUCTIONS + MORE_COMPONENTS + ARGS_ARE_XY_VALUES
            0x01, 0x22, // flags
            0x00, 0x01, // glyphIndex
            0x00, 0x00, // args (byte)

            // Component 2: last, minimal
            0x00, 0x02, // flags (ARGS_ARE_XY_VALUES)
            0x00, 0x02, // glyphIndex
            0x00, 0x00, // args (byte)
        };

        Assert.IsFalse(GlyfTable.TryGetCompositeGlyphInstructions(glyph, out _));

        Assert.IsTrue(GlyfTable.TryCreateCompositeGlyphComponentEnumerator(glyph, out var e));
        Assert.IsFalse(e.MoveNext());
        Assert.IsFalse(e.IsValid);
    }
}

