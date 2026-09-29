using System.Text;
using FontWeave.Core;

namespace FontWeave.Tables;

/// <summary>Strict version/name semantics on the sole schema-backed post reader.</summary>
public readonly ref partial struct PostView
{
    public ReadStatus Validate(out FontDiagnostic diagnostic, int maxStringDataBytes = 8 * 1024 * 1024)
        => ValidateCore(out _, out _, out diagnostic, maxStringDataBytes);

    private ReadStatus ValidateCore(out int count, out int poolCount,
        out FontDiagnostic diagnostic, int maxStringDataBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxStringDataBytes);
        count = poolCount = 0;
        if (!IsBound)
            return ReadWire.Fail(ReadStatus.Uninitialized, DiagnosticCode.UnboundView,
                Location, out diagnostic);
        ReadStatus status = TryRequireKnownVersion(out _, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (_data.Length < PostLayout.HeaderLength)
            return ReadWire.Fail(ReadStatus.Truncated, DiagnosticCode.TruncatedData,
                Location.At(DataField.Length, (ulong)_data.Length), out diagnostic,
                (ulong)_data.Length, PostLayout.HeaderLength);
        if (Version is PostLayout.Version10 or PostLayout.Version30)
            return ExactEnd(PostLayout.HeaderLength, out diagnostic);
        status = TryGetNumGlyphs(out ushort glyphCount, out diagnostic);
        if (status != ReadStatus.Done) return status;
        count = glyphCount;
        if (Version == PostLayout.Version25)
        {
            for (int i = 0; i < count; i++)
            {
                status = TryGetGlyphOffset(i, out var item, out diagnostic);
                if (status != ReadStatus.Done) return status;
                int index = i + item.Offset;
                if (index is < 0 or > 257)
                    return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                        Location.At(DataField.GlyphId, 34UL + (uint)i, i), out diagnostic,
                        unchecked((ulong)(long)index), 257);
            }
            return ExactEnd(34 + count, out diagnostic);
        }
        for (int i = 0; i < count; i++)
        {
            status = TryGetGlyphNameIndex(i, out _, out diagnostic);
            if (status != ReadStatus.Done) return status;
        }
        status = TryGetStringDataStart(out ulong stringStart, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (stringStart > (ulong)_data.Length)
            return ReadWire.Fail(ReadStatus.Truncated, DiagnosticCode.TruncatedData,
                Location.At(DataField.Payload, stringStart), out diagnostic,
                (ulong)_data.Length, stringStart);
        int start = (int)stringStart;
        if (_data.Length - start > maxStringDataBytes)
            return ReadWire.Fail(ReadStatus.ResourceLimit, DiagnosticCode.BudgetExceeded,
                Location.At(DataField.Payload, stringStart), out diagnostic,
                (ulong)(_data.Length - start), (ulong)maxStringDataBytes);
        // Pascal framing is variable-length name semantics, not a second table reader.
        for (int at = start; at < _data.Length; poolCount++)
        {
            int lengthAt = at, length = _data[at++];
            if (length > _data.Length - at)
                return ReadWire.Fail(ReadStatus.Truncated, DiagnosticCode.TruncatedData,
                    Location.At(DataField.Payload, (ulong)lengthAt, poolCount), out diagnostic,
                    (ulong)(_data.Length - at), (ulong)length);
            if (!IsValidName(_data.Slice(at, length)))
                return ReadWire.Fail(ReadStatus.InvalidEncoding, DiagnosticCode.InvalidValue,
                    Location.At(DataField.Payload, (ulong)lengthAt, poolCount), out diagnostic,
                    (ulong)length, 63);
            at += length;
        }
        for (int i = 0; i < count; i++)
        {
            _ = TryGetGlyphNameIndex(i, out var item, out _);
            if (item.Index >= 258 && item.Index - 258 >= poolCount)
                return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                    Location.At(DataField.GlyphId, 34UL + 2UL * (uint)i, i), out diagnostic,
                    item.Index, (ulong)poolCount + 258);
        }
        diagnostic = default;
        return ReadStatus.Done;
    }

    private ReadStatus ExactEnd(int expected, out FontDiagnostic diagnostic)
    {
        diagnostic = default;
        if (_data.Length == expected) return ReadStatus.Done;
        return ReadWire.Fail(_data.Length < expected ? ReadStatus.Truncated : ReadStatus.UnsupportedFormat,
            _data.Length < expected ? DiagnosticCode.TruncatedData : DiagnosticCode.UnexpectedTrailingData,
            Location.At(DataField.Length, (ulong)expected), out diagnostic,
            (ulong)_data.Length, (ulong)expected);
    }

    public ReadStatus TryRead(out PostTable? model, out FontDiagnostic diagnostic,
        BuildLimits? limits = null)
    {
        model = null;
        var budget = new BuildBudget(limits);
        if (!budget.Input(_data.Length, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        return TryRead(out model, out diagnostic, budget);
    }

    internal ReadStatus TryRead(out PostTable? model, out FontDiagnostic diagnostic, BuildBudget budget)
    {
        model = null;
        if (!budget.Work(3L * _data.Length, Location, out diagnostic)) return ReadStatus.ResourceLimit;
        ReadStatus status = ValidateCore(out int count, out int poolCount, out diagnostic,
            budget.Limits.MaxInputBytes);
        if (status != ReadStatus.Done) return status;
        if (!budget.Buffer(2L * _data.Length + (long)poolCount * IntPtr.Size,
                Location, out diagnostic)) return ReadStatus.ResourceLimit;
        ushort[] indices = Version == PostLayout.Version20 ? new ushort[count] : Array.Empty<ushort>();
        sbyte[] offsets = Version == PostLayout.Version25 ? new sbyte[count] : Array.Empty<sbyte>();
        for (int i = 0; i < indices.Length; i++)
        { _ = TryGetGlyphNameIndex(i, out var row, out _); indices[i] = row.Index; }
        for (int i = 0; i < offsets.Length; i++)
        { _ = TryGetGlyphOffset(i, out var row, out _); offsets[i] = row.Offset; }
        string[] names = poolCount == 0 ? Array.Empty<string>() : new string[poolCount];
        int at = 34 + 2 * count;
        for (int i = 0; i < names.Length; i++)
        {
            int length = _data[at++];
            names[i] = Encoding.ASCII.GetString(_data.Slice(at, length));
            at += length;
        }
        model = new PostTable(Version, ReadHeader(), indices, offsets, names, Location);
        diagnostic = default;
        return ReadStatus.Done;
    }

    private PostHeaderValues ReadHeader()
    {
        _ = TryGetItalicAngleRaw(out int angle, out _);
        _ = TryGetUnderlinePosition(out short position, out _);
        _ = TryGetUnderlineThickness(out short thickness, out _);
        _ = TryGetIsFixedPitch(out uint pitch, out _);
        _ = TryGetMinMemType42(out uint min42, out _);
        _ = TryGetMaxMemType42(out uint max42, out _);
        _ = TryGetMinMemType1(out uint min1, out _);
        _ = TryGetMaxMemType1(out uint max1, out _);
        return new(angle, position, thickness, pitch, min42, max42, min1, max1);
    }

    internal static bool IsValidName(ReadOnlySpan<byte> name)
    {
        if (name.Length is < 1 or > 63) return false;
        foreach (byte c in name)
            if (!(c is >= (byte)'A' and <= (byte)'Z' or >= (byte)'a' and <= (byte)'z'
                or >= (byte)'0' and <= (byte)'9' or (byte)'.' or (byte)'_')) return false;
        return true;
    }

    internal static bool IsValidName(string name)
    {
        if (name.Length is < 1 or > 63) return false;
        foreach (char c in name)
            if (!(c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '.' or '_')) return false;
        return true;
    }
}
