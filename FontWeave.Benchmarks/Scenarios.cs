using FontWeave;
using FontWeave.Containers;
using FontWeave.Core;
using FontWeave.Inspection;
using FontWeave.Operations;
using FontWeave.Operations.Subset;
using FontWeave.Tables;

namespace FontWeave.Benchmarks;

/// <summary>The benchmark scenarios. Each method is the complete per-invocation work a
/// FontWeave CLI command performs for the corresponding operation over one prepared
/// input: open the chain, operate, and (where the command writes a file) copy the
/// compiled sfnt into a fresh memory buffer. Public FontWeave API only.
///
/// Return convention: positive = bytes of the emitted sfnt (or the consumed read
/// extent for the read scenarios); negative = the negated <see cref="ReadStatus"/> of
/// a finite product refusal (the pipeline decides whole-before-compile, so a refusal
/// measures the admission+decide prefix only — see README's refusal map). The most
/// recent refusal's diagnostic is mirrored in <see cref="LastRefusal"/> for reporting.</summary>
public static class Scenarios
{
    /// <summary>The diagnostic of the most recent refusal this process observed; the
    /// runners are single-threaded, so no synchronization is required.</summary>
    public static FontDiagnostic LastRefusal { get; private set; }

    private static long Refused(ReadStatus status, FontDiagnostic diagnostic)
    {
        LastRefusal = diagnostic;
        return -(long)status;
    }
    /// <summary>The shared subset option set: retain the layout family raw (S2a) and
    /// drop any digital signature. Both are explicit caller choices the CLI surface
    /// exposes as --preserve-layout / --drop-signature; the corpus carries layout
    /// tables and (on some faces) DSIG, so the default refusal paths are not what the
    /// scenarios measure.</summary>
    public static SubsetOptions SharedSubsetOptions { get; } = new()
    {
        PreserveLayout = true,
        DropDigitalSignature = true,
    };

    private static MetadataEditOptions EditOptions { get; } = new()
    {
        DropDigitalSignature = true,
    };

    // ---- sfnt scenarios (FontInput) ----

    /// <summary>FontFile open + directory view (the CLI's container-level inspect).</summary>
    public static long OpenFace(FontInput input)
    {
        FontFile file = FontFile.Parse(input.Bytes, new FontLimits());
        FontFace face = file.GetFace(0);
        long extent = face.Tables.Count;
        foreach (SfntTable table in face.Tables)
            extent += table.Length + (uint)table.Tag.Length;
        return extent;
    }

    /// <summary>Full typed table read (the CLI's typed inspect/validate walk).</summary>
    public static long ReadAll(FontInput input)
    {
        var limits = input.Limits;
        ReadStatus status = FontSource.TryCopy(input.Bytes, out FontSource? source, out var diagnostic, limits.MaxInputBytes);
        if (status != ReadStatus.Done) return Refused(status, diagnostic);
        status = source!.TryGetFace(0, out FontFaceRef face, out diagnostic);
        if (status != ReadStatus.Done) return Refused(status, diagnostic);
        status = FontInspection.TryValidate(face, out var report, out diagnostic, null, limits);
        long consumed = report is null ? 0 : report.Tables.Sum(table =>
            (long)table.Ordinal + (long)table.Location.Offset +
            (long)(table.ValidationStatus.GetValueOrDefault() + 1));
        return status == ReadStatus.Done ? consumed : Refused(status, diagnostic);
    }

    /// <summary>ReadAll's chain, then a whole-glyph-domain retain-GIDs rebuild and an
    /// in-memory sfnt emission — the identity-domain roundtrip ("serialize").</summary>
    public static long EmitRoundtrip(FontInput input) =>
        RunSubset(input, mode: SubsetMode.RetainGids, root: SubsetRoot(input.GidsAll));

    /// <summary>SubsetOperation, ExplicitGlyphSet root = first min(512, numGlyphs) GIDs, retain-GIDs.</summary>
    public static long SubsetRetain(FontInput input) =>
        RunSubset(input, mode: SubsetMode.RetainGids, root: SubsetRoot(input.Gids512));

    /// <summary>SubsetOperation, same root, compact-remap (the full typed rebuild column).</summary>
    public static long SubsetCompact(FontInput input) =>
        RunSubset(input, mode: SubsetMode.CompactRemap, root: SubsetRoot(input.Gids512));

    /// <summary>SubsetOperation, Unicode root derived from the typed cmap read, retain-GIDs.</summary>
    public static long SubsetCodepoints(FontInput input) =>
        RunSubset(input, mode: SubsetMode.RetainGids, root: new SubsetGlyphRoot
        {
            Kind = SubsetRootKind.UnicodeCodepoints,
            Codepoints = input.Codepoints,
        });

    /// <summary>One representative name-record edit plus re-emission (the CLI's edit-rebuild).</summary>
    public static long EditAndEmit(FontInput input)
    {
        if (input.EditOrdinal < 0) return Refused(ReadStatus.InvalidFormat, default);
        var plan = new NameEditPlan
        {
            Replacements = [new NameReplacement(input.EditOrdinal, CorpusLoader.EditText)],
        };
        ReadStatus status = OpenTypedFace(input, out FontFaceRef face);
        if (status != ReadStatus.Done) return Refused(status, default);
        status = MetadataEditOperation.TryPrepare(face, plan, out var compiled, out var diagnostic, EditOptions, input.Limits);
        if (status != ReadStatus.Done || compiled is null) return Refused(status, diagnostic);
        return CopyToBuffer(compiled);
    }

    // ---- TTC scenarios (TtcInput; the typed operation chain refuses collection
    // faces by design, so the split scenarios use the legacy write chain — the same
    // chain the CLI's name-replace command runs over a --face argument.) ----

    /// <summary>Collection open: every face directory is parsed and validated.</summary>
    public static long TtcOpen(TtcInput input)
    {
        FontFile file = FontFile.Parse(input.Bytes, new FontLimits());
        long extent = file.FaceCount;
        for (int i = 0; i < file.FaceCount; i++)
            foreach (SfntTable table in file.GetFace(i).Tables)
                extent += table.Length;
        return extent;
    }

    /// <summary>Face 0 repackaged as a standalone sfnt buffer (TTC split).</summary>
    public static long TtcSplitFace0(TtcInput input) => TtcSplit(input, 0);

    /// <summary>Face 1 repackaged as a standalone sfnt buffer (TTC split).</summary>
    public static long TtcSplitFace1(TtcInput input) => TtcSplit(input, 1);

    private static long TtcSplit(TtcInput input, int faceIndex)
    {
        if (faceIndex >= input.FaceCount || input.EditOrdinals[faceIndex] < 0)
            return Refused(ReadStatus.InvalidIndex, default);
        FontFile file = FontFile.Parse(input.Bytes, new FontLimits());
        FontFace face = file.GetFace(faceIndex);
        NameTable names = NameTable.Parse(face.GetTable("name").Data, new FontLimits());
        byte[] newName = NameEditor.Replace(names, input.EditOrdinals[faceIndex], CorpusLoader.EditPayloadUtf16Be());
        var plan = SfntWriter.Prepare(face, newName, new FontWriteOptions
        {
            DropSignature = true,
            // The legacy write chain's enum (FontWeave.UnknownTablePolicy), kept
            // qualified: FontWeave.Operations carries a different UnknownTablePolicy.
            UnknownTablePolicy = global::FontWeave.UnknownTablePolicy.PreserveExplicit,
        });
        byte[] buffer = new byte[plan.OutputLength];
        plan.CopyTo(buffer);
        return plan.OutputLength;
    }

    // ---- shared plumbing ----

    private static SubsetGlyphRoot SubsetRoot(ushort[] gids) => new()
    {
        Kind = SubsetRootKind.ExplicitGlyphSet,
        Gids = gids,
    };

    private static long RunSubset(FontInput input, SubsetMode mode, SubsetGlyphRoot root)
    {
        ReadStatus status = OpenTypedFace(input, out FontFaceRef face);
        if (status != ReadStatus.Done) return Refused(status, default);
        var request = new SubsetRequest
        {
            Face = face,
            Root = root,
            Mode = mode,
            Options = SharedSubsetOptions,
        };
        status = SubsetOperation.TryPrepare(request, out var prepared, out _, out var diagnostic, input.Limits);
        if (status != ReadStatus.Done || prepared is null) return Refused(status, diagnostic);
        return CopyToBuffer(prepared.Font);
    }

    private static ReadStatus OpenTypedFace(FontInput input, out FontFaceRef face)
    {
        face = default;
        var limits = input.Limits;
        ReadStatus status = FontSource.TryCopy(input.Bytes, out FontSource? source, out _, limits.MaxInputBytes);
        if (status != ReadStatus.Done) return status;
        return source!.TryGetFace(0, out face, out _);
    }

    /// <summary>The CLI's output-to-memory shape: an uninitialized buffer sized to the
    /// prepared output, filled by the compiled font's TryCopyTo.</summary>
    private static long CopyToBuffer(CompiledFont font)
    {
        byte[] buffer = GC.AllocateUninitializedArray<byte>(font.OutputLength);
        ReadStatus status = font.TryCopyTo(buffer, out int written, out var diagnostic);
        return status == ReadStatus.Done ? written : Refused(status, diagnostic);
    }
}
