// @argfile input channel tests for the subset roots (--gids/--codepoints/--text @path).
//
// The @file form only swaps a root value's source from the argv text to a UTF-8 file;
// the token semantics stay frozen (given order, no ranges, no dedup), so these tests
// pin the expansion, the widened separator set (comma or whitespace for list files),
// and the usage-error surface. List parsing runs through the internal Program helpers
// the subset root construction uses; the subset-path wiring is pinned end-to-end
// through Program.Execute with a missing list file.
using System.Text;
using FontWeave.Cli;
using Xunit;

namespace FontWeave.Tests;

public sealed class CliArgfileTests
{
    private const int DefaultBudget = 64 * 1024 * 1024;

    // Inline semantics are frozen: comma-only separators, given order, duplicates kept.
    [Fact]
    public void InlineGidsKeepFrozenCommaSemantics()
    {
        Assert.Equal(new ushort[] { 12, 16, 3 }, FontWeave.Cli.Program.ParseGids("12,0x10,3", DefaultBudget));
        Assert.Equal(new ushort[] { 7, 7 }, FontWeave.Cli.Program.ParseGids("7,7", DefaultBudget));
    }

    [Fact]
    public void InlineGidsStillRefuseRangesAndEmptyTokens()
    {
        FontWeave.Cli.Program.CliException range = Assert.Throws<FontWeave.Cli.Program.CliException>(
            () => FontWeave.Cli.Program.ParseGids("0-3", DefaultBudget));
        Assert.Equal("Usage", range.Code);
        Assert.Contains("ranges are not accepted", range.Message);
        Assert.Throws<FontWeave.Cli.Program.CliException>(() => FontWeave.Cli.Program.ParseGids("", DefaultBudget));
    }

    [Fact]
    public void FileGidsAcceptCommaAndWhitespaceSeparators()
    {
        string path = TempTextFile("10 0x1f,\n32\t,7\n");
        try
        {
            Assert.Equal(new ushort[] { 10, 31, 32, 7 },
                FontWeave.Cli.Program.ParseGids("@" + path, DefaultBudget));
        }
        finally { File.Delete(path); }
    }

    // The list is the caller's given sequence: no reordering, no deduplication.
    [Fact]
    public void FileGidsPreserveGivenOrderAndDuplicates()
    {
        string path = TempTextFile("3\n1, 1\n");
        try
        {
            Assert.Equal(new ushort[] { 3, 1, 1 },
                FontWeave.Cli.Program.ParseGids("@" + path, DefaultBudget));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void InlineCodepointsKeepFrozenSemanticsAndFilesWidenSeparators()
    {
        Assert.Equal(new uint[] { 0x41 }, FontWeave.Cli.Program.ParseCodepoints("U+41", DefaultBudget));
        string path = TempTextFile("U+41 U+1F600\nu+10FFFF\n");
        try
        {
            Assert.Equal(new uint[] { 0x41, 0x1F600, 0x10FFFF },
                FontWeave.Cli.Program.ParseCodepoints("@" + path, DefaultBudget));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void FileTokenStillRefusesRangeAndBareHexForms()
    {
        string gids = TempTextFile("0-3\n");
        try
        {
            FontWeave.Cli.Program.CliException range = Assert.Throws<FontWeave.Cli.Program.CliException>(
                () => FontWeave.Cli.Program.ParseGids("@" + gids, DefaultBudget));
            Assert.Equal("Usage", range.Code);
            Assert.Contains("ranges are not accepted", range.Message);
        }
        finally { File.Delete(gids); }
        string codepoints = TempTextFile("41 U+41\n");
        try
        {
            Assert.Throws<FontWeave.Cli.Program.CliException>(
                () => FontWeave.Cli.Program.ParseCodepoints("@" + codepoints, DefaultBudget));
        }
        finally { File.Delete(codepoints); }
    }

    [Fact]
    public void MissingFileIsAUsageError()
    {
        string missing = Path.Combine(Path.GetTempPath(), $"fontweave-missing-{Guid.NewGuid():N}.tmp");
        FontWeave.Cli.Program.CliException error = Assert.Throws<FontWeave.Cli.Program.CliException>(
            () => FontWeave.Cli.Program.ParseGids("@" + missing, DefaultBudget));
        Assert.Equal("Usage", error.Code);
        Assert.Contains(missing, error.Message);
    }

    [Fact]
    public void OversizedListFileIsAUsageError()
    {
        string path = TempTextFile(new string('1', 24) + "\n");
        try
        {
            FontWeave.Cli.Program.CliException error = Assert.Throws<FontWeave.Cli.Program.CliException>(
                () => FontWeave.Cli.Program.ParseGids("@" + path, 10));
            Assert.Equal("Usage", error.Code);
        }
        finally { File.Delete(path); }
    }

    // Strict UTF-8: a malformed byte is a usage error instead of silently carrying a
    // replacement character into a text root.
    [Fact]
    public void MalformedUtf8FileIsAUsageError()
    {
        string path = Path.Combine(Path.GetTempPath(), $"fontweave-argfile-{Guid.NewGuid():N}.tmp");
        File.WriteAllBytes(path, [0x80]);
        try
        {
            FontWeave.Cli.Program.CliException error = Assert.Throws<FontWeave.Cli.Program.CliException>(
                () => FontWeave.Cli.Program.ParseGids("@" + path, DefaultBudget));
            Assert.Equal("Usage", error.Code);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Utf8ByteOrderMarkIsStrippedNotTokenized()
    {
        string path = Path.Combine(Path.GetTempPath(), $"fontweave-argfile-{Guid.NewGuid():N}.tmp");
        File.WriteAllText(path, "5", new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        try
        {
            Assert.Equal(new ushort[] { 5 },
                FontWeave.Cli.Program.ParseGids("@" + path, DefaultBudget));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void TextRootReadsFileContentLiterally()
    {
        var inline = FontWeave.Cli.Program.ExpandListFile("R.T. regular", "text", DefaultBudget);
        Assert.False(inline.FromFile);
        Assert.Equal("R.T. regular", inline.Text);
        string path = TempTextFile("R.T.\nLig\n");
        try
        {
            var fromFile = FontWeave.Cli.Program.ExpandListFile("@" + path, "text", DefaultBudget);
            Assert.True(fromFile.FromFile);
            Assert.Equal("R.T.\nLig\n", fromFile.Text);
        }
        finally { File.Delete(path); }
    }

    // Wiring pin: the subset root construction resolves @path, so a missing list file
    // surfaces as Usage even before the (here degenerate) font is examined further.
    [Fact]
    public void SubsetWiringResolvesArgfilesAndRefusesMissingFilesAsUsage()
    {
        (string input, string output) = TempFontPair();
        try
        {
            string missing = Path.Combine(Path.GetTempPath(), $"fontweave-missing-{Guid.NewGuid():N}.tmp");
            FontWeave.Cli.Program.CliException missingFile = Assert.Throws<FontWeave.Cli.Program.CliException>(() => FontWeave.Cli.Program.Execute(
                ["subset", input, "--mode", "retain", "--gids", "@" + missing, "--output", output],
                out _));
            Assert.Equal("Usage", missingFile.Code);
            Assert.Contains(missing, missingFile.Message);

            // The inline channel keeps the frozen refusal on the same wiring.
            FontWeave.Cli.Program.CliException inlineRange = Assert.Throws<FontWeave.Cli.Program.CliException>(() => FontWeave.Cli.Program.Execute(
                ["subset", input, "--mode", "retain", "--gids", "3-5", "--output", output],
                out _));
            Assert.Equal("Usage", inlineRange.Code);
            Assert.Contains("ranges are not accepted", inlineRange.Message);
        }
        finally { Cleanup(input, output); }
    }

    private static string TempTextFile(string content)
    {
        string path = Path.Combine(Path.GetTempPath(), $"fontweave-argfile-{Guid.NewGuid():N}.tmp");
        File.WriteAllText(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return path;
    }

    private static (string Input, string Output) TempFontPair()
    {
        string input = Path.Combine(Path.GetTempPath(), $"fontweave-argfont-{Guid.NewGuid():N}.bin");
        File.WriteAllBytes(input, new byte[16]);
        string output = Path.Combine(Path.GetTempPath(), $"fontweave-argout-{Guid.NewGuid():N}.bin");
        return (input, output);
    }

    private static void Cleanup(string input, string output)
    {
        File.Delete(input);
        if (File.Exists(output)) File.Delete(output);
    }
}
