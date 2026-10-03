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
        foreach (var pair in plans)
        {
            ReadStatus status = model.TryGetGlyph(pair.Key, out var glyph, out diagnostic);
            if (status != ReadStatus.Done) return status;
            if (glyph!.Kind != GlyfGlyphKind.Simple)
                return ReadWire.Fail(ReadStatus.UnsupportedFormat,
                    DiagnosticCode.UnsupportedOperation,
                    model.Location.At(DataField.GlyphId, 0, pair.Key), out diagnostic,
                    unchecked((ulong)(long)pair.Key), (ulong)glyph.Kind);
            status = EncodeSimple(glyph, pair.Value, out byte[]? record, out diagnostic);
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
            status = EncodeComposite(glyph, pair.Value, model.NumGlyphs, out byte[]? record,
                out diagnostic);
            if (status != ReadStatus.Done) return status;
            encoded[pair.Key] = record!;
        }
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
        if (!budget.Work(4L * total, model.Location, out diagnostic) ||
            !budget.Output(total, model.Location, out diagnostic) ||
            !budget.Buffer(total + 8L * (model.NumGlyphs + 1), model.Location, out diagnostic))
            return ReadStatus.ResourceLimit;
        var image = new List<byte>((int)total);
        var offsets = new uint[model.NumGlyphs + 1];
        for (int gid = 0; gid < model.NumGlyphs; gid++)
        {
            _ = model.TryGetGlyph(gid, out var glyph, out _);
            offsets[gid] = (uint)image.Count;
            if (encoded.TryGetValue(gid, out var record)) image.AddRange(record);
            else if (!glyph!.IsEmpty)
                image.AddRange(model.OriginalBytes.Slice((int)glyph.Start, (int)glyph.Length));
        }
        offsets[model.NumGlyphs] = (uint)image.Count;
        byte[] glyfImage = [.. image];
        // The component graph must be re-verified on the edited glyphs. Component-level edits
        // can introduce a cycle or exceed the depth budget while every individual reference
        // stays inside the glyph domain, and the reader refuses such a table — so emitting it
        // here would produce a font that this library cannot read back. The graph builder only
        // consults component references, so the edited composites are substituted into a
        // candidate model and checked with the same verifier the reading path uses.
        if (compositePlans.Count > 0)
        {
            var checkedGlyphs = new GlyfGlyph?[model.NumGlyphs];
            for (int gid = 0; gid < checkedGlyphs.Length; gid++)
            {
                _ = model.TryGetGlyph(gid, out var existing, out _);
                if (existing is null) continue;
                if (!compositePlans.TryGetValue(gid, out var compositePlan))
                {
                    checkedGlyphs[gid] = existing;
                    continue;
                }
                ReadStatus editStatus = model.TryGetGlyph(gid, out var source, out diagnostic);
                if (editStatus != ReadStatus.Done) return editStatus;
                editStatus = TryEditComposite(source!, compositePlan, model.NumGlyphs,
                    out var editedComposite, out diagnostic);
                if (editStatus != ReadStatus.Done) return editStatus;
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

    /// <summary>Re-encodes one simple glyph. Flag and coordinate encodings are chosen
    /// deterministically from the current values; the point count is invariant.</summary>
    internal static ReadStatus EncodeSimple(GlyfGlyph glyph, GlyphEdit plan, out byte[]? record,
        out FontDiagnostic diagnostic)
    {
        record = null;
        SimpleGlyph simple = glyph.Simple!;
        int count = simple.PointCount;
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
        byte[] instructions = plan.Instructions ?? simple.Instructions.ToArray();
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
        }
        var body = new List<byte>(GlyfLayout.HeaderLength + count * 5 + instructions.Length);
        WriteInt16(body, glyph.Header.NumberOfContours);
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
        WriteInt16(body, xMin);
        WriteInt16(body, yMin);
        WriteInt16(body, xMax);
        WriteInt16(body, yMax);
        foreach (ushort end in simple.EndPointsOfContours) WriteUInt16(body, end);
        if (instructions.Length > ushort.MaxValue)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                DataLocation.Anonymous(GlyfLayout.Tag).At(DataField.Length, 0), out diagnostic,
                (ulong)instructions.Length, ushort.MaxValue);
        WriteUInt16(body, (ushort)instructions.Length);
        body.AddRange(instructions);
        for (int i = 0; i < count; i++) body.Add(flags[i]);
        for (int i = 0; i < count; i++) WriteCoordinate(body, flags[i], dx[i], GlyfLayout.XShort, GlyfLayout.XSameOrPositive);
        for (int i = 0; i < count; i++) WriteCoordinate(body, flags[i], dy[i], GlyfLayout.YShort, GlyfLayout.YSameOrPositive);
        record = [.. body];
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>Whether an inter-point delta can be written on the wire. Short form covers
    /// -255..255; otherwise the value is stored as a signed 16-bit.</summary>
    private static bool DeltaFits(int delta) => delta is >= short.MinValue and <= short.MaxValue;

    private static void WriteCoordinate(List<byte> body, byte flag, int delta, byte shortBit,
        byte sameBit)
    {
        if ((flag & shortBit) != 0) body.Add((byte)Math.Abs(delta));
        else if ((flag & sameBit) == 0) WriteInt16(body, unchecked((short)delta));
    }

    private static void WriteInt16(List<byte> body, short value)
    {
        // Splitting a signed value into bytes is reinterpretation, not overflow.
        ushort raw = unchecked((ushort)value);
        body.Add(unchecked((byte)(raw >> 8)));
        body.Add(unchecked((byte)raw));
    }

    private static void WriteUInt16(List<byte> body, ushort value)
    {
        body.Add(unchecked((byte)(value >> 8)));
        body.Add(unchecked((byte)value));
    }

    /// <summary>Applies a component-level rewrite and returns an edited copy of the
    /// composite, without encoding it. Component order is preserved, flags are carried
    /// over, and transforms are never merged or optimized.</summary>
    internal static ReadStatus TryEditComposite(GlyfGlyph glyph, GlyfCompositeEdit plan,
        int numGlyphs, out GlyfCompositeGlyph? edited, out FontDiagnostic diagnostic)
    {
        edited = null;
        GlyfCompositeGlyph source = glyph.Composite!;
        int original = source.ComponentCount;
        foreach (int ordinal in plan.RemoveComponents)
            if (ordinal < 0 || ordinal >= original)
                return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                    DataLocation.Anonymous(GlyfLayout.Tag).At(DataField.Count, 0, glyph.GlyphId),
                    out diagnostic, unchecked((ulong)(long)ordinal), (ulong)original);
        var removals = new HashSet<int>(plan.RemoveComponents);
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
        var kept = new List<GlyfComponent>(original + plan.AddComponents.Count);
        for (int ordinal = 0; ordinal < original; ordinal++)
        {
            if (removals.Contains(ordinal)) continue;
            var current = source[ordinal];
            if (!edits.TryGetValue(ordinal, out var edit))
            {
                kept.Add(current);
                continue;
            }
            ReadStatus status = MergeComponent(current, edit, numGlyphs, glyph.GlyphId,
                out var merged, out diagnostic);
            if (status != ReadStatus.Done) return status;
            kept.Add(merged!);
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
            kept.Add(new(kept.Count, flags, addition.GlyphIndex, (short)addition.XOffset,
                (short)addition.YOffset, addition.Transform));
        }
        if (kept.Count == 0)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                DataLocation.Anonymous(GlyfLayout.Tag).At(DataField.Count, 0, glyph.GlyphId),
                out diagnostic, 0, 1);
        if (kept.Count > GlyfLayout.MaxComponentsPerGlyph)
            return ReadWire.Fail(ReadStatus.ResourceLimit, DiagnosticCode.BudgetExceeded,
                DataLocation.Anonymous(GlyfLayout.Tag).At(DataField.Count, 0, glyph.GlyphId),
                out diagnostic, (ulong)kept.Count, GlyfLayout.MaxComponentsPerGlyph);
        byte[] instructions = plan.Instructions ?? source.Instructions.ToArray();
        // The sequence is renumbered so ordinal matches position after removals, and
        // MORE_COMPONENTS is always clear on the final record by construction.
        var renumbered = new GlyfComponent[kept.Count];
        for (int i = 0; i < kept.Count; i++)
        {
            var component = kept[i];
            ushort flags = component.Flags;
            flags = (ushort)(i == kept.Count - 1
                ? flags & ~GlyfLayout.MoreComponents
                : flags | GlyfLayout.MoreComponents);
            flags = (ushort)(instructions.Length > 0
                ? (i == kept.Count - 1 ? flags | GlyfLayout.WeHaveInstructions
                    : flags & ~GlyfLayout.WeHaveInstructions)
                : flags & ~GlyfLayout.WeHaveInstructions);
            renumbered[i] = new(i, flags, component.GlyphIndex, component.Arg1,
                component.Arg2, component.Transform);
        }
        edited = new(glyph.GlyphId, source.Header, renumbered, instructions, 0, glyph.Start);
        diagnostic = default;
        return ReadStatus.Done;
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
    internal static ReadStatus EncodeComposite(GlyfGlyph glyph, GlyfCompositeEdit plan,
        int numGlyphs, out byte[]? record, out FontDiagnostic diagnostic)
    {
        record = null;
        ReadStatus status = TryEditComposite(glyph, plan, numGlyphs, out var edited,
            out diagnostic);
        if (status != ReadStatus.Done) return status;
        GlyfCompositeGlyph composite = edited!;
        var body = new List<byte>(GlyfLayout.HeaderLength + 16 * composite.ComponentCount + 4);
        WriteInt16(body, composite.Header.NumberOfContours);
        WriteInt16(body, composite.Header.XMin);
        WriteInt16(body, composite.Header.YMin);
        WriteInt16(body, composite.Header.XMax);
        WriteInt16(body, composite.Header.YMax);
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
            WriteUInt16(body, flags);
            WriteUInt16(body, component.GlyphIndex);
            if (words)
            {
                WriteInt16(body, component.Arg1);
                WriteInt16(body, component.Arg2);
            }
            else
            {
                body.Add(unchecked((byte)(sbyte)component.Arg1));
                body.Add(unchecked((byte)(sbyte)component.Arg2));
            }
            switch (component.Transform.Kind)
            {
                case GlyfTransformKind.Scale:
                    WriteInt16(body, component.Transform.A);
                    break;
                case GlyfTransformKind.XAndYScale:
                    WriteInt16(body, component.Transform.A);
                    WriteInt16(body, component.Transform.D);
                    break;
                case GlyfTransformKind.TwoByTwo:
                    WriteInt16(body, component.Transform.A);
                    WriteInt16(body, component.Transform.B);
                    WriteInt16(body, component.Transform.C);
                    WriteInt16(body, component.Transform.D);
                    break;
            }
        }
        if (composite.HasInstructions)
        {
            if (composite.Instructions.Length > ushort.MaxValue)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    DataLocation.Anonymous(GlyfLayout.Tag).At(DataField.Length, 0, glyph.GlyphId),
                    out diagnostic, (ulong)composite.Instructions.Length, ushort.MaxValue);
            body.Add(unchecked((byte)(composite.Instructions.Length >> 8)));
            body.Add(unchecked((byte)composite.Instructions.Length));
            body.AddRange(composite.Instructions);
        }
        record = [.. body];
        diagnostic = default;
        return ReadStatus.Done;
    }
}
