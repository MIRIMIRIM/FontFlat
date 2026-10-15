using FontWeave.Containers;
using FontWeave.Core;
using FontWeave.Tables;

namespace FontWeave.Operations.Subset;

/// <summary>RETAIN_RAW registration facts for the CFF table of an admitted static CFF1
/// face (S1b card §3). These are report evidence, never diagnostics: the bytes move
/// untouched, and the report records what the output still references. The read depth is
/// the frozen lean ruling (OQ-S1b-1): the decide-stage <c>Validate</c> plus bounded
/// accessors only — the name-keyed/CID-keyed models are never materialized, so fields
/// that would need a full expansion (FD counts, per-glyph SID columns) stay out of this
/// record and land with the compact CFF rebuild slice that must materialize anyway.</summary>
public sealed record CffStaleness
{
    internal CffStaleness(string flavor, int charStringsCount,
        Cff1CharsetForm charsetForm, Cff1EncodingForm? encodingForm,
        bool charsetSidColumnReachable, int[]? rosSids, int? cidCount, int? fdSelectFormat)
    {
        Flavor = flavor;
        CharStringsCount = charStringsCount;
        CharsetForm = charsetForm;
        EncodingForm = encodingForm;
        CharsetSidColumnReachable = charsetSidColumnReachable;
        _rosSids = rosSids;
        CidCount = cidCount;
        FdSelectFormat = fdSelectFormat;
    }

    /// <summary>Which CFF1 form the table carries, worded like the inspection profile
    /// ("CFF1 name-keyed" / "CFF1 CID-keyed"; detection is the Top DICT ROS key).</summary>
    public string Flavor { get; }

    /// <summary>The CharStrings INDEX count; admission refused the operation unless it
    /// equals maxp.numGlyphs, so this is also the output GID domain (card §1.2).</summary>
    public int CharStringsCount { get; }

    /// <summary>Charset reference form: predefined marker or custom table with its wire
    /// format byte (Cff1View.TryGetCharsetForm).</summary>
    public Cff1CharsetForm CharsetForm { get; }

    /// <summary>Encoding reference form, name-keyed only; CID-keyed fonts carry no
    /// Encoding (the sfnt cmap does), so this is null there.</summary>
    public Cff1EncodingForm? EncodingForm { get; }

    /// <summary>Name-keyed only: the per-glyph charset SID column resolves through the
    /// bounded accessor (TryGetSidForGlyph) — probed at both ends of the GID domain as
    /// reachability evidence; strict coverage was already guaranteed by Validate.</summary>
    public bool CharsetSidColumnReachable { get; }

    /// <summary>CID-keyed only: the ROS triple (registry SID, ordering SID, supplement),
    /// in that order; null on a name-keyed face.</summary>
    public IReadOnlyList<int>? RosSids => _rosSids is null ? null : Array.AsReadOnly(_rosSids);
    private readonly int[]? _rosSids;

    /// <summary>CID-keyed only: the raw CIDCount key value; null when the key is absent,
    /// in which case the CID space is bounded by 1&lt;&lt;16 (CidLayout.MaxCidCount).
    /// CIDCount is an upper bound of the CID space, not the GID domain, and is legal to
    /// differ from numGlyphs (W1 §3.4; card §1.3).</summary>
    public int? CidCount { get; }

    /// <summary>CID-keyed only: the FDSelect wire format (0 or 3 in CFF1); null on a
    /// name-keyed face.</summary>
    public int? FdSelectFormat { get; }

    /// <summary>Contract wording for what "unselected slot" means on a CFF face: the
    /// unselected GIDs' charstring, subr and metric bytes remain in the output verbatim
    /// (retain definition). "Unselected slot = empty glyph" describes the glyf rebuild
    /// only and must not be applied here (S1b card §3).</summary>
    public string UnselectedSlotSemantics =>
        "Unselected GIDs keep their CFF, VORG and metric bytes in the output " +
        "(RETAIN_RAW); the empty-slot semantics of the glyf rebuild do not apply.";
}

/// <summary>RETAIN_RAW registration facts for a VORG table on an admitted static CFF1
/// face (S1b card §3): the bytes move untouched, and the strict version/record rules
/// (VorgView.Validate) were checked once at admission, so the report only carries the
/// fields an auditor needs to reconstruct the retained vertical-origin surface.</summary>
public sealed record VorgStaleness
{
    internal VorgStaleness(short defaultVertOriginY, int numVertOriginYMetrics)
    {
        DefaultVertOriginY = defaultVertOriginY;
        NumVertOriginYMetrics = numVertOriginYMetrics;
    }

    public short DefaultVertOriginY { get; }

    /// <summary>Number of vertOriginYMetrics records; Validate guaranteed every record
    /// GID is inside the maxp domain, strictly ascending and unique.</summary>
    public int NumVertOriginYMetrics { get; }
}

/// <summary>Decide-stage admission reads behind the static CFF1 profile (S1b card §1.2).
/// Both readers run once, before any per-table policy decision is recorded; every
/// failure is a finite refusal whose diagnostic is the read side's own.</summary>
public static partial class SubsetOperation
{
    /// <summary>Typed admission read of the CFF table: TryOpen + strict Validate over
    /// the pipeline budget, the CharStrings↔maxp consistency check (FontInspection
    /// precedent shape), and the staleness facts. On success the CFF staleness record is
    /// parked on the state for BuildReport.</summary>
    private static bool TryAdmitCffFace(SfntDirectoryView directory, ProfileScan profile,
        MaxpTable maxp, PrepareState state, out ReadStatus refusalStatus,
        out FontDiagnostic diagnostic)
    {
        refusalStatus = ReadStatus.UnsupportedFormat;
        ReadStatus status = directory.TryGetTableBytes(profile.CffOrdinal, out var cffBytes,
            out diagnostic);
        if (status != ReadStatus.Done)
        {
            refusalStatus = status;
            return false;
        }
        // Read-side failures (N4) propagate their own status and diagnostic untouched.
        status = Cff1View.TryOpen(cffBytes, out var view, out diagnostic,
            directory.TableLocation(profile.CffOrdinal));
        if (status != ReadStatus.Done)
        {
            refusalStatus = status;
            return false;
        }
        status = view.Validate(out diagnostic, state.Budget);
        if (status != ReadStatus.Done)
        {
            refusalStatus = status;
            return false;
        }

        // The GID domain authority stays maxp; the CFF side must agree with it exactly
        // (N3), in the same diagnostic shape as the inspection chain (maxp value in
        // Detail, CFF count in Expected).
        status = view.TryGetCharStringsCount(out int charStrings, out diagnostic);
        if (status != ReadStatus.Done)
        {
            refusalStatus = status;
            return false;
        }
        if (charStrings != maxp.NumGlyphs)
        {
            // N3 is a data-consistency refusal, not an out-of-slice profile: the status
            // lands as InvalidFormat, matching the inspection chain's CFF↔maxp shape.
            state.Diagnostic = new(DiagnosticCode.InvalidValue,
                maxp.Location.At(DataField.NumGlyphs, MaxpLayout.NumGlyphsAt),
                (ulong)maxp.NumGlyphs, (ulong)charStrings);
            diagnostic = state.Diagnostic;
            refusalStatus = ReadStatus.InvalidFormat;
            return false;
        }

        // Staleness is a RETAIN_RAW registration fact: "the raw CFF/VORG bytes still in
        // the output reference the full GID domain" (S1b card §3). Under compact-remap
        // those tables are rebuilt, the fact does not hold, and the UnselectedSlot
        // wording would be false — so the compact path skips both reads and its report
        // carries only the decisions plus the map artifact (S1c card §1.1). The retain
        // path is unchanged byte-for-byte and diagnostic-for-diagnostic.
        if (state.Mode != SubsetMode.CompactRemap)
        {
            status = ReadCffStaleness(view, state, out diagnostic);
            if (status != ReadStatus.Done)
            {
                refusalStatus = status;
                return false;
            }
            if (directory.TryFindUniqueTable(Vorg, out int vorgOrdinal, out _) ==
                ReadStatus.Done)
            {
                status = ReadVorgStaleness(directory, vorgOrdinal, maxp, state,
                    out diagnostic);
                if (status != ReadStatus.Done)
                {
                    refusalStatus = status;
                    return false;
                }
            }
        }
        diagnostic = default;
        return true;
    }

    /// <summary>Flavor, charset/encoding forms and the CID facts, through bounded
    /// accessors only (OQ-S1b-1 lean: no materialization, no charstring interpretation).</summary>
    private static ReadStatus ReadCffStaleness(Cff1View view, PrepareState state,
        out FontDiagnostic diagnostic)
    {
        // The ROS key is the flavor boundary; Validate already checked its arity and
        // bounds, so the second read cannot fail here except by budget.
        ReadStatus status = view.TryGetTopDictIntegers(Cff1Layout.OpRos, out var ros,
            out diagnostic);
        if (status != ReadStatus.Done && status != ReadStatus.NotFound) return status;
        bool isCid = status == ReadStatus.Done;
        status = view.TryGetCharStringsCount(out int charStrings, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = view.TryGetCharsetForm(out var charsetForm, out diagnostic);
        if (status != ReadStatus.Done) return status;

        Cff1EncodingForm? encodingForm = null;
        bool sidColumnReachable = false;
        int[]? rosSids = null;
        int? cidCount = null;
        int? fdSelectFormat = null;
        if (isCid)
        {
            rosSids = ros!;
            status = view.TryGetTopDictIntegers(Cff1Layout.OpCidCount, out var cidOperands,
                out diagnostic);
            if (status == ReadStatus.Done)
                cidCount = cidOperands![0];
            else if (status != ReadStatus.NotFound) return status;
            status = view.TryGetTopDictIntegers(Cff1Layout.OpFdSelect, out var fdSelect,
                out diagnostic);
            if (status != ReadStatus.Done) return status;
            // Validate checked the FDSelect block, so the format byte is in bounds.
            fdSelectFormat = view.RawBytes[fdSelect![0]];
        }
        else
        {
            status = view.TryGetEncodingForm(out var form, out diagnostic);
            if (status != ReadStatus.Done) return status;
            encodingForm = form;
            // Reachability probe over the bounded SID accessor at both domain ends;
            // Validate already expanded the whole column, so this is evidence, not a
            // second validation.
            status = view.TryGetSidForGlyph(0, out _, out diagnostic);
            if (status != ReadStatus.Done) return status;
            status = view.TryGetSidForGlyph(charStrings - 1, out _, out diagnostic);
            if (status != ReadStatus.Done) return status;
            sidColumnReachable = true;
        }
        state.CffStaleness = new(isCid ? "CFF1 CID-keyed" : "CFF1 name-keyed",
            charStrings, charsetForm, encodingForm, sidColumnReachable, rosSids,
            cidCount, fdSelectFormat);
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>VORG facts for the report: the strict record rules were checked by
    /// VorgView.Validate against the maxp-bound context (N-negative variant face: an
    /// out-of-domain or disordered record refuses here with the view's own diagnostic).</summary>
    private static ReadStatus ReadVorgStaleness(SfntDirectoryView directory,
        int vorgOrdinal, MaxpTable maxp, PrepareState state, out FontDiagnostic diagnostic)
    {
        ReadStatus status = directory.TryGetTableBytes(vorgOrdinal, out var vorgBytes,
            out diagnostic);
        if (status != ReadStatus.Done) return status;
        var location = directory.TableLocation(vorgOrdinal);
        status = VorgContext.TryCreate(maxp, location, out var context, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = VorgView.TryOpen(vorgBytes, out var view, out diagnostic, location);
        if (status != ReadStatus.Done) return status;
        status = view.Validate(context!, out diagnostic);
        if (status != ReadStatus.Done) return status;
        state.VorgStaleness = new(view.DefaultVertOriginY, view.NumVertOriginYMetrics);
        diagnostic = default;
        return ReadStatus.Done;
    }
}
