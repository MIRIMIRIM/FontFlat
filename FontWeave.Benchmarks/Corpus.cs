using System.Buffers.Binary;
using System.Text;
using FontWeave;
using FontWeave.Containers;
using FontWeave.Core;
using FontWeave.Inspection;
using FontWeave.Tables;

namespace FontWeave.Benchmarks;

/// <summary>Resolved corpus location plus the pinned default file names. The corpus
/// itself is external and untracked: files resolve against a "bench-inputs" directory
/// found by an upward walk from the executable directory (or an explicit --inputs path,
/// which is also published as FONTWEAVE_BENCH_INPUTS so BenchmarkDotNet's child
/// processes resolve the same directory).</summary>
public static class Corpus
{
    public const string InputsDirectoryName = "bench-inputs";
    public const string InputsEnvironmentVariable = "FONTWEAVE_BENCH_INPUTS";

    /// <summary>The pinned sfnt corpus, in benchmark display order.</summary>
    public static readonly string[] DefaultSfntFiles =
    [
        "overlay-roboto-foundation-layout-repaired.sfnt",
        "source-sans-tt-layout.sfnt",
        "opensans-variable-layout.sfnt",
        "source-serif-cff2-variable-layout.sfnt",
        "noto-colr-svg-layout.sfnt",
        "fontations-cbdt-supplement.sfnt",
    ];

    /// <summary>The pinned TTC corpus. TTC faces run only the three TTC scenarios.</summary>
    public static readonly string[] DefaultTtcFiles = ["cambria-math.ttc"];

    public static string ResolveInputsDir(string? explicitPath)
    {
        if (!string.IsNullOrWhiteSpace(explicitPath))
            return Path.GetFullPath(explicitPath);
        string? fromEnvironment = Environment.GetEnvironmentVariable(InputsEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(fromEnvironment) && Directory.Exists(fromEnvironment))
            return Path.GetFullPath(fromEnvironment);
        ReadOnlySpan<string> roots =
        [
            AppContext.BaseDirectory,
            Environment.CurrentDirectory,
        ];
        foreach (string root in roots)
        {
            DirectoryInfo? dir = new(root);
            for (int depth = 0; dir is not null && depth < 12; depth++, dir = dir.Parent)
            {
                string candidate = Path.Combine(dir.FullName, InputsDirectoryName);
                if (Directory.Exists(candidate)) return candidate;
            }
        }
        throw new DirectoryNotFoundException(
            $"No '{InputsDirectoryName}' directory found at or above the program directory. " +
            "Pass --inputs <path> to point at the corpus.");
    }
}

/// <summary>Everything one sfnt benchmark invocation needs, prepared once outside the
/// measured region: the raw bytes, the glyph-domain roots, the derived Unicode root and
/// the chosen name-edit ordinal. Preparation mirrors what a CLI caller fixes before
/// invoking a command (a --gids list, a --codepoints list, a --record ordinal).</summary>
public sealed class FontInput
{
    public required string Label { get; init; }
    public required string FilePath { get; init; }
    public required byte[] Bytes { get; init; }
    public required int NumGlyphs { get; init; }
    /// <summary>GIDs 0..min(512, numGlyphs)-1, strictly ascending (the explicit root's wire contract).</summary>
    public required ushort[] Gids512 { get; init; }
    /// <summary>GIDs 0..numGlyphs-1, strictly ascending (the retain-all / identity domain).</summary>
    public required ushort[] GidsAll { get; init; }
    /// <summary>Unicode root derived from the typed cmap read; ASCII printable first, then
    /// stride-selected additional mapped scalars up to 512 total. May be empty when the
    /// derivation is refused (the scenario then records the product's own refusal).</summary>
    public required uint[] Codepoints { get; init; }
    /// <summary>First record ordinal whose NameId is editable ({1,2,4,16,17}) on a UTF-16
    /// record; -1 when the face offers none (the edit scenario then records the refusal).</summary>
    public required int EditOrdinal { get; init; }

    public BuildLimits Limits { get; } = new();
}

/// <summary>TTC input: the collection bytes plus the per-face name-edit ordinals the
/// split scenario substitutes before invoking the write chain.</summary>
public sealed class TtcInput
{
    public required string Label { get; init; }
    public required string FilePath { get; init; }
    public required byte[] Bytes { get; init; }
    public required int FaceCount { get; init; }
    /// <summary>Per-face first editable UTF-16 name ordinal; -1 when none.</summary>
    public required int[] EditOrdinals { get; init; }

    public BuildLimits Limits { get; } = new();
}

/// <summary>Input preparation over FontWeave's public read API. Every derived value here
/// is exactly what a CLI caller would have to spell out on the command line; none of it
/// runs inside a measured region.</summary>
public static class CorpusLoader
{
    public const string EditText = "FontWeave Bench";
    public const int CodepointBudget = 512;
    public const int GidRootBudget = 512;

    private const uint MaxpTag = 0x6D617870; // 'maxp'
    private const uint NameTag = 0x6E616D65; // 'name'
    private const uint CmapTag = 0x636D6170; // 'cmap'

    /// <summary>The product's own Unicode-semantic cmap record predicate
    /// (SubsetCmapRebuild.cs:85-86): platform 0 in full, or the Windows Unicode
    /// encodings (3,0)/(3,1)/(3,10).</summary>
    private static bool IsUnicodeSemantic(ushort platformId, ushort encodingId) =>
        platformId == 0 || (platformId == 3 && encodingId is 0 or 1 or 10);

    /// <summary>The product's own UTF-16 name-record predicate
    /// (NameView.IsUtf16): Unicode platform encodings 0..4 and the Windows
    /// encodings (3,0)/(3,1)/(3,2)/(3,6)/(3,10).</summary>
    private static bool IsUtf16NameRecord(ushort platformId, ushort encodingId) =>
        (platformId == 0 && encodingId <= 4) ||
        (platformId == 3 && encodingId is 0 or 1 or 2 or 6 or 10);

    public static FontInput LoadSfnt(string inputsDir, string fileName)
    {
        string path = Path.Combine(inputsDir, fileName);
        byte[] bytes = ReadAllBytes(path);
        var limits = new BuildLimits();

        // The typed open chain, exactly as the CLI's typed commands run it.
        ReadStatus status = FontSource.TryCopy(bytes, out FontSource? source, out _, limits.MaxInputBytes);
        ThrowOnFailure(fileName, "FontSource.TryCopy", status);
        status = source!.TryGetFace(0, out FontFaceRef face, out _);
        ThrowOnFailure(fileName, "TryGetFace", status);

        int numGlyphs = ReadNumGlyphs(fileName, face, limits);
        var codepoints = DeriveCodepoints(fileName, face, limits);
        int editOrdinal = FindEditOrdinal(fileName, face, limits);

        return new FontInput
        {
            Label = fileName,
            FilePath = path,
            Bytes = bytes,
            NumGlyphs = numGlyphs,
            Gids512 = [.. Enumerable.Range(0, Math.Min(GidRootBudget, numGlyphs)).Select(i => (ushort)i)],
            GidsAll = [.. Enumerable.Range(0, numGlyphs).Select(i => (ushort)i)],
            Codepoints = codepoints,
            EditOrdinal = editOrdinal,
        };
    }

    public static TtcInput LoadTtc(string inputsDir, string fileName)
    {
        string path = Path.Combine(inputsDir, fileName);
        byte[] bytes = ReadAllBytes(path);

        // The legacy snapshot chain parses every face directory up front; face count
        // and the per-face editable ordinals are fixed input preparation.
        FontFile file = FontFile.Parse(bytes, new FontLimits());
        var ordinals = new int[file.FaceCount];
        for (int i = 0; i < file.FaceCount; i++)
        {
            NameTable names = NameTable.Parse(file.GetFace(i).GetTable("name").Data, new FontLimits());
            ordinals[i] = -1;
            foreach (var record in names.Records)
            {
                if (record.NameId is 1 or 2 or 4 or 16 or 17 &&
                    IsUtf16NameRecord(record.PlatformId, record.EncodingId))
                {
                    ordinals[i] = record.Ordinal;
                    break;
                }
            }
        }
        return new TtcInput
        {
            Label = fileName,
            FilePath = path,
            Bytes = bytes,
            FaceCount = file.FaceCount,
            EditOrdinals = ordinals,
        };
    }

    private static int ReadNumGlyphs(string label, FontFaceRef face, BuildLimits limits)
    {
        ReadStatus status = face.TryOpenDirectory(out var directory, out var diagnostic);
        ThrowOnFailure(label, "TryOpenDirectory", status, diagnostic);
        status = directory.TryFindUniqueTable(MaxpTag, out int ordinal, out diagnostic);
        ThrowOnFailure(label, "TryFindUniqueTable(maxp)", status, diagnostic);
        status = directory.TryGetTableBytes(ordinal, out var bytes, out diagnostic);
        ThrowOnFailure(label, "TryGetTableBytes(maxp)", status, diagnostic);
        status = MaxpView.TryOpen(bytes, out var view, out diagnostic, directory.TableLocation(ordinal));
        ThrowOnFailure(label, "MaxpView.TryOpen", status, diagnostic);
        status = view.TryRead(out var maxp, out diagnostic, limits);
        ThrowOnFailure(label, "MaxpView.TryRead", status, diagnostic);
        return maxp!.NumGlyphs;
    }

    /// <summary>Derives the Unicode root from the typed cmap read: the consistent
    /// forward mapping of every Unicode-semantic record (the product's R1/R2 shape —
    /// a scalar survives only when every such record agrees on its GID), restricted
    /// to non-variation-selector scalars. Selection: the mapped ASCII printable range
    /// ascending first, then stride-selected additional mapped scalars up to
    /// <see cref="CodepointBudget"/> total. An empty result means the face offers no
    /// safely resolvable scalar; the scenario then records the product's refusal.</summary>
    private static uint[] DeriveCodepoints(string label, FontFaceRef face, BuildLimits limits)
    {
        ReadStatus status = face.TryOpenDirectory(out var directory, out var diagnostic);
        ThrowOnFailure(label, "TryOpenDirectory", status, diagnostic);
        status = directory.TryFindUniqueTable(MaxpTag, out int maxpOrdinal, out diagnostic);
        ThrowOnFailure(label, "TryFindUniqueTable(maxp)", status, diagnostic);
        status = directory.TryFindUniqueTable(CmapTag, out int cmapOrdinal, out diagnostic);
        ThrowOnFailure(label, "TryFindUniqueTable(cmap)", status, diagnostic);
        status = directory.TryGetTableBytes(maxpOrdinal, out var maxpBytes, out diagnostic);
        ThrowOnFailure(label, "TryGetTableBytes(maxp)", status, diagnostic);
        status = MaxpView.TryOpen(maxpBytes, out var maxpView, out diagnostic, directory.TableLocation(maxpOrdinal));
        ThrowOnFailure(label, "MaxpView.TryOpen", status, diagnostic);
        status = maxpView.TryRead(out var maxp, out diagnostic, limits);
        ThrowOnFailure(label, "MaxpView.TryRead", status, diagnostic);
        status = directory.TryGetTableBytes(cmapOrdinal, out var cmapBytes, out diagnostic);
        ThrowOnFailure(label, "TryGetTableBytes(cmap)", status, diagnostic);
        DataLocation cmapAt = directory.TableLocation(cmapOrdinal);
        status = CmapContext.TryCreate(maxp!, cmapAt, out var context, out diagnostic);
        ThrowOnFailure(label, "CmapContext.TryCreate", status, diagnostic);
        status = CmapView.TryOpen(cmapBytes, out var view, out diagnostic, cmapAt);
        ThrowOnFailure(label, "CmapView.TryOpen", status, diagnostic);
        status = view.TryRead(context!, out var model, out diagnostic, limits);
        ThrowOnFailure(label, "CmapView.TryRead", status, diagnostic);

        Dictionary<uint, ushort>? consistent = null;
        bool consulted = false;
        var records = model!.Records;
        for (int i = 0; i < records.Length; i++)
        {
            if (!IsUnicodeSemantic(records[i].PlatformId, records[i].EncodingId)) continue;
            status = model.TryGetSubtable(i, out var subtable, out diagnostic);
            ThrowOnFailure(label, "CmapTable.TryGetSubtable", status, diagnostic);
            status = model.TryGetMappings(subtable!, out var mappings, out diagnostic, limits);
            if (status != ReadStatus.Done)
            {
                // The product's codepoint resolution point-queries every
                // Unicode-semantic record; a record that cannot be enumerated
                // (format 14 / raw) makes any root refuse there. An empty
                // derivation is the honest outcome and the scenario records the
                // product's own refusal at run time.
                return [];
            }
            consulted = true;
            if (consistent is null)
            {
                consistent = [];
                foreach (var mapping in mappings!)
                    consistent[mapping.CharacterCode] = mapping.GlyphId;
                continue;
            }
            var agreed = new Dictionary<uint, ushort>(consistent.Count);
            foreach (var mapping in mappings!)
                if (consistent.TryGetValue(mapping.CharacterCode, out ushort previous) &&
                    previous == mapping.GlyphId)
                    agreed[mapping.CharacterCode] = mapping.GlyphId;
            consistent = agreed;
        }

        if (!consulted || consistent is null || consistent.Count == 0) return [];

        List<uint> selected = [];
        // Posture 6: variation selectors have no third state and never ride a root.
        static bool IsVariationSelector(uint code) =>
            code is >= 0xFE00 and <= 0xFE0F || code is >= 0xE0100 and <= 0xE01EF;

        var all = consistent.Keys.Where(code => !IsVariationSelector(code)).Order().ToArray();
        foreach (uint code in all)
        {
            if (selected.Count >= CodepointBudget) break;
            if (code is >= 0x20 and <= 0x7E) selected.Add(code);
        }
        if (selected.Count < CodepointBudget)
        {
            // Extras ride above the ASCII block only: mapped control code points
            // (below 0x20) would descend behind the already-selected ASCII range and
            // violate the root's strictly-ascending input contract (R3).
            var extras = all.Where(code => code > 0x7E).ToArray();
            int budget = CodepointBudget - selected.Count;
            int stride = Math.Max(1, (extras.Length + budget - 1) / budget);
            for (int i = 0; i < extras.Length && selected.Count < CodepointBudget; i += stride)
                selected.Add(extras[i]);
        }
        return [.. selected];
    }

    private static int FindEditOrdinal(string label, FontFaceRef face, BuildLimits limits)
    {
        ReadStatus status = face.TryOpenDirectory(out var directory, out var diagnostic);
        ThrowOnFailure(label, "TryOpenDirectory", status, diagnostic);
        status = directory.TryFindUniqueTable(NameTag, out int ordinal, out diagnostic);
        ThrowOnFailure(label, "TryFindUniqueTable(name)", status, diagnostic);
        status = directory.TryGetTableBytes(ordinal, out var bytes, out diagnostic);
        ThrowOnFailure(label, "TryGetTableBytes(name)", status, diagnostic);
        status = NameView.TryOpen(bytes, out var view, out diagnostic, directory.TableLocation(ordinal));
        ThrowOnFailure(label, "NameView.TryOpen", status, diagnostic);
        status = view.TryRead(out var model, out diagnostic, limits);
        ThrowOnFailure(label, "NameView.TryRead", status, diagnostic);
        foreach (var record in model!.Records)
            if (record.NameId is 1 or 2 or 4 or 16 or 17 &&
                IsUtf16NameRecord(record.PlatformId, record.EncodingId))
                return record.Ordinal;
        return -1;
    }

    /// <summary>The UTF-16BE payload the edit scenarios substitute, mirroring the CLI's
    /// edit-rebuild encoding contract.</summary>
    public static byte[] EditPayloadUtf16Be() => new UnicodeEncoding(bigEndian: true, byteOrderMark: false).GetBytes(EditText);

    private static byte[] ReadAllBytes(string path) =>
        File.Exists(path)
            ? File.ReadAllBytes(path)
            : throw new FileNotFoundException($"Corpus file not found: {path}");

    private static void ThrowOnFailure(string label, string step, ReadStatus status, FontDiagnostic diagnostic = default) =>
        _ = status == ReadStatus.Done ? status
            : throw new InvalidOperationException(
                $"{label}: {step} refused with {status} ({diagnostic.Code}). Input preparation cannot proceed.");
}
