using FontWeave.Containers;
using FontWeave.Core;
using FontWeave.Tables;

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
/// set (glyf, loca, cmap, maxp, hhea, hmtx, vhea, vmtx, post@2.0, hdmx, LTSH, VDMX)
/// over one frozen glyph map on a TrueType face and refuses CFF1 faces at admission
/// (SC card §1). Every other present table follows the frozen policy matrix's
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
        Hvar = 0x48564152, Vvar = 0x56564152, Mvar = 0x4D564152;

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
            if (!TryDecideTable(directory, i, record.Tag, options, state, out diagnostic))
                return Refuse(state, ReadStatus.UnsupportedFormat, diagnostic, out result,
                    out report, out diagnostic);
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
        GlyphRemap? map = null;
        if (compact)
        {
            status = FreezeCompactMap(request.Face, head!, state.Retained, state,
                budget, out map, out diagnostic);
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
                    _ => null
                };
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
        internal int GlyfOrdinal = -1, LocaOrdinal = -1, CffOrdinal = -1, CmapOrdinal = -1;
        internal DataLocation GlyfLocation, LocaLocation, CffLocation, Cff2Location,
            FvarLocation, HheaLocation, HmtxLocation, VheaLocation, VmtxLocation;
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
                    scan.HasCff2 = true; scan.Cff2Location = directory.TableLocation(i);
                    break;
                case Fvar when !scan.HasFvar:
                    scan.HasFvar = true; scan.FvarLocation = directory.TableLocation(i);
                    break;
                case Cmap when !scan.HasCmap:
                    scan.HasCmap = true; scan.CmapOrdinal = i;
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
        if (profile.HasCff2)
        {
            // CFF2 outlines are the variable-font gate's input and stay out of slice
            // (OQ-S0-7); the refusal names the CFF2 tag itself (card §1.2, N1).
            return RejectProfile(state, DiagnosticCode.OutlineProfileUnsupported,
                profile.Cff2Location, out diagnostic);
        }
        if (profile.HasCff && state.Mode == SubsetMode.CompactRemap)
        {
            // Compact-remap on a static CFF1 face has no rebuild path in slice: the
            // primaryPolicy column's CFF rebuild belongs to the CFF compact slice
            // (SC card §1 profile gate); the refusal names the CFF tag itself.
            return RejectProfile(state, DiagnosticCode.OutlineProfileUnsupported,
                profile.CffLocation, out diagnostic);
        }
        if (profile.HasCff)
        {
            // Static CFF1 admission (card §1.2): a CFF table may not share the face with
            // a TrueType outline spine, a CFF face expects maxp 0.5 symmetrically to the
            // TrueType face's maxp 1.0 requirement, and the table itself must pass the
            // typed read chain before any policy decision consumes it (N2/N3/N4).
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
        if (profile.HasFvar)
        {
            // Variable faces are outside the static profile this slice serves (OQ-S0-7
            // lean; HorizontalMetricsReencodeOperation.cs:156-162 precedent).
            return RejectProfile(state, DiagnosticCode.StaticProfileRequired,
                profile.FvarLocation, out diagnostic);
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
        SubsetOptions options, PrepareState state, out FontDiagnostic diagnostic)
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
            if (!options.NoLayout)
            {
                // Layout retention has no S1 execution path: the request refuses and
                // points at S2 (W1 §2.2; S0-RF-004-LAYOUT).
                state.Diagnostic = new(DiagnosticCode.LayoutFamilyUnsupported,
                    directory.TableLocation(ordinal), tag);
                diagnostic = state.Diagnostic;
                return false;
            }
            decision = new(tag, TableAction.Drop, SubsetPolicy.Delete,
                SubsetDecisionOrigin.RequestOption);
            state.Dropped.Add(tag);
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
        else if (compact && IsCompactDeferredRow(tag))
        {
            // RC2-RC4 (SC card §3): the primaryPolicy column defers these rows to a
            // later slice (VORG rebuilds only with CFF; MERG and the color/bitmap
            // families are DEFERRED_TO_S1), so a compact request — which always changes
            // the GID domain — refuses instead of leaving per-GID data on stale GIDs.
            // Keyword contracts: "VORG"/"compact"/"rebuild", "MERG"/"compact",
            // "<tag>"/"compact"; diagnostic code UnsupportedOperation (=17).
            state.Diagnostic = new(DiagnosticCode.UnsupportedOperation,
                directory.TableLocation(ordinal), tag);
            diagnostic = state.Diagnostic;
            return false;
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

    /// <summary>Rows the primaryPolicy column sends to a slice without a compact
    /// execution path (SC card §3 RC2-RC4): VORG rides the CFF rebuild, MERG and the
    /// color/bitmap families are DEFERRED_TO_S1, and the variation metrics families
    /// share that deferral. Retain-GIDs keeps all of them raw.</summary>
    private static bool IsCompactDeferredRow(uint tag) =>
        tag is Vorg or Merg or Colr or Svg or Sbix or Cbdt or Cblc or Ebdt or Eblc
            or Ebsc or Gvar or Cvar or Hvar or Vvar or Mvar;

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

            internal SubsetReport BuildReport()
            {
                // ClosureAddedGids stays empty until the closure stage has run; a refusal
                // before that point has no additions to report. The map artifact sha
                // appears once the artifact has serialized (SC card §1.3); the staleness
                // members stay null unless the static CFF1 profile admission filled them.
                return new SubsetReport(Mode, [.. Decisions], UnselectedSlotCount,
                    ClosureAdded?.ToArray() ?? [], [.. Dropped], [.. OptionRecords],
                    [.. CmapConsumedNotEmitted], [.. Diagnostics], MapArtifactSha256,
                    CffStaleness, VorgStaleness);
            }
        }
}
