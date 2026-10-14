namespace FontWeave.Core;

// Only the direction-sensitive semantic boundary gets wrappers. CID and FD index
// remain explicitly named wire fields in future CFF records; they are not GIDs.
public readonly record struct InputGlyphId(ushort Value);
public readonly record struct OutputGlyphId(ushort Value);

/// <summary>One frozen injective output-to-input order for exactly one source face.
/// This does not compute a glyph closure, validate a font's glyph count or edit tables.</summary>
public sealed class GlyphRemap
{
    private readonly ushort[] _outputToInput;
    private readonly int[] _inputToOutput;
    private GlyphRemap(FontFaceRef sourceFace, ushort[] outputToInput, int[] inputToOutput)
    {
        SourceFace = sourceFace;
        _outputToInput = outputToInput;
        _inputToOutput = inputToOutput;
    }
    public FontFaceRef SourceFace { get; }
    public int InputGlyphCount => _inputToOutput.Length;
    public int OutputGlyphCount => _outputToInput.Length;

    public static ReadStatus TryCreate(FontFaceRef sourceFace, int inputGlyphCount,
        ReadOnlySpan<ushort> outputToInput, out GlyphRemap? map, out FontDiagnostic diagnostic)
    {
        map = null;
        diagnostic = default;
        DataLocation location = sourceFace.Location with { Basis = OffsetBasis.None, Offset = 0, Field = DataField.GlyphId };
        if (!sourceFace.IsBound)
            return ReadWire.Fail(ReadStatus.Uninitialized, DiagnosticCode.UnboundView, location, out diagnostic);
        if (inputGlyphCount is < 1 or > 65536 || outputToInput.Length == 0 || outputToInput.Length > inputGlyphCount)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue, location,
                out diagnostic, (ulong)outputToInput.Length, inputGlyphCount < 0 ? 0 : (ulong)inputGlyphCount);
        if (outputToInput[0] != 0)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                location with { RecordOrdinal = 0 }, out diagnostic, outputToInput[0], 0);
        int[] reverse = new int[inputGlyphCount];
        Array.Fill(reverse, -1);
        for (int i = 0; i < outputToInput.Length; i++)
        {
            ushort input = outputToInput[i];
            if (input >= inputGlyphCount)
                return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                    location with { RecordOrdinal = i }, out diagnostic, input, (ulong)inputGlyphCount);
            if (reverse[input] >= 0)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.DuplicateGlyph,
                    location with { RecordOrdinal = i }, out diagnostic, input, (ulong)reverse[input]);
            reverse[input] = i;
        }
        map = new(sourceFace, outputToInput.ToArray(), reverse);
        return ReadStatus.Done;
    }

    public ReadStatus TryGetOutputGlyph(FontFaceRef sourceFace, InputGlyphId inputGlyph,
        out OutputGlyphId outputGlyph, out FontDiagnostic diagnostic)
    {
        outputGlyph = default;
        diagnostic = default;
        DataLocation location = sourceFace.Location with { Basis = OffsetBasis.None, Offset = 0, Field = DataField.GlyphId };
        if (sourceFace != SourceFace)
            return ReadWire.Fail(ReadStatus.InvalidSource, DiagnosticCode.WrongSourceFace,
                location, out diagnostic, (ulong)sourceFace.SourceId, (ulong)SourceFace.SourceId);
        if (inputGlyph.Value >= _inputToOutput.Length)
            return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                location, out diagnostic, inputGlyph.Value, (ulong)_inputToOutput.Length);
        int output = _inputToOutput[inputGlyph.Value];
        if (output < 0)
            return ReadWire.Fail(ReadStatus.NotFound, DiagnosticCode.GlyphNotMapped, location, out diagnostic, inputGlyph.Value);
        outputGlyph = new((ushort)output);
        return ReadStatus.Done;
    }

    public ReadStatus TryGetInputGlyph(OutputGlyphId outputGlyph, out InputGlyphId inputGlyph,
        out FontDiagnostic diagnostic)
    {
        inputGlyph = default;
        diagnostic = default;
        if (outputGlyph.Value >= _outputToInput.Length)
            return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                SourceFace.Location with { Basis = OffsetBasis.None, Offset = 0, Field = DataField.GlyphId },
                out diagnostic, outputGlyph.Value, (ulong)_outputToInput.Length);
        inputGlyph = new(_outputToInput[outputGlyph.Value]);
        return ReadStatus.Done;
    }

    /// <summary>Same-assembly rebuild stages read the reverse order directly: the caller
    /// has already validated the input GID against the map's own domain, so a per-call
    /// status protocol would restate the TryCreate invariants without adding a check.</summary>
    internal int OutputOf(int inputGlyphId) => _inputToOutput[inputGlyphId];

    /// <summary>The frozen output-to-input order itself; read-only by construction and
    /// never resized after TryCreate (W1 §3.3: a different order is a new operation).</summary>
    internal ReadOnlySpan<ushort> OutputToInput => _outputToInput;
}
