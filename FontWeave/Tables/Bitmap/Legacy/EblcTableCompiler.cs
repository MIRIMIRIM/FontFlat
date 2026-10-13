using System.Buffers.Binary;
using FontWeave.Core;

namespace FontWeave.Tables;

/// <summary>Canonical EBLC index emission. Pair compilation also repacks EBDT
/// and recalculates every cross-table offset before publishing either output.</summary>
public static class EblcTableCompiler
{
    public static ReadStatus TryCreate(ushort majorVersion, ushort minorVersion,
        IReadOnlyList<EblcStrike> sizes, EbdtTable ebdt, out EblcTable? model,
        out FontDiagnostic diagnostic, BuildLimits? limits = null, DataLocation location = default)
    {
        ArgumentNullException.ThrowIfNull(ebdt);
        location = location == default ? DataLocation.Anonymous(EblcLayout.Tag) : location;
        return Create(majorVersion, minorVersion, sizes, ebdt, out model, out diagnostic,
            new BuildBudget(limits), location);
    }

    private static ReadStatus Create(ushort majorVersion, ushort minorVersion,
        IReadOnlyList<EblcStrike> sizes, EbdtTable ebdt, out EblcTable? model,
        out FontDiagnostic diagnostic, BuildBudget budget, DataLocation location)
    {
        model = null;
        ArgumentNullException.ThrowIfNull(sizes);
        ReadStatus status = RequireVersion(majorVersion, minorVersion, location, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (location.SourceId != ebdt.Location.SourceId || location.FaceIndex != ebdt.Location.FaceIndex)
            return ReadWire.Fail(ReadStatus.InvalidSource, DiagnosticCode.WrongSourceFace,
                ebdt.Location, out diagnostic);
        status = EbdtTableCompiler.RequireVersion(ebdt.MajorVersion, ebdt.MinorVersion,
            ebdt.Location, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = CheckParts(sizes, location, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        var images = ebdt.Images;
        status = EbdtTableCompiler.CheckImages(images, ebdt.Location, out _, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        long count = CountChainSlots(sizes);
        if (count != images.Count)
            return WrongSource(location, count, images.Count, out diagnostic);
        if (!budget.Buffer(sizes.Count * 48L, location, out diagnostic)) return ReadStatus.ResourceLimit;
        var rewrittenSizes = new EblcStrike[sizes.Count];
        long poolCursor = EbdtLayout.HeaderLength;
        int imageCursor = 0;
        for (int s = 0; s < sizes.Count; s++)
        {
            var strike = sizes[s];
            var parts = strike.Subtables;
            if (!budget.Buffer(parts.Count * (long)IntPtr.Size, location, out diagnostic))
                return ReadStatus.ResourceLimit;
            var rewritten = new EblcIndexSubtable[parts.Count];
            for (int r = 0; r < parts.Count; r++)
            {
                status = RewriteSubtable(parts[r], strike.BitDepth, images, ref imageCursor,
                    ref poolCursor, location, out rewritten[r], out diagnostic, budget);
                if (status != ReadStatus.Done) return status;
            }
            rewrittenSizes[s] = CopyStrike(strike, 0, 0, rewritten);
        }
        if (imageCursor != images.Count)
            return WrongSource(location, imageCursor, images.Count, out diagnostic);
        status = Layout(rewrittenSizes, location, out var canonical, out long length,
            out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        model = new(majorVersion, minorVersion, canonical, location, (int)length);
        return Done(out diagnostic);
    }

    /// <summary>Compiles both tables with a shared budget. Frame format and
    /// depth receipts must match the current EBLC slots; outputs stay null on
    /// every refusal, including a changed depth with unchanged image data.</summary>
    public static ReadStatus TryCompilePair(EblcTable model, EbdtTable ebdt,
        out byte[]? eblcBytes, out byte[]? ebdtBytes, out FontDiagnostic diagnostic,
        BuildLimits? limits = null)
    {
        eblcBytes = null;
        ebdtBytes = null;
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(ebdt);
        var budget = new BuildBudget(limits);
        ReadStatus status = Create(model.MajorVersion, model.MinorVersion, model.Sizes, ebdt,
            out var canonical, out diagnostic, budget, model.Location);
        if (status != ReadStatus.Done) return status;
        long total = canonical!.ConsumedLength + EbdtTableCompiler.PackedLength(ebdt);
        if (!budget.Output(total, model.Location, out diagnostic)) return ReadStatus.ResourceLimit;
        status = Compile(canonical, out var indexBytes, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        status = EbdtTableCompiler.Compile(ebdt, out var imageBytes, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        eblcBytes = indexBytes;
        ebdtBytes = imageBytes;
        return Done(out diagnostic);
    }

    public static ReadStatus TryWritePair(EblcTable model, EbdtTable ebdt,
        Span<byte> eblcDestination, Span<byte> ebdtDestination,
        out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        ReadStatus status = TryCompilePair(model, ebdt, out var indexBytes, out var imageBytes,
            out diagnostic, limits);
        if (status != ReadStatus.Done) return status;
        if (eblcDestination.Length < indexBytes!.Length)
            return ShortDestination(model.Location, eblcDestination.Length, indexBytes.Length, out diagnostic);
        if (ebdtDestination.Length < imageBytes!.Length)
            return ShortDestination(ebdt.Location, ebdtDestination.Length, imageBytes.Length, out diagnostic);
        indexBytes.AsSpan().CopyTo(eblcDestination);
        imageBytes.AsSpan().CopyTo(ebdtDestination);
        return Done(out diagnostic);
    }

    public static ReadStatus TryWrite(EblcTable model, Span<byte> destination,
        out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        ReadStatus status = TryCompile(model, out var bytes, out diagnostic, limits);
        if (status != ReadStatus.Done) return status;
        if (destination.Length < bytes!.Length)
            return ShortDestination(model.Location, destination.Length, bytes.Length, out diagnostic);
        bytes.AsSpan().CopyTo(destination);
        return Done(out diagnostic);
    }

    /// <summary>Normalizes the EBLC internal index layout while preserving
    /// EBDT coordinates. Use TryCompilePair when the EBDT pool changes.</summary>
    public static ReadStatus TryCompile(EblcTable model, out byte[]? bytes,
        out FontDiagnostic diagnostic, BuildLimits? limits = null) =>
        Compile(model, out bytes, out diagnostic, new BuildBudget(limits));

    private static ReadStatus Compile(EblcTable model, out byte[]? bytes,
        out FontDiagnostic diagnostic, BuildBudget budget)
    {
        bytes = null;
        ArgumentNullException.ThrowIfNull(model);
        ReadStatus status = RequireVersion(model.MajorVersion, model.MinorVersion,
            model.Location, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = CheckParts(model.Sizes, model.Location, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        status = Layout(model.Sizes, model.Location, out var sizes, out long length,
            out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        if (!budget.Buffer(length, model.Location, out diagnostic) ||
            !budget.Output(length, model.Location, out diagnostic)) return ReadStatus.ResourceLimit;
        var output = new byte[(int)length];
        RequireWritten(EblcView.TryWriteMajorVersion(output, model.MajorVersion, out _, model.Location));
        RequireWritten(EblcView.TryWriteMinorVersion(output, model.MinorVersion, out _, model.Location));
        RequireWritten(EblcView.TryWriteNumSizes(output, (uint)sizes.Length, out _, model.Location));
        for (int s = 0; s < sizes.Length; s++)
        {
            var strike = sizes[s];
            var parts = strike.Subtables;
            RequireWritten(EblcView.TryWriteBitmapSizeAt(output,
                EblcLayout.BitmapSizeAt + (ulong)s * EblcLayout.BitmapSizeStride,
                Row(s, strike), out _, model.Location));
            int recordAt = (int)strike.IndexSubtableListOffset;
            ulong relative = (ulong)parts.Count * EblcLayout.IndexSubtableRecordStride;
            for (int r = 0; r < parts.Count; r++)
            {
                relative = Align4(relative);
                var part = parts[r];
                W16(output, recordAt, part.FirstGlyphIndex);
                W16(output, recordAt + 2, part.LastGlyphIndex);
                W32(output, recordAt + 4, (uint)relative);
                WriteSubtable(output, (int)(strike.IndexSubtableListOffset + relative), part);
                recordAt += EblcLayout.IndexSubtableRecordStride;
                relative += SubtableLength(part);
            }
        }
        budget.RecordCompiledTable(output.Length);
        bytes = output;
        return Done(out diagnostic);
    }

    private static ReadStatus Layout(IReadOnlyList<EblcStrike> source, DataLocation location,
        out EblcStrike[] sizes, out long length, out FontDiagnostic diagnostic, BuildBudget budget)
    {
        sizes = [];
        ulong cursor = EblcLayout.HeaderLength + (ulong)source.Count * EblcLayout.BitmapSizeStride;
        foreach (var strike in source)
        {
            cursor += (ulong)strike.Subtables.Count * EblcLayout.IndexSubtableRecordStride;
            foreach (var part in strike.Subtables)
            {
                cursor = Align4(cursor);
                cursor += SubtableLength(part);
            }
        }
        length = (long)cursor;
        if (cursor > int.MaxValue)
            return ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.RangeOutsideSource,
                location.AtLocal(DataField.Length, 0), out diagnostic, cursor, int.MaxValue);
        if (!budget.Work(length, location, out diagnostic) ||
            !budget.Buffer(source.Count * 48L, location, out diagnostic)) return ReadStatus.ResourceLimit;
        sizes = new EblcStrike[source.Count];
        cursor = EblcLayout.HeaderLength + (ulong)source.Count * EblcLayout.BitmapSizeStride;
        for (int s = 0; s < source.Count; s++)
        {
            var strike = source[s];
            var parts = strike.Subtables;
            if (!budget.Buffer(parts.Count * (long)IntPtr.Size, location, out diagnostic)) return ReadStatus.ResourceLimit;
            ulong start = cursor;
            cursor += (ulong)parts.Count * EblcLayout.IndexSubtableRecordStride;
            var copies = new EblcIndexSubtable[parts.Count];
            for (int r = 0; r < parts.Count; r++)
            {
                cursor = Align4(cursor);
                cursor += SubtableLength(parts[r]);
                copies[r] = parts[r];
            }
            sizes[s] = CopyStrike(strike, (uint)start, (uint)(cursor - start), copies);
        }
        return Done(out diagnostic);
    }

    private static ReadStatus CheckParts(IReadOnlyList<EblcStrike> sizes, DataLocation location,
        out FontDiagnostic diagnostic, BuildBudget budget)
    {
        if (!budget.Work(sizes.Count * 48L, location, out diagnostic)) return ReadStatus.ResourceLimit;
        for (int s = 0; s < sizes.Count; s++)
        {
            var strike = sizes[s];
            ArgumentNullException.ThrowIfNull(strike);
            if (strike.HoriMetrics.Length != EblcLayout.SbitLineMetricsLength)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    location.AtLocal(DataField.Length, EblcLayout.BitmapSizeAt + (ulong)s * EblcLayout.BitmapSizeStride +
                        EblcLayout.HoriMetricsAt, s), out diagnostic,
                    (ulong)strike.HoriMetrics.Length, EblcLayout.SbitLineMetricsLength);
            if (strike.VertMetrics.Length != EblcLayout.SbitLineMetricsLength)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    location.AtLocal(DataField.Length, EblcLayout.BitmapSizeAt + (ulong)s * EblcLayout.BitmapSizeStride +
                        EblcLayout.VertMetricsAt, s), out diagnostic,
                    (ulong)strike.VertMetrics.Length, EblcLayout.SbitLineMetricsLength);
            if (strike.StartGlyphIndex > strike.EndGlyphIndex)
                return Invalid(location, DataField.GlyphId, s, strike.StartGlyphIndex, strike.EndGlyphIndex, out diagnostic);
            if (strike.ColorRef != 0)
                return Invalid(location, DataField.Payload, s, strike.ColorRef, 0, out diagnostic);
            if (!EblcLayout.IsBitDepthDefined(strike.BitDepth))
                return Invalid(location, DataField.Payload, s, strike.BitDepth, 0, out diagnostic);
            if ((strike.Flags & EblcLayout.FlagsReservedMask) != 0)
                return Invalid(location, DataField.Flags, s, (ulong)(strike.Flags & EblcLayout.FlagsReservedMask), 0, out diagnostic);
            var parts = strike.Subtables;
            ushort previousFirst = 0, previousLast = 0;
            if (!budget.Work(parts.Count * 8L, location, out diagnostic)) return ReadStatus.ResourceLimit;
            for (int r = 0; r < parts.Count; r++)
            {
                var part = parts[r];
                ArgumentNullException.ThrowIfNull(part);
                if (!EblcLayout.IsIndexFormatDefined(part.IndexFormat) || !EblcLayout.IsImageFormatTyped(part.ImageFormat))
                    return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
                        location.AtLocal(DataField.Payload, 0, r), out diagnostic,
                        !EblcLayout.IsIndexFormatDefined(part.IndexFormat) ? part.IndexFormat : part.ImageFormat, 0UL);
                if (part.FirstGlyphIndex > part.LastGlyphIndex)
                    return Invalid(location, DataField.GlyphId, r, part.FirstGlyphIndex, part.LastGlyphIndex, out diagnostic);
                if (!EblcLayout.IsPairingDefined(part.IndexFormat, part.ImageFormat))
                    return Invalid(location, DataField.Payload, r, part.ImageFormat, 0, out diagnostic);
                if (r > 0 && part.FirstGlyphIndex < previousFirst)
                    return Invalid(location, DataField.GlyphId, r, part.FirstGlyphIndex, previousFirst, out diagnostic);
                if (r > 0 && part.FirstGlyphIndex <= previousLast)
                    return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.OverlappingRange,
                        location.AtLocal(DataField.GlyphId, 0, r), out diagnostic, part.FirstGlyphIndex, previousLast);
                previousFirst = part.FirstGlyphIndex;
                previousLast = part.LastGlyphIndex;
                ReadStatus status = CheckPart(part, location, out diagnostic, budget);
                if (status != ReadStatus.Done) return status;
            }
        }
        return Done(out diagnostic);
    }

    private static ReadStatus CheckPart(EblcIndexSubtable part, DataLocation location,
        out FontDiagnostic diagnostic, BuildBudget budget)
    {
        switch (part)
        {
            case EblcOffsetSubtable offsets:
                if (part.IndexFormat is not (1 or 3) ||
                    offsets.SbitOffsets.Length != part.LastGlyphIndex - part.FirstGlyphIndex + 2)
                    return Invalid(location, DataField.Count, part.Ordinal,
                        (ulong)offsets.SbitOffsets.Length, (ulong)(part.LastGlyphIndex - part.FirstGlyphIndex + 2), out diagnostic);
                if (!budget.Work(offsets.SbitOffsets.Length, location, out diagnostic)) return ReadStatus.ResourceLimit;
                for (int k = 0; k < offsets.SbitOffsets.Length; k++)
                {
                    uint value = offsets.SbitOffsets[k];
                    if (k > 0 && value < offsets.SbitOffsets[k - 1])
                        return Invalid(location, DataField.Offset, part.Ordinal, value, offsets.SbitOffsets[k - 1], out diagnostic);
                    if (part.IndexFormat == 3 && value > ushort.MaxValue)
                        return Range(location, value, ushort.MaxValue, out diagnostic);
                }
                break;
            case EblcImageSizeSubtable constant:
                if (constant.BigMetrics.Length != EblcLayout.BigGlyphMetricsLength)
                    return Invalid(location, DataField.Length, part.Ordinal, (ulong)constant.BigMetrics.Length, 8, out diagnostic);
                break;
            case EblcPairSubtable pairs:
                if (pairs.GlyphIds.Length == 0 || pairs.GlyphIds.Length != pairs.SbitOffsets.Length)
                    return Invalid(location, DataField.Count, part.Ordinal,
                        (ulong)pairs.GlyphIds.Length, (ulong)pairs.SbitOffsets.Length, out diagnostic);
                if (!budget.Work(pairs.GlyphIds.Length, location, out diagnostic)) return ReadStatus.ResourceLimit;
                for (int k = 1; k < pairs.GlyphIds.Length; k++)
                {
                    if (pairs.GlyphIds[k] <= pairs.GlyphIds[k - 1])
                        return Invalid(location, DataField.GlyphId, part.Ordinal, pairs.GlyphIds[k], pairs.GlyphIds[k - 1], out diagnostic);
                    if (pairs.SbitOffsets[k] < pairs.SbitOffsets[k - 1])
                        return Invalid(location, DataField.Offset, part.Ordinal, pairs.SbitOffsets[k], pairs.SbitOffsets[k - 1], out diagnostic);
                }
                break;
            case EblcGlyphIdArraySubtable glyphs:
                if (glyphs.BigMetrics.Length != EblcLayout.BigGlyphMetricsLength)
                    return Invalid(location, DataField.Length, part.Ordinal, (ulong)glyphs.BigMetrics.Length, 8, out diagnostic);
                if (!budget.Work(glyphs.GlyphIds.Length, location, out diagnostic)) return ReadStatus.ResourceLimit;
                for (int k = 1; k < glyphs.GlyphIds.Length; k++)
                    if (glyphs.GlyphIds[k] <= glyphs.GlyphIds[k - 1])
                        return Invalid(location, DataField.GlyphId, part.Ordinal, glyphs.GlyphIds[k], glyphs.GlyphIds[k - 1], out diagnostic);
                break;
            default:
                return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
                    location.AtLocal(DataField.Payload, 0, part.Ordinal), out diagnostic, part.IndexFormat, 0UL);
        }
        return Done(out diagnostic);
    }

    private static ReadStatus RewriteSubtable(EblcIndexSubtable source, byte bitDepth,
        IReadOnlyList<EbdtImage> images, ref int imageCursor, ref long poolCursor,
        DataLocation location, out EblcIndexSubtable rewritten, out FontDiagnostic diagnostic,
        BuildBudget budget)
    {
        rewritten = null!;
        long firstAt = poolCursor;
        switch (source)
        {
            case EblcOffsetSubtable offsets:
            {
                if (!budget.Buffer(offsets.SbitOffsets.Length * 4L, location, out diagnostic)) return ReadStatus.ResourceLimit;
                var fresh = new uint[offsets.SbitOffsets.Length];
                ulong run = 0;
                for (int k = 0; k < fresh.Length - 1; k++)
                {
                    fresh[k] = (uint)run;
                    if (offsets.SbitOffsets[k] == offsets.SbitOffsets[k + 1]) continue;
                    ReadStatus status = NextFrame(source, bitDepth, images, ref imageCursor,
                        ref poolCursor, location, out long frame, out diagnostic);
                    if (status != ReadStatus.Done) return status;
                    run += (ulong)frame;
                    ulong maximum = source.IndexFormat == 3 ? ushort.MaxValue : uint.MaxValue;
                    if (run > maximum) return Range(location, run, maximum, out diagnostic);
                }
                fresh[^1] = (uint)run;
                rewritten = new EblcOffsetSubtable(source.Ordinal, source.IndexFormat, source.ImageFormat,
                    (uint)firstAt, source.FirstGlyphIndex, source.LastGlyphIndex, fresh);
                break;
            }
            case EblcPairSubtable pairs:
            {
                if (!budget.Buffer(pairs.SbitOffsets.Length * 2L, location, out diagnostic)) return ReadStatus.ResourceLimit;
                var fresh = new ushort[pairs.SbitOffsets.Length];
                ulong run = 0;
                for (int k = 0; k < fresh.Length - 1; k++)
                {
                    fresh[k] = (ushort)run;
                    if (pairs.SbitOffsets[k] == pairs.SbitOffsets[k + 1]) continue;
                    ReadStatus status = NextFrame(source, bitDepth, images, ref imageCursor,
                        ref poolCursor, location, out long frame, out diagnostic);
                    if (status != ReadStatus.Done) return status;
                    run += (ulong)frame;
                    if (run > ushort.MaxValue) return Range(location, run, ushort.MaxValue, out diagnostic);
                }
                fresh[^1] = (ushort)run;
                rewritten = new EblcPairSubtable(source.Ordinal, source.ImageFormat, (uint)firstAt,
                    source.FirstGlyphIndex, source.LastGlyphIndex, pairs.GlyphIds, fresh);
                break;
            }
            case EblcImageSizeSubtable constant:
            {
                int count = constant.ImageSize == 0 ? 0 : source.LastGlyphIndex - source.FirstGlyphIndex + 1;
                ReadStatus status = ConstantFrames(source, bitDepth, count, images,
                    ref imageCursor, ref poolCursor, location, out uint size, out diagnostic);
                if (status != ReadStatus.Done) return status;
                rewritten = new EblcImageSizeSubtable(source.Ordinal, source.ImageFormat, (uint)firstAt,
                    source.FirstGlyphIndex, source.LastGlyphIndex, size, constant.BigMetrics);
                break;
            }
            case EblcGlyphIdArraySubtable glyphs:
            {
                int count = glyphs.ImageSize == 0 ? 0 : glyphs.GlyphIds.Length;
                ReadStatus status = ConstantFrames(source, bitDepth, count, images,
                    ref imageCursor, ref poolCursor, location, out uint size, out diagnostic);
                if (status != ReadStatus.Done) return status;
                rewritten = new EblcGlyphIdArraySubtable(source.Ordinal, source.ImageFormat, (uint)firstAt,
                    source.FirstGlyphIndex, source.LastGlyphIndex, size, glyphs.BigMetrics, glyphs.GlyphIds);
                break;
            }
            default:
                return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
                    location.AtLocal(DataField.Payload, 0, source.Ordinal), out diagnostic, source.IndexFormat, 0UL);
        }
        return Done(out diagnostic);
    }

    private static ReadStatus NextFrame(EblcIndexSubtable source, byte bitDepth,
        IReadOnlyList<EbdtImage> images, ref int imageCursor, ref long poolCursor,
        DataLocation location, out long frame, out FontDiagnostic diagnostic)
    {
        frame = 0;
        if (imageCursor >= images.Count)
            return WrongSource(location, imageCursor, images.Count, out diagnostic);
        var image = images[imageCursor];
        if (image.ImageFormat != source.ImageFormat)
            return ReadWire.Fail(ReadStatus.InvalidSource, DiagnosticCode.WrongSourceFace,
                location.AtLocal(DataField.Payload, 0, source.Ordinal), out diagnostic,
                image.ImageFormat, source.ImageFormat);
        if (image.BitDepth != bitDepth)
            return Invalid(location, DataField.Payload, source.Ordinal, image.BitDepth, bitDepth, out diagnostic);
        frame = EbdtTableCompiler.FrameLength(image);
        if (frame == 0)
            return Invalid(location, DataField.Length, source.Ordinal, 0, 1, out diagnostic);
        poolCursor += frame;
        imageCursor++;
        return Done(out diagnostic);
    }

    private static ReadStatus ConstantFrames(EblcIndexSubtable source, byte bitDepth, int count,
        IReadOnlyList<EbdtImage> images, ref int imageCursor, ref long poolCursor,
        DataLocation location, out uint size, out FontDiagnostic diagnostic)
    {
        size = 0;
        for (int k = 0; k < count; k++)
        {
            ReadStatus status = NextFrame(source, bitDepth, images, ref imageCursor,
                ref poolCursor, location, out long frame, out diagnostic);
            if (status != ReadStatus.Done) return status;
            if (k == 0) size = (uint)frame;
            else if (frame != size)
                return Invalid(location, DataField.Length, source.Ordinal, (ulong)frame, size, out diagnostic);
        }
        return Done(out diagnostic);
    }

    private static long CountChainSlots(IReadOnlyList<EblcStrike> sizes)
    {
        long count = 0;
        foreach (var strike in sizes)
            foreach (var part in strike.Subtables)
                count += part switch
                {
                    EblcOffsetSubtable offsets => CountPopulated(offsets.SbitOffsets),
                    EblcPairSubtable pairs => CountPopulated(pairs.SbitOffsets),
                    EblcImageSizeSubtable constant when constant.ImageSize != 0 => part.LastGlyphIndex - part.FirstGlyphIndex + 1,
                    EblcGlyphIdArraySubtable glyphs when glyphs.ImageSize != 0 => glyphs.GlyphIds.Length,
                    _ => 0,
                };
        return count;
    }

    private static int CountPopulated<T>(T[] offsets) where T : IEquatable<T>
    {
        int count = 0;
        for (int k = 0; k < offsets.Length - 1; k++)
            if (!offsets[k].Equals(offsets[k + 1])) count++;
        return count;
    }

    private static ulong SubtableLength(EblcIndexSubtable part) => part switch
    {
        EblcOffsetSubtable offsets when part.IndexFormat == 3 =>
            8UL + 2UL * (ulong)offsets.SbitOffsets.Length + 2UL * (ulong)(offsets.SbitOffsets.Length % 2),
        EblcOffsetSubtable offsets => 8UL + 4UL * (ulong)offsets.SbitOffsets.Length,
        EblcImageSizeSubtable => 20UL,
        EblcPairSubtable pairs => 12UL + 4UL * (ulong)pairs.GlyphIds.Length,
        EblcGlyphIdArraySubtable glyphs => 24UL + 2UL * (ulong)glyphs.GlyphIds.Length + 2UL * (ulong)(glyphs.GlyphIds.Length % 2),
        _ => 0UL,
    };

    private static ulong Align4(ulong value) => (value + 3UL) & ~3UL;

    private static EblcStrike CopyStrike(EblcStrike source, uint listOffset, uint listSize,
        EblcIndexSubtable[] subtables) => new(listOffset, listSize, source.ColorRef,
            source.HoriMetrics, source.VertMetrics, source.StartGlyphIndex, source.EndGlyphIndex,
            source.PpemX, source.PpemY, source.BitDepth, source.Flags, subtables);

    private static EblcView.BitmapSizeData Row(int ordinal, EblcStrike strike)
    {
        var h = strike.HoriMetrics;
        var v = strike.VertMetrics;
        return new(ordinal, strike.IndexSubtableListOffset, strike.IndexSubtableListSize,
            (uint)strike.Subtables.Count, strike.ColorRef,
            unchecked((sbyte)h[0]), unchecked((sbyte)h[1]), h[2], unchecked((sbyte)h[3]),
            unchecked((sbyte)h[4]), unchecked((sbyte)h[5]), unchecked((sbyte)h[6]),
            unchecked((sbyte)h[7]), unchecked((sbyte)h[8]), unchecked((sbyte)h[9]),
            unchecked((sbyte)h[10]), unchecked((sbyte)h[11]),
            unchecked((sbyte)v[0]), unchecked((sbyte)v[1]), v[2], unchecked((sbyte)v[3]),
            unchecked((sbyte)v[4]), unchecked((sbyte)v[5]), unchecked((sbyte)v[6]),
            unchecked((sbyte)v[7]), unchecked((sbyte)v[8]), unchecked((sbyte)v[9]),
            unchecked((sbyte)v[10]), unchecked((sbyte)v[11]),
            strike.StartGlyphIndex, strike.EndGlyphIndex, strike.PpemX, strike.PpemY,
            strike.BitDepth, strike.Flags);
    }

    private static void WriteSubtable(byte[] output, int at, EblcIndexSubtable part)
    {
        W16(output, at, part.IndexFormat);
        W16(output, at + 2, part.ImageFormat);
        W32(output, at + 4, part.ImageDataOffset);
        switch (part)
        {
            case EblcOffsetSubtable offsets:
                for (int k = 0; k < offsets.SbitOffsets.Length; k++)
                    if (part.IndexFormat == 1) W32(output, at + 8 + 4 * k, offsets.SbitOffsets[k]);
                    else W16(output, at + 8 + 2 * k, (ushort)offsets.SbitOffsets[k]);
                break;
            case EblcImageSizeSubtable constant:
                W32(output, at + 8, constant.ImageSize);
                constant.BigMetrics.AsSpan().CopyTo(output.AsSpan(at + 12, 8));
                break;
            case EblcPairSubtable pairs:
                W32(output, at + 8, (uint)(pairs.GlyphIds.Length - 1));
                for (int k = 0; k < pairs.GlyphIds.Length; k++)
                {
                    W16(output, at + 12 + 4 * k, pairs.GlyphIds[k]);
                    W16(output, at + 14 + 4 * k, pairs.SbitOffsets[k]);
                }
                break;
            case EblcGlyphIdArraySubtable glyphs:
                W32(output, at + 8, glyphs.ImageSize);
                glyphs.BigMetrics.AsSpan().CopyTo(output.AsSpan(at + 12, 8));
                W32(output, at + 20, (uint)glyphs.GlyphIds.Length);
                for (int k = 0; k < glyphs.GlyphIds.Length; k++) W16(output, at + 24 + 2 * k, glyphs.GlyphIds[k]);
                break;
        }
    }

    private static void W16(byte[] output, int at, ushort value) =>
        BinaryPrimitives.WriteUInt16BigEndian(output.AsSpan(at, 2), value);
    private static void W32(byte[] output, int at, uint value) =>
        BinaryPrimitives.WriteUInt32BigEndian(output.AsSpan(at, 4), value);

    private static ReadStatus RequireVersion(ushort major, ushort minor, DataLocation location,
        out FontDiagnostic diagnostic) => EbdtTableCompiler.RequireVersion(major, minor, location, out diagnostic);

    private static ReadStatus Invalid(DataLocation location, DataField field, int ordinal,
        ulong actual, ulong expected, out FontDiagnostic diagnostic) =>
        ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
            location.AtLocal(field, 0, ordinal), out diagnostic, actual, expected);

    private static ReadStatus Range(DataLocation location, ulong actual, ulong expected,
        out FontDiagnostic diagnostic) => ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.RangeOutsideSource,
            location.AtLocal(DataField.Offset, 0), out diagnostic, actual, expected);

    private static ReadStatus WrongSource(DataLocation location, long actual, long expected,
        out FontDiagnostic diagnostic) => ReadWire.Fail(ReadStatus.InvalidSource, DiagnosticCode.WrongSourceFace,
            location.AtLocal(DataField.Count, 0), out diagnostic, (ulong)actual, (ulong)expected);

    private static ReadStatus ShortDestination(DataLocation location, int actual, int expected,
        out FontDiagnostic diagnostic) => ReadWire.Fail(ReadStatus.DestinationTooSmall, DiagnosticCode.DestinationTooSmall,
            location.AtLocal(DataField.OutputBytes, 0), out diagnostic, (ulong)actual, (ulong)expected);

    private static ReadStatus Done(out FontDiagnostic diagnostic)
    {
        diagnostic = default;
        return ReadStatus.Done;
    }

    private static void RequireWritten(ReadStatus status)
    {
        if (status != ReadStatus.Done) throw new InvalidOperationException("A preflighted EBLC write failed.");
    }
}
