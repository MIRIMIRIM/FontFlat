using System.Numerics;
using FontWeave.Core;

namespace FontWeave.Tables.Layout;

public readonly ref partial struct SinglePosView
{
    public ReadStatus TryGetValueCount(out ushort count, out FontDiagnostic diagnostic)
    {
        count = 0;
        ReadStatus status = TryRequireKnownVersion(out _, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (Format == 1) { count = 1; diagnostic = default; return ReadStatus.Done; }
        status = TryReadValueCountRecordAt(SinglePosLayout.CountField, -1, out var record, out diagnostic);
        if (status == ReadStatus.Done) count = record.Count;
        return status;
    }

    public ReadStatus Validate(int numGlyphs, out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        var budget = new BuildBudget(limits);
        if (!budget.Input(_data.Length, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        return Process(numGlyphs, 0, new(numGlyphs, Location, _data.Length, false), out _, out diagnostic, budget);
    }

    internal ReadStatus Validate(int numGlyphs, out FontDiagnostic diagnostic, BuildBudget budget) =>
        Process(numGlyphs, 0, new(numGlyphs, Location, _data.Length, false), out _, out diagnostic, budget);

    public ReadStatus TryRead(int numGlyphs, out SinglePosTable? model,
        out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        model = null;
        var budget = new BuildBudget(limits);
        if (!budget.Input(_data.Length, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        return TryRead(numGlyphs, out model, out diagnostic, budget);
    }

    internal ReadStatus TryRead(int numGlyphs, out SinglePosTable? model,
        out FontDiagnostic diagnostic, BuildBudget budget) =>
        Process(numGlyphs, 0, new(numGlyphs, Location, _data.Length, true), out model, out diagnostic, budget);

    internal ReadStatus TryRead(int numGlyphs, out SinglePosTable? model, out FontDiagnostic diagnostic,
        BuildBudget budget, SinglePosReadState state, int subtableStart) =>
        Process(numGlyphs, subtableStart, state, out model, out diagnostic, budget);

    private ReadStatus Process(int numGlyphs, int position, SinglePosReadState state,
        out SinglePosTable? model, out FontDiagnostic diagnostic, BuildBudget budget)
    {
        model = null;
        ReadStatus status = TryRequireKnownVersion(out _, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = CoverageTableCompiler.CheckNumGlyphs(numGlyphs, Location, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (state.NumGlyphs != numGlyphs || position < 0 || position != state.Length - _data.Length)
            return ReadWire.Fail(ReadStatus.InvalidSource, DiagnosticCode.WrongSourceFace, Location, out diagnostic);
        status = SinglePosTableCompiler.ValidateMask(ValueFormat, Location, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = TryGetValueCount(out ushort count, out diagnostic);
        if (status != ReadStatus.Done) return status;
        int start = Format == 1 ? SinglePosLayout.Format1Rows : SinglePosLayout.Format2Rows;
        int stride = SinglePosLayout.WordStride * BitOperations.PopCount((uint)ValueFormat);
        int end = start + count * stride;
        if (!budget.Work(end + (long)count, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        // Every present word is range-checked before child traversal. When the
        // complete extent fits, no second wire parser or temporary row array is needed.
        if (end > _data.Length)
        {
            for (int i = 0; i < count; i++)
            {
                status = ReadWords(start + i * stride, i, out _, out _, out diagnostic);
                if (status != ReadStatus.Done) return status;
            }
        }
        status = Target(CoverageOffset, end, SinglePosLayout.CoverageField, -1, out diagnostic);
        if (status != ReadStatus.Done) return status;
        int coveragePosition = position + CoverageOffset;
        status = ReadCoverage(numGlyphs, coveragePosition, state, out var coverage,
            out var coverageFact, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        if (Format == 2 && count != coverageFact.Count)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.AtLocal(DataField.Count, SinglePosLayout.CountField), out diagnostic,
                count, (ulong)coverageFact.Count);
        StaticValueRecord[]? values = null;
        if (state.Materialize)
        {
            if (!budget.Buffer(40L * count, Location, out diagnostic)) return ReadStatus.ResourceLimit;
            values = new StaticValueRecord[count];
        }
        int consumed = Math.Max(end, CoverageOffset + coverageFact.Length);
        for (int i = 0; i < count; i++)
        {
            int rowPosition = start + i * stride;
            status = ReadWords(rowPosition, i, out var scalar, out var offsets, out diagnostic);
            if (status != ReadStatus.Done) return status;
            StaticDeviceTable? xp = null, yp = null, xa = null, ya = null;
            for (int slot = 4; slot < 8; slot++)
            {
                if ((ValueFormat & (1 << slot)) == 0) continue;
                ushort offset = offsets.At(slot);
                if (offset == 0) continue;
                int field = rowPosition + SinglePosLayout.WordStride * BitOperations.PopCount((uint)(ValueFormat & ((1 << slot) - 1)));
                status = Target(offset, end, field, i, out diagnostic);
                if (status != ReadStatus.Done) return status;
                status = ReadDevice(position + offset, state, out var device, out int length, out diagnostic, budget);
                if (status != ReadStatus.Done) return status;
                consumed = Math.Max(consumed, offset + length);
                switch (slot) { case 4: xp = device; break; case 5: yp = device; break; case 6: xa = device; break; case 7: ya = device; break; }
            }
            if (values is not null)
                values[i] = new(scalar.XPlacement, scalar.YPlacement, scalar.XAdvance, scalar.YAdvance, xp, yp, xa, ya);
        }
        if (values is not null) model = new(coverage!, values, Format, ValueFormat, Location, consumed);
        diagnostic = default; return ReadStatus.Done;
    }

    private readonly record struct DeviceWords(ushort X, ushort Y, ushort AdvanceX, ushort AdvanceY)
    { internal ushort At(int slot) => slot switch { 4 => X, 5 => Y, 6 => AdvanceX, _ => AdvanceY }; }

    private ReadStatus ReadWords(int position, int ordinal, out StaticValueRecord scalar,
        out DeviceWords offsets, out FontDiagnostic diagnostic)
    {
        scalar = default; offsets = default;
        short x = 0, y = 0, ax = 0, ay = 0;
        ushort dx = 0, dy = 0, dax = 0, day = 0;
        int at = position;
        for (int slot = 0; slot < 8; slot++)
        {
            if ((ValueFormat & (1 << slot)) == 0) continue;
            ReadStatus status;
            if (slot < 4)
            {
                status = TryReadSignedValueAt((ulong)at, ordinal, out var word, out diagnostic);
                if (status != ReadStatus.Done) return status;
                switch (slot) { case 0: x = word.Value; break; case 1: y = word.Value; break; case 2: ax = word.Value; break; case 3: ay = word.Value; break; }
            }
            else
            {
                status = TryReadDeviceOffsetAt((ulong)at, ordinal, out var word, out diagnostic);
                if (status != ReadStatus.Done) return status;
                switch (slot) { case 4: dx = word.Offset; break; case 5: dy = word.Offset; break; case 6: dax = word.Offset; break; case 7: day = word.Offset; break; }
            }
            at += SinglePosLayout.WordStride;
        }
        scalar = new(x, y, ax, ay); offsets = new(dx, dy, dax, day);
        diagnostic = default; return ReadStatus.Done;
    }

    private ReadStatus ReadCoverage(int numGlyphs, int target, SinglePosReadState state,
        out CoverageTable? model, out SinglePosReadState.Leaf fact, out FontDiagnostic diagnostic, BuildBudget budget)
    {
        model = null; fact = default;
        if (!budget.Work(1, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        if (state.CoverageFacts.TryGetValue(target, out fact))
        { if (state.Materialize) model = state.Coverages[target]; diagnostic = default; return ReadStatus.Done; }
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
        fact = new(count, length); state.CoverageFacts.Add(target, fact);
        if (state.Materialize) state.Coverages.Add(target, model!);
        diagnostic = default; return ReadStatus.Done;
    }

    private ReadStatus ReadDevice(int target, SinglePosReadState state, out StaticDeviceTable? model,
        out int length, out FontDiagnostic diagnostic, BuildBudget budget)
    {
        model = null; length = 0;
        if (!budget.Work(1, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        if (state.DeviceFacts.TryGetValue(target, out var fact))
        { length = fact.Length; if (state.Materialize) model = state.Devices[target]; diagnostic = default; return ReadStatus.Done; }
        int local = target - (state.Length - _data.Length);
        ReadStatus status = StaticDeviceView.TryOpen(_data.Slice(local), out var view, out diagnostic, state.At(target));
        if (status != ReadStatus.Done) return status;
        status = view.Inspect(out int count, out length, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        if (state.Materialize)
        {
            status = view.TryRead(out model, out diagnostic, budget);
            if (status != ReadStatus.Done) return status;
        }
        if (!budget.Buffer(state.Materialize ? 32 : 16, state.At(target), out diagnostic)) return ReadStatus.ResourceLimit;
        state.DeviceFacts.Add(target, new(count, length));
        if (state.Materialize) state.Devices.Add(target, model!);
        diagnostic = default; return ReadStatus.Done;
    }

    private ReadStatus Target(ushort offset, int rowsEnd, int field, int ordinal, out FontDiagnostic diagnostic)
    {
        if (offset == 0 || offset >= _data.Length)
            return ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.RangeOutsideSource,
                Location.AtLocal(DataField.Offset, (ulong)field, ordinal), out diagnostic, offset, (ulong)_data.Length);
        if (offset < rowsEnd)
            return ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.StorageBeforeRecords,
                Location.AtLocal(DataField.Offset, (ulong)field, ordinal), out diagnostic, offset, (ulong)rowsEnd);
        diagnostic = default; return ReadStatus.Done;
    }
}
