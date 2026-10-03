using FontWeave.Core;

namespace FontWeave.Tables.Layout;

public static class StaticDeviceTableCompiler
{
    public static ReadStatus TryCreate(ushort startSize, ushort endSize, ushort deltaFormat,
        ReadOnlySpan<sbyte> deltas, out StaticDeviceTable? model, out FontDiagnostic diagnostic,
        BuildLimits? limits = null, DataLocation location = default) =>
        TryCreate(startSize, endSize, deltaFormat, deltas, out model, out diagnostic, new BuildBudget(limits), location);

    internal static ReadStatus TryCreate(ushort startSize, ushort endSize, ushort deltaFormat,
        ReadOnlySpan<sbyte> deltas, out StaticDeviceTable? model, out FontDiagnostic diagnostic,
        BuildBudget budget, DataLocation location = default)
    {
        model = null;
        ReadStatus status = StaticDeviceLayout.CheckHeader(startSize, endSize, deltaFormat, location,
            out int count, out int bits, out int consumed, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (deltas.Length != count)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                location.AtLocal(DataField.Count, StaticDeviceLayout.EndAt), out diagnostic,
                (ulong)deltas.Length, (ulong)count);
        if (!budget.Work(2L * count, location, out diagnostic)) return ReadStatus.ResourceLimit;
        int minimum = -(1 << (bits - 1)), maximum = (1 << (bits - 1)) - 1;
        int perWord = 16 / bits;
        for (int i = 0; i < count; i++)
            if (deltas[i] < minimum || deltas[i] > maximum)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    location.AtLocal(DataField.Payload, StaticDeviceLayout.HeaderLength +
                        (ulong)(i / perWord) * StaticDeviceLayout.WordStride, i),
                    out diagnostic, unchecked((ulong)(long)deltas[i]), (ulong)maximum);
        if (!budget.Buffer(count, location, out diagnostic)) return ReadStatus.ResourceLimit;
        model = new(startSize, endSize, deltaFormat, deltas.ToArray(), location, consumed);
        diagnostic = default;
        return ReadStatus.Done;
    }

    public static ReadStatus TryCompile(StaticDeviceTable model, out byte[]? output,
        out FontDiagnostic diagnostic, BuildLimits? limits = null) =>
        TryCompile(model, out output, out diagnostic, new BuildBudget(limits));

    internal static ReadStatus TryCompile(StaticDeviceTable model, out byte[]? output,
        out FontDiagnostic diagnostic, BuildBudget budget)
    {
        ArgumentNullException.ThrowIfNull(model);
        output = null;
        int length = Measure(model);
        if (!budget.Work(EmissionWork(model), model.Location, out diagnostic) ||
            !budget.Output(length, model.Location, out diagnostic) ||
            !budget.Buffer(length, model.Location, out diagnostic)) return ReadStatus.ResourceLimit;
        byte[] bytes = new byte[length];
        WritePlanned(model, bytes);
        budget.RecordCompiledTable(length);
        output = bytes;
        diagnostic = default;
        return ReadStatus.Done;
    }

    public static ReadStatus TryWrite(StaticDeviceTable model, Span<byte> destination,
        out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        int length = Measure(model);
        if (destination.Length < length)
            return ReadWire.Fail(ReadStatus.DestinationTooSmall, DiagnosticCode.DestinationTooSmall,
                model.Location.AtLocal(DataField.OutputBytes, 0), out diagnostic,
                (ulong)destination.Length, (ulong)length);
        var budget = new BuildBudget(limits);
        if (!budget.Work(EmissionWork(model), model.Location, out diagnostic) ||
            !budget.Output(length, model.Location, out diagnostic)) return ReadStatus.ResourceLimit;
        WritePlanned(model, destination);
        diagnostic = default;
        return ReadStatus.Done;
    }

    internal static int Measure(StaticDeviceTable model) => model.ConsumedLength;
    // Includes encoded bytes and all uncompressed values visited by the signed packer.
    internal static long EmissionWork(StaticDeviceTable model) => Measure(model) + (long)model.Deltas.Length;

    // Only immutable, validated models and fully preflighted destinations reach this path.
    internal static void WritePlanned(StaticDeviceTable model, Span<byte> destination)
    {
        CoverageTableCompiler.RequireWritten(StaticDeviceView.TryWriteStartSize(destination, model.StartSize, out _, model.Location));
        CoverageTableCompiler.RequireWritten(StaticDeviceView.TryWriteEndSize(destination, model.EndSize, out _, model.Location));
        CoverageTableCompiler.RequireWritten(StaticDeviceView.TryWriteDeltaFormat(destination, model.DeltaFormat, out _, model.Location));
        int bits = 1 << model.DeltaFormat, mask = (1 << bits) - 1, perWord = 16 / bits;
        int words = StaticDeviceLayout.WordCount(model.Deltas.Length, bits), at = 0;
        for (int i = 0; i < words; i++)
        {
            int packed = 0;
            for (int j = 0; j < perWord && at < model.Deltas.Length; j++)
                packed |= (model.Deltas[at++] & mask) << (16 - bits - j * bits);
            CoverageTableCompiler.RequireWritten(StaticDeviceView.TryWritePackedWordAt(destination,
                StaticDeviceLayout.HeaderLength + (ulong)i * StaticDeviceLayout.WordStride,
                new StaticDeviceView.PackedWordData(i, (ushort)packed), out _, model.Location));
        }
    }
}
