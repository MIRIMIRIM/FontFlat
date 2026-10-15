using FontWeave.Core;

namespace FontWeave.Operations.Subset;

/// <summary>Explicit either/or subset mode; there is no default implicit mode. An
/// undeclared or out-of-domain value is a finite refusal (W1 §1.4, S0-RF-006-MODE).</summary>
public enum SubsetMode : byte { RetainGids = 1, CompactRemap = 2 }

/// <summary>Discriminant of the request root. The root produces the input GID set; both
/// kinds feed the same pipeline once resolved (W1 §1.2).</summary>
public enum SubsetRootKind : byte { ExplicitGlyphSet = 1, UnicodeCodepoints = 2 }

/// <summary>The request root: exactly one form must be populated. Explicit GIDs are a
/// normalized input sequence (strictly ascending, no duplicates); every value must fall
/// inside the source face's maxp.numGlyphs domain, and violations are finite refusals,
/// never silent normalization (W1 §1.2; card §1.2).</summary>
public sealed record SubsetGlyphRoot
{
    public SubsetRootKind Kind { get; init; }

    /// <summary>Form (a) root: caller-supplied input-face GIDs. This is the only root
    /// the S1a slice executes; form (b) is refused as not-yet-implemented.</summary>
    public IReadOnlyList<ushort>? Gids { get; init; }

    /// <summary>Form (b) root: Unicode scalar values resolved through the typed cmap
    /// read. Carried for contract completeness; resolution lands with a later slice.</summary>
    public IReadOnlyList<uint>? Codepoints { get; init; }
}

/// <summary>Per-table caller choice for out-of-catalogue tags. There is deliberately no
/// batch preserve/drop switch: each unknown tag is declared individually and every
/// declaration is reported (W1 §2.1, OQ-S0-1).</summary>
public enum SubsetUnknownTableChoice : byte { Drop = 1, Preserve = 2 }

/// <summary>The five explicit caller option families (the S2a slice added
/// PreserveLayout as the fifth member; the original four keep their frozen
/// semantics byte for byte). Everything else is decided by the frozen policy
/// matrix and cannot be negotiated by a request (W1 §2).</summary>
public sealed record SubsetOptions
{
    /// <summary>Per-tag declarations for out-of-catalogue tags only; a known tag here is
    /// an invalid request. A missing declaration for a present unknown tag refuses.</summary>
    public IReadOnlyDictionary<uint, SubsetUnknownTableChoice>? UnknownTables { get; init; }

    /// <summary>true drops every present layout-family tag (kern/GSUB/GPOS/GDEF/BASE/
    /// JSTF/MATH); false (default) with any of them present refuses the whole operation
    /// and points to S2 (W1 §2.2).</summary>
    public bool NoLayout { get; init; }

    /// <summary>true retains every present layout-family tag raw (S2a card §1.1): the
    /// operation admits each present layout table through its whole-table typed read
    /// chain and extends the retained set with the layout reference union before the
    /// composite closure. Legal only in retain-GIDs mode and only together with
    /// NoLayout=false — the double-true request refuses outright (S2a RF-D), and a
    /// compact-remap request keeps the layout-family refusal unchanged (layout
    /// rewrite is S2b's execution face).</summary>
    public bool PreserveLayout { get; init; }

    /// <summary>true drops fpgm/prep/cvt/LTSH and strips glyf instructions during the
    /// rebuild; gasp is preserved regardless (W1 §2.3, OQ-S0-4).</summary>
    public bool DropHinting { get; init; }

    /// <summary>true drops DSIG; false preserves an empty marker and refuses a real
    /// signature (W1 §2.4, OQ-S0-2).</summary>
    public bool DropDigitalSignature { get; init; }

    /// <summary>Any true value expresses GID 0 exclusion and refuses the operation;
    /// .notdef is always an implicit member (W1 §1.3).</summary>
    public bool ExcludeNotdef { get; init; }
}

/// <summary>A retain-GIDs/compact-remap subset request bound to one source face. The
/// request carries no head/maxp/name or metric boundary edits by construction — those
/// members do not exist here (W1 §1.5).</summary>
public sealed record SubsetRequest
{
    /// <summary>The bound source face (Face.IsBound). An unbound face is a caller
    /// parameter error, not a font diagnostic (W1 §1.1).</summary>
    public FontFaceRef Face { get; init; }

    /// <summary>Exactly one root form; both or neither is a finite refusal (W1 §1.2).</summary>
    public SubsetGlyphRoot? Root { get; init; }

    /// <summary>null means undeclared, which refuses (S0-RF-006-MODE).</summary>
    public SubsetMode? Mode { get; init; }

    public SubsetOptions Options { get; init; } = new();
}

/// <summary>Effective per-table policy word, restricted to the matrix vocabulary that can
/// appear in a decision: RETAIN_RAW/REBUILD_TYPED/DELETE. REFUSE fails the operation and
/// DEFERRED_* has no execution path, so neither enters a TableDecision (W1 §2).</summary>
public enum SubsetPolicy : byte { RetainRaw = 1, RebuildTyped = 2, Delete = 3 }

/// <summary>Where a decision's policy came from: the frozen matrix row, an explicit
/// request option, or a mode-coupled rule (W1 §4.2).</summary>
public enum SubsetDecisionOrigin : byte { MatrixRow = 1, RequestOption = 2, ModeCoupling = 3 }

/// <summary>One wire-order table decision plus its subset provenance. The Tag/Action
/// prefix mirrors the existing TableDecision contract so the compiled font and this
/// report stay consistent (W1 §4.2).</summary>
public readonly record struct SubsetTableDecision(uint Tag, TableAction Action,
    SubsetPolicy Policy, SubsetDecisionOrigin Origin);

/// <summary>Which explicit option a record captures.</summary>
public enum SubsetOptionId : byte
{
    Mode = 1, NoLayout = 2, DropHinting = 3, DropDigitalSignature = 4, ExcludeNotdef = 5,
    UnknownTable = 6, PreserveLayout = 7
}

/// <summary>Machine-readable record of one effective option choice, so an auditor can
/// reconstruct the policy set that was in force from the report alone (W1 §2.5).</summary>
public readonly record struct SubsetOptionRecord(SubsetOptionId Id, uint Tag, string Value);

/// <summary>Compact-remap map artifact (W1 §4.3 content columns). Retain-GIDs never
/// constructs one and SubsetResult.Map is null there; the producing slice owns the
/// deterministic serialization whose sha256 is pinned into SubsetReport. The input face
/// sha256 travels with the process-local identity so an artifact stays traceable across
/// processes (W1 §7 finding 4); it must be present when the artifact serializes.</summary>
public sealed record FrozenMapArtifact(long SourceId, int FaceIndex, int FontRevisionRaw,
    int InputGlyphCount, int OutputGlyphCount, IReadOnlyList<FrozenMapEntry> Entries)
{
    /// <summary>The source face's full input bytes sha256 (64 hex characters).</summary>
    public string? InputFaceSha256 { get; init; }
}

/// <summary>One map artifact row: output ordinal, source ordinal and the stable identity
/// column's serialized form (W1 §3.4).</summary>
public readonly record struct FrozenMapEntry(ushort OutputGid, ushort InputGid,
    string StableIdentity);

/// <summary>One cmap encoding record that was read as input but not carried into the
/// canonical rebuild: non-Unicode-semantic records (e.g. (1,0) Mac Roman) are consumed
/// for evidence and registered here instead of being refused or re-emitted (OQ-S1a-2).</summary>
public readonly record struct CmapConsumedRecord(ushort PlatformId, ushort EncodingId);

/// <summary>Per-table policy report (W1 §4.2 member set). Success and failure paths share
/// this type: a failure retains every decision formed before the first refusing one plus
/// the diagnostic itself, and no font bytes are published.</summary>
public sealed class SubsetReport
{
    private readonly SubsetTableDecision[] _decisions;
    private readonly ushort[] _closureAddedGids;
    private readonly uint[] _droppedTables;
    private readonly SubsetOptionRecord[] _optionRecords;
    private readonly CmapConsumedRecord[] _cmapConsumedNotEmitted;
    private readonly FontDiagnostic[] _diagnostics;
    private readonly string? _mapArtifactSha256;

    internal SubsetReport(SubsetMode? mode, SubsetTableDecision[] decisions,
        int unselectedSlotCount, ushort[] closureAddedGids, uint[] droppedTables,
        SubsetOptionRecord[] optionRecords, CmapConsumedRecord[] cmapConsumedNotEmitted,
        FontDiagnostic[] diagnostics, string? mapArtifactSha256 = null,
        CffStaleness? cffStaleness = null, VorgStaleness? vorgStaleness = null)
    {
        Mode = mode;
        _decisions = decisions;
        UnselectedSlotCount = unselectedSlotCount;
        _closureAddedGids = closureAddedGids;
        _droppedTables = droppedTables;
        _optionRecords = optionRecords;
        _cmapConsumedNotEmitted = cmapConsumedNotEmitted;
        _diagnostics = diagnostics;
        _mapArtifactSha256 = mapArtifactSha256;
        Cff = cffStaleness;
        Vorg = vorgStaleness;
    }

    /// <summary>The declared mode; null when the request was refused before a mode could
    /// be accepted (S0-RF-006-MODE).</summary>
    public SubsetMode? Mode { get; }

    /// <summary>Wire-order decisions formed before the pipeline stopped. A successful
    /// prepare carries the complete decision set, which was fixed before any payload
    /// work (decide-whole-before-compile, W1 §4.4).</summary>
    public IReadOnlyList<SubsetTableDecision> Decisions =>
        Array.AsReadOnly(_decisions);

    /// <summary>Retain-GIDs slots left empty because the root (plus closure) did not
    /// select them; metrics on those slots keep their original values (W1 §1.4).</summary>
    public int UnselectedSlotCount { get; }

    /// <summary>GIDs the composite dependency closure pulled into the retained set,
    /// ascending, excluding the root and the implicit GID 0 (card §3).</summary>
    public IReadOnlyList<ushort> ClosureAddedGids => Array.AsReadOnly(_closureAddedGids);

    /// <summary>Tags whose action is Drop; consistent with Decisions (W1 §4.2).</summary>
    public IReadOnlyList<uint> DroppedTables => Array.AsReadOnly(_droppedTables);

    /// <summary>Every effective option choice, including per-tag unknown-table
    /// declarations (W1 §2.5).</summary>
    public IReadOnlyList<SubsetOptionRecord> OptionRecords => Array.AsReadOnly(_optionRecords);

    /// <summary>Cmap input records read but not re-emitted by the canonical rebuild,
    /// in wire order (OQ-S1a-2).</summary>
    public IReadOnlyList<CmapConsumedRecord> CmapConsumedNotEmitted =>
        Array.AsReadOnly(_cmapConsumedNotEmitted);

    /// <summary>Compact-remap pins the canonical map artifact serialization's sha256
    /// here at the moment the artifact serializes — before any payload work, so a later
    /// refusal still evidences the frozen map it had frozen (W1 §4.3). A budget failure
    /// during the serialization itself leaves it null: no artifact exists. Retain-GIDs
    /// keeps it null because no map artifact exists (W1 §1.4, §4.3).</summary>
    public string? MapArtifactSha256 => _mapArtifactSha256;

    /// <summary>The first failure diagnostic; empty on success (W1 §4.5).</summary>
    public IReadOnlyList<FontDiagnostic> Diagnostics => Array.AsReadOnly(_diagnostics);

    /// <summary>RETAIN_RAW registration facts for the CFF table of an admitted static
    /// CFF1 face; null on every other profile (TrueType faces and the zero-rebuild
    /// profile carry no CFF staleness — S1b card §3).</summary>
    public CffStaleness? Cff { get; }

    /// <summary>RETAIN_RAW registration facts for a VORG table on an admitted static
    /// CFF1 face; null when the face has no VORG table or is not a CFF face (S1b §3).</summary>
    public VorgStaleness? Vorg { get; }

    /// <summary>The maxp 1.0 maxima are not recomputed: retain-GIDs preserves maxp raw,
    /// and compact rebuilds maxp with only numGlyphs changed over the map output domain
    /// (OQ-SC-1, the same shape as OQ-S1a-10). This is an explicit fact, not an
    /// oversight: the output glyph set is a subset of the input set, so the original
    /// maxima remain upper bounds of the output domain.</summary>
    public bool MaxpMaximaNotRecomputed => true;
}

/// <summary>Prepared subset output. The font carries bytes, budget usage and plain table
/// decisions; the map is null in retain-GIDs; the report carries the policy evidence.</summary>
public sealed class SubsetResult
{
    internal SubsetResult(CompiledFont font, FrozenMapArtifact? map, SubsetReport report)
    {
        Font = font; Map = map; Report = report;
    }

    /// <summary>Publish through Font.TryCopyTo; a short destination writes nothing.</summary>
    public CompiledFont Font { get; }

    /// <summary>Always null in retain-GIDs: the identity mapping is definitional and no
    /// map artifact is produced (W1 §1.4).</summary>
    public FrozenMapArtifact? Map { get; }

    public SubsetReport Report { get; }
}
