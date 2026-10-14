using FontWeave.Core;

namespace FontWeave.Tables;

/// <summary>Rewrite of one simple glyph's points. <paramref name="PointOrdinal"/> indexes
/// the decoded point array of that glyph. Contours are preserved, so the point count may
/// not change; the coordinates are given as absolute design units.</summary>
public readonly record struct GlyphPointEdit(int PointOrdinal, int X, int Y, bool? OnCurve = null);

/// <summary>One glyph's edit: point coordinates, flag bytes, and an opaque instruction
/// payload replacement. All three are optional and address the same glyph.</summary>
public sealed record GlyphEdit
{
    public required int GlyphId { get; init; }
    public IReadOnlyList<GlyphPointEdit> Points { get; init; } = [];

    /// <summary>New flag byte per point ordinal. The point count may not change.</summary>
    public IReadOnlyDictionary<int, byte> Flags { get; init; } = new Dictionary<int, byte>();

    /// <summary>Replacement instruction payload; never interpreted as a program.</summary>
    public byte[]? Instructions { get; init; }
}

/// <summary>A rewrite of one existing component of a composite glyph. Every field is
/// optional; omitted fields keep their current value.</summary>
public sealed record GlyfComponentEdit
{
    public required int Ordinal { get; init; }

    /// <summary>Replacement transform, including its encoding kind. Switching kind is
    /// how scale, x-and-y scale, 2x2 and no-transform are exchanged.</summary>
    public GlyfTransform? Transform { get; init; }

    /// <summary>Replacement xy offset. Only meaningful when the component carries xy
    /// values, which this slice requires of every component.</summary>
    public int? XOffset { get; init; }

    public int? YOffset { get; init; }

    /// <summary>Replacement component reference.</summary>
    public ushort? GlyphIndex { get; init; }

    public bool? UseMyMetrics { get; init; }
}

/// <summary>A component appended to a composite glyph.</summary>
public sealed record GlyfNewComponent
{
    public required ushort GlyphIndex { get; init; }
    public int XOffset { get; init; }
    public int YOffset { get; init; }
    public GlyfTransform Transform { get; init; } = GlyfTransform.Identity;
    public bool UseMyMetrics { get; init; }
}

/// <summary>One composite glyph's edit: component rewrites, appended components, removed
/// components, and an opaque instruction payload replacement.</summary>
public sealed record GlyfCompositeEdit
{
    public required int GlyphId { get; init; }
    public IReadOnlyList<GlyfComponentEdit> Components { get; init; } = [];
    public IReadOnlyList<GlyfNewComponent> AddComponents { get; init; } = [];

    /// <summary>Component ordinals to drop, applied against the original sequence.</summary>
    public IReadOnlyList<int> RemoveComponents { get; init; } = [];

    /// <summary>Replacement instruction payload; never interpreted as a program.</summary>
    public byte[]? Instructions { get; init; }
}

public sealed record GlyfEditPlan
{
    public required IReadOnlyList<GlyphEdit> Glyphs { get; init; }
    public IReadOnlyList<GlyfCompositeEdit> Composites { get; init; } = [];
}

/// <summary>Compiles glyf contents back to bytes.
///
/// Canonical form preserves every unedited glyph byte-for-byte and re-encodes only edited
/// glyphs. Trailing padding is dropped, because it is not part of the decoded structure;
/// that is deterministic and changes no decoded value. The output offsets are returned
/// alongside the bytes so the caller can rebuild loca; this compiler never writes loca,
/// head, or maxp.</summary>
public static class GlyfTableCompiler
{
    public static ReadStatus TryCompile(GlyfTable model, out byte[]? output,
        out FontDiagnostic diagnostic, BuildLimits? limits = null) =>
        TryCompile(model, out output, out diagnostic, new BuildBudget(limits));

    internal static ReadStatus TryCompile(GlyfTable model, out byte[]? output,
        out FontDiagnostic diagnostic, BuildBudget budget)
    {
        ArgumentNullException.ThrowIfNull(model);
        output = null;
        int length = model.OriginalBytes.Length;
        if (!budget.Work(length, model.Location, out diagnostic) ||
            !budget.Output(length, model.Location, out diagnostic) ||
            !budget.Buffer(length, model.Location, out diagnostic))
            return ReadStatus.ResourceLimit;
        byte[] bytes = model.OriginalBytes.ToArray();
        budget.RecordCompiledTable(length);
        output = bytes;
        diagnostic = default;
        return ReadStatus.Done;
    }

    public static ReadStatus TryWrite(GlyfTable model, Span<byte> destination,
        out FontDiagnostic diagnostic)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (destination.Length < model.OriginalBytes.Length)
            return ReadWire.Fail(ReadStatus.DestinationTooSmall, DiagnosticCode.DestinationTooSmall,
                model.Location.At(DataField.OutputBytes, 0), out diagnostic,
                (ulong)destination.Length, (ulong)model.OriginalBytes.Length);
        model.OriginalBytes.CopyTo(destination);
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>Applies an edit plan and writes the rebuilt image into a caller-supplied
    /// destination. Nothing is written unless the whole image fits, so a refused write
    /// leaves the destination untouched rather than partially overwritten.</summary>
    public static ReadStatus TryApplyTo(GlyfTable model, GlyfEditPlan edit,
        Span<byte> destination, out int written, out FontDiagnostic diagnostic,
        BuildLimits? limits = null)
    {
        written = 0;
        ReadStatus status = TryApply(model, edit, out var image, out _, out diagnostic, limits);
        if (status != ReadStatus.Done) return status;
        if (destination.Length < image!.Length)
            return ReadWire.Fail(ReadStatus.DestinationTooSmall, DiagnosticCode.DestinationTooSmall,
                model.Location.At(DataField.OutputBytes, 0), out diagnostic,
                (ulong)destination.Length, (ulong)image.Length);
        image.CopyTo(destination);
        written = image.Length;
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>Applies an edit plan and rebuilds the glyf image with the loca offsets that
    /// describe it. Empty-glyph boundaries and the glyph count are invariant.</summary>
    public static ReadStatus TryApply(GlyfTable model, GlyfEditPlan edit, out byte[]? glyf,
        out uint[]? locaOffsets, out FontDiagnostic diagnostic, BuildLimits? limits = null) =>
        TryApply(model, edit, out glyf, out locaOffsets, out diagnostic, new BuildBudget(limits));

    internal static ReadStatus TryApply(GlyfTable model, GlyfEditPlan edit, out byte[]? glyf,
        out uint[]? locaOffsets, out FontDiagnostic diagnostic, BuildBudget budget)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(edit);
        ArgumentNullException.ThrowIfNull(edit.Glyphs);
        glyf = null;
        locaOffsets = null;
        if (edit.Glyphs.Count == 0 && edit.Composites.Count == 0)
            return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
                model.Location.At(DataField.Payload, 0), out diagnostic);
        if (!budget.Work((long)edit.Glyphs.Count + edit.Composites.Count,
            model.Location, out diagnostic))
            return ReadStatus.ResourceLimit;
        var plans = new Dictionary<int, GlyphEdit>();
        var compositePlans = new Dictionary<int, GlyfCompositeEdit>();
        foreach (var plan in edit.Glyphs)
        {
            ArgumentNullException.ThrowIfNull(plan);
            if (plan.GlyphId < 0 || plan.GlyphId >= model.NumGlyphs)
                return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                    model.Location.At(DataField.GlyphId, 0, plan.GlyphId), out diagnostic,
                    unchecked((ulong)(long)plan.GlyphId), (ulong)model.NumGlyphs);
            if (plans.ContainsKey(plan.GlyphId))
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.DuplicateGlyph,
                    model.Location.At(DataField.GlyphId, 0, plan.GlyphId), out diagnostic,
                    unchecked((ulong)(long)plan.GlyphId));
            plans[plan.GlyphId] = plan;
        }
        foreach (var plan in edit.Composites)
        {
            ArgumentNullException.ThrowIfNull(plan);
            if (plan.GlyphId < 0 || plan.GlyphId >= model.NumGlyphs)
                return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                    model.Location.At(DataField.GlyphId, 0, plan.GlyphId), out diagnostic,
                    unchecked((ulong)(long)plan.GlyphId), (ulong)model.NumGlyphs);
            if (plans.ContainsKey(plan.GlyphId))
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.DuplicateGlyph,
                    model.Location.At(DataField.GlyphId, 0, plan.GlyphId), out diagnostic,
                    unchecked((ulong)(long)plan.GlyphId));
            compositePlans[plan.GlyphId] = plan;
        }
        // Encode every edited glyph first so a failure leaves no partial output.
        var encoded = new Dictionary<int, byte[]>();
        var editedComposites = new Dictionary<int, GlyfCompositeGlyph>();
        foreach (var pair in plans)
        {
            ReadStatus status = model.TryGetGlyph(pair.Key, out var glyph, out diagnostic);
            if (status != ReadStatus.Done) return status;
            if (glyph!.Kind != GlyfGlyphKind.Simple)
                return ReadWire.Fail(ReadStatus.UnsupportedFormat,
                    DiagnosticCode.UnsupportedOperation,
                    model.Location.At(DataField.GlyphId, 0, pair.Key), out diagnostic,
                    unchecked((ulong)(long)pair.Key), (ulong)glyph.Kind);
            status = EncodeSimple(glyph, pair.Value, out byte[]? record, out diagnostic,
                budget, model.Location);
            if (status != ReadStatus.Done) return status;
            encoded[pair.Key] = record!;
        }
        foreach (var pair in compositePlans)
        {
            ReadStatus status = model.TryGetGlyph(pair.Key, out var glyph, out diagnostic);
            if (status != ReadStatus.Done) return status;
            if (glyph!.Kind != GlyfGlyphKind.Composite || glyph.Composite is null)
                return ReadWire.Fail(ReadStatus.UnsupportedFormat,
                    DiagnosticCode.UnsupportedOperation,
                    model.Location.At(DataField.GlyphId, 0, pair.Key), out diagnostic,
                    unchecked((ulong)(long)pair.Key), (ulong)glyph.Kind);
            status = TryEditComposite(glyph, pair.Value, model.NumGlyphs,
                out var editedComposite, out diagnostic, budget, model.Location);
            if (status != ReadStatus.Done) return status;
            status = EncodeComposite(editedComposite!, out byte[]? record, out diagnostic,
                budget, model.Location);
            if (status != ReadStatus.Done) return status;
            encoded[pair.Key] = record!;
            editedComposites[pair.Key] = editedComposite!;
        }
        if (!budget.Work(model.NumGlyphs, model.Location, out diagnostic))
            return ReadStatus.ResourceLimit;
        long total = 0;
        for (int gid = 0; gid < model.NumGlyphs; gid++)
        {
            _ = model.TryGetGlyph(gid, out var glyph, out _);
            total += encoded.TryGetValue(gid, out var record) ? record.Length : glyph!.Length;
        }
        if (total > int.MaxValue)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                model.Location.At(DataField.Length, 0), out diagnostic,
                unchecked((ulong)total), int.MaxValue);
        if (!budget.Work(total + model.NumGlyphs, model.Location, out diagnostic) ||
            !budget.Output(total, model.Location, out diagnostic) ||
            !budget.Buffer(total + 4L * (model.NumGlyphs + 1), model.Location, out diagnostic))
            return ReadStatus.ResourceLimit;
        var glyfImage = new byte[(int)total];
        var offsets = new uint[model.NumGlyphs + 1];
        int imagePosition = 0;
        for (int gid = 0; gid < model.NumGlyphs; gid++)
        {
            _ = model.TryGetGlyph(gid, out var glyph, out _);
            offsets[gid] = (uint)imagePosition;
            if (encoded.TryGetValue(gid, out var record))
            {
                record.CopyTo(glyfImage.AsSpan(imagePosition));
                imagePosition += record.Length;
            }
            else if (!glyph!.IsEmpty)
            {
                int length = (int)glyph.Length;
                model.OriginalBytes.Slice((int)glyph.Start, length)
                    .CopyTo(glyfImage.AsSpan(imagePosition));
                imagePosition += length;
            }
        }
        offsets[model.NumGlyphs] = (uint)imagePosition;
        // The component graph must be re-verified on the edited glyphs. Component-level edits
        // can introduce a cycle or exceed the depth budget while every individual reference
        // stays inside the glyph domain, and the reader refuses such a table — so emitting it
        // here would produce a font that this library cannot read back. The graph builder only
        // consults component references, so the edited composites are substituted into a
        // candidate model and checked with the same verifier the reading path uses.
        if (compositePlans.Count > 0)
        {
            if (!budget.Work(model.NumGlyphs, model.Location, out diagnostic) ||
                !budget.Buffer((long)IntPtr.Size * model.NumGlyphs,
                    model.Location, out diagnostic))
                return ReadStatus.ResourceLimit;
            var checkedGlyphs = new GlyfGlyph?[model.NumGlyphs];
            for (int gid = 0; gid < checkedGlyphs.Length; gid++)
            {
                _ = model.TryGetGlyph(gid, out var existing, out _);
                if (existing is null) continue;
                if (!editedComposites.TryGetValue(gid, out var editedComposite))
                {
                    checkedGlyphs[gid] = existing;
                    continue;
                }
                checkedGlyphs[gid] = new GlyfGlyph(existing.GlyphId, existing.Header,
                    existing.Kind, existing.Start, existing.End, null, editedComposite);
            }
            var candidate = new GlyfTable(checkedGlyphs, glyfImage, model.Location,
                model.LocaLocation);
            ReadStatus graphStatus = new ComponentGraphBuilder(candidate)
                .TryBuild(out _, out diagnostic, budget);
            if (graphStatus != ReadStatus.Done) return graphStatus;
        }
        glyf = glyfImage;
        locaOffsets = offsets;
        budget.RecordCompiledTable(glyf.Length);
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>Retain-GIDs subset splice (S1a card §4). Retained slots copy their source
    /// window byte-for-byte — instructions, composite structure and trailing padding
    /// included — and unselected slots become empty glyphs without compressing or shifting
    /// anything. Short-format images keep every offset even by padding a slot with at most
    /// one zero tail byte when the running offset would turn odd (a source window can end
    /// odd only after instruction stripping re-encodes it; long format needs no evenness).
    /// With <paramref name="dropHinting"/> the retained simple and composite glyphs are
    /// re-encoded without their instruction payloads while every other slot stays a byte
    /// copy. The rebuilt image is graph-verified before it is published, and the returned
    /// offsets feed loca's TryFromOffsets.</summary>
    internal static ReadStatus TryRetainSubset(GlyfTable model, ReadOnlySpan<bool> retained,
        bool shortFormat, bool dropHinting, BuildBudget budget, out byte[]? glyf,
        out uint[]? locaOffsets, out FontDiagnostic diagnostic)
    {
        glyf = null;
        locaOffsets = null;
        if (retained.Length != model.NumGlyphs)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                model.Location.At(DataField.Count, 0), out diagnostic,
                (ulong)retained.Length, (ulong)model.NumGlyphs);
        // Encode every re-encoded glyph up front so a failure leaves no partial output;
        // a plain copy needs nothing here.
        var encoded = new Dictionary<int, byte[]>();
        if (dropHinting)
        {
            if (!budget.Work(model.NumGlyphs, model.Location, out diagnostic))
                return ReadStatus.ResourceLimit;
            for (int gid = 0; gid < model.NumGlyphs; gid++)
            {
                if (!retained[gid]) continue;
                _ = model.TryGetGlyph(gid, out var glyph, out diagnostic);
                if (glyph is null || glyph.IsEmpty) continue;
                byte[]? record;
                if (glyph.Kind == GlyfGlyphKind.Simple)
                {
                    var plan = new GlyphEdit { GlyphId = gid, Instructions = [] };
                    ReadStatus status = EncodeSimple(glyph, plan, out record, out diagnostic,
                        budget, model.Location);
                    if (status != ReadStatus.Done) return status;
                }
                else
                {
                    var plan = new GlyfCompositeEdit { GlyphId = gid, Instructions = [] };
                    ReadStatus status = TryEditComposite(glyph, plan, model.NumGlyphs,
                        out var edited, out diagnostic, budget, model.Location);
                    if (status != ReadStatus.Done) return status;
                    status = EncodeComposite(edited!, out record, out diagnostic, budget,
                        model.Location);
                    if (status != ReadStatus.Done) return status;
                }
                encoded[gid] = record!;
            }
        }
        // Size the image: source window lengths for copies, encoded lengths for stripped
        // glyphs, plus the single evenness pad per slot the short format may need.
        if (!budget.Work(model.NumGlyphs, model.Location, out diagnostic))
            return ReadStatus.ResourceLimit;
        long total = 0;
        for (int gid = 0; gid < model.NumGlyphs; gid++)
        {
            if (!retained[gid]) continue;
            long length = encoded.TryGetValue(gid, out var record) ? record.Length
                : SlotLength(model, gid);
            if (shortFormat) length = (length + 1) & ~1L;
            total += length;
        }
        if (total > int.MaxValue)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                model.Location.At(DataField.Length, 0), out diagnostic,
                (ulong)total, int.MaxValue);
        if (!budget.Work(total + model.NumGlyphs, model.Location, out diagnostic) ||
            !budget.Output(total, model.Location, out diagnostic) ||
            !budget.Buffer(total + 4L * (model.NumGlyphs + 1), model.Location, out diagnostic))
            return ReadStatus.ResourceLimit;
        var image = new byte[(int)total];
        var offsets = new uint[model.NumGlyphs + 1];
        int position = 0;
        for (int gid = 0; gid < model.NumGlyphs; gid++)
        {
            offsets[gid] = (uint)position;
            if (!retained[gid]) continue;
            if (encoded.TryGetValue(gid, out var record))
            {
                record.CopyTo(image.AsSpan(position));
                position += record.Length;
            }
            else
            {
                _ = model.TryGetGlyph(gid, out var glyph, out _);
                if (glyph is { } present && !present.IsEmpty)
                {
                    int length = (int)present.Length;
                    model.OriginalBytes.Slice((int)present.Start, length)
                        .CopyTo(image.AsSpan(position));
                    position += length;
                }
            }
            // Short format: keep the next offset even with at most one zero tail byte.
            // The read side accepts all-zero trailing bytes, so the padded slot stays a
            // legal record and the full-selection image stays byte-identical whenever
            // every source window was already even.
            if (shortFormat && (position & 1) != 0) image[position++] = 0;
        }
        offsets[model.NumGlyphs] = (uint)position;
        // Re-verify the component graph on the rebuilt image: the selected subgraph is a
        // subgraph of the verified input, so this is a cheap read-back safety net, not a
        // new control rule (empty slots add no edges).
        if (!budget.Work(model.NumGlyphs, model.Location, out diagnostic) ||
            !budget.Buffer((long)IntPtr.Size * model.NumGlyphs, model.Location, out diagnostic))
            return ReadStatus.ResourceLimit;
        var glyphs = new GlyfGlyph?[model.NumGlyphs];
        for (int gid = 0; gid < glyphs.Length; gid++)
        {
            _ = model.TryGetGlyph(gid, out var glyph, out _);
            glyphs[gid] = retained[gid] ? glyph : null;
        }
        var candidate = new GlyfTable(glyphs, image, model.Location, model.LocaLocation);
        ReadStatus graphStatus = new ComponentGraphBuilder(candidate)
            .TryBuild(out _, out diagnostic, budget);
        if (graphStatus != ReadStatus.Done) return graphStatus;
        glyf = image;
        locaOffsets = offsets;
        budget.RecordCompiledTable(glyf.Length);
        diagnostic = default;
        return ReadStatus.Done;
    }

    private static long SlotLength(GlyfTable model, int gid)
    {
        _ = model.TryGetGlyph(gid, out var glyph, out _);
        return glyph is { } present && !present.IsEmpty ? present.Length : 0;
    }

    /// <summary>Compact-remap splice (SC card §2): output slot o copies the source
    /// window of input slot <c>outputToInput[o]</c> byte-for-byte — instructions,
    /// trailing padding included — and every slot is selected, so the output domain is
    /// exactly [0, N) with no empty slots. The one byte-level rewrite is the composite
    /// component glyphIndex words, patched in place to their output ordinals via the
    /// decoded component offsets; a component outside the map is a pipeline bug and
    /// refuses. Under dropHinting the retained glyphs re-encode without instructions
    /// through the same encode helpers the retain splice uses. Short-format images pad
    /// at most one zero byte per slot to keep offsets even, and the rebuilt image is
    /// graph-verified over the output domain before it is published.</summary>
    internal static ReadStatus TryCompactSubset(GlyfTable model,
        ReadOnlySpan<ushort> outputToInput, GlyphRemap map, bool shortFormat,
        bool dropHinting, BuildBudget budget, out byte[]? glyf, out uint[]? locaOffsets,
        out FontDiagnostic diagnostic)
    {
        glyf = null;
        locaOffsets = null;
        int outputCount = outputToInput.Length;
        if (outputCount < 1 || outputCount > model.NumGlyphs)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                model.Location.At(DataField.Count, 0), out diagnostic,
                (ulong)outputCount, (ulong)model.NumGlyphs);
        var inputGlyphs = new GlyfGlyph?[outputCount];
        for (int o = 0; o < outputCount; o++)
        {
            _ = model.TryGetGlyph(outputToInput[o], out var glyph, out _);
            inputGlyphs[o] = glyph;
        }
        // Encode every re-encoded glyph up front so a failure leaves no partial output;
        // the encode helpers are the retain splice's own, keyed by input slot.
        var encoded = new Dictionary<int, byte[]>();
        if (dropHinting)
        {
            if (!budget.Work(model.NumGlyphs, model.Location, out diagnostic))
                return ReadStatus.ResourceLimit;
            foreach (var glyph in inputGlyphs)
            {
                if (glyph is null || glyph.IsEmpty) continue;
                byte[]? record;
                if (glyph.Kind == GlyfGlyphKind.Simple)
                {
                    var plan = new GlyphEdit { GlyphId = glyph.GlyphId, Instructions = [] };
                    ReadStatus status = EncodeSimple(glyph, plan, out record, out diagnostic,
                        budget, model.Location);
                    if (status != ReadStatus.Done) return status;
                }
                else
                {
                    var plan = new GlyfCompositeEdit { GlyphId = glyph.GlyphId, Instructions = [] };
                    ReadStatus status = TryEditComposite(glyph, plan, model.NumGlyphs,
                        out var edited, out diagnostic, budget, model.Location);
                    if (status != ReadStatus.Done) return status;
                    status = EncodeComposite(edited!, out record, out diagnostic, budget,
                        model.Location);
                    if (status != ReadStatus.Done) return status;
                }
                encoded[glyph.GlyphId] = record!;
            }
        }
        // Size the image over the output slots: source window lengths for copies,
        // encoded lengths for stripped glyphs, plus the short format's evenness pad.
        if (!budget.Work(outputCount, model.Location, out diagnostic))
            return ReadStatus.ResourceLimit;
        long total = 0;
        foreach (var glyph in inputGlyphs)
        {
            long length = 0;
            if (glyph is { } present && !present.IsEmpty)
                length = encoded.TryGetValue(present.GlyphId, out var record)
                    ? record.Length : present.Length;
            if (shortFormat) length = (length + 1) & ~1L;
            total += length;
        }
        if (total > int.MaxValue)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                model.Location.At(DataField.Length, 0), out diagnostic,
                (ulong)total, int.MaxValue);
        if (!budget.Work(total + outputCount, model.Location, out diagnostic) ||
            !budget.Output(total, model.Location, out diagnostic) ||
            !budget.Buffer(total + 4L * (outputCount + 1), model.Location, out diagnostic))
            return ReadStatus.ResourceLimit;
        var image = new byte[(int)total];
        var offsets = new uint[outputCount + 1];
        int position = 0;
        for (int o = 0; o < outputCount; o++)
        {
            offsets[o] = (uint)position;
            var glyph = inputGlyphs[o];
            if (glyph is not { } present || present.IsEmpty) continue;
            if (encoded.TryGetValue(present.GlyphId, out var record))
            {
                record.CopyTo(image.AsSpan(position));
                position += record.Length;
            }
            else
            {
                int length = (int)present.Length;
                model.OriginalBytes.Slice((int)present.Start, length)
                    .CopyTo(image.AsSpan(position));
                if (present.Composite is { } composite && !PatchComponentIndices(
                        image.AsSpan(position, length), composite, map, model.Location,
                        out diagnostic))
                    return ReadStatus.InvalidFormat;
                position += length;
            }
            // Short format: keep the next offset even with at most one zero tail byte,
            // the same relation the retain splice maintains.
            if (shortFormat && (position & 1) != 0) image[position++] = 0;
        }
        offsets[outputCount] = (uint)position;
        // Graph-verify the rebuilt image over the output domain: glyphs re-keyed to
        // their output ordinals with remapped component references, so the verified
        // graph is exactly the image's own (a subgraph of the verified input's).
        if (!budget.Work(outputCount, model.Location, out diagnostic) ||
            !budget.Buffer((long)IntPtr.Size * outputCount, model.Location, out diagnostic))
            return ReadStatus.ResourceLimit;
        var outputGlyphs = new GlyfGlyph?[outputCount];
        for (int o = 0; o < outputCount; o++)
        {
            var glyph = inputGlyphs[o];
            if (glyph is null || glyph.IsEmpty)
            {
                outputGlyphs[o] = glyph;
                continue;
            }
            var present = glyph;
            if (present.Composite is { } composite)
            {
                var components = new GlyfComponent[composite.ComponentCount];
                for (int i = 0; i < components.Length; i++)
                {
                    var component = composite[i];
                    int output = map.OutputOf(component.GlyphIndex);
                    if (output < 0)
                        return ReadWire.Fail(ReadStatus.InvalidFormat,
                            DiagnosticCode.InvalidValue,
                            model.Location.At(DataField.GlyphId, 0, component.GlyphIndex),
                            out diagnostic, (ulong)component.GlyphIndex,
                            (ulong)model.NumGlyphs);
                    components[i] = new(component.Ordinal, component.Flags,
                        checked((ushort)output), component.Arg1, component.Arg2,
                        component.Transform);
                }
                var remapped = new GlyfCompositeGlyph(o, present.Header, components,
                    composite.Instructions.ToArray(), composite.ConsumedLength, 0);
                outputGlyphs[o] = new(o, present.Header, GlyfGlyphKind.Composite, 0,
                    (uint)present.Length, null, remapped);
            }
            else
            {
                outputGlyphs[o] = new(o, present.Header, present.Kind, 0,
                    (uint)present.Length, present.Simple);
            }
        }
        var candidate = new GlyfTable(outputGlyphs, image, model.Location, model.LocaLocation);
        ReadStatus graphStatus = new ComponentGraphBuilder(candidate)
            .TryBuild(out _, out diagnostic, budget);
        if (graphStatus != ReadStatus.Done) return graphStatus;
        glyf = image;
        locaOffsets = offsets;
        budget.RecordCompiledTable(glyf.Length);
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>Rewrites the composite's component glyphIndex words in place: the splice
    /// is otherwise a byte copy, so only these two-byte fields carry the remap.</summary>
    private static bool PatchComponentIndices(Span<byte> window, GlyfCompositeGlyph composite,
        GlyphRemap map, DataLocation location, out FontDiagnostic diagnostic)
    {
        diagnostic = default;
        var offsets = composite.ComponentIndexOffsets;
        for (int i = 0; i < composite.ComponentCount; i++)
        {
            int input = composite[i].GlyphIndex;
            int output = map.OutputOf(input);
            if (output is < 0 or > ushort.MaxValue)
            {
                diagnostic = new(DiagnosticCode.InvalidValue,
                    location.At(DataField.GlyphId, 0, input), (ulong)input, (ulong)output);
                return false;
            }
            int at = offsets[i];
            // Byte splitting a GID is reinterpretation, not arithmetic: unchecked keeps
            // the checked-arithmetic build from refusing outputs >= 256.
            window[at] = unchecked((byte)(output >> 8));
            window[at + 1] = unchecked((byte)output);
        }
        return true;
    }

    /// <summary>Re-encodes one simple glyph. Flag and coordinate encodings are chosen
    /// deterministically from the current values; the point count is invariant.</summary>
    internal static ReadStatus EncodeSimple(GlyfGlyph glyph, GlyphEdit plan, out byte[]? record,
        out FontDiagnostic diagnostic, BuildBudget budget, DataLocation location)
    {
        record = null;
        SimpleGlyph simple = glyph.Simple!;
        int count = simple.PointCount;
        ReadOnlySpan<byte> instructions = plan.Instructions is { } replacement
            ? replacement : simple.Instructions;
        if (instructions.Length > ushort.MaxValue)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                DataLocation.Anonymous(GlyfLayout.Tag).At(DataField.Length, 0), out diagnostic,
                (ulong)instructions.Length, ushort.MaxValue);
        // Logical array payload: one flag byte, a point (two ints and a bool), and two
        // int delta values per point. This is cumulative admission, not CLR layout or RSS.
        if (!budget.Work(8L * count + simple.ContourCount + plan.Flags.Count +
                plan.Points.Count, location, out diagnostic) ||
            !budget.Buffer(18L * count, location, out diagnostic))
            return ReadStatus.ResourceLimit;
        byte[] flags = simple.Flags.ToArray();
        if (plan.Flags.Count > 0)
        {
            foreach (var pair in plan.Flags)
            {
                if (pair.Key < 0 || pair.Key >= count)
                    return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                        DataLocation.Anonymous(GlyfLayout.Tag).At(DataField.GlyphId, 0, pair.Key),
                        out diagnostic, unchecked((ulong)(long)pair.Key), (ulong)count);
                if ((pair.Value & GlyfLayout.ReservedFlag) != 0)
                    return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                        DataLocation.Anonymous(GlyfLayout.Tag).At(DataField.Flags, 0, pair.Key),
                        out diagnostic, pair.Value, GlyfLayout.ReservedFlag);
                flags[pair.Key] = pair.Value;
            }
        }
        var points = simple.Points.ToArray();
        foreach (var item in plan.Points)
        {
            if (item.PointOrdinal < 0 || item.PointOrdinal >= count)
                return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                    DataLocation.Anonymous(GlyfLayout.Tag).At(DataField.GlyphId, 0,
                        item.PointOrdinal), out diagnostic,
                    unchecked((ulong)(long)item.PointOrdinal), (ulong)count);
            if (item.X is < short.MinValue or > short.MaxValue ||
                item.Y is < short.MinValue or > short.MaxValue)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    DataLocation.Anonymous(GlyfLayout.Tag).At(DataField.Payload, 0,
                        item.PointOrdinal), out diagnostic,
                    unchecked((ulong)(long)Math.Max(item.X, item.Y)), (ulong)short.MaxValue);
            points[item.PointOrdinal] = new(item.X, item.Y,
                item.OnCurve ?? points[item.PointOrdinal].OnCurve);
            // The flag byte is rebuilt from the point model below, so an edited OnCurve has
            // to reach the retained bits here or it would be silently dropped.
            if (item.OnCurve is bool onCurve)
                flags[item.PointOrdinal] = onCurve
                    ? (byte)(flags[item.PointOrdinal] | GlyfLayout.OnCurve)
                    : (byte)(flags[item.PointOrdinal] & ~GlyfLayout.OnCurve);
        }
        // Inter-point deltas, then choose the cheapest legal encoding per axis. A delta is
        // written as a signed 16-bit value, so it must fit one: two points that each sit
        // inside the int16 domain can still be 65535 apart, and truncating that would emit
        // a contour that no longer matches the request.
        var dx = new int[count];
        var dy = new int[count];
        int px = 0, py = 0;
        for (int i = 0; i < count; i++)
        {
            dx[i] = points[i].X - px;
            dy[i] = points[i].Y - py;
            px = points[i].X;
            py = points[i].Y;
            if (DeltaFits(dx[i]) && DeltaFits(dy[i])) continue;
            bool xAxis = !DeltaFits(dx[i]);
            int offending = xAxis ? dx[i] : dy[i];
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                DataLocation.Anonymous(GlyfLayout.Tag).At(DataField.Payload, 0, i),
                out diagnostic, unchecked((ulong)(long)offending),
                (ulong)short.MaxValue);
        }
        // Rebuild each flag from the retained semantic bits plus the cheapest legal
        // encoding of this point's delta. Bits that describe the coordinate stream are
        // fully determined by the values, so they are recomputed rather than preserved.
        int coordinateBytes = 0;
        for (int i = 0; i < count; i++)
        {
            byte flag = (byte)(flags[i] & (GlyfLayout.OnCurve | GlyfLayout.OverlapSimple));
            if (dx[i] == 0) flag |= GlyfLayout.XSameOrPositive;
            else if (dx[i] is >= -255 and <= 255)
                flag |= (byte)(GlyfLayout.XShort | (dx[i] > 0 ? GlyfLayout.XSameOrPositive : 0));
            if (dy[i] == 0) flag |= GlyfLayout.YSameOrPositive;
            else if (dy[i] is >= -255 and <= 255)
                flag |= (byte)(GlyfLayout.YShort | (dy[i] > 0 ? GlyfLayout.YSameOrPositive : 0));
            flags[i] = flag;
            coordinateBytes += CoordinateLength(flag, GlyfLayout.XShort,
                GlyfLayout.XSameOrPositive) + CoordinateLength(flag, GlyfLayout.YShort,
                GlyfLayout.YSameOrPositive);
        }
        int length = GlyfLayout.HeaderLength + 2 * simple.ContourCount + 2 +
            instructions.Length + count + coordinateBytes;
        if (!budget.Work(length, location, out diagnostic) ||
            !budget.Buffer(length, location, out diagnostic))
            return ReadStatus.ResourceLimit;
        var body = new byte[length];
        int position = 0;
        WriteInt16(body, ref position, glyph.Header.NumberOfContours);
        // The bounding box is derived from the edited points; this slice reports geometry
        // facts rather than preserving a possibly stale box.
        short xMin = 0, yMin = 0, xMax = 0, yMax = 0;
        if (count > 0)
        {
            // Points were range-checked to int16 before reaching here, so the bounding
            // box narrowing cannot lose information.
            xMin = xMax = unchecked((short)points[0].X);
            yMin = yMax = unchecked((short)points[0].Y);
            for (int i = 1; i < count; i++)
            {
                short boxX = unchecked((short)points[i].X);
                short boxY = unchecked((short)points[i].Y);
                xMin = Math.Min(xMin, boxX);
                xMax = Math.Max(xMax, boxX);
                yMin = Math.Min(yMin, boxY);
                yMax = Math.Max(yMax, boxY);
            }
        }
        WriteInt16(body, ref position, xMin);
        WriteInt16(body, ref position, yMin);
        WriteInt16(body, ref position, xMax);
        WriteInt16(body, ref position, yMax);
        foreach (ushort end in simple.EndPointsOfContours)
            WriteUInt16(body, ref position, end);
        WriteUInt16(body, ref position, (ushort)instructions.Length);
        instructions.CopyTo(body.AsSpan(position));
        position += instructions.Length;
        flags.CopyTo(body.AsSpan(position));
        position += flags.Length;
        for (int i = 0; i < count; i++)
            WriteCoordinate(body, ref position, flags[i], dx[i], GlyfLayout.XShort,
                GlyfLayout.XSameOrPositive);
        for (int i = 0; i < count; i++)
            WriteCoordinate(body, ref position, flags[i], dy[i], GlyfLayout.YShort,
                GlyfLayout.YSameOrPositive);
        record = body;
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>Whether an inter-point delta can be written on the wire. Short form covers
    /// -255..255; otherwise the value is stored as a signed 16-bit.</summary>
    private static bool DeltaFits(int delta) => delta is >= short.MinValue and <= short.MaxValue;

    private static int CoordinateLength(byte flag, byte shortBit, byte sameBit) =>
        (flag & shortBit) != 0 ? 1 : (flag & sameBit) == 0 ? 2 : 0;

    private static void WriteCoordinate(Span<byte> body, ref int position, byte flag,
        int delta, byte shortBit, byte sameBit)
    {
        if ((flag & shortBit) != 0) body[position++] = (byte)Math.Abs(delta);
        else if ((flag & sameBit) == 0)
            WriteInt16(body, ref position, unchecked((short)delta));
    }

    private static void WriteInt16(Span<byte> body, ref int position, short value)
    {
        // Splitting a signed value into bytes is reinterpretation, not overflow.
        ushort raw = unchecked((ushort)value);
        body[position++] = unchecked((byte)(raw >> 8));
        body[position++] = unchecked((byte)raw);
    }

    private static void WriteUInt16(Span<byte> body, ref int position, ushort value)
    {
        body[position++] = unchecked((byte)(value >> 8));
        body[position++] = unchecked((byte)value);
    }

    /// <summary>Applies a component-level rewrite and returns an edited copy of the
    /// composite, without encoding it. Component order is preserved, flags are carried
    /// over, and transforms are never merged or optimized.</summary>
    internal static ReadStatus TryEditComposite(GlyfGlyph glyph, GlyfCompositeEdit plan,
        int numGlyphs, out GlyfCompositeGlyph? edited, out FontDiagnostic diagnostic,
        BuildBudget budget, DataLocation location)
    {
        edited = null;
        GlyfCompositeGlyph source = glyph.Composite!;
        int original = source.ComponentCount;
        if (!budget.Work((long)plan.RemoveComponents.Count + plan.Components.Count +
            plan.AddComponents.Count + original, location, out diagnostic))
            return ReadStatus.ResourceLimit;
        var removals = new HashSet<int>();
        foreach (int ordinal in plan.RemoveComponents)
        {
            if (ordinal < 0 || ordinal >= original)
                return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                    DataLocation.Anonymous(GlyfLayout.Tag).At(DataField.Count, 0, glyph.GlyphId),
                    out diagnostic, unchecked((ulong)(long)ordinal), (ulong)original);
            removals.Add(ordinal);
        }
        var edits = new Dictionary<int, GlyfComponentEdit>();
        foreach (var edit in plan.Components)
        {
            ArgumentNullException.ThrowIfNull(edit);
            if (edit.Ordinal < 0 || edit.Ordinal >= original)
                return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                    DataLocation.Anonymous(GlyfLayout.Tag).At(DataField.Count, 0, glyph.GlyphId),
                    out diagnostic, unchecked((ulong)(long)edit.Ordinal), (ulong)original);
            if (removals.Contains(edit.Ordinal))
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    DataLocation.Anonymous(GlyfLayout.Tag).At(DataField.Count, 0, glyph.GlyphId),
                    out diagnostic, unchecked((ulong)(long)edit.Ordinal), 0);
            edits[edit.Ordinal] = edit;
        }
        long componentCount = (long)original - removals.Count + plan.AddComponents.Count;
        if (componentCount == 0)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                DataLocation.Anonymous(GlyfLayout.Tag).At(DataField.Count, 0, glyph.GlyphId),
                out diagnostic, 0, 1);
        if (componentCount > GlyfLayout.MaxComponentsPerGlyph)
            return ReadWire.Fail(ReadStatus.ResourceLimit, DiagnosticCode.BudgetExceeded,
                DataLocation.Anonymous(GlyfLayout.Tag).At(DataField.Count, 0, glyph.GlyphId),
                out diagnostic, (ulong)componentCount, GlyfLayout.MaxComponentsPerGlyph);
        int instructionLength = plan.Instructions?.Length ?? source.Instructions.Length;
        if (instructionLength > ushort.MaxValue)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                DataLocation.Anonymous(GlyfLayout.Tag).At(DataField.Length, 0, glyph.GlyphId),
                out diagnostic, (ulong)instructionLength, ushort.MaxValue);
        int copiedInstructions = plan.Instructions is null ? instructionLength : 0;
        if (!budget.Work(copiedInstructions, location, out diagnostic) ||
            !budget.Buffer((long)IntPtr.Size * componentCount + copiedInstructions,
                location, out diagnostic))
            return ReadStatus.ResourceLimit;
        byte[] instructions = plan.Instructions ?? source.Instructions.ToArray();
        var renumbered = new GlyfComponent[(int)componentCount];
        int position = 0;
        for (int ordinal = 0; ordinal < original; ordinal++)
        {
            if (removals.Contains(ordinal)) continue;
            var current = source[ordinal];
            if (edits.TryGetValue(ordinal, out var edit))
            {
                ReadStatus status = MergeComponent(current, edit, numGlyphs, glyph.GlyphId,
                    out var merged, out diagnostic);
                if (status != ReadStatus.Done) return status;
                current = merged!;
            }
            ushort flags = SequenceFlags(current.Flags, position, renumbered.Length,
                instructionLength);
            renumbered[position] = new(position, flags, current.GlyphIndex, current.Arg1,
                current.Arg2, current.Transform);
            position++;
        }
        foreach (var addition in plan.AddComponents)
        {
            ArgumentNullException.ThrowIfNull(addition);
            if (addition.GlyphIndex >= numGlyphs)
                return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                    DataLocation.Anonymous(GlyfLayout.Tag).At(DataField.GlyphId, 0,
                        addition.GlyphIndex), out diagnostic, addition.GlyphIndex,
                    (ulong)numGlyphs);
            if (addition.XOffset is < short.MinValue or > short.MaxValue ||
                addition.YOffset is < short.MinValue or > short.MaxValue)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    DataLocation.Anonymous(GlyfLayout.Tag).At(DataField.Payload, 0,
                        glyph.GlyphId), out diagnostic,
                    unchecked((ulong)(long)addition.XOffset), (ulong)short.MaxValue);
            ReadStatus status = ValidateTransform(addition.Transform, glyph.GlyphId,
                out diagnostic);
            if (status != ReadStatus.Done) return status;
            ushort flags = GlyfLayout.ArgsAreXyValues;
            if (addition.UseMyMetrics) flags |= GlyfLayout.UseMyMetrics;
            flags |= (ushort)((addition.Transform.Kind) switch
            {
                GlyfTransformKind.Scale => GlyfLayout.WeHaveAScale,
                GlyfTransformKind.XAndYScale => GlyfLayout.WeHaveAnXAndYScale,
                GlyfTransformKind.TwoByTwo => GlyfLayout.WeHaveATwoByTwo,
                _ => 0
            });
            flags = SequenceFlags(flags, position, renumbered.Length, instructionLength);
            renumbered[position] = new(position, flags, addition.GlyphIndex,
                (short)addition.XOffset, (short)addition.YOffset, addition.Transform);
            position++;
        }
        edited = new(glyph.GlyphId, source.Header, renumbered, instructions, 0, glyph.Start);
        diagnostic = default;
        return ReadStatus.Done;
    }

    // Renumber once after removals: only the final component can terminate the sequence
    // and carry instructions. Both encoding and graph verification use this same result.
    private static ushort SequenceFlags(ushort flags, int ordinal, int count,
        int instructionLength)
    {
        flags = (ushort)(ordinal == count - 1
            ? flags & ~GlyfLayout.MoreComponents : flags | GlyfLayout.MoreComponents);
        return (ushort)(instructionLength > 0 && ordinal == count - 1
            ? flags | GlyfLayout.WeHaveInstructions : flags & ~GlyfLayout.WeHaveInstructions);
    }

    private static ReadStatus MergeComponent(GlyfComponent current, GlyfComponentEdit edit,
        int numGlyphs, int glyphId, out GlyfComponent? merged, out FontDiagnostic diagnostic)
    {
        merged = null;
        ushort glyphIndex = current.GlyphIndex;
        if (edit.GlyphIndex is { } replacement)
        {
            if (replacement >= numGlyphs)
                return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                    DataLocation.Anonymous(GlyfLayout.Tag).At(DataField.GlyphId, 0, glyphId),
                    out diagnostic, replacement, (ulong)numGlyphs);
            glyphIndex = replacement;
        }
        int x = edit.XOffset ?? current.Arg1;
        int y = edit.YOffset ?? current.Arg2;
        if (x is < short.MinValue or > short.MaxValue || y is < short.MinValue or > short.MaxValue)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                DataLocation.Anonymous(GlyfLayout.Tag).At(DataField.Payload, 0, glyphId),
                out diagnostic, unchecked((ulong)(long)x), (ulong)short.MaxValue);
        var transform = edit.Transform ?? current.Transform;
        ReadStatus status = ValidateTransform(transform, glyphId, out diagnostic);
        if (status != ReadStatus.Done) return status;
        ushort flags = current.Flags;
        flags = (ushort)(flags & ~(GlyfLayout.WeHaveAScale | GlyfLayout.WeHaveAnXAndYScale |
            GlyfLayout.WeHaveATwoByTwo));
        flags |= (ushort)(transform.Kind switch
        {
            GlyfTransformKind.Scale => GlyfLayout.WeHaveAScale,
            GlyfTransformKind.XAndYScale => GlyfLayout.WeHaveAnXAndYScale,
            GlyfTransformKind.TwoByTwo => GlyfLayout.WeHaveATwoByTwo,
            _ => 0
        });
        if (edit.UseMyMetrics is { } useMyMetrics)
            flags = useMyMetrics
                ? (ushort)(flags | GlyfLayout.UseMyMetrics)
                : (ushort)(flags & ~GlyfLayout.UseMyMetrics);
        merged = new(current.Ordinal, flags, glyphIndex, (short)x, (short)y, transform);
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>A transform is written as raw F2DOT14 values, so any int16 is encodable.
    /// The kind itself must be one of the four defined encodings, and TwoByTwo requires
    /// all four coefficients to be present rather than implied by a uniform or
    /// per-axis scale.</summary>
    private static ReadStatus ValidateTransform(GlyfTransform transform, int glyphId,
        out FontDiagnostic diagnostic)
    {
        if (!Enum.IsDefined(transform.Kind))
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                DataLocation.Anonymous(GlyfLayout.Tag).At(DataField.Flags, 0, glyphId),
                out diagnostic, (ulong)transform.Kind, (ulong)GlyfTransformKind.TwoByTwo);
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>Re-encodes one composite glyph. Component order and flags are preserved;
    /// the argument encoding is chosen from the current values, and the trailing padding
    /// is dropped because it is not part of the decoded structure.</summary>
    internal static ReadStatus EncodeComposite(GlyfCompositeGlyph composite,
        out byte[]? record, out FontDiagnostic diagnostic, BuildBudget budget,
        DataLocation location)
    {
        record = null;
        if (!budget.Work(composite.ComponentCount, location, out diagnostic))
            return ReadStatus.ResourceLimit;
        int length = GlyfLayout.HeaderLength;
        for (int i = 0; i < composite.ComponentCount; i++)
        {
            var component = composite[i];
            bool words = component.Arg1 is < sbyte.MinValue or > sbyte.MaxValue ||
                component.Arg2 is < sbyte.MinValue or > sbyte.MaxValue;
            length += 4 + (words ? 4 : 2) + (component.Transform.Kind switch
            {
                GlyfTransformKind.Scale => 2,
                GlyfTransformKind.XAndYScale => 4,
                GlyfTransformKind.TwoByTwo => 8,
                _ => 0
            });
        }
        if (composite.HasInstructions) length += 2 + composite.Instructions.Length;
        if (!budget.Work(length, location, out diagnostic) ||
            !budget.Buffer(length, location, out diagnostic))
            return ReadStatus.ResourceLimit;
        var body = new byte[length];
        int position = 0;
        WriteInt16(body, ref position, composite.Header.NumberOfContours);
        WriteInt16(body, ref position, composite.Header.XMin);
        WriteInt16(body, ref position, composite.Header.YMin);
        WriteInt16(body, ref position, composite.Header.XMax);
        WriteInt16(body, ref position, composite.Header.YMax);
        for (int i = 0; i < composite.ComponentCount; i++)
        {
            var component = composite[i];
            // A component is only encodable when its arguments are xy values; point
            // matching is out of scope, and such components cannot reach here because
            // decoding rejects them.
            bool words = component.Arg1 is < sbyte.MinValue or > sbyte.MaxValue ||
                component.Arg2 is < sbyte.MinValue or > sbyte.MaxValue;
            ushort flags = component.Flags;
            flags = words
                ? (ushort)(flags | GlyfLayout.Arg1And2AreWords)
                : (ushort)(flags & ~GlyfLayout.Arg1And2AreWords);
            WriteUInt16(body, ref position, flags);
            WriteUInt16(body, ref position, component.GlyphIndex);
            if (words)
            {
                WriteInt16(body, ref position, component.Arg1);
                WriteInt16(body, ref position, component.Arg2);
            }
            else
            {
                body[position++] = unchecked((byte)(sbyte)component.Arg1);
                body[position++] = unchecked((byte)(sbyte)component.Arg2);
            }
            switch (component.Transform.Kind)
            {
                case GlyfTransformKind.Scale:
                    WriteInt16(body, ref position, component.Transform.A);
                    break;
                case GlyfTransformKind.XAndYScale:
                    WriteInt16(body, ref position, component.Transform.A);
                    WriteInt16(body, ref position, component.Transform.D);
                    break;
                case GlyfTransformKind.TwoByTwo:
                    WriteInt16(body, ref position, component.Transform.A);
                    WriteInt16(body, ref position, component.Transform.B);
                    WriteInt16(body, ref position, component.Transform.C);
                    WriteInt16(body, ref position, component.Transform.D);
                    break;
            }
        }
        if (composite.HasInstructions)
        {
            WriteUInt16(body, ref position, (ushort)composite.Instructions.Length);
            composite.Instructions.CopyTo(body.AsSpan(position));
        }
        record = body;
        diagnostic = default;
        return ReadStatus.Done;
    }
}
