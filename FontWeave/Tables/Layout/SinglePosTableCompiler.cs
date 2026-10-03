using System.Numerics;
using FontWeave.Core;

namespace FontWeave.Tables.Layout;

public static partial class SinglePosTableCompiler
{
    public static ReadStatus TryCreate(ReadOnlySpan<ushort> glyphs, ReadOnlySpan<StaticValueRecord> values,
        int numGlyphs, ushort format, ushort valueFormat, out SinglePosTable? model,
        out FontDiagnostic diagnostic, BuildLimits? limits = null, DataLocation location = default,
        ushort coverageFormat = 1) => TryCreate(glyphs, values, numGlyphs, format, valueFormat,
            out model, out diagnostic, new BuildBudget(limits), location, coverageFormat);

    internal static ReadStatus TryCreate(ReadOnlySpan<ushort> glyphs, ReadOnlySpan<StaticValueRecord> values,
        int numGlyphs, ushort format, ushort valueFormat, out SinglePosTable? model,
        out FontDiagnostic diagnostic, BuildBudget budget, DataLocation location = default,
        ushort coverageFormat = 1)
    {
        model = null;
        ReadStatus status = CoverageTableCompiler.CheckNumGlyphs(numGlyphs, location, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (format is not (1 or 2))
            return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedVersion,
                location.AtLocal(DataField.Version, 0), out diagnostic, format);
        status = ValidateMask(valueFormat, location, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (glyphs.Length > ushort.MaxValue)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                location.AtLocal(DataField.Count, SinglePosLayout.CountField), out diagnostic,
                (ulong)glyphs.Length, ushort.MaxValue);
        int expected = format == 1 ? 1 : glyphs.Length;
        if (values.Length != expected)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                location.AtLocal(format == 1 ? DataField.Payload : DataField.Count,
                    (ulong)(format == 1 ? SinglePosLayout.Format1Rows : SinglePosLayout.CountField)),
                out diagnostic, (ulong)values.Length, (ulong)expected);
        if (!budget.Work(10L * values.Length + 1, location, out diagnostic)) return ReadStatus.ResourceLimit;
        for (int i = 0; i < values.Length; i++)
        {
            var value = values[i];
            for (int slot = 0; slot < 8; slot++)
            {
                bool present = (valueFormat & (1 << slot)) != 0;
                if (!present && (slot < 4 ? value.ScalarAt(slot) != 0 : value.DeviceAt(slot) is not null))
                    return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                        location.AtLocal(DataField.Flags, SinglePosLayout.MaskField, i),
                        out diagnostic, (ulong)(1 << slot), valueFormat);
                if (slot >= 4 && value.DeviceAt(slot) is StaticDeviceTable device &&
                    (device.Location.SourceId != location.SourceId || device.Location.FaceIndex != location.FaceIndex ||
                        location.SourceId != 0 && device.Location.TableTag != location.TableTag))
                    return ReadWire.Fail(ReadStatus.InvalidSource, DiagnosticCode.WrongSourceFace,
                        location.AtLocal(DataField.None, 0, i), out diagnostic);
            }
        }
        int header = format == 1 ? SinglePosLayout.Format1Rows : SinglePosLayout.Format2Rows;
        int bodyEnd = header + ValueStride(valueFormat) * values.Length;
        status = CoverageTableCompiler.TryCreate(glyphs, numGlyphs, coverageFormat, out var coverage,
            out diagnostic, budget, location.AtLocal(DataField.None, (ulong)bodyEnd));
        if (status != ReadStatus.Done) return status;
        if (!budget.Buffer(40L * values.Length, location, out diagnostic)) return ReadStatus.ResourceLimit;
        // Caller-created/edited models have no consumed source extent. Relocated
        // Coverage/Device size is established by the writer's concrete plan.
        model = new(coverage!, values.ToArray(), format, valueFormat, location, 0);
        diagnostic = default; return ReadStatus.Done;
    }

    internal static int ValueStride(ushort mask) => SinglePosLayout.WordStride * BitOperations.PopCount((uint)mask);

    internal static ReadStatus ValidateMask(ushort mask, DataLocation location, out FontDiagnostic diagnostic)
    {
        if ((mask & ~SinglePosLayout.SupportedMask) != 0)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                location.AtLocal(DataField.Flags, SinglePosLayout.MaskField), out diagnostic, mask, SinglePosLayout.SupportedMask);
        diagnostic = default; return ReadStatus.Done;
    }
}
