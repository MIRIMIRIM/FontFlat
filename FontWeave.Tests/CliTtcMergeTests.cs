// ttc-merge CLI tests: the SfntCollectionWriter assembles the parsed faces of the given
// inputs into one TTC 1.0 container with every face's tables landing independently. The
// merged output must re-parse as a collection, keep each face's table set, and pass the
// same aggregate validate the container faces serve; the inspect verb's independent
// checksum recomputation pins the rebased directories and resealed head adjustments.
using System.Text.Json;
using FontWeave;
using Xunit;

namespace FontWeave.Tests;

public sealed class CliTtcMergeTests
{
    private const uint HeadTag = 0x68656164, MaxpTag = 0x6D617870, NameTag = 0x6E616D65,
        VorgTag = 0x564F5247;

    [Fact]
    public void TwoStandaloneFacesMergeIntoAReadableCollection()
    {
        (string zero, string one, string output) = TempPaths();
        try
        {
            File.WriteAllBytes(zero, Standalone("Merge Zero"));
            File.WriteAllBytes(one, Standalone("Merge One", extraVorg: true));
            var receipt = Json(FontWeave.Cli.Program.Execute(
                ["ttc-merge", zero, one, "--output", output], out int exitCode));
            Assert.Equal(0, exitCode);
            Assert.Equal("Done", receipt.GetProperty("statusName").GetString());
            Assert.True(receipt.GetProperty("outputWritten").GetBoolean());
            Assert.Equal(2, receipt.GetProperty("faceCount").GetInt32());
            var facts = receipt.GetProperty("faces").EnumerateArray().ToArray();
            Assert.Equal(3, facts[0].GetProperty("tableCount").GetInt32());
            Assert.Equal(4, facts[1].GetProperty("tableCount").GetInt32());
            Assert.Equal(receipt.GetProperty("outputLength").GetInt32(),
                receipt.GetProperty("usage").GetProperty("outputBytes").GetInt32());

            byte[] written = File.ReadAllBytes(output);
            Assert.Equal(receipt.GetProperty("outputLength").GetInt32(), written.Length);
            FontFile merged = FontFile.Parse(written);
            Assert.True(merged.IsCollection);
            Assert.Equal(2, merged.FaceCount);
            Assert.Equal(new[] { "head", "maxp", "name" }, merged.GetFace(0).Tables
                .Select(table => table.Tag).ToArray());
            Assert.Equal(new[] { "VORG", "head", "maxp", "name" }, merged.GetFace(1).Tables
                .Select(table => table.Tag).ToArray());
            Assert.Equal("Merge Zero", NameRecordZero(merged.GetFace(0)));
            Assert.Equal("Merge One", NameRecordZero(merged.GetFace(1)));

            // The aggregate validate serves the merged container, and the inspect verb's
            // own checksum recomputation accepts every rebased table and resealed head.
            var validated = Json(FontWeave.Cli.Program.Execute(["validate", output], out int validateExit));
            Assert.Equal(0, validateExit);
            Assert.Equal("Done", validated.GetProperty("statusName").GetString());
            Assert.Equal(2, validated.GetProperty("faceCount").GetInt32());
            for (int face = 0; face < 2; face++)
            {
                var inspected = Json(FontWeave.Cli.Program.Execute(
                    ["inspect", output, "--face", face.ToString()], out int inspectExit));
                Assert.Equal(0, inspectExit);
                Assert.Equal("ttc", inspected.GetProperty("container").GetString());
                foreach (var table in inspected.GetProperty("tables").EnumerateArray())
                    Assert.True(table.GetProperty("checksumMatches").GetBoolean(),
                        $"face {face} table {table.GetProperty("tag").GetString()}");
            }
        }
        finally { Cleanup(zero, one, output); }
    }

    [Fact]
    public void SingleFaceMergeProducesALegalOneFaceCollection()
    {
        (string zero, string _, string output) = TempPaths();
        try
        {
            File.WriteAllBytes(zero, Standalone("Lone Face"));
            var receipt = Json(FontWeave.Cli.Program.Execute(
                ["ttc-merge", zero, "--output", output], out int exitCode));
            Assert.Equal(0, exitCode);
            Assert.Equal(1, receipt.GetProperty("faceCount").GetInt32());
            FontFile merged = FontFile.Parse(File.ReadAllBytes(output));
            Assert.True(merged.IsCollection);
            Assert.Equal(1, merged.FaceCount);
            Assert.Equal("Lone Face", NameRecordZero(merged.GetFace(0)));
            var validated = Json(FontWeave.Cli.Program.Execute(["validate", output], out int validateExit));
            Assert.Equal(0, validateExit);
            Assert.Equal("Done", validated.GetProperty("statusName").GetString());
        }
        finally { Cleanup(zero, null, output); }
    }

    [Fact]
    public void MergeFlattensTtcInputsAlongsideSfntInputs()
    {
        (string collection, string standalone, string output) = TempPaths();
        try
        {
            File.WriteAllBytes(collection, CliTtcFixture.ValidatableTwoFaceTtc());
            File.WriteAllBytes(standalone, Standalone("Merge Two"));
            var receipt = Json(FontWeave.Cli.Program.Execute(
                ["ttc-merge", collection, standalone, "--output", output], out int exitCode));
            Assert.Equal(0, exitCode);
            Assert.Equal(3, receipt.GetProperty("faceCount").GetInt32());
            FontFile merged = FontFile.Parse(File.ReadAllBytes(output));
            Assert.Equal(3, merged.FaceCount);
            // Every collection face's table set survives the flattening, and the VORG of
            // face 1 does not leak into the neighbors.
            Assert.Equal(new[] { "head", "maxp", "name" }, merged.GetFace(0).Tables
                .Select(table => table.Tag).ToArray());
            Assert.Equal(new[] { "VORG", "head", "maxp", "name" }, merged.GetFace(1).Tables
                .Select(table => table.Tag).ToArray());
            Assert.Equal(new[] { "head", "maxp", "name" }, merged.GetFace(2).Tables
                .Select(table => table.Tag).ToArray());
            var validated = Json(FontWeave.Cli.Program.Execute(["validate", output], out int validateExit));
            Assert.Equal(0, validateExit);
            Assert.Equal("Done", validated.GetProperty("statusName").GetString());
        }
        finally { Cleanup(collection, standalone, output); }
    }

    // A version mismatch refuses before any file write; a broken face directory refuses
    // at parse. Both keep the output path absent.
    [Fact]
    public void MergeRefusesBadInputsAndWritesNothing()
    {
        (string bad, string _, string output) = TempPaths();
        try
        {
            File.WriteAllBytes(bad, [0x7A, 0x7A, 0x7A, 0x7A, 0, 0, 0, 0, 0, 0, 0, 0]);
            Assert.Throws<FontDataException>(() => FontWeave.Cli.Program.Execute(
                ["ttc-merge", bad, "--output", output], out _));
            Assert.False(File.Exists(output));

            byte[] broken = CliTtcFixture.ValidatableTwoFaceTtc();
            int faceOne = (int)System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(broken.AsSpan(16));
            // Truncate inside face 1's directory: the container header still promises it.
            File.WriteAllBytes(bad, broken.AsSpan(0, faceOne + 12 + 8).ToArray());
            Assert.Throws<FontDataException>(() => FontWeave.Cli.Program.Execute(
                ["ttc-merge", bad, "--output", output], out _));
            Assert.False(File.Exists(output));
        }
        finally { Cleanup(bad, null, output); }
    }

    [Fact]
    public void MergeKeepsTheOutputPathNewAndDisjointFromInputs()
    {
        (string zero, string _, string output) = TempPaths();
        try
        {
            File.WriteAllBytes(zero, Standalone("Merge Zero"));
            File.WriteAllBytes(output, [1]);
            Assert.Throws<FontWeave.Cli.Program.CliException>(() => FontWeave.Cli.Program.Execute(
                ["ttc-merge", zero, "--output", output], out _));
            Assert.Throws<FontWeave.Cli.Program.CliException>(() => FontWeave.Cli.Program.Execute(
                ["ttc-merge", zero, "--output", zero], out _));
        }
        finally { Cleanup(zero, null, output); }
    }

    private static byte[] Standalone(string name, bool extraVorg = false)
    {
        var tables = new List<(uint Tag, byte[] Bytes)>
        {
            (HeadTag, CliTtcFixture.ValidHead()),
            (MaxpTag, CliTtcFixture.Maxp()),
            (NameTag, CliTtcFixture.Name((3, 1, 0x409, 1, name)))
        };
        if (extraVorg) tables.Add((VorgTag, CliTtcFixture.ValidVorg()));
        return SubsetFontFixture.Sfnt(false, tables.ToArray());
    }

    private static string NameRecordZero(FontFace face) =>
        NameTable.Parse(face.GetTable("name").Data).Records[0].Text!;

    private static JsonElement Json(Dictionary<string, object?> receipt) =>
        JsonSerializer.SerializeToElement(receipt);

    private static (string Zero, string One, string Output) TempPaths()
    {
        string stem = $"fontweave-merge-{Guid.NewGuid():N}";
        return (Path.Combine(Path.GetTempPath(), $"{stem}-in0.ttf"),
            Path.Combine(Path.GetTempPath(), $"{stem}-in1.ttf"),
            Path.Combine(Path.GetTempPath(), $"{stem}-out.ttc"));
    }

    private static void Cleanup(string zero, string? one, string output)
    {
        File.Delete(zero);
        if (one is not null) File.Delete(one);
        if (File.Exists(output)) File.Delete(output);
    }
}
