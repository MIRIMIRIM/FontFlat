using System.Numerics;
using FontWeave.Core;

namespace FontWeave.Tables.Layout;

public readonly ref partial struct PairPosView
{
    public ReadStatus Validate(int numGlyphs, out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        var budget = new BuildBudget(limits);
        if (!budget.Input(_data.Length, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        return Process(numGlyphs, 0, new(numGlyphs, Location, _data.Length, false), out _, out diagnostic, budget);
    }

    internal ReadStatus Validate(int numGlyphs, out FontDiagnostic diagnostic, BuildBudget budget) =>
        Process(numGlyphs, 0, new(numGlyphs, Location, _data.Length, false), out _, out diagnostic, budget);

    public ReadStatus TryRead(int numGlyphs, out PairPosTable? model,
        out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        model = null;
        var budget = new BuildBudget(limits);
        if (!budget.Input(_data.Length, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        return TryRead(numGlyphs, out model, out diagnostic, budget);
    }

    internal ReadStatus TryRead(int numGlyphs, out PairPosTable? model,
        out FontDiagnostic diagnostic, BuildBudget budget) =>
        Process(numGlyphs, 0, new(numGlyphs, Location, _data.Length, true), out model, out diagnostic, budget);

    internal ReadStatus TryRead(int numGlyphs, out PairPosTable? model, out FontDiagnostic diagnostic,
        BuildBudget budget, PairPosReadState state, int subtableStart) =>
        Process(numGlyphs, subtableStart, state, out model, out diagnostic, budget);

    private ReadStatus Process(int numGlyphs, int position, PairPosReadState state,
        out PairPosTable? model, out FontDiagnostic diagnostic, BuildBudget budget)
    {
        model = null;
        ReadStatus status = TryRequireKnownVersion(out _, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = CoverageTableCompiler.CheckNumGlyphs(numGlyphs, Location, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (state.NumGlyphs != numGlyphs || position < 0 || position != state.Length - _data.Length)
            return ReadWire.Fail(ReadStatus.InvalidSource, DiagnosticCode.WrongSourceFace, Location, out diagnostic);
        status = ValidateMasks(ValueFormat1, ValueFormat2, Location, out diagnostic);
        if (status != ReadStatus.Done) return status;
        return PairFormat == 1
            ? ProcessFormat1(numGlyphs, position, state, out model, out diagnostic, budget)
            : ProcessFormat2(numGlyphs, position, state, out model, out diagnostic, budget);
    }

    // The two ValueFormats reuse the SinglePos mask contract (SupportedMask=0x00FF,
    // reserved bits refuse); each format's refusal carries its own field position.
    internal static ReadStatus ValidateMasks(ushort valueFormat1, ushort valueFormat2,
        DataLocation location, out FontDiagnostic diagnostic)
    {
        if ((valueFormat1 & ~PairPosLayout.SupportedMask) != 0)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                location.AtLocal(DataField.Flags, PairPosLayout.Format1Field), out diagnostic,
                valueFormat1, PairPosLayout.SupportedMask);
        if ((valueFormat2 & ~PairPosLayout.SupportedMask) != 0)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                location.AtLocal(DataField.Flags, PairPosLayout.Format2Field), out diagnostic,
                valueFormat2, PairPosLayout.SupportedMask);
        diagnostic = default;
        return ReadStatus.Done;
    }

    // Format 1: one PairSet per Coverage index (W8's relation), strict second-glyph
    // order inside each set (W7's pin), PairSet-relative Device tables (W2's base).
    private ReadStatus ProcessFormat1(int numGlyphs, int position, PairPosReadState state,
        out PairPosTable? model, out FontDiagnostic diagnostic, BuildBudget budget)
    {
        model = null;
        ReadStatus status = TryGetPairSetCount(out ushort pairSetCount, out diagnostic);
        if (status != ReadStatus.Done) return status;
        int offsetsEnd = PairPosLayout.Format1Header + PairPosLayout.OffsetStride * pairSetCount;
        // The generated counted-array extent proves every offset row lies inside the
        // source; the per-row guard below is only reached when it does not.
        status = TryRequirePairSetOffsetRecordExtent(out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (!budget.Work(offsetsEnd + (long)pairSetCount, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        status = TryGetCoverageOffset(out ushort coverageOffset, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = ReadCoverage(numGlyphs, position + coverageOffset, state,
            out var coverage, out var coverageFact, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        if (pairSetCount != coverageFact.Count)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.AtLocal(DataField.Count, PairPosLayout.PairSetCountField), out diagnostic,
                pairSetCount, (ulong)coverageFact.Count);
        PairSetTable[]? sets = null;
        if (state.Materialize)
        {
            if (!budget.Buffer(16L * pairSetCount, Location, out diagnostic)) return ReadStatus.ResourceLimit;
            sets = new PairSetTable[pairSetCount];
        }
        var setCache = new Dictionary<ushort, PairSetTable>();
        int consumed = Math.Max(offsetsEnd, coverageOffset + coverageFact.Length);
        for (int i = 0; i < pairSetCount; i++)
        {
            status = TryGetPairSetOffsetRecord(i, out var record, out diagnostic);
            if (status != ReadStatus.Done) return status;
            ushort setOffset = record.PairSetOffset;
            if (!setCache.TryGetValue(setOffset, out PairSetTable? set))
            {
                status = TryOpenPairSet(i, out var setView, out _, out diagnostic);
                if (status != ReadStatus.Done) return status;
                status = setView.TryRead(numGlyphs, ValueFormat1, ValueFormat2, state,
                    position + setOffset, out set, out diagnostic, budget);
                if (status != ReadStatus.Done) return status;
                if (!budget.Buffer(16, Location, out diagnostic)) return ReadStatus.ResourceLimit;
                setCache.Add(setOffset, set!);
            }
            // Validate mode leaves the set model null; only materialize-mode consumed
            // extents feed the model (LigCaretList validate-path precedent).
            consumed = Math.Max(consumed, setOffset + (set?.ConsumedLength ?? 0));
            if (sets is not null) sets[i] = set!;
        }
        if (state.Materialize)
            model = new(coverage!, PairFormat, ValueFormat1, ValueFormat2, pairSets: sets,
                location: Location, consumedLength: consumed);
        diagnostic = default;
        return ReadStatus.Done;
    }

    // Format 2: the declared class1×class2 matrix owns the window (W9: a Coverage
    // landing before the declared rows end is the StorageBeforeRecords family
    // refusal); the counts-versus-ClassDef-peak relation is out of contract
    // (OQ-T4-05-2 - a declared super-wide row is a legal all-zero row). The two
    // ClassDef offsets may legally name one shared table (W12, spec gpos.md:307).
    private ReadStatus ProcessFormat2(int numGlyphs, int position, PairPosReadState state,
        out PairPosTable? model, out FontDiagnostic diagnostic, BuildBudget budget)
    {
        model = null;
        ReadStatus status = TryReadFormat2HeaderAt(PairPosLayout.Format2HeaderField, -1,
            out var format2Header, out diagnostic);
        if (status != ReadStatus.Done) return status;
        ushort class1Count = format2Header.Class1Count, class2Count = format2Header.Class2Count;
        int cellStride = PairPosLayout.WordStride * (BitOperations.PopCount((uint)ValueFormat1)
            + BitOperations.PopCount((uint)ValueFormat2));
        long cellCount = (long)class1Count * class2Count;
        long matrixEnd = PairPosLayout.Format2Header + cellCount * cellStride;
        if (!budget.Work(matrixEnd + class1Count, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        status = TryGetCoverageOffset(out ushort coverageOffset, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = ReadCoverage(numGlyphs, position + coverageOffset, state,
            out var coverage, out var coverageFact, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        if (coverageOffset < matrixEnd)
            return ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.StorageBeforeRecords,
                Location.AtLocal(DataField.Offset, PairPosLayout.CoverageField), out diagnostic,
                coverageOffset, (ulong)matrixEnd);
        ushort classDef1Offset = format2Header.ClassDef1Offset, classDef2Offset = format2Header.ClassDef2Offset;
        // The same declared window owns both ClassDef offsets (W9's truncated-declared
        // -rows shape lands the first ClassDef body inside the matrix).
        if (classDef1Offset < matrixEnd)
            return ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.StorageBeforeRecords,
                Location.AtLocal(DataField.Offset, PairPosLayout.Format2HeaderField), out diagnostic,
                classDef1Offset, (ulong)matrixEnd);
        if (classDef2Offset < matrixEnd)
            return ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.StorageBeforeRecords,
                Location.AtLocal(DataField.Offset, PairPosLayout.Format2HeaderField + 2), out diagnostic,
                classDef2Offset, (ulong)matrixEnd);
        status = TryOpenClassDef1At(PairPosLayout.Format2HeaderField, -1, out var classDef1View,
            out _, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = TryOpenClassDef2At(PairPosLayout.Format2HeaderField, -1, out var classDef2View,
            out _, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = ReadClassDef(numGlyphs, position + classDef1Offset, classDef1View,
            state, out var classDef1, out int classDef1Length, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        status = ReadClassDef(numGlyphs, position + classDef2Offset, classDef2View,
            state, out var classDef2, out int classDef2Length, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        // The window checks plus the openers proved every declared child target and the
        // matrix itself fit inside the source before the cell walk.
        StaticValueRecord[]? values1 = null, values2 = null;
        if (state.Materialize)
        {
            if (!budget.Buffer(80L * cellCount, Location, out diagnostic)) return ReadStatus.ResourceLimit;
            values1 = new StaticValueRecord[cellCount];
            values2 = new StaticValueRecord[cellCount];
        }
        int consumed = Math.Max((int)Math.Max(matrixEnd, coverageOffset + coverageFact.Length),
            Math.Max(classDef1Offset + classDef1Length, classDef2Offset + classDef2Length));
        for (int class1 = 0; class1 < class1Count; class1++)
        {
            long rowPosition = PairPosLayout.Format2Header + (long)class1 * class2Count * cellStride;
            for (int class2 = 0; class2 < class2Count; class2++)
            {
                int cellPosition = (int)(rowPosition + (long)class2 * cellStride);
                status = ReadCell(cellPosition, class1 * class2Count + class2, cellStride,
                    position, matrixEnd, state, out var value1, out var value2,
                    ref consumed, out diagnostic, budget);
                if (status != ReadStatus.Done) return status;
                if (values1 is not null && values2 is not null)
                {
                    values1[class1 * class2Count + class2] = value1;
                    values2[class1 * class2Count + class2] = value2;
                }
            }
        }
        if (state.Materialize)
            model = new(coverage!, PairFormat, ValueFormat1, ValueFormat2, classDef1: classDef1,
                classDef2: classDef2, class1Count: class1Count, class2Count: class2Count,
                values1: values1, values2: values2, location: Location, consumedLength: consumed);
        diagnostic = default;
        return ReadStatus.Done;
    }

    // One matrix cell: the first-glyph words then the second-glyph words, in mask
    // order; Device tables are subtable-relative (spec gpos.md:679's second parent).
    private ReadStatus ReadCell(int cellPosition, int ordinal, int cellStride, int position,
        long matrixEnd, PairPosReadState state, out StaticValueRecord value1,
        out StaticValueRecord value2, ref int consumed, out FontDiagnostic diagnostic,
        BuildBudget budget)
    {
        value1 = value2 = default;
        ReadStatus status = ReadWords(cellPosition, ordinal, ValueFormat1, out var words1, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = ReadWords(cellPosition + PairPosLayout.WordStride * BitOperations.PopCount((uint)ValueFormat1),
            ordinal, ValueFormat2, out var words2, out diagnostic);
        if (status != ReadStatus.Done) return status;
        StaticDeviceTable? x1 = null, y1 = null, ax1 = null, ay1 = null;
        StaticDeviceTable? x2 = null, y2 = null, ax2 = null, ay2 = null;
        for (int slot = 4; slot < 8; slot++)
        {
            status = ReadSlotDevice(words1.OffsetAt(slot), slot, ValueFormat1, cellPosition,
                ordinal, position, matrixEnd, state, ref consumed, out var device1, out diagnostic, budget);
            if (status != ReadStatus.Done) return status;
            switch (slot) { case 4: x1 = device1; break; case 5: y1 = device1; break; case 6: ax1 = device1; break; default: ay1 = device1; break; }
            status = ReadSlotDevice(words2.OffsetAt(slot), slot, ValueFormat2, cellPosition,
                ordinal, position, matrixEnd, state, ref consumed, out var device2, out diagnostic, budget);
            if (status != ReadStatus.Done) return status;
            switch (slot) { case 4: x2 = device2; break; case 5: y2 = device2; break; case 6: ax2 = device2; break; default: ay2 = device2; break; }
        }
        value1 = WithDevices(words1.Value, x1, y1, ax1, ay1);
        value2 = WithDevices(words2.Value, x2, y2, ax2, ay2);
        diagnostic = default;
        return ReadStatus.Done;
    }

    private static StaticValueRecord WithDevices(StaticValueRecord value,
        StaticDeviceTable? xPlacement, StaticDeviceTable? yPlacement,
        StaticDeviceTable? xAdvance, StaticDeviceTable? yAdvance) =>
        new(value.XPlacement, value.YPlacement, value.XAdvance, value.YAdvance,
            xPlacement, yPlacement, xAdvance, yAdvance);

    private ReadStatus ReadSlotDevice(ushort offset, int slot, ushort mask, int cellPosition,
        int ordinal, int position, long matrixEnd, PairPosReadState state,
        ref int consumed, out StaticDeviceTable? device, out FontDiagnostic diagnostic,
        BuildBudget budget)
    {
        device = null;
        if ((mask & (1 << slot)) == 0 || offset == 0)
        {
            diagnostic = default;
            return ReadStatus.Done;
        }
        int field = cellPosition + PairPosLayout.WordStride * BitOperations.PopCount((uint)(mask & ((1 << slot) - 1)));
        if (offset >= _data.Length)
            return ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.RangeOutsideSource,
                Location.AtLocal(DataField.Offset, (ulong)field, ordinal), out diagnostic, offset, (ulong)_data.Length);
        if (offset < matrixEnd)
            return ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.StorageBeforeRecords,
                Location.AtLocal(DataField.Offset, (ulong)field, ordinal), out diagnostic, offset, (ulong)matrixEnd);
        ReadStatus status = ReadDevice(position + offset, _data.Slice(offset), state,
            out device, out int length, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        if (offset + length > consumed) consumed = offset + length;
        diagnostic = default;
        return ReadStatus.Done;
    }

    private readonly record struct Words(StaticValueRecord Value, ushort DeviceX,
        ushort DeviceY, ushort DeviceAdvanceX, ushort DeviceAdvanceY)
    {
        internal ushort OffsetAt(int slot) => slot switch
        { 4 => DeviceX, 5 => DeviceY, 6 => DeviceAdvanceX, _ => DeviceAdvanceY };
    }

    private ReadStatus ReadWords(int position, int ordinal, ushort mask, out Words words,
        out FontDiagnostic diagnostic)
    {
        words = default;
        short x = 0, y = 0, ax = 0, ay = 0;
        ushort dx = 0, dy = 0, dax = 0, day = 0;
        int at = position;
        for (int slot = 0; slot < 8; slot++)
        {
            if ((mask & (1 << slot)) == 0) continue;
            ReadStatus status;
            if (slot < 4)
            {
                status = TryReadPairScalarAt((ulong)at, ordinal, out var word, out diagnostic);
                if (status != ReadStatus.Done) return status;
                switch (slot) { case 0: x = word.Value; break; case 1: y = word.Value; break; case 2: ax = word.Value; break; case 3: ay = word.Value; break; }
            }
            else
            {
                status = TryReadPairDeviceOffsetAt((ulong)at, ordinal, out var word, out diagnostic);
                if (status != ReadStatus.Done) return status;
                switch (slot) { case 4: dx = word.Offset; break; case 5: dy = word.Offset; break; case 6: dax = word.Offset; break; case 7: day = word.Offset; break; }
            }
            at += PairPosLayout.WordStride;
        }
        words = new(new(x, y, ax, ay), dx, dy, dax, day);
        diagnostic = default;
        return ReadStatus.Done;
    }

    private ReadStatus ReadCoverage(int numGlyphs, int target, PairPosReadState state,
        out CoverageTable? model, out PairPosReadState.Leaf fact, out FontDiagnostic diagnostic, BuildBudget budget)
    {
        model = null; fact = default;
        if (!budget.Work(1, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        if (state.CoverageFacts.TryGetValue(target, out fact))
        { if (state.Materialize) state.Coverages.TryGetValue(target, out model); diagnostic = default; return ReadStatus.Done; }
        int local = target - (state.Length - _data.Length);
        ReadStatus status = CoverageView.TryOpen(_data.Slice(local), out var view, out diagnostic, state.At(target));
        if (status != ReadStatus.Done) return status;
        status = view.Inspect(numGlyphs, out int count, out int length, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        if (state.Materialize)
        {
            // Expansion consumes the facts Inspect just proved; no second inspection.
            status = view.Materialize(numGlyphs, count, length, out model, out diagnostic, budget);
            if (status != ReadStatus.Done) return status;
        }
        if (!budget.Buffer(state.Materialize ? 32 : 16, state.At(target), out diagnostic)) return ReadStatus.ResourceLimit;
        fact = new(count, length);
        state.CoverageFacts.Add(target, fact);
        if (state.Materialize) state.Coverages.Add(target, model!);
        diagnostic = default;
        return ReadStatus.Done;
    }

    private ReadStatus ReadClassDef(int numGlyphs, int target, ClassDefView view,
        PairPosReadState state, out ClassDefTable? model, out int length,
        out FontDiagnostic diagnostic, BuildBudget budget)
    {
        model = null; length = 0;
        if (!budget.Work(1, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        if (state.ClassDefFacts.TryGetValue(target, out var fact))
        {
            length = fact.Length;
            if (state.Materialize) state.ClassDefs.TryGetValue(target, out model);
            diagnostic = default;
            return ReadStatus.Done;
        }
        if (state.Materialize)
        {
            ReadStatus status = view.TryRead(numGlyphs, out model, out diagnostic, budget);
            if (status != ReadStatus.Done) return status;
            length = model!.ConsumedLength;
        }
        else
        {
            ReadStatus status = view.Validate(numGlyphs, out diagnostic, budget);
            if (status != ReadStatus.Done) return status;
        }
        if (!budget.Buffer(state.Materialize ? 32 : 16, state.At(target), out diagnostic)) return ReadStatus.ResourceLimit;
        state.ClassDefFacts.Add(target, new(0, length));
        if (state.Materialize) state.ClassDefs.Add(target, model!);
        diagnostic = default;
        return ReadStatus.Done;
    }

    private ReadStatus ReadDevice(int target, ReadOnlySpan<byte> slice, PairPosReadState state,
        out StaticDeviceTable? model, out int length, out FontDiagnostic diagnostic, BuildBudget budget)
    {
        model = null; length = 0;
        if (!budget.Work(1, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        if (state.DeviceFacts.TryGetValue(target, out var fact))
        { length = fact.Length; if (state.Materialize) state.Devices.TryGetValue(target, out model); diagnostic = default; return ReadStatus.Done; }
        ReadStatus status = StaticDeviceView.TryOpen(slice, out var view, out diagnostic, state.At(target));
        if (status != ReadStatus.Done) return status;
        status = view.InspectWords(out int count, out int bits, out length, out ushort lastWord, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        if (state.Materialize)
        {
            // Expansion consumes the facts InspectWords just proved (spec 6.2).
            status = view.Materialize(count, bits, length, lastWord, out model, out diagnostic, budget);
            if (status != ReadStatus.Done) return status;
        }
        if (!budget.Buffer(state.Materialize ? 32 : 16, state.At(target), out diagnostic)) return ReadStatus.ResourceLimit;
        state.DeviceFacts.Add(target, new(count, length));
        if (state.Materialize) state.Devices.Add(target, model!);
        diagnostic = default;
        return ReadStatus.Done;
    }
}
