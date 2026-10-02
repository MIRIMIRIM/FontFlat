using FontWeave.Core;

namespace FontWeave.Tables;

/// <summary>Single hand-written view over a whole glyf table.
///
/// Explicit hand-written boundary: glyf is a mesh of variable-length records whose
/// per-glyph layout depends on flags expanded from the record itself and whose record
/// boundaries come from the paired loca table. That is beyond the flat, self-describing
/// record/array shape the schema generator expresses, so this type owns the byte reading
/// directly. There is deliberately no Generated*View and no second parser; the schema
/// generator is not extended to model glyf.
///
/// The view borrows the whole table image. Nothing here executes hinting, evaluates
/// curves, recomputes bounding boxes, or assigns outline semantics beyond decoding.</summary>
public readonly ref partial struct GlyfView
{
    private readonly ReadOnlySpan<byte> _data;
    private readonly DataLocation _location;
    private readonly bool _bound;

    private GlyfView(ReadOnlySpan<byte> data, DataLocation location, bool bound)
    {
        _data = data;
        _location = location;
        _bound = bound;
    }

    /// <summary>True once the view was opened over a table image. An empty image is
    /// bound but has no bytes, which is distinct from an unopened default view.</summary>
    public bool IsBound => _bound;

    public ReadOnlySpan<byte> RawBytes => _data;

    public DataLocation Location => _location == default
        ? DataLocation.Anonymous(GlyfLayout.Tag)
        : _location;

    public static ReadStatus TryOpen(ReadOnlySpan<byte> data, out GlyfView view,
        out FontDiagnostic diagnostic, DataLocation location = default)
    {
        // A zero-length glyf image is legal: a font whose every glyph is empty has no
        // glyph records at all, and its loca describes only equal offsets. Whether such
        // an image agrees with the paired loca is decided by Validate, not here.
        view = new(data, Normalize(location), true);
        diagnostic = default;
        return ReadStatus.Done;
    }

    private static DataLocation Normalize(DataLocation location) =>
        location == default ? DataLocation.Anonymous(GlyfLayout.Tag) : location;

    /// <summary>Decodes one glyph's description. <paramref name="range"/> must come from the
    /// paired loca table over the same source face; this view never reads loca itself.
    ///
    /// <paramref name="strict"/> controls only documented wide-read relaxations: a nonzero
    /// reserved simple-glyph flag bit. It never relaxes structural bounds, contour ordering,
    /// coordinate completeness, or trailing-byte checks.</summary>
    public ReadStatus TryDecodeGlyph(LocaRange range, out GlyfGlyph? glyph,
        out FontDiagnostic diagnostic, bool strict = true)
    {
        return TryDecodeGlyph(range, out glyph, out diagnostic, strict, -1);
    }

    /// <summary>Decodes one glyph's description, optionally decoding composite structure.
    ///
    /// When <paramref name="numGlyphs"/> is non-negative the composite component records
    /// are decoded by the same parser and exposed through <see cref="GlyfGlyph.Composite"/>,
    /// with component references checked against that glyph count. When it is negative a
    /// composite is classified but left unparsed, which is the narrower contract the
    /// simple-glyph slice relies on.</summary>
    public ReadStatus TryDecodeGlyph(LocaRange range, out GlyfGlyph? glyph,
        out FontDiagnostic diagnostic, bool strict, int numGlyphs)
    {
        return TryDecodeGlyph(range, out glyph, out diagnostic, strict, numGlyphs, null);
    }

    /// <summary>Decoding entry point that also charges temporary space for the arrays a
    /// simple glyph expands into. The caller's budget is passed down because a glyph's
    /// decoded size is driven by its point count, not by the bytes it occupies on the wire:
    /// a compressed flag run lets a few hundred bytes describe many thousands of points.</summary>
    internal ReadStatus TryDecodeGlyph(LocaRange range, out GlyfGlyph? glyph,
        out FontDiagnostic diagnostic, bool strict, int numGlyphs, BuildBudget? budget)
    {
        glyph = null;
        if (!IsBound)
            return ReadWire.Fail(ReadStatus.Uninitialized, DiagnosticCode.UnboundView,
                Location.At(DataField.None, 0), out diagnostic);
        if (range.End < range.Start)
            return ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.InvalidValue,
                Location.At(DataField.Offset, range.Start, range.GlyphId), out diagnostic,
                range.End, range.Start);
        if (range.End > (uint)_data.Length)
            return ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.RangeOutsideSource,
                Location.At(DataField.Offset, range.Start, range.GlyphId), out diagnostic,
                range.End, (ulong)_data.Length);
        if (range.IsEmpty)
        {
            glyph = new(range.GlyphId, default, GlyfGlyphKind.Empty, range.Start, range.End, null);
            diagnostic = default;
            return ReadStatus.Done;
        }
        if (range.Length < GlyfLayout.MinimumGlyph)
            return ReadWire.Fail(ReadStatus.Truncated, DiagnosticCode.TruncatedData,
                Location.At(DataField.Payload, range.Start, range.GlyphId), out diagnostic,
                range.Length, GlyfLayout.MinimumGlyph);
        ReadOnlySpan<byte> record = _data.Slice((int)range.Start, (int)range.Length);
        // Header scalars are signed two's-complement; the narrowing is an intentional
        // reinterpretation of the sign bit, so it is explicitly unchecked.
        var header = new GlyfHeader(
            unchecked((short)((record[0] << 8) | record[1])),
            unchecked((short)((record[2] << 8) | record[3])),
            unchecked((short)((record[4] << 8) | record[5])),
            unchecked((short)((record[6] << 8) | record[7])),
            unchecked((short)((record[8] << 8) | record[9])));
        if (header.NumberOfContours < 0)
        {
            if (numGlyphs < 0)
            {
                // Not decoded on this path: composite structure belongs to the
                // composite-aware entry points. Reported as not parsed rather than
                // accepted or rejected.
                glyph = new(range.GlyphId, header, GlyfGlyphKind.Composite, range.Start,
                    range.End, null);
                diagnostic = default;
                return ReadStatus.Done;
            }
            ReadStatus compositeStatus = TryDecodeCompositeBody(range, record, numGlyphs,
                out var composite, out diagnostic, strict);
            if (compositeStatus != ReadStatus.Done) return compositeStatus;
            glyph = new(range.GlyphId, header, GlyfGlyphKind.Composite, range.Start, range.End,
                null, composite);
            diagnostic = default;
            return ReadStatus.Done;
        }
        return TryDecodeSimple(range, header, record, strict, budget, out glyph, out diagnostic);
    }

    /// <summary>Bytes of temporary space one decoded point costs: the expanded flag byte,
    /// the byte[] the flag run materializes into, and the GlyphPoint record itself.</summary>
    private const int TemporaryBytesPerPoint = 1 + 1 + 8;

    private ReadStatus TryDecodeSimple(LocaRange range, GlyfHeader header,
        ReadOnlySpan<byte> record, bool strict, BuildBudget? budget, out GlyfGlyph? glyph,
        out FontDiagnostic diagnostic)
    {
        glyph = null;
        int contours = header.NumberOfContours;
        int at = GlyfLayout.HeaderLength;
        // endPtsOfContours is exact-width: numberOfContours * 2 bytes.
        if (contours > (record.Length - at) / 2)
            return ReadWire.Fail(ReadStatus.Truncated, DiagnosticCode.TruncatedData,
                Location.At(DataField.Count, range.Start + (ulong)at, range.GlyphId),
                out diagnostic, (ulong)record.Length, (ulong)(at + unchecked(contours * 2)));
        var endPoints = new ushort[contours];
        for (int i = 0; i < contours; i++)
        {
            endPoints[i] = (ushort)((record[at] << 8) | record[at + 1]);
            at += 2;
            if (i > 0 && endPoints[i] <= endPoints[i - 1])
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    Location.At(DataField.Payload, range.Start + (ulong)(at - 2), range.GlyphId),
                    out diagnostic, endPoints[i], (ulong)endPoints[i - 1] + 1);
        }
        if (at + 2 > record.Length)
            return ReadWire.Fail(ReadStatus.Truncated, DiagnosticCode.TruncatedData,
                Location.At(DataField.Length, range.Start + (ulong)at, range.GlyphId),
                out diagnostic, (ulong)record.Length, (ulong)(at + 2));
        int instructionLength = (record[at] << 8) | record[at + 1];
        at += 2;
        if (instructionLength > record.Length - at)
            return ReadWire.Fail(ReadStatus.Truncated, DiagnosticCode.TruncatedData,
                Location.At(DataField.Length, range.Start + (ulong)(at - 2), range.GlyphId),
                out diagnostic, (ulong)(record.Length - at), (ulong)instructionLength);
        byte[] instructions = record.Slice(at, instructionLength).ToArray();
        at += instructionLength;
        // The point count is the final endpoint plus one. A 0xFFFF final endpoint would
        // wrap that addition back to zero and silently decode an empty outline, so it is
        // rejected explicitly instead of relying on the widening accumulating later.
        if (contours > 0 && endPoints[contours - 1] == ushort.MaxValue)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.At(DataField.Count, range.Start + (ulong)(at - instructionLength - 2 -
                    2 * contours), range.GlyphId), out diagnostic,
                ushort.MaxValue, (ulong)(ushort.MaxValue - 1));
        int pointCount = contours == 0 ? 0 : endPoints[contours - 1] + 1;
        // Charge the expanded point count, not the record length. TryExpandFlags allocates a
        // List<byte> of one flag per point and TryDecodeCoordinates materializes one
        // GlyphPoint per point, so the temporary cost is set by pointCount alone.
        if (budget is { } charge &&
            !charge.Buffer((long)TemporaryBytesPerPoint * pointCount, Location, out diagnostic))
            return ReadStatus.ResourceLimit;
        ReadStatus status = TryExpandFlags(record, range, at, pointCount, strict,
            out byte[] flags, out int flagEnd, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = TryDecodeCoordinates(record, range, flags, flagEnd, out var points,
            out int consumed, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (consumed > record.Length)
            return ReadWire.Fail(ReadStatus.Truncated, DiagnosticCode.TruncatedData,
                Location.At(DataField.Payload, range.Start + (ulong)consumed, range.GlyphId),
                out diagnostic, (ulong)record.Length, (ulong)consumed);
        int padding = record.Length - consumed;
        for (int i = consumed; i < record.Length; i++)
            if (record[i] != 0)
                return ReadWire.Fail(ReadStatus.InvalidFormat,
                    DiagnosticCode.UnexpectedTrailingData,
                    Location.At(DataField.Payload, range.Start + (ulong)i, range.GlyphId),
                    out diagnostic, record[i], 0);
        var simple = new SimpleGlyph(endPoints, flags, points, instructions, consumed, padding);
        glyph = new(range.GlyphId, header, GlyfGlyphKind.Simple, range.Start, range.End, simple);
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>Expands the packed flag array to exactly one logical flag per point.
    /// Bit 3 (REPEAT) makes the following byte repeat the flag that many additional times.
    /// The logical array keeps the raw flag byte, including the repeat bit.</summary>
    private ReadStatus TryExpandFlags(ReadOnlySpan<byte> record, LocaRange range, int at,
        int pointCount, bool strict, out byte[] flags, out int end, out FontDiagnostic diagnostic)
    {
        flags = [];
        end = at;
        var expanded = new List<byte>(pointCount);
        diagnostic = default;
        while (expanded.Count < pointCount)
        {
            if (end >= record.Length)
                return ReadWire.Fail(ReadStatus.Truncated, DiagnosticCode.TruncatedData,
                    Location.At(DataField.Length, range.Start + (ulong)end, range.GlyphId),
                    out diagnostic, (ulong)record.Length, (ulong)(end + 1));
            byte flag = record[end++];
            if (strict && (flag & GlyfLayout.ReservedFlag) != 0)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    Location.At(DataField.Flags, range.Start + (ulong)(end - 1), range.GlyphId),
                    out diagnostic, flag, GlyfLayout.ReservedFlag);
            expanded.Add(flag);
            if ((flag & GlyfLayout.Repeat) != 0)
            {
                if (end >= record.Length)
                    return ReadWire.Fail(ReadStatus.Truncated, DiagnosticCode.TruncatedData,
                        Location.At(DataField.Length, range.Start + (ulong)end, range.GlyphId),
                        out diagnostic, (ulong)record.Length, (ulong)(end + 1));
                int repeat = record[end++];
                if (expanded.Count + repeat > pointCount)
                    return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                        Location.At(DataField.Flags, range.Start + (ulong)(end - 1), range.GlyphId),
                        out diagnostic, (ulong)(expanded.Count + repeat), (ulong)pointCount);
                for (int i = 0; i < repeat; i++) expanded.Add(flag);
            }
        }
        if (expanded.Count != pointCount)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.At(DataField.Flags, range.Start + (ulong)at, range.GlyphId),
                out diagnostic, (ulong)expanded.Count, (ulong)pointCount);
        flags = [.. expanded];
        return ReadStatus.Done;
    }

    /// <summary>Decodes the x/y coordinate streams into absolute design-unit points.
    /// Byte widths follow the specification exactly:
    /// X_SHORT -> 1 byte, else X_SAME -> 0 bytes, else int16. The y stream is analogous.</summary>
    private ReadStatus TryDecodeCoordinates(ReadOnlySpan<byte> record, LocaRange range,
        byte[] flags, int at, out GlyphPoint[] points, out int consumed,
        out FontDiagnostic diagnostic)
    {
        points = [];
        consumed = at;
        int count = flags.Length;
        var deltas = new (short X, short Y)[count];
        for (int i = 0; i < count; i++)
        {
            byte flag = flags[i];
            short x;
            if ((flag & GlyfLayout.XShort) != 0)
            {
                if (at >= record.Length) return ShortCoordinate(range, at, out diagnostic);
                int value = record[at++];
                x = (short)((flag & GlyfLayout.XSameOrPositive) != 0 ? value : -value);
            }
            else if ((flag & GlyfLayout.XSameOrPositive) != 0) x = 0;
            else
            {
                if (at + 2 > record.Length) return ShortCoordinate(range, at, out diagnostic);
                // Two-byte big-endian signed delta. The reinterpretation is explicitly
                // unchecked because the high bit is the sign bit, not an overflow.
                x = unchecked((short)((record[at] << 8) | record[at + 1]));
                at += 2;
            }
            deltas[i] = (x, 0);
        }
        for (int i = 0; i < count; i++)
        {
            byte flag = flags[i];
            short y;
            if ((flag & GlyfLayout.YShort) != 0)
            {
                if (at >= record.Length) return ShortCoordinate(range, at, out diagnostic);
                int value = record[at++];
                y = (short)((flag & GlyfLayout.YSameOrPositive) != 0 ? value : -value);
            }
            else if ((flag & GlyfLayout.YSameOrPositive) != 0) y = 0;
            else
            {
                if (at + 2 > record.Length) return ShortCoordinate(range, at, out diagnostic);
                y = unchecked((short)((record[at] << 8) | record[at + 1]));
                at += 2;
            }
            deltas[i] = (deltas[i].X, y);
        }
        var decoded = new GlyphPoint[count];
        // Coordinate accumulation is intentionally unchecked: each stored delta is a
        // signed 16-bit value and real outlines accumulate past int16 range on purpose.
        // The result is range-checked afterwards, so an out-of-range outline is a
        // diagnostic rather than an overflow exception.
        int cx = 0, cy = 0;
        for (int i = 0; i < count; i++)
        {
            cx = unchecked(cx + deltas[i].X);
            cy = unchecked(cy + deltas[i].Y);
            if (cx is < short.MinValue or > short.MaxValue ||
                cy is < short.MinValue or > short.MaxValue)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    Location.At(DataField.Payload, range.Start + (ulong)at, range.GlyphId),
                    out diagnostic, unchecked((ulong)(long)cx), (ulong)short.MaxValue);
            decoded[i] = new(cx, cy, (flags[i] & GlyfLayout.OnCurve) != 0);
        }
        points = decoded;
        consumed = at;
        diagnostic = default;
        return ReadStatus.Done;
    }

    private ReadStatus ShortCoordinate(LocaRange range, int at, out FontDiagnostic diagnostic) =>
        ReadWire.Fail(ReadStatus.Truncated, DiagnosticCode.TruncatedData,
            Location.At(DataField.Payload, range.Start + (ulong)at, range.GlyphId),
            out diagnostic, (ulong)at, (ulong)(at + 2));
}
