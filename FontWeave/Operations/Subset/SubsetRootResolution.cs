using System.Buffers;
using System.Text;
using FontWeave.Containers;
using FontWeave.Core;
using FontWeave.Tables;

namespace FontWeave.Operations.Subset;

/// <summary>The SR slice's root resolution (SR card §2): the two resolved root forms —
/// UnicodeCodepoints and TextLayout — turn the request's scalar values into the input
/// GID set through the existing typed cmap read, once, at step 3.25 (after the cmap
/// format gate, before the layout admission; SR card §2.4). The materialized cmap model
/// parks on the prepare state and the rebuild consumes it without a second read
/// (OQ-SR-5) — the explicit-GID path never materializes it and stays byte-identical.
///
/// Every failure is a finite refusal and never produces a partial GID set (W1 §1.2):
/// a code point with no Unicode-semantic mapping (including the GID-0 resolutions the
/// typed read reports as unmapped) refuses (R1); a variation-selector value refuses
/// outright — posture 6 has no third state, and the format-14 admission gate already
/// removed UVS semantics, so a legacy record's incidental mapping of a VS code point
/// is never consulted; a code point with two
/// distinct mappings across the Unicode-semantic records refuses (R2 — the caller's
/// input set is not well-defined; the rebuild's first-wins rule adjudicates only the
/// code points the root did not commit, SR card §7.2); a text with an unpaired
/// surrogate refuses (R4); an out-of-domain resolved GID refuses (R7 — a face defect
/// face). The TextLayout root's caller-declared script/language/feature lists derive
/// the request's layout scope (there is no built-in Unicode script data to derive
/// anything, SR card §2.2/OQ-SR-2); the derived scope rides the S2c step 3.6 machine
/// unchanged and is mutually exclusive with an explicit options.LayoutScope (OQ-SR-4).</summary>
public static partial class SubsetOperation
{
    /// <summary>Step 3.25 (SR card §2.4). On success the resolved set is parked on the
    /// state (ascending, duplicate-free, in the maxp domain) and the derived scope —
    /// null unless the TextLayout root declares a list — rides out for step 3.6.</summary>
    private static ReadStatus ResolveSubsetRoot(SubsetRequest request,
        SfntDirectoryView directory, ProfileScan profile, MaxpTable maxp, int numGlyphs,
        PrepareState state, out SubsetLayoutScope? derivedScope, out FontDiagnostic diagnostic)
    {
        derivedScope = null;
        diagnostic = default;
        var root = request.Root!;
        DataLocation faceAt = request.Face.Location;
        ReadStatus status;

        // The scalar input: the form (b) sequence arrives step-1-validated (strictly
        // ascending scalar values); the form (c) text decodes once into its set.
        uint[] scalars;
        if (root.Kind == SubsetRootKind.UnicodeCodepoints)
        {
            scalars = [.. root.Codepoints!];
        }
        else
        {
            status = DecodeTextScalars(root.Text!, faceAt, state.Budget,
                out scalars, out diagnostic);
            if (status != ReadStatus.Done) return status;
            if (root.ScriptTags is not null || root.LanguageTags is not null ||
                root.FeatureTags is not null)
            {
                // The derived scope carries the caller's lists verbatim: the S2c
                // value domain and matching semantics (verbatim tags, no wildcards,
                // an absent tag is ignored and made visible by the census match
                // counts) are the machine's own, inherited without a new policy.
                derivedScope = new SubsetLayoutScope
                {
                    ScriptTags = root.ScriptTags,
                    LanguageTags = root.LanguageTags,
                    FeatureTags = root.FeatureTags
                };
            }
        }

        // The one cmap model materialization (OQ-SR-5): parked for the rebuild, so the
        // payload stage never re-reads the table on a resolved-root request. An empty
        // scalar set needs no model — it resolves to the empty GID set on any face,
        // including the cmap-less zero-rebuild profile (the empty-explicit-list twin);
        // a non-empty set with no cmap has nothing to resolve through and refuses.
        if (scalars.Length == 0 && !profile.HasCmap)
        {
            state.ResolvedRootGids = [];
            diagnostic = default;
            return ReadStatus.Done;
        }
        if (!profile.HasCmap)
        {
            diagnostic = new(DiagnosticCode.TableNotFound,
                faceAt with { TableTag = Cmap }, Cmap);
            return ReadStatus.InvalidFormat;
        }
        status = directory.TryGetTableBytes(profile.CmapOrdinal, out var cmapBytes,
            out diagnostic);
        if (status != ReadStatus.Done) return status;
        DataLocation cmapAt = directory.TableLocation(profile.CmapOrdinal);
        status = CmapContext.TryCreate(maxp, cmapAt, out var context, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = CmapView.TryOpen(cmapBytes, out var view, out diagnostic, cmapAt);
        if (status != ReadStatus.Done) return status;
        status = view.TryRead(context!, out var model, out diagnostic, state.Budget);
        if (status != ReadStatus.Done) return status;
        state.CmapModel = model;

        // Point queries over every Unicode-semantic record (the rebuild's own record
        // predicate): one (code point × record) query is one Work unit (SR card §2.4,
        // the SubsetLayoutScope shape). Non-Unicode records are read as evidence but
        // never consulted — a code point carried only by them is unmapped, not
        // ambiguous (SR card §2.1 posture 1). GID 0 never resolves: the typed read
        // reports it as unmapped, so the refusal is R1 and the implicit .notdef
        // membership stays the seeder's alone (posture 4).
        var resolved = new SortedSet<ushort>();
        var records = model!.Records;
        for (int s = 0; s < scalars.Length; s++)
        {
            uint code = scalars[s];
            // Posture 6, the explicit arm: variation selectors have no third state.
            // A face carrying format 14 was refused at the admission gate, so a VS
            // value here can never resolve through UVS semantics — and a legacy
            // Unicode record that happens to map the code point (the R1 hole this
            // arm closes) is still the posture-2 refusal, checked before any query.
            if (code is >= 0xFE00 and <= 0xFE0F || code is >= 0xE0100 and <= 0xE01EF)
            {
                diagnostic = new(DiagnosticCode.InvalidValue,
                    cmapAt.At(DataField.CharCode, code), code);
                return ReadStatus.InvalidFormat;
            }
            ushort? mapped = null;
            for (int i = 0; i < records.Length; i++)
            {
                if (!IsUnicodeSemantic(records[i].PlatformId, records[i].EncodingId))
                    continue;
                status = model.TryGetSubtable(i, out var subtable, out diagnostic);
                if (status != ReadStatus.Done) return status;
                if (!state.Budget.Work(1, cmapAt, out diagnostic))
                    return ReadStatus.ResourceLimit;
                status = subtable!.TryMapCharacter(code, out bool charMapped,
                    out uint glyphId, out diagnostic);
                if (status != ReadStatus.Done) return status;
                if (!charMapped) continue;
                if (mapped is { } previous)
                {
                    if (previous != glyphId)
                    {
                        // R2: cross-record ambiguity — the distinct-GID count reached
                        // two, so the caller's input set is not well-defined and no
                        // partial set may form (W1 §1.2; the first ambiguous record
                        // carries the location).
                        diagnostic = new(DiagnosticCode.InvalidValue,
                            subtable.Location.At(DataField.CharCode, code, i), code,
                            previous);
                        return ReadStatus.InvalidFormat;
                    }
                    continue;
                }
                mapped = (ushort)glyphId;
            }
            if (mapped is not { } gid)
            {
                // R1: no Unicode-semantic mapping — including code points resolving
                // to GID 0, which the typed read reports as unmapped. Both
                // dispositions are the same refusal (SR card §2.1 posture 4;
                // posture 6's VS arm runs above, before the query).
                diagnostic = new(DiagnosticCode.InvalidValue,
                    cmapAt.At(DataField.CharCode, code), code);
                return ReadStatus.InvalidFormat;
            }
            resolved.Add(gid);
        }

        // R7 (posture 5): a resolved GID outside the maxp domain is a face defect —
        // refuse rather than seed (the maxp.Ordinal/GlyphId form of the explicit
        // root's own domain gate).
        foreach (ushort gid in resolved)
        {
            if (gid < numGlyphs) continue;
            diagnostic = new(DiagnosticCode.IndexOutOfRange,
                maxp.Location.At(DataField.GlyphId, 4), gid, (ulong)numGlyphs);
            return ReadStatus.InvalidFormat;
        }
        state.ResolvedRootGids = [.. resolved];
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>The form (c) decode (SR card §2.2): one pass over the UTF-16 text with
    /// <see cref="Rune.DecodeFromUtf16"/>, the scalar values collected into a sorted
    /// distinct set. Text is a set-semantics entry — deduplication and ordering are the
    /// form's definition, not a normalization of a precise input contract (the form (b)
    /// sequence refuses what the text absorbs). Zero normalization beyond the decode.
    /// One rune is one Work unit.</summary>
    private static ReadStatus DecodeTextScalars(string text, DataLocation at,
        BuildBudget budget, out uint[] scalars, out FontDiagnostic diagnostic)
    {
        scalars = [];
        var set = new SortedSet<uint>();
        int cursor = 0;
        while (cursor < text.Length)
        {
            if (!budget.Work(1, at, out diagnostic)) return ReadStatus.ResourceLimit;
            OperationStatus status = Rune.DecodeFromUtf16(text.AsSpan(cursor), out Rune rune,
                out int consumed);
            if (status != OperationStatus.Done)
            {
                // R4 (defensive — the step-1 walk already refuses this shape): an
                // unpaired surrogate is a finite refusal, never a replacement char.
                return ReadWire.Fail(ReadStatus.InvalidEncoding, DiagnosticCode.InvalidUtf16,
                    at.At(DataField.Payload, (ulong)cursor), out diagnostic);
            }
            set.Add((uint)rune.Value);
            cursor += consumed;
        }
        scalars = [.. set];
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>The scope-declaration test over one scope record (the S2c card §1.3
    /// "any member non-null" shape, lifted so the derived scope rides the same test).</summary>
    private static bool ScopeDeclaredOn(SubsetLayoutScope? scope) =>
        scope is { } declared &&
        (declared.ScriptTags is not null || declared.LanguageTags is not null ||
            declared.FeatureTags is not null);
}
