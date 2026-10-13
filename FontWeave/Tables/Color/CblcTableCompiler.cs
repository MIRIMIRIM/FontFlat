using FontWeave.Core;
using FontWeave.Tables.Layout;

namespace FontWeave.Tables;

/// <summary>Canonical CBLC compiler (card T6-05 §4, OQ-T6-05-13's single
/// candidate). One planned layout: header(8) → bitmapSizes (48·n, input
/// order, rows rewritten with the canonical listOffset/listSize and every
/// other field verbatim) → each strike's IndexSubtableList placed immediately
/// after the array (the corpus's 101/130 immediate majority shape), records
/// first, then the subtables packed in record order - each 4-byte aligned
/// with zero padding, and formats 3/5 growing one pad element on an odd
/// element count (E L268's writer-side obligation; the reader derives window
/// lengths without the pad, OQ-T6-05-5's observation lean).
///
/// The cross-table fixup face (the two compilers' coordination contract): the
/// images travel in joint read-chain order, so the k-th populated slot of the
/// chain (strikes in wire order, records in wire order, glyph slots
/// ascending, absent forms skipped) pairs with the k-th
/// <see cref="CbdtTable"/> image and both compilers derive identical
/// positions - the pool packs from the CBDT header's end (the E L266
/// addressing convention: the offsets address the whole table including the
/// 4-byte head), each subtable's imageDataOffset lands on its first
/// populated slot, and the sbitOffsets / pair offsets / glyph-array sizes
/// derive from the paired frames' lengths. Explicit source/face bindings must
/// agree when both inputs carry them; anonymous owned construction remains
/// available. A chain whose populated-slot count differs from the image count
/// is refused as a wrong source face. The
/// root re-answers the version domain, every row value domain, the sparse
/// orderings, the record ordering must + overlap refusal and the pairing
/// matrix; structural changes (strike/subtable/slot count changes, format
/// swaps, version writes) have no edit surface (OQ-T6-05-13's finite
/// refusals).</summary>
public static class CblcTableCompiler
{
    /// <summary>Explicit root-model factory from owned parts (card §4.1): the
    /// root re-answers every read-side face over the given strike parts and
    /// rewrites the index layer to the canonical layout against the CBDT
    /// parts' frames. The returned model is the canonical wire plan; edits
    /// re-root through the owned TryWith* faces and compile deterministically.</summary>
    public static ReadStatus TryCreate(ushort majorVersion, ushort minorVersion,
        IReadOnlyList<CblcStrike> sizes, CbdtTable cbdt, out CblcTable? model,
        out FontDiagnostic diagnostic, BuildLimits? limits = null, DataLocation location = default)
        => Create(majorVersion, minorVersion, sizes, cbdt, out model, out diagnostic,
            new BuildBudget(limits), location);

    private static ReadStatus Create(ushort majorVersion, ushort minorVersion,
        IReadOnlyList<CblcStrike> sizes, CbdtTable cbdt, out CblcTable? model,
        out FontDiagnostic diagnostic, BuildBudget budget, DataLocation location)
    {
        model = null;
        ArgumentNullException.ThrowIfNull(sizes);
        ArgumentNullException.ThrowIfNull(cbdt);
        location = location == default ? DataLocation.Anonymous(CblcLayout.Tag) : location;
        ReadStatus status = RequireVersion(majorVersion, minorVersion, location, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if ((location.SourceId != 0 || location.FaceIndex != -1) &&
            (cbdt.Location.SourceId != 0 || cbdt.Location.FaceIndex != -1) &&
            (location.SourceId != cbdt.Location.SourceId || location.FaceIndex != cbdt.Location.FaceIndex))
            return ReadWire.Fail(ReadStatus.InvalidSource, DiagnosticCode.WrongSourceFace,
                cbdt.Location, out diagnostic);
        status = CbdtTableCompiler.RequireVersion(cbdt.MajorVersion, cbdt.MinorVersion, cbdt.Location, out diagnostic);
        if (status != ReadStatus.Done) return status;
        var images = cbdt.Images;
        int sizeCount = sizes.Count, imageCount = cbdt.ImageCount;
        status = CbdtTableCompiler.CheckImages(images, cbdt.Location, budget, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (!budget.Work(sizeCount * 48L + imageCount * 8L, location, out diagnostic) ||
            !budget.Buffer(imageCount * 8L + sizeCount * (long)IntPtr.Size, location, out diagnostic))
            return ReadStatus.ResourceLimit;
        var frames = new long[imageCount];
        var canonical = new CblcStrike[sizeCount];
        long packedLength = CbdtLayout.HeaderLength;
        for (int i = 0; i < imageCount; i++)
        {
            frames[i] = CbdtTableCompiler.FrameLength(images[i]);
            packedLength += frames[i];
        }
        long chainCount = 0;
        for (int s = 0; s < sizeCount; s++)
        {
            var strike = sizes[s];
            status = RequireRowValues(strike, location, out diagnostic);
            if (status != ReadStatus.Done) return status;
            int recordCount = strike.Subtables.Count;
            if (!budget.Work(recordCount, location, out diagnostic)) return ReadStatus.ResourceLimit;
            ushort previousLast = 0;
            for (int r = 0; r < recordCount; r++)
            {
                var part = strike.Subtables[r];
                status = CheckSubtable(part, location, budget, out diagnostic);
                if (status != ReadStatus.Done) return status;
                if (r > 0 && part.FirstGlyphIndex <= previousLast)
                    return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.OverlappingRange,
                        location.AtLocal(DataField.GlyphId, 0, r), out diagnostic, part.FirstGlyphIndex, previousLast);
                previousLast = part.LastGlyphIndex;
                chainCount += CountSlots(part);
            }
        }
        if (chainCount != imageCount)
            return WrongSource(location, chainCount, imageCount, out diagnostic);
        long layoutCursor = CblcLayout.BitmapSizeAt + 48L * sizeCount;
        long poolCursor = CbdtLayout.HeaderLength;
        int imageCursor = 0;
        for (int s = 0; s < sizeCount; s++)
        {
            var source = sizes[s];
            int recordCount = source.Subtables.Count;
            if (!budget.Buffer(recordCount * (long)IntPtr.Size, location, out diagnostic))
                return ReadStatus.ResourceLimit;
            var subtables = new CblcIndexSubtable[recordCount];
            int firstImage = imageCursor;
            bool composite = false;
            for (int r = 0; r < recordCount; r++)
            {
                var part = source.Subtables[r];
                composite |= part.ImageFormat is 8 or 9;
                status = RewriteSubtable(part, source.BitDepth, images, frames, ref imageCursor,
                    ref poolCursor, packedLength, location, budget, out var rewritten, out diagnostic);
                if (status != ReadStatus.Done) return status;
                subtables[r] = rewritten;
            }
            int strikeImages = imageCursor - firstImage;
            if (composite && strikeImages != 0)
            {
                if (!budget.Buffer(2L * strikeImages, cbdt.Location, out diagnostic)) return ReadStatus.ResourceLimit;
                var glyphIds = new ushort[strikeImages];
                FillGlyphIds(subtables, glyphIds);
                status = CbdtTableCompiler.CheckCycles(images, firstImage, glyphIds, cbdt.Location, budget, out diagnostic);
                if (status != ReadStatus.Done) return status;
            }
            long listStart = layoutCursor;
            layoutCursor += 8L * recordCount;
            foreach (var subtable in subtables) layoutCursor += (long)Align4(SubtableLength(subtable));
            if (layoutCursor > int.MaxValue)
                return ReadWire.Fail(ReadStatus.ResourceLimit, DiagnosticCode.BudgetExceeded,
                    location.AtLocal(DataField.OutputBytes, 0), out diagnostic, (ulong)layoutCursor, int.MaxValue);
            canonical[s] = new CblcStrike((uint)listStart, (uint)(layoutCursor - listStart),
                source.ColorRef, source.HoriMetrics, source.VertMetrics,
                source.StartGlyphIndex, source.EndGlyphIndex, source.PpemX, source.PpemY,
                source.BitDepth, source.Flags, subtables);
        }
        model = new(majorVersion, minorVersion, canonical, location, (int)layoutCursor);
        diagnostic = default;
        return ReadStatus.Done;
    }

    public static ReadStatus TryCompilePair(CblcTable model, CbdtTable cbdt,
        out byte[]? cblcBytes, out byte[]? cbdtBytes, out FontDiagnostic diagnostic,
        BuildLimits? limits = null)
    {
        cblcBytes = null;
        cbdtBytes = null;
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(cbdt);
        var budget = new BuildBudget(limits);
        ReadStatus status = Create(model.MajorVersion, model.MinorVersion, model.Sizes, cbdt,
            out var canonical, out diagnostic, budget, model.Location);
        if (status != ReadStatus.Done) return status;
        long total = canonical!.ConsumedLength + CbdtTableCompiler.PackedLength(cbdt);
        if (!budget.Output(total, model.Location, out diagnostic)) return ReadStatus.ResourceLimit;
        status = Compile(canonical, out var indexBytes, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        status = CbdtTableCompiler.Compile(cbdt, out var imageBytes, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        cblcBytes = indexBytes;
        cbdtBytes = imageBytes;
        diagnostic = default;
        return ReadStatus.Done;
    }

    public static ReadStatus TryWritePair(CblcTable model, CbdtTable cbdt,
        Span<byte> cblcDestination, Span<byte> cbdtDestination,
        out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        ReadStatus status = TryCompilePair(model, cbdt, out var indexBytes, out var imageBytes,
            out diagnostic, limits);
        if (status != ReadStatus.Done) return status;
        if (cblcDestination.Length < indexBytes!.Length)
            return ReadWire.Fail(ReadStatus.DestinationTooSmall, DiagnosticCode.DestinationTooSmall,
                model.Location.AtLocal(DataField.OutputBytes, 0), out diagnostic,
                (ulong)cblcDestination.Length, (ulong)indexBytes.Length);
        if (cbdtDestination.Length < imageBytes!.Length)
            return ReadWire.Fail(ReadStatus.DestinationTooSmall, DiagnosticCode.DestinationTooSmall,
                cbdt.Location.AtLocal(DataField.OutputBytes, 0), out diagnostic,
                (ulong)cbdtDestination.Length, (ulong)imageBytes.Length);
        indexBytes.AsSpan().CopyTo(cblcDestination);
        imageBytes.AsSpan().CopyTo(cbdtDestination);
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>Deterministic write through the same planned emission; every
    /// budget admission completes before any byte lands, and a short
    /// destination leaves the span untouched (the T5-05 atomicity
    /// discipline).</summary>
    public static ReadStatus TryWrite(CblcTable model, Span<byte> destination,
        out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        ReadStatus status = TryCompile(model, out byte[]? encoded, out diagnostic, limits);
        if (status != ReadStatus.Done) return status;
        if (destination.Length < encoded!.Length)
            return ReadWire.Fail(ReadStatus.DestinationTooSmall, DiagnosticCode.DestinationTooSmall,
                model.Location.AtLocal(DataField.OutputBytes, 0), out diagnostic,
                (ulong)destination.Length, (uint)encoded.Length);
        encoded.AsSpan().CopyTo(destination);
        diagnostic = default;
        return ReadStatus.Done;
    }

    public static ReadStatus TryCompile(CblcTable model, out byte[]? bytes,
        out FontDiagnostic diagnostic, BuildLimits? limits = null)
        => Compile(model, out bytes, out diagnostic, new BuildBudget(limits));

    private static ReadStatus Compile(CblcTable model, out byte[]? bytes,
        out FontDiagnostic diagnostic, BuildBudget budget)
    {
        bytes = null;
        ArgumentNullException.ThrowIfNull(model);
        // The compile-time re-answers (a hand-built model cannot smuggle a
        // refused shape past the emission): the version domain and every row
        // value domain; the index layer's faces rode TryCreate's rewrite.
        ReadStatus status = RequireVersion(model.MajorVersion, model.MinorVersion,
            model.Location, out diagnostic);
        if (status != ReadStatus.Done) return status;
        long length = CblcLayout.BitmapSizeAt + 48L * model.SizeCount;
        foreach (var strike in model.Sizes)
        {
            status = RequireRowValues(strike, model.Location, out diagnostic);
            if (status != ReadStatus.Done) return status;
            length += 8L * strike.Subtables.Count;
            foreach (var subtable in strike.Subtables)
            {
                status = CheckSubtable(subtable, model.Location, budget, out diagnostic);
                if (status != ReadStatus.Done) return status;
                length += (long)Align4(SubtableLength(subtable));
            }
        }
        if (!budget.Work(length, model.Location, out diagnostic) ||
            !budget.Buffer(length, model.Location, out diagnostic) ||
            !budget.Output(length, model.Location, out diagnostic))
            return ReadStatus.ResourceLimit;
        var output = new byte[length];
        RequireWritten(CblcView.TryWriteMajorVersion(output, model.MajorVersion,
            out _, model.Location));
        RequireWritten(CblcView.TryWriteMinorVersion(output, model.MinorVersion,
            out _, model.Location));
        RequireWritten(CblcView.TryWriteNumSizes(output, (uint)model.SizeCount,
            out _, model.Location));
        ulong listStart = CblcLayout.BitmapSizeAt + 48UL * (uint)model.SizeCount;
        for (int s = 0; s < model.SizeCount; s++)
        {
            var strike = model.Sizes[s];
            ulong listLength = 8UL * (uint)strike.Subtables.Count;
            foreach (var subtable in strike.Subtables)
                listLength += Align4(SubtableLength(subtable));
            RequireWritten(CblcView.TryWriteBitmapSizeAt(output,
                CblcLayout.BitmapSizeAt + 48UL * (uint)s,
                Row(s, strike, (uint)listStart, (uint)listLength),
                out _, model.Location));
            ulong at = listStart;
            for (int r = 0; r < strike.Subtables.Count; r++)
            {
                var subtable = strike.Subtables[r];
                W16(output, (int)at, subtable.FirstGlyphIndex);
                W16(output, (int)at + 2, subtable.LastGlyphIndex);
                W32(output, (int)at + 4, RecordOffset(strike.Subtables, r));
                at += CblcLayout.IndexSubtableRecordStride;
            }
            ulong body = listStart +
                (ulong)strike.Subtables.Count * CblcLayout.IndexSubtableRecordStride;
            for (int r = 0; r < strike.Subtables.Count; r++)
            {
                body = Align4(body);
                WriteSubtable(output, (int)body, strike.Subtables[r]);
                body += SubtableLength(strike.Subtables[r]);
            }
            listStart += listLength;
        }
        budget.RecordCompiledTable(output.Length);
        bytes = output;
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>One subtable's canonical rewrite: the paired frames' lengths
    /// drive the fresh offset run - the pool packs from the CBDT header's
    /// end, the subtable's imageDataOffset lands on its first populated
    /// slot, and the offsets/pairs/sizes derive from the cumulative frame
    /// positions. The index format, the glyph ranges and the sparse glyph
    /// sets travel from the owned part (re-answered for order); the image
    /// positions never do.</summary>
    private static ReadStatus RewriteSubtable(CblcIndexSubtable source, byte bitDepth,
        IReadOnlyList<CbdtImage> images, long[] frames,
        ref int imageCursor, ref long poolCursor, long packedLength, DataLocation location,
        BuildBudget budget, out CblcIndexSubtable rewritten, out FontDiagnostic diagnostic)
    {
        rewritten = null!;
        diagnostic = default;
        if (source.FirstGlyphIndex > source.LastGlyphIndex)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                location.AtLocal(DataField.GlyphId, 0, source.Ordinal), out diagnostic,
                source.FirstGlyphIndex, source.LastGlyphIndex);
        switch (source)
        {
            case CblcOffsetSubtable offsets:
            {
                int span = offsets.SbitOffsets.Length - 1;
                if (!budget.Buffer(4L * offsets.SbitOffsets.Length, location, out diagnostic))
                    return ReadStatus.ResourceLimit;
                var fresh = new uint[offsets.SbitOffsets.Length];
                uint run = 0;
                long firstAt = -1;
                for (int k = 0; k < span; k++)
                {
                    fresh[k] = run;
                    // The source's populated pattern (a non-equal pair) pairs
                    // with the next image; the absent form stays absent.
                    if (offsets.SbitOffsets[k] != offsets.SbitOffsets[k + 1])
                    {
                        firstAt = firstAt < 0 ? poolCursor : firstAt;
                        ReadStatus status = NextFrame(source, bitDepth, images, frames,
                            ref imageCursor, ref poolCursor, location, out long frame, out diagnostic);
                        if (status != ReadStatus.Done) return status;
                        ulong next = run + (ulong)frame;
                        ulong limit = source.IndexFormat == CblcLayout.IndexFormat3 ? ushort.MaxValue : uint.MaxValue;
                        if (next > limit) return OffsetOverflow(location, source.Ordinal, next, limit, out diagnostic);
                        run = (uint)next;
                    }
                }
                fresh[span] = run;
                rewritten = new CblcOffsetSubtable(source.Ordinal, source.IndexFormat,
                    source.ImageFormat, (uint)(firstAt < 0 ? poolCursor : firstAt), source.FirstGlyphIndex,
                    source.LastGlyphIndex, fresh);
                diagnostic = default;
                return ReadStatus.Done;
            }
            case CblcImageSizeSubtable imageSize:
            {
                int span = source.LastGlyphIndex - source.FirstGlyphIndex + 1;
                if (imageSize.ImageSize == 0)
                {
                    rewritten = new CblcImageSizeSubtable(source.Ordinal, source.ImageFormat,
                        (uint)poolCursor, source.FirstGlyphIndex, source.LastGlyphIndex, 0, imageSize.BigMetrics);
                    return Done(out diagnostic);
                }
                if (imageCursor + span > frames.Length)
                    return WrongSource(location, imageCursor, frames.Length,
                        out diagnostic);
                long firstAt = poolCursor;
                uint size = (uint)frames[imageCursor];
                for (int k = 0; k < span; k++)
                {
                    // The constant-imageSize face: every slot's frame must
                    // carry the same length (a hand-built non-uniform part
                    // has no canonical emission).
                    ReadStatus status = NextFrame(source, bitDepth, images, frames,
                        ref imageCursor, ref poolCursor, location, out long frame, out diagnostic);
                    if (status != ReadStatus.Done) return status;
                    if (frame != size)
                        return ReadWire.Fail(ReadStatus.InvalidFormat,
                            DiagnosticCode.InvalidValue,
                            location.AtLocal(DataField.Length, 0, source.Ordinal),
                            out diagnostic, (ulong)frame, size);
                }
                rewritten = new CblcImageSizeSubtable(source.Ordinal, source.ImageFormat,
                    (uint)firstAt, source.FirstGlyphIndex, source.LastGlyphIndex, size,
                    imageSize.BigMetrics);
                diagnostic = default;
                return ReadStatus.Done;
            }
            case CblcPairSubtable pairs:
            {
                int count = pairs.GlyphIds.Length - 1;
                if (!budget.Buffer(2L * pairs.SbitOffsets.Length, location, out diagnostic))
                    return ReadStatus.ResourceLimit;
                var freshOffsets = new ushort[pairs.SbitOffsets.Length];
                uint run = 0;
                long firstAt = -1;
                for (int k = 0; k < count; k++)
                {
                    if (k > 0 && pairs.GlyphIds[k] <= pairs.GlyphIds[k - 1])
                        return ReadWire.Fail(ReadStatus.InvalidFormat,
                            DiagnosticCode.InvalidValue,
                            location.AtLocal(DataField.GlyphId, 0, source.Ordinal),
                            out diagnostic, pairs.GlyphIds[k], pairs.GlyphIds[k - 1]);
                    freshOffsets[k] = (ushort)run;
                    if (pairs.SbitOffsets[k] != pairs.SbitOffsets[k + 1])
                    {
                        firstAt = firstAt < 0 ? poolCursor : firstAt;
                        ReadStatus status = NextFrame(source, bitDepth, images, frames,
                            ref imageCursor, ref poolCursor, location, out long frame, out diagnostic);
                        if (status != ReadStatus.Done) return status;
                        ulong next = run + (ulong)frame;
                        if (next > ushort.MaxValue)
                            return OffsetOverflow(location, source.Ordinal, next, ushort.MaxValue, out diagnostic);
                        run = (uint)next;
                    }
                }
                if (count > 0 && pairs.GlyphIds[count] <= pairs.GlyphIds[count - 1])
                    return ReadWire.Fail(ReadStatus.InvalidFormat,
                        DiagnosticCode.InvalidValue,
                        location.AtLocal(DataField.GlyphId, 0, source.Ordinal),
                        out diagnostic, pairs.GlyphIds[count], pairs.GlyphIds[count - 1]);
                freshOffsets[count] = (ushort)run;
                rewritten = new CblcPairSubtable(source.Ordinal, source.ImageFormat,
                    (uint)(firstAt < 0 ? poolCursor : firstAt), source.FirstGlyphIndex, source.LastGlyphIndex,
                    pairs.GlyphIds, freshOffsets);
                diagnostic = default;
                return ReadStatus.Done;
            }
            case CblcGlyphIdArraySubtable glyphIds:
            {
                int count = glyphIds.GlyphIds.Length;
                if (glyphIds.ImageSize == 0 || count == 0)
                {
                    rewritten = new CblcGlyphIdArraySubtable(source.Ordinal, source.ImageFormat,
                        (uint)poolCursor, source.FirstGlyphIndex, source.LastGlyphIndex, 0,
                        glyphIds.BigMetrics, glyphIds.GlyphIds);
                    return Done(out diagnostic);
                }
                if (imageCursor + count > frames.Length)
                    return WrongSource(location, imageCursor, frames.Length,
                        out diagnostic);
                for (int k = 0; k < count; k++)
                {
                    if (k > 0 && glyphIds.GlyphIds[k] <= glyphIds.GlyphIds[k - 1])
                        return ReadWire.Fail(ReadStatus.InvalidFormat,
                            DiagnosticCode.InvalidValue,
                            location.AtLocal(DataField.GlyphId, 0, source.Ordinal),
                            out diagnostic, glyphIds.GlyphIds[k],
                            glyphIds.GlyphIds[k - 1]);
                }
                long firstAt = poolCursor;
                uint size = (uint)frames[imageCursor];
                for (int k = 0; k < count; k++)
                {
                    ReadStatus status = NextFrame(source, bitDepth, images, frames,
                        ref imageCursor, ref poolCursor, location, out long frame, out diagnostic);
                    if (status != ReadStatus.Done) return status;
                    if (frame != size)
                        return ReadWire.Fail(ReadStatus.InvalidFormat,
                            DiagnosticCode.InvalidValue,
                            location.AtLocal(DataField.Length, 0, source.Ordinal),
                            out diagnostic, (ulong)frame, size);
                }
                rewritten = new CblcGlyphIdArraySubtable(source.Ordinal,
                    source.ImageFormat, (uint)firstAt, source.FirstGlyphIndex,
                    source.LastGlyphIndex, size, glyphIds.BigMetrics, glyphIds.GlyphIds);
                diagnostic = default;
                return ReadStatus.Done;
            }
            default:
                return ReadWire.Fail(ReadStatus.UnsupportedFormat,
                    DiagnosticCode.UnsupportedVersion,
                    location.AtLocal(DataField.Payload, 0, source.Ordinal), out diagnostic,
                    source.IndexFormat, 0UL);
            }
    }

    /// <summary>The chain's populated-slot count over the source parts: the
    /// non-equal offset pairs (formats 1/3), the full contiguous spans
    /// (format 2), the non-equal pair prefixes (format 4) and the whole
    /// glyph arrays (format 5).</summary>
    internal static int CountSlots(CblcIndexSubtable subtable) => subtable switch
    {
        CblcOffsetSubtable offsets => CountPopulated(offsets.SbitOffsets),
        CblcImageSizeSubtable imageSize => imageSize.ImageSize == 0 ? 0 :
            imageSize.LastGlyphIndex - imageSize.FirstGlyphIndex + 1,
        CblcPairSubtable pairs => CountPopulated(pairs.SbitOffsets),
        CblcGlyphIdArraySubtable glyphIds => glyphIds.ImageSize == 0 ? 0 : glyphIds.GlyphIds.Length,
        _ => 0,
    };

    internal static void FillGlyphIds(IReadOnlyList<CblcIndexSubtable> subtables, Span<ushort> glyphIds)
    {
        int at = 0;
        foreach (var part in subtables)
        {
            switch (part)
            {
                case CblcOffsetSubtable offsets:
                    for (int k = 0; k < offsets.SbitOffsets.Length - 1; k++)
                        if (offsets.SbitOffsets[k] != offsets.SbitOffsets[k + 1])
                            glyphIds[at++] = (ushort)(offsets.FirstGlyphIndex + k);
                    break;
                case CblcImageSizeSubtable size when size.ImageSize != 0:
                    for (int g = size.FirstGlyphIndex; g <= size.LastGlyphIndex; g++) glyphIds[at++] = (ushort)g;
                    break;
                case CblcPairSubtable pairs:
                    for (int k = 0; k < pairs.GlyphIds.Length - 1; k++)
                        if (pairs.SbitOffsets[k] != pairs.SbitOffsets[k + 1]) glyphIds[at++] = pairs.GlyphIds[k];
                    break;
                case CblcGlyphIdArraySubtable ids when ids.ImageSize != 0:
                    ids.GlyphIds.AsSpan().CopyTo(glyphIds.Slice(at));
                    at += ids.GlyphIds.Length;
                    break;
            }
        }
    }

    private static ReadStatus NextFrame(CblcIndexSubtable source, byte bitDepth,
        IReadOnlyList<CbdtImage> images, long[] frames, ref int imageCursor, ref long poolCursor,
        DataLocation location, out long frame, out FontDiagnostic diagnostic)
    {
        frame = 0;
        if (imageCursor >= frames.Length) return WrongSource(location, imageCursor, frames.Length, out diagnostic);
        var image = images[imageCursor];
        if (image.ImageFormat != source.ImageFormat)
            return ReadWire.Fail(ReadStatus.InvalidSource, DiagnosticCode.WrongSourceFace,
                location.AtLocal(DataField.Payload, 0, source.Ordinal), out diagnostic,
                image.ImageFormat, source.ImageFormat);
        if (CblcLayout.IsImageFormatInherited(image.ImageFormat) && image.BitDepth != bitDepth)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                location.AtLocal(DataField.Payload, 0, source.Ordinal), out diagnostic, image.BitDepth, bitDepth);
        frame = frames[imageCursor];
        if (frame == 0)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                location.AtLocal(DataField.Length, 0, source.Ordinal), out diagnostic, 0UL, 1UL);
        if ((ulong)poolCursor + (ulong)frame > uint.MaxValue)
            return OffsetOverflow(location, source.Ordinal, (ulong)poolCursor + (ulong)frame, uint.MaxValue, out diagnostic);
        poolCursor += frame;
        imageCursor++;
        return Done(out diagnostic);
    }

    private static ReadStatus OffsetOverflow(DataLocation location, int ordinal, ulong actual,
        ulong expected, out FontDiagnostic diagnostic) =>
        ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.RangeOutsideSource,
            location.AtLocal(DataField.Offset, 0, ordinal), out diagnostic, actual, expected);

    private static ReadStatus CheckSubtable(CblcIndexSubtable part, DataLocation location,
        BuildBudget budget, out FontDiagnostic diagnostic)
    {
        if (!CblcLayout.IsIndexFormatDefined(part.IndexFormat) || !CblcLayout.IsImageFormatTyped(part.ImageFormat))
            return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
                location.AtLocal(DataField.Payload, 0, part.Ordinal), out diagnostic,
                !CblcLayout.IsIndexFormatDefined(part.IndexFormat) ? part.IndexFormat : part.ImageFormat, 0UL);
        if (!CblcLayout.IsPairingDefined(part.IndexFormat, part.ImageFormat))
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                location.AtLocal(DataField.Payload, 0, part.Ordinal), out diagnostic, part.ImageFormat, 0UL);
        if (part.FirstGlyphIndex > part.LastGlyphIndex)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                location.AtLocal(DataField.GlyphId, 0, part.Ordinal), out diagnostic,
                part.FirstGlyphIndex, part.LastGlyphIndex);
        int span = part.LastGlyphIndex - part.FirstGlyphIndex + 1;
        switch (part)
        {
            case CblcOffsetSubtable offsets:
                if (offsets.IndexFormat is not (1 or 3) || offsets.SbitOffsets.Length != span + 1)
                    return InvalidLength(location, part.Ordinal, offsets.SbitOffsets.Length, span + 1, out diagnostic);
                if (!budget.Work(offsets.SbitOffsets.Length, location, out diagnostic)) return ReadStatus.ResourceLimit;
                for (int k = 1; k < offsets.SbitOffsets.Length; k++)
                {
                    uint current = offsets.SbitOffsets[k], previous = offsets.SbitOffsets[k - 1];
                    if (current < previous || offsets.IndexFormat == 3 && current > ushort.MaxValue)
                        return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                            location.AtLocal(DataField.Offset, 0, k), out diagnostic, current,
                            current < previous ? previous : ushort.MaxValue);
                }
                break;
            case CblcImageSizeSubtable size:
                if (size.BigMetrics.Length != 8) return InvalidLength(location, part.Ordinal, size.BigMetrics.Length, 8, out diagnostic);
                break;
            case CblcPairSubtable pairs:
                if (pairs.GlyphIds.Length == 0 || pairs.SbitOffsets.Length != pairs.GlyphIds.Length)
                    return InvalidLength(location, part.Ordinal, pairs.SbitOffsets.Length, Math.Max(1, pairs.GlyphIds.Length), out diagnostic);
                if (!budget.Work(pairs.GlyphIds.Length, location, out diagnostic)) return ReadStatus.ResourceLimit;
                for (int k = 0; k < pairs.GlyphIds.Length; k++)
                {
                    if (k > 0 && (pairs.GlyphIds[k] <= pairs.GlyphIds[k - 1] || pairs.SbitOffsets[k] < pairs.SbitOffsets[k - 1]))
                        return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                            location.AtLocal(DataField.GlyphId, 0, k), out diagnostic, pairs.GlyphIds[k], pairs.GlyphIds[k - 1]);
                    if (k < pairs.GlyphIds.Length - 1 && (pairs.GlyphIds[k] < part.FirstGlyphIndex || pairs.GlyphIds[k] > part.LastGlyphIndex))
                        return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                            location.AtLocal(DataField.GlyphId, 0, k), out diagnostic, pairs.GlyphIds[k], part.LastGlyphIndex);
                }
                break;
            case CblcGlyphIdArraySubtable ids:
                if (ids.BigMetrics.Length != 8) return InvalidLength(location, part.Ordinal, ids.BigMetrics.Length, 8, out diagnostic);
                if (!budget.Work(ids.GlyphIds.Length, location, out diagnostic)) return ReadStatus.ResourceLimit;
                for (int k = 0; k < ids.GlyphIds.Length; k++)
                {
                    if (ids.GlyphIds[k] < part.FirstGlyphIndex || ids.GlyphIds[k] > part.LastGlyphIndex ||
                        k > 0 && ids.GlyphIds[k] <= ids.GlyphIds[k - 1])
                        return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                            location.AtLocal(DataField.GlyphId, 0, k), out diagnostic, ids.GlyphIds[k], part.LastGlyphIndex);
                }
                break;
            default:
                return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
                    location.AtLocal(DataField.Payload, 0, part.Ordinal), out diagnostic, part.IndexFormat, 0UL);
        }
        return Done(out diagnostic);
    }

    private static ReadStatus InvalidLength(DataLocation location, int ordinal, int actual,
        int expected, out FontDiagnostic diagnostic) =>
        ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
            location.AtLocal(DataField.Length, 0, ordinal), out diagnostic, (ulong)actual, (ulong)expected);

    private static ReadStatus Done(out FontDiagnostic diagnostic)
    {
        diagnostic = default;
        return ReadStatus.Done;
    }

    private static int CountPopulated<T>(T[] offsets) where T : IEquatable<T>
    {
        int count = 0;
        for (int k = 0; k < offsets.Length - 1; k++)
            if (!offsets[k].Equals(offsets[k + 1])) count++;
        return count;
    }

    private static CblcView.BitmapSizeData Row(int ordinal, CblcStrike strike,
        uint listOffset, uint listSize)
    {
        var hori = strike.HoriMetrics;
        var vert = strike.VertMetrics;
        return new CblcView.BitmapSizeData(ordinal, listOffset,
            listSize, (uint)strike.Subtables.Count, strike.ColorRef,
            unchecked((sbyte)hori[0]), unchecked((sbyte)hori[1]), hori[2],
            unchecked((sbyte)hori[3]), unchecked((sbyte)hori[4]), unchecked((sbyte)hori[5]),
            unchecked((sbyte)hori[6]), unchecked((sbyte)hori[7]), unchecked((sbyte)hori[8]),
            unchecked((sbyte)hori[9]), unchecked((sbyte)hori[10]), unchecked((sbyte)hori[11]),
            unchecked((sbyte)vert[0]), unchecked((sbyte)vert[1]), vert[2],
            unchecked((sbyte)vert[3]), unchecked((sbyte)vert[4]), unchecked((sbyte)vert[5]),
            unchecked((sbyte)vert[6]), unchecked((sbyte)vert[7]), unchecked((sbyte)vert[8]),
            unchecked((sbyte)vert[9]), unchecked((sbyte)vert[10]), unchecked((sbyte)vert[11]),
            strike.StartGlyphIndex, strike.EndGlyphIndex, strike.PpemX, strike.PpemY,
            strike.BitDepth, strike.Flags);
    }

    private static uint RecordOffset(IReadOnlyList<CblcIndexSubtable> subtables,
        int recordOrdinal)
    {
        ulong at = (ulong)subtables.Count * CblcLayout.IndexSubtableRecordStride;
        for (int r = 0; r < recordOrdinal; r++)
        {
            at = Align4(at);
            at += SubtableLength(subtables[r]);
        }
        return checked((uint)Align4(at));
    }

    /// <summary>One subtable's canonical body length including its format's
    /// pad element (formats 3/5 grow two bytes on an odd element count - the
    /// pad rides the emission, never the read-side window formulas).</summary>
    private static ulong SubtableLength(CblcIndexSubtable subtable) => subtable switch
    {
        CblcOffsetSubtable offsets when subtable.IndexFormat == CblcLayout.IndexFormat3 =>
            (ulong)CblcLayout.IndexSubHeaderLength +
                2UL * (ulong)offsets.SbitOffsets.Length +
                2UL * (ulong)(offsets.SbitOffsets.Length % 2),
        CblcGlyphIdArraySubtable glyphIds =>
            (ulong)CblcLayout.IndexSubHeaderLength + 4UL +
                (ulong)CblcLayout.BigGlyphMetricsLength + 4UL +
                2UL * (ulong)glyphIds.GlyphIds.Length +
                2UL * (ulong)(glyphIds.GlyphIds.Length % 2),
        CblcOffsetSubtable offsets =>
            (ulong)CblcLayout.IndexSubHeaderLength +
                4UL * (ulong)offsets.SbitOffsets.Length,
        CblcImageSizeSubtable => (ulong)CblcLayout.IndexSubHeaderLength + 4UL +
            (ulong)CblcLayout.BigGlyphMetricsLength,
        CblcPairSubtable pairs => (ulong)CblcLayout.IndexSubHeaderLength + 4UL +
            4UL * (ulong)pairs.GlyphIds.Length,
        _ => 0UL,
    };

    private static ulong Align4(ulong value) => (value + 3UL) & ~3UL;

    /// <summary>One subtable's canonical emission: the 8-byte header (the
    /// index format, the image format and the rewritten imageDataOffset ride
    /// the typed part) plus the format body over its pad-grown length.</summary>
    private static void WriteSubtable(byte[] output, int at, CblcIndexSubtable subtable)
    {
        W16(output, at, subtable.IndexFormat);
        W16(output, at + 2, subtable.ImageFormat);
        W32(output, at + 4, subtable.ImageDataOffset);
        switch (subtable)
        {
            case CblcOffsetSubtable offsets:
            {
                int elementWidth = subtable.IndexFormat == CblcLayout.IndexFormat1 ? 4 : 2;
                for (int k = 0; k < offsets.SbitOffsets.Length; k++)
                {
                    if (elementWidth == 4)
                        W32(output, at + 8 + 4 * k, offsets.SbitOffsets[k]);
                    else
                        W16(output, at + 8 + 2 * k, (ushort)offsets.SbitOffsets[k]);
                }
                break;
            }
            case CblcImageSizeSubtable imageSize:
                W32(output, at + 8, imageSize.ImageSize);
                imageSize.BigMetrics.AsSpan().CopyTo(output.AsSpan(at + 12));
                break;
            case CblcPairSubtable pairs:
                W32(output, at + 8, (uint)(pairs.GlyphIds.Length - 1));
                for (int k = 0; k < pairs.GlyphIds.Length; k++)
                {
                    W16(output, at + 12 + 4 * k, pairs.GlyphIds[k]);
                    W16(output, at + 14 + 4 * k, pairs.SbitOffsets[k]);
                }
                break;
            case CblcGlyphIdArraySubtable glyphIds:
                W32(output, at + 8, glyphIds.ImageSize);
                glyphIds.BigMetrics.AsSpan().CopyTo(output.AsSpan(at + 12));
                W32(output, at + 20, (uint)glyphIds.GlyphIds.Length);
                for (int k = 0; k < glyphIds.GlyphIds.Length; k++)
                    W16(output, at + 24 + 2 * k, glyphIds.GlyphIds[k]);
                break;
        }
    }

    private static ReadStatus WrongSource(DataLocation location, long actual, int expected,
        out FontDiagnostic diagnostic) =>
        ReadWire.Fail(ReadStatus.InvalidSource, DiagnosticCode.WrongSourceFace,
            location.AtLocal(DataField.Count, 0), out diagnostic,
            (ulong)actual, (ulong)expected);

    private static ReadStatus RequireVersion(ushort majorVersion, ushort minorVersion,
        DataLocation location, out FontDiagnostic diagnostic)
    {
        if (majorVersion != CblcLayout.MajorVersion3)
            return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedVersion,
                location.AtLocal(DataField.Version, 0), out diagnostic, majorVersion,
                CblcLayout.MajorVersion3);
        if (minorVersion != 0)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                location.AtLocal(DataField.Payload, 2), out diagnostic, minorVersion, 0UL);
        diagnostic = default;
        return ReadStatus.Done;
    }

    private static ReadStatus RequireRowValues(CblcStrike strike, DataLocation location,
        out FontDiagnostic diagnostic)
    {
        if (strike.HoriMetrics.Length != 12) return InvalidLength(location, 0, strike.HoriMetrics.Length, 12, out diagnostic);
        if (strike.VertMetrics.Length != 12) return InvalidLength(location, 0, strike.VertMetrics.Length, 12, out diagnostic);
        if (strike.StartGlyphIndex > strike.EndGlyphIndex)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                location.AtLocal(DataField.GlyphId, 0), out diagnostic,
                strike.StartGlyphIndex, strike.EndGlyphIndex);
        if (strike.ColorRef != 0)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                location.AtLocal(DataField.Payload, 0), out diagnostic,
                strike.ColorRef, 0UL);
        if ((strike.Flags & CblcLayout.FlagsReservedMask) != 0)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                location.AtLocal(DataField.Flags, 0), out diagnostic,
                (ulong)(strike.Flags & CblcLayout.FlagsReservedMask), 0UL);
        if (!CblcLayout.IsBitDepthDefined(strike.BitDepth))
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                location.AtLocal(DataField.Payload, 0), out diagnostic,
                strike.BitDepth, 0UL);
        diagnostic = default;
        return ReadStatus.Done;
    }

    private static void W16(byte[] output, int at, ushort value)
    {
        output[at] = (byte)(value >> 8);
        output[at + 1] = unchecked((byte)value);
    }

    private static void W32(byte[] output, int at, uint value)
    {
        W16(output, at, (ushort)(value >> 16));
        W16(output, at + 2, unchecked((ushort)value));
    }

    private static void RequireWritten(ReadStatus status)
    {
        if (status != ReadStatus.Done)
            throw new InvalidOperationException("A preflighted CBLC write failed.");
    }
}
