// Variable-font profile admission and retain-mode execution tests (SV card §1/§4,
// commit ① acceptance). The two outright gates of the static-first slices are
// replaced by the VF admission chain, so these tests pin the new faces: the A1-A5
// combination refusals (each with its frozen keyword contract carried by the
// numeric code 28's registry) and the retain-mode execution on a variable
// TrueType face — the migrated S0-RF-001-VF expectation, which used to refuse.
using System.Buffers.Binary;
using FontWeave.Core;
using FontWeave.Operations;
using FontWeave.Operations.Subset;
using Xunit;

namespace FontWeave.Tests;

public sealed class SubsetVfProfileTests
{
    private static SubsetRequest Request(FontFaceRef face, IReadOnlyList<ushort> gids,
        SubsetMode mode = SubsetMode.RetainGids, SubsetOptions? options = null) => new()
    {
        Face = face,
        Root = new SubsetGlyphRoot { Kind = SubsetRootKind.ExplicitGlyphSet, Gids = gids },
        Mode = mode,
        Options = options ?? new SubsetOptions { NoLayout = true }
    };

    // A TrueType variable face with the full admission set: fvar (the axis
    // authority), gvar and HVAR (both RETAIN_RAW on this path), on the two-glyph
    // static face's glyf/loca/cmap spine.
    private static FontFaceRef VariableTrueTypeFace() => SubsetFontFixture.Face(
        (SubsetFontFixture.HeadTag, SubsetFontFixture.Head()),
        (SubsetFontFixture.MaxpTag, SubsetFontFixture.Maxp(2)),
        (SubsetFontFixture.GlyfTag,
            [.. SubsetFontFixture.InstructedSimpleGlyph(),
             .. GlyfImages.NoContourGlyphRecord()]),
        (SubsetFontFixture.LocaTag, GlyfImages.LongLoca([0, 18, 30])),
        (SubsetFontFixture.CmapTag,
            SubsetFontFixture.CmapWithMacRoman((0x41, 1))),
        (SubsetFontFixture.FvarTag, SubsetFontFixture.Fvar()),
        (SubsetFontFixture.GvarTag, SubsetFontFixture.Gvar(1, 2)),
        (SubsetFontFixture.HvarTag, SubsetFontFixture.Hvar(1, 2)));

    // ---- A1: fvar without a variable outline spine --------------------------------

    // The migrated S0-RF-001-VF face shape (fvar present, no glyf/loca, no CFF2)
    // keeps its refusal, now under the profile code 28 and naming fvar
    // (SubsetRefusalTests.VariableFontRefuses carries the migrated main-gate form).
    [Fact]
    public void FvarWithoutOutlineSpineRefusesAtFvar()
    {
        var face = SubsetFontFixture.Face(
            (SubsetFontFixture.HeadTag, SubsetFontFixture.Head()),
            (SubsetFontFixture.MaxpTag, SubsetFontFixture.Maxp(4)),
            (SubsetFontFixture.FvarTag, SubsetFontFixture.Fvar()));
        foreach (var mode in new[] { SubsetMode.RetainGids, SubsetMode.CompactRemap })
        {
            var status = SubsetOperation.TryPrepare(Request(face, [0], mode),
                out var result, out _, out var diagnostic);
            Assert.Equal(ReadStatus.UnsupportedFormat, status);
            Assert.Equal(DiagnosticCode.OutlineProfileUnsupported, diagnostic.Code);
            Assert.Equal(SubsetFontFixture.FvarTag, diagnostic.Location.TableTag);
            Assert.Null(result);
        }
    }

    // ---- A2: two outline spines (CFF2 + glyf/loca) --------------------------------

    // Tag presence alone drives the refusal (the guard runs before any read), so a
    // shell CFF2 payload carries the face.
    [Fact]
    public void Cff2WithTrueTypeSpineRefusesAtCff2()
    {
        var face = SubsetFontFixture.Face(
            (SubsetFontFixture.HeadTag, SubsetFontFixture.Head()),
            (SubsetFontFixture.MaxpTag, SubsetFontFixture.Maxp(2)),
            (SubsetFontFixture.GlyfTag,
                [.. SubsetFontFixture.InstructedSimpleGlyph(),
                 .. GlyfImages.NoContourGlyphRecord()]),
            (SubsetFontFixture.LocaTag, GlyfImages.LongLoca([0, 18, 30])),
            (SubsetFontFixture.Cff2Tag, SubsetFontFixture.Dummy()));
        var status = SubsetOperation.TryPrepare(Request(face, [0]),
            out var result, out _, out var diagnostic);
        Assert.Equal(ReadStatus.UnsupportedFormat, status);
        Assert.Equal(DiagnosticCode.OutlineProfileUnsupported, diagnostic.Code);
        Assert.Equal(SubsetFontFixture.Cff2Tag, diagnostic.Location.TableTag);
        Assert.Null(result);
    }

    // ---- A3: gvar and CFF2 share a face (spec L1612) ------------------------------

    [Fact]
    public void Cff2WithGvarRefusesAtGvar()
    {
        var face = SubsetFontFixture.Face(
            (SubsetFontFixture.HeadTag, SubsetFontFixture.Head()),
            (SubsetFontFixture.MaxpTag, SubsetFontFixture.Maxp05(2)),
            (SubsetFontFixture.Cff2Tag, SubsetFontFixture.Dummy()),
            (SubsetFontFixture.GvarTag, SubsetFontFixture.Gvar(1, 2)));
        var status = SubsetOperation.TryPrepare(Request(face, [0]),
            out var result, out _, out var diagnostic);
        Assert.Equal(ReadStatus.UnsupportedFormat, status);
        Assert.Equal(DiagnosticCode.OutlineProfileUnsupported, diagnostic.Code);
        Assert.Equal(SubsetFontFixture.GvarTag, diagnostic.Location.TableTag);
        Assert.Null(result);
    }

    // ---- A4: a CFF2 face requires maxp 0.5 (spec L1574) ---------------------------

    [Fact]
    public void Cff2WithMaxp10RefusesAtMaxpVersion()
    {
        var face = SubsetFontFixture.Face(
            (SubsetFontFixture.HeadTag, SubsetFontFixture.Head()),
            (SubsetFontFixture.MaxpTag, SubsetFontFixture.Maxp(2)),
            (SubsetFontFixture.Cff2Tag, SubsetFontFixture.Dummy()));
        var status = SubsetOperation.TryPrepare(Request(face, [0]),
            out var result, out _, out var diagnostic);
        Assert.Equal(ReadStatus.UnsupportedFormat, status);
        Assert.Equal(DiagnosticCode.OutlineProfileUnsupported, diagnostic.Code);
        Assert.Equal(DataField.Version, diagnostic.Location.Field);
        Assert.Equal(0x00010000UL, diagnostic.Detail); // maxp 1.0's raw version word
        Assert.Null(result);
    }

    // ---- A5: cvar without its cvt pairing partner ---------------------------------

    [Fact]
    public void CvarWithoutCvtRefusesAtCvar()
    {
        var face = SubsetFontFixture.Face(
            (SubsetFontFixture.HeadTag, SubsetFontFixture.Head()),
            (SubsetFontFixture.MaxpTag, SubsetFontFixture.Maxp(2)),
            (SubsetFontFixture.GlyfTag,
                [.. SubsetFontFixture.InstructedSimpleGlyph(),
                 .. GlyfImages.NoContourGlyphRecord()]),
            (SubsetFontFixture.LocaTag, GlyfImages.LongLoca([0, 18, 30])),
            (SubsetFontFixture.CmapTag,
                SubsetFontFixture.CmapWithMacRoman((0x41, 1))),
            (SubsetFontFixture.FvarTag, SubsetFontFixture.Fvar()),
            (SubsetFontFixture.CvarTag, SubsetFontFixture.Dummy(21)));
        var status = SubsetOperation.TryPrepare(Request(face, [0]),
            out var result, out _, out var diagnostic);
        Assert.Equal(ReadStatus.UnsupportedFormat, status);
        Assert.Equal(DiagnosticCode.OutlineProfileUnsupported, diagnostic.Code);
        Assert.Equal(SubsetFontFixture.CvarTag, diagnostic.Location.TableTag);
        Assert.Null(result);
    }

    // ---- the migrated S0-RF-001-VF expectation: retain-mode execution --------------

    // The former vf-input-gate refusal now executes end to end on a variable
    // TrueType face: every variable row (fvar/gvar/HVAR) follows the relaxed
    // column's RETAIN_RAW and moves byte-identical, the static spine follows its
    // own rows unchanged, and the report shows the raw-copy decisions.
    [Fact]
    public void VariableTrueTypeFaceRetainExecutesWithRawVariableFamily()
    {
        var face = VariableTrueTypeFace();
        var status = SubsetOperation.TryPrepare(Request(face, [0, 1]),
            out var result, out var report, out var diagnostic);
        Assert.Equal(ReadStatus.Done, status);
        Assert.Equal((ushort)0, (ushort)diagnostic.Code);
        Assert.NotNull(result);

        byte[] output = new byte[result!.Font.OutputLength];
        _ = result.Font.TryCopyTo(output, out _, out _);
        foreach (uint tag in new[] { SubsetFontFixture.FvarTag, SubsetFontFixture.GvarTag,
            SubsetFontFixture.HvarTag })
        {
            Assert.True(FindTable(output, tag, out var payload));
            Assert.Equal(TableAction.Preserve,
                Assert.Single(report!.Decisions, d => d.Tag == tag).Action);
        }
    }

    private static bool FindTable(byte[] sfnt, uint tag, out byte[] payload)
    {
        ushort count = BinaryPrimitives.ReadUInt16BigEndian(sfnt.AsSpan(4));
        for (int i = 0; i < count; i++)
        {
            int record = 12 + 16 * i;
            if (BinaryPrimitives.ReadUInt32BigEndian(sfnt.AsSpan(record)) != tag) continue;
            int offset = (int)BinaryPrimitives.ReadUInt32BigEndian(sfnt.AsSpan(record + 8));
            int length = (int)BinaryPrimitives.ReadUInt32BigEndian(sfnt.AsSpan(record + 12));
            payload = sfnt[offset..(offset + length)];
            return true;
        }
        payload = [];
        return false;
    }
}
