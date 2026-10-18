using System.Numerics;
using FontWeave.Core;

namespace FontWeave.Tables.Layout;

// Immutable leaf fragment plan (card A6 1.1-① shape): layout facts only, no payload
// bytes. One subtable is one part - the T4-01 internal split stays type-1-specific
// (OQ-T4-05-9), so an Offset16-unreachable layout is a finite refusal and multi
// -subtable models remain caller-driven key-disjoint sharding. Canonical order:
// header, offset records, pair-set/class-matrix bodies, ClassDefs, Coverage, Device
// leaves last. A format-2 subtable whose subtable-relative offsets overflow the
// canonical order re-plans once with the compact profile — Coverage and the Device
// region pulled ahead of the matrix (CoverageTarget/MatrixTarget move with them) —
// the GsubTableCompiler two-candidate precedent; format 1's set-relative deltas
// keep the single canonical profile.
internal sealed record PairPosPartPlan(int Format, int Length, int CoverageTarget,
    ushort CoverageFormat, int CoverageRangeCount, int[] SetBases,
    int ClassDef1Target, int ClassDef2Target,
    PairPosTableCompiler.DeviceLeaf[] Devices,
    Dictionary<StaticDeviceTable, int> DeviceTargets, long EmissionWork,
    int MatrixTarget = PairPosLayout.Format2Header);

public static class PairPosTableCompiler
{
    internal sealed class DeviceLeaf(StaticDeviceTable model, int position)
    {
        internal StaticDeviceTable Model { get; } = model;
        internal int Position { get; set; } = position;
    }

    // Explicit format-1 factory: one PairValueRecord row set per Coverage key, strict
    // second-glyph order preserved on creation (OQ-T4-05-1's write-side face), Device
    // identity bound to the caller's source, mask/value presence consistency per the
    // SinglePos factory contract. The empty set is a legal no-op row set (OQ-T4-05-3).
    public static ReadStatus TryCreate(ReadOnlySpan<ushort> glyphs,
        ReadOnlySpan<PairValueRecord[]> pairSets, int numGlyphs, ushort valueFormat1,
        ushort valueFormat2, out PairPosTable? model, out FontDiagnostic diagnostic,
        BuildLimits? limits = null, DataLocation location = default, ushort coverageFormat = 1) =>
        TryCreate(glyphs, pairSets, numGlyphs, valueFormat1, valueFormat2, out model,
            out diagnostic, new BuildBudget(limits), location, coverageFormat);

    internal static ReadStatus TryCreate(ReadOnlySpan<ushort> glyphs,
        ReadOnlySpan<PairValueRecord[]> pairSets, int numGlyphs, ushort valueFormat1,
        ushort valueFormat2, out PairPosTable? model, out FontDiagnostic diagnostic,
        BuildBudget budget, DataLocation location = default, ushort coverageFormat = 1)
    {
        model = null;
        ReadStatus status = CoverageTableCompiler.CheckNumGlyphs(numGlyphs, location, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = PairPosView.ValidateMasks(valueFormat1, valueFormat2, location, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (glyphs.Length != pairSets.Length || glyphs.Length > ushort.MaxValue)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                location.AtLocal(DataField.Count, PairPosLayout.PairSetCountField), out diagnostic,
                (ulong)pairSets.Length, (ulong)glyphs.Length);
        long rows = 0;
        for (int i = 0; i < pairSets.Length; i++)
        {
            PairValueRecord[]? set = pairSets[i];
            if (set is null)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    location.AtLocal(DataField.Payload, PairPosLayout.Format1Header +
                        (ulong)i * PairPosLayout.OffsetStride, i), out diagnostic, (ulong)i);
            rows += set.Length;
            if (!budget.Work(set.Length + 1, location, out diagnostic)) return ReadStatus.ResourceLimit;
            ushort previous = 0;
            for (int j = 0; j < set.Length; j++)
            {
                var record = set[j];
                if (record.SecondGlyph >= numGlyphs)
                    return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                        location.AtLocal(DataField.GlyphId, PairPosLayout.Format1Header +
                            (ulong)i * PairPosLayout.OffsetStride, i), out diagnostic,
                        record.SecondGlyph, (ulong)numGlyphs);
                if (j > 0 && record.SecondGlyph <= previous)
                    return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                        location.AtLocal(DataField.GlyphId, PairPosLayout.Format1Header +
                            (ulong)i * PairPosLayout.OffsetStride, i), out diagnostic,
                        record.SecondGlyph, previous);
                previous = record.SecondGlyph;
                status = CheckRecordValues(record, valueFormat1, valueFormat2, location, i, out diagnostic);
                if (status != ReadStatus.Done) return status;
            }
        }
        status = CoverageTableCompiler.TryCreate(glyphs, numGlyphs, coverageFormat, out var coverage,
            out diagnostic, budget, location);
        if (status != ReadStatus.Done) return status;
        if (!budget.Buffer(rows * 80L + 8L * pairSets.Length, location, out diagnostic)) return ReadStatus.ResourceLimit;
        var copy = pairSets.Length == 0 ? [] : new PairSetTable[pairSets.Length];
        long consumed = PairPosLayout.Format1Header + PairPosLayout.OffsetStride * pairSets.Length;
        int stride = RowStride(valueFormat1, valueFormat2);
        for (int i = 0; i < pairSets.Length; i++)
        {
            var setRows = (PairValueRecord[])pairSets[i].Clone();
            copy[i] = new(setRows, location, 0);
            consumed += 2 + (long)setRows.Length * stride;
        }
        consumed += CoverageTableCompiler.Measure(coverage!);
        model = new(coverage!, 1, valueFormat1, valueFormat2, pairSets: copy,
            location: location, consumedLength: (int)consumed);
        diagnostic = default;
        return ReadStatus.Done;
    }

    // Explicit format-2 factory: the declared class1xclass2 matrix keeps physical rows
    // including all-zero cells; the two ClassDefs may be one shared instance (W12).
    public static ReadStatus TryCreate(ReadOnlySpan<ushort> glyphs, ReadOnlySpan<ClassDefRange> class1Ranges,
        ReadOnlySpan<ClassDefRange> class2Ranges, ushort class1Count, ushort class2Count,
        ReadOnlySpan<StaticValueRecord> values1, ReadOnlySpan<StaticValueRecord> values2,
        int numGlyphs, ushort valueFormat1, ushort valueFormat2, out PairPosTable? model,
        out FontDiagnostic diagnostic, BuildLimits? limits = null, DataLocation location = default,
        ushort coverageFormat = 1, ushort classDefFormat = 1) =>
        TryCreate(glyphs, class1Ranges, class2Ranges, class1Count, class2Count, values1, values2,
            numGlyphs, valueFormat1, valueFormat2, out model, out diagnostic,
            new BuildBudget(limits), location, coverageFormat, classDefFormat);

    internal static ReadStatus TryCreate(ReadOnlySpan<ushort> glyphs, ReadOnlySpan<ClassDefRange> class1Ranges,
        ReadOnlySpan<ClassDefRange> class2Ranges, ushort class1Count, ushort class2Count,
        ReadOnlySpan<StaticValueRecord> values1, ReadOnlySpan<StaticValueRecord> values2,
        int numGlyphs, ushort valueFormat1, ushort valueFormat2, out PairPosTable? model,
        out FontDiagnostic diagnostic, BuildBudget budget, DataLocation location = default,
        ushort coverageFormat = 1, ushort classDefFormat = 1)
    {
        model = null;
        ReadStatus status = CoverageTableCompiler.CheckNumGlyphs(numGlyphs, location, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = PairPosView.ValidateMasks(valueFormat1, valueFormat2, location, out diagnostic);
        if (status != ReadStatus.Done) return status;
        long cells = (long)class1Count * class2Count;
        if (values1.Length != cells || values2.Length != cells)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                location.AtLocal(DataField.Count, PairPosLayout.Format2HeaderField), out diagnostic,
                (ulong)values1.Length, (ulong)cells);
        if (!budget.Work(cells + class1Ranges.Length + class2Ranges.Length + 1, location, out diagnostic))
            return ReadStatus.ResourceLimit;
        for (int i = 0; i < values1.Length; i++)
        {
            status = CheckRecordValues(new(default, values1[i], values2[i]), valueFormat1,
                valueFormat2, location, i, out diagnostic);
            if (status != ReadStatus.Done) return status;
        }
        status = ClassDefTableCompiler.TryCreate(class1Ranges, numGlyphs, classDefFormat,
            out var classDef1, out diagnostic, budget, location);
        if (status != ReadStatus.Done) return status;
        status = ClassDefTableCompiler.TryCreate(class2Ranges, numGlyphs, classDefFormat,
            out var classDef2, out diagnostic, budget, location);
        if (status != ReadStatus.Done) return status;
        status = CoverageTableCompiler.TryCreate(glyphs, numGlyphs, coverageFormat, out var coverage,
            out diagnostic, budget, location);
        if (status != ReadStatus.Done) return status;
        if (!budget.Buffer(cells * 80L, location, out diagnostic)) return ReadStatus.ResourceLimit;
        long consumed = PairPosLayout.Format2Header + cells * RowStride(valueFormat1, valueFormat2)
            + ClassDefTableCompiler.Measure(classDef1!)
            + (ReferenceEquals(classDef1, classDef2) ? 0 : ClassDefTableCompiler.Measure(classDef2!))
            + CoverageTableCompiler.Measure(coverage!);
        model = new(coverage!, 2, valueFormat1, valueFormat2,
            classDef1: classDef1!, classDef2: classDef2!, class1Count: class1Count,
            class2Count: class2Count, values1: values1.ToArray(), values2: values2.ToArray(),
            location: location, consumedLength: (int)consumed);
        diagnostic = default;
        return ReadStatus.Done;
    }

    private static int RowStride(ushort valueFormat1, ushort valueFormat2) =>
        PairPosLayout.WordStride * (1 + BitOperations.PopCount((uint)valueFormat1)
            + BitOperations.PopCount((uint)valueFormat2));

    // Presence belongs to the owning ValueFormats: a non-default word under an absent
    // bit refuses (SinglePos factory contract); a Device model must carry the caller's
    // source identity.
    private static ReadStatus CheckRecordValues(in PairValueRecord record, ushort valueFormat1,
        ushort valueFormat2, DataLocation location, int ordinal, out FontDiagnostic diagnostic)
    {
        diagnostic = default;
        for (int slot = 0; slot < 8; slot++)
        {
            foreach (var (mask, value) in new[] { (valueFormat1, record.Value1), (valueFormat2, record.Value2) })
            {
                bool present = (mask & (1 << slot)) != 0;
                StaticDeviceTable? device = value.DeviceAt(slot);
                if (!present && (slot < 4 ? value.ScalarAt(slot) != 0 : device is not null))
                    return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                        location.AtLocal(DataField.Flags, PairPosLayout.Format1Field, ordinal),
                        out diagnostic, (ulong)(1 << slot), mask);
                if (device is StaticDeviceTable model &&
                    (model.Location.SourceId != location.SourceId ||
                        model.Location.FaceIndex != location.FaceIndex ||
                        location.SourceId != 0 && model.Location.TableTag != location.TableTag))
                    return ReadWire.Fail(ReadStatus.InvalidSource, DiagnosticCode.WrongSourceFace,
                        location.AtLocal(DataField.None, 0, ordinal), out diagnostic);
            }
        }
        return ReadStatus.Done;
    }

    public static ReadStatus TryCompile(PairPosTable model, out byte[]? output,
        out FontDiagnostic diagnostic, BuildLimits? limits = null) =>
        TryCompile(model, out output, out diagnostic, new BuildBudget(limits));

    internal static ReadStatus TryCompile(PairPosTable model, out byte[]? output,
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

    public static ReadStatus TryWrite(PairPosTable model, Span<byte> destination,
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

    // Terminal fragment planning: one part per subtable, no internal split
    // (OQ-T4-05-9). Every fixup goes through the generated per-reference helpers whose
    // too-narrow refusal IS the registered finite overflow refusal
    // (UnsupportedFormat/UnsupportedOperation, Detail=target, Expected=65535); the
    // hand-written row words (no generated reference exists for them) preflight their
    // deltas with the same shape. Shared leaves are identity-shared, never address
    // -deduped (W12's shared ClassDef emits one body).
    internal static ReadStatus TryPlanSubtables(PairPosTable model, out PairPosPartPlan[]? plans,
        out FontDiagnostic diagnostic, BuildBudget budget)
    {
        plans = null;
        ReadStatus status = PairPosView.ValidateMasks(model.ValueFormat1, model.ValueFormat2,
            model.Location, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (model.Format == 1)
            return PlanFormat1(model, out plans, out diagnostic, budget);
        status = PlanFormat2(model, compactProfile: false, out plans, out diagnostic, budget);
        if (status == ReadStatus.Done || status != ReadStatus.UnsupportedFormat) return status;
        // The canonical order's subtable-relative offsets overflowed (the rebuilt
        // ClassDefs can scatter wider than the source's under the output remap); the
        // one alternate profile pulls Coverage and the Device region ahead of the
        // class matrix (a refused candidate's Work stays charged, the second's
        // refusal stands).
        return PlanFormat2(model, compactProfile: true, out plans, out diagnostic, budget);
    }

    private static ReadStatus PlanFormat1(PairPosTable model, out PairPosPartPlan[]? plans,
        out FontDiagnostic diagnostic, BuildBudget budget)
    {
        plans = null;
        int count = model.PairSetCount;
        if (!budget.Work(32L * count + 1, model.Location, out diagnostic)) return ReadStatus.ResourceLimit;
        int stride = RowStride(model.ValueFormat1, model.ValueFormat2);
        long bodies = 0;
        for (int i = 0; i < count; i++) bodies += 2L + (long)model.PairSets![i].PairValueCount * stride;
        int coverageAt = PairPosLayout.Format1Header + PairPosLayout.OffsetStride * count + (int)bodies;
        ReadStatus status = PairPosView.TryGetCoverageOffsetValue(present: true, origin: 0UL,
            target: (ulong)coverageAt, out ushort coverageOffset, out diagnostic,
            model.Location.AtLocal(DataField.Offset, PairPosLayout.CoverageField));
        if (status != ReadStatus.Done) return status;
        var setBases = new int[count];
        long cursor = PairPosLayout.Format1Header + PairPosLayout.OffsetStride * count;
        for (int i = 0; i < count; i++)
        {
            setBases[i] = (int)cursor;
            status = PairPosView.TryGetPairSetOffsetValue(present: true, origin: 0UL,
                target: (ulong)cursor, out _, out diagnostic, model.Location.AtLocal(DataField.Offset,
                    PairPosLayout.Format1Header + (ulong)i * PairPosLayout.OffsetStride, i));
            if (status != ReadStatus.Done) return status;
            cursor += 2 + (long)model.PairSets![i].PairValueCount * stride;
        }
        status = CollectDevices(model, model.EnumerateDevices(), budget,
            out var devices, out long leafBytes, out diagnostic);
        if (status != ReadStatus.Done) return status;
        PlaceDevices(devices, coverageAt + CoverageTableCompiler.Measure(model.Coverage));
        var deviceTargets = DeviceTargets(devices);
        status = PreflightFormat1Deltas(model, deviceTargets, setBases, stride, out diagnostic);
        if (status != ReadStatus.Done) return status;
        int length = coverageAt + CoverageTableCompiler.Measure(model.Coverage) + (int)leafBytes;
        long emission = EmissionWork(model, [.. devices], length);
        if (!budget.Work(emission, model.Location, out diagnostic) ||
            !budget.Output(length, model.Location, out diagnostic)) return ReadStatus.ResourceLimit;
        plans = [new PairPosPartPlan(1, length, coverageAt, model.Coverage.Format,
            model.Coverage.RangeCount, setBases, 0, 0, [.. devices], deviceTargets, emission)];
        diagnostic = default;
        return ReadStatus.Done;
    }

    // The set-relative device deltas are the hand-written boundary's fixups: their
    // plan-time preflight mirrors the generated OffsetValue refusal shape exactly
    // (UnsupportedFormat/UnsupportedOperation, Detail=delta, Expected=65535).
    private static ReadStatus PreflightFormat1Deltas(PairPosTable model,
        Dictionary<StaticDeviceTable, int> targets, int[] setBases, int stride,
        out FontDiagnostic diagnostic)
    {
        for (int i = 0; i < model.PairSetCount; i++)
        {
            var set = model.PairSets![i];
            for (int j = 0; j < set.PairValueCount; j++)
            {
                var record = set.Records[j];
                foreach (var (mask, value) in new[] { (model.ValueFormat1, record.Value1),
                    (model.ValueFormat2, record.Value2) })
                {
                    for (int slot = 4; slot < 8; slot++)
                    {
                        if ((mask & (1 << slot)) == 0 || value.DeviceAt(slot) is not StaticDeviceTable device)
                            continue;
                        int field = setBases[i] + 2 + j * stride + PairPosLayout.WordStride
                            + PairPosLayout.WordStride * BitOperations.PopCount((uint)(mask & ((1 << slot) - 1)));
                        long delta = targets[device] - setBases[i];
                        if (delta > ushort.MaxValue)
                            return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
                                model.Location.AtLocal(DataField.Offset, (ulong)field, j),
                                out diagnostic, (ulong)delta, ushort.MaxValue);
                    }
                }
            }
        }
        diagnostic = default;
        return ReadStatus.Done;
    }

    private static ReadStatus PlanFormat2(PairPosTable model, bool compactProfile,
        out PairPosPartPlan[]? plans, out FontDiagnostic diagnostic, BuildBudget budget)
    {
        plans = null;
        long cells = (long)model.Class1Count * model.Class2Count;
        if (!budget.Work(32L * cells + 1, model.Location, out diagnostic)) return ReadStatus.ResourceLimit;
        int stride = PairPosLayout.WordStride * (BitOperations.PopCount((uint)model.ValueFormat1)
            + BitOperations.PopCount((uint)model.ValueFormat2));
        // The Device leaves' identity first (no placement); the two profiles differ
        // only in where the subtable-relative small regions land — canonical: matrix,
        // ClassDefs, Coverage, Devices; compact: Coverage, Devices, matrix, ClassDefs.
        List<DeviceLeaf> leaves;
        long leafBytes;
        ReadStatus status = CollectDevices(model, model.EnumerateDevices(), budget,
            out leaves, out leafBytes, out diagnostic);
        if (status != ReadStatus.Done) return status;
        int matrixAt;
        long cursor = PairPosLayout.Format2Header;
        int coverageAt;
        if (compactProfile)
        {
            coverageAt = (int)cursor;
            cursor += CoverageTableCompiler.Measure(model.Coverage);
            PlaceDevices(leaves, cursor);
            cursor += leafBytes;
        }
        else coverageAt = 0;
        matrixAt = (int)cursor;
        cursor += cells * stride;
        bool shared = ReferenceEquals(model.ClassDef1, model.ClassDef2);
        int cd1At = (int)cursor;
        cursor += ClassDefTableCompiler.Measure(model.ClassDef1!);
        int cd2At = (int)cursor;
        if (!shared) cursor += ClassDefTableCompiler.Measure(model.ClassDef2!);
        if (!compactProfile)
        {
            coverageAt = (int)cursor;
            cursor += CoverageTableCompiler.Measure(model.Coverage);
            PlaceDevices(leaves, cursor);
            cursor += leafBytes;
        }
        status = PairPosView.TryGetCoverageOffsetValue(present: true, origin: 0UL,
            target: (ulong)coverageAt, out ushort coverageOffset, out diagnostic,
            model.Location.AtLocal(DataField.Offset, PairPosLayout.CoverageField));
        if (status != ReadStatus.Done) return status;
        status = PairPosView.TryGetClassDef1OffsetValue(present: true, origin: 0UL,
            target: (ulong)cd1At, out _, out diagnostic,
            model.Location.AtLocal(DataField.Offset, PairPosLayout.Format2HeaderField));
        if (status != ReadStatus.Done) return status;
        status = PairPosView.TryGetClassDef2OffsetValue(present: true, origin: 0UL,
            target: (ulong)(shared ? cd1At : cd2At), out _, out diagnostic,
            model.Location.AtLocal(DataField.Offset, PairPosLayout.Format2HeaderField + 2));
        if (status != ReadStatus.Done) return status;
        // Format-2 Device offsets are subtable-relative, so the placed leaf targets
        // themselves are the deltas the 16-bit field must hold. The compact profile
        // placed them at the header, so only the canonical order can refuse here.
        foreach (var leaf in leaves)
            if (leaf.Position > ushort.MaxValue)
                return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
                    model.Location.AtLocal(DataField.Offset, PairPosLayout.Format2HeaderField),
                    out diagnostic, (ulong)leaf.Position, ushort.MaxValue);
        var targets = DeviceTargets(leaves);
        int length = (int)cursor;
        long emission = EmissionWork(model, [.. leaves], length);
        if (!budget.Work(emission, model.Location, out diagnostic) ||
            !budget.Output(length, model.Location, out diagnostic)) return ReadStatus.ResourceLimit;
        plans = [new PairPosPartPlan(2, length, coverageAt, model.Coverage.Format,
            model.Coverage.RangeCount, [], cd1At, cd2At, [.. leaves], targets, emission,
            matrixAt)];
        diagnostic = default;
        return ReadStatus.Done;
    }

    private static long EmissionWork(PairPosTable model, PairPosTableCompiler.DeviceLeaf[] devices, int length)
    {
        long work = length;
        foreach (var leaf in devices)
            work += StaticDeviceTableCompiler.EmissionWork(leaf.Model) - StaticDeviceTableCompiler.Measure(leaf.Model);
        return work;
    }

    // One device leaf per unique model instance; the two placement profiles assign
    // the positions — canonical = after every declared body, compact (format 2
    // only) = directly after the Coverage. Identity-shared leaves are never
    // address-deduped (W12's shared ClassDef analog), so a too-narrow set-relative
    // or subtable-relative delta stays the finite overflow refusal after both
    // profiles refuse (OQ-T4-05-9, amended 2026-10-18 by the compact+preserve
    // acceptance on the source-serif probe: the format-2 subtable's subtable-
    // relative regions re-plan once, the GsubTableCompiler two-candidate precedent).
    private static ReadStatus CollectDevices(PairPosTable model, IEnumerable<StaticDeviceTable?> slots,
        BuildBudget budget, out List<DeviceLeaf> leaves, out long leafBytes,
        out FontDiagnostic diagnostic)
    {
        leaves = [];
        leafBytes = 0;
        var seen = new HashSet<StaticDeviceTable>(ReferenceEqualityComparer.Instance);
        foreach (var slot in slots)
        {
            if (slot is not StaticDeviceTable device || !seen.Add(device)) continue;
            if (!budget.Buffer(16, model.Location, out diagnostic)) return ReadStatus.ResourceLimit;
            int length = StaticDeviceTableCompiler.Measure(device);
            leaves.Add(new(device, 0));
            leafBytes += length;
        }
        diagnostic = default;
        return ReadStatus.Done;
    }

    private static void PlaceDevices(List<DeviceLeaf> leaves, long start)
    {
        long cursor = start;
        for (int i = 0; i < leaves.Count; i++)
        {
            leaves[i].Position = (int)cursor;
            cursor += StaticDeviceTableCompiler.Measure(leaves[i].Model);
        }
    }

    private static Dictionary<StaticDeviceTable, int> DeviceTargets(List<DeviceLeaf> leaves)
    {
        var targets = new Dictionary<StaticDeviceTable, int>(ReferenceEqualityComparer.Instance);
        foreach (var leaf in leaves) targets.Add(leaf.Model, leaf.Position);
        return targets;
    }

    // Emission into the caller's final destination: deterministic from the model and
    // plan; byte-for-byte stable (MultipleSubst WritePlanned shape).
    internal static void WritePlanned(PairPosTable model, PairPosPartPlan plan, Span<byte> destination)
    {
        var location = model.Location;
        CoverageTableCompiler.RequireWritten(PairPosView.TryWritePairFormat(destination, (ushort)plan.Format, out _, location));
        CoverageTableCompiler.RequireWritten(PairPosView.TryWriteCoverageOffset(destination, (ushort)plan.CoverageTarget, out _, location));
        CoverageTableCompiler.RequireWritten(PairPosView.TryWriteValueFormat1(destination, model.ValueFormat1, out _, location));
        CoverageTableCompiler.RequireWritten(PairPosView.TryWriteValueFormat2(destination, model.ValueFormat2, out _, location));
        if (plan.Format == 1) WriteFormat1(model, plan, destination, location);
        else WriteFormat2(model, plan, destination, location);
        CoverageTableCompiler.WritePlanned(model.Coverage.Glyphs, plan.CoverageFormat,
            plan.CoverageRangeCount, destination.Slice(plan.CoverageTarget,
            CoverageTableCompiler.Measure(model.Coverage)), location);
        foreach (var leaf in plan.Devices)
            StaticDeviceTableCompiler.WritePlanned(leaf.Model, destination.Slice(leaf.Position));
    }

    private static void WriteFormat1(PairPosTable model, PairPosPartPlan plan,
        Span<byte> destination, DataLocation location)
    {
        int count = model.PairSetCount;
        int stride = RowStride(model.ValueFormat1, model.ValueFormat2);
        CoverageTableCompiler.RequireWritten(PairPosView.TryWritePairSetCount(destination, (ushort)count, out _, location));
        for (int i = 0; i < count; i++)
            CoverageTableCompiler.RequireWritten(PairPosView.TryWritePairSetOffsetRecordAt(destination,
                PairPosLayout.Format1Header + (ulong)i * PairPosLayout.OffsetStride,
                new PairPosView.PairSetOffsetRecordData(i, (ushort)plan.SetBases[i]), out _, location));
        for (int i = 0; i < count; i++)
        {
            var set = model.PairSets![i];
            int at = plan.SetBases[i];
            var setSlice = destination.Slice(at, 2 + set.PairValueCount * stride);
            CoverageTableCompiler.RequireWritten(PairSetView.TryWritePairValueCount(setSlice,
                (ushort)set.PairValueCount, out _, location));
            for (int j = 0; j < set.PairValueCount; j++)
            {
                var record = set.Records[j];
                int row = 2 + j * stride;
                CoverageTableCompiler.RequireWritten(PairSetView.TryWritePairSecondGlyphAt(setSlice,
                    (ulong)row, new PairSetView.PairSecondGlyphData(j, record.SecondGlyph), out _, location));
                int words = row + PairSetLayout.WordStride;
                WriteWords(setSlice, words, record.Value1, model.ValueFormat1, j,
                    plan.SetBases[i], plan, location);
                WriteWords(setSlice, words + PairPosLayout.WordStride * BitOperations.PopCount((uint)model.ValueFormat1),
                    record.Value2, model.ValueFormat2, j, plan.SetBases[i], plan, location);
            }
        }
    }

    private static void WriteFormat2(PairPosTable model, PairPosPartPlan plan,
        Span<byte> destination, DataLocation location)
    {
        bool shared = ReferenceEquals(model.ClassDef1, model.ClassDef2);
        CoverageTableCompiler.RequireWritten(PairPosView.TryWriteFormat2HeaderAt(destination,
            PairPosLayout.Format2HeaderField, new PairPosView.Format2HeaderData(-1,
                (ushort)plan.ClassDef1Target, (ushort)(shared ? plan.ClassDef1Target : plan.ClassDef2Target),
                (ushort)model.Class1Count, (ushort)model.Class2Count), out _, location));
        int stride = PairPosLayout.WordStride * (BitOperations.PopCount((uint)model.ValueFormat1)
            + BitOperations.PopCount((uint)model.ValueFormat2));
        for (int class1 = 0; class1 < model.Class1Count; class1++)
        {
            for (int class2 = 0; class2 < model.Class2Count; class2++)
            {
                int at = plan.MatrixTarget + (class1 * model.Class2Count + class2) * stride;
                int ordinal = class1 * model.Class2Count + class2;
                var value1 = model.GetMatrixValue1(class1, class2);
                var value2 = model.GetMatrixValue2(class1, class2);
                WriteWords(destination, at, value1, model.ValueFormat1, ordinal, 0, plan, location);
                WriteWords(destination, at + PairPosLayout.WordStride * BitOperations.PopCount((uint)model.ValueFormat1),
                    value2, model.ValueFormat2, ordinal, 0, plan, location);
            }
        }
        ClassDefTableCompiler.WritePlanned(model.ClassDef1!, destination.Slice(plan.ClassDef1Target,
            ClassDefTableCompiler.Measure(model.ClassDef1!)));
        if (!shared)
            ClassDefTableCompiler.WritePlanned(model.ClassDef2!, destination.Slice(plan.ClassDef2Target,
                ClassDefTableCompiler.Measure(model.ClassDef2!)));
    }

    // The value words and Device offset words of one ValueRecord; the device deltas
    // were preflighted at plan time (fmt1 set-relative, fmt2 subtable-relative), so a
    // zero model encodes the present-null offset 0 (T4-01 decoupling).
    private static void WriteWords(Span<byte> destination, int at, in StaticValueRecord value,
        ushort mask, int ordinal, int deviceBase, PairPosPartPlan plan, DataLocation location)
    {
        for (int slot = 0; slot < 8; slot++)
        {
            if ((mask & (1 << slot)) == 0) continue;
            if (slot < 4)
            {
                CoverageTableCompiler.RequireWritten(PairPosView.TryWritePairScalarAt(destination,
                    (ulong)at, new PairPosView.PairScalarData(ordinal, value.ScalarAt(slot)), out _, location));
            }
            else
            {
                StaticDeviceTable? device = value.DeviceAt(slot);
                ushort offset = 0;
                if (device is not null)
                    offset = checked((ushort)(plan.DeviceTargets[device] - deviceBase));
                CoverageTableCompiler.RequireWritten(PairPosView.TryWritePairDeviceOffsetAt(destination,
                    (ulong)at, new PairPosView.PairDeviceOffsetData(ordinal, offset), out _, location));
            }
            at += PairPosLayout.WordStride;
        }
    }
}
