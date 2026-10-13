// glyf/loca retain-splice tests (S1a card §4, commit ③ acceptance).
//
// The splice is the structural identity anchor of the slice: a full selection must
// reproduce the source image and offsets exactly, an unselected slot must become an
// equal-length empty glyph, and a short-format image must keep every offset even with at
// most one zero tail byte per slot. loca rebuilds from the produced offsets in the same
// format head already points at.
using System.Buffers.Binary;
using FontWeave.Containers;
using FontWeave.Core;
using FontWeave.Tables;
using Xunit;

namespace FontWeave.Tests;

public sealed class SubsetGlyfLocaSpliceTests
{
    private static (GlyfTable Glyf, LocaTable Loca, byte[] GlyfBytes) Model(
        IReadOnlyList<byte[]> records, bool longFormat)
    {
        var (locaBytes, glyfBytes, numGlyphs) = GlyfImages.Assemble([.. records]);
        LocaTable? loca;
        if (longFormat)
        {
            loca = GlyfImages.LocaOrThrow(locaBytes, numGlyphs);
        }
        else
        {
            var offsets = new uint[numGlyphs + 1];
            for (int i = 0; i <= numGlyphs; i++)
                offsets[i] = BinaryPrimitives.ReadUInt32BigEndian(locaBytes.AsSpan(4 * i));
            loca = GlyfImages.Loca(ShortLoca(offsets), numGlyphs, false);
        }
        Assert.NotNull(loca);
        _ = GlyfView.TryOpen(glyfBytes, out var view, out _, GlyfImages.GlyfLocation);
        ReadStatus status = view.TryReadComposites(loca, out var model, out _);
        Assert.Equal(ReadStatus.Done, status);
        return (model!, loca!, glyfBytes);
    }

    private static byte[] ShortLoca(IReadOnlyList<uint> offsets)
    {
        var bytes = new byte[2 * offsets.Count];
        for (int i = 0; i < offsets.Count; i++)
            BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(2 * i), (ushort)(offsets[i] / 2));
        return bytes;
    }

    private static (byte[]? Glyf, uint[]? Offsets) Splice(GlyfTable model, bool[] retained,
        bool shortFormat, bool dropHinting)
    {
        var status = GlyfTableCompiler.TryRetainSubset(model, retained, shortFormat,
            dropHinting, new BuildBudget(), out var glyf, out var offsets, out var diagnostic);
        Assert.Equal(ReadStatus.Done, status);
        Assert.Equal(DiagnosticCode.None, diagnostic.Code);
        return (glyf, offsets);
    }

    // Full selection is the slice's identity anchor: same bytes, same offsets.
    [Fact]
    public void FullRetainReproducesSourceImage()
    {
        var (model, _, source) = Model(
        [
            SubsetFontFixture.InstructedSimpleGlyph(),
            GlyfImages.CompositeRecord(0),
            GlyfImages.NoContourGlyphRecord()
        ], longFormat: true);
        var (glyf, offsets) = Splice(model, [true, true, true], shortFormat: false,
            dropHinting: false);
        Assert.True(source.AsSpan().SequenceEqual(glyf!));
        Assert.Equal([0, 18, 34, 46], offsets);
    }

    // An unselected slot becomes an empty glyph: no bytes, equal loca neighbours.
    [Fact]
    public void UnselectedSlotsBecomeEmpty()
    {
        var (model, _, source) = Model(
        [
            GlyfImages.NoContourGlyphRecord(),          // 12 bytes
            SubsetFontFixture.InstructedSimpleGlyph(),  // 18 bytes
            GlyfImages.CompositeRecord(0),              // 16 bytes
            GlyfImages.NoContourGlyphRecord()           // 12 bytes
        ], longFormat: true);
        var (glyf, offsets) = Splice(model, [true, false, true, false], shortFormat: false,
            dropHinting: false);
        Assert.Equal([0, 12, 12, 28, 28], offsets);
        Assert.Equal(28, glyf!.Length);
        // Slot 0 is verbatim, slot 2 follows with its own bytes verbatim.
        Assert.True(source.AsSpan(0, 12).SequenceEqual(glyf.AsSpan(0, 12)));
        Assert.True(source.AsSpan(30, 16).SequenceEqual(glyf.AsSpan(12, 16)));
    }

    // Short format keeps offsets even: the re-encoded (odd) slot gains exactly one zero
    // tail byte.
    [Fact]
    public void ShortFormatPadsOddReencodedSlots()
    {
        var (model, loca, source) = Model(
        [
            SubsetFontFixture.InstructedSimpleGlyph(),  // 18 bytes with instructions
            GlyfImages.NoContourGlyphRecord()           // 12 bytes
        ], longFormat: false);
        Assert.False(loca.LongFormat);
        var (glyf, offsets) = Splice(model, [true, true], shortFormat: true, dropHinting: true);
        // Stripping the instructions re-encodes slot 0 to 17 bytes; one zero tail byte
        // keeps the next offset even.
        Assert.Equal([0, 18, 30], offsets);
        Assert.Equal(30, glyf!.Length);
        Assert.Equal(0, BinaryPrimitives.ReadUInt16BigEndian(glyf.AsSpan(12)));
        Assert.Equal(0, glyf[29]); // the pad byte is zero
        // Slot 1 still moved verbatim.
        Assert.True(source.AsSpan(18, 12).SequenceEqual(glyf.AsSpan(18, 12)));
    }

    // Drop-hinting on a long-format face strips instructions without padding.
    [Fact]
    public void DropHintingStripsInstructions()
    {
        var (model, _, _) = Model(
        [
            SubsetFontFixture.InstructedSimpleGlyph(),
            GlyfImages.NoContourGlyphRecord()
        ], longFormat: true);
        var (glyf, offsets) = Splice(model, [true, true], shortFormat: false, dropHinting: true);
        Assert.Equal([0, 17, 29], offsets);
        Assert.Equal(0, BinaryPrimitives.ReadUInt16BigEndian(glyf!.AsSpan(12)));
    }

    // Without drop-hinting the short-format copy never repads: offsets match the source.
    [Fact]
    public void ShortFormatCopyWithoutDropHintingKeepsOffsets()
    {
        var (model, _, source) = Model(
        [
            SubsetFontFixture.InstructedSimpleGlyph(),
            GlyfImages.NoContourGlyphRecord()
        ], longFormat: false);
        var (glyf, offsets) = Splice(model, [true, true], shortFormat: true, dropHinting: false);
        Assert.True(source.AsSpan().SequenceEqual(glyf!));
        Assert.Equal([0, 18, 30], offsets);
    }

    // The produced offsets feed loca's same-format rebuild; the byte length stays
    // (numGlyphs+1) × (2|4).
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void OffsetsRebuildLocaInSourceFormat(bool longFormat)
    {
        var (model, loca, _) = Model(
        [
            GlyfImages.NoContourGlyphRecord(),
            GlyfImages.CompositeRecord(0),
            GlyfImages.NoContourGlyphRecord()
        ], longFormat);
        var (_, offsets) = Splice(model, [true, false, true], shortFormat: !longFormat,
            dropHinting: false);
        var status = LocaTableCompiler.TryFromOffsets(loca, offsets!, out var rebuilt,
            out var diagnostic);
        Assert.Equal(ReadStatus.Done, status);
        Assert.Equal(loca.NumGlyphs, rebuilt!.NumGlyphs);
        Assert.Equal(loca.LongFormat, rebuilt.LongFormat);
        Assert.Equal([0, 12, 12, 24], rebuilt.Offsets.ToArray());
        status = LocaTableCompiler.TryCompile(rebuilt, out var bytes, out diagnostic);
        Assert.Equal(ReadStatus.Done, status);
        Assert.Equal((rebuilt.NumGlyphs + 1) * (rebuilt.LongFormat ? 4 : 2), bytes!.Length);
    }
}
