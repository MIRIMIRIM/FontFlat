using FontWeave.Core;

namespace FontWeave.Operations.Subset;

/// <summary>Explicit either/or subset mode; there is no default implicit mode. An
/// undeclared or out-of-domain value is a finite refusal (W1 §1.4, S0-RF-006-MODE).</summary>
public enum SubsetMode : byte { RetainGids = 1, CompactRemap = 2 }

/// <summary>Discriminant of the request root. The root produces the input GID set; every
/// kind feeds the same pipeline once resolved (W1 §1.2; the SR slice executes the two
/// resolved forms alongside the explicit one).</summary>
public enum SubsetRootKind : byte { ExplicitGlyphSet = 1, UnicodeCodepoints = 2, TextLayout = 3 }

/// <summary>The request root: exactly one form must be populated. Explicit GIDs are a
/// normalized input sequence (strictly ascending, no duplicates); every value must fall
/// inside the source face's maxp.numGlyphs domain, and violations are finite refusals,
/// never silent normalization (W1 §1.2; card §1.2).</summary>
public sealed record SubsetGlyphRoot
{
    public SubsetRootKind Kind { get; init; }

    /// <summary>Form (a) root: caller-supplied input-face GIDs. The seed is the caller's
    /// sequence verbatim (validated, never normalized).</summary>
    public IReadOnlyList<ushort>? Gids { get; init; }

    /// <summary>Form (b) root: Unicode scalar values resolved through the typed cmap
    /// read — every Unicode-semantic record is consulted, a code point with no mapping
    /// or with two distinct mappings refuses, and a resolution never produces a partial
    /// GID set (W1 §1.2; SR card §2.1).</summary>
    public IReadOnlyList<uint>? Codepoints { get; init; }

    /// <summary>Form (c) root (SR card §2.2): UTF-16 text decoded once into a Unicode
    /// scalar-value set (deduplicated, order-free — text is a set-semantics entry, unlike
    /// the form (b) sequence's precise input contract; an unpaired surrogate refuses).
    /// Zero normalization is performed: the request is interpreted over its characters
    /// as given. The decoded set resolves through exactly the form (b) path.</summary>
    public string? Text { get; init; }

    /// <summary>Form (c) declaration dimensions (SR card §2.2, caller-declared — the
    /// pipeline has no built-in Unicode script data to derive them): the same value
    /// domain and matching semantics as <see cref="SubsetLayoutScope"/>. A non-null
    /// member derives that scope dimension; when any member is non-null the three
    /// together derive the request's layout scope, mutually exclusive with
    /// <see cref="SubsetOptions.LayoutScope"/> (the dual-source request refuses).</summary>
    public IReadOnlyList<uint>? ScriptTags { get; init; }

    /// <summary>Form (c) derived-scope language dimension (tagged LangSys record tags);
    /// null derives nothing for this dimension. See <see cref="ScriptTags"/>.</summary>
    public IReadOnlyList<uint>? LanguageTags { get; init; }

    /// <summary>Form (c) derived-scope feature dimension (FeatureList entry tags);
    /// null derives nothing for this dimension. See <see cref="ScriptTags"/>.</summary>
    public IReadOnlyList<uint>? FeatureTags { get; init; }
}

/// <summary>Per-table caller choice for out-of-catalogue tags. There is deliberately no
/// batch preserve/drop switch: each unknown tag is declared individually and every
/// declaration is reported (W1 §2.1, OQ-S0-1).</summary>
public enum SubsetUnknownTableChoice : byte { Drop = 1, Preserve = 2 }

/// <summary>The caller's explicit three-tier layout retention scope (S2c card §1.1):
/// per dimension null = the dimension is wholly kept, a non-null list = exactly the
/// listed tags survive (a 4-byte script tag, a tagged LangSys record tag, a
/// FeatureList entry tag — matched verbatim), and an empty list = the dimension is
/// cut to empty (a legal value — the zero-count ScriptList root is a legal wire
/// form). There are no wildcards and no negative lists: a null member covers the
/// wildcard's expressiveness (OQ-S2c-2). A declared tag absent from the face is
/// ignored and registered (the per-dimension match counts make the typo visible in
/// the scope census, OQ-S2c-7/10), never a refusal — a refusal would couple the
/// declaration to one face's catalogue.</summary>
public sealed record SubsetLayoutScope
{
    /// <summary>The surviving ScriptList record tags; null keeps every script.</summary>
    public IReadOnlyList<uint>? ScriptTags { get; init; }

    /// <summary>The surviving tagged LangSys record tags inside every surviving
    /// script; null keeps every tagged language. A script's DefaultLanguage carries
    /// no tag, so it follows the script and is never constrained by this list.</summary>
    public IReadOnlyList<uint>? LanguageTags { get; init; }

    /// <summary>The surviving FeatureList entry tags; null keeps every feature.
    /// FeatureList is one global list with per-(script,langsys) eligibility copies of
    /// its entries, so a declared feature tag keeps *all* of that tag's entries —
    /// the eligibility dimension is adjudicated independently by the script and
    /// language lists. A surviving langsys's required feature is implicitly kept
    /// (shapers apply it unconditionally) even when its tag is not declared.</summary>
    public IReadOnlyList<uint>? FeatureTags { get; init; }
}

/// <summary>The six explicit caller option families (the S2a slice added
/// PreserveLayout as the fifth member; the S2c slice added LayoutScope as the
/// sixth; the original four keep their frozen semantics byte for byte). Everything
/// else is decided by the frozen policy matrix and cannot be negotiated by a
/// request (W1 §2).</summary>
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

    /// <summary>The explicit script/language/feature retention scope of the layout
    /// family (S2c card §1). null (the member itself, not an empty scope record) means
    /// undeclared: the PreserveLayout semantics stay exactly the pre-S2c form — in
    /// retain-GIDs the whole-table RETAIN_RAW closure, in compact-remap the S2b whole-
    /// table rewrite — byte for byte. A declared scope (any member non-null) is legal
    /// only together with PreserveLayout=true and NoLayout=false: deleting the layout
    /// family or refusing to retain it has no common meaning with declaring which part
    /// of it survives — both contradictions refuse outright under the code-23 contract
    /// (S2c card §1.3). Declaring a scope whose resolution keeps every script, feature
    /// and lookup is the no-op form: the output is byte-identical to the undeclared
    /// request (the consequence-equivalence verdict, S2c card §4.2); a declaration
    /// that truly cuts pulls the surviving-subset re-emission (the matrix's fifth
    /// alternate).</summary>
    public SubsetLayoutScope? LayoutScope { get; init; }

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

/// <summary>Which explicit option a record captures. The SR slice added Root
/// (the root form in force) and TextRootScope (the TextLayout root's derived
/// scope declaration, one record per declared tag) as members 9 and 10 (OQ-SR-6).</summary>
public enum SubsetOptionId : byte
{
    Mode = 1, NoLayout = 2, DropHinting = 3, DropDigitalSignature = 4, ExcludeNotdef = 5,
    UnknownTable = 6, PreserveLayout = 7, LayoutScope = 8, Root = 9, TextRootScope = 10
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

/// <summary>Per-table reference census of one retained layout table (OQ-S2a-4): the
/// number of distinct GIDs the table's reference union enumerated (count level only —
/// never a per-GID listing). A zero row is the legal no-reference form (BASE).</summary>
public readonly record struct LayoutTableCensus(uint Tag, int ReferencedGidCount);

/// <summary>Per-table rewrite census of one compact+PreserveLayout rewritten layout
/// table (OQ-S2b-10): the count-level in/out facts — subtable counts (GSUB/GPOS: the
/// lookup list's total subtable count; GDEF: the present top-level child blocks),
/// table payload lengths, and whether the count-identity invariant held (the §3
/// zero-rule-loss defense's recorded result; the GDEF class domains' definitional
/// entry shrink is entry-level, so it never moves this count). A row for a table the
/// S2c scope rewrote over its surviving subset legally reads in≠out — the
/// count-identity assertion scopes to the rows of unscoped rewrites only
/// (OQ-S2c-10); every surviving lookup's own subtable count is still asserted.</summary>
public readonly record struct LayoutRewriteCensusRow(uint Tag, int InputSubtableCount,
    int OutputSubtableCount, int InputBytes, int OutputBytes,
    bool CountsInvarianceHeld);

/// <summary>Per-table scope census of one resolved layout list table (OQ-S2c-10):
/// the input and surviving element counts (scripts/features/lookups) plus the
/// per-dimension declared-tag match counts — a declared tag matching nothing (the
/// typo case) reads zero here and is never a refusal (OQ-S2c-7). Rows appear for
/// every layout list table a declared scope resolved, no-op verdicts included;
/// every request without a declared scope carries no rows.</summary>
public readonly record struct LayoutScopeCensusRow(uint Tag, int InputScriptCount,
    int SurvivingScriptCount, int InputFeatureCount, int SurvivingFeatureCount,
    int InputLookupCount, int SurvivingLookupCount, int ScriptTagMatches,
    int LanguageTagMatches, int FeatureTagMatches, int OrphanMarkSets);

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
    private readonly ushort[] _layoutClosureAddedGids;
    private readonly LayoutTableCensus[] _layoutReferenceCensus;
    private readonly LayoutRewriteCensusRow[] _layoutRewriteCensus;
    private readonly LayoutScopeCensusRow[] _layoutScopeCensus;
    private readonly ushort[] _seacClosureAddedGids;

    internal SubsetReport(SubsetMode? mode, SubsetTableDecision[] decisions,
        int unselectedSlotCount, ushort[] closureAddedGids, uint[] droppedTables,
        SubsetOptionRecord[] optionRecords, CmapConsumedRecord[] cmapConsumedNotEmitted,
        FontDiagnostic[] diagnostics, string? mapArtifactSha256 = null,
        CffStaleness? cffStaleness = null, VorgStaleness? vorgStaleness = null,
        ushort[]? layoutClosureAddedGids = null,
        LayoutTableCensus[]? layoutReferenceCensus = null,
        LayoutRewriteCensusRow[]? layoutRewriteCensus = null,
        LayoutScopeCensusRow[]? layoutScopeCensus = null,
        ushort[]? seacClosureAddedGids = null)
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
        _layoutClosureAddedGids = layoutClosureAddedGids ?? [];
        _layoutReferenceCensus = layoutReferenceCensus ?? [];
        _layoutRewriteCensus = layoutRewriteCensus ?? [];
        _layoutScopeCensus = layoutScopeCensus ?? [];
        _seacClosureAddedGids = seacClosureAddedGids ?? [];
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

    /// <summary>GIDs the S2a layout reference union pulled into the retained set —
    /// L \ (seed ∪ {0}) at the moment the merge ran, ascending, before the composite
    /// closure (S2a card §2.6). Empty on every request without PreserveLayout. The
    /// implementation invariant keeps this list disjoint from
    /// <see cref="ClosureAddedGids"/>: the layout merge runs first, and the composite
    /// closure only ever adds glyphs still unretained.</summary>
    public IReadOnlyList<ushort> LayoutClosureAddedGids =>
        Array.AsReadOnly(_layoutClosureAddedGids);

    /// <summary>GIDs the CFF1 seac closure pulled into the retained set (S3 card §2.3):
    /// the base and accent glyphs every retained Type 2 endchar seac form references by
    /// standard-encoding code, ascending, excluding glyphs already retained (the root,
    /// the implicit GID 0 and any layout addition). Empty on every request whose face
    /// carries no static CFF1 table with a seac form. Disjoint from the layout list by
    /// construction: the seac closure runs after the layout union and only ever adds
    /// glyphs still unretained.</summary>
    public IReadOnlyList<ushort> SeacClosureAddedGids =>
        Array.AsReadOnly(_seacClosureAddedGids);

    /// <summary>The per-table reference census of every retained layout table, in the
    /// admission order (S2a card §2.6): count level only. Empty without PreserveLayout.</summary>
    public IReadOnlyList<LayoutTableCensus> LayoutReferenceCensus =>
        Array.AsReadOnly(_layoutReferenceCensus);

    /// <summary>The per-table rewrite census of every compact+PreserveLayout rewritten
    /// layout table, in the rewrite order GDEF→GSUB→GPOS (OQ-S2b-10): count level only.
    /// Empty on every request the rewrite did not run.</summary>
    public IReadOnlyList<LayoutRewriteCensusRow> LayoutRewriteCensus =>
        Array.AsReadOnly(_layoutRewriteCensus);

    /// <summary>The per-table scope census of every layout list table the declared
    /// LayoutScope resolved (OQ-S2c-10), in the resolution order GSUB→GPOS: the
    /// surviving element counts and the per-dimension declared-tag match counts,
    /// count level only. Empty on every request without a declared scope.</summary>
    public IReadOnlyList<LayoutScopeCensusRow> LayoutScopeCensus =>
        Array.AsReadOnly(_layoutScopeCensus);

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
