using System.Security.Cryptography;
using FontWeave.Containers;
using FontWeave.Core;
using FontWeave.Tables;

namespace FontWeave.Operations.Subset;

/// <summary>Compact-remap map freeze and artifact serialization (SC card §1, S1c §1.2).
///
/// The freeze happens after the composite closure, over the final retained set: the
/// output order is the ascending stable identity (OQ-SC-6 lean) and one GlyphRemap
/// instance is frozen for the whole operation. On a TrueType face the stable identity
/// is the input GID itself (glyf kind, zero new work); on a static CFF1 face it is the
/// charset SID (name-keyed) or CID (CID-keyed), so the freeze materializes the CFF
/// table exactly once — the same view chain feeding the payload rebuilds, never a
/// second reader — guards the .notdef identity (S1c card §1.1), and sorts the retained
/// set ascending by that identity. The map artifact serializes immediately after the
/// freeze — decide complete, no payload copied — with the payload charged to Buffer and
/// the traversal to Work (W1 §5.2 row 2); exhaustion refuses with
/// ResourceLimit/BudgetExceeded at TemporaryBytes and never degrades into a map-less
/// product (W1 §4.3, S0-BF-002).</summary>
public static partial class SubsetOperation
{
    private static ReadStatus FreezeCompactMap(FontFaceRef face, HeadTable head,
        bool[] retained, SfntDirectoryView directory, int cffOrdinal, PrepareState state,
        BuildBudget budget, out GlyphRemap? map, out FontDiagnostic diagnostic)
    {
        map = null;
        // One walk over the final retained set lays the output order; the work unit
        // covers the counting and copy passes (no new default limit is introduced).
        // Both charges sit at the face location: the map is a face-level object, so the
        // BF-002 probe walk identifies the serialization boundary as the face-level
        // Buffer boundary past the decide-phase charges (W1 §5.2 row 2).
        if (!budget.Work(retained.Length, face.Location, out diagnostic))
            return ReadStatus.ResourceLimit;
        int outputCount = 0;
        for (int gid = 0; gid < retained.Length; gid++)
            if (retained[gid]) outputCount++;

        ReadOnlySpan<int> identities;
        DataLocation cffAt = default;
        if (cffOrdinal >= 0)
        {
            // CFF1 face: the stable identity needs the charset expansion, so the table
            // materializes here, once, through the official typed read chain; the model
            // is parked on the state for the payload rebuilds (S1c card §1.2 — same
            // view chain for map artifact and rebuild, no second reader).
            ReadStatus status = directory.TryGetTableBytes(cffOrdinal, out var cffBytes,
                out diagnostic);
            if (status != ReadStatus.Done) return status;
            status = Cff1View.TryOpen(cffBytes, out var view, out diagnostic,
                directory.TableLocation(cffOrdinal));
            if (status != ReadStatus.Done) return status;
            status = view.TryRead(out var nameKeyed, out var cidKeyed,
                out var flavor, out diagnostic, budget);
            if (status != ReadStatus.Done) return status;
            state.CffNameKeyed = nameKeyed;
            state.CffCidKeyed = cidKeyed;
            identities = flavor == Cff1Flavor.CidKeyed
                ? cidKeyed!.CharsetCids
                : nameKeyed!.CharsetSids;
            cffAt = flavor == Cff1Flavor.CidKeyed
                ? cidKeyed!.Location
                : nameKeyed!.Location;
            // A1 (S1c card §1.1): 0 is .notdef's identity — the expansion forces
            // sids[0]/cids[0] = 0, so any gid >= 1 carrying 0 would share the identity
            // of glyph 0 and break the map's injective identity rule (W1 §3.3 MUST).
            // The typed read guarantees span uniqueness otherwise, so this is the only
            // extra admission guard the compact path needs.
            for (int gid = 1; gid < identities.Length; gid++)
            {
                if (identities[gid] == 0)
                    return ReadWire.Fail(ReadStatus.InvalidFormat,
                        DiagnosticCode.InvalidValue,
                        cffAt.At(DataField.Cid, 0, gid),
                        out diagnostic, 0UL, 1UL);
            }
        }
        else
        {
            // glyf kind: the ascending input GID is the ascending stable identity.
            identities = ReadOnlySpan<int>.Empty;
        }

        // Ascending stable identity: the retained gids sort by their identity value.
        // The typed read's span uniqueness plus the zero guard above make identity
        // values unique per glyph, so the order is total (no tie-break can arise).
        var outputToInput = new ushort[outputCount];
        if (cffOrdinal < 0)
        {
            int at = 0;
            for (int gid = 0; gid < retained.Length; gid++)
                if (retained[gid]) outputToInput[at++] = (ushort)gid;
        }
        else
        {
            var order = new (int Identity, int Gid)[outputCount];
            int at = 0;
            for (int gid = 0; gid < retained.Length; gid++)
                if (retained[gid]) order[at++] = (identities[gid], gid);
            Array.Sort(order, static (a, b) => a.Identity.CompareTo(b.Identity));
            for (int i = 0; i < order.Length; i++)
                outputToInput[i] = (ushort)order[i].Gid;
        }
        // GlyphRemap.TryCreate enforces the frozen contract itself: 0 → 0, injectivity
        // and the maxp domain bound. The zero guard plus the sort above put GID 0 —
        // identity 0 — first, so the contract's outputToInput[0] == 0 holds by
        // construction; a violation here is a pipeline bug, not a request error, and
        // surfaces with the map's own diagnostics.
        ReadStatus mapStatus = GlyphRemap.TryCreate(face, retained.Length, outputToInput,
            out map, out diagnostic);
        if (mapStatus != ReadStatus.Done) return mapStatus;
        state.Map = map;

        // The artifact is frozen and serialized before any payload stage can run
        // (W1 §4.3/§4.4): a serialization budget failure must refuse a map-less product
        // rather than downgrade.
        var entries = new FrozenMapEntry[outputCount];
        for (int o = 0; o < outputCount; o++)
        {
            int input = outputToInput[o];
            entries[o] = new((ushort)o, (ushort)input, cffOrdinal < 0
                ? FrozenMapArtifactCodec.GlyfStableIdentity((ushort)input)
                : state.CffCidKeyed is not null
                    ? FrozenMapArtifactCodec.Cff1CidStableIdentity(
                        state.CffCidKeyed.CharsetCids[input])
                    : FrozenMapArtifactCodec.Cff1SidStableIdentity(
                        state.CffNameKeyed!.CharsetSids[input]));
        }
        var artifact = new FrozenMapArtifact(face.SourceId, face.FaceIndex,
            head.Values.FontRevisionRaw, retained.Length, outputCount, entries)
        {
            InputFaceSha256 = Convert.ToHexString(
                SHA256.HashData(face.Source!.Bytes)).ToLowerInvariant(),
        };
        state.FrozenMapArtifact = artifact;
        ReadStatus serializeStatus = FrozenMapArtifactCodec.TrySerialize(artifact,
            out byte[]? payload, out diagnostic);
        if (serializeStatus != ReadStatus.Done) return serializeStatus;
        if (!budget.Work(entries.Length + 8, face.Location, out diagnostic) ||
            !budget.Buffer(payload!.Length, face.Location, out diagnostic))
            return ReadStatus.ResourceLimit;
        state.MapArtifactSha256 = FrozenMapArtifactCodec.Sha256Hex(artifact);
        return ReadStatus.Done;
    }
}
