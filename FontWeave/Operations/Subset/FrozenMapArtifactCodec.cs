using System.Security.Cryptography;
using System.Text.Json;
using FontWeave.Core;

namespace FontWeave.Operations.Subset;

/// <summary>The deterministic serialization of a frozen map artifact (W1 §4.3, SC card
/// §1.3). The canonical form is UTF-8 without BOM, LF line endings, two-space indent,
/// decimal integers, fixed field order and a trailing newline; entries ascend by
/// outputGid. The same artifact always serializes to the same bytes, so the sha256 of
/// one serialization pins the artifact in SubsetReport.MapArtifactSha256. TryDeserialize
/// is the strict read-back face: a payload that is not exactly this grammar — including
/// an entry set that is not the injective [0, outputGlyphCount) order with 0 → 0 — is
/// rejected rather than accepted loosely.</summary>
public static class FrozenMapArtifactCodec
{
    private const string SchemaLiteral = "fontweave.frozen-map/1";

    /// <summary>The stable identity column's glyf-kind serialization (W1 §3.4): the
    /// input GID itself, decimal.</summary>
    public static string GlyfStableIdentity(ushort inputGid) => $"glyf:{inputGid}";

    /// <summary>The CFF1 name-keyed kind (S1c card §4): the input charset SID, decimal.
    /// The SID is the glyph's name identity; the charstring INDEX dimension rides the
    /// row's inputGid anchor, so no separate token exists.</summary>
    public static string Cff1SidStableIdentity(int inputSid) => $"cff1-sid:{inputSid}";

    /// <summary>The CFF1 CID-keyed kind (S1c card §4): the input charset CID, decimal.</summary>
    public static string Cff1CidStableIdentity(int inputCid) => $"cff1-cid:{inputCid}";

    /// <summary>The CFF2 kind (SV card §3, OQ-SV-4 lean): the input GID itself,
    /// decimal. CFF2's TopDICT carries no charset/encoding/String INDEX (the
    /// five-operator whitelist), so the CharStrings INDEX order — the input GID —
    /// is the only glyph identity source; FDSelect's FD numbers are explicit wire
    /// attribute fields and stay out of the identity (the W1 §3.4 "+FDSelect"
    /// reading corrected to an attribute domain carried by the rebuild).</summary>
    public static string Cff2IdxStableIdentity(ushort inputGid) => $"cff2-idx:{inputGid}";

    /// <summary>The kind prefixes of the additive stable-identity grammar (S1c card
    /// §1.3; the CFF2 kind appended by the SV slice): one glyph row carries exactly
    /// one, and one artifact carries exactly one kind across all rows. The schema
    /// literal stays fontweave.frozen-map/1 — W1 §3.4 never closed the Kind set,
    /// and the extension leaves every glyf/cff1 row's serialized bytes
    /// bit-identical (OQ-S1c-5 lean carried forward).</summary>
    private static readonly string[] IdentityKinds =
        ["glyf:", "cff1-sid:", "cff1-cid:", "cff2-idx:"];

    private static string? KindOf(string identity)
    {
        foreach (string kind in IdentityKinds)
            if (identity.StartsWith(kind, StringComparison.Ordinal)) return kind;
        return null;
    }

    public static ReadStatus TrySerialize(FrozenMapArtifact artifact, out byte[]? utf8,
        out FontDiagnostic diagnostic)
    {
        utf8 = null;
        diagnostic = default;
        DataLocation at = DataLocation.Anonymous(0x6D617070); // 'mapp': the map artifact
        ReadStatus status = ValidateShape(artifact, out diagnostic);
        if (status != ReadStatus.Done) return status;

        utf8 = new byte[SerializedLength(artifact)];
        Write(artifact, utf8);
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>The artifact contract both directions share: counts inside the maxp
    /// domain, entries exactly the injective ascending [0, outputGlyphCount) with
    /// 0 → 0, one stable-identity kind per artifact with per-row grammar (glyf rows
    /// pin the identity to the input GID; CFF1 rows pin it to a well-formed decimal
    /// SID/CID whose face semantics the freeze path constructed and the gates verify
    /// independently), a non-negative revision raw and the 64-hex input face sha
    /// (W1 §3.3/§3.4, §7 finding 4; S1c card §1.3).</summary>
    private static ReadStatus ValidateShape(FrozenMapArtifact artifact,
        out FontDiagnostic diagnostic)
    {
        DataLocation at = DataLocation.Anonymous(0x6D617070);
        if (artifact.InputGlyphCount is < 1 or > 65536 ||
            artifact.OutputGlyphCount < 1 || artifact.OutputGlyphCount > artifact.InputGlyphCount ||
            artifact.Entries.Count != artifact.OutputGlyphCount)
            return Fail(at, (ulong)artifact.OutputGlyphCount, (ulong)artifact.InputGlyphCount,
                out diagnostic);
        if (artifact.Entries[0] is not { OutputGid: 0, InputGid: 0 })
            return Fail(at,
                artifact.Entries.Count == 0 ? 0UL : artifact.Entries[0].OutputGid,
                0UL, out diagnostic);
        string? kind = KindOf(artifact.Entries[0].StableIdentity);
        if (kind is null)
            return Fail(at, 0, artifact.Entries[0].InputGid, out diagnostic);
        for (int i = 0; i < artifact.Entries.Count; i++)
        {
            FrozenMapEntry entry = artifact.Entries[i];
            if (entry.OutputGid != (ushort)i || entry.InputGid >= artifact.InputGlyphCount ||
                KindOf(entry.StableIdentity) != kind)
                return Fail(at, entry.OutputGid, (ulong)i, out diagnostic);
            if (kind == IdentityKinds[0])
            {
                if (entry.StableIdentity != GlyfStableIdentity(entry.InputGid))
                    return Fail(at, entry.OutputGid, (ulong)i, out diagnostic);
            }
            else if (!WellFormedIdentityValue(entry.StableIdentity.AsSpan(kind.Length)))
                return Fail(at, entry.OutputGid, (ulong)i, out diagnostic);
        }
        if (artifact.FontRevisionRaw < 0)
            return Fail(at, unchecked((ulong)artifact.FontRevisionRaw), 0UL, out diagnostic);
        if (artifact.InputFaceSha256 is not { } hex || hex.Length != 64 || !IsHex(hex))
            return Fail(at, 0, 0, out diagnostic);
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>A CFF1 identity value: decimal digits, no leading zeros, inside the
    /// 16-bit SID/CID wire domain. The wire charset carries SID/CID as u16, so a value
    /// outside [0, 65535] cannot name an input glyph.</summary>
    private static bool WellFormedIdentityValue(ReadOnlySpan<char> digits)
    {
        if (digits.IsEmpty || digits.Length > 5) return false;
        if (digits.Length > 1 && digits[0] == '0') return false;
        int value = 0;
        foreach (char c in digits)
        {
            if (!char.IsAsciiDigit(c)) return false;
            value = value * 10 + (c - '0');
        }
        return value <= 0xFFFF;
    }

    /// <summary>sha256 over the canonical serialization; the exact value pinned into
    /// SubsetReport.MapArtifactSha256.</summary>
    public static string Sha256Hex(FrozenMapArtifact artifact) =>
        Convert.ToHexString(SHA256.HashData(SerializeOrThrow(artifact))).ToLowerInvariant();

    public static ReadStatus TryDeserialize(ReadOnlySpan<byte> utf8,
        out FrozenMapArtifact? artifact, out FontDiagnostic diagnostic)
    {
        artifact = null;
        diagnostic = default;
        DataLocation at = DataLocation.Anonymous(0x6D617070);
        FrozenMapArtifact? parsed;
        try
        {
            parsed = Parse(utf8);
        }
        // The parse boundary maps every malformed shape to the one diagnostic
        // (grammar, wrong-typed fields, missing properties) — the Try* contract
        // owns the failure, never an escaping exception.
        catch (Exception e) when (e is JsonException or InvalidOperationException
            or FormatException or KeyNotFoundException or OverflowException)
        {
            return Fail(at, 0, 0, out diagnostic);
        }
        if (parsed is null) return Fail(at, 0, 0, out diagnostic);
        ReadStatus shape = ValidateShape(parsed, out diagnostic);
        if (shape != ReadStatus.Done) return shape;
        // Round-trip equality: the re-serialization of a deserialized artifact must be
        // byte-identical to its input, which pins field order, formatting and grammar.
        byte[] reserialized = SerializeOrThrow(parsed);
        if (!reserialized.AsSpan().SequenceEqual(utf8))
            return Fail(at, 0, 0, out diagnostic);
        artifact = parsed;
        diagnostic = default;
        return ReadStatus.Done;
    }

    private static FrozenMapArtifact? Parse(ReadOnlySpan<byte> utf8)
    {
        using JsonDocument document = JsonDocument.Parse(utf8.ToArray(),
            new JsonDocumentOptions { MaxDepth = 8 });
        JsonElement root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object ||
            root.GetProperty("schema").GetString() != SchemaLiteral) return null;
        JsonElement sourceFace = root.GetProperty("sourceFace");
        long sourceId = sourceFace.GetProperty("sourceId").GetInt64();
        int faceIndex = sourceFace.GetProperty("faceIndex").GetInt32();
        string sha = sourceFace.GetProperty("inputFaceSha256").GetString()!;
        int fontRevisionRaw = root.GetProperty("fontRevisionRaw").GetInt32();
        int inputGlyphCount = root.GetProperty("inputGlyphCount").GetInt32();
        int outputGlyphCount = root.GetProperty("outputGlyphCount").GetInt32();
        JsonElement entriesElement = root.GetProperty("entries");
        var entries = new FrozenMapEntry[entriesElement.GetArrayLength()];
        int at = 0;
        foreach (JsonElement entry in entriesElement.EnumerateArray())
        {
            if (at >= entries.Length) return null;
            entries[at++] = new(
                checked((ushort)entry.GetProperty("outputGid").GetUInt32()),
                checked((ushort)entry.GetProperty("inputGid").GetUInt32()),
                entry.GetProperty("stableIdentity").GetString()!);
        }
        if (at != entries.Length) return null;
        return new(sourceId, faceIndex, fontRevisionRaw, inputGlyphCount, outputGlyphCount,
            entries) { InputFaceSha256 = sha };
    }

    private static byte[] SerializeOrThrow(FrozenMapArtifact artifact)
    {
        var bytes = new byte[SerializedLength(artifact)];
        Write(artifact, bytes);
        return bytes;
    }

    private static int SerializedLength(FrozenMapArtifact artifact)
    {
        // Header and footer are fixed; each entry row is the fixed frame plus the decimal
        // digits of the output GID once, the input GID once and the identity string as
        // validated (a glyf row's identity is "glyf:" plus the input GID's digits).
        // "{\n" opens the document; the footer is "  ]\n}\n".
        int length = 2
            + ("  \"schema\": \"" + SchemaLiteral + "\",").Length + 1
            + "  \"sourceFace\": {".Length + 1
            + ("    \"sourceId\": ").Length + DecimalLength(artifact.SourceId) + 1 + 1
            + ("    \"faceIndex\": ").Length + DecimalLength(artifact.FaceIndex) + 1 + 1
            + "    \"inputFaceSha256\": \"".Length + 64 + 1 + 1
            + "  },".Length + 1
            + "  \"fontRevisionRaw\": ".Length + DecimalLength(artifact.FontRevisionRaw) + 1 + 1
            + "  \"inputGlyphCount\": ".Length + DecimalLength(artifact.InputGlyphCount) + 1 + 1
            + "  \"outputGlyphCount\": ".Length + DecimalLength(artifact.OutputGlyphCount) + 1 + 1
            + "  \"entries\": [".Length + 1;
        foreach (FrozenMapEntry entry in artifact.Entries)
            length += 4 + EntryFrame + DecimalLength(entry.OutputGid)
                + DecimalLength(entry.InputGid) + entry.StableIdentity.Length + 1;
        length += artifact.Entries.Count - 1; // the separating commas between rows
        length += "  ]".Length + 1 + "}".Length + 1;
        return length;
    }

    private static readonly int EntryFrame =
        "{ \"outputGid\": , \"inputGid\": , \"stableIdentity\": \"\" }".Length;

    private static void Write(FrozenMapArtifact artifact, byte[] target)
    {
        int at = 0;
        void Line(string text)
        {
            foreach (char c in text) target[at++] = (byte)c;
            target[at++] = (byte)'\n';
        }
        void ValueLine(string prefix, long value, string suffix)
        {
            Line(prefix + DecimalString(value) + suffix);
        }
        Line("{");
        Line("  \"schema\": \"" + SchemaLiteral + "\",");
        Line("  \"sourceFace\": {");
        ValueLine("    \"sourceId\": ", artifact.SourceId, ",");
        ValueLine("    \"faceIndex\": ", artifact.FaceIndex, ",");
        Line("    \"inputFaceSha256\": \"" + artifact.InputFaceSha256 + "\"");
        Line("  },");
        ValueLine("  \"fontRevisionRaw\": ", artifact.FontRevisionRaw, ",");
        ValueLine("  \"inputGlyphCount\": ", artifact.InputGlyphCount, ",");
        ValueLine("  \"outputGlyphCount\": ", artifact.OutputGlyphCount, ",");
        Line("  \"entries\": [");
        for (int i = 0; i < artifact.Entries.Count; i++)
        {
            FrozenMapEntry entry = artifact.Entries[i];
            string row = "    { \"outputGid\": " + DecimalString(entry.OutputGid)
                + ", \"inputGid\": " + DecimalString(entry.InputGid)
                + ", \"stableIdentity\": \"" + entry.StableIdentity + "\" }";
            if (i + 1 < artifact.Entries.Count) Line(row + ",");
            else Line(row);
        }
        Line("  ]");
        Line("}");
        if (at != target.Length)
            throw new InvalidOperationException(
                $"The canonical map serialization drifted: wrote {at} of {target.Length} bytes.");
    }

    private static string DecimalString(long value) =>
        value >= 0 ? value.ToString() : "-" + unchecked((ulong)(-value)).ToString();

    private static int DecimalLength(long value)
    {
        ulong magnitude = value >= 0 ? (ulong)value : unchecked((ulong)(-value));
        int length = 1;
        while (magnitude >= 10) { magnitude /= 10; length++; }
        return length + (value < 0 ? 1 : 0);
    }

    private static bool IsHex(string text)
    {
        foreach (char c in text)
            if (!char.IsAsciiHexDigit(c)) return false;
        return true;
    }

    private static ReadStatus Fail(DataLocation at, ulong detail, ulong expected,
        out FontDiagnostic diagnostic)
    {
        diagnostic = new(DiagnosticCode.InvalidValue, at, detail, expected);
        return ReadStatus.InvalidFormat;
    }
}
