using FontWeave.Containers;
using FontWeave.Core;
using FontWeave.Tables;
using FontWeave.Tables.Variation;

namespace FontWeave.Operations.Subset;

/// <summary>Variable-font profile admission (SV card §1): the one face-level walk that
/// replaces the two outright refusals — CFF2 faces (formerly OutlineProfileUnsupported
/// unconditionally, OQ-S0-7) and fvar faces (formerly StaticProfileRequired) now admit
/// through the typed read chains, with fvar promoted from refusal trigger to profile
/// discriminating input. Both modes walk the same chain; retain keeps every variable
/// table raw under the relaxed column, compact rebuilds the GID-domain rows over the
/// frozen map (gvar, HVAR/VVAR, CFF2 — the SV card §2 rebuild face).
///
/// The chain consumes only the tables' own typed readers (no second reader, AGENTS):
/// fvar is materialized first as the axis authority (T5-02), every present family
/// table then re-answers its explicit input contract against that authority — avar,
/// STAT, gvar, cvar (against the cvt value count), HVAR/VVAR and MVAR — and CFF2
/// closes with its own numGlyphs/axisCount signature coupling. Read failures refuse
/// with the reader's own diagnostics, never a new code (SV lean: 29–31 stays empty).
/// Every materialized model parks on the <see cref="PrepareState"/>, so the decide
/// loop and the payload rebuilds reuse the same objects without re-reading.</summary>
public static partial class SubsetOperation
{
    private static bool TryAdmitVariableFace(SfntDirectoryView directory,
        ProfileScan profile, MaxpTable maxp, PrepareState state,
        out ReadStatus refusalStatus, out FontDiagnostic diagnostic)
    {
        refusalStatus = ReadStatus.UnsupportedFormat;
        BuildBudget budget = state.Budget;
        int numGlyphs = maxp.NumGlyphs;
        bool ttSpine = profile.HasGlyf && profile.HasLoca;

        // A1 (SV card §4): fvar without a variable outline spine — no glyf/loca pair
        // and no CFF2 — has no rebuildable face this operation serves (the CFF1
        // face + fvar form keeps its refusal, now under the profile code 28 instead
        // of the retired static-profile code 22).
        if (profile.HasFvar && !ttSpine && !profile.HasCff2)
            return RejectProfile(state, DiagnosticCode.OutlineProfileUnsupported,
                profile.FvarLocation, out diagnostic);
        // A3 (SV card §4): gvar does not apply to CFF2 fonts (spec L1612); the
        // refusal names the gvar tag its rule is about.
        if (profile.HasCff2 && profile.HasGvar)
            return RejectProfile(state, DiagnosticCode.OutlineProfileUnsupported,
                profile.GvarLocation, out diagnostic, Gvar);

        // fvar: the axis-count/axis-order authority (T5-02). Its read chain runs
        // first; a failure propagates as-is (finite refusal, no partial admission).
        int axisCount = 0;
        if (profile.HasFvar)
        {
            ReadStatus status = directory.TryGetTableBytes(profile.FvarOrdinal,
                out var fvarBytes, out diagnostic);
            if (status != ReadStatus.Done) return false;
            status = FvarView.TryOpen(fvarBytes, out var fvarView, out diagnostic,
                profile.FvarLocation);
            if (status != ReadStatus.Done) return false;
            status = fvarView.ValidateTable(out diagnostic, budget);
            if (status != ReadStatus.Done) return false;
            status = fvarView.TryReadTable(out var fvar, out diagnostic, budget);
            if (status != ReadStatus.Done) return false;
            state.Fvar = fvar;
            axisCount = fvar!.AxisCount;
        }

        // The family consistency walk (T5-02 explicit input contracts, face level):
        // each present table re-answers its own validation against the fvar
        // authorities. Presence without a contract (fvar absent) still validates
        // against the view's structural domains — a table never bypasses its read
        // chain because the profile happens to be static.
        if (profile.HasAvar)
        {
            ReadStatus status = directory.TryGetTableBytes(profile.AvarOrdinal,
                out var avarBytes, out diagnostic);
            if (status != ReadStatus.Done) return false;
            status = AvarView.TryOpen(avarBytes, out var avarView, out diagnostic,
                profile.AvarLocation);
            if (status != ReadStatus.Done) return false;
            status = avarView.ValidateTable(axisCount, out diagnostic, budget);
            if (status != ReadStatus.Done) return false;
        }
        if (profile.HasStat)
        {
            ReadStatus status = directory.TryGetTableBytes(profile.StatOrdinal,
                out var statBytes, out diagnostic);
            if (status != ReadStatus.Done) return false;
            status = StatView.TryOpen(statBytes, out var statView, out diagnostic,
                profile.StatLocation);
            if (status != ReadStatus.Done) return false;
            status = statView.ValidateTable(state.Fvar, out diagnostic, budget);
            if (status != ReadStatus.Done) return false;
        }
        if (profile.HasGvar && !profile.HasCff2)
        {
            // The gvar model materializes once here; the compact rebuild (SV card
            // §2.1) consumes the parked model without a second read.
            ReadStatus status = directory.TryGetTableBytes(profile.GvarOrdinal,
                out var gvarBytes, out diagnostic);
            if (status != ReadStatus.Done) return false;
            status = GvarView.TryOpen(gvarBytes, out var gvarView, out diagnostic,
                profile.GvarLocation);
            if (status != ReadStatus.Done) return false;
            status = gvarView.ValidateTable(axisCount, numGlyphs, out diagnostic, budget);
            if (status != ReadStatus.Done) return false;
            status = gvarView.TryReadTable(axisCount, numGlyphs, out var gvar,
                out diagnostic, budget);
            if (status != ReadStatus.Done) return false;
            state.Gvar = gvar;
        }
        if (profile.HasCvar)
        {
            if (!profile.HasCvt)
            {
                // A5 (SV card §4): the cvar/cvt pairing is broken — cvar's delta
                // domain is CVT indices (OQ-SV-3 wire fact), so a missing partner
                // leaves the deltas unanchorable. The RC5 metrics-pairing shape,
                // same code.
                return RejectProfile(state, DiagnosticCode.OutlineProfileUnsupported,
                    profile.CvarLocation, out diagnostic, Cvar);
            }
            ReadStatus status = directory.TryGetTableBytes(profile.CvtOrdinal,
                out var cvtBytes, out diagnostic);
            if (status != ReadStatus.Done) return false;
            status = CvtView.TryOpen(cvtBytes, out var cvtView, out diagnostic,
                profile.CvtLocation);
            if (status != ReadStatus.Done) return false;
            status = directory.TryGetTableBytes(profile.CvarOrdinal,
                out var cvarBytes, out diagnostic);
            if (status != ReadStatus.Done) return false;
            status = CvarView.TryOpen(cvarBytes, out var cvarView, out diagnostic,
                profile.CvarLocation);
            if (status != ReadStatus.Done) return false;
            status = cvarView.ValidateTable(axisCount, cvtView.ValueCount,
                out diagnostic, budget);
            if (status != ReadStatus.Done) return false;
        }
        if (profile.HasHvar)
        {
            ReadStatus status = directory.TryGetTableBytes(profile.HvarOrdinal,
                out var hvarBytes, out diagnostic);
            if (status != ReadStatus.Done) return false;
            status = HvarView.TryOpen(hvarBytes, out var hvarView, out diagnostic,
                profile.HvarLocation);
            if (status != ReadStatus.Done) return false;
            status = hvarView.ValidateTable(axisCount, numGlyphs, out diagnostic, budget);
            if (status != ReadStatus.Done) return false;
            status = hvarView.TryReadTable(axisCount, numGlyphs, out var hvar,
                out diagnostic, budget);
            if (status != ReadStatus.Done) return false;
            state.Hvar = hvar;
        }
        if (profile.HasVvar)
        {
            ReadStatus status = directory.TryGetTableBytes(profile.VvarOrdinal,
                out var vvarBytes, out diagnostic);
            if (status != ReadStatus.Done) return false;
            status = VvarView.TryOpen(vvarBytes, out var vvarView, out diagnostic,
                profile.VvarLocation);
            if (status != ReadStatus.Done) return false;
            status = vvarView.ValidateTable(axisCount, numGlyphs, out diagnostic, budget);
            if (status != ReadStatus.Done) return false;
            status = vvarView.TryReadTable(axisCount, numGlyphs, out var vvar,
                out diagnostic, budget);
            if (status != ReadStatus.Done) return false;
            state.Vvar = vvar;
        }
        if (profile.HasMvar)
        {
            ReadStatus status = directory.TryGetTableBytes(profile.MvarOrdinal,
                out var mvarBytes, out diagnostic);
            if (status != ReadStatus.Done) return false;
            status = MvarView.TryOpen(mvarBytes, out var mvarView, out diagnostic,
                profile.MvarLocation);
            if (status != ReadStatus.Done) return false;
            status = mvarView.ValidateTable(axisCount, out diagnostic, budget);
            if (status != ReadStatus.Done) return false;
        }

        // CFF2 closes the chain: its store<->fvar signature coupling is carried by
        // the axis count fed here (Cff2ViewValidate, OQ-T5-05-7), and the
        // materialized model parks for the compact rebuild (SV card §2.5).
        if (profile.HasCff2)
        {
            ReadStatus status = directory.TryGetTableBytes(profile.Cff2Ordinal,
                out var cff2Bytes, out diagnostic);
            if (status != ReadStatus.Done) return false;
            status = Cff2View.TryOpen(cff2Bytes, out var cff2View, out diagnostic,
                profile.Cff2Location);
            if (status != ReadStatus.Done) return false;
            status = cff2View.ValidateTable(numGlyphs,
                profile.HasFvar ? axisCount : null, out diagnostic, budget);
            if (status != ReadStatus.Done) return false;
            status = cff2View.TryReadTable(numGlyphs,
                profile.HasFvar ? axisCount : null, out var cff2, out diagnostic, budget);
            if (status != ReadStatus.Done) return false;
            state.Cff2 = cff2;
        }

        diagnostic = default;
        return true;
    }
}
