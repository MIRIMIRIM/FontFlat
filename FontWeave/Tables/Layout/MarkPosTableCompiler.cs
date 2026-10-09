using FontWeave.Core;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;

namespace FontWeave.Tables.Layout;

// Immutable leaf fragment plan (the CursivePosPartPlan shape): one mark subtable is
// one part - no internal split (the T4-01/03/05 discipline; the type-9 wrap-all
// candidate is the oversize rescue). Canonical order: 12-byte header, mark coverage,
// second coverage, mark array (markCount + records in mark-coverage index order,
// class values carried verbatim), matrix side (BaseArray/Mark2Array rows or the
// LigatureArray attach bodies), anchor bodies, anchor Device leaves. The subtable
// offsets are subtable-relative; mark-record anchors are MarkArray-relative and
// matrix anchors are matrix-array-relative (spec gpos.md section 2).
internal sealed record MarkPosPartPlan(int Length, int MarkCoverageTarget, int SecondCoverageTarget,
    int MarkArrayTarget, int MatrixTarget, int AttachArrayTarget, MarkPosTableCompiler.AnchorLeaf[] Anchors,
    Dictionary<AnchorTable, int> AnchorTargets, MarkPosTableCompiler.DeviceLeaf[] Devices,
    Dictionary<StaticDeviceTable, int> DeviceTargets, long EmissionWork);

/// <summary>The mark-family leaf compilers (GPOS lookup types 4/5/6, T4-06 section
/// 4.1): three planners/emitters over one shared anchor/device placement tail - the
/// wire shapes share the 12-byte header, the MarkArray of class+anchor records and
/// the classCount x 2 matrix rows whose stride the generator cannot declare
/// (OQ-T4-06-1's hand-written boundary, the PairSet value-row precedent); type 5
/// adds the LigatureArray hop. Shared anchors/devices dedupe by model identity,
/// never by equal addresses. Overflow is the finite refusal: every Offset16 slot
/// preflights through the generated too-narrow value helpers, so an unreachable
/// target refuses UnsupportedFormat/UnsupportedOperation with the slot's
/// provenance; the matrix row words are the hand-written fixups (no generated
/// reference exists for them) and preflight the same reach bound.</summary>
public static class MarkPosTableCompiler
{
    internal sealed class AnchorLeaf(AnchorTable model, int position)
    {
        internal AnchorTable Model { get; } = model;
        internal int Position { get; set; } = position;
    }

    internal sealed class DeviceLeaf(StaticDeviceTable model, int position)
    {
        internal StaticDeviceTable Model { get; } = model;
        internal int Position { get; set; } = position;
    }

    // One anchor use: the array-relative base its offset is encoded against and the
    // slot's field provenance for the too-narrow refusal.
    private readonly struct AnchorUse(AnchorTable anchor, int basePosition, DataField field, ulong fieldAt)
    {
        internal AnchorTable Anchor { get; } = anchor;
        internal int BasePosition { get; } = basePosition;
        internal DataField Field { get; } = field;
        internal ulong FieldAt { get; } = fieldAt;
    }

    internal static int MeasureAnchor(AnchorTable anchor) => anchor.Format switch
    {
        1 => AnchorLayout.MinimumHeader,
        2 => AnchorLayout.MinimumHeader + 2,
        _ => AnchorLayout.Format3Length + (anchor.XDevice is null ? 0 : StaticDeviceTableCompiler.Measure(anchor.XDevice))
            + (anchor.YDevice is null ? 0 : StaticDeviceTableCompiler.Measure(anchor.YDevice)),
    };

    private static int MeasureAnchorBody(AnchorTable anchor) => anchor.Format switch
    {
        1 => AnchorLayout.MinimumHeader,
        2 => AnchorLayout.MinimumHeader + 2,
        _ => AnchorLayout.Format3Length,
    };

    private static ReadStatus CheckSource(AnchorTable anchor, DataLocation location, int ordinal,
        out FontDiagnostic diagnostic)
    {
        if (anchor.Location.SourceId != location.SourceId ||
            anchor.Location.FaceIndex != location.FaceIndex ||
            location.SourceId != 0 && anchor.Location.TableTag != location.TableTag)
            return ReadWire.Fail(ReadStatus.InvalidSource, DiagnosticCode.WrongSourceFace,
                location.AtLocal(DataField.None, 0, ordinal), out diagnostic);
        diagnostic = default;
        return ReadStatus.Done;
    }

    private static void CollectUses(MarkRecord[] marks, int markArrayAt,
        IEnumerable<(AnchorTable? anchor, int basePosition, DataField field, ulong fieldAt)> matrix,
        List<AnchorUse> uses)
    {
        foreach (var record in marks)
            uses.Add(new(record.Anchor, markArrayAt, DataField.Offset, 2));
        foreach (var (anchor, basePosition, field, fieldAt) in matrix)
            if (anchor is not null)
                uses.Add(new(anchor, basePosition, field, fieldAt));
    }

    // The shared tail: dedupe anchors/devices by identity, place anchor bodies
    // largest-last from `anchorBase`, devices after them, preflight every anchor
    // offset (the generated value helper's too-narrow refusal = the finite
    // overflow refusal, at the slot's own provenance) and every Device delta.
    private static ReadStatus PlaceTail(DataLocation location, List<AnchorUse> uses, long anchorBase,
        BuildBudget budget, out MarkPosTableCompiler.AnchorLeaf[] anchors,
        out Dictionary<AnchorTable, int> anchorTargets, out DeviceLeaf[] devices,
        out Dictionary<StaticDeviceTable, int> deviceTargets, out long end, out long emission,
        out FontDiagnostic diagnostic)
    {
        anchors = [];
        devices = [];
        anchorTargets = new(ReferenceEqualityComparer.Instance);
        deviceTargets = new(ReferenceEqualityComparer.Instance);
        end = 0;
        emission = 0;
        var anchorList = new List<AnchorLeaf>();
        var deviceList = new List<DeviceLeaf>();
        foreach (var use in uses)
        {
            if (anchorTargets.ContainsKey(use.Anchor)) continue;
            if (!budget.Buffer(16, location, out diagnostic)) return ReadStatus.ResourceLimit;
            anchorTargets.Add(use.Anchor, 0);
            anchorList.Add(new(use.Anchor, 0));
            if (use.Anchor.XDevice is not null && !deviceTargets.ContainsKey(use.Anchor.XDevice))
            {
                deviceTargets.Add(use.Anchor.XDevice, 0);
                deviceList.Add(new(use.Anchor.XDevice, 0));
            }
            if (use.Anchor.YDevice is not null && !deviceTargets.ContainsKey(use.Anchor.YDevice))
            {
                deviceTargets.Add(use.Anchor.YDevice, 0);
                deviceList.Add(new(use.Anchor.YDevice, 0));
            }
        }
        int largest = -1;
        for (int i = 0; i < anchorList.Count; i++)
            if (largest < 0 || MeasureAnchorBody(anchorList[i].Model) > MeasureAnchorBody(anchorList[largest].Model))
                largest = i;
        long at = anchorBase;
        for (int i = 0; i < anchorList.Count; i++)
        {
            if (i == largest) continue;
            anchorList[i].Position = (int)at;
            at += MeasureAnchorBody(anchorList[i].Model);
        }
        if (largest >= 0)
        {
            anchorList[largest].Position = (int)at;
            at += MeasureAnchorBody(anchorList[largest].Model);
        }
        foreach (var leaf in anchorList) anchorTargets[leaf.Model] = leaf.Position;
        long deviceAt = at;
        foreach (var leaf in deviceList)
        {
            leaf.Position = (int)deviceAt;
            deviceTargets[leaf.Model] = (int)deviceAt;
            deviceAt += StaticDeviceTableCompiler.Measure(leaf.Model);
        }
        // Every anchor offset is array-relative; the widest use preflights through
        // the generated value helper (the registered too-narrow refusal).
        foreach (var use in uses)
        {
            long delta = anchorTargets[use.Anchor] - use.BasePosition;
            if (delta > ushort.MaxValue)
                return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
                    location.AtLocal(use.Field, use.FieldAt), out diagnostic,
                    (ulong)delta, (ulong)ushort.MaxValue);
        }
        foreach (var leaf in anchorList)
        {
            var body = leaf.Model;
            if (body.XDevice is not null && deviceTargets[body.XDevice] - leaf.Position > ushort.MaxValue)
                return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
                    location.AtLocal(DataField.Offset, AnchorLayout.Format3HeaderField),
                    out diagnostic, (ulong)(deviceTargets[body.XDevice] - leaf.Position), (ulong)ushort.MaxValue);
            if (body.YDevice is not null && deviceTargets[body.YDevice] - leaf.Position > ushort.MaxValue)
                return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
                    location.AtLocal(DataField.Offset, AnchorLayout.Format3HeaderField + 2),
                    out diagnostic, (ulong)(deviceTargets[body.YDevice] - leaf.Position), (ulong)ushort.MaxValue);
        }
        anchors = anchorList.ToArray();
        devices = deviceList.ToArray();
        end = deviceAt;
        emission = deviceAt + deviceList.Count;
        diagnostic = default;
        return ReadStatus.Done;
    }

    private static void WriteAnchorLeaves(MarkPosPartPlan plan, Span<byte> destination, DataLocation location)
    {
        foreach (var leaf in plan.Anchors)
        {
            var body = leaf.Model;
            var slice = destination.Slice(leaf.Position, MeasureAnchorBody(body));
            CoverageTableCompiler.RequireWritten(AnchorView.TryWriteAnchorFormat(slice, body.Format, out _, location));
            CoverageTableCompiler.RequireWritten(AnchorView.TryWriteXCoordinate(slice, body.XCoordinate, out _, location));
            CoverageTableCompiler.RequireWritten(AnchorView.TryWriteYCoordinate(slice, body.YCoordinate, out _, location));
            if (body.Format == 2)
                CoverageTableCompiler.RequireWritten(AnchorView.TryWriteFormat2HeaderAt(slice,
                    AnchorLayout.Format2HeaderField, new AnchorView.Format2HeaderData(-1, body.AnchorPoint),
                    out _, location));
            if (body.Format == 3)
            {
                ushort x = body.XDevice is StaticDeviceTable device ? (ushort)(plan.DeviceTargets[device] - leaf.Position) : (ushort)0;
                ushort y = body.YDevice is StaticDeviceTable device2 ? (ushort)(plan.DeviceTargets[device2] - leaf.Position) : (ushort)0;
                CoverageTableCompiler.RequireWritten(AnchorView.TryWriteFormat3HeaderAt(slice,
                    AnchorLayout.Format3HeaderField, new AnchorView.Format3HeaderData(-1, x, y), out _, location));
            }
        }
        foreach (var leaf in plan.Devices)
            StaticDeviceTableCompiler.WritePlanned(leaf.Model, destination.Slice(leaf.Position));
    }

    // The shared matrix-row writer: classCount Offset16 cells per row, each either
    // the array-relative anchor target or the legal no-op 0 (the hand-written
    // fixups; a present anchor's reach was preflighted at planning).
    private static void WriteMatrixRow(Span<byte> destination, long rowAt, ReadOnlySpan<AnchorTable?> row,
        MarkPosPartPlan plan, int arrayAt)
    {
        for (int c = 0; c < row.Length; c++)
        {
            ushort offset = row[c] is AnchorTable body ? (ushort)(plan.AnchorTargets[body] - arrayAt) : (ushort)0;
            BinaryPrimitives.WriteUInt16BigEndian(destination.Slice((int)(rowAt + 2L * c)), offset);
        }
    }

    private static void WriteMarkArray(Span<byte> destination, MarkRecord[] marks, MarkPosPartPlan plan,
        DataLocation location)
    {
        // MarkArrayView writers address from the array's own start; callers pass the
        // whole subtable span, so rebase once here — writing at the subtable start
        // would clobber the format word (the three families share this helper).
        Span<byte> body = destination.Slice(plan.MarkArrayTarget,
            MarkArrayLayout.MinimumHeader + marks.Length * MarkArrayLayout.RecordStride);
        CoverageTableCompiler.RequireWritten(MarkArrayView.TryWriteMarkCount(body,
            (ushort)marks.Length, out _, location));
        for (int i = 0; i < marks.Length; i++)
        {
            ushort anchor = (ushort)(plan.AnchorTargets[marks[i].Anchor] - plan.MarkArrayTarget);
            CoverageTableCompiler.RequireWritten(MarkArrayView.TryWriteMarkRecordAt(body,
                MarkArrayLayout.MinimumHeader + (ulong)i * MarkArrayLayout.RecordStride,
                new MarkArrayView.MarkRecordData(i, marks[i].Class, anchor), out _, location));
        }
    }

    // ---------------------------------------------------------------- type 4 --
    public static ReadStatus TryCreate(ReadOnlySpan<ushort> markGlyphs, ReadOnlySpan<MarkRecord> marks,
        ReadOnlySpan<ushort> baseGlyphs, ReadOnlySpan<AnchorTable?> baseCells, ushort classCount,
        int numGlyphs, out MarkBasePosTable? model, out FontDiagnostic diagnostic,
        BuildLimits? limits = null, DataLocation location = default, ushort coverageFormat = 1)
    {
        model = null;
        ReadStatus status = CoverageTableCompiler.CheckNumGlyphs(numGlyphs, location, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (markGlyphs.Length != marks.Length || markGlyphs.Length > ushort.MaxValue ||
            (long)baseGlyphs.Length * classCount != (long)baseCells.Length)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                location.AtLocal(DataField.Count, MarkBasePosLayout.ClassCountField), out diagnostic,
                (ulong)baseCells.Length, (ulong)((long)baseGlyphs.Length * classCount));
        var markCopy = marks.ToArray();
        for (int i = 0; i < markCopy.Length; i++)
        {
            status = CheckSource(markCopy[i].Anchor, location, i, out diagnostic);
            if (status != ReadStatus.Done) return status;
            if (markCopy[i].Class >= classCount)
                return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                    location.AtLocal(DataField.Payload, MarkArrayLayout.MinimumHeader, i),
                    out diagnostic, markCopy[i].Class, (ulong)classCount);
        }
        status = CoverageTableCompiler.TryCreate(markGlyphs, numGlyphs, coverageFormat, out var markCoverage,
            out diagnostic, limits, location);
        if (status != ReadStatus.Done) return status;
        status = CoverageTableCompiler.TryCreate(baseGlyphs, numGlyphs, coverageFormat, out var baseCoverage,
            out diagnostic, limits, location);
        if (status != ReadStatus.Done) return status;
        var rows = new AnchorTable?[baseGlyphs.Length][];
        for (int i = 0; i < rows.Length; i++)
        {
            var row = new AnchorTable?[classCount];
            for (int c = 0; c < classCount; c++)
            {
                var anchor = baseCells[i * classCount + c];
                if (anchor is not null)
                {
                    status = CheckSource(anchor, location, i, out diagnostic);
                    if (status != ReadStatus.Done) return status;
                }
                row[c] = anchor;
            }
            rows[i] = row;
        }
        long consumed = MarkBasePosLayout.MinimumHeader +
            CoverageTableCompiler.Measure(markCoverage!) + CoverageTableCompiler.Measure(baseCoverage!) +
            MarkArrayLayout.MinimumHeader + (long)markCopy.Length * MarkArrayLayout.RecordStride +
            MarkAnchorMatrix.ArrayHeaderLength + (long)rows.Length * 2L * classCount;
        foreach (var record in markCopy) consumed += MeasureAnchor(record.Anchor);
        for (int i = 0; i < rows.Length; i++)
            foreach (var anchor in rows[i])
                if (anchor is not null) consumed += MeasureAnchor(anchor);
        model = new(markCoverage!, markCopy, baseCoverage!, classCount, rows, numGlyphs, location, (int)consumed);
        diagnostic = default;
        return ReadStatus.Done;
    }

    public static ReadStatus TryCompile(MarkBasePosTable model, out byte[]? output,
        out FontDiagnostic diagnostic, BuildLimits? limits = null) =>
        TryCompile(model, out output, out diagnostic, new BuildBudget(limits));

    internal static ReadStatus TryCompile(MarkBasePosTable model, out byte[]? output,
        out FontDiagnostic diagnostic, BuildBudget budget)
    {
        output = null;
        ReadStatus status = TryPlanSubtables(model, out var plan, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        if (!budget.Buffer(plan!.Length, model.Location, out diagnostic)) return ReadStatus.ResourceLimit;
        byte[] bytes = new byte[plan.Length];
        WritePlanned(model, plan, bytes);
        budget.RecordCompiledTable(bytes.Length);
        output = bytes;
        diagnostic = default;
        return ReadStatus.Done;
    }

    public static ReadStatus TryWrite(MarkBasePosTable model, Span<byte> destination,
        out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        var budget = new BuildBudget(limits);
        ReadStatus status = TryPlanSubtables(model, out var plan, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        if (destination.Length < plan!.Length)
            return ReadWire.Fail(ReadStatus.DestinationTooSmall, DiagnosticCode.DestinationTooSmall,
                model.Location.AtLocal(DataField.OutputBytes, 0), out diagnostic,
                (ulong)destination.Length, (ulong)plan.Length);
        WritePlanned(model, plan, destination);
        diagnostic = default;
        return ReadStatus.Done;
    }

    internal static ReadStatus TryPlanSubtables(MarkBasePosTable model, out MarkPosPartPlan? plan,
        out FontDiagnostic diagnostic, BuildBudget budget)
    {
        ReadStatus status;
        plan = null;
        var location = model.Location;
        int markCount = model.MarkCount, baseCount = model.BaseCount;
        if (!budget.Work(32L * markCount + baseCount + 1, location, out diagnostic))
            return ReadStatus.ResourceLimit;
        long fixedBytes = MarkBasePosLayout.MinimumHeader
            + CoverageTableCompiler.Measure(model.MarkCoverage)
            + CoverageTableCompiler.Measure(model.BaseCoverage)
            + MarkArrayLayout.MinimumHeader + (long)markCount * MarkArrayLayout.RecordStride
            + MarkAnchorMatrix.ArrayHeaderLength + (long)baseCount * 2L * model.ClassCount;
        if (!budget.Work(fixedBytes, location, out diagnostic)) return ReadStatus.ResourceLimit;
        long markArrayAt = MarkBasePosLayout.MinimumHeader
            + CoverageTableCompiler.Measure(model.MarkCoverage)
            + CoverageTableCompiler.Measure(model.BaseCoverage);
        int matrixAt = (int)markArrayAt + MarkArrayLayout.MinimumHeader + markCount * MarkArrayLayout.RecordStride;
        var uses = new List<AnchorUse>();
        foreach (var record in model.Marks)
            uses.Add(new(record.Anchor, (int)markArrayAt, DataField.Offset, MarkBasePosLayout.MarkArrayField));
        for (int i = 0; i < baseCount; i++)
        {
            var row = model.BaseRow(i);
            for (int c = 0; c < row.Length; c++)
                if (row[c] is AnchorTable body)
                    uses.Add(new(body, matrixAt, DataField.Offset,
                        MarkBasePosLayout.BaseArrayField));
        }
        status = PlaceTail(location, uses, fixedBytes, budget, out var anchors, out var anchorTargets,
            out var devices, out var deviceTargets, out long end, out long emission, out diagnostic);
        if (status != ReadStatus.Done) return status;
        plan = new((int)end, MarkBasePosLayout.MinimumHeader,
            MarkBasePosLayout.MinimumHeader + CoverageTableCompiler.Measure(model.MarkCoverage),
            (int)markArrayAt, matrixAt, 0, anchors, anchorTargets, devices, deviceTargets, emission);
        diagnostic = default;
        return ReadStatus.Done;
    }

    internal static void WritePlanned(MarkBasePosTable model, MarkPosPartPlan plan, Span<byte> destination)
    {
        var location = model.Location;
        CoverageTableCompiler.RequireWritten(MarkBasePosView.TryWritePosFormat(destination, 1, out _, location));
        CoverageTableCompiler.RequireWritten(MarkBasePosView.TryWriteMarkCoverageOffset(destination,
            (ushort)plan.MarkCoverageTarget, out _, location));
        CoverageTableCompiler.RequireWritten(MarkBasePosView.TryWriteBaseCoverageOffset(destination,
            (ushort)plan.SecondCoverageTarget, out _, location));
        CoverageTableCompiler.RequireWritten(MarkBasePosView.TryWriteClassCount(destination,
            model.ClassCount, out _, location));
        CoverageTableCompiler.RequireWritten(MarkBasePosView.TryWriteMarkArrayOffset(destination,
            (ushort)plan.MarkArrayTarget, out _, location));
        CoverageTableCompiler.RequireWritten(MarkBasePosView.TryWriteBaseArrayOffset(destination,
            (ushort)plan.MatrixTarget, out _, location));
        CoverageTableCompiler.WritePlanned(model.MarkCoverage.Glyphs, model.MarkCoverage.Format,
            model.MarkCoverage.RangeCount, destination.Slice(plan.MarkCoverageTarget,
            CoverageTableCompiler.Measure(model.MarkCoverage)), location);
        CoverageTableCompiler.WritePlanned(model.BaseCoverage.Glyphs, model.BaseCoverage.Format,
            model.BaseCoverage.RangeCount, destination.Slice(plan.SecondCoverageTarget,
            CoverageTableCompiler.Measure(model.BaseCoverage)), location);
        WriteMarkArray(destination, model.Marks.ToArray(), plan, location);
        for (int i = 0; i < model.BaseCount; i++)
        {
            long rowAt = plan.MatrixTarget + MarkAnchorMatrix.ArrayHeaderLength + (long)i * 2L * model.ClassCount;
            if (i == 0)
                BinaryPrimitives.WriteUInt16BigEndian(destination.Slice(plan.MatrixTarget),
                    (ushort)model.BaseCount);
            WriteMatrixRow(destination, rowAt, model.BaseRow(i), plan, plan.MatrixTarget);
        }
        WriteAnchorLeaves(plan, destination, location);
    }

    // ---------------------------------------------------------------- type 5 --
    public static ReadStatus TryCreateLig(ReadOnlySpan<ushort> markGlyphs, ReadOnlySpan<MarkRecord> marks,
        ReadOnlySpan<ushort> ligGlyphs, ReadOnlySpan<LigatureAttachTable> ligatures,
        int numGlyphs, out MarkLigPosTable? model, out FontDiagnostic diagnostic,
        BuildLimits? limits = null, DataLocation location = default, ushort coverageFormat = 1)
    {
        model = null;
        ReadStatus status = CoverageTableCompiler.CheckNumGlyphs(numGlyphs, location, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (markGlyphs.Length != marks.Length || ligGlyphs.Length != ligatures.Length ||
            markGlyphs.Length > ushort.MaxValue)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                location.AtLocal(DataField.Count, MarkLigPosLayout.LigatureArrayField), out diagnostic,
                (ulong)ligatures.Length, (ulong)ligGlyphs.Length);
        var markCopy = marks.ToArray();
        for (int i = 0; i < markCopy.Length; i++)
        {
            status = CheckSource(markCopy[i].Anchor, location, i, out diagnostic);
            if (status != ReadStatus.Done) return status;
        }
        status = CoverageTableCompiler.TryCreate(markGlyphs, numGlyphs, coverageFormat, out var markCoverage,
            out diagnostic, limits, location);
        if (status != ReadStatus.Done) return status;
        status = CoverageTableCompiler.TryCreate(ligGlyphs, numGlyphs, coverageFormat, out var ligCoverage,
            out diagnostic, limits, location);
        if (status != ReadStatus.Done) return status;
        var ligCopy = ligatures.ToArray();
        long consumed = MarkLigPosLayout.MinimumHeader +
            CoverageTableCompiler.Measure(markCoverage!) + CoverageTableCompiler.Measure(ligCoverage!) +
            MarkArrayLayout.MinimumHeader + (long)markCopy.Length * MarkArrayLayout.RecordStride +
            LigatureArrayLayout.MinimumHeader + (long)ligCopy.Length * LigatureArrayLayout.OffsetStride;
        foreach (var record in markCopy) consumed += MeasureAnchor(record.Anchor);
        foreach (var attach in ligCopy)
        {
            int classCount = 0;
            for (int c = 0; c < attach.Components; c++)
            {
                var row = attach.ComponentRow(c);
                classCount = row.Length;
                foreach (var anchor in row)
                    if (anchor is not null) consumed += MeasureAnchor(anchor);
            }
            consumed += MarkAnchorMatrix.ArrayHeaderLength + (long)attach.Components * 2L * classCount;
        }
        model = new(markCoverage!, markCopy, ligCoverage!, ligCopy, numGlyphs, location, (int)consumed);
        diagnostic = default;
        return ReadStatus.Done;
    }

    public static ReadStatus TryCompile(MarkLigPosTable model, out byte[]? output,
        out FontDiagnostic diagnostic, BuildLimits? limits = null) =>
        TryCompile(model, out output, out diagnostic, new BuildBudget(limits));

    internal static ReadStatus TryCompile(MarkLigPosTable model, out byte[]? output,
        out FontDiagnostic diagnostic, BuildBudget budget)
    {
        output = null;
        ReadStatus status = TryPlanSubtables(model, out var plan, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        if (!budget.Buffer(plan!.Length, model.Location, out diagnostic)) return ReadStatus.ResourceLimit;
        byte[] bytes = new byte[plan.Length];
        WritePlanned(model, plan, bytes);
        budget.RecordCompiledTable(bytes.Length);
        output = bytes;
        diagnostic = default;
        return ReadStatus.Done;
    }

    public static ReadStatus TryWrite(MarkLigPosTable model, Span<byte> destination,
        out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        var budget = new BuildBudget(limits);
        ReadStatus status = TryPlanSubtables(model, out var plan, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        if (destination.Length < plan!.Length)
            return ReadWire.Fail(ReadStatus.DestinationTooSmall, DiagnosticCode.DestinationTooSmall,
                model.Location.AtLocal(DataField.OutputBytes, 0), out diagnostic,
                (ulong)destination.Length, (ulong)plan.Length);
        WritePlanned(model, plan, destination);
        diagnostic = default;
        return ReadStatus.Done;
    }

    internal static ReadStatus TryPlanSubtables(MarkLigPosTable model, out MarkPosPartPlan? plan,
        out FontDiagnostic diagnostic, BuildBudget budget)
    {
        plan = null;
        var location = model.Location;
        int markCount = model.MarkCount, ligCount = model.Ligatures.Count;
        if (!budget.Work(32L * markCount + ligCount + 1, location, out diagnostic))
            return ReadStatus.ResourceLimit;
        long markArrayAt = MarkLigPosLayout.MinimumHeader
            + CoverageTableCompiler.Measure(model.MarkCoverage)
            + CoverageTableCompiler.Measure(model.LigatureCoverage);
        int markArrayTarget = (int)markArrayAt;
        int matrixAt = markArrayTarget + MarkArrayLayout.MinimumHeader + markCount * MarkArrayLayout.RecordStride;
        long attachArrayAt = matrixAt + MarkAnchorMatrix.ArrayHeaderLength;
        for (int i = 0; i < ligCount; i++)
        {
            var attach = model.Ligatures[i];
            int classCount = 0;
            for (int c = 0; c < attach.Components; c++) classCount = attach.ComponentRow(c).Length;
            attachArrayAt += MarkAnchorMatrix.ArrayHeaderLength + (long)attach.Components * 2L * classCount;
        }
        long fixedBytes = attachArrayAt + (long)ligCount * LigatureArrayLayout.OffsetStride
            - MarkLigPosLayout.MinimumHeader;
        if (!budget.Work(fixedBytes, location, out diagnostic)) return ReadStatus.ResourceLimit;
        int attachArrayTarget = matrixAt + MarkAnchorMatrix.ArrayHeaderLength;
        var uses = new List<AnchorUse>();
        foreach (var record in model.Marks)
            uses.Add(new(record.Anchor, markArrayTarget, DataField.Offset, MarkLigPosLayout.MarkArrayField));
        long attachAt = attachArrayTarget + (long)ligCount * LigatureArrayLayout.OffsetStride;
        for (int i = 0; i < ligCount; i++)
        {
            var attach = model.Ligatures[i];
            int classCount = 0;
            for (int c = 0; c < attach.Components; c++) classCount = attach.ComponentRow(c).Length;
            for (int c = 0; c < attach.Components; c++)
            {
                var row = attach.ComponentRow(c);
                for (int k = 0; k < row.Length; k++)
                    if (row[k] is AnchorTable body)
                        uses.Add(new(body, (int)attachAt, DataField.Offset,
                            MarkLigPosLayout.LigatureArrayField));
            }
            attachAt += MarkAnchorMatrix.ArrayHeaderLength + (long)attach.Components * 2L * classCount;
        }
        ReadStatus status = PlaceTail(location, uses, attachAt, budget, out var anchors,
            out var anchorTargets, out var devices, out var deviceTargets, out long end,
            out long emission, out diagnostic);
        if (status != ReadStatus.Done) return status;
        plan = new((int)end, MarkLigPosLayout.MinimumHeader,
            MarkLigPosLayout.MinimumHeader + CoverageTableCompiler.Measure(model.MarkCoverage),
            markArrayTarget, matrixAt, attachArrayTarget, anchors, anchorTargets, devices,
            deviceTargets, emission);
        diagnostic = default;
        return ReadStatus.Done;
    }

    internal static void WritePlanned(MarkLigPosTable model, MarkPosPartPlan plan, Span<byte> destination)
    {
        var location = model.Location;
        CoverageTableCompiler.RequireWritten(MarkLigPosView.TryWritePosFormat(destination, 1, out _, location));
        CoverageTableCompiler.RequireWritten(MarkLigPosView.TryWriteMarkCoverageOffset(destination,
            (ushort)plan.MarkCoverageTarget, out _, location));
        CoverageTableCompiler.RequireWritten(MarkLigPosView.TryWriteLigatureCoverageOffset(destination,
            (ushort)plan.SecondCoverageTarget, out _, location));
        CoverageTableCompiler.RequireWritten(MarkLigPosView.TryWriteClassCount(destination, ClassCountOf(model), out _, location));
        CoverageTableCompiler.RequireWritten(MarkLigPosView.TryWriteMarkArrayOffset(destination,
            (ushort)plan.MarkArrayTarget, out _, location));
        CoverageTableCompiler.RequireWritten(MarkLigPosView.TryWriteLigatureArrayOffset(destination,
            (ushort)plan.MatrixTarget, out _, location));
        CoverageTableCompiler.WritePlanned(model.MarkCoverage.Glyphs, model.MarkCoverage.Format,
            model.MarkCoverage.RangeCount, destination.Slice(plan.MarkCoverageTarget,
            CoverageTableCompiler.Measure(model.MarkCoverage)), location);
        CoverageTableCompiler.WritePlanned(model.LigatureCoverage.Glyphs, model.LigatureCoverage.Format,
            model.LigatureCoverage.RangeCount, destination.Slice(plan.SecondCoverageTarget,
            CoverageTableCompiler.Measure(model.LigatureCoverage)), location);
        WriteMarkArray(destination, model.Marks.ToArray(), plan, location);
        int ligCount = model.Ligatures.Count;
        BinaryPrimitives.WriteUInt16BigEndian(destination.Slice(plan.MatrixTarget), (ushort)ligCount);
        long attachAt = plan.AttachArrayTarget + (long)ligCount * LigatureArrayLayout.OffsetStride;
        for (int i = 0; i < ligCount; i++)
        {
            var attach = model.Ligatures[i];
            int classCount = 0;
            for (int c = 0; c < attach.Components; c++) classCount = attach.ComponentRow(c).Length;
            BinaryPrimitives.WriteUInt16BigEndian(destination.Slice(
                (int)(plan.AttachArrayTarget + (long)i * LigatureArrayLayout.OffsetStride)), (ushort)attachAt);
            BinaryPrimitives.WriteUInt16BigEndian(destination.Slice((int)attachAt), attach.ComponentCount);
            for (int c = 0; c < attach.Components; c++)
                WriteMatrixRow(destination,
                    attachAt + MarkAnchorMatrix.ArrayHeaderLength + (long)c * 2L * classCount,
                    attach.ComponentRow(c), plan, (int)attachAt);
            attachAt += MarkAnchorMatrix.ArrayHeaderLength + (long)attach.Components * 2L * classCount;
        }
        WriteAnchorLeaves(plan, destination, location);
    }

    private static ushort ClassCountOf(MarkLigPosTable model)
    {
        foreach (var attach in model.Ligatures)
        {
            int classCount = 0;
            for (int c = 0; c < attach.Components; c++) classCount = attach.ComponentRow(c).Length;
            return (ushort)classCount;
        }
        return 0;
    }

    // ---------------------------------------------------------------- type 6 --
    public static ReadStatus TryCreateMark(ReadOnlySpan<ushort> markGlyphs, ReadOnlySpan<MarkRecord> marks,
        ReadOnlySpan<ushort> mark2Glyphs, ReadOnlySpan<AnchorTable?> mark2Cells, ushort classCount,
        int numGlyphs, out MarkMarkPosTable? model, out FontDiagnostic diagnostic,
        BuildLimits? limits = null, DataLocation location = default, ushort coverageFormat = 1)
    {
        model = null;
        ReadStatus status = CoverageTableCompiler.CheckNumGlyphs(numGlyphs, location, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (markGlyphs.Length != marks.Length || markGlyphs.Length > ushort.MaxValue ||
            (ulong)mark2Glyphs.Length * classCount != (ulong)mark2Cells.Length)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                location.AtLocal(DataField.Count, MarkMarkPosLayout.ClassCountField), out diagnostic,
                (ulong)mark2Cells.Length, (ulong)((long)mark2Glyphs.Length * classCount));
        var markCopy = marks.ToArray();
        for (int i = 0; i < markCopy.Length; i++)
        {
            status = CheckSource(markCopy[i].Anchor, location, i, out diagnostic);
            if (status != ReadStatus.Done) return status;
            if (markCopy[i].Class >= classCount)
                return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                    location.AtLocal(DataField.Payload, MarkArrayLayout.MinimumHeader, i),
                    out diagnostic, markCopy[i].Class, (ulong)classCount);
        }
        status = CoverageTableCompiler.TryCreate(markGlyphs, numGlyphs, coverageFormat, out var markCoverage,
            out diagnostic, limits, location);
        if (status != ReadStatus.Done) return status;
        status = CoverageTableCompiler.TryCreate(mark2Glyphs, numGlyphs, coverageFormat, out var mark2Coverage,
            out diagnostic, limits, location);
        if (status != ReadStatus.Done) return status;
        var rows = new AnchorTable?[mark2Glyphs.Length][];
        for (int i = 0; i < rows.Length; i++)
        {
            var row = new AnchorTable?[classCount];
            for (int c = 0; c < classCount; c++)
            {
                var anchor = mark2Cells[i * classCount + c];
                if (anchor is not null)
                {
                    status = CheckSource(anchor, location, i, out diagnostic);
                    if (status != ReadStatus.Done) return status;
                }
                row[c] = anchor;
            }
            rows[i] = row;
        }
        long consumed = MarkMarkPosLayout.MinimumHeader +
            CoverageTableCompiler.Measure(markCoverage!) + CoverageTableCompiler.Measure(mark2Coverage!) +
            MarkArrayLayout.MinimumHeader + (long)markCopy.Length * MarkArrayLayout.RecordStride +
            MarkAnchorMatrix.ArrayHeaderLength + (long)rows.Length * 2L * classCount;
        foreach (var record in markCopy) consumed += MeasureAnchor(record.Anchor);
        for (int i = 0; i < rows.Length; i++)
            foreach (var anchor in rows[i])
                if (anchor is not null) consumed += MeasureAnchor(anchor);
        model = new(markCoverage!, markCopy, mark2Coverage!, classCount, rows, numGlyphs, location, (int)consumed);
        diagnostic = default;
        return ReadStatus.Done;
    }

    public static ReadStatus TryCompile(MarkMarkPosTable model, out byte[]? output,
        out FontDiagnostic diagnostic, BuildLimits? limits = null) =>
        TryCompile(model, out output, out diagnostic, new BuildBudget(limits));

    internal static ReadStatus TryCompile(MarkMarkPosTable model, out byte[]? output,
        out FontDiagnostic diagnostic, BuildBudget budget)
    {
        output = null;
        ReadStatus status = TryPlanSubtables(model, out var plan, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        if (!budget.Buffer(plan!.Length, model.Location, out diagnostic)) return ReadStatus.ResourceLimit;
        byte[] bytes = new byte[plan.Length];
        WritePlanned(model, plan, bytes);
        budget.RecordCompiledTable(bytes.Length);
        output = bytes;
        diagnostic = default;
        return ReadStatus.Done;
    }

    public static ReadStatus TryWrite(MarkMarkPosTable model, Span<byte> destination,
        out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        var budget = new BuildBudget(limits);
        ReadStatus status = TryPlanSubtables(model, out var plan, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        if (destination.Length < plan!.Length)
            return ReadWire.Fail(ReadStatus.DestinationTooSmall, DiagnosticCode.DestinationTooSmall,
                model.Location.AtLocal(DataField.OutputBytes, 0), out diagnostic,
                (ulong)destination.Length, (ulong)plan.Length);
        WritePlanned(model, plan, destination);
        diagnostic = default;
        return ReadStatus.Done;
    }

    internal static ReadStatus TryPlanSubtables(MarkMarkPosTable model, out MarkPosPartPlan? plan,
        out FontDiagnostic diagnostic, BuildBudget budget)
    {
        plan = null;
        var location = model.Location;
        int markCount = model.MarkCount, mark2Count = model.Mark2Count;
        if (!budget.Work(32L * markCount + mark2Count + 1, location, out diagnostic))
            return ReadStatus.ResourceLimit;
        long markArrayAt = MarkMarkPosLayout.MinimumHeader
            + CoverageTableCompiler.Measure(model.MarkCoverage)
            + CoverageTableCompiler.Measure(model.Mark2Coverage);
        int markArrayTarget = (int)markArrayAt;
        int matrixAt = markArrayTarget + MarkArrayLayout.MinimumHeader + markCount * MarkArrayLayout.RecordStride;
        long fixedBytes = MarkMarkPosLayout.MinimumHeader
            + CoverageTableCompiler.Measure(model.MarkCoverage)
            + CoverageTableCompiler.Measure(model.Mark2Coverage)
            + MarkArrayLayout.MinimumHeader + (long)markCount * MarkArrayLayout.RecordStride
            + MarkAnchorMatrix.ArrayHeaderLength + (long)mark2Count * 2L * model.ClassCount;
        if (!budget.Work(fixedBytes, location, out diagnostic)) return ReadStatus.ResourceLimit;
        var uses = new List<AnchorUse>();
        foreach (var record in model.Marks)
            uses.Add(new(record.Anchor, markArrayTarget, DataField.Offset, MarkMarkPosLayout.Mark1ArrayField));
        for (int i = 0; i < mark2Count; i++)
        {
            var row = model.Mark2Row(i);
            for (int c = 0; c < row.Length; c++)
                if (row[c] is AnchorTable body)
                    uses.Add(new(body, matrixAt, DataField.Offset, MarkMarkPosLayout.Mark2ArrayField));
        }
        ReadStatus status = PlaceTail(location, uses, fixedBytes, budget, out var anchors,
            out var anchorTargets, out var devices, out var deviceTargets, out long end,
            out long emission, out diagnostic);
        if (status != ReadStatus.Done) return status;
        plan = new((int)end, MarkMarkPosLayout.MinimumHeader,
            MarkMarkPosLayout.MinimumHeader + CoverageTableCompiler.Measure(model.MarkCoverage),
            markArrayTarget, matrixAt, 0, anchors, anchorTargets, devices, deviceTargets, emission);
        diagnostic = default;
        return ReadStatus.Done;
    }

    internal static void WritePlanned(MarkMarkPosTable model, MarkPosPartPlan plan, Span<byte> destination)
    {
        var location = model.Location;
        CoverageTableCompiler.RequireWritten(MarkMarkPosView.TryWritePosFormat(destination, 1, out _, location));
        CoverageTableCompiler.RequireWritten(MarkMarkPosView.TryWriteMark1CoverageOffset(destination,
            (ushort)plan.MarkCoverageTarget, out _, location));
        CoverageTableCompiler.RequireWritten(MarkMarkPosView.TryWriteMark2CoverageOffset(destination,
            (ushort)plan.SecondCoverageTarget, out _, location));
        CoverageTableCompiler.RequireWritten(MarkMarkPosView.TryWriteClassCount(destination,
            model.ClassCount, out _, location));
        CoverageTableCompiler.RequireWritten(MarkMarkPosView.TryWriteMark1ArrayOffset(destination,
            (ushort)plan.MarkArrayTarget, out _, location));
        CoverageTableCompiler.RequireWritten(MarkMarkPosView.TryWriteMark2ArrayOffset(destination,
            (ushort)plan.MatrixTarget, out _, location));
        CoverageTableCompiler.WritePlanned(model.MarkCoverage.Glyphs, model.MarkCoverage.Format,
            model.MarkCoverage.RangeCount, destination.Slice(plan.MarkCoverageTarget,
            CoverageTableCompiler.Measure(model.MarkCoverage)), location);
        CoverageTableCompiler.WritePlanned(model.Mark2Coverage.Glyphs, model.Mark2Coverage.Format,
            model.Mark2Coverage.RangeCount, destination.Slice(plan.SecondCoverageTarget,
            CoverageTableCompiler.Measure(model.Mark2Coverage)), location);
        WriteMarkArray(destination, model.Marks.ToArray(), plan, location);
        BinaryPrimitives.WriteUInt16BigEndian(destination.Slice(plan.MatrixTarget), (ushort)model.Mark2Count);
        for (int i = 0; i < model.Mark2Count; i++)
        {
            long rowAt = plan.MatrixTarget + MarkAnchorMatrix.ArrayHeaderLength + (long)i * 2L * model.ClassCount;
            WriteMatrixRow(destination, rowAt, model.Mark2Row(i), plan, plan.MatrixTarget);
        }
        WriteAnchorLeaves(plan, destination, location);
    }
}
