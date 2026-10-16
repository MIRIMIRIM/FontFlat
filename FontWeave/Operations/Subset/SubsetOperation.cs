using FontWeave.Containers;
using FontWeave.Core;
using FontWeave.Tables;
using FontWeave.Tables.Layout;
using FontWeave.Tables.Variation;

namespace FontWeave.Operations.Subset;

/// <summary>Retain-GIDs and compact-remap subsetting of one source face (S1a/SC slices).
///
/// The rebuild face follows both the mode and the admitted profile: retain-GIDs
/// rebuilds exactly {glyf, loca, cmap} on a TrueType face - glyf/loca re-spliced
/// around the retained slots - while a static CFF1 face (S1b) rebuilds exactly
/// {cmap}, reusing the same prune-and-canonicalize pipeline unchanged because its
/// input domain is maxp.numGlyphs, orthogonal to the outline format, with the
/// CFF/VORG bytes RETAIN_RAW and their staleness facts registered in the report
/// (S1b card §2/§3); compact-remap rebuilds the primaryPolicy column's executable
/// set (glyf, loca, cmap, maxp, hhea, hmtx, vhea, vmtx, post@2.0, hdmx, LTSH, VDMX,
/// and the SE color/bitmap/MERG nine: COLR, SVG␠, sbix, CBDT/CBLC, EBDT/EBLC, EBSC,
/// MERG) over one frozen glyph map on a TrueType face and, since the CFF compact
/// slice (S1c card §1), also serves static CFF1 faces: charset/encoding/CharStrings
/// and FDSelect rebuild over the frozen map's ascending stable identity (name-keyed =
/// charset SID, CID-keyed = charset CID) with VORG riding the same remap (SC card §1;
/// SE card §1). Every other present table follows the frozen policy matrix's
/// column for the requested mode; the caller can only steer the four explicit
/// option families. The whole operation is decided before any payload is compiled
/// or copied, and any refusal is finite: result is null, no sfnt bytes exist, and
/// the report keeps the decisions formed so far plus the first failure diagnostic
/// (W1 §4.4/§4.5).</summary>
public static partial class SubsetOperation
{
    private const uint Head = 0x68656164, Maxp = 0x6D617870, Name = 0x6E616D65,
        Dsig = 0x44534947;
    private const uint Glyf = 0x676C7966, Loca = 0x6C6F6361, Cmap = 0x636D6170;
    private const uint Cff = 0x43464620, Cff2 = 0x43464632, Fvar = 0x66766172;
    private const uint Post = 0x706F7374;
    private const uint Cvt = 0x63767420, Fpgm = 0x6670676D, Prep = 0x70726570,
        Ltsh = 0x4C545348;
    private const uint Kern = 0x6B65726E, Gsub = 0x47535542, Gpos = 0x47504F53,
        Gdef = 0x47444546, Base = 0x42415345, Jstf = 0x4A535446, Math = 0x4D415448;
    private const uint Hhea = 0x68686561, Hmtx = 0x686D7478, Vhea = 0x76686561,
        Vmtx = 0x766D7478, Vorg = 0x564F5247, Vdmx = 0x56444D58, Hdmx = 0x68646D78;
    // Compact-side deferral rows (SC card §3 RC2-RC4): the primaryPolicy column defers
    // them to a later slice, so a compact request refuses while retain keeps raw copies.
    private const uint Merg = 0x4D455247, Colr = 0x434F4C52, Svg = 0x53564720,
        Sbix = 0x73626978, Cbdt = 0x43424454, Cblc = 0x43424C43, Ebdt = 0x45424454,
        Eblc = 0x45424C43, Ebsc = 0x45425343, Gvar = 0x67766172, Cvar = 0x63766172,
        Hvar = 0x48564152, Vvar = 0x56564152, Mvar = 0x4D564152, Cpal = 0x4350414C;
    // The remaining variable-family tags (SV card §1): the admission chain walks
    // them against the fvar axis authority.
    private const uint Avar = 0x61766172, Stat = 0x53544154;

    public static ReadStatus TryPrepare(SubsetRequest request, out SubsetResult? result,
        out SubsetReport? report, out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        result = null;
        report = null;
        diagnostic = default;
        // Identity is the face itself; an unbound face is a caller parameter error and
        // never a font diagnostic (W1 §1.1).
        if (!request.Face.IsBound)
            throw new ArgumentException("The subset request face is not bound to a source.",
                nameof(request));
        var options = request.Options ?? new SubsetOptions();
        bool compact = request.Mode == SubsetMode.CompactRemap;
        var state = new PrepareState(request.Mode, new BuildBudget(limits));
        var budget = state.Budget;
        DataLocation faceAt = request.Face.Location;

        // Step 1: request validity — root shape, mode, option guards (card §2 step 1).
        if (!TryValidateRequest(request, options, state, out diagnostic))
            return Refuse(state, ReadStatus.UnsupportedFormat, diagnostic, out result,
                out report, out diagnostic);

        // Step 2: directory admission, the same accounting shape as the other operations
        // (MetadataEditOperation.cs:55-61).
        ReadStatus status = request.Face.TryOpenDirectory(out var directory, out diagnostic);
        if (status != ReadStatus.Done)
            return Refuse(state, status, diagnostic, out result, out report, out diagnostic);
        if (!budget.Input(directory.SourceByteLength, faceAt, out diagnostic) ||
            !budget.Work(32L * directory.DeclaredCount, faceAt, out diagnostic))
            return Refuse(state, ReadStatus.ResourceLimit, diagnostic, out result,
                out report, out diagnostic);
        status = directory.Validate(out diagnostic);
        if (status != ReadStatus.Done)
            return Refuse(state, status, diagnostic, out result, out report, out diagnostic);

        // head supplies the revision anchor and the loca format; maxp is the GID domain
        // authority (card §1.2). Both are read before any policy decision needs them.
        status = directory.TryFindUniqueTable(Head, out int headOrdinal, out diagnostic);
        if (status != ReadStatus.Done)
            return Refuse(state, status, diagnostic, out result, out report, out diagnostic);
        status = directory.TryFindUniqueTable(Maxp, out int maxpOrdinal, out diagnostic);
        if (status != ReadStatus.Done)
            return Refuse(state, status, diagnostic, out result, out report, out diagnostic);
        status = directory.TryGetTableBytes(headOrdinal, out var headBytes, out diagnostic);
        if (status != ReadStatus.Done)
            return Refuse(state, status, diagnostic, out result, out report, out diagnostic);
        status = HeadView.TryOpen(headBytes, out var headView, out diagnostic,
            directory.TableLocation(headOrdinal));
        if (status != ReadStatus.Done)
            return Refuse(state, status, diagnostic, out result, out report, out diagnostic);
        status = headView.TryRead(out HeadTable? head, out diagnostic, budget);
        if (status != ReadStatus.Done)
            return Refuse(state, status, diagnostic, out result, out report, out diagnostic);
        status = directory.TryGetTableBytes(maxpOrdinal, out var maxpBytes, out diagnostic);
        if (status != ReadStatus.Done)
            return Refuse(state, status, diagnostic, out result, out report, out diagnostic);
        status = MaxpView.TryOpen(maxpBytes, out var maxpView, out diagnostic,
            directory.TableLocation(maxpOrdinal));
        if (status != ReadStatus.Done)
            return Refuse(state, status, diagnostic, out result, out report, out diagnostic);
        status = maxpView.TryRead(out MaxpTable? maxp, out diagnostic, budget);
        if (status != ReadStatus.Done)
            return Refuse(state, status, diagnostic, out result, out report, out diagnostic);
        int numGlyphs = maxp!.NumGlyphs;

        // Root GIDs are caller data validated against the maxp domain: out-of-domain,
        // duplicate and disordered values refuse rather than normalize (W1 §1.2).
        if (numGlyphs < 1)
        {
            diagnostic = new(DiagnosticCode.InvalidValue,
                maxp!.Location.At(DataField.NumGlyphs, 4), (ulong)numGlyphs, 1UL);
            return Refuse(state, ReadStatus.InvalidFormat, diagnostic, out result,
                out report, out diagnostic);
        }
        if (!TryValidateRootGids(request, numGlyphs, maxp!.Location, state, out diagnostic))
            return Refuse(state, ReadStatus.InvalidFormat, diagnostic, out result,
                out report, out diagnostic);

        // Step 3: face profile gate (card §1.2). One pass collects presence and the
        // first location per category; the gate then refuses in a fixed order.
        var profile = ScanProfile(directory);
        if (!TryAdmitProfile(directory, profile, maxp!, state, out var refusalStatus,
                out diagnostic))
            return Refuse(state, refusalStatus, diagnostic, out result,
                out report, out diagnostic);
        bool trueType = profile.HasGlyf && profile.HasLoca;
        if (profile.HasCmap &&
            !TryAdmitCmapFormats(directory, profile.CmapOrdinal, state, out diagnostic))
            return Refuse(state, ReadStatus.UnsupportedFormat, diagnostic, out result,
                out report, out diagnostic);

        // Step 3.5 (S2a card §2.1; dual-mode since S2b-1, OQ-S2b-2): the PreserveLayout
        // admission segment. Every present layout table in the admitted prefix walks its
        // whole-table typed read chain in the fixed dependency order and parks its one
        // model on the state. A read-chain failure propagates its own diagnostic (RF-A);
        // a metadata-only lookup refuses (RF-B). The prefix is mode-dependent: retain
        // admits the whole family (S2a), compact admits only GDEF→GSUB→GPOS — the
        // S2b-1 rewrite prefix — because an unrewritten table's first diagnostic must
        // stay the decide-stage code-23 refusal, not a read-chain diagnostic (the
        // admit-before-decide ordering makes the prefix the drift guard, S2b card §1).
        bool preserveLayout = options.PreserveLayout;
        if (preserveLayout)
        {
            status = AdmitLayoutFamily(directory, profile, maxp!, state,
                layoutRewritePrefixOnly: compact, out diagnostic);
            if (status != ReadStatus.Done)
                return Refuse(state, status, diagnostic, out result, out report,
                    out diagnostic);
        }

        // Step 4: per-record policy decision. The array is reserved up front and the
        // whole loop completes before any payload work (decide-whole-before-compile,
        // MetadataEditOperation.cs:67-94).
        if (!budget.Buffer(8L * directory.DeclaredCount, faceAt, out diagnostic) ||
            !budget.Buffer(numGlyphs, faceAt, out diagnostic))
            return Refuse(state, ReadStatus.ResourceLimit, diagnostic, out result,
                out report, out diagnostic);
        state.Retained = new bool[numGlyphs];
        state.Retained[0] = true; // .notdef is an implicit member (W1 §1.3).
        foreach (ushort gid in request.Root!.Gids!) state.Retained[gid] = true;
        for (int i = 0; i < directory.DeclaredCount; i++)
        {
            status = directory.TryGetRecord(i, out var record, out diagnostic);
            if (status != ReadStatus.Done)
                return Refuse(state, status, diagnostic, out result, out report,
                    out diagnostic);
            if (!TryDecideTable(directory, i, record.Tag, options, state,
                    profile.HasCff || profile.HasCff2, out diagnostic))
                return Refuse(state, ReadStatus.UnsupportedFormat, diagnostic, out result,
                    out report, out diagnostic);
        }

        // Step 4.5 (S2a card §2.2): the layout reference union merges into the
        // retained set before the composite closure, so the existing CloseComposites
        // worklist consumes the extended set unchanged and the two addition lists
        // stay disjoint.
        if (preserveLayout)
        {
            status = CloseLayoutReferences(state, out diagnostic);
            if (status != ReadStatus.Done)
                return Refuse(state, status, diagnostic, out result, out report,
                    out diagnostic);
        }

        // Step 5: composite dependency closure over the TrueType profile (card §3). The
        // typed read chain runs first, so graph violations surface with the reader's own
        // diagnostics; the zero-rebuild profile has no composites and skips this stage.
        LocaTable? loca = null;
        GlyfTable? glyfModel = null;
        List<ushort> closureAdded = [];
        if (trueType)
        {
            status = CloseComposites(directory, profile, head!, maxp!, budget,
                state.Retained, out loca, out glyfModel, out closureAdded, out diagnostic);
            if (status != ReadStatus.Done)
                return Refuse(state, status, diagnostic, out result, out report,
                    out diagnostic);
            state.ClosureAdded = closureAdded;
        }

        // Step 5.5 (compact only, SC card §1.1): freeze the one GlyphRemap over the
        // final retained set and serialize the map artifact before any payload stage.
        // On a static CFF1 face the output order is the ascending stable identity of
        // the charset (SID/CID), so the freeze materializes the CFF table once and
        // parks the model on the state for the payload rebuilds (S1c card §1.2).
        GlyphRemap? map = null;
        if (compact)
        {
            status = FreezeCompactMap(request.Face, head!, state.Retained, directory,
                profile.CffOrdinal, profile.HasCff2, state, budget, out map,
                out diagnostic);
            if (status != ReadStatus.Done)
                return Refuse(state, status, diagnostic, out result, out report,
                    out diagnostic);
        }

        // Step 6: derived counts over the final retained set.
        if (!budget.Work(numGlyphs, faceAt, out diagnostic))
            return Refuse(state, ReadStatus.ResourceLimit, diagnostic, out result,
                out report, out diagnostic);
        int retainedCount = 0;
        for (int gid = 0; gid < state.Retained.Length; gid++)
            if (state.Retained[gid]) retainedCount++;
        state.UnselectedSlotCount = numGlyphs - retainedCount;

        // Step 7: maxp/head invariants. numGlyphs is invariant by definition (maxp is
        // preserved raw); loca must already match the length its format implies, because
        // the rebuild reuses that format (FoundationTableEditOperation locaVerdict form).
        if (trueType)
        {
            status = directory.TryGetRecord(profile.LocaOrdinal, out var locaRecord,
                out diagnostic);
            if (status != ReadStatus.Done)
                return Refuse(state, status, diagnostic, out result, out report,
                    out diagnostic);
            uint expectedLocaLength = (uint)(numGlyphs + 1) *
                (head!.Values.IndexToLocFormat == 0 ? 2u : 4u);
            if (locaRecord.Length != expectedLocaLength)
            {
                diagnostic = new(DiagnosticCode.InvalidValue,
                    directory.TableLocation(profile.LocaOrdinal).At(DataField.Length, 0),
                    locaRecord.Length, expectedLocaLength);
                return Refuse(state, ReadStatus.InvalidFormat, diagnostic, out result,
                    out report, out diagnostic);
            }
        }

        // Step 8/9: payload. Rebuild stages produce their bytes before the copy loop, so
        // a rebuild failure still refuses with nothing published.
        byte[]? rebuiltGlyf = null, rebuiltLoca = null, rebuiltCmap = null;
        byte[]? rebuiltMaxp = null, rebuiltHhea = null, rebuiltHmtx = null,
            rebuiltVhea = null, rebuiltVmtx = null;
        byte[]? rebuiltPost = null, rebuiltHdmx = null, rebuiltLtsh = null,
            rebuiltVdmx = null;
        byte[]? rebuiltCff = null, rebuiltVorg = null;
        byte[]? rebuiltGvar = null, rebuiltHvar = null, rebuiltVvar = null,
            rebuiltCff2 = null;
        byte[]? rebuiltColr = null, rebuiltSvg = null, rebuiltSbix = null,
            rebuiltCbdt = null, rebuiltCblc = null, rebuiltEbdt = null,
            rebuiltEblc = null, rebuiltEbsc = null, rebuiltMerg = null;
        byte[]? rebuiltGdefLayout = null, rebuiltGsubLayout = null,
            rebuiltGposLayout = null;
        var rebuildTags = new HashSet<uint>(
            state.Decisions.Where(d => d.Action == TableAction.Rebuild).Select(d => d.Tag));
        if (trueType)
        {
            if (compact)
            {
                status = RebuildCompactGlyfAndLoca(glyfModel!, loca!, map!,
                    options.DropHinting, budget, out rebuiltGlyf, out rebuiltLoca,
                    out diagnostic);
                if (status != ReadStatus.Done)
                    return Refuse(state, status, diagnostic, out result, out report,
                        out diagnostic);
            }
            else
            {
                status = RebuildGlyfAndLoca(state.Retained, loca!, glyfModel!,
                    options.DropHinting, budget, out rebuiltGlyf, out rebuiltLoca,
                    out diagnostic);
                if (status != ReadStatus.Done)
                    return Refuse(state, status, diagnostic, out result, out report,
                        out diagnostic);
            }
        }
        if (profile.HasCmap)
        {
            status = RebuildCmap(directory, profile.CmapOrdinal, maxp!, state.Retained,
                budget, out rebuiltCmap, out var consumedNotEmitted, out diagnostic,
                compact ? map : null);
            if (status != ReadStatus.Done)
                return Refuse(state, status, diagnostic, out result, out report,
                    out diagnostic);
            state.CmapConsumedNotEmitted = consumedNotEmitted;
        }
        if (compact)
        {
            // Commit ③/④ rebuild face (SC card §2): the decide list drives which
            // producers must exist, so a rebuild decision without a producer below
            // still refuses at the switch instead of passing silently.
            status = RebuildMaxp(maxp!, map!.OutputGlyphCount, budget, out rebuiltMaxp,
                out diagnostic);
            if (status != ReadStatus.Done)
                return Refuse(state, status, diagnostic, out result, out report,
                    out diagnostic);
            if (rebuildTags.Contains(Hhea))
            {
                status = RebuildHorizontalMetrics(directory, maxp!, map!, budget,
                    out rebuiltHhea, out rebuiltHmtx, out diagnostic);
                if (status != ReadStatus.Done)
                    return Refuse(state, status, diagnostic, out result, out report,
                        out diagnostic);
            }
            if (rebuildTags.Contains(Vhea))
            {
                status = RebuildVerticalMetrics(directory, maxp!, map!, budget,
                    out rebuiltVhea, out rebuiltVmtx, out diagnostic);
                if (status != ReadStatus.Done)
                    return Refuse(state, status, diagnostic, out result, out report,
                        out diagnostic);
            }
            if (rebuildTags.Contains(Post))
            {
                status = RebuildPost20(directory, maxp!, map!, budget, out rebuiltPost,
                    out diagnostic);
                if (status != ReadStatus.Done)
                    return Refuse(state, status, diagnostic, out result, out report,
                        out diagnostic);
            }
            if (rebuildTags.Contains(Hdmx))
            {
                status = RebuildHdmx(directory, maxp!, map!, budget, out rebuiltHdmx,
                    out diagnostic);
                if (status != ReadStatus.Done)
                    return Refuse(state, status, diagnostic, out result, out report,
                        out diagnostic);
            }
            if (rebuildTags.Contains(Ltsh))
            {
                status = RebuildLtsh(directory, maxp!, map!, budget, out rebuiltLtsh,
                    out diagnostic);
                if (status != ReadStatus.Done)
                    return Refuse(state, status, diagnostic, out result, out report,
                        out diagnostic);
            }
            if (rebuildTags.Contains(Vdmx))
            {
                status = RebuildVdmx(directory, budget, out rebuiltVdmx, out diagnostic);
                if (status != ReadStatus.Done)
                    return Refuse(state, status, diagnostic, out result, out report,
                        out diagnostic);
            }
            if (rebuildTags.Contains(Cff))
            {
                // The map freeze parked the one materialized CFF1 model; the payload
                // consumes it without a second read (S1c card §1.2). The flavor picks
                // the producer: name-keyed through Cff1TableCompiler, CID-keyed through
                // the derived-model entry of CidCff1TableCompiler (S1c card §2.5).
                status = state.CffCidKeyed is not null
                    ? RebuildCidCff1(state.CffCidKeyed, map!, budget, out rebuiltCff,
                        out diagnostic)
                    : RebuildCff1NameKeyed(state.CffNameKeyed!, map!, budget,
                        out rebuiltCff, out diagnostic);
                if (status != ReadStatus.Done)
                    return Refuse(state, status, diagnostic, out result, out report,
                        out diagnostic);
            }
            if (rebuildTags.Contains(Vorg))
            {
                // VORG rides the same remap as the CFF rebuild (S1c card §3): surviving
                // records re-map to their output GIDs, dropped glyphs' records go.
                status = RebuildVorg(directory, maxp!, map!, budget, out rebuiltVorg,
                    out diagnostic);
                if (status != ReadStatus.Done)
                    return Refuse(state, status, diagnostic, out result, out report,
                        out diagnostic);
            }
            if (rebuildTags.Contains(Gvar))
            {
                // SV (card §2.1): the per-glyph tuple blocks re-slot over the
                // frozen map byte-for-byte; the admission chain's parked model
                // feeds the rebuild without a second read.
                if (state.Gvar is not { } gvarModel)
                {
                    diagnostic = new(DiagnosticCode.UnsupportedOperation,
                        faceAt with { TableTag = Gvar }, Gvar);
                    return Refuse(state, ReadStatus.UnsupportedFormat, diagnostic,
                        out result, out report, out diagnostic);
                }
                status = RebuildGvar(gvarModel, map!, budget, out rebuiltGvar,
                    out diagnostic);
                if (status != ReadStatus.Done)
                    return Refuse(state, status, diagnostic, out result,
                        out report, out diagnostic);
            }
            if (rebuildTags.Contains(Hvar))
            {
                // SV (card §2.3): the store re-emits byte-identical; the advance
                // map re-roots over the frozen map (explicit entries resolve in
                // the input domain, a NULL advance map materializes, OQ-SV-6).
                if (state.Hvar is not { } hvarModel)
                {
                    diagnostic = new(DiagnosticCode.UnsupportedOperation,
                        faceAt with { TableTag = Hvar }, Hvar);
                    return Refuse(state, ReadStatus.UnsupportedFormat, diagnostic,
                        out result, out report, out diagnostic);
                }
                status = RebuildHvar(hvarModel, map!, budget, out rebuiltHvar,
                    out diagnostic);
                if (status != ReadStatus.Done)
                    return Refuse(state, status, diagnostic, out result,
                        out report, out diagnostic);
            }
            if (rebuildTags.Contains(Vvar))
            {
                // SV (card §2.3): the VVAR twin — advanceHeight rides the HVAR
                // map semantics, tsb/bsb/vOrg keep their NULL no-variation slots
                // (OQ-T5-04-4; materializing them would invent data).
                if (state.Vvar is not { } vvarModel)
                {
                    diagnostic = new(DiagnosticCode.UnsupportedOperation,
                        faceAt with { TableTag = Vvar }, Vvar);
                    return Refuse(state, ReadStatus.UnsupportedFormat, diagnostic,
                        out result, out report, out diagnostic);
                }
                status = RebuildVvar(vvarModel, map!, budget, out rebuiltVvar,
                    out diagnostic);
                if (status != ReadStatus.Done)
                    return Refuse(state, status, diagnostic, out result,
                        out report, out diagnostic);
            }
            if (rebuildTags.Contains(Cff2))
            {
                // SV (card §2.5): the CharStrings INDEX items and the per-glyph
                // FD index re-slot over the frozen map as reference moves; the
                // admission chain's parked model feeds the rebuild without a
                // second read.
                if (state.Cff2 is not { } cff2Model)
                {
                    diagnostic = new(DiagnosticCode.UnsupportedOperation,
                        faceAt with { TableTag = Cff2 }, Cff2);
                    return Refuse(state, ReadStatus.UnsupportedFormat, diagnostic,
                        out result, out report, out diagnostic);
                }
                status = RebuildCff2(cff2Model, map!, budget, out rebuiltCff2,
                    out diagnostic);
                if (status != ReadStatus.Done)
                    return Refuse(state, status, diagnostic, out result,
                        out report, out diagnostic);
            }
            if (rebuildTags.Contains(Colr))
            {
                // SE (card §2.1): the COLR rebuild prunes and remaps on the typed
                // model; CPAL stays raw and supplies the palette-entry authority.
                status = RebuildColr(directory, maxp!, map!, budget, out rebuiltColr,
                    out bool colrDropped, out diagnostic);
                if (status != ReadStatus.Done)
                    return Refuse(state, status, diagnostic, out result, out report,
                        out diagnostic);
                if (colrDropped) state.PayloadDropped.Add(Colr);
            }
            HashSet<int> survivingStrikes = [];
            if (rebuildTags.Contains(Cbdt))
            {
                // SE (card §2.4): the bitmap pool+index pair rebuilds jointly over
                // the frozen map (the RE1 pairing gate already ran at admission).
                status = RebuildCbdtCblc(directory, maxp!, map!, budget,
                    out rebuiltCbdt, out rebuiltCblc, out bool cbdtDropped,
                    out diagnostic);
                if (status != ReadStatus.Done)
                    return Refuse(state, status, diagnostic, out result, out report,
                        out diagnostic);
                if (cbdtDropped)
                {
                    state.PayloadDropped.Add(Cbdt);
                    state.PayloadDropped.Add(Cblc);
                }
            }
            if (rebuildTags.Contains(Ebdt))
            {
                // SE (card §2.5): the EBDT/EBLC pair mirrors the CBDT/CBLC walk;
                // the surviving strike ppem pairs feed the EBSC record filter.
                status = RebuildEbdtEblc(directory, maxp!, map!, budget,
                    out rebuiltEbdt, out rebuiltEblc, out survivingStrikes,
                    out bool ebdtDropped, out diagnostic);
                if (status != ReadStatus.Done)
                    return Refuse(state, status, diagnostic, out result, out report,
                        out diagnostic);
                if (ebdtDropped)
                {
                    state.PayloadDropped.Add(Ebdt);
                    state.PayloadDropped.Add(Eblc);
                }
            }
            if (rebuildTags.Contains(Ebsc))
            {
                // SE (card §2.5): EBSC rides the rebuilt strike set; a missing EBLC
                // partner propagates the paired read chain's own refusal (RE2).
                status = RebuildEbsc(directory, maxp!, survivingStrikes, budget,
                    out rebuiltEbsc, out bool ebscDropped, out diagnostic);
                if (status != ReadStatus.Done)
                    return Refuse(state, status, diagnostic, out result, out report,
                        out diagnostic);
                if (ebscDropped) state.PayloadDropped.Add(Ebsc);
            }
            if (rebuildTags.Contains(Sbix))
            {
                // SE (card §2.3): strike slots re-slot over the output domain with
                // the dupe targets remapped (RE3 on a dangling one).
                status = RebuildSbix(directory, maxp!, map!, budget, out rebuiltSbix,
                    out bool sbixDropped, out diagnostic);
                if (status != ReadStatus.Done)
                    return Refuse(state, status, diagnostic, out result, out report,
                        out diagnostic);
                if (sbixDropped) state.PayloadDropped.Add(Sbix);
            }
            if (rebuildTags.Contains(Svg))
            {
                // SE (card §2.2): document records re-span over the surviving
                // spans; unreferenced documents drop, bytes stay verbatim.
                status = RebuildSvg(directory, map!, budget, out rebuiltSvg,
                    out bool svgDropped, out diagnostic);
                if (status != ReadStatus.Done)
                    return Refuse(state, status, diagnostic, out result, out report,
                        out diagnostic);
                if (svgDropped) state.PayloadDropped.Add(Svg);
            }
            if (rebuildTags.Contains(Merg))
            {
                // SE (card §2.6): every distinct ClassDef body re-answers over the
                // output domain in its own format; the class-keyed matrix travels
                // byte-verbatim (OQ-SE-4) and MERG never drops (its zero form is a
                // legal emission).
                status = RebuildMerg(directory, maxp!, map!, budget,
                    out rebuiltMerg, out diagnostic);
                if (status != ReadStatus.Done)
                    return Refuse(state, status, diagnostic, out result, out report,
                        out diagnostic);
            }
            if (rebuildTags.Contains(Gdef) || rebuildTags.Contains(Gsub) ||
                rebuildTags.Contains(Gpos))
            {
                // S2b-1 (card §2): the compact layout rewrite runs GDEF first — the
                // GSUB/GPOS lookup flags validate against its rebuilt lookup context —
                // then the two layout lists. Every stage reads the step 3.5 materialized
                // models, remaps through the frozen map, and re-emits through the
                // existing compilers; a remap miss or a count break refuses with no
                // bytes published (RF-S2b-2).
                status = RewriteCompactLayout(state, map!, rebuildTags,
                    out rebuiltGdefLayout, out rebuiltGsubLayout, out rebuiltGposLayout,
                    out diagnostic);
                if (status != ReadStatus.Done)
                    return Refuse(state, status, diagnostic, out result, out report,
                        out diagnostic);
            }
        }
        var tables = new SortedDictionary<uint, byte[]>();
        for (int i = 0; i < directory.DeclaredCount; i++)
        {
            var decision = state.Decisions[i];
            if (decision.Action == TableAction.Drop) continue;
            if (decision.Action == TableAction.Rebuild)
            {
                byte[]? rebuilt = decision.Tag switch
                {
                    Glyf => rebuiltGlyf,
                    Loca => rebuiltLoca,
                    Cmap => rebuiltCmap,
                    Maxp => rebuiltMaxp,
                    Hhea => rebuiltHhea,
                    Hmtx => rebuiltHmtx,
                    Vhea => rebuiltVhea,
                    Vmtx => rebuiltVmtx,
                    Post => rebuiltPost,
                    Hdmx => rebuiltHdmx,
                    Ltsh => rebuiltLtsh,
                    Vdmx => rebuiltVdmx,
                    Colr => rebuiltColr,
                    Svg => rebuiltSvg,
                    Sbix => rebuiltSbix,
                    Cbdt => rebuiltCbdt,
                    Cblc => rebuiltCblc,
                    Ebdt => rebuiltEbdt,
                    Eblc => rebuiltEblc,
                    Ebsc => rebuiltEbsc,
                    Merg => rebuiltMerg,
                    Cff => rebuiltCff,
                    Vorg => rebuiltVorg,
                    Gvar => rebuiltGvar,
                    Hvar => rebuiltHvar,
                    Vvar => rebuiltVvar,
                    Cff2 => rebuiltCff2,
                    Gdef => rebuiltGdefLayout,
                    Gsub => rebuiltGsubLayout,
                    Gpos => rebuiltGposLayout,
                    _ => null
                };
                if (rebuilt is null && state.PayloadDropped.Contains(decision.Tag))
                {
                    // SE (OQ-SE-5): the rebuild produced an empty body — every slot/row
                    // pruned — so the table drops with the payload-time decision revision
                    // feeding DroppedTables and the report's decision set (a success path
                    // refinement, formed before any output byte exists).
                    state.ReviseDecision(decision.Tag, TableAction.Drop, SubsetPolicy.Delete);
                    continue;
                }
                if (rebuilt is null)
                {
                    diagnostic = new(DiagnosticCode.UnsupportedOperation, faceAt,
                        decision.Tag);
                    return Refuse(state, ReadStatus.UnsupportedFormat, diagnostic,
                        out result, out report, out diagnostic);
                }
                tables.Add(decision.Tag, rebuilt);
                continue;
            }
            status = directory.TryGetTableBytes(i, out var bytes, out diagnostic);
            if (status != ReadStatus.Done)
                return Refuse(state, status, diagnostic, out result, out report,
                    out diagnostic);
            var location = directory.TableLocation(i);
            if (!budget.Buffer(bytes.Length, location, out diagnostic) ||
                !budget.Work(bytes.Length, location, out diagnostic))
                return Refuse(state, ReadStatus.ResourceLimit, diagnostic, out result,
                    out report, out diagnostic);
            tables.Add(decision.Tag, bytes.ToArray());
            budget.RecordTableCopy(bytes.Length);
        }
        status = SfntEmitter.TryEmit(directory.Version, faceAt, tables, out byte[]? output,
            out diagnostic, budget);
        if (status != ReadStatus.Done)
            return Refuse(state, status, diagnostic, out result, out report, out diagnostic);
        report = state.BuildReport();
        result = new SubsetResult(new CompiledFont(output!, budget.Usage,
            PlainDecisions(state.Decisions)), state.FrozenMapArtifact, report);
        diagnostic = default;
        return ReadStatus.Done;
    }

    private static TableDecision[] PlainDecisions(List<SubsetTableDecision> decisions)
    {
        var plain = new TableDecision[decisions.Count];
        for (int i = 0; i < decisions.Count; i++)
            plain[i] = new TableDecision(decisions[i].Tag, decisions[i].Action);
        return plain;
    }

    // ---- request validity (W1 §1; card §2 step 1) ----

    private static bool TryValidateRequest(SubsetRequest request, SubsetOptions options,
        PrepareState state, out FontDiagnostic diagnostic)
    {
        diagnostic = default;
        var root = request.Root;
        if (root is null)
        {
            // Neither root form is present; there is nothing to resolve (W1 §1.2).
            diagnostic = new(DiagnosticCode.InvalidValue, request.Face.Location);
            return false;
        }
        if (!Enum.IsDefined(root.Kind))
            throw new ArgumentOutOfRangeException(nameof(request), "Unknown root kind.");
        if (root.Kind == SubsetRootKind.ExplicitGlyphSet)
        {
            if (root.Gids is null || root.Codepoints is not null)
            {
                diagnostic = new(DiagnosticCode.InvalidValue, request.Face.Location);
                return false;
            }
        }
        else
        {
            if (root.Codepoints is null || root.Gids is not null)
            {
                diagnostic = new(DiagnosticCode.InvalidValue, request.Face.Location);
                return false;
            }
            // The Unicode root is contract text, not an executed path in this slice; it
            // refuses as not-yet-implemented instead of pretending it resolved (card §1.1).
            diagnostic = new(DiagnosticCode.UnsupportedOperation, request.Face.Location);
            return false;
        }
        if (request.Mode is not { } mode)
        {
            // Undeclared mode: explicit either/or, no default (S0-RF-006-MODE).
            diagnostic = new(DiagnosticCode.SubsetModeRequired, request.Face.Location);
            return false;
        }
        if (!Enum.IsDefined(mode))
        {
            // Out-of-domain values refuse as before (S0-RF-006-MODE). CompactRemap itself
            // now executes in this operation: the R6 finite refusal of the retain-only
            // slice was replaced by execution (SC card §3, S1A-RF-COMPACT-MODE migration).
            diagnostic = new(DiagnosticCode.SubsetModeRequired, request.Face.Location,
                (ushort)mode);
            return false;
        }
        if (options.ExcludeNotdef)
        {
            // An explicit .notdef exclusion expression refuses outright (S0-RF-005-NOTDEF).
            diagnostic = new(DiagnosticCode.NotdefExcluded, request.Face.Location);
            return false;
        }
        if (options.NoLayout && options.PreserveLayout)
        {
            // S2a RF-D (card §1.1): the two layout options are mutually exclusive —
            // "drop the layout family" and "retain the layout family" have no common
            // meaning. Same guard shape as the ExcludeNotdef expression above; the
            // code stays 23 (the zero-new-codes discipline, OQ-S2a-5) and the
            // registered keyword contract carries both option names.
            diagnostic = new(DiagnosticCode.LayoutFamilyUnsupported, request.Face.Location);
            return false;
        }
        if (options.UnknownTables is { } declared)
        {
            foreach ((uint tag, _) in declared)
            {
                if (!Enum.IsDefined(declared[tag]))
                    throw new ArgumentOutOfRangeException(nameof(request),
                        "Unknown-table choice is not a defined value.");
                // Known-table policy is matrix-owned and never a caller option (W1 §2.1).
                if (IsKnownSubsetTable(tag))
                {
                    diagnostic = new(DiagnosticCode.InvalidValue,
                        request.Face.Location with { TableTag = tag }, tag);
                    return false;
                }
            }
        }

        // "Selection into the report" (W1 §2.5): every effective option value is recorded.
        state.OptionRecords.Add(new(SubsetOptionId.Mode, 0, mode.ToString()));
        state.OptionRecords.Add(new(SubsetOptionId.NoLayout, 0, options.NoLayout.ToString()));
        state.OptionRecords.Add(new(SubsetOptionId.PreserveLayout, 0,
            options.PreserveLayout.ToString()));
        state.OptionRecords.Add(new(SubsetOptionId.DropHinting, 0,
            options.DropHinting.ToString()));
        state.OptionRecords.Add(new(SubsetOptionId.DropDigitalSignature, 0,
            options.DropDigitalSignature.ToString()));
        state.OptionRecords.Add(new(SubsetOptionId.ExcludeNotdef, 0,
            options.ExcludeNotdef.ToString()));
        if (options.UnknownTables is { } entries)
            foreach ((uint tag, var choice) in entries)
                state.OptionRecords.Add(new(SubsetOptionId.UnknownTable, tag,
                    choice.ToString()));
        return true;
    }

    private static bool TryValidateRootGids(SubsetRequest request, int numGlyphs,
        DataLocation maxpAt, PrepareState state, out FontDiagnostic diagnostic)
    {
        diagnostic = default;
        var gids = request.Root!.Gids!;
        int previous = -1;
        for (int i = 0; i < gids.Count; i++)
        {
            ushort gid = gids[i];
            if (gid >= numGlyphs)
            {
                diagnostic = new(DiagnosticCode.IndexOutOfRange,
                    maxpAt.At(DataField.GlyphId, 4), gid, (ulong)numGlyphs);
                return false;
            }
            if (gid == previous)
            {
                diagnostic = new(DiagnosticCode.DuplicateGlyph,
                    maxpAt.At(DataField.GlyphId, 4), gid);
                return false;
            }
            if (previous >= 0 && gid < previous)
            {
                diagnostic = new(DiagnosticCode.InvalidValue,
                    maxpAt.At(DataField.GlyphId, 4), gid, (ulong)previous);
                return false;
            }
            previous = gid;
        }
        return true;
    }

    // ---- face profile (card §1.2) ----

        private sealed class ProfileScan
        {
            internal bool HasGlyf, HasLoca, HasCff, HasCff2, HasFvar, HasCmap;
            internal bool HasHhea, HasHmtx, HasVhea, HasVmtx;
            internal bool HasCbdt, HasCblc, HasEbdt, HasEblc;
            // The layout-family presence set (S2a card §2.1): the PreserveLayout
            // admission segment walks them in the fixed dependency order.
            internal bool HasGdef, HasGsub, HasGpos, HasBase, HasJstf, HasMath, HasKern;
            // The variable-family presence set (SV card §1): the admission chain walks
            // every present row against the fvar axis authority.
            internal bool HasAvar, HasStat, HasGvar, HasCvar, HasHvar, HasVvar, HasMvar,
                HasCvt;
            internal int GlyfOrdinal = -1, LocaOrdinal = -1, CffOrdinal = -1, CmapOrdinal = -1;
            internal int FvarOrdinal = -1, Cff2Ordinal = -1, AvarOrdinal = -1,
                StatOrdinal = -1, GvarOrdinal = -1, CvarOrdinal = -1, HvarOrdinal = -1,
                VvarOrdinal = -1, MvarOrdinal = -1, CvtOrdinal = -1;
            internal int GdefOrdinal = -1, GsubOrdinal = -1, GposOrdinal = -1,
                BaseOrdinal = -1, JstfOrdinal = -1, MathOrdinal = -1, KernOrdinal = -1;
            internal DataLocation GlyfLocation, LocaLocation, CffLocation, Cff2Location,
                FvarLocation, HheaLocation, HmtxLocation, VheaLocation, VmtxLocation,
                CbdtLocation, CblcLocation, EbdtLocation, EblcLocation;
            internal DataLocation AvarLocation, StatLocation, GvarLocation, CvarLocation,
                HvarLocation, VvarLocation, MvarLocation, CvtLocation;
            internal DataLocation GdefLocation, GsubLocation, GposLocation, BaseLocation,
                JstfLocation, MathLocation, KernLocation;
        }

    private static ProfileScan ScanProfile(SfntDirectoryView directory)
    {
        var scan = new ProfileScan();
        for (int i = 0; i < directory.DeclaredCount; i++)
        {
            if (directory.TryGetRecord(i, out var record, out _) != ReadStatus.Done) continue;
            switch (record.Tag)
            {
                case Glyf when !scan.HasGlyf:
                    scan.HasGlyf = true; scan.GlyfOrdinal = i;
                    scan.GlyfLocation = directory.TableLocation(i);
                    break;
                case Loca when !scan.HasLoca:
                    scan.HasLoca = true; scan.LocaOrdinal = i;
                    scan.LocaLocation = directory.TableLocation(i);
                    break;
                case Cff when !scan.HasCff:
                    scan.HasCff = true; scan.CffOrdinal = i;
                    scan.CffLocation = directory.TableLocation(i);
                    break;
                case Cff2 when !scan.HasCff2:
                    // The two outline tags locate separately: a face may carry both, and
                    // the refusal must point at the tag its profile rule is about (card
                    // §9 note 3 — the shared field was correct only while both refused).
                    scan.HasCff2 = true; scan.Cff2Ordinal = i;
                    scan.Cff2Location = directory.TableLocation(i);
                    break;
                case Fvar when !scan.HasFvar:
                    scan.HasFvar = true; scan.FvarOrdinal = i;
                    scan.FvarLocation = directory.TableLocation(i);
                    break;
                case Cvt when !scan.HasCvt:
                    scan.HasCvt = true; scan.CvtOrdinal = i;
                    scan.CvtLocation = directory.TableLocation(i);
                    break;
                case Avar when !scan.HasAvar:
                    scan.HasAvar = true; scan.AvarOrdinal = i;
                    scan.AvarLocation = directory.TableLocation(i);
                    break;
                case Stat when !scan.HasStat:
                    scan.HasStat = true; scan.StatOrdinal = i;
                    scan.StatLocation = directory.TableLocation(i);
                    break;
                case Gvar when !scan.HasGvar:
                    scan.HasGvar = true; scan.GvarOrdinal = i;
                    scan.GvarLocation = directory.TableLocation(i);
                    break;
                case Cvar when !scan.HasCvar:
                    scan.HasCvar = true; scan.CvarOrdinal = i;
                    scan.CvarLocation = directory.TableLocation(i);
                    break;
                case Hvar when !scan.HasHvar:
                    scan.HasHvar = true; scan.HvarOrdinal = i;
                    scan.HvarLocation = directory.TableLocation(i);
                    break;
                case Vvar when !scan.HasVvar:
                    scan.HasVvar = true; scan.VvarOrdinal = i;
                    scan.VvarLocation = directory.TableLocation(i);
                    break;
                case Mvar when !scan.HasMvar:
                    scan.HasMvar = true; scan.MvarOrdinal = i;
                    scan.MvarLocation = directory.TableLocation(i);
                    break;
                case Cmap when !scan.HasCmap:
                    scan.HasCmap = true; scan.CmapOrdinal = i;
                    break;
                case Gdef when !scan.HasGdef:
                    scan.HasGdef = true; scan.GdefOrdinal = i;
                    scan.GdefLocation = directory.TableLocation(i);
                    break;
                case Gsub when !scan.HasGsub:
                    scan.HasGsub = true; scan.GsubOrdinal = i;
                    scan.GsubLocation = directory.TableLocation(i);
                    break;
                case Gpos when !scan.HasGpos:
                    scan.HasGpos = true; scan.GposOrdinal = i;
                    scan.GposLocation = directory.TableLocation(i);
                    break;
                case Base when !scan.HasBase:
                    scan.HasBase = true; scan.BaseOrdinal = i;
                    scan.BaseLocation = directory.TableLocation(i);
                    break;
                case Jstf when !scan.HasJstf:
                    scan.HasJstf = true; scan.JstfOrdinal = i;
                    scan.JstfLocation = directory.TableLocation(i);
                    break;
                case Math when !scan.HasMath:
                    scan.HasMath = true; scan.MathOrdinal = i;
                    scan.MathLocation = directory.TableLocation(i);
                    break;
                case Kern when !scan.HasKern:
                    scan.HasKern = true; scan.KernOrdinal = i;
                    scan.KernLocation = directory.TableLocation(i);
                    break;
                case Hhea when !scan.HasHhea:
                    scan.HasHhea = true; scan.HheaLocation = directory.TableLocation(i);
                    break;
                case Hmtx when !scan.HasHmtx:
                    scan.HasHmtx = true; scan.HmtxLocation = directory.TableLocation(i);
                    break;
                case Vhea when !scan.HasVhea:
                    scan.HasVhea = true; scan.VheaLocation = directory.TableLocation(i);
                    break;
                case Vmtx when !scan.HasVmtx:
                    scan.HasVmtx = true; scan.VmtxLocation = directory.TableLocation(i);
                    break;
                case Cbdt when !scan.HasCbdt:
                    scan.HasCbdt = true; scan.CbdtLocation = directory.TableLocation(i);
                    break;
                case Cblc when !scan.HasCblc:
                    scan.HasCblc = true; scan.CblcLocation = directory.TableLocation(i);
                    break;
                case Ebdt when !scan.HasEbdt:
                    scan.HasEbdt = true; scan.EbdtLocation = directory.TableLocation(i);
                    break;
                case Eblc when !scan.HasEblc:
                    scan.HasEblc = true; scan.EblcLocation = directory.TableLocation(i);
                    break;
            }
        }
        return scan;
    }

    private static bool TryAdmitProfile(SfntDirectoryView directory, ProfileScan profile,
        MaxpTable maxp, PrepareState state, out ReadStatus refusalStatus,
        out FontDiagnostic diagnostic)
    {
        refusalStatus = ReadStatus.UnsupportedFormat;
        // glyf/loca must pair or both be absent; a lone outline spine is a broken face.
        if (profile.HasGlyf != profile.HasLoca)
            return RejectProfile(state, DiagnosticCode.OutlineProfileUnsupported,
                profile.HasGlyf ? profile.GlyfLocation : profile.LocaLocation,
                out diagnostic);
        if (state.Mode == SubsetMode.CompactRemap &&
            (profile.HasHhea != profile.HasHmtx || profile.HasVhea != profile.HasVmtx))
        {
            // RC5 (SC card §3): the compact rebuild obligation for the metrics families
            // has no execution path without the pairing partner (glyf/loca precedent).
            DataLocation orphan = profile.HasHhea != profile.HasHmtx
                ? (profile.HasHhea ? profile.HheaLocation : profile.HmtxLocation)
                : (profile.HasVhea ? profile.VheaLocation : profile.VmtxLocation);
            return RejectProfile(state, DiagnosticCode.OutlineProfileUnsupported, orphan,
                out diagnostic, orphan.TableTag);
        }
        if (state.Mode == SubsetMode.CompactRemap &&
            (profile.HasCbdt != profile.HasCblc || profile.HasEbdt != profile.HasEblc))
        {
            // RE1 (SE card §3): the compact rebuild obligation for the bitmap pool+index
            // pairs has no execution path without the pairing partner — the joint read
            // chain is defined over both tables only (RC5 metrics-pairing shape, the
            // same diagnostic code; the refusal names the orphan tag).
            DataLocation orphan = profile.HasCbdt != profile.HasCblc
                ? (profile.HasCbdt ? profile.CbdtLocation : profile.CblcLocation)
                : (profile.HasEbdt ? profile.EbdtLocation : profile.EblcLocation);
            return RejectProfile(state, DiagnosticCode.OutlineProfileUnsupported, orphan,
                out diagnostic, orphan.TableTag);
        }
        if (profile.HasCff2)
        {
            // CFF2 admission (SV card §1): the outright refusal of the static-first
            // slices is replaced by the CFF branch's shape. The structural guards run
            // here — A2 (two outline spines share the face; the CFF1+glyf form at the
            // former gate's position, now naming the CFF2 tag) and A4 (a CFF2 face
            // expects maxp 0.5, spec L1574, the CFF form above) — and the typed read
            // chain runs inside the variable admission below, where the fvar axis
            // authority for the store signature coupling is materialized.
            if (profile.HasGlyf || profile.HasLoca || profile.HasCff)
                return RejectProfile(state, DiagnosticCode.OutlineProfileUnsupported,
                    profile.Cff2Location, out diagnostic);
            if (maxp is not Maxp05Table)
                return RejectProfile(state, DiagnosticCode.OutlineProfileUnsupported,
                    maxp.Location.At(DataField.Version, MaxpLayout.VersionAt),
                    out diagnostic, maxp.VersionRaw);
        }
        if (profile.HasCff)
        {
            // Static CFF1 admission (card §1.2): a CFF table may not share the face with
            // a TrueType outline spine, a CFF face expects maxp 0.5 symmetrically to the
            // TrueType face's maxp 1.0 requirement, and the table itself must pass the
            // typed read chain before any policy decision consumes it (N2/N3/N4). Both
            // modes walk this chain; the compact mode's extra identity guard runs at the
            // map freeze where the charset column is materialized (S1c card §1.1).
            if (profile.HasGlyf || profile.HasLoca)
                return RejectProfile(state, DiagnosticCode.OutlineProfileUnsupported,
                    profile.CffLocation, out diagnostic);
            if (maxp is not Maxp05Table)
                return RejectProfile(state, DiagnosticCode.OutlineProfileUnsupported,
                    maxp.Location.At(DataField.Version, MaxpLayout.VersionAt),
                    out diagnostic, maxp.VersionRaw);
            if (!TryAdmitCffFace(directory, profile, maxp, state,
                    out refusalStatus, out diagnostic))
                return false;
        }
        if (profile.HasFvar || profile.HasCff2 || profile.HasGvar ||
            profile.HasCvar || profile.HasHvar || profile.HasVvar ||
            profile.HasMvar)
        {
            // The variable-font profile (SV card §1): fvar promotes from refusal
            // trigger to profile discriminating input, and both modes walk the one
            // admission chain — A1/A3/A5's combination guards, the fvar axis
            // authority, the family consistency walk and the CFF2 typed read. The
            // GID/store-coupled rows (gvar/cvar/HVAR/VVAR/MVAR) also trigger the
            // chain without fvar: their T5-02 explicit contracts re-answer against
            // the zero axis count and refuse through the read chains' own
            // diagnostics (the gvar-actual-fvar-dependency-missing witness), so a
            // broken face never rides a raw copy over its stale GID domain.
            if (!TryAdmitVariableFace(directory, profile, maxp, state,
                    out refusalStatus, out diagnostic))
                return false;
        }
        if (profile.HasGlyf && maxp is not Maxp10Table)
        {
            // A TrueType outline spine requires maxp 1.0 (card §1.2; the metric
            // operations' trueType && maxp is Maxp10Table form).
            return RejectProfile(state, DiagnosticCode.OutlineProfileUnsupported,
                maxp.Location.At(DataField.Version, 0), out diagnostic,
                maxp.VersionRaw);
        }
        if (profile.HasGlyf && !profile.HasCmap)
        {
            // cmap is a minimal-rebuild-set member; the TrueType path cannot fulfil the
            // rebuild obligation without it (OQ-S1a-3 lean).
            return RejectProfile(state, DiagnosticCode.OutlineProfileUnsupported,
                directory.Face.Location with { TableTag = Cmap }, out diagnostic);
        }
        diagnostic = default;
        return true;
    }

    private static bool RejectProfile(PrepareState state, DiagnosticCode code,
        DataLocation at, out FontDiagnostic diagnostic, ulong detail = 0)
    {
        state.Diagnostic = new(code, at, detail);
        diagnostic = state.Diagnostic;
        return false;
    }

    // ---- cmap subtable format admission (card §5 refusal face) ----

    private static bool TryAdmitCmapFormats(SfntDirectoryView directory, int cmapOrdinal,
        PrepareState state, out FontDiagnostic diagnostic)
    {
        ReadStatus status = directory.TryGetTableBytes(cmapOrdinal, out var cmapBytes,
            out diagnostic);
        if (status != ReadStatus.Done) return false;
        status = CmapView.TryOpen(cmapBytes, out var view, out diagnostic,
            directory.TableLocation(cmapOrdinal));
        if (status != ReadStatus.Done) return false;
        for (int i = 0; i < view.NumTables; i++)
        {
            status = view.TryGetSubtableExtent(i, out ushort format, out uint offset,
                out _, out diagnostic);
            if (status != ReadStatus.Done) return false;
            // {2, 8, 10, 13} sit outside the authoritative rebuild set {4, 12}; format 14
            // carries UVS semantics this slice does not serve (OQ-S0-9; S0-RF-007..010).
            if (format is 2 or 8 or 10 or 13 or 14)
            {
                state.Diagnostic = new(DiagnosticCode.CmapFormatUnsupported,
                    view.Location.AtLocal(DataField.Offset, offset, i), format);
                diagnostic = state.Diagnostic;
                return false;
            }
            if (format is not (0 or 4 or 6 or 12))
            {
                // The profile gate requires every cmap subtable to be typed-readable
                // (card §1.2); an unknown format refuses rather than passing through raw.
                state.Diagnostic = new(DiagnosticCode.OutlineProfileUnsupported,
                    view.Location.AtLocal(DataField.Offset, offset, i), format);
                diagnostic = state.Diagnostic;
                return false;
            }
        }
        diagnostic = default;
        return true;
    }

    // ---- per-table policy decision (card §2 step 4; matrix retainGidsPolicy column) ----

    private static bool TryDecideTable(SfntDirectoryView directory, int ordinal, uint tag,
        SubsetOptions options, PrepareState state, bool cffOrCff2Face,
        out FontDiagnostic diagnostic)
    {
        diagnostic = default;
        // The two matrix columns meet here: retain-GIDs follows the retainGidsPolicy
        // column, compact-remap the primaryPolicy column (SC card §4 commit ①; the only
        // authority for both is validation/FontWeave.S0/policy-matrix.json).
        bool compact = state.Mode == SubsetMode.CompactRemap;
        SubsetTableDecision decision;
        if (tag is Glyf or Loca or Cmap)
        {
            decision = new(tag, TableAction.Rebuild, SubsetPolicy.RebuildTyped,
                SubsetDecisionOrigin.MatrixRow);
        }
        else if (tag == Maxp)
        {
            // Retain keeps numGlyphs unchanged with the 1.0 maxima explicitly not
            // recomputed (OQ-S1a-10). Compact rebuilds maxp over the map output domain;
            // the maxima keep their original values and stay reported as not recomputed
            // (OQ-SC-1, the same shape), so they remain upper bounds of the subset set.
            decision = compact
                ? new(tag, TableAction.Rebuild, SubsetPolicy.RebuildTyped,
                    SubsetDecisionOrigin.MatrixRow)
                : new(tag, TableAction.Preserve, SubsetPolicy.RetainRaw,
                    SubsetDecisionOrigin.MatrixRow);
        }
        else if (tag == Head)
        {
            // RETAIN_RAW: head bytes move untouched, so indexToLocFormat — and therefore
            // the rebuilt loca's format — cannot change (card §1.2).
            decision = new(tag, TableAction.Preserve, SubsetPolicy.RetainRaw,
                SubsetDecisionOrigin.MatrixRow);
        }
        else if (tag == Post)
        {
            ReadStatus status = directory.TryGetTableBytes(ordinal, out var postBytes,
                out diagnostic);
            if (status != ReadStatus.Done) return false;
            status = PostView.TryOpen(postBytes, out var postView, out diagnostic,
                directory.TableLocation(ordinal));
            if (status != ReadStatus.Done) return false;
            if (postView.Version == 0x00025000)
            {
                // post 2.5's deprecated signed-byte offsets refuse (S0-RF-011-POST25).
                state.Diagnostic = new(DiagnosticCode.PostVersionDeprecated,
                    directory.TableLocation(ordinal).At(DataField.Version, 0));
                diagnostic = state.Diagnostic;
                return false;
            }
            // post 2.0 carries a per-GID glyphNameIndex, so compact rebuilds it;
            // 1.0/3.0 have no per-GID data and stay raw in both modes (SC card §2).
            decision = compact && postView.Version == PostLayout.Version20
                ? new(tag, TableAction.Rebuild, SubsetPolicy.RebuildTyped,
                    SubsetDecisionOrigin.MatrixRow)
                : new(tag, TableAction.Preserve, SubsetPolicy.RetainRaw,
                    SubsetDecisionOrigin.MatrixRow);
        }
        else if (tag is Kern or Gsub or Gpos or Gdef or Base or Jstf or Math)
        {
            // S2b-1 (OQ-S2b-2, decide per-tag three-state): the rewritten prefix
            // (GSUB/GPOS/GDEF) answers the caller's PreserveLayout selection in both
            // modes — retain keeps the S2a RETAIN_RAW form, compact flips to the
            // primaryPolicy column's executed REBUILD_TYPED (MatrixRow origin: the
            // request only pulls the row's fourth alternate trigger, it never
            // renegotiates the policy). The four unrewritten family rows keep their
            // byte-identical pre-S2b shape: RETAIN_RAW under retain-GIDs only, the
            // code-23 refusal under compact — their producers arrive with S2b-2.
            bool rewrittenPrefix = tag is Gsub or Gpos or Gdef;
            if (options.PreserveLayout && (!compact || rewrittenPrefix))
            {
                decision = compact
                    ? new(tag, TableAction.Rebuild, SubsetPolicy.RebuildTyped,
                        SubsetDecisionOrigin.MatrixRow)
                    : new(tag, TableAction.Preserve, SubsetPolicy.RetainRaw,
                        SubsetDecisionOrigin.MatrixRow);
            }
            else if (!options.NoLayout)
            {
                // Layout retention has no S1 execution path: the request refuses and
                // points at S2 (W1 §2.2; S0-RF-004-LAYOUT). Compact keeps this shape
                // under every option combination except the S2b-1 rewritten prefix —
                // an unrewritten family table refuses here as its first diagnostic.
                state.Diagnostic = new(DiagnosticCode.LayoutFamilyUnsupported,
                    directory.TableLocation(ordinal), tag);
                diagnostic = state.Diagnostic;
                return false;
            }
            else
            {
                decision = new(tag, TableAction.Drop, SubsetPolicy.Delete,
                    SubsetDecisionOrigin.RequestOption);
                state.Dropped.Add(tag);
            }
        }
        else if (tag is Cvt or Fpgm or Prep or Ltsh)
        {
            // Hinting family: RETAIN_RAW by default, DELETE under the explicit
            // drop-hinting option; gasp is preserved regardless (W1 §2.3). LTSH's
            // per-GID threshold domain instead rebuilds under compact, where the GID
            // domain changes (SC card §2).
            decision = options.DropHinting
                ? new(tag, TableAction.Drop, SubsetPolicy.Delete,
                    SubsetDecisionOrigin.RequestOption)
                : compact && tag == Ltsh
                    ? new(tag, TableAction.Rebuild, SubsetPolicy.RebuildTyped,
                        SubsetDecisionOrigin.MatrixRow)
                    : new(tag, TableAction.Preserve, SubsetPolicy.RetainRaw,
                        SubsetDecisionOrigin.MatrixRow);
            if (options.DropHinting) state.Dropped.Add(tag);
        }
        else if (tag == Dsig)
        {
            ReadStatus status = directory.TryGetTableBytes(ordinal, out var signatureBytes,
                out diagnostic);
            if (status != ReadStatus.Done) return false;
            // An empty DSIG has no signature to invalidate. A real signature may only be
            // dropped (FoundationTableEditOperation.cs:156-169 empty-marker form).
            bool emptyDsig = signatureBytes.Length == DsigLayout.HeaderLength &&
                DsigView.TryOpen(signatureBytes, out var signatureView, out _,
                    directory.TableLocation(ordinal)) == ReadStatus.Done &&
                signatureView.Version == 1 && signatureView.NumSignatures == 0 &&
                signatureView.Flags == 0;
            if (!emptyDsig && !options.DropDigitalSignature)
            {
                state.Diagnostic = new(DiagnosticCode.SignatureRequiresDrop,
                    directory.TableLocation(ordinal), tag);
                diagnostic = state.Diagnostic;
                return false;
            }
            decision = options.DropDigitalSignature
                ? new(tag, TableAction.Drop, SubsetPolicy.Delete,
                    SubsetDecisionOrigin.RequestOption)
                : new(tag, TableAction.Preserve, SubsetPolicy.RetainRaw,
                    SubsetDecisionOrigin.MatrixRow);
            if (options.DropDigitalSignature) state.Dropped.Add(tag);
        }
        else if (compact && tag == Cff)
        {
            // primaryPolicy REBUILD_TYPED (policy-matrix CFF row): the compact rebuild
            // re-splices charset/encoding/CharStrings (and, CID-keyed, FDSelect) around
            // the frozen map's ascending stable identity (S1c card §2). The payload
            // producer for this decision is the CFF1 compact rebuild; until a producer
            // exists the copy loop's switch refuses with the decision's own tag, never
            // passing a raw CFF table over a changed GID domain.
            decision = new(tag, TableAction.Rebuild, SubsetPolicy.RebuildTyped,
                SubsetDecisionOrigin.MatrixRow);
        }
        else if (compact && tag == Vorg)
        {
            if (!cffOrCff2Face)
            {
                // RC2 (SC card §3, widened by the SV slice's OQ-SV-7 lean): on a
                // face whose outline spine no CFF rebuild serves, the VORG compact
                // rebuild has no execution path, so the request refuses instead of
                // leaving per-GID records on stale GIDs. VORG is a CFF-domain
                // per-GID table and a CFF2 face legally carries it, so the CFF2
                // face now rides the same remap as the CFF1 face (SV card §2.7).
                // Keyword contract: "VORG"/"compact"/"rebuild"; code
                // UnsupportedOperation (=17), unchanged since SC.
                state.Diagnostic = new(DiagnosticCode.UnsupportedOperation,
                    directory.TableLocation(ordinal), tag);
                diagnostic = state.Diagnostic;
                return false;
            }
            // CFF face: VORG's per-GID records ride the same remap as the CFF rebuild
            // (matrix "随 CFF 重建联动" coupling, S1c card §3). The payload producer is
            // the VORG remap; the copy loop's switch refuses until it exists.
            decision = new(tag, TableAction.Rebuild, SubsetPolicy.RebuildTyped,
                SubsetDecisionOrigin.MatrixRow);
        }
        else if (compact && tag == Cff2)
        {
            // primaryPolicy REBUILD_TYPED (policy-matrix CFF2 row, executed since the
            // SV slice): the compact rebuild re-slots CharStrings/FDSelect over the
            // frozen map's cff2-idx stable identity (SV card §2.5/§3). The payload
            // producer follows in the SV slice; until it exists the copy loop's
            // switch refuses with the decision's own tag, never passing a raw CFF2
            // table over a changed GID domain.
            decision = new(tag, TableAction.Rebuild, SubsetPolicy.RebuildTyped,
                SubsetDecisionOrigin.MatrixRow);
        }
        else if (compact && tag is Gvar or Hvar or Vvar)
        {
            // primaryPolicy REBUILD_TYPED (policy-matrix rows, revised by the SV
            // slice's OQ-SV-3 adjudication): gvar re-splices its per-GID tuple blocks
            // and HVAR/VVAR re-map their DeltaSetIndexMap domains over the frozen
            // map (SV card §2.1/§2.3). The payload producers follow in the SV slice;
            // until they exist the copy loop's switch refuses with the decision's
            // own tag.
            decision = new(tag, TableAction.Rebuild, SubsetPolicy.RebuildTyped,
                SubsetDecisionOrigin.MatrixRow);
        }
        else if (compact && tag is Merg or Colr or Svg or Sbix or Cbdt or Cblc
            or Ebdt or Eblc or Ebsc)
        {
            // SE (SE card §1): the color/bitmap/MERG rows' primaryPolicy column now
            // reads REBUILD_TYPED for compact — the GID domain changes, so these
            // per-GID tables rebuild over the frozen map (retain keeps raw; the
            // pairing gate and the per-table read chains carry the refusal faces).
            decision = new(tag, TableAction.Rebuild, SubsetPolicy.RebuildTyped,
                SubsetDecisionOrigin.MatrixRow);
        }
        else if (compact && tag is Hhea or Hmtx or Vhea or Vmtx or Vdmx or Hdmx)
        {
            // primaryPolicy REBUILD_TYPED rows with a per-GID storage layout (metrics
            // pairs, device/threshold tables): compact rebuilds, retain keeps raw
            // (matrix primaryPolicy column).
            decision = new(tag, TableAction.Rebuild, SubsetPolicy.RebuildTyped,
                SubsetDecisionOrigin.MatrixRow);
        }
        else if (IsKnownSubsetTable(tag))
        {
            decision = new(tag, TableAction.Preserve, SubsetPolicy.RetainRaw,
                SubsetDecisionOrigin.MatrixRow);
        }
        else if (options.UnknownTables is { } declared &&
            declared.TryGetValue(tag, out var choice))
        {
            decision = choice == SubsetUnknownTableChoice.Drop
                ? new(tag, TableAction.Drop, SubsetPolicy.Delete,
                    SubsetDecisionOrigin.RequestOption)
                : new(tag, TableAction.Preserve, SubsetPolicy.RetainRaw,
                    SubsetDecisionOrigin.RequestOption);
            if (choice == SubsetUnknownTableChoice.Drop) state.Dropped.Add(tag);
        }
        else
        {
            // An undeclared out-of-catalogue tag refuses (W1 §2.1; MetadataEditOperation
            // .cs:88-90 same diagnostic shape, S0-RF-002-UNKNOWN).
            state.Diagnostic = new(DiagnosticCode.UnknownTable,
                directory.TableLocation(ordinal), tag);
            diagnostic = state.Diagnostic;
            return false;
        }
        state.Decisions.Add(decision);
        return true;
    }

    /// <summary>The 50-tag directory catalogue: the 47 opaque-relocatable tags the
    /// metadata operation already knows plus head, name and DSIG. The authoritative
    /// per-table policies live in the frozen matrix data; this set only separates
    /// "known, matrix-decided" from "caller must declare" (W1 §2.1).</summary>
    private static bool IsKnownSubsetTable(uint tag) =>
        tag is Head or Name or Dsig ||
        MetadataEditOperation.IsKnownMetadataIndependentTable(tag);

    // ---- failure plumbing: finite refusal, retained report, no bytes (W1 §4.5) ----

    private static ReadStatus Refuse(PrepareState state, ReadStatus status,
        FontDiagnostic diagnostic, out SubsetResult? result, out SubsetReport? report,
        out FontDiagnostic outDiagnostic)
    {
        result = null;
        if (diagnostic.Code != 0) state.Diagnostics.Add(diagnostic);
        report = state.BuildReport();
        outDiagnostic = diagnostic;
        return status;
    }

        /// <summary>Mutable pipeline evidence; a refusal freezes whatever accumulated.</summary>
        private sealed class PrepareState
        {
            internal PrepareState(SubsetMode? mode, BuildBudget budget)
            {
                Mode = mode;
                Budget = budget;
            }

            internal BuildBudget Budget { get; }
            internal SubsetMode? Mode { get; }
            internal readonly List<SubsetTableDecision> Decisions = [];
            internal readonly List<SubsetOptionRecord> OptionRecords = [];
            internal readonly List<uint> Dropped = [];
            internal readonly List<FontDiagnostic> Diagnostics = [];
            internal FontDiagnostic Diagnostic;
            internal bool[] Retained = [];
            internal List<ushort>? ClosureAdded;
            internal List<CmapConsumedRecord> CmapConsumedNotEmitted = [];
            internal int UnselectedSlotCount;
            internal GlyphRemap? Map;
            internal FrozenMapArtifact? FrozenMapArtifact;
            internal string? MapArtifactSha256;
            internal CffStaleness? CffStaleness;
            internal VorgStaleness? VorgStaleness;
            // The one materialized CFF1 model of a compact CFF face, parked by the map
            // freeze (S1c card §1.2): exactly one of the two forms is non-null, matching
            // the flavor, and the payload rebuilds consume it without a second read.
            internal Cff1Table? CffNameKeyed;
            internal CidCff1Table? CffCidKeyed;
            // The variable-profile admission chain's materialized models (SV card
            // §1): every present GID-domain row parks here once — cvar/MVAR stay
            // validation-only (RETAIN_RAW has no payload consumer) — and the compact
            // rebuilds consume them without a second read.
            internal FvarTable? Fvar;
            internal GvarTable? Gvar;
            internal HvarTable? Hvar;
            internal VvarTable? Vvar;
            internal Cff2Table? Cff2;
            // The S2a PreserveLayout admission segment's materialized layout models
            // (card §2.1): one whole-table model per present layout table, parked once
            // and consumed by the closure and its verification without a second read.
            internal GdefTable? Gdef;
            internal GdefLookupContext? GdefContext;
            internal LayoutListsTable? Gsub;
            internal LayoutListsTable? Gpos;
            internal JstfTable? Jstf;
            internal KernTable? Kern;
            internal MathTable? Math;
            // BASE is validation-only (no GID reference anywhere — S2a card §2.4);
            // the flag only feeds the census's zero-reference row.
            internal bool BaseAdmitted;
            // The layout union's additions (L \ (seed ∪ {0}), ascending — the merge
            // happened before the composite closure, so the two addition lists stay
            // disjoint by construction) and the per-table reference census (OQ-S2a-4).
            internal List<ushort>? LayoutClosureAdded;
            internal List<LayoutTableCensus>? LayoutReferenceCensus;

            /// <summary>Tags a SE rebuild stage found empty after pruning (OQ-SE-5):
            /// the payload-time drop revises the decide-stage Rebuild before any byte
            /// exists, so DroppedTables and the decision set stay consistent.</summary>
            internal readonly HashSet<uint> PayloadDropped = [];

            internal void ReviseDecision(uint tag, TableAction action, SubsetPolicy policy)
            {
                for (int i = 0; i < Decisions.Count; i++)
                {
                    if (Decisions[i].Tag != tag) continue;
                    Decisions[i] = new(tag, action, policy, Decisions[i].Origin);
                    if (action == TableAction.Drop) Dropped.Add(tag);
                    return;
                }
            }

            internal SubsetReport BuildReport()
            {
                // ClosureAddedGids stays empty until the closure stage has run; a refusal
                // before that point has no additions to report. The map artifact sha
                // appears once the artifact has serialized (SC card §1.3); the staleness
                // members stay null unless the static CFF1 profile admission filled them.
                return new SubsetReport(Mode, [.. Decisions], UnselectedSlotCount,
                    ClosureAdded?.ToArray() ?? [], [.. Dropped], [.. OptionRecords],
                    [.. CmapConsumedNotEmitted], [.. Diagnostics], MapArtifactSha256,
                    CffStaleness, VorgStaleness,
                    LayoutClosureAdded?.ToArray() ?? [],
                    LayoutReferenceCensus?.ToArray() ?? []);
            }
        }
}
