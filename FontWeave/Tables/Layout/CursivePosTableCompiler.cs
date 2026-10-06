using FontWeave.Core;

namespace FontWeave.Tables.Layout;

// Immutable leaf fragment plan (card A6 1.1-① shape): one cursive subtable is one
// part - no internal split (OQ-T4-05-9). Canonical order: header, EntryExit records,
// Coverage, Anchor bodies, Anchor Device leaves. Anchor offsets are subtable-relative
// and Device offsets Anchor-relative (spec gpos.md:382-383/761).
internal sealed record CursivePosPartPlan(int Length, int CoverageTarget, ushort CoverageFormat,
    int CoverageRangeCount, CursivePosTableCompiler.AnchorLeaf[] Anchors,
    Dictionary<AnchorTable, int> AnchorTargets,
    CursivePosTableCompiler.DeviceLeaf[] Devices,
    Dictionary<StaticDeviceTable, int> DeviceTargets, long EmissionWork);

public static class CursivePosTableCompiler
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

    // Explicit factory: one EntryExit record per Coverage key, both slots optional
    // (OQ-T4-05-4's write-side face). Anchor models are carried by identity, so shared
    // bodies stay shared through planning and emission (W5).
    public static ReadStatus TryCreate(ReadOnlySpan<ushort> glyphs,
        ReadOnlySpan<EntryExitTable> entryExit, int numGlyphs, out CursivePosTable? model,
        out FontDiagnostic diagnostic, BuildLimits? limits = null, DataLocation location = default,
        ushort coverageFormat = 1) =>
        TryCreate(glyphs, entryExit, numGlyphs, out model, out diagnostic,
            new BuildBudget(limits), location, coverageFormat);

    internal static ReadStatus TryCreate(ReadOnlySpan<ushort> glyphs,
        ReadOnlySpan<EntryExitTable> entryExit, int numGlyphs, out CursivePosTable? model,
        out FontDiagnostic diagnostic, BuildBudget budget, DataLocation location = default,
        ushort coverageFormat = 1)
    {
        model = null;
        ReadStatus status = CoverageTableCompiler.CheckNumGlyphs(numGlyphs, location, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (glyphs.Length != entryExit.Length || glyphs.Length > ushort.MaxValue)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                location.AtLocal(DataField.Count, CursivePosLayout.CountField), out diagnostic,
                (ulong)entryExit.Length, (ulong)glyphs.Length);
        if (!budget.Work(entryExit.Length + 1, location, out diagnostic)) return ReadStatus.ResourceLimit;
        for (int i = 0; i < entryExit.Length; i++)
        {
            foreach (var anchor in new[] { entryExit[i].Entry, entryExit[i].Exit })
            {
                if (anchor is not AnchorTable body) continue;
                if (body.Location.SourceId != location.SourceId ||
                    body.Location.FaceIndex != location.FaceIndex ||
                    location.SourceId != 0 && body.Location.TableTag != location.TableTag)
                    return ReadWire.Fail(ReadStatus.InvalidSource, DiagnosticCode.WrongSourceFace,
                        location.AtLocal(DataField.None, 0, i), out diagnostic);
            }
        }
        status = CoverageTableCompiler.TryCreate(glyphs, numGlyphs, coverageFormat, out var coverage,
            out diagnostic, budget, location);
        if (status != ReadStatus.Done) return status;
        if (!budget.Buffer(24L * entryExit.Length, location, out diagnostic)) return ReadStatus.ResourceLimit;
        var copy = entryExit.ToArray();
        long consumed = CursivePosLayout.MinimumHeader + CursivePosLayout.RecordStride * copy.Length
            + CoverageTableCompiler.Measure(coverage!);
        foreach (var record in copy)
            foreach (var anchor in new[] { record.Entry, record.Exit })
                if (anchor is AnchorTable body)
                    consumed += MeasureAnchor(body);
        model = new(coverage!, copy, numGlyphs, location, (int)consumed);
        diagnostic = default;
        return ReadStatus.Done;
    }

    internal static int MeasureAnchor(AnchorTable anchor) => anchor.Format switch
    {
        1 => AnchorLayout.MinimumHeader,
        2 => AnchorLayout.MinimumHeader + 2,
        _ => AnchorLayout.Format3Length + (anchor.XDevice is null ? 0 : StaticDeviceTableCompiler.Measure(anchor.XDevice))
            + (anchor.YDevice is null ? 0 : StaticDeviceTableCompiler.Measure(anchor.YDevice)),
    };

    // The body-only stride: WriteAnchor emits just the anchor header while devices are
    // placed as separate leaves, so placement and the plan length must advance by the
    // header size, never by the composite MeasureAnchor.
    private static int MeasureAnchorBody(AnchorTable anchor) => anchor.Format switch
    {
        1 => AnchorLayout.MinimumHeader,
        2 => AnchorLayout.MinimumHeader + 2,
        _ => AnchorLayout.Format3Length,
    };

    public static ReadStatus TryCompile(CursivePosTable model, out byte[]? output,
        out FontDiagnostic diagnostic, BuildLimits? limits = null) =>
        TryCompile(model, out output, out diagnostic, new BuildBudget(limits));

    internal static ReadStatus TryCompile(CursivePosTable model, out byte[]? output,
        out FontDiagnostic diagnostic, BuildBudget budget)
    {
        ArgumentNullException.ThrowIfNull(model);
        output = null;
        ReadStatus status = TryPlanSubtables(model, out var plans, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        if (!budget.Buffer(plans![0].Length, model.Location, out diagnostic)) return ReadStatus.ResourceLimit;
        byte[] bytes = new byte[plans[0].Length];
        WritePlanned(model, plans[0], bytes);
        budget.RecordCompiledTable(bytes.Length);
        output = bytes;
        diagnostic = default;
        return ReadStatus.Done;
    }

    public static ReadStatus TryWrite(CursivePosTable model, Span<byte> destination,
        out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        var budget = new BuildBudget(limits);
        ReadStatus status = TryPlanSubtables(model, out var plans, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        // Fallible hooks and budgets complete before the destination capacity check
        // (spec 7.3; A6 1.5 O4 order); the destination is untouched on refusal.
        if (destination.Length < plans![0].Length)
            return ReadWire.Fail(ReadStatus.DestinationTooSmall, DiagnosticCode.DestinationTooSmall,
                model.Location.AtLocal(DataField.OutputBytes, 0), out diagnostic,
                (ulong)destination.Length, (ulong)plans[0].Length);
        WritePlanned(model, plans[0], destination);
        diagnostic = default;
        return ReadStatus.Done;
    }

    // Terminal fragment planning: shared anchors and devices dedupe by model identity
    // (never by equal addresses); every Offset16 fixup goes through the generated
    // per-reference helpers whose too-narrow refusal IS the finite overflow refusal.
    internal static ReadStatus TryPlanSubtables(CursivePosTable model,
        out CursivePosPartPlan[]? plans, out FontDiagnostic diagnostic, BuildBudget budget)
    {
        plans = null;
        int count = model.EntryExitCount;
        if (!budget.Work(32L * count + 1, model.Location, out diagnostic)) return ReadStatus.ResourceLimit;
        int recordsEnd = CursivePosLayout.MinimumHeader + CursivePosLayout.RecordStride * count;
        int coverageAt = recordsEnd;
        ReadStatus status = CursivePosView.TryGetCoverageOffsetValue(present: true, origin: 0UL,
            target: (ulong)coverageAt, out ushort coverageOffset, out diagnostic,
            model.Location.AtLocal(DataField.Offset, CursivePosLayout.CoverageField));
        if (status != ReadStatus.Done) return status;
        var anchors = new List<AnchorLeaf>();
        var anchorTargets = new Dictionary<AnchorTable, int>(ReferenceEqualityComparer.Instance);
        var devices = new List<DeviceLeaf>();
        var deviceTargets = new Dictionary<StaticDeviceTable, int>(ReferenceEqualityComparer.Instance);
        long leafBytes = 0, deltaWork = 0;
        int cursor = coverageAt + CoverageTableCompiler.Measure(model.Coverage);
        int anchorBase = cursor;
        for (int i = 0; i < count; i++)
        {
            model.TryGetEntryExit(i, out var record, out _);
            foreach (var anchor in new[] { record.Entry, record.Exit })
            {
                if (anchor is not AnchorTable body || anchorTargets.ContainsKey(body)) continue;
                if (!budget.Buffer(16, model.Location, out diagnostic)) return ReadStatus.ResourceLimit;
                    int anchorLength = MeasureAnchorBody(body);
                    anchorTargets.Add(body, 0);
                    anchors.Add(new(body, 0));
                    leafBytes += anchorLength;
                if (body.XDevice is not null && !deviceTargets.ContainsKey(body.XDevice))
                {
                    deviceTargets.Add(body.XDevice, 0);
                    devices.Add(new(body.XDevice, 0));
                    leafBytes += StaticDeviceTableCompiler.Measure(body.XDevice);
                    deltaWork += StaticDeviceTableCompiler.EmissionWork(body.XDevice)
                        - StaticDeviceTableCompiler.Measure(body.XDevice);
                }
                if (body.YDevice is not null && !deviceTargets.ContainsKey(body.YDevice))
                {
                    deviceTargets.Add(body.YDevice, 0);
                    devices.Add(new(body.YDevice, 0));
                    leafBytes += StaticDeviceTableCompiler.Measure(body.YDevice);
                    deltaWork += StaticDeviceTableCompiler.EmissionWork(body.YDevice)
                        - StaticDeviceTableCompiler.Measure(body.YDevice);
                }
            }
        }
        // Place the largest anchor body last so a later Offset16 anchor slot keeps its
        // reach; every anchor offset is preflighted through the generated helpers.
        int largest = -1;
        for (int i = 0; i < anchors.Count; i++)
            if (largest < 0 || MeasureAnchorBody(anchors[i].Model) > MeasureAnchorBody(anchors[largest].Model))
                largest = i;
        long at = anchorBase;
        for (int i = 0; i < anchors.Count; i++)
        {
            if (i == largest) continue;
            anchors[i].Position = (int)at;
            at += MeasureAnchorBody(anchors[i].Model);
        }
        if (largest >= 0)
        {
            anchors[largest].Position = (int)at;
            at += MeasureAnchorBody(anchors[largest].Model);
        }
        // The record slot offsets were placed largest-body-last (SinglePos canon), so
        // the last anchor end is the widest Offset16 the EntryExit records must hold;
        // its generated-helper preflight carries the canonical too-narrow refusal.
        if (anchors.Count != 0)
        {
            status = CursivePosView.TryGetEntryAnchorOffsetValue(present: true, origin: 0UL,
                target: (ulong)at, out _, out diagnostic,
                model.Location.AtLocal(DataField.Offset, CursivePosLayout.EntryOffsetAt));
            if (status != ReadStatus.Done) return status;
        }
        foreach (var leaf in anchors) anchorTargets[leaf.Model] = leaf.Position;
        long deviceAt = at;
        foreach (var leaf in devices)
        {
            leaf.Position = (int)deviceAt;
            deviceTargets[leaf.Model] = (int)deviceAt;
            deviceAt += StaticDeviceTableCompiler.Measure(leaf.Model);
        }
        // Anchor-relative Device deltas preflight like the generated OffsetValue shape.
        foreach (var leaf in anchors)
        {
            var body = leaf.Model;
            if (body.XDevice is not null && deviceTargets[body.XDevice] - leaf.Position > ushort.MaxValue)
                return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
                    model.Location.AtLocal(DataField.Offset, AnchorLayout.Format3HeaderField),
                    out diagnostic, (ulong)(deviceTargets[body.XDevice] - leaf.Position), ushort.MaxValue);
            if (body.YDevice is not null && deviceTargets[body.YDevice] - leaf.Position > ushort.MaxValue)
                return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
                    model.Location.AtLocal(DataField.Offset, AnchorLayout.Format3HeaderField + 2),
                    out diagnostic, (ulong)(deviceTargets[body.YDevice] - leaf.Position), ushort.MaxValue);
        }
        _ = budget;
        int length = (int)deviceAt;
        long emission = length + count + deltaWork;
        if (!budget.Work(emission, model.Location, out diagnostic) ||
            !budget.Output(length, model.Location, out diagnostic)) return ReadStatus.ResourceLimit;
        plans = [new CursivePosPartPlan(length, coverageAt, model.Coverage.Format,
            model.Coverage.RangeCount, anchors.ToArray(), anchorTargets,
            devices.ToArray(), deviceTargets, emission)];
        diagnostic = default;
        return ReadStatus.Done;
    }

    // Emission into the caller's final destination; byte-for-byte deterministic.
    internal static void WritePlanned(CursivePosTable model, CursivePosPartPlan plan,
        Span<byte> destination)
    {
        var location = model.Location;
        int count = model.EntryExitCount;
        CoverageTableCompiler.RequireWritten(CursivePosView.TryWriteCursiveFormat(destination, 1, out _, location));
        CoverageTableCompiler.RequireWritten(CursivePosView.TryWriteCoverageOffset(destination,
            (ushort)plan.CoverageTarget, out _, location));
        CoverageTableCompiler.RequireWritten(CursivePosView.TryWriteEntryExitCount(destination,
            (ushort)count, out _, location));
        for (int i = 0; i < count; i++)
        {
            model.TryGetEntryExit(i, out var record, out _);
            ushort entry = record.Entry is AnchorTable body ? (ushort)plan.AnchorTargets[body] : (ushort)0;
            ushort exit = record.Exit is AnchorTable body2 ? (ushort)plan.AnchorTargets[body2] : (ushort)0;
            CoverageTableCompiler.RequireWritten(CursivePosView.TryWriteEntryExitRecordAt(destination,
                CursivePosLayout.MinimumHeader + (ulong)i * CursivePosLayout.RecordStride,
                new CursivePosView.EntryExitRecordData(i, entry, exit), out _, location));
        }
        CoverageTableCompiler.WritePlanned(model.Coverage.Glyphs, plan.CoverageFormat,
            plan.CoverageRangeCount, destination.Slice(plan.CoverageTarget,
            CoverageTableCompiler.Measure(model.Coverage)), location);
        foreach (var leaf in plan.Anchors)
            WriteAnchor(leaf.Model, leaf.Position, plan, destination, location);
        foreach (var leaf in plan.Devices)
            StaticDeviceTableCompiler.WritePlanned(leaf.Model, destination.Slice(leaf.Position));
    }

    private static void WriteAnchor(AnchorTable anchor, int at, CursivePosPartPlan plan,
        Span<byte> destination, DataLocation location)
    {
        var slice = destination.Slice(at, MeasureAnchorBody(anchor)); // window only; devices emit separately
        CoverageTableCompiler.RequireWritten(AnchorView.TryWriteAnchorFormat(slice,
            anchor.Format, out _, location));
        CoverageTableCompiler.RequireWritten(AnchorView.TryWriteXCoordinate(slice,
            anchor.XCoordinate, out _, location));
        CoverageTableCompiler.RequireWritten(AnchorView.TryWriteYCoordinate(slice,
            anchor.YCoordinate, out _, location));
        if (anchor.Format == 2)
            CoverageTableCompiler.RequireWritten(AnchorView.TryWriteFormat2HeaderAt(slice,
                AnchorLayout.Format2HeaderField, new AnchorView.Format2HeaderData(-1,
                    anchor.AnchorPoint), out _, location));
        if (anchor.Format == 3)
        {
            // Both Device offsets are Anchor-relative; a null model encodes the
            // present-null offset 0 (T4-01 mask/offset decoupling).
            ushort x = anchor.XDevice is StaticDeviceTable device
                ? (ushort)(plan.DeviceTargets[device] - at) : (ushort)0;
            ushort y = anchor.YDevice is StaticDeviceTable device2
                ? (ushort)(plan.DeviceTargets[device2] - at) : (ushort)0;
            CoverageTableCompiler.RequireWritten(AnchorView.TryWriteFormat3HeaderAt(slice,
                AnchorLayout.Format3HeaderField, new AnchorView.Format3HeaderData(-1, x, y),
                out _, location));
        }
    }
}
