using FontWeave.Core;

namespace FontWeave.Tables;

/// <summary>One owned sbix glyph slot: the raw origin placement pair, the
/// graphicType tag and the verbatim payload bytes. Empty slots travel as null
/// references - the OQ-T6-04-11 dual wire forms (NULL anchor and zero span)
/// collapse to one owned shape, and the wire tier (canonical equal form vs a
/// NULL-form re-emission) is decided at compile time.</summary>
public readonly record struct SbixGlyph(short OriginOffsetX, short OriginOffsetY,
    uint GraphicType, byte[] Data);

/// <summary>One owned strike: the ppem/ppi head values and the numGlyphs glyph
/// slots (null = empty). Strikes are independent opaque domains - no shared
/// storage across strikes, dupe glyphs reference their targets by glyph ID at
/// read time.</summary>
public sealed class SbixStrike
{
    private readonly SbixGlyph?[] _glyphs;

    /// <summary>Creates one strike over the given slots (null = the normalized
    /// empty); the factory and edit paths share this owned-part shape.</summary>
    public SbixStrike(ushort ppem, ushort ppi, SbixGlyph?[] glyphs)
    {
        Ppem = ppem;
        Ppi = ppi;
        _glyphs = glyphs;
    }

    public ushort Ppem { get; }
    public ushort Ppi { get; }
    public int GlyphCount => _glyphs.Length;
    public IReadOnlyList<SbixGlyph?> Glyphs => Array.AsReadOnly(_glyphs);

    internal SbixGlyph?[] GlyphsInternal => _glyphs;
}

/// <summary>Owned sbix table (card T6-04 §2): the version/flags receipts, the
/// numGlyphs authority receipt, and the strikes in wire order. No PNG/JPEG/TIFF
/// decoding, no color management, no strike selection strategy - those are
/// consumer domains (card T6-04 §11). Produced by SbixView.TryReadTable and
/// edited through the value-level API; structural changes (strike/slot count
/// changes, cross-strike moves, flags or version writes) have no edit surface
/// (OQ-T6-04-14); consumers re-root through SbixTableCompiler.TryCreate.</summary>
public sealed class SbixTable
{
    private readonly SbixStrike[] _strikes;

    internal SbixTable(ushort version, ushort flags, int numGlyphs, SbixStrike[] strikes,
        DataLocation location, int consumedLength)
    {
        Version = version;
        Flags = flags;
        NumGlyphs = numGlyphs;
        _strikes = strikes;
        Location = location;
        ConsumedLength = consumedLength;
    }

    /// <summary>The wire version: 1, the declared supported domain.</summary>
    public ushort Version { get; }

    /// <summary>The flags word's receipt: bit0 carries the read-side required
    /// face, bit1 is the observed draw-outlines instruction, bits 2-15
    /// validated to zero. The word has no edit surface (OQ-T6-04-14).</summary>
    public ushort Flags { get; }

    /// <summary>The numGlyphs authority the table was read with (the caller's
    /// maxp forward); every strike's slot array is exactly this long.</summary>
    public int NumGlyphs { get; }

    public int StrikeCount => _strikes.Length;
    public IReadOnlyList<SbixStrike> Strikes => Array.AsReadOnly(_strikes);

    public DataLocation Location { get; }
    public int ConsumedLength { get; }

    /// <summary>One strike's owned projection.</summary>
    public ReadStatus TryGetStrike(int strikeIndex, out SbixStrike strike,
        out FontDiagnostic diagnostic)
    {
        strike = null!;
        if ((uint)strikeIndex >= (uint)_strikes.Length)
            return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                Location.AtLocal(DataField.Count, SbixLayout.StrikeOffsetsAt +
                    (ulong)strikeIndex * SbixLayout.StrikeOffsetStride, strikeIndex),
                out diagnostic, unchecked((ulong)(long)strikeIndex), (ulong)_strikes.Length);
        strike = _strikes[strikeIndex];
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>One glyph slot's owned projection; present=false for the
    /// normalized empty slots (OQ-T6-04-11).</summary>
    public ReadStatus TryGetGlyph(int strikeIndex, int glyphId, out SbixGlyph glyph,
        out bool present, out FontDiagnostic diagnostic)
    {
        glyph = default;
        present = false;
        ReadStatus status = RequireGlyphDomain(strikeIndex, glyphId, DataField.GlyphId, 0,
            out diagnostic);
        if (status != ReadStatus.Done) return status;
        var slot = _strikes[strikeIndex].GlyphsInternal[glyphId];
        if (slot is not { } value)
        {
            diagnostic = default;
            return ReadStatus.Done;
        }
        glyph = value;
        present = true;
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>Atomically rewrites one glyph slot's origin pair (i16 values
    /// travel raw - the placement semantics are the consumer's, B L91).</summary>
    public ReadStatus TryWithGlyphOrigin(int strikeIndex, int glyphId, short originOffsetX,
        short originOffsetY, out SbixTable? updated, out FontDiagnostic diagnostic)
    {
        updated = null;
        ReadStatus status = RequireGlyphDomain(strikeIndex, glyphId, DataField.Payload, 0,
            out diagnostic);
        if (status != ReadStatus.Done) return status;
        var slot = _strikes[strikeIndex].GlyphsInternal[glyphId];
        if (slot is not { } value)
            return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                Location.AtLocal(DataField.GlyphId,
                    StrikeDataAt(strikeIndex, glyphId), glyphId), out diagnostic,
                unchecked((ulong)(long)glyphId), 0UL);
        var strikes = CloneStrikes();
        strikes[strikeIndex].GlyphsInternal[glyphId] = value with
        {
            OriginOffsetX = originOffsetX,
            OriginOffsetY = originOffsetY,
        };
        updated = new(Version, Flags, NumGlyphs, strikes, Location, ConsumedLength);
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>Atomically replaces one glyph slot whole (tag + payload): the
    /// graphicType closed set re-answers (OQ-T6-04-12), and a 'dupe' tag
    /// re-answers the three-layer semantics against the edited table - an
    /// out-of-domain target, an empty target or a cycle refuses with the
    /// read-side faces (the dangling/cycle-making edit is not taken). Slots
    /// replaced with a non-dupe tag drop their old dupe edges; replacing a dupe
    /// target keeps the referring dupes valid (they follow the slot). Making a
    /// slot empty has no surface - structural changes are finite refusals by
    /// absence (OQ-T6-04-14).</summary>
    public ReadStatus TryWithGlyphPayload(int strikeIndex, int glyphId, uint graphicType,
        ReadOnlySpan<byte> payload, out SbixTable? updated, out FontDiagnostic diagnostic)
    {
        updated = null;
        ReadStatus status = RequireGlyphDomain(strikeIndex, glyphId, DataField.Payload, 0,
            out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (!SbixLayout.IsGraphicTypeDefined(graphicType))
            return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedVersion,
                Location.AtLocal(DataField.Tag, StrikeDataAt(strikeIndex, glyphId) + 4UL,
                    glyphId), out diagnostic, graphicType, 0UL);
        if (graphicType == SbixLayout.GraphicTypeDupe)
        {
            if (payload.Length < 2)
                return ReadWire.Fail(ReadStatus.Truncated, DiagnosticCode.TruncatedData,
                    Location.AtLocal(DataField.Payload,
                        StrikeDataAt(strikeIndex, glyphId) + SbixLayout.GlyphDataHeaderLength,
                        glyphId), out diagnostic, (ulong)payload.Length, 2UL);
            int target = global::System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(payload);
            if (target >= NumGlyphs)
                return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                    Location.AtLocal(DataField.GlyphId,
                        StrikeDataAt(strikeIndex, glyphId) + SbixLayout.GlyphDataHeaderLength,
                        glyphId), out diagnostic, (uint)target, (uint)NumGlyphs);
            var targetSlot = _strikes[strikeIndex].GlyphsInternal[target];
            if (targetSlot is null)
                return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                    Location.AtLocal(DataField.GlyphId,
                        StrikeDataAt(strikeIndex, glyphId) + SbixLayout.GlyphDataHeaderLength,
                        glyphId), out diagnostic, (uint)target, 0UL);
        }
        var strikes = CloneStrikes();
        strikes[strikeIndex].GlyphsInternal[glyphId] = new SbixGlyph(
            strikes[strikeIndex].GlyphsInternal[glyphId]?.OriginOffsetX ?? 0,
            strikes[strikeIndex].GlyphsInternal[glyphId]?.OriginOffsetY ?? 0,
            graphicType, payload.ToArray());
        if (graphicType == SbixLayout.GraphicTypeDupe)
        {
            status = RequireAcyclicDupes(strikes[strikeIndex], glyphId, out diagnostic);
            if (status != ReadStatus.Done) return status;
        }
        updated = new(Version, Flags, NumGlyphs, strikes, Location, ConsumedLength);
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>The edited strike's dupe graph must stay acyclic from the
    /// replaced slot: the chain walk follows targets through dupe slots until a
    /// non-dupe terminator; a revisit is a cycle (the COLR path-set family).</summary>
    private ReadStatus RequireAcyclicDupes(SbixStrike strike, int replacedGlyphId,
        out FontDiagnostic diagnostic)
    {
        var visited = new HashSet<int> { replacedGlyphId };
        int current = global::System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(
            strike.GlyphsInternal[replacedGlyphId]!.Value.Data);
        while (true)
        {
            if (visited.Contains(current))
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    Location.AtLocal(DataField.GlyphId, StrikeDataAt(replacedGlyphId, current),
                        current), out diagnostic, (uint)current, 0UL);
            visited.Add(current);
            var slot = strike.GlyphsInternal[current];
            if (slot is not { } value || value.GraphicType != SbixLayout.GraphicTypeDupe)
                break;
            if (value.Data.Length < 2)
                return ReadWire.Fail(ReadStatus.Truncated, DiagnosticCode.TruncatedData,
                    Location.AtLocal(DataField.Payload, StrikeDataAt(replacedGlyphId, current),
                        current), out diagnostic, (ulong)value.Data.Length, 2UL);
            current = global::System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(value.Data);
        }
        diagnostic = default;
        return ReadStatus.Done;
    }

    private SbixStrike[] CloneStrikes()
    {
        var strikes = new SbixStrike[_strikes.Length];
        for (int i = 0; i < strikes.Length; i++)
            strikes[i] = new SbixStrike(_strikes[i].Ppem, _strikes[i].Ppi,
                (SbixGlyph?[])_strikes[i].GlyphsInternal.Clone());
        return strikes;
    }

    /// <summary>The edited slot's canonical-plan data coordinate (the factory
    /// plan: header(8) + strikeOffsets(4n) + the strike's own plan) - the edit
    /// diagnostics' coordinate before the first compile re-roots them.</summary>
    private ulong StrikeDataAt(int strikeIndex, int glyphId)
    {
        ulong at = SbixLayout.StrikeOffsetsAt + 4UL * (uint)StrikeCount +
            4UL * (uint)strikeIndex * (SbixLayout.StrikeHeaderLength +
                4UL * ((uint)NumGlyphs + 1UL));
        return at + SbixLayout.StrikeHeaderLength + 4UL * ((uint)NumGlyphs + 1UL) +
            8UL * (uint)glyphId;
    }

    private ReadStatus RequireGlyphDomain(int strikeIndex, int glyphId, DataField field,
        ulong at, out FontDiagnostic diagnostic)
    {
        if ((uint)strikeIndex >= (uint)_strikes.Length)
            return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                Location.AtLocal(DataField.Count, SbixLayout.StrikeOffsetsAt +
                    (ulong)strikeIndex * SbixLayout.StrikeOffsetStride, strikeIndex),
                out diagnostic, unchecked((ulong)(long)strikeIndex), (ulong)_strikes.Length);
        if (glyphId < 0 || glyphId >= NumGlyphs)
            return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                Location.AtLocal(field, at, glyphId), out diagnostic,
                unchecked((ulong)(long)glyphId), (uint)NumGlyphs);
        diagnostic = default;
        return ReadStatus.Done;
    }
}
