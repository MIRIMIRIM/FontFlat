// Refusal-surface tests for the subset operation (S1a card §6, commit ① acceptance).
//
// Every frozen S0 refusal case is pinned at the unit level: outcome, diagnostic code and
// the failure shape (null result, retained report). Keyword contracts are keyed by the
// explicitly registered DiagnosticCode values (ReadContracts.cs), which the validation
// gate maps to the frozen wording.
using FontWeave.Core;
using FontWeave.Operations.Subset;
using Xunit;

namespace FontWeave.Tests;

public sealed class SubsetRefusalTests
{
    private static SubsetRequest Request(FontFaceRef face, IReadOnlyList<ushort>? gids,
        SubsetMode? mode = SubsetMode.RetainGids, SubsetOptions? options = null) => new()
    {
        Face = face,
        Root = gids is null
            ? new SubsetGlyphRoot { Kind = SubsetRootKind.UnicodeCodepoints, Codepoints = [] }
            : new SubsetGlyphRoot { Kind = SubsetRootKind.ExplicitGlyphSet, Gids = gids },
        Mode = mode,
        Options = options ?? new SubsetOptions()
    };

    private static FontFaceRef TrueTypeFace() => SubsetFontFixture.Face(
        (SubsetFontFixture.HeadTag, SubsetFontFixture.Head()),
        (SubsetFontFixture.MaxpTag, SubsetFontFixture.Maxp(4)),
        (SubsetFontFixture.GlyfTag, [.. GlyfImages.NoContourGlyphRecord()]),
        (SubsetFontFixture.LocaTag, GlyfImages.LongLoca([0, 12])),
        (SubsetFontFixture.CmapTag, SubsetFontFixture.CmapShell(4)));

    // S0-RF-001-VF, migrated (SV card §4 M1): the former vf-input-gate refusal
    // (StaticProfileRequired at the frozen freeze) executes since the SV slice —
    // the retained execution expectation is carried by
    // SubsetVfProfileTests.VariableTrueTypeFaceRetainExecutesWithRawVariableFamily
    // and the SV gate. This face shape (fvar present, no variable outline spine)
    // keeps a finite refusal under the profile code: A1 names fvar (SV card §4).
    [Fact]
    public void VariableFontRefuses()
    {
        var face = SubsetFontFixture.Face(
            (SubsetFontFixture.HeadTag, SubsetFontFixture.Head()),
            (SubsetFontFixture.MaxpTag, SubsetFontFixture.Maxp(4)),
            (SubsetFontFixture.FvarTag, SubsetFontFixture.Dummy()));
        var status = SubsetOperation.TryPrepare(Request(face, [0, 1]), out var result,
            out var report, out var diagnostic);
        Assert.Equal(ReadStatus.UnsupportedFormat, status);
        Assert.Equal(DiagnosticCode.OutlineProfileUnsupported, diagnostic.Code);
        Assert.Equal(SubsetFontFixture.FvarTag, diagnostic.Location.TableTag);
        Assert.Null(result);
        Assert.NotNull(report);
        FontDiagnostic retained = Assert.Single(report!.Diagnostics);
        Assert.Equal(DiagnosticCode.OutlineProfileUnsupported, retained.Code);
    }

    // S0-RF-002-UNKNOWN: an undeclared out-of-catalogue tag refuses with the pinned code.
    [Fact]
    public void UndeclaredUnknownTableRefuses()
    {
        var face = SubsetFontFixture.Face(
            (SubsetFontFixture.HeadTag, SubsetFontFixture.Head()),
            (SubsetFontFixture.MaxpTag, SubsetFontFixture.Maxp(4)),
            (SubsetFontFixture.ZzZzTag, SubsetFontFixture.Dummy()));
        var status = SubsetOperation.TryPrepare(Request(face, [0, 1]), out var result,
            out _, out var diagnostic);
        Assert.Equal(ReadStatus.UnsupportedFormat, status);
        Assert.Equal(DiagnosticCode.UnknownTable, diagnostic.Code);
        Assert.Equal((ulong)SubsetFontFixture.ZzZzTag, diagnostic.Detail);
        Assert.Null(result);
    }

    // S0-RF-002 companion: a per-table declaration admits the tag and lands in the report.
    [Fact]
    public void DeclaredUnknownTagIsAdmittedAndRecorded()
    {
        var face = SubsetFontFixture.Face(
            (SubsetFontFixture.HeadTag, SubsetFontFixture.Head()),
            (SubsetFontFixture.MaxpTag, SubsetFontFixture.Maxp(4)),
            (SubsetFontFixture.ZzZzTag, SubsetFontFixture.Dummy()));
        var options = new SubsetOptions
        {
            UnknownTables = new Dictionary<uint, SubsetUnknownTableChoice>
            {
                [SubsetFontFixture.ZzZzTag] = SubsetUnknownTableChoice.Preserve
            }
        };
        var status = SubsetOperation.TryPrepare(Request(face, [0], options: options),
            out var result, out var report, out _);
        Assert.Equal(ReadStatus.Done, status);
        Assert.NotNull(result);
        Assert.Contains(report!.OptionRecords, r =>
            r.Id == SubsetOptionId.UnknownTable && r.Tag == SubsetFontFixture.ZzZzTag &&
            r.Value == nameof(SubsetUnknownTableChoice.Preserve));
    }

    // A known table is never a caller option (W1 §2.1).
    [Fact]
    public void DeclaredKnownTableRefuses()
    {
        var face = SubsetFontFixture.Face(
            (SubsetFontFixture.HeadTag, SubsetFontFixture.Head()),
            (SubsetFontFixture.MaxpTag, SubsetFontFixture.Maxp(4)));
        var options = new SubsetOptions
        {
            UnknownTables = new Dictionary<uint, SubsetUnknownTableChoice>
            {
                [SubsetFontFixture.GsubTag] = SubsetUnknownTableChoice.Drop
            }
        };
        var status = SubsetOperation.TryPrepare(Request(face, [0], options: options),
            out var result, out _, out var diagnostic);
        Assert.Equal(ReadStatus.UnsupportedFormat, status);
        Assert.Equal(DiagnosticCode.InvalidValue, diagnostic.Code);
        Assert.Null(result);
    }

    // S0-RF-003-DSIG: a real signature refuses unless the caller drops it.
    [Fact]
    public void RealSignatureRefuses()
    {
        var face = SubsetFontFixture.Face(
            (SubsetFontFixture.HeadTag, SubsetFontFixture.Head()),
            (SubsetFontFixture.MaxpTag, SubsetFontFixture.Maxp(4)),
            (SubsetFontFixture.DsigTag, SubsetFontFixture.DsigReal()));
        var status = SubsetOperation.TryPrepare(Request(face, [0]), out var result,
            out _, out var diagnostic);
        Assert.Equal(ReadStatus.UnsupportedFormat, status);
        Assert.Equal(DiagnosticCode.SignatureRequiresDrop, diagnostic.Code);
        Assert.Null(result);
    }

    [Fact]
    public void RealSignatureDropsWhenDeclared()
    {
        var face = SubsetFontFixture.Face(
            (SubsetFontFixture.HeadTag, SubsetFontFixture.Head()),
            (SubsetFontFixture.MaxpTag, SubsetFontFixture.Maxp(4)),
            (SubsetFontFixture.DsigTag, SubsetFontFixture.DsigReal()));
        var options = new SubsetOptions { DropDigitalSignature = true };
        var status = SubsetOperation.TryPrepare(Request(face, [0], options: options),
            out var result, out var report, out _);
        Assert.Equal(ReadStatus.Done, status);
        Assert.NotNull(result);
        Assert.Contains<uint>(SubsetFontFixture.DsigTag, report!.DroppedTables);
    }

    // S0-RF-004-LAYOUT: layout retention refuses and points at S2; NoLayout drops instead.
    [Fact]
    public void LayoutFamilyRefusesByDefault()
    {
        var face = SubsetFontFixture.Face(
            (SubsetFontFixture.HeadTag, SubsetFontFixture.Head()),
            (SubsetFontFixture.MaxpTag, SubsetFontFixture.Maxp(4)),
            (SubsetFontFixture.GsubTag, SubsetFontFixture.Dummy()));
        var status = SubsetOperation.TryPrepare(Request(face, [0]), out var result,
            out _, out var diagnostic);
        Assert.Equal(ReadStatus.UnsupportedFormat, status);
        Assert.Equal(DiagnosticCode.LayoutFamilyUnsupported, diagnostic.Code);
        Assert.Null(result);
    }

    [Fact]
    public void LayoutFamilyDropsUnderNoLayout()
    {
        var face = SubsetFontFixture.Face(
            (SubsetFontFixture.HeadTag, SubsetFontFixture.Head()),
            (SubsetFontFixture.MaxpTag, SubsetFontFixture.Maxp(4)),
            (SubsetFontFixture.GsubTag, SubsetFontFixture.Dummy()));
        var options = new SubsetOptions { NoLayout = true };
        var status = SubsetOperation.TryPrepare(Request(face, [0], options: options),
            out var result, out var report, out _);
        Assert.Equal(ReadStatus.Done, status);
        Assert.NotNull(result);
        Assert.Contains<uint>(SubsetFontFixture.GsubTag, report!.DroppedTables);
    }

    // S2a RF-D (card §1.1): NoLayout and PreserveLayout are mutually exclusive —
    // the double-true request refuses at step 1 with the code-23 contract, before
    // any profile or directory work, regardless of the requested mode.
    [Theory]
    [InlineData((int)SubsetMode.RetainGids)]
    [InlineData((int)SubsetMode.CompactRemap)]
    public void PreserveLayoutMutuallyExclusiveWithNoLayout(SubsetMode mode)
    {
        var face = TrueTypeFace();
        var options = new SubsetOptions { NoLayout = true, PreserveLayout = true };
        var status = SubsetOperation.TryPrepare(Request(face, [0], mode: mode,
            options: options), out var result, out var report, out var diagnostic);
        Assert.Equal(ReadStatus.UnsupportedFormat, status);
        Assert.Equal(DiagnosticCode.LayoutFamilyUnsupported, diagnostic.Code);
        Assert.Null(result);
        Assert.NotNull(report);
        FontDiagnostic retained = Assert.Single(report!.Diagnostics);
        Assert.Equal(DiagnosticCode.LayoutFamilyUnsupported, retained.Code);
        // Step-1 refusal: the option records only form after every request guard
        // passes, so this refusal carries the diagnostic alone (W1 §2.5 ordering).
        Assert.Empty(report.OptionRecords);
    }

    // S0-RF-005-NOTDEF: an explicit exclusion expression refuses outright.
    [Fact]
    public void NotdefExclusionRefuses()
    {
        var face = TrueTypeFace();
        var options = new SubsetOptions { NoLayout = true, ExcludeNotdef = true };
        var status = SubsetOperation.TryPrepare(Request(face, [1], options: options),
            out var result, out _, out var diagnostic);
        Assert.Equal(ReadStatus.UnsupportedFormat, status);
        Assert.Equal(DiagnosticCode.NotdefExcluded, diagnostic.Code);
        Assert.Null(result);
    }

    // S0-RF-006-MODE: undeclared and out-of-domain modes refuse. The former
    // compact-remap case of this theory is the S1A-RF-COMPACT-MODE migration (SC card
    // §3 R6): CompactRemap now executes instead of refusing; its new expectations live
    // in SubsetCompactPolicyTests.
    [Theory]
    [InlineData((int)ModeCase.Undeclared)]
    [InlineData((int)ModeCase.UndefinedValue)]
    public void ModeRefusals(ModeCase testCase)
    {
        var face = TrueTypeFace();
        SubsetMode? mode = testCase switch
        {
            ModeCase.Undeclared => null,
            _ => (SubsetMode)99
        };
        var options = new SubsetOptions { NoLayout = true };
        var status = SubsetOperation.TryPrepare(Request(face, [0, 1], mode, options),
            out var result, out _, out var diagnostic);
        Assert.Equal(ReadStatus.UnsupportedFormat, status);
        Assert.Equal(DiagnosticCode.SubsetModeRequired, diagnostic.Code);
        Assert.Null(result);
    }

    public enum ModeCase : int
    {
        Undeclared = 0, UndefinedValue = 1
    }

    // S0-RF-007..010: each unsupported cmap format refuses with the pinned code.
    [Theory]
    [InlineData(2)]
    [InlineData(8)]
    [InlineData(10)]
    [InlineData(13)]
    [InlineData(14)]
    public void UnsupportedCmapFormatRefuses(ushort format)
    {
        var face = SubsetFontFixture.Face(
            (SubsetFontFixture.HeadTag, SubsetFontFixture.Head()),
            (SubsetFontFixture.MaxpTag, SubsetFontFixture.Maxp(4)),
            (SubsetFontFixture.GlyfTag, [.. GlyfImages.NoContourGlyphRecord()]),
            (SubsetFontFixture.LocaTag, GlyfImages.LongLoca([0, 12])),
            (SubsetFontFixture.CmapTag, SubsetFontFixture.CmapShell(format)));
        var options = new SubsetOptions { NoLayout = true };
        var status = SubsetOperation.TryPrepare(Request(face, [0, 1], options: options),
            out var result, out _, out var diagnostic);
        Assert.Equal(ReadStatus.UnsupportedFormat, status);
        Assert.Equal(DiagnosticCode.CmapFormatUnsupported, diagnostic.Code);
        Assert.Equal(format, (ushort)diagnostic.Detail);
        Assert.Null(result);
    }

    // S0-RF-011-POST25: the deprecated post version refuses.
    [Fact]
    public void Post25Refuses()
    {
        var face = SubsetFontFixture.Face(
            (SubsetFontFixture.HeadTag, SubsetFontFixture.Head()),
            (SubsetFontFixture.MaxpTag, SubsetFontFixture.Maxp(4)),
            (SubsetFontFixture.PostTag, SubsetFontFixture.Post25(2)));
        var options = new SubsetOptions { NoLayout = true };
        var status = SubsetOperation.TryPrepare(Request(face, [0, 1], options: options),
            out var result, out _, out var diagnostic);
        Assert.Equal(ReadStatus.UnsupportedFormat, status);
        Assert.Equal(DiagnosticCode.PostVersionDeprecated, diagnostic.Code);
        Assert.Null(result);
    }

    // S1a admission faces: broken pairing, CFF outlines and cmap-less TrueType refuse.
    [Fact]
    public void LoneGlyfWithoutLocaRefuses()
    {
        var face = SubsetFontFixture.Face(
            (SubsetFontFixture.HeadTag, SubsetFontFixture.Head()),
            (SubsetFontFixture.MaxpTag, SubsetFontFixture.Maxp(4)),
            (SubsetFontFixture.GlyfTag, [.. GlyfImages.NoContourGlyphRecord()]));
        var status = SubsetOperation.TryPrepare(Request(face, [0]), out var result,
            out _, out var diagnostic);
        Assert.Equal(ReadStatus.UnsupportedFormat, status);
        Assert.Equal(DiagnosticCode.OutlineProfileUnsupported, diagnostic.Code);
        Assert.Null(result);
    }

    [Fact]
    public void CffOutlineRefuses()
    {
        var face = SubsetFontFixture.Face(
            (SubsetFontFixture.HeadTag, SubsetFontFixture.Head()),
            (SubsetFontFixture.MaxpTag, SubsetFontFixture.Maxp(4)),
            (SubsetFontFixture.CffTag, SubsetFontFixture.Dummy()));
        var status = SubsetOperation.TryPrepare(Request(face, [0]), out var result,
            out _, out var diagnostic);
        Assert.Equal(ReadStatus.UnsupportedFormat, status);
        Assert.Equal(DiagnosticCode.OutlineProfileUnsupported, diagnostic.Code);
        Assert.Null(result);
    }

    [Fact]
    public void TrueTypeWithoutCmapRefuses()
    {
        var face = SubsetFontFixture.Face(
            (SubsetFontFixture.HeadTag, SubsetFontFixture.Head()),
            (SubsetFontFixture.MaxpTag, SubsetFontFixture.Maxp(4)),
            (SubsetFontFixture.GlyfTag, [.. GlyfImages.NoContourGlyphRecord()]),
            (SubsetFontFixture.LocaTag, GlyfImages.LongLoca([0, 12])));
        var options = new SubsetOptions { NoLayout = true };
        var status = SubsetOperation.TryPrepare(Request(face, [0], options: options),
            out var result, out _, out var diagnostic);
        Assert.Equal(ReadStatus.UnsupportedFormat, status);
        Assert.Equal(DiagnosticCode.OutlineProfileUnsupported, diagnostic.Code);
        Assert.Null(result);
    }

    // Root validation: domain, duplicates and ordering are finite refusals (W1 §1.2).
    [Theory]
    [InlineData(new ushort[] { 5 }, (int)RootCase.OutOfRange)]
    [InlineData(new ushort[] { 1, 1 }, (int)RootCase.Duplicate)]
    [InlineData(new ushort[] { 2, 1 }, (int)RootCase.Disordered)]
    public void RootGidViolationsRefuse(ushort[] gids, RootCase expected)
    {
        var face = SubsetFontFixture.Face(
            (SubsetFontFixture.HeadTag, SubsetFontFixture.Head()),
            (SubsetFontFixture.MaxpTag, SubsetFontFixture.Maxp(4)));
        var status = SubsetOperation.TryPrepare(Request(face, gids), out var result,
            out _, out var diagnostic);
        Assert.Equal(ReadStatus.InvalidFormat, status);
        Assert.Equal(expected switch
        {
            RootCase.OutOfRange => DiagnosticCode.IndexOutOfRange,
            RootCase.Duplicate => DiagnosticCode.DuplicateGlyph,
            _ => DiagnosticCode.InvalidValue
        }, diagnostic.Code);
        Assert.Null(result);
    }

    public enum RootCase : int
    {
        OutOfRange = 0, Duplicate = 1, Disordered = 2
    }

    // The Unicode root is contract text only in this slice; it refuses, never fakes a
    // resolution (card §1.1).
    [Fact]
    public void UnicodeRootRefusesAsNotImplemented()
    {
        var face = TrueTypeFace();
        var status = SubsetOperation.TryPrepare(Request(face, null), out var result,
            out _, out var diagnostic);
        Assert.Equal(ReadStatus.UnsupportedFormat, status);
        Assert.Equal(DiagnosticCode.UnsupportedOperation, diagnostic.Code);
        Assert.Null(result);
    }

    // An unbound face is a caller parameter error, not a font diagnostic (W1 §1.1).
    [Fact]
    public void UnboundFaceThrows()
    {
        var unbound = default(FontFaceRef);
        Assert.Throws<ArgumentException>(() => SubsetOperation.TryPrepare(
            Request(unbound, [0]), out _, out _, out _));
    }
}
