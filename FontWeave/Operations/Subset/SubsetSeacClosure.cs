using FontWeave.Containers;
using FontWeave.Core;
using FontWeave.Tables;

namespace FontWeave.Operations.Subset;

/// <summary>seac reference closure over static CFF1 faces (S3 card §2).
///
/// Type 2 endchar's deprecated seac form (TN5176 Type 2 charstring appendix) references
/// its base and accent glyphs by STANDARD-encoding code, so the retained set must extend
/// to both referenced glyphs or the output renders the composite from missing parts —
/// the S1c compact slice kept the charstring bytes but could drop a referenced glyph
/// (S1c card §2.3's registered gap). The scan is a token-level conservative walk
/// (OQ-S3-2 lean) over the shared walker <see cref="SubrWalk"/> (OQ-S4-5: the stack
/// effect table, three-state values, hint-persistent frames, biased descent and cycle
/// guard are one implementation with the S4 prune analysis, whose corrected
/// TN5177 §4.7 bias — index = operand + bias, tiers {107, 1131, 32768} — is the INDEX
/// semantics both consumers share; S4 card §8 finding 1). Detection resolves through
/// the Standard encoding regardless of the face's own encoding form (card §2.2); a
/// reference that cannot be decided statically — a code without a Standard entry, a
/// standard name outside the charset, operands still unknown at endchar, or any seac
/// form at all on a CID-keyed face, whose charset has no name domain to resolve
/// through (OQ-S3-3 lean) — refuses the operation with UnsupportedOperation(17) at the
/// model's location, failure atomic. Newly resolved glyphs join the retained set and
/// are scanned in turn (worklist fixed point, the S1a CloseComposites shape); every
/// visited program body charges one work unit and every newly added GID one more (the
/// closure budget's injection point, S0-BF-001).</summary>
public static partial class SubsetOperation
{
    /// <summary>Materializes the CFF table of an admitted static CFF1 face once, after
    /// decide and before any closure or freeze stage (S3 card §1.3, OQ-S3-5): the parked
    /// model feeds the seac closure below, the compact map freeze, the subr prune plan
    /// and the payload rebuilds, replacing the freeze-time materialization — one
    /// TryRead per operation in both modes (single-parse discipline, S1c card §1.2).</summary>
    private static ReadStatus MaterializeCff1(SfntDirectoryView directory, int cffOrdinal,
        PrepareState state, out FontDiagnostic diagnostic)
    {
        ReadStatus status = directory.TryGetTableBytes(cffOrdinal, out var cffBytes,
            out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = Cff1View.TryOpen(cffBytes, out var view, out diagnostic,
            directory.TableLocation(cffOrdinal));
        if (status != ReadStatus.Done) return status;
        status = view.TryRead(out var nameKeyed, out var cidKeyed, out _,
            out diagnostic, state.Budget);
        if (status != ReadStatus.Done) return status;
        state.CffNameKeyed = nameKeyed;
        state.CffCidKeyed = cidKeyed;
        return ReadStatus.Done;
    }

    /// <summary>Extends the retained set to the fixed point of the seac references of
    /// every retained glyph program (card §2.1). Runs after the layout reference union —
    /// layout-added programs are scanned too — and before the map freeze, so the
    /// compact frozen order carries the additions. Both flavors scan for detection;
    /// a detected form resolves name-keyed through the Standard encoding and refuses
    /// CID-keyed, where the charset carries CIDs and no name domain exists.</summary>
    private static ReadStatus CloseSeacReferences(DataLocation faceAt, PrepareState state,
        BuildBudget budget, out FontDiagnostic diagnostic)
    {
        diagnostic = default;
        Cff1Table? nameKeyed = state.CffNameKeyed;
        CidCff1Table? cidKeyed = state.CffCidKeyed;
        if (nameKeyed is null && cidKeyed is null)
        {
            // The materialization step parked the model for every admitted CFF face;
            // a missing one is a pipeline bug, never a request error (the parked-model
            // guard shape of the variable rebuilds).
            diagnostic = new(DiagnosticCode.UnsupportedOperation,
                faceAt with { TableTag = Cff }, Cff);
            return ReadStatus.UnsupportedFormat;
        }

        var added = new List<ushort>();
        var queue = new Queue<int>();
        for (int gid = 0; gid < state.Retained.Length; gid++)
            if (state.Retained[gid]) queue.Enqueue(gid);
        while (queue.Count > 0)
        {
            int gid = queue.Dequeue();
            byte[][] locals, globals;
            DataLocation modelAt;
            if (cidKeyed is not null)
            {
                cidKeyed.TryGetLocalSubrs(gid, out var perGlyph, out _);
                locals = perGlyph.ToArray();
                globals = cidKeyed.GlobalSubrs.ToArray();
                modelAt = cidKeyed.Location;
            }
            else
            {
                locals = nameKeyed!.LocalSubrs.ToArray();
                globals = nameKeyed.GlobalSubrs.ToArray();
                modelAt = nameKeyed.Location;
            }
            var scan = new SubrWalk(budget, modelAt.At(DataField.GlyphId, 0, gid),
                locals, globals, cff2: false, seac: true, analysis: null, fd: 0,
                rewrite: null);
            ReadStatus status = scan.RunSeac(cidKeyed is not null
                ? cidKeyed.CharStrings[gid] : nameKeyed!.CharStrings[gid],
                out diagnostic);
            if (status != ReadStatus.Done) return status;
            if (!scan.Found) continue;
            if (cidKeyed is not null)
            {
                // No name domain: a detected seac form is structurally unresolvable on
                // a CIDFont regardless of its operand values (TN5176: CIDFonts carry no
                // glyph names; OQ-S3-3's structural tier, distinct from a name-keyed
                // face's dangling data).
                return ReadWire.Fail(ReadStatus.UnsupportedFormat,
                    DiagnosticCode.UnsupportedOperation,
                    cidKeyed.Location.At(DataField.GlyphId, 0, gid), out diagnostic,
                    (ulong)gid, 0UL);
            }
            status = AddSeacReference(nameKeyed!, state, budget, gid, scan.BcharCode,
                added, queue, out diagnostic);
            if (status != ReadStatus.Done) return status;
            status = AddSeacReference(nameKeyed!, state, budget, gid, scan.AcharCode,
                added, queue, out diagnostic);
            if (status != ReadStatus.Done) return status;
        }
        added.Sort();
        state.SeacClosureAdded = added;
        return ReadStatus.Done;
    }

    /// <summary>Resolves one seac code through the Standard encoding and adds its
    /// glyph to the retained set (card §2.2/§2.3). GID 0 (.notdef) resolves and adds
    /// nothing; a code without a Standard entry and a name outside the charset are
    /// the face's own dangling references and refuse (the TT composite over-domain
    /// tier of face defects).</summary>
    private static ReadStatus AddSeacReference(Cff1Table model, PrepareState state,
        BuildBudget budget, int gid, int code, List<ushort> added, Queue<int> queue,
        out FontDiagnostic diagnostic)
    {
        // The seac codes are Standard-encoding bytes by definition; a known literal
        // outside 0..255 has no Standard entry, so it refuses on the same tier without
        // handing the accessor an out-of-domain code.
        if (code is < 0 or > 0xFF)
            return RefuseUnmappedCode(model, gid, code, out diagnostic);
        ReadStatus status = model.TryMapStandardCode(code, out int sid, out bool mapped,
            out int refGid, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (sid == 0) return RefuseUnmappedCode(model, gid, code, out diagnostic);
        if (!mapped)
        {
            // Category 2: the standard name is not in the charset — the input face's
            // own hanging reference, the same tier as a TT composite pointing outside
            // the face's GID domain.
            return ReadWire.Fail(ReadStatus.UnsupportedFormat,
                DiagnosticCode.UnsupportedOperation,
                model.Location.At(DataField.GlyphId, 0, gid), out diagnostic,
                (ulong)sid, 0UL);
        }
        if (refGid == 0 || state.Retained[refGid]) return ReadStatus.Done;
        if (!budget.Work(1, model.Location.At(DataField.GlyphId, 0, refGid),
                out diagnostic))
            return ReadStatus.ResourceLimit;
        state.Retained[refGid] = true;
        added.Add((ushort)refGid);
        queue.Enqueue(refGid);
        return ReadStatus.Done;
    }

    /// <summary>Category 1 (card §2.3): the code has no Standard encoding entry, so no
    /// glyph could ever answer the reference; Detail is the code (sign-extended for the
    /// impossible negative literals), Expected the empty SID.</summary>
    private static ReadStatus RefuseUnmappedCode(Cff1Table model, int gid, int code,
        out FontDiagnostic diagnostic) =>
        ReadWire.Fail(ReadStatus.UnsupportedFormat,
            DiagnosticCode.UnsupportedOperation,
            model.Location.At(DataField.GlyphId, 0, gid), out diagnostic,
            unchecked((ulong)(long)code), 0UL);
}
