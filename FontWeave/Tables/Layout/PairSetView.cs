using System.Numerics;
using FontWeave.Core;

namespace FontWeave.Tables.Layout;

public readonly ref partial struct PairSetView
{
    // The dynamic rows are this view's hand-written boundary (PairPos schema comment):
    // the record stride is 2 + 2*popcount(vf1) + 2*popcount(vf2) bytes and the per-row
    // word walk follows the two masks in declared slot order (SinglePosView.ReadWords
    // same shape, not generalized). The strict second-glyph order is the semantic
    // chain's property (OQ-T4-05-1, W7's pin); the GID domain uses the caller's
    // explicit numGlyphs, never a reverse inference. Devices are PairSet-relative
    // (spec gpos.md:679, W2's base). The empty set (pairValueCount=0) is the
    // adjudicated legal no-op (OQ-T4-05-3) and needs no special case.
    internal ReadStatus TryRead(int numGlyphs, ushort valueFormat1, ushort valueFormat2,
        PairPosReadState state, int setStart, out PairSetTable? model,
        out FontDiagnostic diagnostic, BuildBudget budget)
    {
        model = null;
        ReadStatus status = TryGetPairValueCount(out ushort pairValueCount, out diagnostic);
        if (status != ReadStatus.Done) return status;
        int stride = PairSetLayout.WordStride * (1 + BitOperations.PopCount((uint)valueFormat1)
            + BitOperations.PopCount((uint)valueFormat2));
        int rowsEnd = PairSetLayout.MinimumHeader + stride * pairValueCount;
        if (!budget.Work(rowsEnd + (long)pairValueCount, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        // Every present word is range-checked before any device traversal. When the
        // complete extent fits, the single pass below needs no pre-pass.
        if (rowsEnd > _data.Length)
        {
            for (int i = 0; i < pairValueCount; i++)
            {
                status = ReadRowWords(PairSetLayout.MinimumHeader + i * stride, i,
                    valueFormat1, valueFormat2, out _, out _, out _, out diagnostic);
                if (status != ReadStatus.Done) return status;
            }
        }
        PairValueRecord[]? records = null;
        if (state.Materialize)
        {
            if (!budget.Buffer(40L * pairValueCount, Location, out diagnostic)) return ReadStatus.ResourceLimit;
            records = new PairValueRecord[pairValueCount];
        }
        int consumed = rowsEnd;
        ushort previous = 0;
        for (int i = 0; i < pairValueCount; i++)
        {
            int rowPosition = PairSetLayout.MinimumHeader + i * stride;
            status = ReadRowWords(rowPosition, i, valueFormat1, valueFormat2,
                out ushort secondGlyph, out var words1, out var words2, out diagnostic);
            if (status != ReadStatus.Done) return status;
            if (secondGlyph >= numGlyphs)
                return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                    Location.AtLocal(DataField.GlyphId, (ulong)rowPosition, i),
                    out diagnostic, secondGlyph, (ulong)numGlyphs);
            if (i > 0 && secondGlyph <= previous)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    Location.AtLocal(DataField.GlyphId, (ulong)rowPosition, i),
                    out diagnostic, secondGlyph, previous);
            previous = secondGlyph;
            StaticDeviceTable? x1 = null, y1 = null, ax1 = null, ay1 = null;
            StaticDeviceTable? x2 = null, y2 = null, ax2 = null, ay2 = null;
            for (int slot = 4; slot < 8; slot++)
            {
                status = ReadSlotDevice(words1.OffsetAt(slot), slot, valueFormat1, rowPosition,
                    i, rowsEnd, setStart, state, ref consumed, out var device1, out diagnostic, budget);
                if (status != ReadStatus.Done) return status;
                switch (slot) { case 4: x1 = device1; break; case 5: y1 = device1; break; case 6: ax1 = device1; break; default: ay1 = device1; break; }
                status = ReadSlotDevice(words2.OffsetAt(slot), slot, valueFormat2, rowPosition,
                    i, rowsEnd, setStart, state, ref consumed, out var device2, out diagnostic, budget);
                if (status != ReadStatus.Done) return status;
                switch (slot) { case 4: x2 = device2; break; case 5: y2 = device2; break; case 6: ax2 = device2; break; default: ay2 = device2; break; }
            }
            if (records is not null)
                records[i] = new(secondGlyph,
                    WithDevices(words1.Value, x1, y1, ax1, ay1),
                    WithDevices(words2.Value, x2, y2, ax2, ay2));
        }
        if (state.Materialize) model = new(records ?? [], Location, consumed);
        diagnostic = default;
        return ReadStatus.Done;
    }

    private static StaticValueRecord WithDevices(StaticValueRecord value,
        StaticDeviceTable? xPlacement, StaticDeviceTable? yPlacement,
        StaticDeviceTable? xAdvance, StaticDeviceTable? yAdvance) =>
        new(value.XPlacement, value.YPlacement, value.XAdvance, value.YAdvance,
            xPlacement, yPlacement, xAdvance, yAdvance);

    private readonly record struct Words(StaticValueRecord Value, ushort DeviceX,
        ushort DeviceY, ushort DeviceAdvanceX, ushort DeviceAdvanceY)
    {
        internal ushort OffsetAt(int slot) => slot switch
        { 4 => DeviceX, 5 => DeviceY, 6 => DeviceAdvanceX, _ => DeviceAdvanceY };
    }

    private ReadStatus ReadRowWords(int position, int ordinal, ushort valueFormat1,
        ushort valueFormat2, out ushort secondGlyph, out Words words1, out Words words2,
        out FontDiagnostic diagnostic)
    {
        secondGlyph = 0;
        words1 = words2 = default;
        ReadStatus status = TryReadPairSecondGlyphAt((ulong)position, ordinal, out var glyph, out diagnostic);
        if (status != ReadStatus.Done) return status;
        secondGlyph = glyph.SecondGlyph;
        int at = position + PairSetLayout.WordStride;
        status = ReadWords(at, ordinal, valueFormat1, out words1, out diagnostic);
        if (status != ReadStatus.Done) return status;
        return ReadWords(at + PairSetLayout.WordStride * BitOperations.PopCount((uint)valueFormat1),
            ordinal, valueFormat2, out words2, out diagnostic);
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
            at += PairSetLayout.WordStride;
        }
        words = new(new(x, y, ax, ay), dx, dy, dax, day);
        diagnostic = default;
        return ReadStatus.Done;
    }

    private ReadStatus ReadSlotDevice(ushort offset, int slot, ushort mask, int rowPosition,
        int ordinal, int rowsEnd, int setStart, PairPosReadState state, ref int consumed,
        out StaticDeviceTable? device, out FontDiagnostic diagnostic, BuildBudget budget)
    {
        device = null;
        if ((mask & (1 << slot)) == 0 || offset == 0)
        {
            diagnostic = default;
            return ReadStatus.Done;
        }
        // The field coordinate includes the second-glyph word that precedes the record
        // words inside the row (rowPosition addresses the whole row's start).
        int field = rowPosition + PairSetLayout.WordStride
            + PairSetLayout.WordStride * BitOperations.PopCount((uint)(mask & ((1 << slot) - 1)));
        if (offset >= _data.Length)
            return ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.RangeOutsideSource,
                Location.AtLocal(DataField.Offset, (ulong)field, ordinal), out diagnostic, offset, (ulong)_data.Length);
        if (offset < rowsEnd)
            return ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.StorageBeforeRecords,
                Location.AtLocal(DataField.Offset, (ulong)field, ordinal), out diagnostic, offset, (ulong)rowsEnd);
        ReadStatus status = ReadDevice(setStart + offset, _data.Slice(offset), state,
            out device, out int length, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        if (offset + length > consumed) consumed = offset + length;
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
