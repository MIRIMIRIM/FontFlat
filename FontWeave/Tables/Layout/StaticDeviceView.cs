using FontWeave.Core;

namespace FontWeave.Tables.Layout;

public readonly ref partial struct StaticDeviceView
{
    /// <summary>Reads one declared packed word without validating unused tail bits.</summary>
    public ReadStatus TryGetPackedWord(int ordinal, out ushort word, out FontDiagnostic diagnostic)
    {
        word = 0;
        ReadStatus status = TryGetDeltaFormat(out ushort format, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = StaticDeviceLayout.CheckHeader(StartSize, EndSize, format, Location,
            out int count, out int bits, out _, out diagnostic);
        if (status != ReadStatus.Done) return status;
        int words = StaticDeviceLayout.WordCount(count, bits);
        if (ordinal < 0 || ordinal >= words)
            return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                Location.AtLocal(DataField.Count, StaticDeviceLayout.EndAt, ordinal),
                out diagnostic, unchecked((ulong)(long)ordinal), (ulong)words);
        status = TryReadPackedWordAt(StaticDeviceLayout.HeaderLength +
            (ulong)ordinal * StaticDeviceLayout.WordStride, ordinal, out var record, out diagnostic);
        if (status == ReadStatus.Done) word = record.Value;
        return status;
    }

    public ReadStatus Validate(out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        var budget = new BuildBudget(limits);
        if (!budget.Input(_data.Length, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        return Validate(out diagnostic, budget);
    }

    internal ReadStatus Validate(out FontDiagnostic diagnostic, BuildBudget budget) =>
        Inspect(out _, out _, out diagnostic, budget);

    // Parents use this same structural owner for pure validation and leaf consumption.
    internal ReadStatus Inspect(out int count, out int consumed, out FontDiagnostic diagnostic, BuildBudget budget) =>
        InspectWords(out count, out _, out consumed, out _, out diagnostic, budget);

    public ReadStatus TryRead(out StaticDeviceTable? model, out FontDiagnostic diagnostic,
        BuildLimits? limits = null)
    {
        model = null;
        var budget = new BuildBudget(limits);
        if (!budget.Input(_data.Length, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        return TryRead(out model, out diagnostic, budget);
    }

    internal ReadStatus TryRead(out StaticDeviceTable? model, out FontDiagnostic diagnostic, BuildBudget budget)
    {
        model = null;
        ReadStatus status = InspectWords(out int count, out int bits, out int consumed,
            out ushort lastWord, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        if (!budget.Work(count, Location, out diagnostic) ||
            !budget.Buffer(count, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        var deltas = new sbyte[count];
        int words = StaticDeviceLayout.WordCount(count, bits), perWord = 16 / bits;
        int mask = (1 << bits) - 1, sign = 1 << (bits - 1), at = 0;
        for (int i = 0; i < words; i++)
        {
            ushort word = lastWord;
            if (i != words - 1)
            {
                status = TryReadPackedWordAt(StaticDeviceLayout.HeaderLength +
                    (ulong)i * StaticDeviceLayout.WordStride, i, out var record, out diagnostic);
                if (status != ReadStatus.Done) return status;
                word = record.Value;
            }
            for (int j = 0; j < perWord && at < count; j++)
            {
                int raw = (word >> (16 - bits - j * bits)) & mask;
                deltas[at++] = (sbyte)(raw >= sign ? raw - (1 << bits) : raw);
            }
        }
        model = new(StartSize, EndSize, DeltaFormat, deltas, Location, consumed);
        diagnostic = default;
        return ReadStatus.Done;
    }

    private ReadStatus InspectWords(out int count, out int bits, out int consumed, out ushort lastWord,
        out FontDiagnostic diagnostic, BuildBudget budget)
    {
        count = bits = consumed = 0;
        lastWord = 0;
        ReadStatus status = TryGetDeltaFormat(out ushort format, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = StaticDeviceLayout.CheckHeader(StartSize, EndSize, format, Location,
            out count, out bits, out consumed, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (!budget.Work(consumed, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        int words = StaticDeviceLayout.WordCount(count, bits);
        if (_data.Length < consumed)
        {
            int firstIncomplete = (_data.Length - StaticDeviceLayout.HeaderLength) / StaticDeviceLayout.WordStride;
            return TryReadPackedWordAt(StaticDeviceLayout.HeaderLength +
                (ulong)firstIncomplete * StaticDeviceLayout.WordStride, firstIncomplete, out _, out diagnostic);
        }
        int last = words - 1;
        status = TryReadPackedWordAt(StaticDeviceLayout.HeaderLength +
            (ulong)last * StaticDeviceLayout.WordStride, last, out var tail, out diagnostic);
        if (status != ReadStatus.Done) return status;
        lastWord = tail.Value;
        int used = count * bits % 16;
        int unusedMask = used == 0 ? 0 : (1 << (16 - used)) - 1;
        if ((lastWord & unusedMask) != 0)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.AtLocal(DataField.Payload, StaticDeviceLayout.HeaderLength +
                    (ulong)last * StaticDeviceLayout.WordStride, last), out diagnostic,
                (ulong)(lastWord & unusedMask), 0);
        diagnostic = default;
        return ReadStatus.Done;
    }
}
