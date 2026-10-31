using System.Buffers.Binary;
using FontWeave.Core;

namespace FontWeave.Tables;

public readonly record struct Os2MetadataEdit(ushort? WeightClass = null, ushort? WidthClass = null);

/// <summary>Writes only the original supported OS/2 profile; short v0 is intentionally read-only.</summary>
public static class Os2TableCompiler
{
    public static ReadStatus TryCompile(Os2Table model, out byte[]? output,
        out FontDiagnostic diagnostic, BuildLimits? limits = null) =>
        TryCompile(model, out output, out diagnostic, new BuildBudget(limits));

    internal static ReadStatus TryCompile(Os2Table model, out byte[]? output,
        out FontDiagnostic diagnostic, BuildBudget budget)
    {
        ArgumentNullException.ThrowIfNull(model);
        output = null;
        ReadStatus status = CheckWritable(model, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (!budget.Work(model.Length, model.Location, out diagnostic) ||
            !budget.Output(model.Length, model.Location, out diagnostic) ||
            !budget.Buffer(model.Length, model.Location, out diagnostic))
            return ReadStatus.ResourceLimit;
        byte[] bytes = new byte[model.Length];
        Write(model, bytes);
        budget.RecordCompiledTable(model.Length);
        output = bytes;
        diagnostic = default;
        return ReadStatus.Done;
    }

    public static ReadStatus TryWrite(Os2Table model, Span<byte> destination,
        out FontDiagnostic diagnostic)
    {
        ArgumentNullException.ThrowIfNull(model);
        ReadStatus status = CheckWritable(model, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (destination.Length < model.Length)
            return ReadWire.Fail(ReadStatus.DestinationTooSmall,
                DiagnosticCode.DestinationTooSmall,
                model.Location.At(DataField.OutputBytes, 0), out diagnostic,
                (ulong)destination.Length, (ulong)model.Length);
        Write(model, destination);
        diagnostic = default;
        return ReadStatus.Done;
    }

    public static ReadStatus TryCompile(ReadOnlySpan<byte> input, Os2MetadataEdit edit,
        out byte[]? output, out FontDiagnostic diagnostic, BuildLimits? limits = null,
        DataLocation location = default, bool tolerateLegacyDialects = false) =>
        CompileEdit(input, edit, out output, out diagnostic, new BuildBudget(limits),
            location, true, tolerateLegacyDialects);

    internal static ReadStatus TryCompile(ReadOnlySpan<byte> input, Os2MetadataEdit edit,
        out byte[]? output, out FontDiagnostic diagnostic, BuildBudget budget,
        DataLocation location = default, bool tolerateLegacyDialects = false) =>
        CompileEdit(input, edit, out output, out diagnostic, budget, location, false,
            tolerateLegacyDialects);

    private static ReadStatus CompileEdit(ReadOnlySpan<byte> input, Os2MetadataEdit edit,
        out byte[]? output, out FontDiagnostic diagnostic, BuildBudget budget,
        DataLocation location, bool chargeInput, bool tolerateLegacyDialects)
    {
        output = null;
        location = location == default ? DataLocation.Anonymous(Os2Layout.Tag) : location;
        if (chargeInput && !budget.Input(input.Length, location, out diagnostic))
            return ReadStatus.ResourceLimit;
        ReadStatus status = Os2View.TryOpen(input, out Os2View view, out diagnostic, location);
        if (status != ReadStatus.Done) return status;
        status = view.TryRead(tolerateLegacyDialects, findings: null,
            out Os2Table? original, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        if (original!.Length == Os2Layout.ShortLength)
            return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
                location.At(DataField.Length, 0), out diagnostic, Os2Layout.ShortLength);
        Os2TableValues old = original.Values;
        if ((edit.WeightClass is null && edit.WidthClass is null) ||
            (edit.WeightClass is null || edit.WeightClass == old.WeightClass) &&
            (edit.WidthClass is null || edit.WidthClass == old.WidthClass))
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                location.At(DataField.Payload, 4), out diagnostic);
        Os2TableValues replacement = old with
        {
            WeightClass = edit.WeightClass ?? old.WeightClass,
            WidthClass = edit.WidthClass ?? old.WidthClass
        };
        status = original.TryWith(replacement, out Os2Table? updated, out diagnostic);
        if (status != ReadStatus.Done) return status;
        return TryCompile(updated!, out output, out diagnostic, budget);
    }

    private static ReadStatus CheckWritable(Os2Table model, out FontDiagnostic diagnostic)
    {
        if (model.Length == Os2Layout.ShortLength)
            return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
                model.Location.At(DataField.Length, 0), out diagnostic, Os2Layout.ShortLength);
        return Os2View.ValidateValues(model.Version, model.Length, model.Values,
            model.Location, model.TolerateLegacyDialects, findings: null, out diagnostic);
    }

    private static void Write(Os2Table model, Span<byte> bytes)
    {
        Os2TableValues v = model.Values;
        static void U16(Span<byte> b, int at, ushort value) => BinaryPrimitives.WriteUInt16BigEndian(b.Slice(at), value);
        static void S16(Span<byte> b, int at, short value) => BinaryPrimitives.WriteInt16BigEndian(b.Slice(at), value);
        static void U32(Span<byte> b, int at, uint value) => BinaryPrimitives.WriteUInt32BigEndian(b.Slice(at), value);
        U16(bytes, 0, model.Version);
        S16(bytes, 2, v.XAvgCharWidth);
        U16(bytes, 4, v.WeightClass);
        U16(bytes, 6, v.WidthClass);
        U16(bytes, 8, v.FsType);
        S16(bytes, 10, v.YSubscriptXSize);
        S16(bytes, 12, v.YSubscriptYSize);
        S16(bytes, 14, v.YSubscriptXOffset);
        S16(bytes, 16, v.YSubscriptYOffset);
        S16(bytes, 18, v.YSuperscriptXSize);
        S16(bytes, 20, v.YSuperscriptYSize);
        S16(bytes, 22, v.YSuperscriptXOffset);
        S16(bytes, 24, v.YSuperscriptYOffset);
        S16(bytes, 26, v.YStrikeoutSize);
        S16(bytes, 28, v.YStrikeoutPosition);
        S16(bytes, 30, v.SFamilyClass);
        bytes[32] = v.Panose0; bytes[33] = v.Panose1;
        bytes[34] = v.Panose2; bytes[35] = v.Panose3;
        bytes[36] = v.Panose4; bytes[37] = v.Panose5;
        bytes[38] = v.Panose6; bytes[39] = v.Panose7;
        bytes[40] = v.Panose8; bytes[41] = v.Panose9;
        U32(bytes, 42, v.UnicodeRange1);
        U32(bytes, 46, v.UnicodeRange2);
        U32(bytes, 50, v.UnicodeRange3);
        U32(bytes, 54, v.UnicodeRange4);
        U32(bytes, 58, v.VendorId);
        U16(bytes, 62, v.FsSelection);
        U16(bytes, 64, v.FirstCharIndex);
        U16(bytes, 66, v.LastCharIndex);
        if (model.Length >= Os2Layout.V0Length)
        {
            S16(bytes, 68, v.TypoAscender!.Value);
            S16(bytes, 70, v.TypoDescender!.Value);
            S16(bytes, 72, v.TypoLineGap!.Value);
            U16(bytes, 74, v.WinAscent!.Value);
            U16(bytes, 76, v.WinDescent!.Value);
        }
        if (model.Version >= 1)
        {
            U32(bytes, 78, v.CodePageRange1!.Value);
            U32(bytes, 82, v.CodePageRange2!.Value);
        }
        if (model.Version >= 2)
        {
            S16(bytes, 86, v.XHeight!.Value);
            S16(bytes, 88, v.CapHeight!.Value);
            U16(bytes, 90, v.DefaultChar!.Value);
            U16(bytes, 92, v.BreakChar!.Value);
            U16(bytes, 94, v.MaxContext!.Value);
        }
        if (model.Version >= 5)
        {
            U16(bytes, 96, v.LowerOpticalPointSize!.Value);
            U16(bytes, 98, v.UpperOpticalPointSize!.Value);
        }
    }
}
