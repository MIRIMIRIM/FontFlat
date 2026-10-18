// TTC-face CLI alignment tests: the per-face library chains (FontFile.Parse → GetFace →
// NameTable.Parse → NameEditor.Replace → SfntWriter.Prepare, and FontInspection.TryInspect)
// already admit collection faces, so the CLI routes inspect/--table inspect/edit-rebuild
// through them; validate and subset serve collection faces through the face-domain
// directory admission (validate aggregates the whole container when --face is absent).
// The edit-rebuild head-edit refusal is pinned verbatim (UnsupportedFormat/
// UnsupportedVersion with the ttcf detail word) to prove the operations without a
// collection contract keep their standalone-only admission.
//
// The verbs run through Program.Execute (the same dictionary the process serializes), so
// the assertions read the exact receipt members an auditor sees.
using System.Text.Json;
using FontWeave;
using Xunit;

namespace FontWeave.Tests;

public sealed class CliCollectionFaceTests
{
    private const ulong Ttcf = 0x74746366;

    [Fact]
    public void NamesVerbReadsTheSecondTtcFace()
    {
        (string input, _) = TempFont();
        try
        {
            var receipt = Json(FontWeave.Cli.Program.Execute(["names", input, "--face", "1"], out int exitCode));
            Assert.Equal(0, exitCode);
            Assert.Equal(1, receipt.GetProperty("faceIndex").GetInt32());
            Assert.Equal(2, receipt.GetProperty("faceCount").GetInt32());
            Assert.Equal("TTC Face One", receipt.GetProperty("records")[0].GetProperty("text").GetString());
            Assert.Equal("Bold", receipt.GetProperty("records")[1].GetProperty("text").GetString());
        }
        finally { File.Delete(input); }
    }

    [Fact]
    public void InspectVerbListsTheSecondFaceTables()
    {
        (string input, _) = TempFont();
        try
        {
            var receipt = Json(FontWeave.Cli.Program.Execute(["inspect", input, "--face", "1"], out int exitCode));
            Assert.Equal(0, exitCode);
            Assert.Equal("ttc", receipt.GetProperty("container").GetString());
            Assert.Equal(2, receipt.GetProperty("faceCount").GetInt32());
            Assert.Equal(1, receipt.GetProperty("faceIndex").GetInt32());
            Assert.Equal(new[] { "VORG", "head", "maxp", "name" }, receipt.GetProperty("tables")
                .EnumerateArray().Select(table => table.GetProperty("tag").GetString()).ToArray());
            Assert.Equal("TTC Face One", receipt.GetProperty("names").GetProperty("records")[0]
                .GetProperty("text").GetString());
        }
        finally { File.Delete(input); }
    }

    // The typed --table inspect keeps its receipt shape, but on a collection face it
    // claims only raw inspection: the face-domain container status is Done and no
    // per-table validation status is asserted.
    [Fact]
    public void TableInspectOnTtcFaceRunsRawInspectionWithTheFaceDomainContainerStatus()
    {
        (string input, _) = TempFont();
        try
        {
            var receipt = Json(FontWeave.Cli.Program.Execute(
                ["inspect", input, "--face", "0", "--table", "name"], out int exitCode));
            Assert.Equal(0, exitCode);
            Assert.Equal(1, receipt.GetProperty("status").GetByte());
            Assert.Equal("Done", receipt.GetProperty("statusName").GetString());
            var report = receipt.GetProperty("report");
            Assert.Equal("Done", report.GetProperty("containerValidationStatusName").GetString());
            Assert.Equal("None", report.GetProperty("containerDiagnostic").GetProperty("codeName").GetString());
            var rows = report.GetProperty("tables").EnumerateArray()
                .Where(row => row.GetProperty("tagText").GetString() == "name").ToArray();
            JsonElement nameRow = Assert.Single(rows);
            Assert.Equal("Done", nameRow.GetProperty("inspectionStatusName").GetString());
            Assert.Equal(JsonValueKind.Null, nameRow.GetProperty("validationStatus").ValueKind);
        }
        finally { File.Delete(input); }
    }

    [Fact]
    public void TableInspectWithRawOnTtcFaceCarriesTheRawPayload()
    {
        (string input, _) = TempFont();
        try
        {
            var receipt = Json(FontWeave.Cli.Program.Execute(
                ["inspect", input, "--face", "0", "--table", "name", "--show-raw"], out int exitCode));
            Assert.Equal(0, exitCode);
            Assert.Equal("Done", receipt.GetProperty("raw").GetProperty("statusName").GetString());
            Assert.Equal("name", receipt.GetProperty("raw").GetProperty("tagText").GetString());
            Assert.False(string.IsNullOrEmpty(receipt.GetProperty("raw").GetProperty("payloadHex").GetString()));
        }
        finally { File.Delete(input); }
    }

    // edit-rebuild on a TTC face repacks the selected face as an independent sfnt: the
    // output container is a standalone sfnt holding only that face's tables, with the
    // replacement name record in place.
    [Fact]
    public void EditRebuildRewritesTheFirstTtcFaceIntoAStandaloneSfnt()
    {
        (string input, string output) = TempFont();
        try
        {
            var receipt = Json(FontWeave.Cli.Program.Execute(
                ["edit-rebuild", input, "--record", "0", "--text", "Edited Zero", "--output", output],
                out int exitCode));
            Assert.Equal(0, exitCode);
            Assert.Equal("Done", receipt.GetProperty("statusName").GetString());
            Assert.True(receipt.GetProperty("outputWritten").GetBoolean());
            Assert.Equal(0, receipt.GetProperty("recordOrdinal").GetInt32());
            Assert.Equal("utf16be", receipt.GetProperty("textEncoding").GetString());
            // The typed budget/decision members have no collection counterpart and stay null.
            Assert.Equal(JsonValueKind.Null, receipt.GetProperty("usage").ValueKind);
            Assert.Equal(JsonValueKind.Null, receipt.GetProperty("decisions").ValueKind);

            byte[] written = File.ReadAllBytes(output);
            Assert.NotEqual(0x74746366u, System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(written));
            FontFile repacked = FontFile.Parse(written);
            Assert.False(repacked.IsCollection);
            Assert.Equal(1, repacked.FaceCount);
            Assert.Equal(new[] { "head", "maxp", "name" }, repacked.GetFace(0).Tables
                .Select(table => table.Tag).ToArray());
            NameTable names = NameTable.Parse(repacked.GetFace(0).GetTable("name").Data);
            Assert.Equal("Edited Zero", names.Records[0].Text);
            Assert.Equal("Regular", names.Records[1].Text);
        }
        finally { Cleanup(input, output); }
    }

    [Fact]
    public void EditRebuildRewritesTheSecondTtcFaceToo()
    {
        (string input, string output) = TempFont();
        try
        {
            var receipt = Json(FontWeave.Cli.Program.Execute(
                ["edit-rebuild", input, "--face", "1", "--record", "0", "--text", "Edited One",
                 "--output", output], out int exitCode));
            Assert.Equal(0, exitCode);
            Assert.Equal(1, receipt.GetProperty("faceIndex").GetInt32());
            FontFile repacked = FontFile.Parse(File.ReadAllBytes(output));
            Assert.False(repacked.IsCollection);
            Assert.Equal(new[] { "VORG", "head", "maxp", "name" }, repacked.GetFace(0).Tables
                .Select(table => table.Tag).ToArray());
            NameTable names = NameTable.Parse(repacked.GetFace(0).GetTable("name").Data);
            Assert.Equal("Edited One", names.Records[0].Text);
        }
        finally { Cleanup(input, output); }
    }

    // Head edits have no per-face collection chain: they fall through to the typed
    // admission and keep the exact ttcf refusal the typed path always produced.
    [Fact]
    public void EditRebuildKeepsRefusingHeadEditsOnTtcFacesWithTheFrozenDiagnostic()
    {
        (string input, string output) = TempFont();
        try
        {
            var receipt = Json(FontWeave.Cli.Program.Execute(
                ["edit-rebuild", input, "--record", "0", "--text", "X", "--head-revision", "5",
                 "--output", output], out int exitCode));
            Assert.Equal(1, exitCode);
            Assert.Equal("UnsupportedFormat", receipt.GetProperty("statusName").GetString());
            Assert.Equal("UnsupportedVersion", receipt.GetProperty("diagnostic").GetProperty("codeName").GetString());
            Assert.Equal(Ttcf, receipt.GetProperty("diagnostic").GetProperty("detail").GetUInt64());
            Assert.False(receipt.GetProperty("outputWritten").GetBoolean());
            Assert.False(File.Exists(output));
        }
        finally { Cleanup(input, output); }
    }

    // validate --face N serves one collection face through the same face-domain typed
    // admission a standalone face gets: the container status is Done and every table's
    // strict validation ran.
    [Fact]
    public void ValidateServesEachTtcFaceThroughTheFaceDomainDirectory()
    {
        for (int faceIndex = 0; faceIndex < 2; faceIndex++)
        {
            (string input, _) = TempValidatableFont();
            try
            {
                var receipt = Json(FontWeave.Cli.Program.Execute(
                    ["validate", input, "--face", faceIndex.ToString()], out int exitCode));
                Assert.Equal(0, exitCode);
                Assert.Equal("Done", receipt.GetProperty("statusName").GetString());
                Assert.Equal(faceIndex, receipt.GetProperty("faceIndex").GetInt32());
                var report = receipt.GetProperty("report");
                Assert.Equal("Done", report.GetProperty("containerValidationStatusName").GetString());
                Assert.True(report.GetProperty("isCollection").GetBoolean());
                foreach (var row in report.GetProperty("tables").EnumerateArray())
                    Assert.Equal("Done", row.GetProperty("validationStatusName").GetString());
            }
            finally { File.Delete(input); }
        }
    }

    // validate without --face over a TTC aggregates every face into one receipt: per-face
    // reports listed under faces, the aggregate succeeds only when every face does.
    [Fact]
    public void ValidateWithoutFaceAggregatesEveryTtcFace()
    {
        (string input, _) = TempValidatableFont();
        try
        {
            var receipt = Json(FontWeave.Cli.Program.Execute(["validate", input], out int exitCode));
            Assert.Equal(0, exitCode);
            Assert.Equal("Done", receipt.GetProperty("statusName").GetString());
            Assert.Equal(JsonValueKind.Null, receipt.GetProperty("faceIndex").ValueKind);
            Assert.Equal(2, receipt.GetProperty("faceCount").GetInt32());
            var faces = receipt.GetProperty("faces").EnumerateArray().ToArray();
            Assert.Equal(2, faces.Length);
            for (int i = 0; i < 2; i++)
            {
                Assert.Equal(i, faces[i].GetProperty("faceIndex").GetInt32());
                Assert.Equal("Done", faces[i].GetProperty("statusName").GetString());
                Assert.Equal(i, faces[i].GetProperty("report").GetProperty("faceIndex").GetInt32());
            }
        }
        finally { File.Delete(input); }
    }

    // A face directory that declares a table beyond the input fails only that face: the
    // aggregate refuses with the failure's diagnostic, whose location carries the face
    // index, and the surviving face's Done report stays listed beside it.
    [Fact]
    public void ValidateAggregateLocatesABrokenFaceDirectoryByFaceIndex()
    {
        byte[] corrupt = CliTtcFixture.ValidatableTwoFaceTtc();
        int faceOne = (int)System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(corrupt.AsSpan(16));
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(
            corrupt.AsSpan(faceOne + 12 + 8), 0xFFFFFFF0);
        (string input, _) = TempValidatableFont(corrupt);
        try
        {
            var receipt = Json(FontWeave.Cli.Program.Execute(["validate", input], out int exitCode));
            Assert.Equal(1, exitCode);
            Assert.Equal("InvalidOffset", receipt.GetProperty("statusName").GetString());
            Assert.Equal(1, receipt.GetProperty("diagnostic").GetProperty("location")
                .GetProperty("faceIndex").GetInt32());
            var faces = receipt.GetProperty("faces").EnumerateArray().ToArray();
            Assert.Equal(2, faces.Length);
            Assert.Equal("Done", faces[0].GetProperty("statusName").GetString());
            Assert.Equal("InvalidOffset", faces[1].GetProperty("statusName").GetString());
            Assert.Equal(1, faces[1].GetProperty("report").GetProperty("faceIndex").GetInt32());
        }
        finally { File.Delete(input); }
    }

    [Fact]
    public void ValidateWithFaceServesTheBrokenFaceDiagnostic()
    {
        byte[] corrupt = CliTtcFixture.ValidatableTwoFaceTtc();
        int faceOne = (int)System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(corrupt.AsSpan(16));
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(
            corrupt.AsSpan(faceOne + 12 + 8), 0xFFFFFFF0);
        (string input, _) = TempValidatableFont(corrupt);
        try
        {
            var receipt = Json(FontWeave.Cli.Program.Execute(
                ["validate", input, "--face", "1"], out int exitCode));
            Assert.Equal(1, exitCode);
            Assert.Equal("InvalidOffset", receipt.GetProperty("statusName").GetString());
            Assert.Equal(1, receipt.GetProperty("faceIndex").GetInt32());
            Assert.Equal(1, receipt.GetProperty("diagnostic").GetProperty("location")
                .GetProperty("faceIndex").GetInt32());
        }
        finally { File.Delete(input); }
    }

    // The standalone single-face receipt keeps its shape: one report at the top level,
    // no faces array, through the same face-domain admission.
    [Fact]
    public void StandaloneValidateKeepsItsSingleFaceReceipt()
    {
        (string input, _) = TempStandaloneFont();
        try
        {
            var receipt = Json(FontWeave.Cli.Program.Execute(["validate", input], out int exitCode));
            Assert.Equal(0, exitCode);
            Assert.Equal("Done", receipt.GetProperty("statusName").GetString());
            Assert.Equal(0, receipt.GetProperty("faceIndex").GetInt32());
            Assert.Equal("Done", receipt.GetProperty("report")
                .GetProperty("containerValidationStatusName").GetString());
            Assert.False(receipt.GetProperty("report").GetProperty("isCollection").GetBoolean());
            Assert.DoesNotContain(receipt.EnumerateObject(), property => property.Name == "faces");
        }
        finally { File.Delete(input); }
    }

    [Fact]
    public void SubsetEmitsTheSelectedTtcFaceAsAStandaloneSfnt()
    {
        (string input, string output) = TempValidatableFont();
        try
        {
            var receipt = Json(FontWeave.Cli.Program.Execute(
                ["subset", input, "--mode", "retain", "--gids", "0", "--output", output],
                out int exitCode));
            Assert.Equal(0, exitCode);
            Assert.Equal("Done", receipt.GetProperty("statusName").GetString());
            Assert.True(receipt.GetProperty("outputWritten").GetBoolean());

            byte[] written = File.ReadAllBytes(output);
            Assert.NotEqual(Ttcf, (ulong)System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(written));
            FontFile repacked = FontFile.Parse(written);
            Assert.False(repacked.IsCollection);
            // The output holds exactly the selected face's tables: face 1's VORG stays
            // behind in the container.
            Assert.Equal(new[] { "head", "maxp", "name" }, repacked.GetFace(0).Tables
                .Select(table => table.Tag).ToArray());
        }
        finally { Cleanup(input, output); }
    }

    private static JsonElement Json(Dictionary<string, object?> receipt) =>
        JsonSerializer.SerializeToElement(receipt);

    private static (string Input, string Output) TempFont()
    {
        string input = Path.Combine(Path.GetTempPath(), $"fontweave-ttc-{Guid.NewGuid():N}.ttc");
        File.WriteAllBytes(input, CliTtcFixture.TwoFaceTtc());
        string output = Path.Combine(Path.GetTempPath(), $"fontweave-ttc-{Guid.NewGuid():N}.out");
        return (input, output);
    }

    private static (string Input, string Output) TempValidatableFont(byte[]? bytes = null)
    {
        string input = Path.Combine(Path.GetTempPath(), $"fontweave-ttc-{Guid.NewGuid():N}.ttc");
        File.WriteAllBytes(input, bytes ?? CliTtcFixture.ValidatableTwoFaceTtc());
        string output = Path.Combine(Path.GetTempPath(), $"fontweave-ttc-{Guid.NewGuid():N}.out");
        return (input, output);
    }

    private static (string Input, string Output) TempStandaloneFont()
    {
        string input = Path.Combine(Path.GetTempPath(), $"fontweave-sfnt-{Guid.NewGuid():N}.ttf");
        File.WriteAllBytes(input, SubsetFontFixture.Sfnt(false,
            (SubsetFontFixture.HeadTag, CliTtcFixture.ValidHead()),
            (SubsetFontFixture.MaxpTag, CliTtcFixture.Maxp()),
            (SubsetFontFixture.NameTag, CliTtcFixture.Name(CliTtcFixture.FaceZeroNameRecords))));
        string output = Path.Combine(Path.GetTempPath(), $"fontweave-sfnt-{Guid.NewGuid():N}.out");
        return (input, output);
    }

    private static void Cleanup(string input, string output)
    {
        File.Delete(input);
        if (File.Exists(output)) File.Delete(output);
    }
}
