using FontWeave.Core;

namespace FontWeave.Tables;

/// <summary>Legacy N2 edit surface; changes only the two named fields.</summary>
public readonly record struct HeadMetadataEdit(int FontRevisionRaw, long ModifiedSeconds);

public static class HeadTableCompiler
{
    public static ReadStatus TryCompile(HeadTable model, out byte[]? output,
        out FontDiagnostic diagnostic, BuildLimits? limits = null) =>
        TryCompile(model, out output, out diagnostic, new BuildBudget(limits));

    internal static ReadStatus TryCompile(HeadTable model, out byte[]? output,
        out FontDiagnostic diagnostic, BuildBudget budget)
    {
        ArgumentNullException.ThrowIfNull(model);
        output = null;
        DataLocation location = model.Location;
        if (!budget.Work(HeadLayout.Length, location, out diagnostic) ||
            !budget.Output(HeadLayout.Length, location, out diagnostic) ||
            !budget.Buffer(HeadLayout.Length, location, out diagnostic))
            return ReadStatus.ResourceLimit;
        byte[] bytes = new byte[HeadLayout.Length];
        ReadStatus status = Write(model.Values, bytes, out diagnostic, model.Location);
        if (status != ReadStatus.Done) return status;
        budget.RecordCompiledTable(HeadLayout.Length);
        output = bytes;
        diagnostic = default;
        return ReadStatus.Done;
    }

    public static ReadStatus TryWrite(HeadTable model, Span<byte> destination,
        out FontDiagnostic diagnostic)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (destination.Length < HeadLayout.Length)
            return ReadWire.Fail(ReadStatus.DestinationTooSmall, DiagnosticCode.DestinationTooSmall,
                model.Location.At(DataField.OutputBytes, 0), out diagnostic,
                (ulong)destination.Length, HeadLayout.Length);
        return Write(model.Values, destination, out diagnostic, model.Location);
    }

    public static ReadStatus TryCompile(ReadOnlySpan<byte> input, HeadMetadataEdit edit,
        out byte[]? output, out FontDiagnostic diagnostic, BuildLimits? limits = null,
        DataLocation location = default) =>
        CompileLegacy(input, edit, out output, out diagnostic,
            new BuildBudget(limits), location, true);

    internal static ReadStatus TryCompile(ReadOnlySpan<byte> input, HeadMetadataEdit edit,
        out byte[]? output, out FontDiagnostic diagnostic, BuildBudget budget,
        DataLocation location = default) =>
        CompileLegacy(input, edit, out output, out diagnostic, budget, location, false);

    private static ReadStatus CompileLegacy(ReadOnlySpan<byte> input, HeadMetadataEdit edit,
        out byte[]? output, out FontDiagnostic diagnostic, BuildBudget budget,
        DataLocation location, bool chargeInput)
    {
        output = null;
        location = location == default ? DataLocation.Anonymous(HeadLayout.Tag) : location;
        if (chargeInput && !budget.Input(input.Length, location, out diagnostic))
            return ReadStatus.ResourceLimit;
        ReadStatus status = HeadView.TryOpen(input, out var view, out diagnostic, location);
        if (status != ReadStatus.Done) return status;
        status = view.TryRead(out HeadTable? original, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        var values = original!.Values with
        { FontRevisionRaw = edit.FontRevisionRaw, ModifiedSeconds = edit.ModifiedSeconds };
        status = original.TryWith(values, out HeadTable? updated, out diagnostic);
        if (status != ReadStatus.Done) return status;
        return TryCompile(updated!, out output, out diagnostic, budget);
    }

    private static ReadStatus Write(HeadTableValues value, Span<byte> bytes,
        out FontDiagnostic diagnostic, DataLocation location)
    {
        ReadStatus status = HeadView.TryWriteVersion(bytes, HeadLayout.Version, out diagnostic, location);
        if (status != ReadStatus.Done) return status;
        status = HeadView.TryWriteFontRevision(bytes, value.FontRevisionRaw, out diagnostic, location);
        if (status != ReadStatus.Done) return status;
        status = HeadView.TryWriteChecksumAdjustment(bytes, value.ChecksumAdjustment, out diagnostic, location);
        if (status != ReadStatus.Done) return status;
        status = HeadView.TryWriteMagicNumber(bytes, value.MagicNumber, out diagnostic, location);
        if (status != ReadStatus.Done) return status;
        status = HeadView.TryWriteFlags(bytes, value.Flags, out diagnostic, location);
        if (status != ReadStatus.Done) return status;
        status = HeadView.TryWriteUnitsPerEm(bytes, value.UnitsPerEm, out diagnostic, location);
        if (status != ReadStatus.Done) return status;
        status = HeadView.TryWriteCreated(bytes, value.CreatedSeconds, out diagnostic, location);
        if (status != ReadStatus.Done) return status;
        status = HeadView.TryWriteModified(bytes, value.ModifiedSeconds, out diagnostic, location);
        if (status != ReadStatus.Done) return status;
        status = HeadView.TryWriteXMin(bytes, value.XMin, out diagnostic, location);
        if (status != ReadStatus.Done) return status;
        status = HeadView.TryWriteYMin(bytes, value.YMin, out diagnostic, location);
        if (status != ReadStatus.Done) return status;
        status = HeadView.TryWriteXMax(bytes, value.XMax, out diagnostic, location);
        if (status != ReadStatus.Done) return status;
        status = HeadView.TryWriteYMax(bytes, value.YMax, out diagnostic, location);
        if (status != ReadStatus.Done) return status;
        status = HeadView.TryWriteMacStyle(bytes, value.MacStyle, out diagnostic, location);
        if (status != ReadStatus.Done) return status;
        status = HeadView.TryWriteLowestRecPpem(bytes, value.LowestRecPpem, out diagnostic, location);
        if (status != ReadStatus.Done) return status;
        status = HeadView.TryWriteFontDirectionHint(bytes, value.FontDirectionHint, out diagnostic, location);
        if (status != ReadStatus.Done) return status;
        status = HeadView.TryWriteIndexToLocFormat(bytes, value.IndexToLocFormat, out diagnostic, location);
        if (status != ReadStatus.Done) return status;
        status = HeadView.TryWriteGlyphDataFormat(bytes, value.GlyphDataFormat, out diagnostic, location);
        if (status != ReadStatus.Done) return status;
        return ReadStatus.Done;
    }
}
