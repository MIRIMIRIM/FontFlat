using System.Security.Cryptography;
using FontWeave.Containers;
using FontWeave.Core;
using FontWeave.Tables;

namespace FontWeave.Operations.Subset;

/// <summary>Compact-remap map freeze and artifact serialization (SC card §1).
///
/// The freeze happens after the composite closure, over the final retained set: the
/// output order is the ascending stable identity, which for the glyf kind is the
/// ascending input GID (OQ-SC-6 lean), and one GlyphRemap instance is frozen for the
/// whole operation. The map artifact serializes immediately after the freeze — decide
/// complete, no payload copied — with the payload charged to Buffer and the traversal to
/// Work (W1 §5.2 row 2); exhaustion refuses with ResourceLimit/BudgetExceeded at
/// TemporaryBytes and never degrades into a map-less product (W1 §4.3, S0-BF-002).</summary>
public static partial class SubsetOperation
{
    private static ReadStatus FreezeCompactMap(FontFaceRef face, HeadTable head,
        bool[] retained, PrepareState state, BuildBudget budget,
        out GlyphRemap? map, out FontDiagnostic diagnostic)
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
        var outputToInput = new ushort[outputCount];
        int at = 0;
        for (int gid = 0; gid < retained.Length; gid++)
            if (retained[gid]) outputToInput[at++] = (ushort)gid;
        // GlyphRemap.TryCreate enforces the frozen contract itself: 0 → 0, injectivity
        // and the maxp domain bound. A violation here is a pipeline bug, not a request
        // error, and surfaces with the map's own diagnostics.
        ReadStatus status = GlyphRemap.TryCreate(face, retained.Length, outputToInput,
            out map, out diagnostic);
        if (status != ReadStatus.Done) return status;
        state.Map = map;

        // The artifact is frozen and serialized before any payload stage can run
        // (W1 §4.3/§4.4): a serialization budget failure must refuse a map-less product
        // rather than downgrade.
        var entries = new FrozenMapEntry[outputCount];
        for (int o = 0; o < outputCount; o++)
            entries[o] = new((ushort)o, outputToInput[o],
                FrozenMapArtifactCodec.GlyfStableIdentity(outputToInput[o]));
        var artifact = new FrozenMapArtifact(face.SourceId, face.FaceIndex,
            head.Values.FontRevisionRaw, retained.Length, outputCount, entries)
        {
            InputFaceSha256 = Convert.ToHexString(
                SHA256.HashData(face.Source!.Bytes)).ToLowerInvariant(),
        };
        state.FrozenMapArtifact = artifact;
        status = FrozenMapArtifactCodec.TrySerialize(artifact, out byte[]? payload,
            out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (!budget.Work(entries.Length + 8, face.Location, out diagnostic) ||
            !budget.Buffer(payload!.Length, face.Location, out diagnostic))
            return ReadStatus.ResourceLimit;
        state.MapArtifactSha256 = FrozenMapArtifactCodec.Sha256Hex(artifact);
        return ReadStatus.Done;
    }
}
