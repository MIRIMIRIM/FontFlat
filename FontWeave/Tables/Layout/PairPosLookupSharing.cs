using FontWeave.Core;

namespace FontWeave.Tables.Layout;

internal static class PairPosLookupSharing
{
    private static readonly IEqualityComparer<ClassDefTable> ClassContent =
        EqualityComparer<ClassDefTable>.Create(
            static (left, right) => ReferenceEquals(left, right) ||
                left is not null && right is not null && left.Format == right.Format &&
                left.Ranges.SequenceEqual(right.Ranges),
            static model =>
            {
                var hash = new HashCode();
                hash.Add(model.Format);
                foreach (var range in model.Ranges) hash.Add(range);
                return hash.ToHashCode();
            });

    private static readonly IEqualityComparer<StaticDeviceTable> DeviceContent =
        EqualityComparer<StaticDeviceTable>.Create(
            static (left, right) => ReferenceEquals(left, right) ||
                left is not null && right is not null && left.StartSize == right.StartSize &&
                left.EndSize == right.EndSize && left.DeltaFormat == right.DeltaFormat &&
                left.ConsumedLength == right.ConsumedLength && left.Deltas.SequenceEqual(right.Deltas),
            static model =>
            {
                var hash = new HashCode();
                hash.Add(model.StartSize);
                hash.Add(model.EndSize);
                hash.Add(model.DeltaFormat);
                hash.Add(model.ConsumedLength);
                foreach (sbyte delta in model.Deltas) hash.Add(delta);
                return hash.ToHashCode();
            });

    internal static ReadStatus TryPlan(LookupTable model, int[] entries, LookupSlotPlan[] source,
        int count, bool extension, out LookupEmitPlan? plan, out FontDiagnostic diagnostic, BuildBudget budget)
    {
        plan = null;
        diagnostic = default;
        if (model.Kind != LayoutLookupKind.Gpos || count == 0) return ReadStatus.Done;
        for (int i = 0; i < count; i++)
            if (source[i] is not PairSlotPlan) return ReadStatus.Done;
        if (count == 1 && ((PairSlotPlan)source[0]).Part.Format != 2) return ReadStatus.Done;

        var location = model.Location;
        if (!budget.Work(count, location, out diagnostic) ||
            !budget.Buffer(32L * count, location, out diagnostic)) return ReadStatus.ResourceLimit;
        int headerLength = LookupLayout.HeaderLength + LookupLayout.OffsetStride * entries.Length +
            (model.MarkFilteringSet.HasValue ? 2 : 0);
        if (extension && headerLength + 8L * (count - 1) > ushort.MaxValue) return ReadStatus.Done;
        var inlineLengths = new int[count];
        var bodyTargets = new int[count];
        int largest = -1;
        long cursor = headerLength + (extension ? 8L * count : 0);
        long privateLength = cursor;
        for (int i = 0; i < count; i++)
        {
            var pair = (PairSlotPlan)source[i];
            inlineLengths[i] = PairPosTableCompiler.BodyLength(pair.Child, pair.Part) +
                CoverageTableCompiler.Measure(pair.Child.Coverage);
            privateLength += pair.Length;
            if (!extension && (largest < 0 || inlineLengths[i] > inlineLengths[largest])) largest = i;
        }
        for (int i = 0; i < count; i++)
        {
            if (i == largest) continue;
            if (!extension && cursor > ushort.MaxValue) return ReadStatus.Done;
            if (!budget.Output(cursor + inlineLengths[i], location, out diagnostic)) return ReadStatus.ResourceLimit;
            bodyTargets[i] = (int)cursor;
            cursor += inlineLengths[i];
        }
        if (largest >= 0)
        {
            if (cursor > ushort.MaxValue) return ReadStatus.Done;
            if (!budget.Output(cursor + inlineLengths[largest], location, out diagnostic)) return ReadStatus.ResourceLimit;
            bodyTargets[largest] = (int)cursor;
            cursor += inlineLengths[largest];
        }

        var classes = new Dictionary<ClassDefTable, int>(ClassContent);
        var canonicalClasses = new Dictionary<ClassDefTable, ClassDefTable>(ReferenceEqualityComparer.Instance);
        var classBodies = new ClassDefTable?[2 * count];
        var devices = new Dictionary<StaticDeviceTable, int>(DeviceContent);
        for (int i = 0; i < count; i++)
        {
            var pair = (PairSlotPlan)source[i];
            if (pair.Part.Format == 2)
            {
                for (int side = 0; side < 2; side++)
                {
                    var original = side == 0 ? pair.Child.ClassDef1! : pair.Child.ClassDef2!;
                    if (!canonicalClasses.TryGetValue(original, out var table))
                    {
                        if (!budget.Buffer(16, location, out diagnostic)) return ReadStatus.ResourceLimit;
                        ReadStatus status = TryCanonicalize(original, out var canonical, out diagnostic, budget);
                        if (status != ReadStatus.Done) return status;
                        table = canonical!;
                        canonicalClasses.Add(original, table);
                    }
                    classBodies[2 * i + side] = table;
                    if (!budget.Work(6L * table.Ranges.Length + 6, location, out diagnostic)) return ReadStatus.ResourceLimit;
                    if (classes.ContainsKey(table)) continue;
                    if (!budget.Buffer(16, location, out diagnostic) ||
                        !budget.Output(cursor + ClassDefTableCompiler.Measure(table), location, out diagnostic))
                        return ReadStatus.ResourceLimit;
                    classes.Add(table, (int)cursor);
                    cursor += ClassDefTableCompiler.Measure(table);
                }
            }
            foreach (var leaf in pair.Part.Devices)
            {
                var table = leaf.Model;
                if (!budget.Work(6L * table.Deltas.Length + 24, location, out diagnostic)) return ReadStatus.ResourceLimit;
                if (devices.ContainsKey(table)) continue;
                if (!budget.Buffer(16, location, out diagnostic) ||
                    !budget.Output(cursor + StaticDeviceTableCompiler.Measure(table), location, out diagnostic))
                    return ReadStatus.ResourceLimit;
                devices.Add(table, (int)cursor);
                cursor += StaticDeviceTableCompiler.Measure(table);
            }
        }
        if (cursor >= privateLength) return ReadStatus.Done;

        var slots = new LookupSlotPlan[count];
        for (int i = 0; i < count; i++)
        {
            var original = (PairSlotPlan)source[i];
            var part = original.Part;
            int at = bodyTargets[i];
            int coverageAt = PairPosTableCompiler.BodyLength(original.Child, part);
            int class1At = part.Format == 2 ? classes[classBodies[2 * i]!] - at : 0;
            int class2At = part.Format == 2 ? classes[classBodies[2 * i + 1]!] - at : 0;
            if (coverageAt > ushort.MaxValue || class1At > ushort.MaxValue || class2At > ushort.MaxValue)
                return ReadStatus.Done;
            if (!budget.Work(original.Length, location, out diagnostic) ||
                !budget.Buffer(24L * part.Devices.Length, location, out diagnostic)) return ReadStatus.ResourceLimit;
            var leaves = new PairPosTableCompiler.DeviceLeaf[part.Devices.Length];
            var targets = new Dictionary<StaticDeviceTable, int>(ReferenceEqualityComparer.Instance);
            for (int j = 0; j < leaves.Length; j++)
            {
                var device = part.Devices[j].Model;
                int target = devices[device] - at;
                if (part.Format == 2 && target > ushort.MaxValue) return ReadStatus.Done;
                leaves[j] = new(device, target);
                targets.Add(device, target);
            }
            if (part.Format == 1)
            {
                ReadStatus status = PairPosTableCompiler.PreflightFormat1Deltas(original.Child, targets,
                    part.SetBases, PairPosTableCompiler.RowStride(original.Child.ValueFormat1,
                        original.Child.ValueFormat2), out diagnostic);
                if (status == ReadStatus.UnsupportedFormat && diagnostic.Code == DiagnosticCode.UnsupportedOperation)
                {
                    diagnostic = default;
                    return ReadStatus.Done;
                }
                if (status != ReadStatus.Done) return status;
            }
            var relocated = part with
            {
                Length = (int)cursor - at,
                CoverageTarget = coverageAt,
                ClassDef1Target = class1At,
                ClassDef2Target = class2At,
                Devices = leaves,
                DeviceTargets = targets,
                ClassDef1Body = classBodies[2 * i],
                ClassDef2Body = classBodies[2 * i + 1],
            };
            var slot = original with
            {
                Part = relocated,
                Target = extension ? (ushort)0 : (ushort)at,
                Length = inlineLengths[i],
            };
            if (extension)
            {
                int wrapperAt = headerLength + 8 * i;
                ReadStatus status = ExtensionPosView.TryGetWrappedPairPosOffsetValue(true, 0UL,
                    (ulong)(at - wrapperAt), out _, out diagnostic,
                    location.AtLocal(DataField.Offset, (ulong)(wrapperAt + ExtensionPosLayout.OffsetAt)));
                if (status != ReadStatus.Done) return status;
                slots[i] = new ExtensionSlotPlan(2, slot, (ushort)wrapperAt, at, inlineLengths[i] + 8, true);
            }
            else slots[i] = slot;
        }
        if (!budget.Work(cursor, location, out diagnostic) ||
            !budget.Output(cursor, location, out diagnostic) ||
            !budget.Work(cursor, location, out diagnostic)) return ReadStatus.ResourceLimit;
        plan = new(extension ? 9 : 2, entries, slots, count, (int)cursor);
        diagnostic = default;
        return ReadStatus.Done;
    }

    private static ReadStatus TryCanonicalize(ClassDefTable source, out ClassDefTable? canonical,
        out FontDiagnostic diagnostic, BuildBudget budget)
    {
        canonical = null;
        if (!budget.Work(2L * source.Ranges.Length + 2, source.Location, out diagnostic))
            return ReadStatus.ResourceLimit;
        int first = -1, last = -1, nonzeroRanges = 0, gaps = 0;
        ushort previousClass = 0;
        foreach (var range in source.Ranges)
        {
            if (range.ClassValue == 0) continue;
            if (first < 0) first = range.StartGlyph;
            if (last < 0 || range.StartGlyph != last + 1 || range.ClassValue != previousClass)
                nonzeroRanges++;
            if (last >= 0 && range.StartGlyph > last + 1) gaps++;
            last = range.EndGlyph;
            previousClass = range.ClassValue;
        }
        int denseLength = ClassDefLayout.DenseValuesOffset + ClassDefLayout.ValueStride *
            (first < 0 ? 0 : last - first + 1);
        int sparseLength = ClassDefLayout.SparseRangesOffset + ClassDefLayout.RangeStride * nonzeroRanges;
        ushort format = denseLength <= sparseLength ? (ushort)1 : (ushort)2;
        int count = nonzeroRanges + (format == 1 ? gaps : 0);
        if (!budget.Buffer((long)ClassDefLayout.RangeStride * count, source.Location, out diagnostic))
            return ReadStatus.ResourceLimit;
        var ranges = count == 0 ? [] : new ClassDefRange[count];
        int written = 0;
        foreach (var range in source.Ranges)
        {
            if (range.ClassValue == 0) continue;
            if (written > 0)
            {
                var previous = ranges[written - 1];
                if (previous.EndGlyph + 1 == range.StartGlyph && previous.ClassValue == range.ClassValue)
                {
                    ranges[written - 1] = new(previous.StartGlyph, range.EndGlyph, previous.ClassValue);
                    continue;
                }
                if (format == 1 && previous.EndGlyph + 1 < range.StartGlyph)
                    ranges[written++] = new((ushort)(previous.EndGlyph + 1),
                        (ushort)(range.StartGlyph - 1), 0);
            }
            ranges[written++] = range;
        }
        return ClassDefTableCompiler.TryCreate(ranges, source.NumGlyphs, format,
            out canonical, out diagnostic, budget, source.Location);
    }
}
