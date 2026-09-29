using System.Buffers.Binary;
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
        Write(model.Values, bytes);
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
        Write(model.Values, destination);
        diagnostic = default;
        return ReadStatus.Done;
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

    private static void Write(HeadTableValues value, Span<byte> bytes)
    {
        BinaryPrimitives.WriteUInt32BigEndian(bytes.Slice(HeadLayout.VersionAt), HeadLayout.Version);
        BinaryPrimitives.WriteInt32BigEndian(bytes.Slice(HeadLayout.FontRevisionAt), value.FontRevisionRaw);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.Slice(HeadLayout.ChecksumAdjustmentAt), value.ChecksumAdjustment);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.Slice(HeadLayout.MagicNumberAt), value.MagicNumber);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.Slice(HeadLayout.FlagsAt), value.Flags);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.Slice(HeadLayout.UnitsPerEmAt), value.UnitsPerEm);
        BinaryPrimitives.WriteInt64BigEndian(bytes.Slice(HeadLayout.CreatedAt), value.CreatedSeconds);
        BinaryPrimitives.WriteInt64BigEndian(bytes.Slice(HeadLayout.ModifiedAt), value.ModifiedSeconds);
        BinaryPrimitives.WriteInt16BigEndian(bytes.Slice(HeadLayout.XMinAt), value.XMin);
        BinaryPrimitives.WriteInt16BigEndian(bytes.Slice(HeadLayout.YMinAt), value.YMin);
        BinaryPrimitives.WriteInt16BigEndian(bytes.Slice(HeadLayout.XMaxAt), value.XMax);
        BinaryPrimitives.WriteInt16BigEndian(bytes.Slice(HeadLayout.YMaxAt), value.YMax);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.Slice(HeadLayout.MacStyleAt), value.MacStyle);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.Slice(HeadLayout.LowestRecPpemAt), value.LowestRecPpem);
        BinaryPrimitives.WriteInt16BigEndian(bytes.Slice(HeadLayout.FontDirectionHintAt), value.FontDirectionHint);
        BinaryPrimitives.WriteInt16BigEndian(bytes.Slice(HeadLayout.IndexToLocFormatAt), value.IndexToLocFormat);
        BinaryPrimitives.WriteInt16BigEndian(bytes.Slice(HeadLayout.GlyphDataFormatAt), value.GlyphDataFormat);
    }
}
