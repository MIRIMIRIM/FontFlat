using FontWeave.Core;
using FontWeave.Tables.Variation;

namespace FontWeave.Tables.Layout;

/// <summary>The first BASE root writer: one planned, atomically published whole-table
/// emission for versions 1.0/1.1, with the typed item variation store accepted and
/// tail-placed as the fixed last block (the T4-02b ruling carried to BASE's Offset32
/// slot: the real witnesses trail naturally, the Offset32 target never competes for
/// the 64K budget, and a mid-position source store re-emits wire-equivalent after the
/// canonical re-placement, not byte-equal). Planning is budget-first with two bounded
/// layout candidates over the two axis contributions - natural slot order and
/// largest-contribution-last (the ScriptList family precedent); a refused candidate
/// keeps its charged work, and no generic repacker exists.
///
/// The canonical order inside a candidate is the witnesses' own kind-grouped shape
/// (R9): per axis the header group (axis, tag list, script list), then every unique
/// script body (axis order, record order), then every unique BaseValues, then the
/// MinMax bodies, then every unique coord with its Device inline - HAN's
/// horiz/vert scripts interleave 140/146/152/158 exactly this way, and both single-
/// axis witnesses are the degenerate case. Shared bodies dedupe by
/// ReferenceEquals first, then content identity (the T4-01 rules; the axis slots
/// may even carry one shared axis instance), so HAN's 8 -> 7 coord graph and 7 -> 2
/// script shapes survive to byte-identity. Same-source admission, version gating and
/// the finite refusals follow the GDEF root contract.</summary>
public static class BaseTableCompiler
{
    private sealed class Placement
    {
        internal int Length;
        internal int StoreAt = -1;
        internal List<BaseAxisTable> AxisOrder = new();
        internal Dictionary<BaseAxisTable, (int AxisAt, int TagListAt, int ScriptListAt)> Headers =
            new(ReferenceEqualityComparer.Instance);
        internal BaseAxisTableCompiler.AxisLayoutPlan Plan = new();
    }

    /// <summary>Explicit root-model factory: every child and the glyph context are
    /// caller-supplied and never inferred; the store is a v1.1-only slot and a
    /// non-null store under 1.0 refuses exactly like the GDEF marksets/store
    /// admission. The raw offset is a placement outcome, never a factory input.</summary>
    public static ReadStatus TryCreate(uint version, int numGlyphs, BaseAxisTable? horizAxis,
        BaseAxisTable? vertAxis, VariationStoreTable? itemVariationStore,
        out BaseTable? model, out FontDiagnostic diagnostic, BuildLimits? limits = null,
        DataLocation location = default)
    {
        model = null;
        ReadStatus status = version is not (BaseLayout.Version10 or BaseLayout.Version11)
            ? ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedVersion,
                location.AtLocal(DataField.Version, 0), out diagnostic, version)
            : CoverageTableCompiler.CheckNumGlyphs(numGlyphs, location, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (itemVariationStore is not null && version != BaseLayout.Version11)
            return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
                location.AtLocal(DataField.Offset, BaseLayout.ItemVariationStoreAt), out diagnostic);
        if (!SameSource(location, horizAxis) || !SameSource(location, vertAxis) ||
            !SameSource(location, itemVariationStore))
            return ReadWire.Fail(ReadStatus.InvalidSource, DiagnosticCode.WrongSourceFace,
                location.AtLocal(DataField.None, 0), out diagnostic);
        model = new(version, numGlyphs, horizAxis, vertAxis, itemVariationStore, 0, location, 0);
        diagnostic = default;
        return ReadStatus.Done;
    }

    private static bool SameSource(DataLocation location, object? model)
    {
        DataLocation child = model switch
        {
            BaseAxisTable axis => axis.Location,
            VariationStoreTable store => store.Location,
            _ => location,
        };
        return location.SourceId == child.SourceId && location.FaceIndex == child.FaceIndex &&
            (location.SourceId == 0 || location.TableTag == child.TableTag);
    }

    public static ReadStatus TryCompile(BaseTable model, out byte[]? output,
        out FontDiagnostic diagnostic, BuildLimits? limits = null) =>
        TryCompile(model, out output, out diagnostic, new BuildBudget(limits));

    internal static ReadStatus TryCompile(BaseTable model, out byte[]? output,
        out FontDiagnostic diagnostic, BuildBudget budget)
    {
        ArgumentNullException.ThrowIfNull(model);
        output = null;
        ReadStatus status = TryPlan(model, out var plan, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        if (!budget.Work(plan!.Length, model.Location, out diagnostic) ||
            !budget.Buffer(plan.Length, model.Location, out diagnostic)) return ReadStatus.ResourceLimit;
        byte[] bytes = new byte[plan.Length];
        WritePlanned(model, plan, bytes);
        budget.RecordCompiledTable(bytes.Length);
        output = bytes;
        diagnostic = default;
        return ReadStatus.Done;
    }

    public static ReadStatus TryWrite(BaseTable model, Span<byte> destination,
        out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        var budget = new BuildBudget(limits);
        ReadStatus status = TryPlan(model, out var plan, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        // Every plan admission, including the emission work, completes before the
        // destination capacity check (spec 7.3; A6 1.5 O4 order); the destination is
        // untouched on refusal.
        if (!budget.Work(plan!.Length, model.Location, out diagnostic)) return ReadStatus.ResourceLimit;
        if (destination.Length < plan.Length)
            return ReadWire.Fail(ReadStatus.DestinationTooSmall, DiagnosticCode.DestinationTooSmall,
                model.Location.AtLocal(DataField.OutputBytes, 0), out diagnostic,
                (ulong)destination.Length, (ulong)plan.Length);
        WritePlanned(model, plan, destination);
        diagnostic = default;
        return ReadStatus.Done;
    }

    private static ReadStatus TryPlan(BaseTable model, out Placement? plan,
        out FontDiagnostic diagnostic, BuildBudget budget)
    {
        plan = null;
        int headerLength = model.Version == BaseLayout.Version11
            ? BaseLayout.Header11 : BaseLayout.Header10;
        bool sharedAxis = model.HorizAxis is not null &&
            ReferenceEquals(model.HorizAxis, model.VertAxis);
        int storeSize = model.ItemVariationStore is null ? 0 :
            VariationStoreTableCompiler.Measure(model.ItemVariationStore);
        bool bothAxes = model.HorizAxis is not null && model.VertAxis is not null && !sharedAxis;
        // Candidate 1: natural slot order. Candidate 2: the larger contribution last
        // (ties keep slot order). Each candidate is a full plan; a refused candidate's
        // Work stays charged and is never refunded (OQ-T4-02-4).
        foreach (bool largestLast in bothAxes ? new[] { false, true } : new[] { false })
        {
            var axes = new List<BaseAxisTable>();
            if (model.HorizAxis is not null) axes.Add(model.HorizAxis);
            if (model.VertAxis is not null && !sharedAxis)
            {
                if (largestLast && ContributionSize(model.VertAxis) > ContributionSize(model.HorizAxis!))
                    axes.Insert(0, model.VertAxis);
                else
                    axes.Add(model.VertAxis);
            }
            var candidate = new Placement();
            var status = Layout(model, axes, headerLength, storeSize, candidate, budget,
                out diagnostic);
            if (status == ReadStatus.Done)
            {
                plan = candidate;
                diagnostic = default;
                return ReadStatus.Done;
            }
            if (status != ReadStatus.UnsupportedFormat) return status;
        }
        // Both candidates refused: the second candidate's width refusal stands, widened
        // to the slot-level contract (UnsupportedOperation at the axis slot).
        return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
            model.Location.AtLocal(DataField.Offset, BaseLayout.HorizAxisAt), out diagnostic,
            0, ushort.MaxValue);
    }

    // One axis contribution's total placed size (dedupe-aware within the axis; the
    // cross-axis shared bodies are charged to the axis that places them first).
    private static int ContributionSize(BaseAxisTable axis)
    {
        var seenScripts = new List<BaseScriptTable>();
        var seenValues = new List<BaseValuesTable>();
        var seenMinMax = new List<BaseMinMaxTable>();
        var seenCoords = new List<BaseCoordTable>();
        int total = BaseAxisLayout.HeaderLength;
        if (axis.TagList is { } tagList) total += BaseAxisTableCompiler.TagListLength(tagList);
        total += BaseAxisTableCompiler.ScriptListLength(axis);
        foreach (var record in axis.Scripts)
        {
            if (Contains(seenScripts, record.Script) is null)
            {
                seenScripts.Add(record.Script);
                total += BaseAxisTableCompiler.ScriptLength(record.Script);
            }
        }
        foreach (var record in axis.Scripts)
        {
            if (record.Script.BaseValues is { } values && Contains(seenValues, values) is null)
            {
                seenValues.Add(values);
                total += BaseAxisTableCompiler.ValuesLength(values);
                foreach (var coord in values.Coords)
                    if (Contains(seenCoords, coord) is null)
                    {
                        seenCoords.Add(coord);
                        total += BaseCoordTableCompiler.Measure(coord);
                    }
            }
        }
        foreach (var record in axis.Scripts)
            foreach (var minMax in MinMaxOrder(record.Script, seenMinMax))
            {
                seenMinMax.Add(minMax);
                total += BaseAxisTableCompiler.MinMaxLength(minMax);
                foreach (var coord in MinMaxCoords(minMax))
                    if (Contains(seenCoords, coord) is null)
                    {
                        seenCoords.Add(coord);
                        total += BaseCoordTableCompiler.Measure(coord);
                    }
            }
        return total;
    }

    // The kind-grouped placement: header groups per axis, then the script, values,
    // minmax and coord pools across both axes in candidate order. Every offset slot
    // is preflighted through its generated helper, whose too-narrow refusal is the
    // candidate's finite overflow refusal.
    private static ReadStatus Layout(BaseTable model, List<BaseAxisTable> axes,
        int headerLength, int storeSize, Placement candidate, BuildBudget budget,
        out FontDiagnostic diagnostic)
    {
        int cursor = headerLength;
        foreach (var axis in axes)
        {
            int axisAt = cursor;
            if (!budget.Work(BaseAxisLayout.HeaderLength, axis.Location, out diagnostic))
                return ReadStatus.ResourceLimit;
            cursor += BaseAxisLayout.HeaderLength;
            int tagListAt = -1;
            if (axis.TagList is { } tagList)
            {
                tagListAt = cursor;
                if (!budget.Work(BaseAxisTableCompiler.TagListLength(tagList), tagList.Location,
                    out diagnostic)) return ReadStatus.ResourceLimit;
                cursor += BaseAxisTableCompiler.TagListLength(tagList);
            }
            int scriptListAt = cursor;
            if (!budget.Work(BaseAxisTableCompiler.ScriptListLength(axis), axis.Location,
                out diagnostic)) return ReadStatus.ResourceLimit;
            cursor += BaseAxisTableCompiler.ScriptListLength(axis);
            candidate.AxisOrder.Add(axis);
            candidate.Headers[axis] = (axisAt, tagListAt, scriptListAt);
        }
        // Scripts.
        foreach (var axis in axes)
            foreach (var record in axis.Scripts)
            {
                if (candidate.Plan.Scripts.ContainsKey(record.Script)) continue;
                if (!budget.Work(BaseAxisTableCompiler.ScriptLength(record.Script),
                    record.Script.Location, out diagnostic)) return ReadStatus.ResourceLimit;
                candidate.Plan.Scripts[record.Script] = cursor;
                cursor += BaseAxisTableCompiler.ScriptLength(record.Script);
            }
        // Values.
        foreach (var axis in axes)
            foreach (var record in axis.Scripts)
            {
                if (record.Script.BaseValues is not { } values) continue;
                if (candidate.Plan.Values.ContainsKey(values)) continue;
                if (!budget.Work(BaseAxisTableCompiler.ValuesLength(values), values.Location,
                    out diagnostic)) return ReadStatus.ResourceLimit;
                candidate.Plan.Values[values] = cursor;
                cursor += BaseAxisTableCompiler.ValuesLength(values);
            }
        // MinMax bodies: default first, then the langSys rows, per script.
        var seenMinMax = new List<BaseMinMaxTable>();
        foreach (var axis in axes)
            foreach (var record in axis.Scripts)
                foreach (var minMax in MinMaxOrder(record.Script, seenMinMax))
                {
                    seenMinMax.Add(minMax);
                    if (!budget.Work(BaseAxisTableCompiler.MinMaxLength(minMax), minMax.Location,
                        out diagnostic)) return ReadStatus.ResourceLimit;
                    candidate.Plan.MinMax[minMax] = cursor;
                    cursor += BaseAxisTableCompiler.MinMaxLength(minMax);
                }
        // Coords: values slots in order, then the minmax targets, each with its
        // Device inline (one placement step of the composite Measure).
        var seenCoords = new List<BaseCoordTable>();
        foreach (var axis in axes)
            foreach (var record in axis.Scripts)
            {
                if (record.Script.BaseValues is not { } values) continue;
                foreach (var coord in values.Coords)
                {
                    var representative = Place(seenCoords, coord);
                    if (representative is null)
                    {
                        seenCoords.Add(coord);
                        if (!budget.Work(BaseCoordTableCompiler.Measure(coord), coord.Location,
                            out diagnostic)) return ReadStatus.ResourceLimit;
                        candidate.Plan.Coords[coord] = cursor;
                        cursor += BaseCoordTableCompiler.Measure(coord);
                    }
                    else
                    {
                        candidate.Plan.Coords[coord] = candidate.Plan.Coords[representative];
                    }
                }
            }
        foreach (var minMax in seenMinMax)
            foreach (var coord in MinMaxCoords(minMax))
            {
                var representative = Place(seenCoords, coord);
                if (representative is null)
                {
                    seenCoords.Add(coord);
                    if (!budget.Work(BaseCoordTableCompiler.Measure(coord), coord.Location,
                        out diagnostic)) return ReadStatus.ResourceLimit;
                    candidate.Plan.Coords[coord] = cursor;
                    cursor += BaseCoordTableCompiler.Measure(coord);
                }
                else
                {
                    candidate.Plan.Coords[coord] = candidate.Plan.Coords[representative];
                }
            }
        // The store is the fixed trailing block: its Offset32 target carries no 64K
        // pressure, so its position is admitted after the subtree layout without a
        // width check, charged like any child.
        if (model.ItemVariationStore is not null)
        {
            if (!budget.Work(storeSize, model.ItemVariationStore.Location, out diagnostic))
                return ReadStatus.ResourceLimit;
            candidate.StoreAt = cursor;
            cursor += storeSize;
        }
        candidate.Length = cursor;
        return Preflight(model, axes, candidate, out diagnostic);
    }

    // Every Offset16/Offset32 slot preflights its final target through the generated
    // helper; a too-narrow target refuses the whole candidate (UnsupportedFormat).
    private static ReadStatus Preflight(BaseTable model, List<BaseAxisTable> axes,
        Placement candidate, out FontDiagnostic diagnostic)
    {
        var location = model.Location;
        ReadStatus status;
        foreach (var axis in axes)
        {
            var (axisAt, tagListAt, scriptListAt) = candidate.Headers[axis];
            status = BaseAxisView.TryGetBaseTagListOffsetValue(axis.TagList is not null, 0UL,
                tagListAt < 0 ? 0UL : (ulong)(tagListAt - axisAt), out _, out diagnostic,
                location.AtLocal(DataField.Offset, BaseLayout.HorizAxisAt));
            if (status != ReadStatus.Done) return status;
            status = BaseAxisView.TryGetBaseScriptListOffsetValue(present: true, 0UL,
                (ulong)(scriptListAt - axisAt), out _, out diagnostic,
                location.AtLocal(DataField.Offset, BaseLayout.VertAxisAt));
            if (status != ReadStatus.Done) return status;
            for (int i = 0; i < axis.ScriptCount; i++)
            {
                var record = axis.Scripts[i];
                var script = record.Script;
                int scriptAt = candidate.Plan.Scripts[script];
                status = BaseScriptListView.TryGetBaseScriptOffsetValue(present: true, 0UL,
                    (ulong)(scriptAt - scriptListAt), out _, out diagnostic,
                    axis.Location.AtLocal(DataField.Offset,
                        BaseScriptListLayout.HeaderLength + (ulong)i * BaseScriptListLayout.RecordStride
                        + BaseScriptListLayout.RecordOffsetField, i));
                if (status != ReadStatus.Done) return status;
                status = BaseScriptView.TryGetBaseValuesOffsetValue(script.BaseValues is not null,
                    0UL, (ulong)(script.BaseValues is null ? 0
                        : candidate.Plan.Values[script.BaseValues] - scriptAt),
                    out _, out diagnostic,
                    script.Location.AtLocal(DataField.Offset, BaseScriptLayout.BaseValuesOffsetField));
                if (status != ReadStatus.Done) return status;
                status = BaseScriptView.TryGetDefaultMinMaxOffsetValue(script.DefaultMinMax is not null,
                    0UL, (ulong)(script.DefaultMinMax is null ? 0
                        : candidate.Plan.MinMax[script.DefaultMinMax] - scriptAt),
                    out _, out diagnostic,
                    script.Location.AtLocal(DataField.Offset, BaseScriptLayout.DefaultMinMaxOffsetField));
                if (status != ReadStatus.Done) return status;
                for (int j = 0; j < script.BaseLangSysCount; j++)
                {
                    status = BaseScriptView.TryGetMinMaxOffsetValue(present: true, 0UL,
                        (ulong)(candidate.Plan.MinMax[script.LangSys[j].MinMax] - scriptAt), out _, out diagnostic,
                        script.Location.AtLocal(DataField.Offset,
                            BaseScriptLayout.HeaderLength + (ulong)j * BaseScriptLayout.RecordStride
                            + BaseScriptLayout.RecordOffsetField, j));
                    if (status != ReadStatus.Done) return status;
                }
                if (script.BaseValues is not { } values) continue;
                int valuesAt = candidate.Plan.Values[values];
                for (int j = 0; j < values.CoordCount; j++)
                {
                    status = BaseValuesView.TryGetCoordOffsetValue(present: true, 0UL,
                        (ulong)(candidate.Plan.Coords[values.Coords[j]] - valuesAt), out _, out diagnostic,
                        values.Location.AtLocal(DataField.Offset,
                            BaseValuesLayout.HeaderLength + (ulong)j * BaseValuesLayout.CoordStride, j));
                    if (status != ReadStatus.Done) return status;
                }
            }
        }
        foreach (var minMax in candidate.Plan.MinMax.Keys)
        {
            int minMaxAt = candidate.Plan.MinMax[minMax];
            status = BaseMinMaxView.TryGetMinCoordOffsetValue(minMax.MinCoord is not null, 0UL,
                (ulong)(minMax.MinCoord is null ? 0 : candidate.Plan.Coords[minMax.MinCoord] - minMaxAt),
                out _, out diagnostic,
                minMax.Location.AtLocal(DataField.Offset, BaseMinMaxLayout.MinCoordOffsetField));
            if (status != ReadStatus.Done) return status;
            status = BaseMinMaxView.TryGetMaxCoordOffsetValue(minMax.MaxCoord is not null, 0UL,
                (ulong)(minMax.MaxCoord is null ? 0 : candidate.Plan.Coords[minMax.MaxCoord] - minMaxAt),
                out _, out diagnostic,
                minMax.Location.AtLocal(DataField.Offset, BaseMinMaxLayout.MaxCoordOffsetField));
            if (status != ReadStatus.Done) return status;
            for (int i = 0; i < minMax.FeatMinMaxCount; i++)
            {
                var record = minMax.FeatMinMax[i];
                status = BaseMinMaxView.TryGetFeatMinCoordOffsetValue(record.MinCoord is not null,
                    0UL, (ulong)(record.MinCoord is null ? 0 : candidate.Plan.Coords[record.MinCoord] - minMaxAt),
                    out _, out diagnostic,
                    minMax.Location.AtLocal(DataField.Offset,
                        BaseMinMaxLayout.HeaderLength + (ulong)i * BaseMinMaxLayout.RecordStride
                        + BaseMinMaxLayout.RecordMinOffsetField, i));
                if (status != ReadStatus.Done) return status;
                status = BaseMinMaxView.TryGetFeatMaxCoordOffsetValue(record.MaxCoord is not null,
                    0UL, (ulong)(record.MaxCoord is null ? 0 : candidate.Plan.Coords[record.MaxCoord] - minMaxAt),
                    out _, out diagnostic,
                    minMax.Location.AtLocal(DataField.Offset,
                        BaseMinMaxLayout.HeaderLength + (ulong)i * BaseMinMaxLayout.RecordStride
                        + BaseMinMaxLayout.RecordMaxOffsetField, i));
                if (status != ReadStatus.Done) return status;
            }
        }
        foreach (var coord in candidate.Plan.Coords.Keys)
        {
            if (coord.Format != 3 || coord.Device is null) continue;
            // The Device offset is coord-relative (the coord view's own origin).
            status = BaseCoordView.TryGetDeviceOffsetValue(present: true,
                (ulong)candidate.Plan.Coords[coord],
                (ulong)(candidate.Plan.Coords[coord] + BaseCoordTableCompiler.BodyLength(coord.Format)),
                out _, out diagnostic,
                coord.Location.AtLocal(DataField.Offset, BaseCoordLayout.Format3HeaderField));
            if (status != ReadStatus.Done) return status;
        }
        if (model.ItemVariationStore is not null)
        {
            status = BaseView.TryGetItemVariationStoreOffsetValue(present: true, 0UL,
                (ulong)candidate.StoreAt, out _, out diagnostic,
                location.AtLocal(DataField.Offset, BaseLayout.ItemVariationStoreAt));
            if (status != ReadStatus.Done) return status;
        }
        diagnostic = default;
        return ReadStatus.Done;
    }

    // Only a fully preflighted plan and destination reach this path. The emission
    // re-walks the planning order; each body writes exactly once at its position
    // (content duplicates share the position, and equal content makes either
    // instance's bytes identical).
    private static void WritePlanned(BaseTable model, Placement plan, Span<byte> destination)
    {
        var location = model.Location;
        CoverageTableCompiler.RequireWritten(BaseView.TryWriteVersion(destination, model.Version,
            out _, location));
        WriteAxisSlot(destination, location, BaseView.TryWriteHorizAxisOffset,
            BaseView.TryGetHorizAxisOffsetValue, model.HorizAxis is not null,
            model.HorizAxis is null ? 0 : plan.Headers[model.HorizAxis!].AxisAt);
        WriteAxisSlot(destination, location, BaseView.TryWriteVertAxisOffset,
            BaseView.TryGetVertAxisOffsetValue, model.VertAxis is not null,
            model.VertAxis is null ? 0 : plan.Headers[model.VertAxis!].AxisAt);
        if (model.Version == BaseLayout.Version11)
        {
            // The store slot encodes the trailing placement (Offset32 domain); a null
            // store encodes zero through present=false.
            var status = BaseView.TryGetItemVariationStoreOffsetValue(
                model.ItemVariationStore is not null, 0UL,
                (ulong)(plan.StoreAt < 0 ? 0 : plan.StoreAt), out uint encoded, out _,
                location.AtLocal(DataField.Offset, BaseLayout.ItemVariationStoreAt));
            CoverageTableCompiler.RequireWritten(status);
            CoverageTableCompiler.RequireWritten(BaseView.TryWriteVariationStoreHeaderAt(destination,
                BaseLayout.ItemVariationStoreAt,
                new BaseView.VariationStoreHeaderData(-1, encoded), out _, location));
        }
        // Header groups.
        foreach (var axis in plan.AxisOrder)
        {
            var (axisAt, tagListAt, scriptListAt) = plan.Headers[axis];
            BaseAxisTableCompiler.WriteAxis(axis, destination, axisAt,
                tagListAt < 0 ? 0 : tagListAt, scriptListAt, axis.Location);
            if (axis.TagList is { } tagList)
                BaseAxisTableCompiler.WriteTagList(tagList, destination, tagListAt, tagList.Location);
            BaseAxisTableCompiler.WriteScriptList(axis, destination, scriptListAt, plan.Plan, axis.Location);
        }
        // Script bodies.
        var writtenScripts = new HashSet<int>();
        foreach (var axis in plan.AxisOrder)
            foreach (var record in axis.Scripts)
            {
                int scriptAt = plan.Plan.Scripts[record.Script];
                if (writtenScripts.Add(scriptAt))
                    BaseAxisTableCompiler.WriteScript(record.Script, destination, scriptAt,
                        plan.Plan, record.Script.Location);
            }
        // Values bodies.
        var writtenValues = new HashSet<int>();
        foreach (var axis in plan.AxisOrder)
            foreach (var record in axis.Scripts)
            {
                if (record.Script.BaseValues is not { } values) continue;
                int valuesAt = plan.Plan.Values[values];
                if (writtenValues.Add(valuesAt))
                    BaseAxisTableCompiler.WriteValues(values, destination, valuesAt,
                        plan.Plan, values.Location);
            }
        // MinMax bodies.
        var writtenMinMax = new HashSet<int>();
        foreach (var minMaxAt in plan.Plan.MinMax.Values)
            if (writtenMinMax.Add(minMaxAt))
            {
                var minMax = KeyAt(plan.Plan.MinMax, minMaxAt);
                BaseAxisTableCompiler.WriteMinMax(minMax, destination, minMaxAt, plan.Plan,
                    minMax.Location);
            }
        // Coord bodies (Device inline).
        var writtenCoords = new HashSet<int>();
        foreach (var coordAt in plan.Plan.Coords.Values)
            if (writtenCoords.Add(coordAt))
            {
                var coord = KeyAt(plan.Plan.Coords, coordAt);
                BaseCoordTableCompiler.WritePlanned(coord, destination.Slice(coordAt));
            }
        if (model.ItemVariationStore is not null)
        {
            // The accepted store compiler (T5-01, byte-proven on HVAR) emits the
            // canonical header -> dataOffsets -> region list -> VarData order.
            VariationStoreTableCompiler.WritePlanned(model.ItemVariationStore,
                destination.Slice(plan.StoreAt));
        }
    }

    // One axis slot write: the Optional value helper encodes absent as zero; a
    // refusal on a preflighted plan is an invariant breach.
    private delegate ReadStatus OffsetEncoder(bool present, ulong origin, ulong target,
        out ushort encoded, out FontDiagnostic diagnostic, DataLocation location);
    private delegate ReadStatus OffsetWriter(Span<byte> destination, ushort encoded,
        out FontDiagnostic diagnostic, DataLocation location);

    private static void WriteAxisSlot(Span<byte> destination, DataLocation location,
        OffsetWriter write, OffsetEncoder encode, bool present, int target)
    {
        var status = encode(present, 0UL, (ulong)target, out ushort encoded, out _,
            location.AtLocal(DataField.Offset, BaseLayout.HorizAxisAt));
        CoverageTableCompiler.RequireWritten(status);
        CoverageTableCompiler.RequireWritten(write(destination, encoded, out _, location));
    }

    private static T KeyAt<T>(Dictionary<T, int> positions, int at) where T : class
    {
        foreach (var pair in positions)
            if (pair.Value == at) return pair.Key;
        throw new InvalidOperationException("A planned position lost its model.");
    }

    private static List<BaseMinMaxTable> MinMaxOrder(BaseScriptTable script,
        List<BaseMinMaxTable> seen)
    {
        var order = new List<BaseMinMaxTable>();
        void Add(BaseMinMaxTable? minMax)
        {
            if (minMax is null || Contains(seen, minMax) is not null) return;
            order.Add(minMax);
        }
        Add(script.DefaultMinMax);
        for (int i = 0; i < script.BaseLangSysCount; i++)
        {
            var minMax = script.LangSys[i].MinMax;
            if (Contains(seen, minMax) is null && !order.Contains(minMax)) order.Add(minMax);
        }
        return order;
    }

    private static IEnumerable<BaseCoordTable> MinMaxCoords(BaseMinMaxTable minMax)
    {
        if (minMax.MinCoord is { } min) yield return min;
        if (minMax.MaxCoord is { } max) yield return max;
        foreach (var record in minMax.FeatMinMax)
        {
            if (record.MinCoord is { } rowMin) yield return rowMin;
            if (record.MaxCoord is { } rowMax) yield return rowMax;
        }
    }

    private static T? Contains<T>(List<T> seen, T candidate) where T : class
    {
        foreach (var item in seen)
            if (ReferenceEquals(item, candidate)) return item;
        return null;
    }

    private static BaseCoordTable? Place(List<BaseCoordTable> seen, BaseCoordTable candidate)
    {
        foreach (var item in seen)
            if (BaseAxisTableCompiler.SameBody(item, candidate)) return item;
        return null;
    }
}
