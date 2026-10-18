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
        // The version gate, the sbix shape: the read face is {2, 3} — 3.0 names
        // its expectation and 2.0 rides as the EBLC generational alias (the
        // 2026-10-18 version-gate briefing lean, user-approved; the OQ-T6-05-1
        // strict-{3} ruling is superseded); minor 0 is the CFF2-family face
        // (D L45-46/48). The write face normalizes to (3, 0).
        ReadStatus status = TryGetMajorVersion(out ushort major, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (major is not (CbdtLayout.MajorVersion2 or CbdtLayout.MajorVersion3))
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
    public ReadStatus TryGetImage1(ulong position, byte bitDepth,
        out CbdtSmallGlyphMetrics metrics, out ReadOnlySpan<byte> data, out FontDiagnostic diagnostic)
        => ReadSmallLegacy(position, bitDepth, 1, out metrics, out data, out diagnostic);

    public ReadStatus TryGetImage2(ulong position, byte bitDepth,
        out CbdtSmallGlyphMetrics metrics, out ReadOnlySpan<byte> data, out FontDiagnostic diagnostic)
        => ReadSmallLegacy(position, bitDepth, 2, out metrics, out data, out diagnostic);

    public ReadStatus TryGetImage6(ulong position, byte bitDepth,
        out CbdtBigGlyphMetrics metrics, out ReadOnlySpan<byte> data, out FontDiagnostic diagnostic)
        => ReadBigLegacy(position, bitDepth, 6, out metrics, out data, out diagnostic);

    public ReadStatus TryGetImage7(ulong position, byte bitDepth,
        out CbdtBigGlyphMetrics metrics, out ReadOnlySpan<byte> data, out FontDiagnostic diagnostic)
        => ReadBigLegacy(position, bitDepth, 7, out metrics, out data, out diagnostic);

    /// <summary>Format 5 takes its length from the index slot, never from dimensions.</summary>
    public ReadStatus TryGetImage5(ulong position, ulong byteLength, byte bitDepth,
        out ReadOnlySpan<byte> data, out FontDiagnostic diagnostic)
    {
        data = default;
        ReadStatus status = AnalyzeLegacyFrame(5, position, byteLength, bitDepth, null,
            out var frame, out diagnostic, new BuildBudget());
        if (status != ReadStatus.Done) return status;
        data = frame.Data;
        return ReadStatus.Done;
    }

    public ReadStatus TryGetImage8(ulong position, byte bitDepth,
        out CbdtSmallGlyphMetrics metrics, out byte pad, out CbdtComponent[] components,
        out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        metrics = default;
        pad = 0;
        components = [];
        var budget = new BuildBudget(limits);
        if (!budget.Input(_data.Length, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        ReadStatus status = AnalyzeLegacyFrame(8, position, Remaining(position), bitDepth, null,
            out var frame, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        if (!budget.Buffer(frame.Components.Length, Location.AtLocal(DataField.Payload, position), out diagnostic))
            return ReadStatus.ResourceLimit;
        metrics = SmallMetrics(frame.Metrics);
        pad = frame.Pad;
        components = MaterializeComponents(frame.Components);
        return ReadStatus.Done;
    }

    public ReadStatus TryGetImage9(ulong position, byte bitDepth,
        out CbdtBigGlyphMetrics metrics, out CbdtComponent[] components,
        out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        metrics = default;
        components = [];
        var budget = new BuildBudget(limits);
        if (!budget.Input(_data.Length, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        ReadStatus status = AnalyzeLegacyFrame(9, position, Remaining(position), bitDepth, null,
            out var frame, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        if (!budget.Buffer(frame.Components.Length, Location.AtLocal(DataField.Payload, position), out diagnostic))
            return ReadStatus.ResourceLimit;
        metrics = BigMetrics(frame.Metrics);
        components = MaterializeComponents(frame.Components);
        return ReadStatus.Done;
    }

    private ReadStatus ReadSmallLegacy(ulong position, byte bitDepth, ushort format,
        out CbdtSmallGlyphMetrics metrics, out ReadOnlySpan<byte> data, out FontDiagnostic diagnostic)
    {
        metrics = default;
        data = default;
        ReadStatus status = AnalyzeLegacyFrame(format, position, Remaining(position), bitDepth, null,
            out var frame, out diagnostic, new BuildBudget());
        if (status != ReadStatus.Done) return status;
        metrics = SmallMetrics(frame.Metrics);
        data = frame.Data;
        return ReadStatus.Done;
    }

    private ReadStatus ReadBigLegacy(ulong position, byte bitDepth, ushort format,
        out CbdtBigGlyphMetrics metrics, out ReadOnlySpan<byte> data, out FontDiagnostic diagnostic)
    {
        metrics = default;
        data = default;
        ReadStatus status = AnalyzeLegacyFrame(format, position, Remaining(position), bitDepth, null,
            out var frame, out diagnostic, new BuildBudget());
        if (status != ReadStatus.Done) return status;
        metrics = BigMetrics(frame.Metrics);
        data = frame.Data;
        return ReadStatus.Done;
    }

    private ReadStatus AnalyzeLegacyFrame(ushort format, ulong position, ulong slotLength,
        byte bitDepth, int? numGlyphs, out BitmapImageCodec.FrameParts frame,
        out FontDiagnostic diagnostic, BuildBudget budget)
    {
        frame = default;
        if (!IsBound)
            return ReadWire.Fail(ReadStatus.Uninitialized, DiagnosticCode.UnboundView, Location, out diagnostic);
        if (!CblcLayout.IsBitDepthDefined(bitDepth))
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.AtLocal(DataField.Payload, position), out diagnostic, bitDepth, 0UL);
        return BitmapImageCodec.AnalyzeLegacyFrame(_data, Location, format, position, slotLength,
            bitDepth, numGlyphs, out frame, out diagnostic, budget);
    }

    private static CbdtComponent[] MaterializeComponents(ReadOnlySpan<byte> bytes)
    {
        var components = new CbdtComponent[bytes.Length / 4];
        for (int i = 0; i < components.Length; i++)
        {
            BitmapImageCodec.ReadComponent(bytes.Slice(i * 4, 4), out ushort glyphId,
                out sbyte xOffset, out sbyte yOffset);
            components[i] = new(glyphId, xOffset, yOffset);
        }
        return components;
    }

    private static CbdtSmallGlyphMetrics SmallMetrics(ReadOnlySpan<byte> metrics) =>
        new(metrics[0], metrics[1], unchecked((sbyte)metrics[2]), unchecked((sbyte)metrics[3]), metrics[4]);

    private static CbdtBigGlyphMetrics BigMetrics(ReadOnlySpan<byte> metrics) =>
        new(metrics[0], metrics[1], unchecked((sbyte)metrics[2]), unchecked((sbyte)metrics[3]),
            metrics[4], unchecked((sbyte)metrics[5]), unchecked((sbyte)metrics[6]), metrics[7]);

    private ulong Remaining(ulong position) => position <= (ulong)_data.Length
        ? (ulong)_data.Length - position : 0UL;


    public ReadStatus TryGetGlyphImage(CblcView cblc, int sizeIndex, int glyphId,
        out CbdtGlyphImage image, out FontDiagnostic diagnostic, BuildLimits? limits = null)
    {
        image = default;
        var budget = new BuildBudget(limits);
        ReadStatus status = RequirePair(cblc, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        status = cblc.TryGetGlyphLocation(sizeIndex, glyphId,
            out CblcGlyphLocation location, out bool present, out ushort indexFormat,
            out ulong imageFormatPosition, out byte bitDepth, out diagnostic, budget);
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
        if (!CblcLayout.IsBitDepthDefined(bitDepth))
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                cblc.Location.AtLocal(DataField.Payload, CblcLayout.BitmapSizeAt +
                    (ulong)sizeIndex * CblcLayout.BitmapSizeStride + 46UL, sizeIndex),
                out diagnostic, bitDepth, 0UL);
        status = ReadImageAt(location.ImageFormat, location.PositionInCbdt,
            location.ByteLength, bitDepth, null, out var candidate, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        if (candidate.ImageFormat is 8 or 9)
        {
            status = CheckReachableCycles(cblc, sizeIndex, (ushort)glyphId,
                Owned(candidate, location.PositionInCbdt), out diagnostic, budget);
            if (status != ReadStatus.Done) return status;
        }
        image = candidate;
        return ReadStatus.Done;
    }

    private static CbdtImage Owned(CbdtGlyphImage image, ulong position) =>
        new(image.ImageFormat, image.Metrics, image.Payload, image.BitDepth, image.Components, image.CompositePad)
        { SourcePosition = position };

    private ReadStatus CheckReachableCycles(CblcView cblc, int sizeIndex, ushort glyphId,
        CbdtImage rootImage, out FontDiagnostic diagnostic, BuildBudget budget)
    {
        const int domain = ushort.MaxValue + 1;
        DataLocation at = Location.AtLocal(DataField.Payload, rootImage.SourcePosition ?? 0UL);
        if (!budget.Buffer((7L + IntPtr.Size) * domain, at, out diagnostic)) return ReadStatus.ResourceLimit;
        var states = new byte[domain];
        var stack = new ushort[domain];
        var nextEdges = new int[domain];
        var cache = new CbdtImage?[domain];
        cache[glyphId] = rootImage;
        states[glyphId] = 1;
        stack[0] = glyphId;
        int top = 0;
        while (top >= 0)
        {
            ushort node = stack[top];
            var current = cache[node]!;
            int edge = nextEdges[top];
            if (edge == current.Components.Length)
            {
                states[node] = 2;
                top--;
                continue;
            }
            DataLocation componentAt = current.ComponentLocation(Location, edge);
            if (!budget.Work(1, componentAt, out diagnostic)) return ReadStatus.ResourceLimit;
            nextEdges[top]++;
            ushort target = current.Components[edge].GlyphId;
            if (states[target] == 1)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    componentAt, out diagnostic, target, 0UL);
            if (states[target] == 2) continue;
            ReadStatus status = cblc.TryGetGlyphLocation(sizeIndex, target,
                out var location, out bool present, out ushort indexFormat,
                out ulong imageFormatPosition, out byte bitDepth, out diagnostic, budget);
            if (status != ReadStatus.Done) return status;
            if (!present)
            {
                states[target] = 2;
                continue;
            }
            if (!CblcLayout.IsImageFormatTyped(location.ImageFormat))
                return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
                    cblc.Location.AtLocal(DataField.Payload, imageFormatPosition), out diagnostic,
                    location.ImageFormat, 0UL);
            if (!CblcLayout.IsPairingDefined(indexFormat, location.ImageFormat))
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    cblc.Location.AtLocal(DataField.Payload, imageFormatPosition), out diagnostic,
                    location.ImageFormat, 0UL);
            if (!CblcLayout.IsBitDepthDefined(bitDepth))
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    cblc.Location.AtLocal(DataField.Payload, CblcLayout.BitmapSizeAt +
                        (ulong)sizeIndex * CblcLayout.BitmapSizeStride + 46UL, sizeIndex),
                    out diagnostic, bitDepth, 0UL);
            status = ReadImageAt(location.ImageFormat, location.PositionInCbdt,
                location.ByteLength, bitDepth, null, out var image, out diagnostic, budget);
            if (status != ReadStatus.Done) return status;
            if (image.ImageFormat is not (8 or 9))
            {
                states[target] = 2;
                continue;
            }
            cache[target] = Owned(image, location.PositionInCbdt);
            states[target] = 1;
            stack[++top] = target;
            nextEdges[top] = 0;
        }
        return Done(out diagnostic);
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
        byte bitDepth, int? numGlyphs, out CbdtGlyphImage image,
        out FontDiagnostic diagnostic, BuildBudget budget)
    {
        image = default;
        if (CblcLayout.IsImageFormatInherited(imageFormat))
        {
            ReadStatus status = AnalyzeLegacyFrame(imageFormat, position, slotLength, bitDepth, numGlyphs,
                out var legacyFrame, out diagnostic, budget);
            if (status != ReadStatus.Done) return status;
            long charge = legacyFrame.Metrics.Length + (long)legacyFrame.Data.Length + legacyFrame.Components.Length;
            if (!budget.Buffer(charge, Location.AtLocal(DataField.Payload, position), out diagnostic))
                return ReadStatus.ResourceLimit;
            image = new(imageFormat, legacyFrame.Metrics.IsEmpty ? null : legacyFrame.Metrics.ToArray(), legacyFrame.Data.ToArray())
            { BitDepth = bitDepth, Components = MaterializeComponents(legacyFrame.Components), CompositePad = legacyFrame.Pad };
            return ReadStatus.Done;
        }
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
        image = new CbdtGlyphImage(imageFormat, metrics, payload) { BitDepth = bitDepth };
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
    /// <summary>Validates every joint frame and each strike's resolved component graph.</summary>
    public ReadStatus ValidateTable(CblcView cblc, int? numGlyphs,
        out FontDiagnostic diagnostic, BuildLimits? limits = null)
        => TryReadTable(cblc, numGlyphs, out _, out diagnostic, limits);

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
        status = cblc.TryReadTable(_data.Length, numGlyphs, out var cblcTable, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        long count = 0;
        var sizes = cblcTable!.Sizes;
        foreach (var strike in sizes)
        {
            foreach (var part in strike.Subtables)
            {
                if (!budget.Work(CountEntries(part), Location, out diagnostic)) return ReadStatus.ResourceLimit;
                count += CblcTableCompiler.CountSlots(part);
            }
        }
        if (count > int.MaxValue)
            return ReadWire.Fail(ReadStatus.ResourceLimit, DiagnosticCode.BudgetExceeded,
                Location.AtLocal(DataField.Count, 0), out diagnostic, (ulong)count, int.MaxValue);
        if (!budget.Buffer(count * IntPtr.Size, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        var images = new CbdtImage[(int)count];
        int imageCursor = 0;
        ulong consumed = CbdtLayout.HeaderLength;
        foreach (var strike in sizes)
        {
            int firstImage = imageCursor;
            bool composite = false;
            foreach (var part in strike.Subtables)
            {
                composite |= part.ImageFormat is 8 or 9;
                switch (part)
                {
                    case CblcOffsetSubtable offsets:
                        for (int k = 0; k < offsets.SbitOffsets.Length - 1; k++)
                        {
                            status = ReadChainImage(part.ImageFormat, (ulong)part.ImageDataOffset + offsets.SbitOffsets[k],
                                (ulong)offsets.SbitOffsets[k + 1] - offsets.SbitOffsets[k], strike.BitDepth, numGlyphs,
                                images, ref imageCursor, budget, ref consumed, out diagnostic);
                            if (status != ReadStatus.Done) return status;
                        }
                        break;
                    case CblcImageSizeSubtable size:
                        for (int g = size.FirstGlyphIndex; g <= size.LastGlyphIndex; g++)
                        {
                            status = ReadChainImage(part.ImageFormat,
                                (ulong)part.ImageDataOffset + (ulong)(g - size.FirstGlyphIndex) * size.ImageSize,
                                size.ImageSize, strike.BitDepth, numGlyphs, images, ref imageCursor,
                                budget, ref consumed, out diagnostic);
                            if (status != ReadStatus.Done) return status;
                        }
                        break;
                    case CblcPairSubtable pairs:
                        for (int k = 0; k < pairs.GlyphIds.Length - 1; k++)
                        {
                            status = ReadChainImage(part.ImageFormat, (ulong)part.ImageDataOffset + pairs.SbitOffsets[k],
                                (ulong)pairs.SbitOffsets[k + 1] - pairs.SbitOffsets[k], strike.BitDepth, numGlyphs,
                                images, ref imageCursor, budget, ref consumed, out diagnostic);
                            if (status != ReadStatus.Done) return status;
                        }
                        break;
                    case CblcGlyphIdArraySubtable ids:
                        for (int k = 0; k < ids.GlyphIds.Length; k++)
                        {
                            status = ReadChainImage(part.ImageFormat,
                                (ulong)part.ImageDataOffset + (ulong)k * ids.ImageSize, ids.ImageSize,
                                strike.BitDepth, numGlyphs, images, ref imageCursor,
                                budget, ref consumed, out diagnostic);
                            if (status != ReadStatus.Done) return status;
                        }
                        break;
                }
            }
            int strikeImages = imageCursor - firstImage;
            if (composite && strikeImages != 0)
            {
                if (!budget.Buffer(2L * strikeImages, Location, out diagnostic)) return ReadStatus.ResourceLimit;
                var glyphIds = new ushort[strikeImages];
                CblcTableCompiler.FillGlyphIds(strike.Subtables, glyphIds);
                status = CbdtTableCompiler.CheckCycles(images, firstImage, glyphIds, Location, budget, out diagnostic);
                if (status != ReadStatus.Done) return status;
            }
        }
        model = new(major, minor, images, Location, (int)consumed);
        return Done(out diagnostic);
    }

    private static int CountEntries(CblcIndexSubtable part) => part switch
    {
        CblcOffsetSubtable offsets => offsets.SbitOffsets.Length,
        CblcPairSubtable pairs => pairs.GlyphIds.Length,
        CblcGlyphIdArraySubtable ids => ids.GlyphIds.Length,
        _ => part.LastGlyphIndex - part.FirstGlyphIndex + 1,
    };

    private ReadStatus ReadChainImage(ushort imageFormat, ulong position, ulong length,
        byte bitDepth, int? numGlyphs, CbdtImage[] images, ref int imageCursor,
        BuildBudget budget, ref ulong consumed, out FontDiagnostic diagnostic)
    {
        diagnostic = default;
        if (length == 0) return ReadStatus.Done;
        ReadStatus status = ReadImageAt(imageFormat, position, length, bitDepth, numGlyphs,
            out var glyphImage, out diagnostic, budget);
        if (status != ReadStatus.Done) return status;
        var image = Owned(glyphImage, position);
        images[imageCursor++] = image;
        consumed = Math.Max(consumed, position + (ulong)CbdtTableCompiler.FrameLength(image));
        return ReadStatus.Done;
    }

    private static ReadStatus Done(out FontDiagnostic diagnostic)
    {
        diagnostic = default;
        return ReadStatus.Done;
    }
}
