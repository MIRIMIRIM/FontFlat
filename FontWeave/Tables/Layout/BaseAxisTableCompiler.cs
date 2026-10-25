using FontWeave.Core;

namespace FontWeave.Tables.Layout;

/// <summary>The BASE subtree writers (T4-07 section 4.1): per-block emission and the
/// kind+content identity rules the shared bodies need. Placement itself is global -
/// the two witnesses' wire is kind-grouped across both axes (header groups, then all
/// scripts, then all values, then all coords, HAN's horiz/vert scripts interleaving
/// 140/146/152/158), so the root owns the cursor; this class writes each block into
/// its planned window and answers the identity questions. Shared bodies dedupe by
/// ReferenceEquals first, then by content identity (the T4-01 shared-Coverage /
/// GDEF ClassDef rules) - never merged when unequal, never duplicated when equal.</summary>
public static class BaseAxisTableCompiler
{
    /// <summary>Every wire instance encountered during planning maps to its
    /// content-representative's position, so writers never re-run identity scans.</summary>
    internal sealed class AxisLayoutPlan
    {
        internal Dictionary<BaseScriptTable, int> Scripts { get; } = new(ReferenceEqualityComparer.Instance);
        internal Dictionary<BaseValuesTable, int> Values { get; } = new(ReferenceEqualityComparer.Instance);
        internal Dictionary<BaseMinMaxTable, int> MinMax { get; } = new(ReferenceEqualityComparer.Instance);
        internal Dictionary<BaseCoordTable, int> Coords { get; } = new(ReferenceEqualityComparer.Instance);
    }

    // ---- sizing (the writers' placement strides; devices ride inside coord blocks) ----

    internal static int TagListLength(BaseTagListTable tagList) =>
        BaseTagListLayout.HeaderLength + BaseTagListLayout.TagStride * tagList.TagCount;
    internal static int ScriptListLength(BaseAxisTable axis) =>
        BaseScriptListLayout.HeaderLength + BaseScriptListLayout.RecordStride * axis.ScriptCount;
    internal static int ScriptLength(BaseScriptTable script) =>
        BaseScriptLayout.HeaderLength + BaseScriptLayout.RecordStride * script.BaseLangSysCount;
    internal static int ValuesLength(BaseValuesTable values) =>
        BaseValuesLayout.HeaderLength + BaseValuesLayout.CoordStride * values.CoordCount;
    internal static int MinMaxLength(BaseMinMaxTable minMax) =>
        BaseMinMaxLayout.HeaderLength + BaseMinMaxLayout.RecordStride * minMax.FeatMinMaxCount;

    // ---- explicit model factories (the CursivePos family shape: public record
    // structs carry the rows, the compilers admit and own the class models; every
    // child is caller-supplied and never inferred) ----

    public static ReadStatus TryCreate(ReadOnlySpan<uint> tags, out BaseTagListTable? model,
        out FontDiagnostic diagnostic, BuildLimits? limits = null, DataLocation location = default)
    {
        model = null;
        var budget = new BuildBudget(limits);
        if (!budget.Work(TagListLength(new(tags.ToArray(), location, 0)), location, out diagnostic))
            return ReadStatus.ResourceLimit;
        if (!budget.Buffer(4L * tags.Length, location, out diagnostic)) return ReadStatus.ResourceLimit;
        model = new(tags.ToArray(), location, BaseTagListLayout.HeaderLength + BaseTagListLayout.TagStride * tags.Length);
        diagnostic = default;
        return ReadStatus.Done;
    }

    public static ReadStatus TryCreate(int defaultBaselineIndex, ReadOnlySpan<BaseCoordTable> coords,
        out BaseValuesTable? model, out FontDiagnostic diagnostic, BuildLimits? limits = null,
        DataLocation location = default)
    {
        model = null;
        if (defaultBaselineIndex is < 0 or > ushort.MaxValue)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                location.AtLocal(DataField.Payload, BaseValuesLayout.DefaultBaselineIndexAt),
                out diagnostic, unchecked((ulong)defaultBaselineIndex), ushort.MaxValue);
        if (coords.Length > ushort.MaxValue)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                location.AtLocal(DataField.Count, BaseValuesLayout.BaseCoordCountAt),
                out diagnostic, (ulong)coords.Length, ushort.MaxValue);
        var budget = new BuildBudget(limits);
        var copy = coords.ToArray();
        foreach (var coord in copy)
            if (coord is null)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    location.AtLocal(DataField.Offset, BaseValuesLayout.HeaderLength), out diagnostic);
        if (!budget.Work(ValuesLength(new(defaultBaselineIndex, copy, location, 0)), location,
            out diagnostic)) return ReadStatus.ResourceLimit;
        if (!budget.Buffer(8L * copy.Length, location, out diagnostic)) return ReadStatus.ResourceLimit;
        model = new(defaultBaselineIndex, copy, location,
            BaseValuesLayout.HeaderLength + BaseValuesLayout.CoordStride * copy.Length);
        diagnostic = default;
        return ReadStatus.Done;
    }

    public static ReadStatus TryCreate(BaseValuesTable? baseValues, BaseMinMaxTable? defaultMinMax,
        ReadOnlySpan<BaseLangSysRecord> langSys, out BaseScriptTable? model,
        out FontDiagnostic diagnostic, BuildLimits? limits = null, DataLocation location = default)
    {
        model = null;
        var budget = new BuildBudget(limits);
        var copy = langSys.ToArray();
        for (int i = 0; i < copy.Length; i++)
            if (copy[i].MinMax is null)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    location.AtLocal(DataField.Offset,
                        BaseScriptLayout.HeaderLength + (ulong)i * BaseScriptLayout.RecordStride
                        + BaseScriptLayout.RecordOffsetField, i), out diagnostic);
        if (copy.Length > ushort.MaxValue)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                location.AtLocal(DataField.Count, BaseScriptLayout.BaseLangSysCountAt),
                out diagnostic, (ulong)copy.Length, ushort.MaxValue);
        if (!budget.Work(ScriptLength(new(baseValues, defaultMinMax, copy, location, 0)), location,
            out diagnostic)) return ReadStatus.ResourceLimit;
        if (!budget.Buffer(16L * copy.Length, location, out diagnostic)) return ReadStatus.ResourceLimit;
        model = new(baseValues, defaultMinMax, copy, location,
            BaseScriptLayout.HeaderLength + BaseScriptLayout.RecordStride * copy.Length);
        diagnostic = default;
        return ReadStatus.Done;
    }

    public static ReadStatus TryCreate(BaseTagListTable? tagList, ReadOnlySpan<BaseScriptRecord> scripts,
        out BaseAxisTable? model, out FontDiagnostic diagnostic, BuildLimits? limits = null,
        DataLocation location = default)
    {
        model = null;
        var budget = new BuildBudget(limits);
        var copy = scripts.ToArray();
        for (int i = 0; i < copy.Length; i++)
            if (copy[i].Script is null)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    location.AtLocal(DataField.Offset,
                        BaseScriptListLayout.HeaderLength + (ulong)i * BaseScriptListLayout.RecordStride
                        + BaseScriptListLayout.RecordOffsetField, i), out diagnostic);
        if (copy.Length > ushort.MaxValue)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                location.AtLocal(DataField.Count, BaseScriptListLayout.HeaderLength),
                out diagnostic, (ulong)copy.Length, ushort.MaxValue);
        if (!budget.Work(ScriptListLength(new(tagList, copy, location, 0)), location,
            out diagnostic)) return ReadStatus.ResourceLimit;
        if (!budget.Buffer(16L * copy.Length, location, out diagnostic)) return ReadStatus.ResourceLimit;
        model = new(tagList, copy, location,
            BaseAxisLayout.HeaderLength + BaseScriptListLayout.RecordStride * copy.Length);
        diagnostic = default;
        return ReadStatus.Done;
    }


    // ---- kind+content identity (ReferenceEquals first, content second) ----

    internal static bool SameBody(BaseTagListTable? left, BaseTagListTable? right) =>
        ReferenceEquals(left, right) || (left is not null && right is not null &&
            left.TagCount == right.TagCount && left.Tags.SequenceEqual(right.Tags));

    internal static bool SameBody(StaticDeviceTable? left, StaticDeviceTable? right) =>
        ReferenceEquals(left, right) || (left is not null && right is not null &&
            left.DeltaFormat == right.DeltaFormat && left.StartSize == right.StartSize &&
            left.EndSize == right.EndSize && left.Deltas.SequenceEqual(right.Deltas));

    internal static bool SameBody(BaseCoordTable? left, BaseCoordTable? right) =>
        ReferenceEquals(left, right) || (left is not null && right is not null &&
            left.Format == right.Format && left.Coordinate == right.Coordinate &&
            left.ReferenceGlyph == right.ReferenceGlyph &&
            left.BaseCoordPoint == right.BaseCoordPoint &&
            SameBody(left.Device, right.Device));

    internal static bool SameBody(BaseValuesTable? left, BaseValuesTable? right)
    {
        if (ReferenceEquals(left, right)) return true;
        if (left is null || right is null ||
            left.DefaultBaselineIndex != right.DefaultBaselineIndex ||
            left.CoordCount != right.CoordCount) return false;
        for (int i = 0; i < left.CoordCount; i++)
            if (!SameBody(left.Coords[i], right.Coords[i])) return false;
        return true;
    }

    internal static bool SameBody(BaseMinMaxTable? left, BaseMinMaxTable? right)
    {
        if (ReferenceEquals(left, right)) return true;
        if (left is null || right is null ||
            !SameBody(left.MinCoord, right.MinCoord) || !SameBody(left.MaxCoord, right.MaxCoord) ||
            left.FeatMinMaxCount != right.FeatMinMaxCount) return false;
        for (int i = 0; i < left.FeatMinMaxCount; i++)
        {
            var l = left.FeatMinMax[i];
            var r = right.FeatMinMax[i];
            if (l.Tag != r.Tag || !SameBody(l.MinCoord, r.MinCoord) || !SameBody(l.MaxCoord, r.MaxCoord))
                return false;
        }
        return true;
    }

    internal static bool SameBody(BaseScriptTable? left, BaseScriptTable? right)
    {
        if (ReferenceEquals(left, right)) return true;
        if (left is null || right is null ||
            !SameBody(left.BaseValues, right.BaseValues) ||
            !SameBody(left.DefaultMinMax, right.DefaultMinMax) ||
            left.BaseLangSysCount != right.BaseLangSysCount) return false;
        for (int i = 0; i < left.BaseLangSysCount; i++)
        {
            var l = left.LangSys[i];
            var r = right.LangSys[i];
            if (l.Tag != r.Tag || !SameBody(l.MinMax, r.MinMax)) return false;
        }
        return true;
    }

    // ---- block writers (only a fully preflighted plan reaches them) ----

    internal static void WriteAxis(BaseAxisTable axis, Span<byte> destination, int at,
        int tagListPosition, int scriptListPosition, DataLocation location)
    {
        var slice = destination.Slice(at);
        var slotLocation = location.AtLocal(DataField.Offset, 0);
        // The absent tagList keeps the plan's -1 sentinel (the preflight form): the
        // slot encodes zero through present=false, never a flattened position whose
        // subtraction against the axis start would underflow.
        var status = BaseAxisView.TryGetBaseTagListOffsetValue(axis.TagList is not null, 0UL,
            tagListPosition < 0 ? 0UL : (ulong)(tagListPosition - at), out ushort tagListOffset,
            out _, slotLocation);
        CoverageTableCompiler.RequireWritten(status);
        CoverageTableCompiler.RequireWritten(BaseAxisView.TryWriteBaseTagListOffset(slice,
            tagListOffset, out _, location));
        status = BaseAxisView.TryGetBaseScriptListOffsetValue(present: true, 0UL,
            (ulong)(scriptListPosition - at), out ushort scriptListOffset, out _, slotLocation);
        CoverageTableCompiler.RequireWritten(status);
        CoverageTableCompiler.RequireWritten(BaseAxisView.TryWriteBaseScriptListOffset(slice,
            scriptListOffset, out _, location));
    }

    internal static void WriteTagList(BaseTagListTable tagList, Span<byte> destination, int at,
        DataLocation location)
    {
        var slice = destination.Slice(at);
        CoverageTableCompiler.RequireWritten(BaseTagListView.TryWriteBaseTagCount(slice,
            (ushort)tagList.TagCount, out _, location));
        for (int i = 0; i < tagList.TagCount; i++)
            CoverageTableCompiler.RequireWritten(BaseTagListView.TryWriteBaselineTag(slice,
                (ulong)tagList.TagCount, new BaseTagListView.BaselineTagData(i, tagList.Tags[i]),
                out _, location));
    }

    internal static void WriteScriptList(BaseAxisTable axis, Span<byte> destination, int at,
        AxisLayoutPlan plan, DataLocation location)
    {
        var slice = destination.Slice(at);
        CoverageTableCompiler.RequireWritten(BaseScriptListView.TryWriteBaseScriptCount(slice,
            (ushort)axis.ScriptCount, out _, location));
        for (int i = 0; i < axis.ScriptCount; i++)
        {
            var record = axis.Scripts[i];
            int target = plan.Scripts[record.Script] - at;
            var status = BaseScriptListView.TryGetBaseScriptOffsetValue(present: true, 0UL,
                (ulong)target, out ushort offset, out _,
                location.AtLocal(DataField.Offset,
                    BaseScriptListLayout.HeaderLength + (ulong)i * BaseScriptListLayout.RecordStride
                    + BaseScriptListLayout.RecordOffsetField, i));
            CoverageTableCompiler.RequireWritten(status);
            CoverageTableCompiler.RequireWritten(BaseScriptListView.TryWriteBaseScriptRecord(slice,
                (ulong)axis.ScriptCount,
                new BaseScriptListView.BaseScriptRecordData(i, record.Tag, offset), out _, location));
        }
    }

    internal static void WriteScript(BaseScriptTable script, Span<byte> destination, int at,
        AxisLayoutPlan plan, DataLocation location)
    {
        var slice = destination.Slice(at);
        var status = BaseScriptView.TryGetBaseValuesOffsetValue(script.BaseValues is not null, 0UL,
            (ulong)(script.BaseValues is null ? 0 : plan.Values[script.BaseValues] - at),
            out ushort valuesOffset, out _, location.AtLocal(DataField.Offset, BaseScriptLayout.BaseValuesOffsetField));
        CoverageTableCompiler.RequireWritten(status);
        CoverageTableCompiler.RequireWritten(BaseScriptView.TryWriteBaseValuesOffset(slice,
            valuesOffset, out _, location));
        status = BaseScriptView.TryGetDefaultMinMaxOffsetValue(script.DefaultMinMax is not null, 0UL,
            (ulong)(script.DefaultMinMax is null ? 0 : plan.MinMax[script.DefaultMinMax] - at),
            out ushort defaultMinMaxOffset, out _,
            location.AtLocal(DataField.Offset, BaseScriptLayout.DefaultMinMaxOffsetField));
        CoverageTableCompiler.RequireWritten(status);
        CoverageTableCompiler.RequireWritten(BaseScriptView.TryWriteDefaultMinMaxOffset(slice,
            defaultMinMaxOffset, out _, location));
        CoverageTableCompiler.RequireWritten(BaseScriptView.TryWriteBaseLangSysCount(slice,
            (ushort)script.BaseLangSysCount, out _, location));
        for (int i = 0; i < script.BaseLangSysCount; i++)
        {
            var record = script.LangSys[i];
            int target = plan.MinMax[record.MinMax] - at;
            status = BaseScriptView.TryGetMinMaxOffsetValue(present: true, 0UL, (ulong)target,
                out ushort offset, out _,
                location.AtLocal(DataField.Offset,
                    BaseScriptLayout.HeaderLength + (ulong)i * BaseScriptLayout.RecordStride
                    + BaseScriptLayout.RecordOffsetField, i));
            CoverageTableCompiler.RequireWritten(status);
            CoverageTableCompiler.RequireWritten(BaseScriptView.TryWriteBaseLangSysRecord(slice,
                (ulong)script.BaseLangSysCount,
                new BaseScriptView.BaseLangSysRecordData(i, record.Tag, offset), out _, location));
        }
    }

    internal static void WriteValues(BaseValuesTable values, Span<byte> destination, int at,
        AxisLayoutPlan plan, DataLocation location)
    {
        var slice = destination.Slice(at);
        CoverageTableCompiler.RequireWritten(BaseValuesView.TryWriteDefaultBaselineIndex(slice,
            (ushort)values.DefaultBaselineIndex, out _, location));
        CoverageTableCompiler.RequireWritten(BaseValuesView.TryWriteBaseCoordCount(slice,
            (ushort)values.CoordCount, out _, location));
        for (int i = 0; i < values.CoordCount; i++)
        {
            int target = plan.Coords[values.Coords[i]] - at;
            var status = BaseValuesView.TryGetCoordOffsetValue(present: true, 0UL, (ulong)target,
                out ushort offset, out _,
                location.AtLocal(DataField.Offset,
                    BaseValuesLayout.HeaderLength + (ulong)i * BaseValuesLayout.CoordStride, i));
            CoverageTableCompiler.RequireWritten(status);
            CoverageTableCompiler.RequireWritten(BaseValuesView.TryWriteBaseCoordOffset(slice,
                (ulong)values.CoordCount, new BaseValuesView.BaseCoordOffsetData(i, offset),
                out _, location));
        }
    }

    internal static void WriteMinMax(BaseMinMaxTable minMax, Span<byte> destination, int at,
        AxisLayoutPlan plan, DataLocation location)
    {
        var slice = destination.Slice(at);
        var status = BaseMinMaxView.TryGetMinCoordOffsetValue(minMax.MinCoord is not null, 0UL,
            (ulong)(minMax.MinCoord is null ? 0 : plan.Coords[minMax.MinCoord] - at),
            out ushort minOffset, out _, location.AtLocal(DataField.Offset, BaseMinMaxLayout.MinCoordOffsetField));
        CoverageTableCompiler.RequireWritten(status);
        CoverageTableCompiler.RequireWritten(BaseMinMaxView.TryWriteMinCoordOffset(slice,
            minOffset, out _, location));
        status = BaseMinMaxView.TryGetMaxCoordOffsetValue(minMax.MaxCoord is not null, 0UL,
            (ulong)(minMax.MaxCoord is null ? 0 : plan.Coords[minMax.MaxCoord] - at),
            out ushort maxOffset, out _, location.AtLocal(DataField.Offset, BaseMinMaxLayout.MaxCoordOffsetField));
        CoverageTableCompiler.RequireWritten(status);
        CoverageTableCompiler.RequireWritten(BaseMinMaxView.TryWriteMaxCoordOffset(slice,
            maxOffset, out _, location));
        CoverageTableCompiler.RequireWritten(BaseMinMaxView.TryWriteBaseFeatMinMaxCount(slice,
            (ushort)minMax.FeatMinMaxCount, out _, location));
        for (int i = 0; i < minMax.FeatMinMaxCount; i++)
        {
            var record = minMax.FeatMinMax[i];
            status = BaseMinMaxView.TryGetFeatMinCoordOffsetValue(record.MinCoord is not null, 0UL,
                (ulong)(record.MinCoord is null ? 0 : plan.Coords[record.MinCoord] - at),
                out ushort minCoordOffset, out _,
                location.AtLocal(DataField.Offset,
                    BaseMinMaxLayout.HeaderLength + (ulong)i * BaseMinMaxLayout.RecordStride
                    + BaseMinMaxLayout.RecordMinOffsetField, i));
            CoverageTableCompiler.RequireWritten(status);
            status = BaseMinMaxView.TryGetFeatMaxCoordOffsetValue(record.MaxCoord is not null, 0UL,
                (ulong)(record.MaxCoord is null ? 0 : plan.Coords[record.MaxCoord] - at),
                out ushort maxCoordOffset, out _,
                location.AtLocal(DataField.Offset,
                    BaseMinMaxLayout.HeaderLength + (ulong)i * BaseMinMaxLayout.RecordStride
                    + BaseMinMaxLayout.RecordMaxOffsetField, i));
            CoverageTableCompiler.RequireWritten(status);
            CoverageTableCompiler.RequireWritten(BaseMinMaxView.TryWriteFeatMinMaxRecord(slice,
                (ulong)minMax.FeatMinMaxCount,
                new BaseMinMaxView.FeatMinMaxRecordData(i, record.Tag, minCoordOffset, maxCoordOffset),
                out _, location));
        }
    }
}
