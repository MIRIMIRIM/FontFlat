using FontWeave.Core;

namespace FontWeave.Tables.Layout;

public readonly ref partial struct StaticDeviceView
{
    // Contract-version-1 computed hooks (card 1.3-c). The generated accessors own the
    // version gate, the 0x8000 known-inactive refusal and the underflow diagnostics;
    // unpacking, tail-bit strictness and per-word semantics stay in this partial.

    private static ReadStatus DeltaValueCountOf(ushort startSize, ushort endSize,
        DataLocation diagnosticLocation, out ulong value, out FontDiagnostic diagnostic)
    {
        value = 0;
        if (endSize < startSize)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                diagnosticLocation, out diagnostic, endSize, startSize);
        value = (ulong)endSize - startSize + 1; // Inclusive UInt16 bounds span up to 65536 values.
        diagnostic = default;
        return ReadStatus.Done;
    }

    private static ReadStatus PackedWordCountOf(ulong deltaValueCount, ushort deltaFormat,
        DataLocation diagnosticLocation, out ulong value, out FontDiagnostic diagnostic)
    {
        value = 0;
        // The conditional activation domain already restricts the generated path to
        // formats 1..3; the refusal only guards direct calls of this private hook.
        int bits = deltaFormat switch
        {
            1 => 2, 2 => 4, 3 => 8,
            _ => -1
        };
        if (bits < 0)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                diagnosticLocation, out diagnostic, deltaFormat, 3);
        value = (deltaValueCount * (ulong)bits + 15) / 16;
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>Reads one declared packed word without validating unused tail bits.</summary>
    public ReadStatus TryGetPackedWord(int ordinal, out ushort word, out FontDiagnostic diagnostic)
    {
        word = 0;
        ReadStatus status = TryGetWord(ordinal, out var record, out diagnostic);
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
        return status == ReadStatus.Done
            ? Materialize(count, bits, consumed, lastWord, out model, out diagnostic, budget) : status;
    }

    // Expansion over already inspected facts (spec 6.2): InspectWords owns every check,
    // so this step re-walks the same proven packed words only to unpack deltas and must
    // not re-inspect. The separate entry lets a parent that just inspected (SinglePos
    // ReadDevice, M1b handoff) consume its facts without paying them twice.
    internal ReadStatus Materialize(int count, int bits, int consumed, ushort lastWord,
        out StaticDeviceTable? model, out FontDiagnostic diagnostic, BuildBudget budget)
    {
        model = null;
        if (!budget.Work(count, Location, out diagnostic) ||
            !budget.Buffer(count, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        if (count == 0)
        {
            // The VariationDevice shape (0x8000): no packed words exist to unpack.
            model = new(StartSize, EndSize, DeltaFormat, [], Location, consumed);
            diagnostic = default;
            return ReadStatus.Done;
        }
        var deltas = new sbyte[count];
        int words = StaticDeviceLayout.WordCount(count, bits), perWord = 16 / bits;
        int mask = (1 << bits) - 1, sign = 1 << (bits - 1), at = 0;
        for (int i = 0; i < words; i++)
        {
            ushort word = lastWord;
            if (i != words - 1)
            {
                ReadStatus status = TryGetWord(i, out var record, out diagnostic);
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

    // The single strict pass over the packed device words; parents that only validate
    // use the Inspect projection, parents that materialize reuse its facts.
    internal ReadStatus InspectWords(out int count, out int bits, out int consumed, out ushort lastWord,
        out FontDiagnostic diagnostic, BuildBudget budget)
    {
        count = bits = 0;
        consumed = 0;
        lastWord = 0;
        // The 0x8000 VariationDevice shape activates here (T4-06 section 3.4): the
        // format word reads before the computed chain so the known-inactive refusal
        // it would emit for 0x8000 is replaced by the six-byte header form - the
        // start/end words carry the outer/inner delta-set indices (indices, so no
        // end<start ordering applies) and no packed words exist. Every other version
        // walks the generated chain whose version gate keeps the unknown-format
        // UnsupportedVersion refusal and the DeltaValueCount hook the end<start
        // underflow refusal, both byte-identical to the pre-activation order.
        ReadStatus status = TryGetDeltaFormat(out ushort format, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (format == 0x8000)
        {
            consumed = StaticDeviceLayout.HeaderLength;
            if (!budget.Work(consumed, Location, out diagnostic)) return ReadStatus.ResourceLimit;
            diagnostic = default;
            return ReadStatus.Done;
        }
        // The generated computed chain runs the version gate and the end<start
        // underflow refusal before any charging, matching the former CheckHeader
        // order at the same diagnostic locations.
        status = TryGetDeltaValueCount(out ulong deltaCount, out diagnostic);
        if (status != ReadStatus.Done) return status;
        count = (int)deltaCount;
        bits = 1 << format;
        status = TryGetWordExtent(out _, out ulong wordCount, out ulong end, out diagnostic);
        if (status != ReadStatus.Done) return status;
        consumed = (int)end;
        if (!budget.Work(consumed, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        if (_data.Length < consumed)
            // The first incomplete word and its field provenance come from the generated
            // require-extent contract (spec 5 rule 3).
            return TryRequireWordExtent(out diagnostic);
        int last = (int)wordCount - 1;
        status = TryGetWord(last, out var tail, out diagnostic);
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
