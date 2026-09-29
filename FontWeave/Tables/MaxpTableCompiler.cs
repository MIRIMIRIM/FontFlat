using System.Buffers.Binary;
using FontWeave.Core;

namespace FontWeave.Tables;

public static class MaxpTableCompiler
{
    public static ReadStatus TryCompile(MaxpTable model, out byte[]? output,
        out FontDiagnostic diagnostic, BuildLimits? limits = null) =>
        TryCompile(model, out output, out diagnostic, new BuildBudget(limits));

    internal static ReadStatus TryCompile(MaxpTable model, out byte[]? output,
        out FontDiagnostic diagnostic, BuildBudget budget)
    {
        ArgumentNullException.ThrowIfNull(model);
        output = null;
        int length = model is Maxp05Table ? MaxpLayout.Length05 : MaxpLayout.Length10;
        if (!budget.Work(length, model.Location, out diagnostic) ||
            !budget.Output(length, model.Location, out diagnostic) ||
            !budget.Buffer(length, model.Location, out diagnostic))
            return ReadStatus.ResourceLimit;
        byte[] bytes = new byte[length];
        Write(model, bytes);
        budget.RecordCompiledTable(length);
        output = bytes;
        diagnostic = default;
        return ReadStatus.Done;
    }

    public static ReadStatus TryWrite(MaxpTable model, Span<byte> destination,
        out FontDiagnostic diagnostic)
    {
        ArgumentNullException.ThrowIfNull(model);
        int length = model is Maxp05Table ? MaxpLayout.Length05 : MaxpLayout.Length10;
        if (destination.Length < length)
            return ReadWire.Fail(ReadStatus.DestinationTooSmall, DiagnosticCode.DestinationTooSmall,
                model.Location.At(DataField.OutputBytes, 0), out diagnostic,
                (ulong)destination.Length, (ulong)length);
        Write(model, destination);
        diagnostic = default;
        return ReadStatus.Done;
    }

    private static void Write(MaxpTable model, Span<byte> bytes)
    {
        BinaryPrimitives.WriteUInt32BigEndian(bytes.Slice(MaxpLayout.VersionAt), model.VersionRaw);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.Slice(MaxpLayout.NumGlyphsAt), model.NumGlyphs);
        if (model is not Maxp10Table v10) return;
        Maxp10Values value = v10.Values;
        BinaryPrimitives.WriteUInt16BigEndian(bytes.Slice(MaxpLayout.MaxPointsAt), value.MaxPoints);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.Slice(MaxpLayout.MaxContoursAt), value.MaxContours);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.Slice(MaxpLayout.MaxCompositePointsAt), value.MaxCompositePoints);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.Slice(MaxpLayout.MaxCompositeContoursAt), value.MaxCompositeContours);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.Slice(MaxpLayout.MaxZonesAt), value.MaxZones);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.Slice(MaxpLayout.MaxTwilightPointsAt), value.MaxTwilightPoints);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.Slice(MaxpLayout.MaxStorageAt), value.MaxStorage);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.Slice(MaxpLayout.MaxFunctionDefsAt), value.MaxFunctionDefs);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.Slice(MaxpLayout.MaxInstructionDefsAt), value.MaxInstructionDefs);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.Slice(MaxpLayout.MaxStackElementsAt), value.MaxStackElements);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.Slice(MaxpLayout.MaxSizeOfInstructionsAt), value.MaxSizeOfInstructions);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.Slice(MaxpLayout.MaxComponentElementsAt), value.MaxComponentElements);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.Slice(MaxpLayout.MaxComponentDepthAt), value.MaxComponentDepth);
    }
}
