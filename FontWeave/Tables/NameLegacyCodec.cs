using System.Buffers;
using System.Text;
using FontWeave.Core;

namespace FontWeave.Tables;

/// <summary>Finite, generated Apple SBCS data with exact sequence-aware reverse mapping.</summary>
internal sealed class NameLegacyCodec
{
    private static readonly NameLegacyCodec[] Codecs = CreateCodecs();
    private readonly string?[] _forward;
    private readonly Dictionary<string, byte> _reverse = new(StringComparer.Ordinal);
    private readonly int _maxSequenceLength;
    private readonly ushort[] _scripts;

    private NameLegacyCodec(string?[] forward, ushort[] scripts,
        params (int Byte, string? Text)[] changes)
    {
        _forward = changes.Length == 0 ? forward : (string?[])forward.Clone();
        _scripts = scripts;
        foreach ((int at, string? text) in changes) _forward[at] = text;
        for (int i = 0; i < _forward.Length; i++)
        {
            string? text = _forward[i];
            if (text is null) continue;
            _maxSequenceLength = Math.Max(_maxSequenceLength, text.Length);
            // KEYBOARD explicitly labels 09 a one-way alias of 61. Later 61 is canonical.
            // No other duplicate exists in the published SBCS catalogue.
            if (!_reverse.TryAdd(text, (byte)i))
            {
                if (forward != NameLegacyData.Keyboard || i != 0x61 || text != "\u2423")
                    throw new InvalidOperationException("Ambiguous generated Apple reverse mapping.");
                _reverse[text] = 0x61;
            }
        }
    }

    internal static NameLegacyCodec? Get(NameLegacyEncoding encoding)
    {
        int index = (int)encoding;
        return index >= 0 && index < Codecs.Length ? Codecs[index] : null;
    }

    internal bool IsCompatible(ushort platformId, ushort encodingId) =>
        platformId == 1 && Array.IndexOf(_scripts, encodingId) >= 0;

    internal int MaxSequenceLength => _maxSequenceLength;

    internal bool TryMeasure(ReadOnlySpan<byte> bytes, out int chars, out int unmapped)
    {
        chars = 0;
        for (int i = 0; i < bytes.Length; i++)
        {
            string? text = _forward[bytes[i]];
            if (text is null) { unmapped = i; return false; }
            chars = checked(chars + text.Length);
        }
        unmapped = -1;
        return true;
    }

    internal ReadStatus TryDecode(ReadOnlySpan<byte> bytes, out string? text,
        out FontDiagnostic diagnostic, DataLocation at)
    {
        text = null;
        if (!TryMeasure(bytes, out int chars, out int unmapped))
            return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
                at with { Field = DataField.Payload, Offset = at.Offset + (uint)unmapped },
                out diagnostic, bytes[unmapped]);
        var builder = new StringBuilder(chars);
        foreach (byte value in bytes) builder.Append(_forward[value]);
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
        var output = new byte[length];
        result = EncodePass(text, choices, output, out _, out diagnostic);
        if (result != ReadStatus.Done) return result;
        bytes = output;
        diagnostic = default;
        return ReadStatus.Done;
    }

    private ReadStatus EncodePass(string text, ReadOnlySpan<NameLegacyByteChoice> choices,
        Span<byte> output, out int written, out FontDiagnostic diagnostic)
    {
        written = 0;
        int choiceIndex = 0;
        var lookup = _reverse.GetAlternateLookup<ReadOnlySpan<char>>();
        for (int at = 0; at < text.Length;)
        {
            if (choiceIndex < choices.Length && choices[choiceIndex].TextOffset < at)
                return InvalidChoice(choices[choiceIndex].TextOffset, choiceIndex, out diagnostic);
            byte value = 0;
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
            if (written == ushort.MaxValue)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    DataLocation.Anonymous(0x6E616D65).At(DataField.Length, 0), out diagnostic,
                    (ulong)written + 1, ushort.MaxValue);
            if (!output.IsEmpty) output[written] = value;
            written++;
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
        new(NameLegacyData.Turkish, [0])
    ];
}
