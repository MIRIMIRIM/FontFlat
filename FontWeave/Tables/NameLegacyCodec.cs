using System.Buffers;
using System.Text;
using FontWeave.Core;

namespace FontWeave.Tables;

/// <summary>Finite Apple mappings with exact sequence-aware reverse mapping and byte-unit grammar.</summary>
internal sealed partial class NameLegacyCodec
{
    private enum Units { SingleByte, Japanese, ChineseTraditional, ChineseSimplified, Korean, Indic }
    private static readonly NameLegacyCodec[] Codecs = CreateCodecs();
    private readonly string?[] _forward;
    private readonly Dictionary<string, ushort> _reverse = new(StringComparer.Ordinal);
    private readonly int _maxSequenceLength;
    private readonly ushort[] _scripts;
    private readonly Units _units;
    private readonly Dictionary<ushort, string>? _pairs;

    private NameLegacyCodec(string?[] forward, ushort[] scripts,
        params (int Byte, string? Text)[] changes) : this(forward, scripts, Units.SingleByte, changes) { }

    private NameLegacyCodec(string?[] forward, ushort[] scripts, Units units,
        params (int Byte, string? Text)[] changes)
    {
        _forward = changes.Length == 0 ? forward : (string?[])forward.Clone();
        _scripts = scripts;
        _units = units;
        foreach ((int at, string? text) in changes) _forward[at] = text;
        for (int i = 0; i < _forward.Length; i++)
        {
            string? text = _forward[i];
            if (text is null) continue;
            _maxSequenceLength = Math.Max(_maxSequenceLength, text.Length);
            // KEYBOARD explicitly labels 09 a one-way alias of 61. Later 61 is canonical.
            // No other duplicate exists in the published SBCS catalogue.
            if (!_reverse.TryAdd(text, (ushort)i))
            {
                if (forward != NameLegacyData.Keyboard || i != 0x61 || text != "\u2423")
                    throw new InvalidOperationException("Ambiguous generated Apple reverse mapping.");
                _reverse[text] = 0x61;
            }
        }
    }

    private NameLegacyCodec(string?[] forward, ushort[] scripts, (ushort Code, string Text)[] pairs)
        : this(forward, scripts, Units.Indic)
    {
        _pairs = new Dictionary<ushort, string>(pairs.Length);
        foreach ((ushort code, string text) in pairs)
        {
            if (code <= 255 || !_pairs.TryAdd(code, text) || !_reverse.TryAdd(text, code))
                throw new InvalidOperationException("Ambiguous generated Apple Indic mapping.");
            _maxSequenceLength = Math.Max(_maxSequenceLength, text.Length);
        }
    }

    internal static NameLegacyCodec? Get(NameLegacyEncoding encoding)
    {
        int index = (int)encoding;
        return index >= 0 && index < Codecs.Length ? Codecs[index] : null;
    }

    internal bool IsCompatible(ushort platformId, ushort encodingId) =>
        platformId == 1 && Array.IndexOf(_scripts, encodingId) >= 0;

    internal int MaxSequenceLength => _maxSequenceLength + (_directions is null ? 0 : 2);

    internal bool TryMeasure(ReadOnlySpan<byte> bytes, out int chars, out int unmapped)
    {
        ReadStatus status = Measure(bytes, out chars, out unmapped, out _, out _);
        return status == ReadStatus.Done;
    }

    private ReadStatus Measure(ReadOnlySpan<byte> bytes, out int chars, out int problemOffset,
        out int problemCode, out DiagnosticCode diagnosticCode, bool includeDirection = true)
    {
        chars = 0;
        problemOffset = -1;
        problemCode = 0;
        diagnosticCode = DiagnosticCode.None;
        byte direction = 0;
        for (int at = 0; at < bytes.Length;)
        {
            int code = bytes[at], length = 1;
            if (IsLead(bytes[at]))
            {
                if (bytes.Length - at < 2)
                {
                    problemOffset = at;
                    problemCode = 1;
                    diagnosticCode = DiagnosticCode.TruncatedData;
                    return ReadStatus.InvalidEncoding;
                }
                code = bytes[at] << 8 | bytes[at + 1];
                length = 2;
                if (!IsTrail(bytes[at], bytes[at + 1]))
                {
                    problemOffset = at;
                    problemCode = code;
                    diagnosticCode = DiagnosticCode.InvalidValue;
                    return ReadStatus.InvalidEncoding;
                }
            }
            else if (_pairs is not null && bytes.Length - at >= 2 &&
                _pairs.ContainsKey((ushort)(bytes[at] << 8 | bytes[at + 1])))
            {
                code = bytes[at] << 8 | bytes[at + 1];
                length = 2;
            }
            string? text = Forward(code);
            if (text is null)
            {
                if (problemOffset < 0) { problemOffset = at; problemCode = code; }
            }
            else
            {
                chars = checked(chars + text.Length);
                if (includeDirection && _directions is not null)
                {
                    byte next = _directions[code];
                    if (direction != next)
                    {
                        if (direction != 0) chars = checked(chars + 1);
                        if (next != 0) chars = checked(chars + 1);
                        direction = next;
                    }
                }
            }
            at += length;
        }
        if (direction != 0) chars = checked(chars + 1);
        // An earlier undefined unit never hides a later malformed or incomplete character.
        if (problemOffset >= 0)
        {
            diagnosticCode = DiagnosticCode.UnsupportedOperation;
            return ReadStatus.UnsupportedFormat;
        }
        return ReadStatus.Done;
    }

    internal ReadStatus Validate(ReadOnlySpan<byte> bytes, DataLocation at, out FontDiagnostic diagnostic)
    {
        ReadStatus status = Measure(bytes, out _, out int offset, out int code, out DiagnosticCode why);
        if (status == ReadStatus.InvalidEncoding) return FailUnit(status, why, at, offset, code, out diagnostic);
        diagnostic = default;
        return ReadStatus.Done;
    }

    private bool IsLead(byte value) => _units switch
    {
        Units.Japanese => value is >= 0x81 and <= 0x9F or >= 0xE0 and <= 0xFC,
        Units.ChineseTraditional or Units.ChineseSimplified => value is >= 0xA1 and <= 0xFC,
        Units.Korean => value is >= 0xA1 and <= 0xFE,
        Units.Indic => value == 0xF0,
        _ => false
    };

    private bool IsTrail(byte lead, byte value) => _units switch
    {
        Units.Japanese => value is >= 0x40 and <= 0x7E or >= 0x80 and <= 0xFC,
        Units.ChineseTraditional => value is >= 0x40 and <= 0x7E or >= 0xA1 and <= 0xFE,
        Units.ChineseSimplified => value is >= 0xA1 and <= 0xFE,
        Units.Korean => value is >= 0xA1 and <= 0xFE ||
            lead <= 0xAD && value is >= 0x41 and <= 0x7D or >= 0x81 and <= 0xA0,
        Units.Indic => value is >= 0xA1 and <= 0xEE,
        _ => false
    };

    private static ReadStatus FailUnit(ReadStatus status, DiagnosticCode code, DataLocation at,
        int offset, int detail, out FontDiagnostic diagnostic) =>
        ReadWire.Fail(status, code,
            at with { Field = DataField.Payload, Offset = at.Offset + (uint)offset }, out diagnostic,
            (ulong)detail, code == DiagnosticCode.TruncatedData ? 2UL : 0UL);

    private int UnitAt(ReadOnlySpan<byte> bytes, ref int at)
    {
        int code = bytes[at++];
        if (IsLead((byte)code)) code = code << 8 | bytes[at++];
        else if (_pairs is not null && at < bytes.Length &&
            _pairs.ContainsKey((ushort)(code << 8 | bytes[at]))) code = code << 8 | bytes[at++];
        return code;
    }

    private string? Forward(int code) => code < _forward.Length ? _forward[code] :
        _pairs is not null && _pairs.TryGetValue((ushort)code, out string? text) ? text : null;

    internal ReadStatus TryDecode(ReadOnlySpan<byte> bytes, out string? text,
        out FontDiagnostic diagnostic, DataLocation at, bool includeDirection = true)
    {
        text = null;
        ReadStatus status = Measure(bytes, out int chars, out int offset, out int code,
            out DiagnosticCode why, includeDirection);
        if (status != ReadStatus.Done) return FailUnit(status, why, at, offset, code, out diagnostic);
        if (bytes.IsEmpty)
        {
            text = string.Empty;
            diagnostic = default;
            return ReadStatus.Done;
        }
        var builder = new StringBuilder(chars);
        if (includeDirection && _directions is not null) AppendDirectional(bytes, builder);
        else
            for (int cursor = 0; cursor < bytes.Length;) builder.Append(Forward(UnitAt(bytes, ref cursor)));
        text = builder.ToString();
        diagnostic = default;
        return ReadStatus.Done;
    }

    internal ReadStatus TryEncode(string text, ReadOnlySpan<NameLegacyByteChoice> choices,
        out byte[]? bytes, out FontDiagnostic diagnostic, BuildBudget budget)
    {
        bytes = null;
        DataLocation request = DataLocation.Anonymous(0x6E616D65);
        if ((long)text.Length * 2 > int.MaxValue)
            return ReadWire.Fail(ReadStatus.ResourceLimit, DiagnosticCode.BudgetExceeded,
                request.At(DataField.InputBytes, 0), out diagnostic, (ulong)text.Length * 2, int.MaxValue);
        if (!budget.Input(text.Length * 2, request, out diagnostic) ||
            !budget.Work((long)text.Length * (_maxSequenceLength + 2) + choices.Length,
                request, out diagnostic)) return ReadStatus.ResourceLimit;
        for (int i = 0; i < choices.Length; i++)
            if (choices[i].TextOffset < 0 || choices[i].TextOffset >= text.Length ||
                i > 0 && choices[i].TextOffset <= choices[i - 1].TextOffset)
                return InvalidChoice(choices[i].TextOffset, i, out diagnostic);
        for (int at = 0; at < text.Length;)
        {
            OperationStatus status = Rune.DecodeFromUtf16(text.AsSpan(at), out _, out int consumed);
            if (status != OperationStatus.Done)
                return ReadWire.Fail(ReadStatus.InvalidEncoding, DiagnosticCode.InvalidUtf16,
                    request.At(DataField.Payload, (ulong)at), out diagnostic);
            at += consumed;
        }
        ReadStatus result = EncodePass(text, choices, default, out int length, out diagnostic);
        if (result != ReadStatus.Done) return result;
        if (!budget.Output(length, request, out diagnostic) ||
            !budget.Buffer(length, request, out diagnostic)) return ReadStatus.ResourceLimit;
        if (_units == Units.Indic && !budget.Work((long)length + text.Length, request, out diagnostic))
            return ReadStatus.ResourceLimit;
        var output = new byte[length];
        result = EncodePass(text, choices, output, out _, out diagnostic);
        if (result != ReadStatus.Done) return result;
        if (_units == Units.Indic && !MatchesText(output, text, out int mismatch))
            return ReadWire.Fail(ReadStatus.InvalidEncoding, DiagnosticCode.InvalidValue,
                request.At(DataField.Payload, (ulong)mismatch), out diagnostic);
        bytes = output;
        diagnostic = default;
        return ReadStatus.Done;
    }

    private bool MatchesText(ReadOnlySpan<byte> bytes, string text, out int mismatch)
    {
        int character = 0;
        for (int at = 0; at < bytes.Length;)
        {
            // Published Indic pair precedence may merge individually encoded singleton
            // characters (e.g. candrabindu+nukta becomes OM). Never publish altered text.
            string? actual = Forward(UnitAt(bytes, ref at));
            if (actual is null) { mismatch = character; return false; }
            foreach (char value in actual)
            {
                if (character == text.Length || value != text[character])
                { mismatch = character; return false; }
                character++;
            }
        }
        mismatch = character;
        return character == text.Length;
    }

    private ReadStatus EncodePass(string text, ReadOnlySpan<NameLegacyByteChoice> choices,
        Span<byte> output, out int written, out FontDiagnostic diagnostic)
    {
        if (_directions is not null)
            return EncodeDirectional(text, choices, output, out written, out diagnostic);
        written = 0;
        int choiceIndex = 0;
        var lookup = _reverse.GetAlternateLookup<ReadOnlySpan<char>>();
        for (int at = 0; at < text.Length;)
        {
            if (choiceIndex < choices.Length && choices[choiceIndex].TextOffset < at)
                return InvalidChoice(choices[choiceIndex].TextOffset, choiceIndex, out diagnostic);
            ushort value = 0;
            int length;
            if (choiceIndex < choices.Length && choices[choiceIndex].TextOffset == at)
            {
                value = choices[choiceIndex].Value;
                string? selected = _forward[value];
                if (selected is null || !text.AsSpan(at).StartsWith(selected, StringComparison.Ordinal))
                    return InvalidChoice(at, choiceIndex, out diagnostic);
                length = selected.Length;
                choiceIndex++;
            }
            else
            {
                for (length = Math.Min(_maxSequenceLength, text.Length - at); length > 0; length--)
                    if (lookup.TryGetValue(text.AsSpan(at, length), out value)) break;
                if (length == 0)
                    return ReadWire.Fail(ReadStatus.InvalidEncoding, DiagnosticCode.InvalidValue,
                        DataLocation.Anonymous(0x6E616D65).At(DataField.Payload, (ulong)at), out diagnostic);
            }
            int byteLength = value > 255 ? 2 : 1;
            if (written > ushort.MaxValue - byteLength)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    DataLocation.Anonymous(0x6E616D65).At(DataField.Length, 0), out diagnostic,
                    (ulong)written + (uint)byteLength, ushort.MaxValue);
            if (!output.IsEmpty)
            {
                if (byteLength == 2) output[written] = (byte)(value >> 8);
                output[written + byteLength - 1] = (byte)(value & 255);
            }
            written += byteLength;
            at += length;
        }
        if (choiceIndex != choices.Length)
            return InvalidChoice(choices[choiceIndex].TextOffset, choiceIndex, out diagnostic);
        diagnostic = default;
        return ReadStatus.Done;
    }

    private static ReadStatus InvalidChoice(int offset, int ordinal, out FontDiagnostic diagnostic) =>
        ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
            DataLocation.Anonymous(0x6E616D65).At(DataField.Offset,
                offset < 0 ? 0 : (ulong)offset, ordinal), out diagnostic);

    private static NameLegacyCodec[] CreateCodecs() =>
    [
        new(NameLegacyData.Roman, [0]),
        new(NameLegacyData.Roman, [0], (0xDB, "\u00A4")),
        new(NameLegacyData.Celtic, [0]),
        new(NameLegacyData.Celtic, [0], (0xDB, "\u00A4")),
        new(NameLegacyData.CentralEuropean, [29]),
        new(NameLegacyData.Croatian, [0]),
        new(NameLegacyData.Croatian, [0], (0xDB, "\u00A4")),
        new(NameLegacyData.Cyrillic, [7]),
        new(NameLegacyData.Cyrillic, [7], (0xA2, "\u00A2"), (0xB6, "\u2202"), (0xFF, "\u00A4")),
        new(NameLegacyData.Cyrillic, [7], (0xFF, "\u00A4")),
        new(NameLegacyData.Dingbats, [0]),
        new(NameLegacyData.Gaelic, [0]),
        new(NameLegacyData.Gaelic, [0], (0xDB, "\u00A4")),
        new(NameLegacyData.Gaelic, [0], (0xE4, "\u2030")),
        new(NameLegacyData.Gaelic, [0], (0xDB, "\u00A4"), (0xE4, "\u2030")),
        new(NameLegacyData.Greek, [0, 6]),
        new(NameLegacyData.Greek, [0, 6], (0x9C, "\u00AD"), (0xFF, null)),
        new(NameLegacyData.Icelandic, [0]),
        new(NameLegacyData.Icelandic, [0], (0xDB, "\u00A4")),
        new(NameLegacyData.Icelandic, [0], (0xBB, "\uFB01"), (0xBC, "\uFB02")),
        new(NameLegacyData.Icelandic, [0], (0xBB, "\uFB01"), (0xBC, "\uFB02"), (0xDB, "\u00A4")),
        new(NameLegacyData.Inuit, [28]),
        new(NameLegacyData.Keyboard, [0]),
        new(NameLegacyData.Romanian, [0]),
        new(NameLegacyData.Romanian, [0], (0xDB, "\u00A4")),
        new(NameLegacyData.Symbol, [0]),
        new(NameLegacyData.Symbol, [0], (0xA0, null)),
        new(NameLegacyData.Thai, [21]),
        new(NameLegacyData.Turkish, [0]),
        new(NameCjkData.Tables[0], [1], Units.Japanese),
        new(NameCjkData.JapaneseBasic(), [1], Units.Japanese),
        new(NameCjkData.Tables[1], [2], Units.ChineseTraditional),
        new(NameCjkData.Tables[2], [25], Units.ChineseSimplified),
        new(NameCjkData.Tables[3], [3], Units.Korean),
        new(NameIndicData.Devanagari, [9], NameIndicData.DevanagariPairs),
        new(NameIndicData.Gujarati, [11], NameIndicData.GujaratiPairs),
        new(NameIndicData.Gurmukhi, [10], NameIndicData.GurmukhiPairs),
        new(NameRtlData.Arabic, [4], NameRtlData.ArabicDirections),
        new(NameRtlData.Arabic, [4], NameRtlData.ArabicDirections,
            (0xAA, "\u00D7", 2), (0xC0, "\u002A", 2)),
        new(NameRtlData.Arabic, [4], NameRtlData.ArabicDirections,
            (0xAA, "\u00D7", 2), (0xC0, "\u066D", 0)),
        new(NameRtlData.Arabic, [4], NameRtlData.ArabicDirections,
            (0x81, null, 0), (0xA3, "\uFDFA", 0), (0xA4, "\uFDF2", 0),
            (0xAA, "\u00D7", 2), (0xDC, "\u25CF", 2), (0xFC, "\u25A0", 2)),
        new(NameRtlData.Farsi, [4], NameRtlData.FarsiDirections),
        new(NameRtlData.Farsi, [4], NameRtlData.FarsiDirections,
            (0x8B, "\uF882", 0), (0xA4, "\uFDFC", 0), (0xAA, "\u00D7", 2),
            (0xC0, "\u002A", 2), (0xF4, "\u00B0", 2), (0xF7, "\uFDFA", 0),
            (0xF9, "\u25CF", 2), (0xFA, "\u25A0", 2), (0xFF, "\u25B2", 2)),
        new(NameRtlData.Hebrew, [5], NameRtlData.HebrewDirections),
        new(NameRtlData.Hebrew, [5], NameRtlData.HebrewDirections, (0xD4, "\u2007", 2))
    ];
}
