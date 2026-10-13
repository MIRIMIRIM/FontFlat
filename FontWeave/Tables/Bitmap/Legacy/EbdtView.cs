using System.Buffers.Binary;
using FontWeave.Core;
using FontWeave.Tables.Layout;

namespace FontWeave.Tables;

/// <summary>EBDT 2.0 frame reads. EBLC supplies every frame window and its
/// strike depth; bitmap bits and composite references are never rendered.</summary>
public readonly ref partial struct EbdtView
{
    public ReadStatus ValidateTable(out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        var budget = new BuildBudget(limits);
        if (!budget.Input(_data.Length, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        return ValidateTable(out diagnostic, budget);
    }

    internal ReadStatus ValidateTable(out FontDiagnostic diagnostic, BuildBudget budget)
    {
        ReadStatus status = TryGetMajorVersion(out ushort major, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (major != EbdtLayout.MajorVersion2)
            return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedVersion,
                Location.AtLocal(DataField.Version, 0), out diagnostic, major, EbdtLayout.MajorVersion2);
        status = TryGetMinorVersion(out ushort minor, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (minor != 0)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.AtLocal(DataField.Payload, 2), out diagnostic, minor, 0UL);
        return Done(out diagnostic);
    }

    public ReadStatus ValidateTable(EblcView eblc, int? numGlyphs,
        out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        var budget = new BuildBudget(limits);
        return Walk(eblc, numGlyphs, false, out _, out diagnostic, budget);
    }

    public ReadStatus TryReadTable(EblcView eblc, int? numGlyphs, out EbdtTable? model,
        out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        var budget = new BuildBudget(limits);
        return Walk(eblc, numGlyphs, true, out model, out diagnostic, budget);
    }

    public ReadStatus TryGetImage1(ulong position, byte bitDepth,
        out EbdtSmallGlyphMetrics metrics, out ReadOnlySpan<byte> data,
        out FontDiagnostic diagnostic) =>
        ReadSmall(position, bitDepth, 1, out metrics, out data, out diagnostic);

    public ReadStatus TryGetImage2(ulong position, byte bitDepth,
        out EbdtSmallGlyphMetrics metrics, out ReadOnlySpan<byte> data,
        out FontDiagnostic diagnostic) =>
        ReadSmall(position, bitDepth, 2, out metrics, out data, out diagnostic);

    public ReadStatus TryGetImage6(ulong position, byte bitDepth,
        out EbdtBigGlyphMetrics metrics, out ReadOnlySpan<byte> data,
        out FontDiagnostic diagnostic) =>
        ReadBig(position, bitDepth, 6, out metrics, out data, out diagnostic);

    public ReadStatus TryGetImage7(ulong position, byte bitDepth,
        out EbdtBigGlyphMetrics metrics, out ReadOnlySpan<byte> data,
        out FontDiagnostic diagnostic) =>
        ReadBig(position, bitDepth, 7, out metrics, out data, out diagnostic);

    /// <summary>Format 5 has no length word or metrics head. byteLength is
    /// the EBLC slot's imageSize authority.</summary>
    public ReadStatus TryGetImage5(ulong position, ulong byteLength, byte bitDepth,
        out ReadOnlySpan<byte> data, out FontDiagnostic diagnostic)
    {
        data = default;
        ReadStatus status = AnalyzeFrame(5, position, byteLength, bitDepth, null,
            out var frame, out diagnostic, new BuildBudget());
        if (status != ReadStatus.Done) return status;
        data = frame.Data;
        return ReadStatus.Done;
    }

    public ReadStatus TryGetImage8(ulong position, byte bitDepth,
        out EbdtSmallGlyphMetrics metrics, out byte pad, out EbdtComponent[] components,
        out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        metrics = default;
        pad = 0;
        components = [];
        var budget = new BuildBudget(limits);
        if (!budget.Input(_data.Length, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        ReadStatus status = AnalyzeFrame(8, position, Remaining(position), bitDepth, null,
            out var frame, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        if (!budget.Buffer(frame.Components.Length, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        metrics = SmallMetrics(frame.Metrics);
        pad = frame.Pad;
        components = MaterializeComponents(frame.Components);
        return ReadStatus.Done;
    }

    public ReadStatus TryGetImage9(ulong position, byte bitDepth,
        out EbdtBigGlyphMetrics metrics, out EbdtComponent[] components,
        out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        metrics = default;
        components = [];
        var budget = new BuildBudget(limits);
        if (!budget.Input(_data.Length, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        ReadStatus status = AnalyzeFrame(9, position, Remaining(position), bitDepth, null,
            out var frame, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        if (!budget.Buffer(frame.Components.Length, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        metrics = BigMetrics(frame.Metrics);
        components = MaterializeComponents(frame.Components);
        return ReadStatus.Done;
    }

    private ReadStatus ReadSmall(ulong position, byte bitDepth, ushort format,
        out EbdtSmallGlyphMetrics metrics, out ReadOnlySpan<byte> data,
        out FontDiagnostic diagnostic)
    {
        metrics = default;
        data = default;
        ReadStatus status = AnalyzeFrame(format, position, Remaining(position), bitDepth, null,
            out var frame, out diagnostic, new BuildBudget());
        if (status != ReadStatus.Done) return status;
        metrics = SmallMetrics(frame.Metrics);
        data = frame.Data;
        return ReadStatus.Done;
    }

    private ReadStatus ReadBig(ulong position, byte bitDepth, ushort format,
        out EbdtBigGlyphMetrics metrics, out ReadOnlySpan<byte> data,
        out FontDiagnostic diagnostic)
    {
        metrics = default;
        data = default;
        ReadStatus status = AnalyzeFrame(format, position, Remaining(position), bitDepth, null,
            out var frame, out diagnostic, new BuildBudget());
        if (status != ReadStatus.Done) return status;
        metrics = BigMetrics(frame.Metrics);
        data = frame.Data;
        return ReadStatus.Done;
    }

    public ReadStatus TryGetGlyphImage(EblcView eblc, int sizeIndex, int glyphId,
        out EbdtGlyphImage image, out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        image = default;
        var budget = new BuildBudget(limits);
        ReadStatus status = RequirePair(eblc, null, out diagnostic, budget, false);
        if (status != ReadStatus.Done) return status;
        status = eblc.TryGetGlyphLocation(sizeIndex, glyphId, out var location,
            out bool present, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        if (!present)
        {
            image = new(0, null, [], [], 0);
            return Done(out diagnostic);
        }
        if (!EblcLayout.IsImageFormatTyped(location.ImageFormat))
            return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
                eblc.Location.AtLocal(DataField.Payload, location.ImageFormatPosition), out diagnostic,
                location.ImageFormat, 0UL);
        if (!EblcLayout.IsPairingDefined(location.IndexFormat, location.ImageFormat))
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                eblc.Location.AtLocal(DataField.Payload, location.ImageFormatPosition), out diagnostic,
                location.ImageFormat, 0UL);
        status = AnalyzeFrame(location.ImageFormat, location.PositionInEbdt,
            location.ByteLength, location.BitDepth, null, out var frame, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        status = Materialize(frame, location.ImageFormat, location.BitDepth, 0,
            out var owned, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        image = new(owned.ImageFormat, owned.Metrics, owned.Data, owned.Components,
            owned.BitDepth, owned.CompositePad);
        return ReadStatus.Done;
    }

    private ReadStatus RequirePair(EblcView eblc, int? numGlyphs,
        out FontDiagnostic diagnostic, BuildBudget budget, bool validateIndex = true)
    {
        if (numGlyphs is { } domain)
        {
            ReadStatus contract = CoverageTableCompiler.CheckNumGlyphs(domain, Location, out diagnostic);
            if (contract != ReadStatus.Done) return contract;
        }
        if (!IsBound || !eblc.IsBound)
            return ReadWire.Fail(ReadStatus.InvalidSource, DiagnosticCode.WrongSourceFace,
                !IsBound ? Location : eblc.Location, out diagnostic);
        if (Location.SourceId != eblc.Location.SourceId || Location.FaceIndex != eblc.Location.FaceIndex)
            return ReadWire.Fail(ReadStatus.InvalidSource, DiagnosticCode.WrongSourceFace,
                eblc.Location, out diagnostic);
        if (!budget.Input(_data.Length, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        if (!budget.Input(eblc.RawBytes.Length, eblc.Location, out diagnostic)) return ReadStatus.ResourceLimit;
        ReadStatus status = ValidateTable(out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        return validateIndex ? eblc.ValidateTable(_data.Length, numGlyphs, out diagnostic, budget)
            : Done(out diagnostic);
    }

    private ReadStatus Walk(EblcView eblc, int? numGlyphs, bool materialize,
        out EbdtTable? model, out FontDiagnostic diagnostic, BuildBudget budget)
    {
        model = null;
        ReadStatus status = RequirePair(eblc, numGlyphs, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        status = eblc.TryGetNumSizes(out uint numSizes, out diagnostic);
        if (status != ReadStatus.Done) return status;
        List<EbdtImage>? images = materialize ? [] : null;
        ulong consumed = EbdtLayout.HeaderLength;
        for (int s = 0; s < numSizes; s++)
        {
            status = eblc.TryGetBitmapSize(s, out var row, out diagnostic);
            if (status != ReadStatus.Done) return status;
            for (int r = 0; r < row.NumIndexSubtables; r++)
            {
                status = eblc.TryGetIndexSubtable(s, r, out var subtable, out diagnostic, budget);
                if (status != ReadStatus.Done) return status;
                int count = subtable switch
                {
                    EblcOffsetSubtable offsets => offsets.SbitOffsets.Length - 1,
                    EblcImageSizeSubtable => subtable.LastGlyphIndex - subtable.FirstGlyphIndex + 1,
                    EblcPairSubtable pairs => pairs.GlyphIds.Length - 1,
                    EblcGlyphIdArraySubtable glyphs => glyphs.GlyphIds.Length,
                    _ => 0,
                };
                for (int k = 0; k < count; k++)
                {
                    Window(subtable, k, out ulong position, out ulong length);
                    if (length == 0) continue;
                    status = AnalyzeFrame(subtable.ImageFormat, position, length, row.BitDepth,
                        numGlyphs, out var frame, out diagnostic, budget);
                    if (status != ReadStatus.Done) return status;
                    consumed = Math.Max(consumed, position + frame.Length);
                    if (images is not null)
                    {
                        status = Materialize(frame, subtable.ImageFormat, row.BitDepth,
                            images.Count, out var owned, out diagnostic, budget);
                        if (status != ReadStatus.Done) return status;
                        images.Add(owned);
                    }
                }
            }
        }
        if (images is not null)
        {
            if (!budget.Buffer(images.Count * (long)IntPtr.Size, Location, out diagnostic))
                return ReadStatus.ResourceLimit;
            model = new(EbdtLayout.MajorVersion2, 0, [.. images], Location, (int)consumed);
        }
        return Done(out diagnostic);
    }

    private static void Window(EblcIndexSubtable subtable, int k,
        out ulong position, out ulong length)
    {
        switch (subtable)
        {
            case EblcOffsetSubtable offsets:
                position = (ulong)subtable.ImageDataOffset + offsets.SbitOffsets[k];
                length = offsets.SbitOffsets[k + 1] - offsets.SbitOffsets[k];
                break;
            case EblcPairSubtable pairs:
                position = (ulong)subtable.ImageDataOffset + pairs.SbitOffsets[k];
                length = (ulong)(pairs.SbitOffsets[k + 1] - pairs.SbitOffsets[k]);
                break;
            case EblcImageSizeSubtable constant:
                position = (ulong)subtable.ImageDataOffset + (ulong)k * constant.ImageSize;
                length = constant.ImageSize;
                break;
            case EblcGlyphIdArraySubtable sparse:
                position = (ulong)subtable.ImageDataOffset + (ulong)k * sparse.ImageSize;
                length = sparse.ImageSize;
                break;
            default:
                position = length = 0;
                break;
        }
    }

    private ReadStatus AnalyzeFrame(ushort format, ulong position, ulong slotLength,
        byte bitDepth, int? numGlyphs, out BitmapImageCodec.FrameParts frame, out FontDiagnostic diagnostic,
        BuildBudget budget)
    {
        frame = default;
        if (!IsBound)
            return ReadWire.Fail(ReadStatus.Uninitialized, DiagnosticCode.UnboundView, Location, out diagnostic);
        if (!EblcLayout.IsBitDepthDefined(bitDepth))
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.AtLocal(DataField.Payload, position), out diagnostic, bitDepth, 0UL);
        return BitmapImageCodec.AnalyzeLegacyFrame(_data, Location, format, position, slotLength,
            bitDepth, numGlyphs, out frame, out diagnostic, budget);
    }

    private ReadStatus Materialize(BitmapImageCodec.FrameParts frame, ushort format, byte bitDepth, int ordinal,
        out EbdtImage image, out FontDiagnostic diagnostic, BuildBudget budget)
    {
        image = null!;
        long charge = frame.Metrics.Length + (long)frame.Data.Length + frame.Components.Length;
        if (!budget.Buffer(charge, Location.AtLocal(DataField.Payload, 0, ordinal), out diagnostic))
            return ReadStatus.ResourceLimit;
        image = new(format, frame.Metrics.IsEmpty ? null : frame.Metrics.ToArray(),
            frame.Data.ToArray(), bitDepth, MaterializeComponents(frame.Components), frame.Pad);
        return Done(out diagnostic);
    }

    private static EbdtComponent[] MaterializeComponents(ReadOnlySpan<byte> bytes)
    {
        var components = new EbdtComponent[bytes.Length / EbdtLayout.EbdtComponentStride];
        for (int i = 0; i < components.Length; i++)
        {
            var row = bytes.Slice(i * EbdtLayout.EbdtComponentStride, EbdtLayout.EbdtComponentStride);
            components[i] = new(BinaryPrimitives.ReadUInt16BigEndian(row),
                unchecked((sbyte)row[2]), unchecked((sbyte)row[3]));
        }
        return components;
    }

    private ulong Remaining(ulong position) => position <= (ulong)_data.Length
        ? (ulong)_data.Length - position : 0UL;

    private static EbdtSmallGlyphMetrics SmallMetrics(ReadOnlySpan<byte> metrics) =>
        new(metrics[0], metrics[1], unchecked((sbyte)metrics[2]), unchecked((sbyte)metrics[3]), metrics[4]);

    private static EbdtBigGlyphMetrics BigMetrics(ReadOnlySpan<byte> metrics) =>
        new(metrics[0], metrics[1], unchecked((sbyte)metrics[2]), unchecked((sbyte)metrics[3]),
            metrics[4], unchecked((sbyte)metrics[5]), unchecked((sbyte)metrics[6]), metrics[7]);

    private static ReadStatus Done(out FontDiagnostic diagnostic)
    {
        diagnostic = default;
        return ReadStatus.Done;
    }
}
