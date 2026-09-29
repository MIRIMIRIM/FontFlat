using System.Text;
using FontWeave.Core;

namespace FontWeave.Tables;

internal sealed partial class NameLegacyCodec
{
    // Apple tags describe required strong direction, not a Unicode character property.
    // The string surface transports them as flat LRO/RLO ... PDF runs; it does not run UBA.
    private readonly byte[]? _directions;
    private readonly Dictionary<string, ushort>[]? _directionalReverse;

    private NameLegacyCodec(string?[] forward, ushort[] scripts, byte[] directions,
        params (int Byte, string? Text, byte Direction)[] changes)
    {
        _forward = changes.Length == 0 ? forward : (string?[])forward.Clone();
        _directions = changes.Length == 0 ? directions : (byte[])directions.Clone();
        _scripts = scripts;
        _units = Units.SingleByte;
        _directionalReverse =
        [
            _reverse,
            new(StringComparer.Ordinal),
            new(StringComparer.Ordinal)
        ];
        foreach ((int at, string? text, byte direction) in changes)
        {
            _forward[at] = text;
            _directions[at] = direction;
        }
        for (int i = 0; i < _forward.Length; i++)
        {
            string? text = _forward[i];
            if (text is null) continue;
            _maxSequenceLength = Math.Max(_maxSequenceLength, text.Length);
            byte direction = _directions[i];
            // A published untagged unit is the explicit bare spelling (e.g. Hebrew 30);
            // its tagged counterpart still needs a run or byte choice. Only multiple
            // tagged candidates with no untagged mapping require caller direction input.
            if (!_reverse.TryAdd(text, (ushort)i))
            {
                ushort previous = _reverse[text];
                if (direction == 0) _reverse[text] = (ushort)i;
                else if (previous == ushort.MaxValue || _directions[previous] != 0)
                    _reverse[text] = ushort.MaxValue;
            }
            if (direction != 0 && !_directionalReverse[direction].TryAdd(text, (ushort)i))
                throw new InvalidOperationException("Ambiguous generated Apple directional mapping.");
        }
    }

    private void AppendDirectional(ReadOnlySpan<byte> bytes, StringBuilder builder)
    {
        byte direction = 0;
        foreach (byte value in bytes)
        {
            byte next = _directions![value];
            if (direction != next)
            {
                if (direction != 0) builder.Append('\u202C');
                if (next != 0) builder.Append(next == 1 ? '\u202D' : '\u202E');
                direction = next;
            }
            builder.Append(_forward[value]);
        }
        if (direction != 0) builder.Append('\u202C');
    }

    private ReadStatus EncodeDirectional(string text, ReadOnlySpan<NameLegacyByteChoice> choices,
        Span<byte> output, out int written, out FontDiagnostic diagnostic)
    {
        written = 0;
        int choiceIndex = 0, runStart = -1;
        byte direction = 0;
        bool hasToken = false;
        for (int at = 0; at < text.Length;)
        {
            if (choiceIndex < choices.Length && choices[choiceIndex].TextOffset < at)
                return InvalidChoice(choices[choiceIndex].TextOffset, choiceIndex, out diagnostic);
            char current = text[at];
            if (current is '\u202C' or '\u202D' or '\u202E')
            {
                if (choiceIndex < choices.Length && choices[choiceIndex].TextOffset == at)
                    return InvalidChoice(at, choiceIndex, out diagnostic);
                if (current == '\u202C')
                {
                    if (direction == 0 || !hasToken) return InvalidDirection(at, out diagnostic);
                    direction = 0;
                }
                else
                {
                    if (direction != 0) return InvalidDirection(at, out diagnostic);
                    direction = current == '\u202D' ? (byte)1 : (byte)2;
                    runStart = at;
                    hasToken = false;
                }
                at++;
                continue;
            }
            ushort value = 0;
            int length;
            if (choiceIndex < choices.Length && choices[choiceIndex].TextOffset == at)
            {
                value = choices[choiceIndex].Value;
                string? selected = _forward[value];
                if (selected is null || !text.AsSpan(at).StartsWith(selected, StringComparison.Ordinal) ||
                    direction != 0 && _directions![value] != direction)
                    return InvalidChoice(at, choiceIndex, out diagnostic);
                length = selected.Length;
                choiceIndex++;
            }
            else
            {
                var lookup = _directionalReverse![direction].GetAlternateLookup<ReadOnlySpan<char>>();
                for (length = Math.Min(_maxSequenceLength, text.Length - at); length > 0; length--)
                    if (lookup.TryGetValue(text.AsSpan(at, length), out value)) break;
                if (length == 0 || value == ushort.MaxValue)
                    return InvalidDirection(at, out diagnostic);
            }
            if (written == ushort.MaxValue)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    DataLocation.Anonymous(0x6E616D65).At(DataField.Length, 0), out diagnostic,
                    (ulong)written + 1, ushort.MaxValue);
            if (!output.IsEmpty) output[written] = (byte)value;
            written++;
            hasToken = true;
            at += length;
        }
        if (direction != 0) return InvalidDirection(runStart, out diagnostic);
        if (choiceIndex != choices.Length)
            return InvalidChoice(choices[choiceIndex].TextOffset, choiceIndex, out diagnostic);
        diagnostic = default;
        return ReadStatus.Done;
    }

    private static ReadStatus InvalidDirection(int offset, out FontDiagnostic diagnostic) =>
        ReadWire.Fail(ReadStatus.InvalidEncoding, DiagnosticCode.InvalidValue,
            DataLocation.Anonymous(0x6E616D65).At(DataField.Payload, (ulong)offset), out diagnostic);
}
