// P1-4: the tolerant OS/2 validation posture. Strict stays the spec audit; the
// tolerant entry admits the documented commercial-font dialect and reports each
// deviation, so the caller can audit what was waved through. Subset never validates
// OS/2 (it rides the raw-preserve policy), so these tests pin the inspection surface.
using System.Buffers.Binary;
using FontWeave.Core;
using FontWeave.Inspection;
using FontWeave.Tables;
using Xunit;

namespace FontWeave.Tests;

public sealed class Os2ToleranceTests
{
    private static FontFaceRef Face(byte[] os2) => SubsetFontFixture.Face(
        (SubsetFontFixture.HeadTag, SubsetFontFixture.Head()),
        (SubsetFontFixture.MaxpTag, SubsetFontFixture.Maxp(4)),
        (SubsetFontFixture.Os2Tag, os2));

    [Fact]
    public void StrictRefusesLegacyFsSelectionAndCodePageBits()
    {
        var status = FontInspection.TryValidate(Face(SubsetFontFixture.Os2Legacy()),
            out var report, out var diagnostic);
        Assert.Equal(ReadStatus.InvalidFormat, status);
        Assert.Equal(DiagnosticCode.InvalidValue, diagnostic.Code);
        Assert.Equal(DataField.Flags, diagnostic.Location.Field);
        Assert.Equal(62UL, diagnostic.Location.Offset);
        Assert.NotNull(report);
        Assert.Empty(report!.ToleratedFindings);
    }

    [Fact]
    public void TolerantAdmitsLegacyBitsAndReportsEachFinding()
    {
        var options = new FontValidationOptions { TolerateLegacyDialects = true };
        var status = FontInspection.TryValidate(Face(SubsetFontFixture.Os2Legacy()),
            options, out var report, out var diagnostic);
        Assert.Equal(ReadStatus.Done, status);
        Assert.Equal(DiagnosticCode.None, diagnostic.Code);
        Assert.NotNull(report);
        Assert.Equal(3, report!.ToleratedFindings.Count);
        Assert.Contains(report.ToleratedFindings, f =>
            f.Rule == "fsSelection.version-coupled-bits" && f.Field == DataField.Flags &&
            f.Offset == 62 && f.Value == 0x0101 && f.Expected == 0x007F);
        Assert.Contains(report.ToleratedFindings, f =>
            f.Rule == "ulCodePageRange1.reserved-bits" && f.Offset == 78 &&
            f.Value == 0x00000201);
        Assert.Contains(report.ToleratedFindings, f =>
            f.Rule == "ulCodePageRange2.reserved-bits" && f.Offset == 82 &&
            f.Value == 0x00000001 && f.Expected == 0xFFFF0000);
    }

    [Fact]
    public void TolerantStillRefusesStructuralErrors()
    {
        var options = new FontValidationOptions { TolerateLegacyDialects = true };
        // fsType bit 0 (restricted license embedding) is not a legacy dialect.
        byte[] os2 = SubsetFontFixture.Os2Legacy();
        os2[9] = 0x01;
        var status = FontInspection.TryValidate(Face(os2), options, out _,
            out var diagnostic);
        Assert.Equal(ReadStatus.InvalidFormat, status);
        Assert.Equal(DiagnosticCode.InvalidValue, diagnostic.Code);
        Assert.Equal(DataField.Flags, diagnostic.Location.Field);
        Assert.Equal(8UL, diagnostic.Location.Offset);
    }

    [Fact]
    public void TolerantStillRefusesBitsOutsideTheSelectionVocabulary()
    {
        var options = new FontValidationOptions { TolerateLegacyDialects = true };
        byte[] os2 = SubsetFontFixture.Os2Legacy(fsSelection: 0x8000);
        var status = FontInspection.TryValidate(Face(os2), options, out _,
            out var diagnostic);
        Assert.Equal(ReadStatus.InvalidFormat, status);
        Assert.Equal(62UL, diagnostic.Location.Offset);
    }

    [Fact]
    public void StrictAcceptsAssignedCodePageRange2Bits()
    {
        // Bits 16..31 are code pages 48..63; the low half (32..47) is reserved.
        byte[] os2 = SubsetFontFixture.Os2Strict();
        BinaryPrimitives.WriteUInt32BigEndian(os2.AsSpan(82), 0x00010000);
        var status = FontInspection.TryValidate(Face(os2), out _, out var diagnostic);
        Assert.Equal(ReadStatus.Done, status);
        Assert.Equal(DiagnosticCode.None, diagnostic.Code);
    }

    [Fact]
    public void StrictRefusesReservedCodePageRange2Bits()
    {
        byte[] os2 = SubsetFontFixture.Os2Strict();
        BinaryPrimitives.WriteUInt32BigEndian(os2.AsSpan(82), 0x00000001);
        var status = FontInspection.TryValidate(Face(os2), out _, out var diagnostic);
        Assert.Equal(ReadStatus.InvalidFormat, status);
        Assert.Equal(82UL, diagnostic.Location.Offset);
    }

    [Fact]
    public void TolerantTryReadReturnsTheModelAndFindings()
    {
        byte[] bytes = SubsetFontFixture.Os2Legacy();
        Assert.Equal(ReadStatus.Done, Os2View.TryOpen(bytes, out var view, out _,
            DataLocation.Anonymous(SubsetFontFixture.Os2Tag)));
        var findings = new List<FontToleranceFinding>();
        var status = view.TryRead(tolerateLegacyDialects: true, findings, out var model,
            out var diagnostic);
        Assert.Equal(ReadStatus.Done, status);
        Assert.Equal(DiagnosticCode.None, diagnostic.Code);
        Assert.NotNull(model);
        Assert.Equal(3, findings.Count);
    }

    [Fact]
    public void StrictTryReadStillRefusesTheLegacyTable()
    {
        byte[] bytes = SubsetFontFixture.Os2Legacy();
        Assert.Equal(ReadStatus.Done, Os2View.TryOpen(bytes, out var view, out _,
            DataLocation.Anonymous(SubsetFontFixture.Os2Tag)));
        var status = view.TryRead(out var model, out var diagnostic);
        Assert.Equal(ReadStatus.InvalidFormat, status);
        Assert.Null(model);
        Assert.Equal(DiagnosticCode.InvalidValue, diagnostic.Code);
    }

    [Fact]
    public void TolerantModelRoundTripsThroughTryWithAndCompiler()
    {
        byte[] bytes = SubsetFontFixture.Os2Legacy();
        Assert.Equal(ReadStatus.Done, Os2View.TryOpen(bytes, out var view, out _,
            DataLocation.Anonymous(SubsetFontFixture.Os2Tag)));
        Assert.Equal(ReadStatus.Done, view.TryRead(tolerateLegacyDialects: true, null,
            out var model, out var diagnostic));
        Assert.True(model!.TolerateLegacyDialects);
        // The same values with a different weight: TryWith keeps the tolerant posture.
        Assert.Equal(ReadStatus.Done, model.TryWith(model.Values with { WeightClass = 500 },
            out var updated, out diagnostic));
        Assert.True(updated!.TolerateLegacyDialects);
        Assert.Equal(ReadStatus.Done, Os2TableCompiler.TryCompile(updated, out var output,
            out diagnostic));
        Assert.NotNull(output);
        // The compiled bytes keep the legacy bits and re-read tolerantly.
        Assert.Equal(ReadStatus.Done, Os2View.TryOpen(output, out var roundTrip, out _,
            DataLocation.Anonymous(SubsetFontFixture.Os2Tag)));
        Assert.Equal(ReadStatus.Done, roundTrip.TryRead(tolerateLegacyDialects: true, null,
            out var roundTripModel, out diagnostic));
        Assert.Equal(500, roundTripModel!.Values.WeightClass);
        // The strict entry still refuses the same bytes.
        Assert.Equal(ReadStatus.InvalidFormat, roundTrip.TryRead(out _, out diagnostic));
    }

    [Fact]
    public void TolerantCompileEditRewritesWeightOnALegacyTable()
    {
        byte[] bytes = SubsetFontFixture.Os2Legacy();
        var status = Os2TableCompiler.TryCompile(bytes, new Os2MetadataEdit(500, 5),
            out var output, out var diagnostic, tolerateLegacyDialects: true);
        Assert.Equal(ReadStatus.Done, status);
        Assert.Equal(DiagnosticCode.None, diagnostic.Code);
        Assert.NotNull(output);
        Assert.Equal(ReadStatus.Done, Os2View.TryOpen(output, out var view, out _,
            DataLocation.Anonymous(SubsetFontFixture.Os2Tag)));
        Assert.Equal(ReadStatus.Done, view.TryRead(tolerateLegacyDialects: true, null,
            out var model, out diagnostic));
        Assert.Equal(500, model!.Values.WeightClass);
        // The strict edit path still refuses the same input.
        Assert.Equal(ReadStatus.InvalidFormat, Os2TableCompiler.TryCompile(bytes,
            new Os2MetadataEdit(500, 5), out _, out diagnostic));
    }

    [Fact]
    public void StrictAcceptsTheSpecLegalTable()
    {
        var status = FontInspection.TryValidate(Face(SubsetFontFixture.Os2Strict()),
            out var report, out var diagnostic);
        Assert.Equal(ReadStatus.Done, status);
        Assert.Equal(DiagnosticCode.None, diagnostic.Code);
        Assert.Empty(report!.ToleratedFindings);
    }
}
