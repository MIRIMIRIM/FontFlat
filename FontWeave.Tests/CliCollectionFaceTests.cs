// TTC-face CLI alignment tests: the per-face library chains (FontFile.Parse → GetFace →
// NameTable.Parse → NameEditor.ReplaceText → SfntWriter.Prepare, and FontInspection.TryInspect)
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

    // retain --face over the subsettable container: each face splices its own glyphs
    // (face 0's composite pulls the closure), emits an independently readable sfnt, and
    // never carries the other face's tables — face 1's gasp stays behind in face 0 output.
    [Fact]
    public void SubsetRetainOnEachTtcFaceEmitsItsOwnClosureAndTables()
    {
        byte[] inputBytes = CliTtcFixture.SubsettableTwoFaceTtc();
        for (int faceIndex = 0; faceIndex < 2; faceIndex++)
        {
            (string input, string output) = TempValidatableFont(inputBytes);
            try
            {
                var receipt = Json(FontWeave.Cli.Program.Execute(
                    ["subset", input, "--face", faceIndex.ToString(), "--mode", "retain",
                     "--gids", faceIndex == 0 ? "1" : "2", "--output", output],
                    out int exitCode));
                Assert.Equal(0, exitCode);
                Assert.Equal("Done", receipt.GetProperty("statusName").GetString());
                Assert.Equal(faceIndex == 0 ? new[] { 2 } : Array.Empty<int>(),
                    receipt.GetProperty("report").GetProperty("closureAddedGids").EnumerateArray()
                        .Select(gid => gid.GetInt32()).ToArray());

                byte[] written = File.ReadAllBytes(output);
                FontFace face = FontFile.Parse(written).GetFace(0);
                Assert.Equal(faceIndex == 0
                        ? new[] { "cmap", "glyf", "head", "loca", "maxp" }
                        : new[] { "cmap", "gasp", "glyf", "head", "loca", "maxp" },
                    face.Tables.Select(table => table.Tag).ToArray());
                Assert.True(Table(written, SubsetFontFixture.MaxpTag, out int maxpAt));
                Assert.Equal(faceIndex == 0 ? 4 : 3, System.Buffers.Binary.BinaryPrimitives
                    .ReadUInt16BigEndian(written.AsSpan(maxpAt + 4)));

                Assert.True(Table(written, SubsetFontFixture.GlyfTag, out _, out int glyfLength));
                Assert.True(Table(written, SubsetFontFixture.LocaTag, out int locaAt, out int locaLength));
                if (faceIndex == 0)
                {
                    // Retained slots move verbatim; slot 3 becomes empty.
                    Assert.Equal(46, glyfLength);
                    Assert.Equal([0, 18, 34, 46, 46], LongOffsets(written, locaAt, locaLength));
                }
                else
                {
                    // Slot 1 becomes empty; the simple glyph rides slot 2 verbatim.
                    Assert.Equal(30, glyfLength);
                    Assert.Equal([0, 12, 12, 30], LongOffsets(written, locaAt, locaLength));
                }
            }
            finally { Cleanup(input, output); }
        }
    }

    // compact --face over the subsettable container: each face remaps over its own frozen
    // map, publishes the map artifact, and emits a standalone sfnt on the output domain
    // (face 1's 0x42 mapping re-slots from input 2 to output GID 1; gasp stays with its
    // own face).
    [Fact]
    public void SubsetCompactOnEachTtcFaceEmitsTheRemappedFaceWithItsMap()
    {
        byte[] inputBytes = CliTtcFixture.SubsettableTwoFaceTtc();
        for (int faceIndex = 0; faceIndex < 2; faceIndex++)
        {
            (string input, string output) = TempValidatableFont(inputBytes);
            string map = Path.Combine(Path.GetTempPath(), $"fontweave-ttc-{Guid.NewGuid():N}.map");
            try
            {
                var receipt = Json(FontWeave.Cli.Program.Execute(
                    ["subset", input, "--face", faceIndex.ToString(), "--mode", "compact",
                     "--gids", faceIndex == 0 ? "1" : "2", "--map", map, "--output", output],
                    out int exitCode));
                Assert.Equal(0, exitCode);
                Assert.Equal("Done", receipt.GetProperty("statusName").GetString());
                Assert.True(receipt.GetProperty("outputWritten").GetBoolean());
                var mapReceipt = receipt.GetProperty("map");
                Assert.Equal(faceIndex == 0 ? 3 : 2, mapReceipt.GetProperty("entryCount").GetInt32());
                Assert.True(File.Exists(map));
                Assert.Equal(mapReceipt.GetProperty("sha256").GetString(),
                    Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(map)))
                        .ToLowerInvariant());

                byte[] written = File.ReadAllBytes(output);
                FontFace face = FontFile.Parse(written).GetFace(0);
                Assert.Equal(faceIndex == 0
                        ? new[] { "cmap", "glyf", "head", "loca", "maxp" }
                        : new[] { "cmap", "gasp", "glyf", "head", "loca", "maxp" },
                    face.Tables.Select(table => table.Tag).ToArray());
                Assert.True(Table(written, SubsetFontFixture.MaxpTag, out int maxpAt));
                Assert.Equal(faceIndex == 0 ? 3 : 2, System.Buffers.Binary.BinaryPrimitives
                    .ReadUInt16BigEndian(written.AsSpan(maxpAt + 4)));

                // The rebuilt cmap's format 12 run rides the output domain: face 0 keeps
                // 0x41 → 1; face 1's 0x42 mapping re-slots from input 2 to output 1.
                Assert.True(Table(written, SubsetFontFixture.CmapTag, out int cmapAt, out int cmapLength));
                (uint start, uint gid) = faceIndex == 0 ? (0x41u, 1u) : (0x42u, 1u);
                Assert.Equal(start, System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(
                    written.AsSpan(cmapAt + cmapLength - 12)));
                Assert.Equal(gid, System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(
                    written.AsSpan(cmapAt + cmapLength - 4)));
            }
            finally
            {
                Cleanup(input, output);
                File.Delete(map);
            }
        }
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

    // The TTC edit-rebuild path carries the standalone typed path's strict-UTF-16 rule:
    // a legacy code-page record refuses instead of receiving UTF-16 bytes its declared
    // encoding cannot describe (and the two CLI forms report the same refusal).
    [Fact]
    public void EditRebuildRefusesUtf16TextOnALegacyTtcNameRecord()
    {
        byte[] name = CliTtcFixture.MacRomanPlusUnicodeName();
        byte[] ttc = CliTtcFixture.Ttc(
            new[] { (CliTtcFixture.HeadTag, CliTtcFixture.ValidHead()),
                (CliTtcFixture.MaxpTag, CliTtcFixture.Maxp()),
                (CliTtcFixture.NameTag, name) });
        (string input, string output) = TempValidatableFont(ttc);
        string standalone = Path.Combine(Path.GetTempPath(),
            $"fontweave-sfnt-{Guid.NewGuid():N}.ttf");
        string typedOutput = Path.Combine(Path.GetTempPath(),
            $"fontweave-sfnt-{Guid.NewGuid():N}.out");
        File.WriteAllBytes(standalone, SubsetFontFixture.Sfnt(false,
            (SubsetFontFixture.HeadTag, CliTtcFixture.ValidHead()),
            (SubsetFontFixture.MaxpTag, CliTtcFixture.Maxp()),
            (SubsetFontFixture.NameTag, name)));
        try
        {
            FontDataException collection = Assert.Throws<FontDataException>(() =>
                FontWeave.Cli.Program.Execute(
                    ["edit-rebuild", input, "--record", "0", "--text", "AB",
                     "--output", output], out _));
            Assert.Equal(FontError.UnsupportedFormat, collection.Error);
            Assert.False(File.Exists(output));

            var receipt = Json(FontWeave.Cli.Program.Execute(
                ["edit-rebuild", standalone, "--record", "0", "--text", "AB",
                 "--output", typedOutput], out int exitCode));
            Assert.Equal(1, exitCode);
            Assert.Equal("UnsupportedFormat", receipt.GetProperty("statusName").GetString());
            Assert.False(receipt.GetProperty("outputWritten").GetBoolean());
            Assert.False(File.Exists(typedOutput));
        }
        finally
        {
            Cleanup(input, output);
            Cleanup(standalone, typedOutput);
        }
    }

    // The whole-container validate runs the container chain first: a TTC2 version word
    // without the DSIG block it introduces is a container failure even though both face
    // directories read fine.
    [Fact]
    public void ValidateRefusesATtc2HeaderWithoutItsDsigBlock()
    {
        byte[] corrupt = CliTtcFixture.ValidatableTwoFaceTtc();
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(
            corrupt.AsSpan(4), 0x00020000);
        (string input, _) = TempValidatableFont(corrupt);
        try
        {
            var receipt = Json(FontWeave.Cli.Program.Execute(["validate", input], out int exitCode));
            Assert.Equal(1, exitCode);
            Assert.Equal("InvalidFormat", receipt.GetProperty("statusName").GetString());
            Assert.Equal("InvalidValue", receipt.GetProperty("diagnostic")
                .GetProperty("codeName").GetString());
        }
        finally { File.Delete(input); }
    }

    // The collection's face-count ceiling is the container's, not a per-face budget: a
    // collection past MaxFaces refuses before any face is admitted, even when every face
    // is individually valid.
    [Fact]
    public void ValidateRefusesACollectionPastMaxFaces()
    {
        var face = new[]
        {
            (CliTtcFixture.HeadTag, CliTtcFixture.ValidHead()),
            (CliTtcFixture.MaxpTag, CliTtcFixture.Maxp()),
            (CliTtcFixture.NameTag, CliTtcFixture.Name(CliTtcFixture.FaceZeroNameRecords))
        };
        byte[] oversized = CliTtcFixture.Ttc([.. Enumerable.Repeat(face, 129)]);
        (string input, _) = TempValidatableFont(oversized);
        try
        {
            var receipt = Json(FontWeave.Cli.Program.Execute(["validate", input], out int exitCode));
            Assert.Equal(1, exitCode);
            Assert.Equal("ResourceLimit", receipt.GetProperty("statusName").GetString());
            Assert.Equal("BudgetExceeded", receipt.GetProperty("diagnostic")
                .GetProperty("codeName").GetString());
            Assert.Equal(JsonValueKind.Null, receipt.GetProperty("faceCount").ValueKind);
            Assert.Empty(receipt.GetProperty("faces").EnumerateArray());
        }
        finally { File.Delete(input); }
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

    private static bool Table(byte[] sfnt, uint tag, out int offset) =>
        Table(sfnt, tag, out offset, out _);

    private static bool Table(byte[] sfnt, uint tag, out int offset, out int length)
    {
        offset = 0;
        length = 0;
        ushort count = System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(sfnt.AsSpan(4));
        for (int i = 0; i < count; i++)
        {
            int record = 12 + 16 * i;
            if (System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(sfnt.AsSpan(record)) != tag) continue;
            offset = (int)System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(sfnt.AsSpan(record + 8));
            length = (int)System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(sfnt.AsSpan(record + 12));
            return true;
        }
        return false;
    }

    private static int[] LongOffsets(byte[] sfnt, int offset, int length)
    {
        var offsets = new int[length / 4];
        for (int i = 0; i < offsets.Length; i++)
            offsets[i] = (int)System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(
                sfnt.AsSpan(offset + 4 * i));
        return offsets;
    }

    private static void Cleanup(string input, string output)
    {
        File.Delete(input);
        if (File.Exists(output)) File.Delete(output);
    }
}
