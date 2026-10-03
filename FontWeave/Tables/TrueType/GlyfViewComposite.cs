using FontWeave.Core;

namespace FontWeave.Tables;

/// <summary>Composite decoding, composite-aware validation, and component-graph
/// verification for the single glyf View.
///
/// This is the same <c>GlyfView</c> type as the simple-glyph slice; composite records
/// are decoded by one parser reached through these entry points. The simple-only
/// <c>Validate</c>/<c>TryRead</c> deliberately keep their narrower contract, so a caller
/// that only needs simple outlines never pays for composite structure or graph work.</summary>
public readonly ref partial struct GlyfView
{
    /// <summary>Decodes one composite glyph record. <paramref name="numGlyphs"/> supplies
    /// the glyph-count bound for component references; component indexes are checked
    /// against it so an out-of-range reference is a diagnostic, not a deferred fault.</summary>
    public ReadStatus TryDecodeComposite(LocaRange range, int numGlyphs,
        out GlyfCompositeGlyph? glyph, out FontDiagnostic diagnostic, bool strict = true)
    {
        glyph = null;
        ReadStatus status = RequireRange(range, out ReadOnlySpan<byte> record, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = TryDecodeCompositeBody(range, record, numGlyphs, out glyph, out diagnostic,
            strict);
        return status;
    }

    private ReadStatus TryDecodeCompositeBody(LocaRange range, ReadOnlySpan<byte> record,
        int numGlyphs, out GlyfCompositeGlyph? glyph, out FontDiagnostic diagnostic,
        bool strict)
    {
        glyph = null;
        var header = new GlyfHeader(
            unchecked((short)((record[0] << 8) | record[1])),
            unchecked((short)((record[2] << 8) | record[3])),
            unchecked((short)((record[4] << 8) | record[5])),
            unchecked((short)((record[6] << 8) | record[7])),
            unchecked((short)((record[8] << 8) | record[9])));
        if (header.NumberOfContours >= 0)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.At(DataField.Count, range.Start, range.GlyphId), out diagnostic,
                unchecked((ulong)header.NumberOfContours), 0);
        int at = GlyfLayout.HeaderLength;
        var components = new List<GlyfComponent>();
        bool lastHadInstructions = false;
        bool sawNonLastInstructions = false;
        while (true)
        {
            if (components.Count >= GlyfLayout.MaxComponentsPerGlyph)
                return ReadWire.Fail(ReadStatus.ResourceLimit, DiagnosticCode.BudgetExceeded,
                    Location.At(DataField.Count, range.Start + (ulong)at, range.GlyphId),
                    out diagnostic, (ulong)components.Count,
                    GlyfLayout.MaxComponentsPerGlyph);
            if (at + 4 > record.Length)
                return TruncatedComposite(range, at, at + 4, out diagnostic);
            ushort flags = (ushort)((record[at] << 8) | record[at + 1]);
            ushort glyphIndex = (ushort)((record[at + 2] << 8) | record[at + 3]);
            at += 4;
            int ordinal = components.Count;

            if (strict && (flags & GlyfLayout.ReservedComponentBits) != 0)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    Location.At(DataField.Flags, range.Start + (ulong)(at - 4), range.GlyphId),
                    out diagnostic, flags, GlyfLayout.ReservedComponentBits);

            int transformBits = flags & (GlyfLayout.WeHaveAScale |
                GlyfLayout.WeHaveAnXAndYScale | GlyfLayout.WeHaveATwoByTwo);
            if (transformBits != 0 && transformBits != GlyfLayout.WeHaveAScale &&
                transformBits != GlyfLayout.WeHaveAnXAndYScale &&
                transformBits != GlyfLayout.WeHaveATwoByTwo)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    Location.At(DataField.Flags, range.Start + (ulong)(at - 4), range.GlyphId),
                    out diagnostic, (ulong)transformBits, GlyfLayout.WeHaveAScale);

            if ((flags & GlyfLayout.ScaledComponentOffset) != 0 &&
                (flags & GlyfLayout.UnscaledComponentOffset) != 0)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    Location.At(DataField.Flags, range.Start + (ulong)(at - 4), range.GlyphId),
                    out diagnostic, 1, 0);

            // Point matching (ARGS_ARE_XY_VALUES clear) is a distinct semantic this slice
            // does not implement. The specification also requires the first component to
            // carry xy values, so a clear bit is refused explicitly rather than accepted
            // and ignored.
            if ((flags & GlyfLayout.ArgsAreXyValues) == 0)
                return ReadWire.Fail(ReadStatus.UnsupportedFormat,
                    DiagnosticCode.UnsupportedOperation,
                    Location.At(DataField.Flags, range.Start + (ulong)(at - 4), range.GlyphId),
                    out diagnostic, (ulong)ordinal, GlyfLayout.ArgsAreXyValues);

            if (glyphIndex >= numGlyphs)
                return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                    Location.At(DataField.GlyphId, range.Start + (ulong)(at - 2), range.GlyphId),
                    out diagnostic, glyphIndex, (ulong)numGlyphs);

            bool words = (flags & GlyfLayout.Arg1And2AreWords) != 0;
            int argumentBytes = words ? 4 : 2;
            if (at + argumentBytes > record.Length)
                return TruncatedComposite(range, at, at + argumentBytes, out diagnostic);
            short arg1, arg2;
            if (words)
            {
                arg1 = unchecked((short)((record[at] << 8) | record[at + 1]));
                arg2 = unchecked((short)((record[at + 2] << 8) | record[at + 3]));
            }
            else
            {
                arg1 = unchecked((sbyte)record[at]);
                arg2 = unchecked((sbyte)record[at + 1]);
            }
            at += argumentBytes;

            var transform = GlyfTransform.Identity;
            int transformBytes = (flags & GlyfLayout.WeHaveAScale) != 0 ? 2
                : (flags & GlyfLayout.WeHaveAnXAndYScale) != 0 ? 4
                : (flags & GlyfLayout.WeHaveATwoByTwo) != 0 ? 8 : 0;
            if (transformBytes > 0)
            {
                if (at + transformBytes > record.Length)
                    return TruncatedComposite(range, at, at + transformBytes, out diagnostic);
                // Read inline rather than through a local function: a local function
                // cannot capture the ref-like span parameter.
                short first = unchecked((short)((record[at] << 8) | record[at + 1]));
                transform = (flags & GlyfLayout.WeHaveAScale) != 0
                    ? new(GlyfTransformKind.Scale, first, 0, 0,
                        unchecked((short)((record[at] << 8) | record[at + 1])))
                    : (flags & GlyfLayout.WeHaveAnXAndYScale) != 0
                        ? new(GlyfTransformKind.XAndYScale, first, 0, 0,
                            unchecked((short)((record[at + 2] << 8) | record[at + 3])))
                        : new(GlyfTransformKind.TwoByTwo, first,
                            unchecked((short)((record[at + 2] << 8) | record[at + 3])),
                            unchecked((short)((record[at + 4] << 8) | record[at + 5])),
                            unchecked((short)((record[at + 6] << 8) | record[at + 7])));
                at += transformBytes;
            }

            bool more = (flags & GlyfLayout.MoreComponents) != 0;
            if ((flags & GlyfLayout.WeHaveInstructions) != 0)
            {
                if (more) sawNonLastInstructions = true;
                else lastHadInstructions = true;
            }
            components.Add(new(ordinal, flags, glyphIndex, arg1, arg2, transform));
            if (!more) break;
        }
        // WE_HAVE_INSTRUCTIONS is meaningful only on the final component. A non-final
        // component setting it while the final one does not is a structural ambiguity.
        if (sawNonLastInstructions && !lastHadInstructions)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.At(DataField.Flags, range.Start + (ulong)at, range.GlyphId),
                out diagnostic, GlyfLayout.WeHaveInstructions, 0);
        byte[] instructions = [];
        if (lastHadInstructions)
        {
            if (at + 2 > record.Length)
                return TruncatedComposite(range, at, at + 2, out diagnostic);
            int instructionLength = (record[at] << 8) | record[at + 1];
            at += 2;
            if (instructionLength > record.Length - at)
                return TruncatedComposite(range, at, at + instructionLength, out diagnostic);
            instructions = record.Slice(at, instructionLength).ToArray();
            at += instructionLength;
        }
        for (int i = at; i < record.Length; i++)
            if (record[i] != 0)
                return ReadWire.Fail(ReadStatus.InvalidFormat,
                    DiagnosticCode.UnexpectedTrailingData,
                    Location.At(DataField.Payload, range.Start + (ulong)i, range.GlyphId),
                    out diagnostic, record[i], 0);
        glyph = new(range.GlyphId, header, [.. components], instructions, at, range.Start);
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>Shared range admission for the composite entry points: the view must be
    /// bound, the range must be ordered, non-empty, ten bytes or more, and inside the
    /// image. Returns the record slice on success.</summary>
    private ReadStatus RequireRange(LocaRange range, out ReadOnlySpan<byte> record,
        out FontDiagnostic diagnostic)
    {
        record = default;
        if (!IsBound)
            return ReadWire.Fail(ReadStatus.Uninitialized, DiagnosticCode.UnboundView,
                Location.At(DataField.None, 0), out diagnostic);
        if (range.End < range.Start)
            return ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.InvalidValue,
                Location.At(DataField.Offset, range.Start, range.GlyphId), out diagnostic,
                range.End, range.Start);
        if (range.End > (uint)_data.Length)
            return ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.RangeOutsideSource,
                Location.At(DataField.Offset, range.Start, range.GlyphId), out diagnostic,
                range.End, (ulong)_data.Length);
        if (range.IsEmpty || range.Length < GlyfLayout.MinimumGlyph)
            return ReadWire.Fail(ReadStatus.Truncated, DiagnosticCode.TruncatedData,
                Location.At(DataField.Payload, range.Start, range.GlyphId), out diagnostic,
                range.Length, GlyfLayout.MinimumGlyph);
        record = _data.Slice((int)range.Start, (int)range.Length);
        diagnostic = default;
        return ReadStatus.Done;
    }

    private ReadStatus TruncatedComposite(LocaRange range, int at, int needed,
        out FontDiagnostic diagnostic) =>
        ReadWire.Fail(ReadStatus.Truncated, DiagnosticCode.TruncatedData,
            Location.At(DataField.Payload, range.Start + (ulong)at, range.GlyphId),
            out diagnostic, (ulong)at, (ulong)needed);

    /// <summary>Validates every glyph including composite structure, then verifies the
    /// component graph. Failure produces no partial model and leaves the input untouched.</summary>
    public ReadStatus ValidateComposites(LocaTable loca, out GlyfValidationSummary summary,
        out FontDiagnostic diagnostic, bool strict = true)
    {
        summary = default;
        ReadStatus status = RequireLoca(loca, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = TryReadComposites(loca, out var model, out diagnostic, Budget(), strict);
        if (status != ReadStatus.Done) return status;
        // TryReadComposites already verifies the graph before publishing the model.
        summary = model!.Summarize();
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>Materializes owned contents with composite structure decoded. The component
    /// graph is verified before any model is published.</summary>
    public ReadStatus TryReadComposites(LocaTable loca, out GlyfTable? model,
        out FontDiagnostic diagnostic, BuildLimits? limits = null, bool strict = true)
    {
        model = null;
        var budget = new BuildBudget(limits);
        if (!budget.Input(_data.Length, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        return TryReadComposites(loca, out model, out diagnostic, budget, strict);
    }

    internal ReadStatus TryReadComposites(LocaTable loca, out GlyfTable? model,
        out FontDiagnostic diagnostic, BuildBudget budget, bool strict = true)
    {
        model = null;
        ReadStatus status = RequireLoca(loca, out diagnostic);
        if (status != ReadStatus.Done) return status;
        // Work is charged per input byte here; temporary space is charged per glyph as it is
        // decoded, because a glyph's decoded size follows its point count rather than its
        // wire length (a REPEAT flag run describes many points in a few bytes).
        if (!budget.Work(_data.Length, Location, out diagnostic))
            return ReadStatus.ResourceLimit;
        if (!budget.Buffer((long)IntPtr.Size * loca.NumGlyphs, Location, out diagnostic))
            return ReadStatus.ResourceLimit;
        var glyphs = new GlyfGlyph?[loca.NumGlyphs];
        int componentEdges = 0;
        for (int gid = 0; gid < glyphs.Length; gid++)
        {
            status = loca.TryGetGlyphRange(gid, out var range, out diagnostic);
            if (status != ReadStatus.Done) return status;
            status = TryDecodeGlyph(range, out var glyph, out diagnostic, strict,
                loca.NumGlyphs, budget);
            if (status != ReadStatus.Done) return status;
            // Enforce the graph-wide edge cap while decoding, rather than first
            // materializing every composite of an already over-budget image.
            componentEdges += glyph?.Composite?.ComponentCount ?? 0;
            if (componentEdges > GlyfLayout.MaxComponentEdgesBudget)
                return ReadWire.Fail(ReadStatus.ResourceLimit, DiagnosticCode.BudgetExceeded,
                    Location.At(DataField.GlyphId, 0, gid), out diagnostic,
                    (ulong)componentEdges, GlyfLayout.MaxComponentEdgesBudget);
            glyphs[gid] = glyph;
        }
        if (!budget.Buffer(_data.Length, Location, out diagnostic))
            return ReadStatus.ResourceLimit;
        var candidate = new GlyfTable(glyphs, _data.ToArray(), Location, loca.Location);
        // The graph is verified before the model is handed to the caller, so a cyclic or
        // over-budget hierarchy can never be observed as a usable result.
        status = new ComponentGraphBuilder(candidate).TryBuild(out _, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        model = candidate;
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>Graph-only verification for a materialized model.</summary>
    public ReadStatus ValidateComponentGraph(GlyfTable model, out ComponentGraph? graph,
        out FontDiagnostic diagnostic)
    {
        ArgumentNullException.ThrowIfNull(model);
        return new ComponentGraphBuilder(model).TryBuild(out graph, out diagnostic);
    }

    private static BuildBudget Budget() => new();
}

/// <summary>Builds and verifies the component hierarchy, enforcing the declared depth
/// and edge budgets.</summary>
internal sealed class ComponentGraphBuilder
{
    private readonly GlyfTable _model;

    internal ComponentGraphBuilder(GlyfTable model) => _model = model;

    internal ReadStatus TryBuild(out ComponentGraph? graph, out FontDiagnostic diagnostic,
        BuildBudget? budget = null)
    {
        graph = null;
        diagnostic = default;
        budget ??= new();
        int count = _model.NumGlyphs;
        if (!budget.Work(count, _model.Location, out diagnostic))
            return ReadStatus.ResourceLimit;
        int edges = 0;
        // Admit the whole adjacency payload before allocating any graph arrays.
        for (int gid = 0; gid < count; gid++)
        {
            _ = _model.TryGetGlyph(gid, out var glyph, out _);
            if (glyph?.Composite is { } composite)
            {
                edges += composite.ComponentCount;
                if (edges > GlyfLayout.MaxComponentEdgesBudget)
                    return Fail(ReadStatus.ResourceLimit, DiagnosticCode.BudgetExceeded,
                        gid, out diagnostic, (ulong)edges,
                        GlyfLayout.MaxComponentEdgesBudget);
            }
        }
        // References, adjacency entries, state, depth, two DFS-frame fields and visited.
        long temporary = ((long)IntPtr.Size + 14) * count + 4L * edges;
        if (!budget.Work(2L * count + 3L * edges, _model.Location, out diagnostic) ||
            !budget.Buffer(temporary, _model.Location, out diagnostic))
            return ReadStatus.ResourceLimit;
        var children = new int[count][];
        for (int gid = 0; gid < count; gid++)
        {
            _ = _model.TryGetGlyph(gid, out var glyph, out _);
            int componentCount = glyph?.Composite?.ComponentCount ?? 0;
            var list = componentCount == 0 ? Array.Empty<int>() : new int[componentCount];
            for (int i = 0; i < list.Length; i++)
            {
                int child = glyph!.Composite![i].GlyphIndex;
                if (child >= count)
                    return Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                        gid, out diagnostic, (ulong)child, (ulong)count);
                list[i] = child;
            }
            children[gid] = list;
        }
        // Iterative post-order DFS preserves memoized subtree depths without using the
        // process stack. A bounded input can still have tens of thousands of levels;
        // checking its resolved depth after a recursive walk would be too late.
        var state = new byte[count];
        var depth = new int[count];
        var stack = new int[count];
        var nextChild = new int[count];
        for (int gid = 0; gid < count; gid++)
        {
            if (state[gid] != 0) continue;
            int top = 0;
            stack[top] = gid;
            nextChild[top] = 0;
            state[gid] = 1;
            while (top >= 0)
            {
                int current = stack[top];
                if (nextChild[top] == children[current].Length)
                {
                    state[current] = 2;
                    top--;
                    if (top >= 0)
                    {
                        int parent = stack[top];
                        depth[parent] = Math.Max(depth[parent], depth[current] + 1);
                    }
                    continue;
                }
                int child = children[current][nextChild[top]++];
                if (state[child] == 1)
                    return Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                        child, out diagnostic, (ulong)child);
                if (state[child] == 2)
                {
                    depth[current] = Math.Max(depth[current], depth[child] + 1);
                    continue;
                }
                state[child] = 1;
                stack[++top] = child;
                nextChild[top] = 0;
            }
        }
        int maxDepth = 0;
        for (int gid = 0; gid < count; gid++) maxDepth = Math.Max(maxDepth, depth[gid]);
        if (maxDepth > GlyfLayout.MaxComponentDepthBudget)
        {
            int offender = Array.IndexOf(depth, maxDepth);
            return Fail(ReadStatus.ResourceLimit, DiagnosticCode.BudgetExceeded, offender,
                out diagnostic, (ulong)maxDepth, GlyfLayout.MaxComponentDepthBudget);
        }
        var visited = new bool[count];
        Array.Fill(visited, true);
        graph = new(children, depth, visited, edges, maxDepth);
        return ReadStatus.Done;
    }

    private ReadStatus Fail(ReadStatus status, DiagnosticCode code, int gid,
        out FontDiagnostic diagnostic, ulong detail = 0, ulong expected = 0)
    {
        diagnostic = new(code, _model.Location
            .At(DataField.GlyphId, 0, gid), detail, expected);
        return status;
    }
}
