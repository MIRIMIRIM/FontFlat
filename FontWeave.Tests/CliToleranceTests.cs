// P1-4 CLI surface: validate --tolerate-legacy reports the admitted OS/2 dialect
// deviations in the receipt, while the default strict run still refuses them.
using System.Text.Json;
using FontWeave.Core;
using Xunit;

namespace FontWeave.Tests;

public sealed class CliToleranceTests
{
    private static (string Input, string Output) TempOs2Font()
    {
        string input = Path.Combine(Path.GetTempPath(),
            $"fontweave-tolerance-{Guid.NewGuid():N}.ttf");
        File.WriteAllBytes(input, SubsetFontFixture.Sfnt(false,
            (SubsetFontFixture.HeadTag, SubsetFontFixture.Head()),
            (SubsetFontFixture.MaxpTag, SubsetFontFixture.Maxp(4)),
            (SubsetFontFixture.Os2Tag, SubsetFontFixture.Os2Legacy())));
        string output = Path.Combine(Path.GetTempPath(),
            $"fontweave-tolerance-{Guid.NewGuid():N}.out");
        return (input, output);
    }

    private static JsonElement Json(Dictionary<string, object?> receipt) =>
        JsonSerializer.SerializeToElement(receipt);

    [Fact]
    public void StrictValidateRefusesTheLegacyOs2Dialect()
    {
        (string input, string output) = TempOs2Font();
        try
        {
            var receipt = Json(FontWeave.Cli.Program.Execute(
                ["validate", input], out int exitCode));
            Assert.Equal(1, exitCode);
            JsonElement findings = receipt.GetProperty("report")
                .GetProperty("toleratedFindings");
            Assert.Equal(0, findings.GetArrayLength());
            Assert.Equal((int)DiagnosticCode.InvalidValue,
                receipt.GetProperty("diagnostic").GetProperty("code").GetInt32());
        }
        finally { File.Delete(input); File.Delete(output); }
    }

    [Fact]
    public void TolerantValidateReportsTheAdmittedDeviations()
    {
        (string input, string output) = TempOs2Font();
        try
        {
            var receipt = Json(FontWeave.Cli.Program.Execute(
                ["validate", input, "--tolerate-legacy"], out int exitCode));
            Assert.Equal(0, exitCode);
            JsonElement findings = receipt.GetProperty("report")
                .GetProperty("toleratedFindings");
            Assert.Equal(3, findings.GetArrayLength());
            Assert.Contains(findings.EnumerateArray(), finding =>
                finding.GetProperty("rule").GetString() == "fsSelection.version-coupled-bits" &&
                finding.GetProperty("offset").GetUInt64() == 62);
            Assert.Contains(findings.EnumerateArray(), finding =>
                finding.GetProperty("rule").GetString() == "ulCodePageRange1.reserved-bits" &&
                finding.GetProperty("offset").GetUInt64() == 78);
            Assert.Contains(findings.EnumerateArray(), finding =>
                finding.GetProperty("rule").GetString() == "ulCodePageRange2.reserved-bits" &&
                finding.GetProperty("offset").GetUInt64() == 82);
        }
        finally { File.Delete(input); File.Delete(output); }
    }
}
