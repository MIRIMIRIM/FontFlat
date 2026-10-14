// Compact map freeze, artifact serialization and BF-002 budget hook tests (SC card
// §4 commit ② acceptance).
//
// The map artifact's deterministic serialization, its sha256 pin and the no-downgrade
// budget failure are pinned here at unit level; the independent JSON read-back runs in
// the validation/FontWeave.SC gate with its own parser.
using System.Text;
using FontWeave.Core;
using FontWeave.Operations.Subset;
using Xunit;

namespace FontWeave.Tests;

public sealed class SubsetCompactMapTests
{
    private static SubsetRequest Request(FontFaceRef face, IReadOnlyList<ushort> gids,
        SubsetMode mode) => new()
    {
        Face = face,
        Root = new SubsetGlyphRoot { Kind = SubsetRootKind.ExplicitGlyphSet, Gids = gids },
        Mode = mode,
        Options = new SubsetOptions { NoLayout = true },
    };

    // A compact prepare pins the artifact sha into its report at the moment of
    // serialization - even when a later stage refuses - while retain stays null.
    [Fact]
    public void CompactReportPinsArtifactShaRetainKeepsNull()
    {
        var face = SubsetCompactPolicyTests.MatrixFace();
        var compact = SubsetOperation.TryPrepare(
            Request(face, [0, 1, 3], SubsetMode.CompactRemap), out _, out var compactReport,
            out _);
        Assert.NotNull(compactReport);
        // The staged pipeline still refuses at the first rebuild row without a producer,
        // but the map artifact already serialized (decide complete, no payload copied).
        Assert.Matches("^[0-9a-f]{64}$", compactReport!.MapArtifactSha256);
        var retain = SubsetOperation.TryPrepare(
            Request(face, [0, 1, 3], SubsetMode.RetainGids), out var retainResult,
            out var retainReport, out _);
        Assert.Equal(ReadStatus.Done, retain);
        Assert.NotNull(retainResult);
        Assert.Null(retainReport!.MapArtifactSha256);
    }

    // Same source face, same request: byte-identical serialization, equal sha.
    [Fact]
    public void SameFaceAndRequestSerializeIdentically()
    {
        var face = SubsetCompactPolicyTests.MatrixFace();
        _ = SubsetOperation.TryPrepare(Request(face, [0, 1, 3], SubsetMode.CompactRemap),
            out _, out var first, out _);
        _ = SubsetOperation.TryPrepare(Request(face, [0, 1, 3], SubsetMode.CompactRemap),
            out _, out var second, out _);
        Assert.Equal(first!.MapArtifactSha256, second!.MapArtifactSha256);
    }

    // The codec round-trip is byte-exact and the canonical bytes carry the frozen
    // grammar's shape: two-space indent, LF endings, entries ascending.
    [Fact]
    public void CodecRoundTripIsByteExact()
    {
        var artifact = new FrozenMapArtifact(3, 0, 139985, 3387, 4,
        [
            new FrozenMapEntry(0, 0, "glyf:0"),
            new FrozenMapEntry(1, 1, "glyf:1"),
            new FrozenMapEntry(2, 19, "glyf:19"),
            new FrozenMapEntry(3, 31, "glyf:31"),
        ])
        {
            InputFaceSha256 = new string('a', 64),
        };
        var status = FrozenMapArtifactCodec.TrySerialize(artifact, out var utf8, out _);
        Assert.Equal(ReadStatus.Done, status);
        string text = Encoding.UTF8.GetString(utf8!);
        Assert.DoesNotContain("\r", text, StringComparison.Ordinal);
        Assert.StartsWith("{\n  \"schema\": \"fontweave.frozen-map/1\",\n", text,
            StringComparison.Ordinal);
        Assert.EndsWith("  ]\n}\n", text, StringComparison.Ordinal);
        Assert.Contains("  \"fontRevisionRaw\": 139985,\n", text, StringComparison.Ordinal);
        Assert.Contains(
            "    { \"outputGid\": 2, \"inputGid\": 19, \"stableIdentity\": \"glyf:19\" },\n",
            text, StringComparison.Ordinal);
        var readBack = FrozenMapArtifactCodec.TryDeserialize(utf8, out var parsed, out _);
        Assert.Equal(ReadStatus.Done, readBack);
        Assert.NotNull(parsed);
        Assert.Equal(artifact.SourceId, parsed!.SourceId);
        Assert.Equal(artifact.FaceIndex, parsed.FaceIndex);
        Assert.Equal(artifact.FontRevisionRaw, parsed.FontRevisionRaw);
        Assert.Equal(artifact.InputGlyphCount, parsed.InputGlyphCount);
        Assert.Equal(artifact.OutputGlyphCount, parsed.OutputGlyphCount);
        Assert.Equal(artifact.Entries, parsed.Entries);
        Assert.Equal(artifact.InputFaceSha256, parsed.InputFaceSha256);
        var status2 = FrozenMapArtifactCodec.TrySerialize(parsed!, out var again, out _);
        Assert.Equal(ReadStatus.Done, status2);
        Assert.True(utf8!.AsSpan().SequenceEqual(again!),
            "re-serialization of the parsed artifact drifted");
        Assert.Equal(FrozenMapArtifactCodec.Sha256Hex(artifact),
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(utf8!))
                .ToLowerInvariant());
    }

    // A deserializer fed mutated bytes must refuse rather than accept loosely.
    [Theory]
    [InlineData("schema", "\"fontweave.frozen-map/2\"")]
    [InlineData("fontRevisionRaw", "-1")]
    [InlineData("outputGlyphCount", "5")]
    [InlineData("entries", "[]")]
    public void DeserializeRejectsDriftedPayload(string field, string replacement)
    {
        var artifact = new FrozenMapArtifact(1, 0, 139985, 4, 2,
        [
            new FrozenMapEntry(0, 0, "glyf:0"),
            new FrozenMapEntry(1, 3, "glyf:3"),
        ])
        {
            InputFaceSha256 = new string('a', 64),
        };
        _ = FrozenMapArtifactCodec.TrySerialize(artifact, out var utf8, out _);
        string text = Encoding.UTF8.GetString(utf8!);
        string drifted = field switch
        {
            "schema" => text.Replace("\"fontweave.frozen-map/1\"", replacement,
                StringComparison.Ordinal),
            "fontRevisionRaw" => text.Replace("  \"fontRevisionRaw\": 139985,",
                $"  \"fontRevisionRaw\": {replacement},", StringComparison.Ordinal),
            "outputGlyphCount" => text.Replace("  \"outputGlyphCount\": 2,",
                $"  \"outputGlyphCount\": {replacement},", StringComparison.Ordinal),
            _ => text.Replace(
                "    { \"outputGid\": 0, \"inputGid\": 0, \"stableIdentity\": \"glyf:0\" },\n",
                "", StringComparison.Ordinal),
        };
        Assert.NotEqual(text, drifted);
        var status = FrozenMapArtifactCodec.TryDeserialize(Encoding.UTF8.GetBytes(drifted),
            out var parsed, out var diagnostic);
        Assert.Equal(ReadStatus.InvalidFormat, status);
        Assert.Equal(DiagnosticCode.InvalidValue, diagnostic.Code);
        Assert.Null(parsed);
    }

    // BF-002: exhausting MaxTemporaryBytes at the artifact serialization refuses with
    // the contracted shape and never downgrades into a map-less product. The fill
    // limit follows the frozen limitRecipe — the total Buffer charged before the map
    // artifact serialization plus one — observed by walking the real accounting's
    // boundaries (the S0-BF-001 calibration precedent). The serialization charge is the
    // face-level boundary (TableTag 0) past the earlier charges; its would-be cumulative
    // is the refusal's detail.
    [Fact]
    public void TemporaryBudgetFillRefusesAtSerialization()
    {
        var face = SubsetCompactPolicyTests.MatrixFace();
        (long before, long boundary) = WalkToSerializationBoundary(face);
        var status = SubsetOperation.TryPrepare(
            Request(face, [0, 1, 3], SubsetMode.CompactRemap),
            out var result, out var report, out var diagnostic,
            new BuildLimits { MaxTemporaryBytes = before + 1 });
        Assert.Equal(ReadStatus.ResourceLimit, status);
        Assert.Equal(DiagnosticCode.BudgetExceeded, diagnostic.Code);
        Assert.Equal(DataField.TemporaryBytes, diagnostic.Location.Field);
        Assert.Equal(boundary, (long)diagnostic.Detail);
        Assert.Equal(before + 1, (long)diagnostic.Expected);
        Assert.Null(result);
        Assert.NotNull(report);
        // No artifact, no sha: the serialization never completed, and nothing was written.
        Assert.Null(report!.MapArtifactSha256);
        Assert.Equal(0u, diagnostic.Location.TableTag);
    }

    // At the recipe's floor the same charge still fails, with the limit itself as the
    // expected value: the serialization can never fit at its own pre-charge total.
    [Fact]
    public void FloorRefusesInsideSerializationCharge()
    {
        var face = SubsetCompactPolicyTests.MatrixFace();
        (long before, long boundary) = WalkToSerializationBoundary(face);
        var status = SubsetOperation.TryPrepare(
            Request(face, [0, 1, 3], SubsetMode.CompactRemap),
            out var result, out _, out var diagnostic,
            new BuildLimits { MaxTemporaryBytes = before });
        Assert.Equal(ReadStatus.ResourceLimit, status);
        Assert.Equal(DiagnosticCode.BudgetExceeded, diagnostic.Code);
        Assert.Equal(DataField.TemporaryBytes, diagnostic.Location.Field);
        Assert.Equal(boundary, (long)diagnostic.Detail);
        Assert.Equal(before, (long)diagnostic.Expected);
        Assert.Null(result);
    }

    /// <summary>Walks the Buffer accounting the way the S1a consumer calibrates BF-001:
    /// each probe at limit L refuses at the first charge whose would-be cumulative
    /// exceeds L, and the diagnostic's detail is that cumulative. The map artifact's
    /// serialization charge is the face-level boundary (TableTag 0) beyond the
    /// decide-phase reserves, which are the only earlier face-level Buffer charges.
    /// Returns the pre-serialization total (the limitRecipe's operand) and the
    /// serialization charge's would-be cumulative.</summary>
    private static (long Before, long Boundary) WalkToSerializationBoundary(FontFaceRef face)
    {
        long limit = 8;
        ulong previous = 0, before = 0, boundary = 0;
        for (int probes = 0; probes < 64; probes++)
        {
            var status = SubsetOperation.TryPrepare(
                Request(face, [0, 1, 3], SubsetMode.CompactRemap),
                out _, out _, out var diagnostic,
                new BuildLimits { MaxTemporaryBytes = limit });
            if (status == ReadStatus.Done) break;
            if (diagnostic.Code != DiagnosticCode.BudgetExceeded ||
                diagnostic.Location.Field != DataField.TemporaryBytes) break;
            ulong cumulative = diagnostic.Detail;
            if (diagnostic.Location.TableTag == 0)
            {
                // The decide-phase reserves are the first face-level charges and the
                // artifact boundary is the last one; its pre-charge total is whatever
                // boundary preceded it in the monotone walk.
                before = previous;
                boundary = cumulative;
            }
            previous = cumulative;
            if (cumulative <= (ulong)limit) break;
            limit = (long)cumulative;
        }
        Assert.True(boundary > before, "no face-level serialization boundary found");
        return ((long)before, (long)boundary);
    }

    // The map freezes over the closure-extended set with the ascending-input output
    // order (OQ-SC-6): the report's closure additions and a distinct sha prove the map
    // saw the extended domain.
    [Fact]
    public void ClosureExtensionReachesTheMap()
    {
        var (loca, glyf, _) = GlyfImages.Assemble(
        [
            GlyfImages.NoContourGlyphRecord(),
            GlyfImages.NoContourGlyphRecord(),
            GlyfImages.CompositeRecord(1),
        ]);
        var face = SubsetFontFixture.Face(
            (SubsetFontFixture.HeadTag, SubsetFontFixture.Head()),
            (SubsetFontFixture.MaxpTag, SubsetFontFixture.Maxp(3)),
            (SubsetFontFixture.GlyfTag, glyf),
            (SubsetFontFixture.LocaTag, loca),
            (SubsetFontFixture.CmapTag, SubsetFontFixture.CmapCanonical((0x41u, 0))));
        var status = SubsetOperation.TryPrepare(
            Request(face, [2], SubsetMode.CompactRemap), out _, out var report, out _);
        Assert.NotNull(report);
        Assert.Equal([1], report!.ClosureAddedGids);
        Assert.Matches("^[0-9a-f]{64}$", report.MapArtifactSha256);
    }

    // A witness artifact without the input face sha refuses to serialize: the
    // cross-process traceability column is mandatory (W1 §7 finding 4).
    [Fact]
    public void SerializeRequiresInputFaceSha()
    {
        var artifact = new FrozenMapArtifact(1, 0, 1, 4, 1,
            [new FrozenMapEntry(0, 0, "glyf:0")]);
        var status = FrozenMapArtifactCodec.TrySerialize(artifact, out var utf8, out _);
        Assert.Equal(ReadStatus.InvalidFormat, status);
        Assert.Null(utf8);
    }
}
