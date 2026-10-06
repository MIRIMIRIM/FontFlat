using FontWeave.Core;

namespace FontWeave.Tables;

public static class PcltTableCompiler
{
    public static ReadStatus TryCompile(PcltTable model, out byte[]? output,
        out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        output = null;
        var budget = new BuildBudget(limits);
        if (!budget.Work(PcltLayout.Length, model.Location, out diagnostic) ||
            !budget.Output(PcltLayout.Length, model.Location, out diagnostic) ||
            !budget.Buffer(PcltLayout.Length, model.Location, out diagnostic))
            return ReadStatus.ResourceLimit;
        byte[] bytes = new byte[PcltLayout.Length];
        Write(model.Values, model.Location, bytes);
        budget.RecordCompiledTable(bytes.Length);
        output = bytes;
        diagnostic = default;
        return ReadStatus.Done;
    }

    public static ReadStatus TryWrite(PcltTable model, Span<byte> destination,
        out FontDiagnostic diagnostic)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (destination.Length < PcltLayout.Length)
            return ReadWire.Fail(ReadStatus.DestinationTooSmall, DiagnosticCode.DestinationTooSmall,
                model.Location.At(DataField.OutputBytes, 0), out diagnostic,
                (ulong)destination.Length, PcltLayout.Length);
        Write(model.Values, model.Location, destination.Slice(0, PcltLayout.Length));
        diagnostic = default;
        return ReadStatus.Done;
    }

    private static void Write(PcltValues v, DataLocation location, Span<byte> bytes)
    {
        // After the capacity preflight these generated writers cannot fail; a failure
        // is a writer contract bug and surfaces loudly. The 64-bit character
        // complement splits into the two declared big-endian dwords at 36/40, which is
        // byte-identical to the single 8-byte write it replaces. The Typeface and
        // FileName ASCII narrowing stays a byte loop: sixteen/six generated U8 writer
        // calls would be churn without a structural win (OQ-M2a-2 keep-churn).
        RequireWritten(PcltView.TryWriteVersionRaw(bytes, PcltLayout.Version, out _, location));
        RequireWritten(PcltView.TryWriteFontNumber(bytes, v.FontNumber, out _, location));
        RequireWritten(PcltView.TryWritePitch(bytes, v.Pitch, out _, location));
        RequireWritten(PcltView.TryWriteXHeight(bytes, v.XHeight, out _, location));
        RequireWritten(PcltView.TryWriteStyle(bytes, v.Style, out _, location));
        RequireWritten(PcltView.TryWriteTypeFamily(bytes, v.TypeFamily, out _, location));
        RequireWritten(PcltView.TryWriteCapHeight(bytes, v.CapHeight, out _, location));
        RequireWritten(PcltView.TryWriteSymbolSet(bytes, v.SymbolSet, out _, location));
        for (int i = 0; i < 16; i++) bytes[20 + i] = (byte)v.Typeface[i];
        RequireWritten(PcltView.TryWriteCharacterComplementHigh(bytes,
            unchecked((uint)(v.CharacterComplement >> 32)), out _, location));
        RequireWritten(PcltView.TryWriteCharacterComplementLow(bytes,
            unchecked((uint)v.CharacterComplement), out _, location));
        for (int i = 0; i < 6; i++) bytes[44 + i] = (byte)v.FileName[i];
        RequireWritten(PcltView.TryWriteStrokeWeight(bytes, v.StrokeWeight, out _, location));
        RequireWritten(PcltView.TryWriteWidthType(bytes, v.WidthType, out _, location));
        RequireWritten(PcltView.TryWriteSerifStyle(bytes, v.SerifStyle, out _, location));
        RequireWritten(PcltView.TryWriteReserved(bytes, 0, out _, location));
    }

    // Same contract as the meta emission path: a preflighted write failing is a
    // mechanical bug, not a caller-facing outcome; it must never be silently discarded.
    private static void RequireWritten(ReadStatus status)
    {
        if (status != ReadStatus.Done) throw new InvalidOperationException("A preflighted pclt write failed.");
    }
}
