using FontWeave.Core;

namespace FontWeave.Tables.Layout;

/// <summary>The BaseCoord leaf writer (formats 1/2/3, T4-07 section 4.1). The model
/// factory carries the read side's index-domain contract (fmt2's referenceGlyph
/// against numGlyphs, OQ-5) and the format-slot matrix (a Device only under format 3,
/// the fmt2 words only under format 2). Canonical emission places the fmt3 Device
/// inline directly after the six-byte body, so one coord block is one placement step
/// of its true body length (the T4-05 MeasureAnchorBody lesson); a null Device
/// encodes the present-null offset 0 (Anchor fmt3's W6 shape).</summary>
public static class BaseCoordTableCompiler
{
    public static ReadStatus TryCreate(ushort format, short coordinate, ushort referenceGlyph,
        ushort baseCoordPoint, StaticDeviceTable? device, int numGlyphs, out BaseCoordTable? model,
        out FontDiagnostic diagnostic, BuildLimits? limits = null, DataLocation location = default) =>
        TryCreate(format, coordinate, referenceGlyph, baseCoordPoint, device, numGlyphs,
            out model, out diagnostic, new BuildBudget(limits), location);

    internal static ReadStatus TryCreate(ushort format, short coordinate, ushort referenceGlyph,
        ushort baseCoordPoint, StaticDeviceTable? device, int numGlyphs, out BaseCoordTable? model,
        out FontDiagnostic diagnostic, BuildBudget budget, DataLocation location = default)
    {
        model = null;
        ReadStatus status = CoverageTableCompiler.CheckNumGlyphs(numGlyphs, location, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (format is not (1 or 2 or 3))
            return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedVersion,
                location.AtLocal(DataField.Version, 0), out diagnostic, format);
        if (format == 2 && referenceGlyph >= numGlyphs)
            return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                location.AtLocal(DataField.Payload, BaseCoordLayout.Format2HeaderField),
                out diagnostic, referenceGlyph, (ulong)numGlyphs);
        if (format != 2 && (referenceGlyph != 0 || baseCoordPoint != 0))
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                location.AtLocal(DataField.Payload, BaseCoordLayout.Format2HeaderField),
                out diagnostic, referenceGlyph, 0ul);
        if (format != 3 && device is not null)
            return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
                location.AtLocal(DataField.Offset, BaseCoordLayout.Format3HeaderField), out diagnostic);
        if (!budget.Work(Measure(new(format, coordinate, referenceGlyph, baseCoordPoint, device,
            location, 0)), location, out diagnostic)) return ReadStatus.ResourceLimit;
        model = new(format, coordinate, referenceGlyph, baseCoordPoint, device, location,
            BodyLength(format) + (device is null ? 0 : StaticDeviceTableCompiler.Measure(device)));
        diagnostic = default;
        return ReadStatus.Done;
    }

    // The declared body length alone - devices are separate leaves in the plan, so
    // placement advances by this, never by the composite Measure.
    internal static int BodyLength(ushort format) => format switch
    {
        2 => BaseCoordLayout.MinimumHeader + 4,
        3 => BaseCoordLayout.Format3Length,
        _ => BaseCoordLayout.MinimumHeader,
    };

    // Composite leaf length: the body plus its inline Device (canonical placement).
    internal static int Measure(BaseCoordTable model) =>
        BodyLength(model.Format) + (model.Device is null ? 0 : StaticDeviceTableCompiler.Measure(model.Device));

    public static ReadStatus TryCompile(BaseCoordTable model, out byte[]? output,
        out FontDiagnostic diagnostic, BuildLimits? limits = null) =>
        TryCompile(model, out output, out diagnostic, new BuildBudget(limits));

    internal static ReadStatus TryCompile(BaseCoordTable model, out byte[]? output,
        out FontDiagnostic diagnostic, BuildBudget budget)
    {
        ArgumentNullException.ThrowIfNull(model);
        output = null;
        int length = Measure(model);
        if (!budget.Work(length, model.Location, out diagnostic) ||
            !budget.Output(length, model.Location, out diagnostic) ||
            !budget.Buffer(length, model.Location, out diagnostic)) return ReadStatus.ResourceLimit;
        byte[] bytes = new byte[length];
        WritePlanned(model, bytes);
        budget.RecordCompiledTable(bytes.Length);
        output = bytes;
        diagnostic = default;
        return ReadStatus.Done;
    }

    public static ReadStatus TryWrite(BaseCoordTable model, Span<byte> destination,
        out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        int length = Measure(model);
        var budget = new BuildBudget(limits);
        if (!budget.Work(length, model.Location, out diagnostic) ||
            !budget.Output(length, model.Location, out diagnostic)) return ReadStatus.ResourceLimit;
        if (destination.Length < length)
            return ReadWire.Fail(ReadStatus.DestinationTooSmall, DiagnosticCode.DestinationTooSmall,
                model.Location.AtLocal(DataField.OutputBytes, 0), out diagnostic,
                (ulong)destination.Length, (ulong)length);
        WritePlanned(model, destination);
        diagnostic = default;
        return ReadStatus.Done;
    }

    // Only an immutable, validated model and a fully preflighted destination reach
    // this path; a refusal here is an invariant breach.
    internal static void WritePlanned(BaseCoordTable model, Span<byte> destination)
    {
        var location = model.Location;
        CoverageTableCompiler.RequireWritten(BaseCoordView.TryWriteBaseCoordFormat(destination,
            model.Format, out _, location));
        CoverageTableCompiler.RequireWritten(BaseCoordView.TryWriteCoordinate(destination,
            model.Coordinate, out _, location));
        if (model.Format == 2)
        {
            CoverageTableCompiler.RequireWritten(BaseCoordView.TryWriteFormat2HeaderAt(destination,
                BaseCoordLayout.Format2HeaderField,
                new BaseCoordView.Format2HeaderData(-1, model.ReferenceGlyph, model.BaseCoordPoint),
                out _, location));
        }
        if (model.Format == 3)
        {
            // Inline placement: the present Device sits directly after the body; a
            // null model encodes the present-null offset 0.
            ushort deviceOffset = model.Device is StaticDeviceTable
                ? (ushort)BodyLength(model.Format) : (ushort)0;
            CoverageTableCompiler.RequireWritten(BaseCoordView.TryWriteFormat3HeaderAt(destination,
                BaseCoordLayout.Format3HeaderField,
                new BaseCoordView.Format3HeaderData(-1, deviceOffset), out _, location));
            if (model.Device is StaticDeviceTable device)
                StaticDeviceTableCompiler.WritePlanned(device,
                    destination.Slice(BodyLength(model.Format)));
        }
    }
}
