using FontWeave.Core;

namespace FontWeave.Tables;

/// <summary>Strict whole-table validation and joint materialization for CBDT
/// v3.0 (card T6-05 §3). The single-table face is exactly the version gate
/// (strict {3}, minor 0 - the OQ-T6-05-1/-2 rulings) plus the 4-byte open
/// minimum: "The rest of the CBDT table is a collection of bitmap data" (D
/// L50) whose structure arrives only through CBLC indexing, so a bare pool has
/// zero single-table checks beyond the head. The per-glyph reads (formats
/// 17/18/19) are the typed face: the metrics head travels raw, dataLen is
/// counted and the payload rides as opaque bytes - never decoded,
/// chunk-inspected or recompressed (D L60's undefined-behavior line excludes
/// read-side obligations; the chunk whitelist is a container-level observation
/// key). The joint layer holds both bounded views explicitly (OQ-T6-05-12:
/// "不能由一侧 reader 猜另一侧") - a glyph image is resolved through the
/// CBLC location, then checked against both the derived slot and this pool's
/// bounds, so a lying dataLen refuses on either face.</summary>
public readonly ref partial struct CbdtView
{
    public ReadStatus ValidateTable(out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        var budget = new BuildBudget(limits);
        if (!budget.Input(_data.Length, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        return ValidateTable(out diagnostic, budget);
    }

    internal ReadStatus ValidateTable(out FontDiagnostic diagnostic, BuildBudget budget)
    {
        // The strict version gate, the sbix shape: major {3} names its
        // expectation; minor 0 is the CFF2-family face (D L45-46/48).
        ReadStatus status = TryGetMajorVersion(out ushort major, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (major != CbdtLayout.MajorVersion3)
            return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedVersion,
                Location.AtLocal(DataField.Version, 0), out diagnostic, major,
                CbdtLayout.MajorVersion3);
        status = TryGetMinorVersion(out ushort minor, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (minor != 0)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.AtLocal(DataField.Payload, 2), out diagnostic, minor, 0UL);
        // The pool has no other single-table face (D L50): zero strikes means
        // zero structure, and the 4-byte pure-head shell binds legally (the
        // OQ-T6-05-10 observation ruling; TryOpen owns the length floor).
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>Format 17's per-glyph window at the absolute pool position:
    /// the 5-byte SmallGlyphMetrics head, the counted length and the opaque
    /// payload slice.</summary>
    public ReadStatus TryGetImage17(ulong position, out CbdtSmallGlyphMetrics metrics,
        out uint dataLength, out ReadOnlySpan<byte> payload, out FontDiagnostic diagnostic)
    {
        metrics = default;
        dataLength = 0;
        payload = default;
        ReadStatus status = RequireImageWindow(position, CbdtLayout.SmallGlyphMetricsLength,
            out var head, out diagnostic);
        if (status != ReadStatus.Done) return status;
        metrics = new CbdtSmallGlyphMetrics(head[0], head[1], unchecked((sbyte)head[2]),
            unchecked((sbyte)head[3]), head[4]);
        return FinishImage(position, CbdtLayout.SmallGlyphMetricsLength, out dataLength,
            out payload, out diagnostic);
    }

    /// <summary>Format 18's per-glyph window: the 8-byte BigGlyphMetrics head,
    /// the counted length and the opaque payload slice.</summary>
    public ReadStatus TryGetImage18(ulong position, out CbdtBigGlyphMetrics metrics,
        out uint dataLength, out ReadOnlySpan<byte> payload, out FontDiagnostic diagnostic)
    {
        metrics = default;
        dataLength = 0;
        payload = default;
        ReadStatus status = RequireImageWindow(position, CblcLayout.BigGlyphMetricsLength,
            out var head, out diagnostic);
        if (status != ReadStatus.Done) return status;
        metrics = new CbdtBigGlyphMetrics(head[0], head[1], unchecked((sbyte)head[2]),
            unchecked((sbyte)head[3]), head[4], unchecked((sbyte)head[5]),
            unchecked((sbyte)head[6]), head[7]);
        return FinishImage(position, CblcLayout.BigGlyphMetricsLength, out dataLength,
            out payload, out diagnostic);
    }

    /// <summary>Format 19's per-glyph window: no metrics head (they live in
    /// the CBLC subtable, D L113) - the counted length and payload only.</summary>
    public ReadStatus TryGetImage19(ulong position, out uint dataLength,
        out ReadOnlySpan<byte> payload, out FontDiagnostic diagnostic)
    {
        dataLength = 0;
        payload = default;
        ReadStatus status = RequireImageWindow(position, 0, out _, out diagnostic);
        if (status != ReadStatus.Done) return status;
        return FinishImage(position, 0, out dataLength, out payload, out diagnostic);
    }

    /// <summary>The image's metrics head plus dataLen word must fit inside
    /// this view's bytes. The raw pool is the bound here; the derived-slot
    /// bound rides the joint read.</summary>
    private ReadStatus RequireImageWindow(ulong position, int metricsLength,
        out ReadOnlySpan<byte> metrics, out FontDiagnostic diagnostic)
    {
        metrics = default;
        if (!IsBound)
            return ReadWire.Fail(ReadStatus.Uninitialized, DiagnosticCode.UnboundView,
                Location, out diagnostic);
        if (position > (ulong)_data.Length)
            return ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.RangeOutsideSource,
                Location.AtLocal(DataField.Offset, position), out diagnostic,
                position, (ulong)_data.Length);
        if (position + (ulong)metricsLength + 4UL > (ulong)_data.Length)
            return ReadWire.Fail(ReadStatus.Truncated, DiagnosticCode.TruncatedData,
                Location.AtLocal(DataField.Payload, position), out diagnostic,
                (ulong)_data.Length - position, (ulong)metricsLength + 4UL);
        metrics = _data.Slice((int)position, metricsLength);
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>The counted length word sits right after the metrics head
    /// (D L93-120); the payload follows it to the frame's end.</summary>
    private ReadStatus FinishImage(ulong position, int metricsLength, out uint dataLength,
        out ReadOnlySpan<byte> payload, out FontDiagnostic diagnostic)
    {
        dataLength = 0;
        payload = default;
        ulong wordAt = position + (ulong)metricsLength;
        if (!TrySlice(_data, wordAt, 4, out var lengthBytes))
            return ReadWire.Fail(ReadStatus.Truncated, DiagnosticCode.TruncatedData,
                Location.AtLocal(DataField.Length, wordAt),
                out diagnostic, (ulong)_data.Length, wordAt + 4UL);
        dataLength = global::System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(lengthBytes);
        ulong payloadAt = wordAt + 4UL;
        if (dataLength > (ulong)_data.Length ||
            payloadAt > (ulong)_data.Length - dataLength)
            return ReadWire.Fail(ReadStatus.Truncated, DiagnosticCode.TruncatedData,
                Location.AtLocal(DataField.Payload, payloadAt), out diagnostic,
                (ulong)_data.Length - payloadAt, dataLength);
        payload = _data.Slice((int)payloadAt, (int)dataLength);
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>The joint glyph-image read (card §2's OQ-T6-05-12 face): the
    /// CBLC view resolves the slot's pool window and the CBDT view decodes it,
    /// both bounded views held explicitly. The dataLen word must fit the
    /// CBLC-derived slot (a length the index layer cannot carry refuses - the
    /// "metrics/PNG length 错" leg) and this pool's remaining bytes (the
    /// "index/data offset 越界" leg); the payload travels verbatim.</summary>
    public ReadStatus TryGetGlyphImage(CblcView cblc, int sizeIndex, int glyphId,
        out CbdtGlyphImage image, out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        image = default;
        var budget = new BuildBudget(limits);
        ReadStatus status = RequirePair(cblc, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        status = cblc.TryGetGlyphLocation(sizeIndex, glyphId,
            out CblcGlyphLocation location, out bool present, out ushort indexFormat,
            out ulong imageFormatPosition, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        if (!present)
        {
            // The absent form is data absence, not corruption: Done with an
            // empty image (the sbix empty-slot normalization line).
            image = new CbdtGlyphImage(0, null, []);
            return Done(out diagnostic);
        }
        if (!CblcLayout.IsImageFormatTyped(location.ImageFormat))
            return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
                cblc.Location.AtLocal(DataField.Payload, imageFormatPosition), out diagnostic,
                location.ImageFormat, 0UL);
        if (!CblcLayout.IsPairingDefined(indexFormat, location.ImageFormat))
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                cblc.Location.AtLocal(DataField.Payload, imageFormatPosition), out diagnostic,
                location.ImageFormat, 0UL);
        return ReadImageAt(location.ImageFormat, location.PositionInCbdt,
            location.ByteLength, out image, out diagnostic, budget);
    }

    private ReadStatus RequirePair(CblcView cblc, out FontDiagnostic diagnostic, BuildBudget budget)
    {
        if (!IsBound || !cblc.IsBound)
            return ReadWire.Fail(ReadStatus.InvalidSource, DiagnosticCode.WrongSourceFace,
                !IsBound ? Location : cblc.Location, out diagnostic);
        if (Location.SourceId != cblc.Location.SourceId || Location.FaceIndex != cblc.Location.FaceIndex)
            return ReadWire.Fail(ReadStatus.InvalidSource, DiagnosticCode.WrongSourceFace,
                cblc.Location, out diagnostic);
        if (!budget.Input(_data.Length, Location, out diagnostic) ||
            !budget.Input(cblc.RawBytes.Length, cblc.Location, out diagnostic)) return ReadStatus.ResourceLimit;
        return ValidateTable(out diagnostic, budget);
    }

    /// <summary>One image's decode at an absolute pool position over a
    /// CBLC-derived slot length: the metrics head, the dataLen word right
    /// after it and the payload to the frame's end, each face checked against
    /// both the slot and the pool.</summary>
    internal ReadStatus ReadImageAt(ushort imageFormat, ulong position, ulong slotLength,
        out CbdtGlyphImage image, out FontDiagnostic diagnostic, BuildBudget budget)
    {
        image = default;
        int frameHead = imageFormat switch
        {
            CblcLayout.ImageFormat17 => CbdtLayout.Format17HeadLength,
            CblcLayout.ImageFormat18 => CbdtLayout.Format18HeadLength,
            CblcLayout.ImageFormat19 => CbdtLayout.Format19HeadLength,
            _ => -1,
        };
        if (frameHead < 0)
            return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
                Location.AtLocal(DataField.Payload, position), out diagnostic,
                imageFormat, 0UL);
        if (position > (ulong)_data.Length)
            return ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.RangeOutsideSource,
                Location.AtLocal(DataField.Offset, position), out diagnostic,
                position, (ulong)_data.Length);
        if ((ulong)frameHead > (ulong)_data.Length - position)
            return ReadWire.Fail(ReadStatus.Truncated, DiagnosticCode.TruncatedData,
                Location.AtLocal(DataField.Payload, position), out diagnostic,
                (ulong)_data.Length - position, (ulong)frameHead);
        // The slot face: the CBLC-derived window must at least carry the
        // frame's head (metrics + word; a slot shorter than its own frame is
        // an index contradiction).
        if ((ulong)frameHead > slotLength)
            return ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.RangeOutsideSource,
                Location.AtLocal(DataField.Offset, position), out diagnostic,
                slotLength, (ulong)frameHead);
        ulong wordAt = position + (ulong)(frameHead - 4);
        if (!TrySlice(_data, wordAt, 4, out var lengthBytes))
            return ReadWire.Fail(ReadStatus.Truncated, DiagnosticCode.TruncatedData,
                Location.AtLocal(DataField.Length, wordAt),
                out diagnostic, (ulong)_data.Length, wordAt + 4UL);
        uint dataLength = global::System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(lengthBytes);
        ulong frame = (ulong)frameHead + dataLength;
        // The pool face: head + word + payload inside this view's bytes.
        if (frame > (ulong)_data.Length - position)
            return ReadWire.Fail(ReadStatus.Truncated, DiagnosticCode.TruncatedData,
                Location.AtLocal(DataField.Payload, wordAt + 4UL),
                out diagnostic, (ulong)_data.Length - position - (ulong)frameHead,
                dataLength);
        // The slot face: the dataLen cannot claim more than the index layer's
        // window carries (the lying-length refusal; a smaller value leaves
        // slot padding - the alignment observation, OQ-5's lean).
        if (frame > slotLength)
            return ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.RangeOutsideSource,
                Location.AtLocal(DataField.Length, wordAt), out diagnostic,
                frame, slotLength);
        int metricsLength = frameHead - 4;
        if (!budget.Buffer(metricsLength + (long)dataLength,
                Location.AtLocal(DataField.Payload, position), out diagnostic)) return ReadStatus.ResourceLimit;
        byte[]? metrics = null;
        if (imageFormat == CblcLayout.ImageFormat17)
            metrics = _data.Slice((int)position, CbdtLayout.SmallGlyphMetricsLength).ToArray();
        else if (imageFormat == CblcLayout.ImageFormat18)
            metrics = _data.Slice((int)position, CblcLayout.BigGlyphMetricsLength).ToArray();
        var payload = new byte[dataLength];
        _data.Slice((int)(position + (ulong)frameHead), (int)dataLength)
            .CopyTo(payload);
        image = new CbdtGlyphImage(imageFormat, metrics, payload);
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>The joint whole-table read (card §2's owned shape): the CBLC
    /// view validates and materializes its index, this view validates its
    /// pool, then the joint read chain (strikes in wire order, records in
    /// wire order, glyph slots ascending - skipping the absent forms)
    /// materializes every populated slot's image in first-appearance order.
    /// cbdtDataLength is this view's own length; the numGlyphs forward rides
    /// to the CBLC gate.</summary>
    public ReadStatus TryReadTable(CblcView cblc, int? numGlyphs, out CbdtTable? model,
        out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        model = null;
        var budget = new BuildBudget(limits);
        ReadStatus status = RequirePair(cblc, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        status = TryGetMajorVersion(out ushort major, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = TryGetMinorVersion(out ushort minor, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = cblc.TryReadTable(_data.Length, numGlyphs, out var cblcTable,
            out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        // The joint read chain walk: the same order the compiler emits.
        var images = new List<CbdtImage>();
        ulong consumed = CbdtLayout.HeaderLength;
        for (int i = 0; i < cblcTable!.SizeCount; i++)
        {
            var strike = cblcTable.Sizes[i];
            foreach (var subtable in strike.Subtables)
            {
                switch (subtable)
                {
                    case CblcOffsetSubtable offsets:
                        for (int g = offsets.FirstGlyphIndex; g <= offsets.LastGlyphIndex; g++)
                        {
                            status = ReadChainImage(offsets.ImageFormat,
                                (ulong)offsets.ImageDataOffset + offsets.SbitOffsets[g - offsets.FirstGlyphIndex],
                                (ulong)(offsets.SbitOffsets[g - offsets.FirstGlyphIndex + 1] -
                                    offsets.SbitOffsets[g - offsets.FirstGlyphIndex]),
                                images, budget, ref consumed, out diagnostic);
                            if (status != ReadStatus.Done) return status;
                        }
                        break;
                    case CblcImageSizeSubtable imageSize:
                        for (int g = imageSize.FirstGlyphIndex; g <= imageSize.LastGlyphIndex; g++)
                        {
                            status = ReadChainImage(imageSize.ImageFormat,
                                (ulong)imageSize.ImageDataOffset +
                                    (ulong)(g - imageSize.FirstGlyphIndex) * imageSize.ImageSize,
                                imageSize.ImageSize, images, budget, ref consumed,
                                out diagnostic);
                            if (status != ReadStatus.Done) return status;
                        }
                        break;
                    case CblcPairSubtable pairs:
                        for (int k = 0; k < pairs.GlyphIds.Length - 1; k++)
                        {
                            status = ReadChainImage(pairs.ImageFormat,
                                (ulong)pairs.ImageDataOffset + pairs.SbitOffsets[k],
                                (ulong)(pairs.SbitOffsets[k + 1] - pairs.SbitOffsets[k]),
                                images, budget, ref consumed, out diagnostic);
                            if (status != ReadStatus.Done) return status;
                        }
                        break;
                    case CblcGlyphIdArraySubtable glyphIds:
                        for (int k = 0; k < glyphIds.GlyphIds.Length; k++)
                        {
                            status = ReadChainImage(glyphIds.ImageFormat,
                                (ulong)glyphIds.ImageDataOffset + (ulong)k * glyphIds.ImageSize,
                                glyphIds.ImageSize, images, budget, ref consumed,
                                out diagnostic);
                            if (status != ReadStatus.Done) return status;
                        }
                        break;
                }
            }
        }
        if (!budget.Buffer(images.Count * (long)IntPtr.Size, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        model = new(major, minor, [.. images], Location, (int)consumed);
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>One chain slot's image: a zero length is the absent form (the
    /// equal-consecutive pair's normalization), anything else decodes through
    /// the shared face.</summary>
    private ReadStatus ReadChainImage(ushort imageFormat, ulong position, ulong length,
        List<CbdtImage> images, BuildBudget budget, ref ulong consumed,
        out FontDiagnostic diagnostic)
    {
        diagnostic = default;
        if (length == 0)
        {
            diagnostic = default;
            return ReadStatus.Done;
        }
        ReadStatus status = ReadImageAt(imageFormat, position, length,
            out var glyphImage, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        images.Add(new CbdtImage(glyphImage.ImageFormat, glyphImage.Metrics,
            glyphImage.Payload));
        consumed = Math.Max(consumed, position +
            (ulong)(glyphImage.Metrics?.Length ?? 0) + 4UL + (ulong)glyphImage.Payload.Length);
        diagnostic = default;
        return ReadStatus.Done;
    }

    private static ReadStatus Done(out FontDiagnostic diagnostic)
    {
        diagnostic = default;
        return ReadStatus.Done;
    }
}
