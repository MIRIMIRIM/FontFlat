using System.Buffers.Binary;
using FontWeave.Core;

namespace FontWeave.Tables;

/// <summary>CFF2 DICT operator vocabulary (OpenType 1.9.1 chapter "CFF2", the
/// 43464632.md snapshot). Each DICT type has its own whitelist; an operator outside
/// its scope's whitelist is a hard refusal (OQ-T5-05-4 lean: L256 "not defined for
/// the current DICT type" is the unspecified face and the product takes the strict
/// shape, the CFF1 DictArity -2 precedent). The PrivateDICT whitelist is the spec
/// operator-summary table verbatim: 16 operators = LocalSubrINDEXOffset + vsindex +
/// blend + the 13 hinting keys (the card's "17" count is a drafting slip against its
/// own 3+13 enumeration; the snapshot table is the normative oracle).</summary>
internal static class Cff2Operators
{
    // TopDICT: exactly five legal operators (spec "TopDICT operator summary").
    internal const int TopCharStrings = 0x11;
    internal const int TopVariationStore = 0x18;
    internal const int TopFdArray = 0x0c24;
    internal const int TopFdSelect = 0x0c25;
    internal const int TopFontMatrix = 0x0c07;

    // FontDICT: the single 0x12 PrivateDICTOffset key.
    internal const int FontPrivateDict = 0x12;

    // PrivateDICT variation and subroutine keys.
    internal const int PrivateLocalSubrs = 0x13;
    internal const int PrivateVsindex = 0x16;
    internal const int PrivateBlend = 0x17;

    // PrivateDICT hinting keys.
    internal const int BlueValues = 6, OtherBlues = 7, FamilyBlues = 8, FamilyOtherBlues = 9;
    internal const int StdHW = 10, StdVW = 11;
    internal const int BlueScale = 0x0c09, BlueShift = 0x0c0a, BlueFuzz = 0x0c0b;
    internal const int StemSnapH = 0x0c0c, StemSnapV = 0x0c0d;
    internal const int LanguageGroup = 0x0c11, ExpansionFactor = 0x0c12;

    internal const int MaxDictOperandStack = 513; // spec L193
    internal const int MaxRealNibbleDigits = 128;
    internal const int MaxCharstringLength = 65535; // spec L795

    internal static bool IsTopOperator(int op) =>
        op is TopCharStrings or TopVariationStore or TopFdArray or TopFdSelect or TopFontMatrix;

    internal static bool IsFontOperator(int op) => op is FontPrivateDict;

    internal static bool IsPrivateOperator(int op) =>
        op is PrivateLocalSubrs or PrivateVsindex or PrivateBlend
            or BlueValues or OtherBlues or FamilyBlues or FamilyOtherBlues
            or StdHW or StdVW or BlueScale or BlueShift or BlueFuzz
            or StemSnapH or StemSnapV or LanguageGroup or ExpansionFactor;

    /// <summary>Exact operand count for a whitelist operator; -1 means an array key
    /// (one or more operands). The private blend operator never reaches this table -
    /// it is an operand-stream modifier, not a key (spec L247-256).</summary>
    internal static int Arity(Cff2DictScope scope, int op) => scope switch
    {
        Cff2DictScope.Top => op switch
        {
            TopCharStrings or TopVariationStore or TopFdArray or TopFdSelect => 1,
            TopFontMatrix => 6,
            _ => -2
        },
        Cff2DictScope.FontDict => op is FontPrivateDict ? 2 : -2,
        _ => op switch
        {
            PrivateLocalSubrs or PrivateVsindex or StdHW or StdVW
                or BlueScale or BlueShift or BlueFuzz or LanguageGroup or ExpansionFactor => 1,
            BlueValues or OtherBlues or FamilyBlues or FamilyOtherBlues
                or StemSnapH or StemSnapV => -1,
            _ => -2
        }
    };

    /// <summary>The two PrivateDICT ordering restrictions (spec "Operator ordering
    /// restrictions"): OtherBlues may only occur after BlueValues, FamilyOtherBlues
    /// only after FamilyBlues (OQ-T5-05-5 lean hard refusals). `required` names the
    /// key that must precede.</summary>
    internal static bool ViolatesPrivateOrdering(int op, bool blueValuesSeen,
        bool familyBluesSeen, out int required)
    {
        if (op is OtherBlues && !blueValuesSeen)
        {
            required = BlueValues;
            return true;
        }
        if (op is FamilyOtherBlues && !familyBluesSeen)
        {
            required = FamilyBlues;
            return true;
        }
        required = 0;
        return false;
    }
}

/// <summary>Which DICT is being parsed; each scope admits a different whitelist.</summary>
internal enum Cff2DictScope : byte
{
    Top = 0,
    FontDict = 1,
    Private = 2
}

/// <summary>Single official CFF2 View: the Schema-declared five-byte header face plus
/// this hand-written partial with every codec walk (TopDICT, the CFF2 INDEX families,
/// FDSelect, FontDICT/PrivateDICT, the charstring lexical scan and the variation
/// store wrapper). No Generated*View exists beside it (AGENTS single-View rule); the
/// CFF1 INDEX/DICT codecs are deliberately NOT shared - the CFF2 INDEX is a
/// different layout (u32 count, empty = 4 bytes, spec L285) and TABLE-COVERAGE pins
/// the two families as non-mixable (card T5-05 §2.1). TryOpen performs the mechanical
/// header admission only; every strict wire rule lives in ValidateTable.</summary>
public readonly ref partial struct Cff2View
{
    private ReadStatus Unbound(out FontDiagnostic diagnostic) =>
        ReadWire.Fail(ReadStatus.Uninitialized, DiagnosticCode.UnboundView, Location, out diagnostic);

    // ---- CFF2 INDEX codec (u32 count, empty INDEX = exactly 4 bytes, spec L277-293) ----

    internal readonly record struct IndexSpan(int Start, int Count, int OffSize,
        int OffsetsAt, int DataAt, int End)
    {
        internal int Length => End - Start;
    }

    /// <summary>Mechanical INDEX delimitation shared by reads and strict validation:
    /// count/offSize/1-based monotone offsets with every byte inside the table. The
    /// additions are done in 64-bit: `at` can be a corrupt offset near int.MaxValue
    /// and the project builds with checked arithmetic.</summary>
    private ReadStatus LocateIndex(int at, out IndexSpan index, out FontDiagnostic diagnostic)
    {
        index = default;
        if (!IsBound)
            return Unbound(out diagnostic);
        if (at < 0 || (long)at + 4 > _data.Length)
            return ReadWire.Fail(ReadStatus.Truncated, DiagnosticCode.TruncatedData,
                Location.At(DataField.Count, (ulong)(uint)Math.Max(0, at)), out diagnostic,
                (ulong)_data.Length, (ulong)Math.Max(0, at) + 4);
        int start = at;
        uint rawCount = ReadWire.U32(_data, at);
        // A count above the table length can never be satisfied; refuse it before
        // the narrowing cast (the checked build would turn it into an overflow).
        if (rawCount > (uint)_data.Length)
            return ReadWire.Fail(ReadStatus.Truncated, DiagnosticCode.TruncatedData,
                Location.At(DataField.Count, (uint)at), out diagnostic,
                (ulong)rawCount, (ulong)_data.Length);
        int count = (int)rawCount;
        if (count == 0)
        {
            index = new IndexSpan(start, 0, 0, 0, 0, start + 4);
            diagnostic = default;
            return ReadStatus.Done;
        }
        if ((long)at + 5 > _data.Length)
            return ReadWire.Fail(ReadStatus.Truncated, DiagnosticCode.TruncatedData,
                Location.At(DataField.Length, (ulong)(uint)((long)at + 4)), out diagnostic,
                (ulong)_data.Length, (ulong)((long)at + 5));
        int offSize = _data[at + 4];
        if (offSize is < 1 or > 4)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.At(DataField.Length, (ulong)(uint)(at + 4)), out diagnostic,
                (uint)offSize, 4);
        long offsetsAt = at + 5;
        long offsetBytes = (long)(count + 1) * offSize;
        if (offsetsAt + offsetBytes > _data.Length)
            return ReadWire.Fail(ReadStatus.Truncated, DiagnosticCode.TruncatedData,
                Location.At(DataField.Count, (uint)at), out diagnostic,
                (ulong)_data.Length, (ulong)(offsetsAt + offsetBytes));
        long dataAt = offsetsAt + offsetBytes;
        long previous = ReadOffsetAt(offsetsAt, offSize);
        if (previous != 1)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.At(DataField.Offset, (ulong)offsetsAt), out diagnostic,
                (ulong)previous, 1);
        for (int i = 1; i <= count; i++)
        {
            long next = ReadOffsetAt(offsetsAt + (long)i * offSize, offSize);
            if (next < previous)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    Location.At(DataField.Offset, (ulong)(offsetsAt + (long)i * offSize)),
                    out diagnostic, (ulong)next, (ulong)previous);
            previous = next;
        }
        long end = dataAt + previous - 1;
        if (end > _data.Length)
            return ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.RangeOutsideSource,
                Location.At(DataField.Offset, (ulong)dataAt), out diagnostic,
                (ulong)end, (ulong)_data.Length);
        index = new IndexSpan(start, count, offSize, (int)offsetsAt, (int)dataAt, (int)end);
        diagnostic = default;
        return ReadStatus.Done;
    }

    private long ReadOffsetAt(long at, int offSize)
    {
        long value = 0;
        for (int i = 0; i < offSize; i++)
            value = (value << 8) | _data[(int)at + i];
        return value;
    }

    private ReadStatus IndexItemBounds(IndexSpan index, int ordinal, out int itemStart,
        out int itemLength, out FontDiagnostic diagnostic)
    {
        itemStart = 0;
        itemLength = 0;
        if (!IsBound)
            return Unbound(out diagnostic);
        if (ordinal < 0 || (uint)ordinal >= (uint)index.Count)
            return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                Location.At(DataField.Count, (uint)index.Start, ordinal), out diagnostic,
                unchecked((ulong)(long)ordinal), (uint)index.Count);
        long startOffset = ReadOffsetAt(index.OffsetsAt + (long)ordinal * index.OffSize, index.OffSize);
        long endOffset = ReadOffsetAt(index.OffsetsAt + (long)(ordinal + 1) * index.OffSize, index.OffSize);
        itemStart = (int)(index.DataAt + startOffset - 1);
        itemLength = (int)(endOffset - startOffset);
        diagnostic = default;
        return ReadStatus.Done;
    }

    private ReadStatus ReadIndexItem(IndexSpan index, int ordinal,
        out ReadOnlySpan<byte> item, out FontDiagnostic diagnostic)
    {
        item = default;
        ReadStatus status = IndexItemBounds(index, ordinal, out int itemStart, out int itemLength,
            out diagnostic);
        if (status != ReadStatus.Done) return status;
        item = _data.Slice(itemStart, itemLength);
        diagnostic = default;
        return ReadStatus.Done;
    }

    // ---- DICT walk (TopDICT / FontDICT / PrivateDICT scopes) ----

    /// <summary>Strict DICT entry decode over one scope's whitelist: exact per-key
    /// arity (arrays take one or more), duplicate-key refusal (spec L248 "must not be
    /// defined more than once"), operand-stack cap 513 (L193), stack-empty after every
    /// key operator (L254; the private blend operator is the exception and stays in
    /// the operand stream as a recorded modifier), 29 = the DICT-only int32 prefix,
    /// 255 = CharString-only and invalid here (card §3).</summary>
    private ReadStatus ParseDict(int start, int length, Cff2DictScope scope,
        out List<Cff2DictEntry> entries, out FontDiagnostic diagnostic)
    {
        entries = [];
        int end = start + length;
        int at = start;
        int entryStart = start;
        int operandStart = start;
        bool blendOperands = false;
        int blendCount = 0;
        var operands = new List<Cff2Operand>(8);
        while (at < end)
        {
            if (operands.Count == 0 && !blendOperands)
            {
                entryStart = at;
                operandStart = at;
            }
            byte b = _data[at++];
            if (b is >= 32 and <= 246)
            {
                operands.Add(new(b - 139, null));
            }
            else if (b is >= 247 and <= 250)
            {
                if (at >= end)
                    return FailDictTruncated(at, out diagnostic);
                operands.Add(new((b - 247) * 256 + _data[at++] + 108, null));
            }
            else if (b is >= 251 and <= 254)
            {
                if (at >= end)
                    return FailDictTruncated(at, out diagnostic);
                operands.Add(new(-(b - 251) * 256 - _data[at++] - 108, null));
            }
            else if (b is 28 or 29)
            {
                int size = b == 28 ? 2 : 4;
                if (at + size > end)
                    return FailDictTruncated(at, out diagnostic);
                int value = size == 2
                    ? BinaryPrimitives.ReadInt16BigEndian(_data.Slice(at, 2))
                    : BinaryPrimitives.ReadInt32BigEndian(_data.Slice(at, 4));
                operands.Add(new(value, null));
                at += size;
            }
            else if (b == 30)
            {
                if (at >= end)
                    return FailDictTruncated(at, out diagnostic);
                ReadStatus status = ScanReal(at, end, out int tokenEnd, out diagnostic);
                if (status != ReadStatus.Done) return status;
                operands.Add(new(0, _data.Slice(at, tokenEnd - at).ToArray()));
                at = tokenEnd;
            }
            else if (b == 255)
            {
                // 255 encodes a CharString Fixed number; in DICT data it is invalid.
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    Location.At(DataField.Payload, (uint)(at - 1)), out diagnostic,
                    (uint)b, 29);
            }
            else
            {
                int op = b;
                int operatorStart = at - 1;
                if (b == 12)
                {
                    if (at >= end)
                        return FailDictTruncated(at, out diagnostic);
                    op = 0x0c00 | _data[at++];
                }
                if (scope == Cff2DictScope.Private && op == Cff2Operators.PrivateBlend)
                {
                    // Not a key: blend modifies the pending operands in place
                    // (spec L247-256). The pairing with a blendable key stays an
                    // observation (OQ-T5-05-6); the walk keeps accumulating.
                    blendOperands = true;
                    blendCount++;
                    continue;
                }
                int arity = Cff2Operators.Arity(scope, op);
                if (arity == -2)
                    return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                        Location.At(DataField.Payload, (uint)operatorStart), out diagnostic,
                        (uint)op);
                // Exact arity is decidable only where blend cannot occur (Top/Font);
                // a blend-modified PrivateDICT operand region carries default values,
                // n*k deltas and n itself on the wire (spec L1487-1496), so the key's
                // lexical operand count is inflated and only non-emptiness stays
                // checkable (the card's PrivateDICT hard list has no arity face).
                if (operands.Count == 0 ||
                    (scope != Cff2DictScope.Private && arity >= 0 && operands.Count != arity))
                    return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                        Location.At(DataField.Payload, (uint)entryStart), out diagnostic,
                        (uint)operands.Count, (uint)Math.Max(0, arity));
                foreach (var existing in entries)
                {
                    if (existing.Operator == op)
                        return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.DuplicateTag,
                            Location.At(DataField.Payload, (uint)entryStart), out diagnostic,
                            (uint)op);
                }
                entries.Add(new Cff2DictEntry(op, [.. operands],
                    _data.Slice(entryStart, at - entryStart).ToArray(), blendCount)
                { EntryStart = entryStart });
                operands.Clear();
                blendOperands = false;
                blendCount = 0;
            }
            if (operands.Count > Cff2Operators.MaxDictOperandStack)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    Location.At(DataField.Payload, (uint)operandStart), out diagnostic,
                    (uint)operands.Count, (uint)Cff2Operators.MaxDictOperandStack);
        }
        // The stack-empty end face (L254) is decidable only without blend operands;
        // a PrivateDICT's trailing region stays outside the hard list for the same
        // lexical reason as the arity face above.
        if (scope != Cff2DictScope.Private && (operands.Count != 0 || blendOperands))
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.At(DataField.Payload, (uint)entryStart), out diagnostic,
                (uint)operands.Count);
        diagnostic = default;
        return ReadStatus.Done;
    }

    private ReadStatus FailDictTruncated(int at, out FontDiagnostic diagnostic) =>
        ReadWire.Fail(ReadStatus.Truncated, DiagnosticCode.TruncatedData,
            Location.At(DataField.Payload, (uint)at), out diagnostic,
            (ulong)_data.Length, (ulong)at + 1);

    /// <summary>Structural BCD real scan: nibble stream with the 0xF terminator, the
    /// reserved 0xD nibble refused, bounded digit count. The value itself is never
    /// decoded on the product path (the bytes travel verbatim).</summary>
    private ReadStatus ScanReal(int at, int end, out int tokenEnd, out FontDiagnostic diagnostic)
    {
        tokenEnd = 0;
        int digits = 0;
        int p = at;
        while (true)
        {
            if (p >= end)
                return ReadWire.Fail(ReadStatus.Truncated, DiagnosticCode.TruncatedData,
                    Location.At(DataField.Payload, (uint)at), out diagnostic,
                    (ulong)_data.Length, (ulong)p + 1);
            byte b = _data[p++];
            for (int shift = 4; shift >= 0; shift -= 4)
            {
                int nibble = (b >> shift) & 15;
                if (nibble == 15)
                {
                    if (digits == 0)
                        return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                            Location.At(DataField.Payload, (uint)at), out diagnostic);
                    tokenEnd = p;
                    diagnostic = default;
                    return ReadStatus.Done;
                }
                if (nibble == 13)
                    return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                        Location.At(DataField.Payload, (uint)(p - 1)), out diagnostic,
                        (uint)nibble);
                digits++;
                if (digits > Cff2Operators.MaxRealNibbleDigits)
                    return ReadWire.Fail(ReadStatus.ResourceLimit, DiagnosticCode.BudgetExceeded,
                        Location.At(DataField.Payload, (uint)at), out diagnostic,
                        (uint)digits, (uint)Cff2Operators.MaxRealNibbleDigits);
            }
        }
    }

    /// <summary>Integer operands of one entry; a real operand where the key's stack
    /// shape demands an integer (offsets, counts, selectors) is the InvalidValue face
    /// (the CFF1 EntryIntegers precedent).</summary>
    private ReadStatus EntryIntegers(Cff2DictEntry entry, out int[] operands,
        out FontDiagnostic diagnostic)
    {
        operands = [];
        var result = new int[entry.Operands.Count];
        for (int i = 0; i < entry.Operands.Count; i++)
        {
            Cff2Operand operand = entry.Operands[i];
            if (operand.IsReal)
                return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                    Location.At(DataField.Payload, (uint)entry.EntryStart), out diagnostic,
                    (uint)entry.Operator);
            result[i] = operand.Value;
        }
        operands = result;
        diagnostic = default;
        return ReadStatus.Done;
    }

    // ---- TopDICT location helpers ----

    /// <summary>The TopDICT window: addressed by the declared headerSize (spec L308
    /// "must be used when locating"), never hard-pinned to 5 (OQ-T5-05-3 lean); a
    /// headerSize below the fixed header or a window past the table is the native
    /// bounds face.</summary>
    private ReadStatus LocateTopDict(out int start, out int length, out FontDiagnostic diagnostic)
    {
        start = 0;
        length = 0;
        if (!IsBound)
            return Unbound(out diagnostic);
        ReadStatus status = TryGetHeaderSize(out byte headerSize, out diagnostic);
        if (status != ReadStatus.Done) return status;
        if (headerSize < Cff2Layout.HeaderLength)
            return ReadWire.Fail(ReadStatus.InvalidOffset, DiagnosticCode.RangeOutsideSource,
                Location.At(DataField.Length, Cff2Layout.HeaderSizeAt), out diagnostic,
                headerSize, Cff2Layout.HeaderLength);
        status = TryGetTopDictSize(out ushort topDictSize, out diagnostic);
        if (status != ReadStatus.Done) return status;
        long end = (long)headerSize + topDictSize;
        if (end > _data.Length)
            return ReadWire.Fail(ReadStatus.Truncated, DiagnosticCode.TruncatedData,
                Location.At(DataField.Length, Cff2Layout.TopDictSizeAt), out diagnostic,
                (ulong)_data.Length, (ulong)end);
        start = headerSize;
        length = topDictSize;
        diagnostic = default;
        return ReadStatus.Done;
    }

    private ReadStatus ParseTopDict(out List<Cff2DictEntry> entries, out int topDictStart,
        out FontDiagnostic diagnostic)
    {
        entries = [];
        ReadStatus status = LocateTopDict(out topDictStart, out int length, out diagnostic);
        if (status != ReadStatus.Done) return status;
        return ParseDict(topDictStart, length, Cff2DictScope.Top, out entries, out diagnostic);
    }

    private ReadStatus ParseTopDict(out List<Cff2DictEntry> entries, out int topDictStart,
        out int topDictLength, out FontDiagnostic diagnostic)
    {
        topDictLength = 0;
        ReadStatus status = ParseTopDict(out entries, out topDictStart, out diagnostic);
        if (status != ReadStatus.Done) return status;
        status = TryGetTopDictSize(out ushort size, out diagnostic);
        if (status != ReadStatus.Done) return status;
        topDictLength = size;
        diagnostic = default;
        return ReadStatus.Done;
    }

    private static bool TryFindEntry(List<Cff2DictEntry> entries, int op, out Cff2DictEntry? entry)
    {
        foreach (var candidate in entries)
        {
            if (candidate.Operator == op)
            {
                entry = candidate;
                return true;
            }
        }
        entry = null;
        return false;
    }

    private ReadStatus TopDictOffset(Cff2DictEntry entry, out int offset, out FontDiagnostic diagnostic)
    {
        offset = 0;
        ReadStatus status = EntryIntegers(entry, out int[] operands, out diagnostic);
        if (status != ReadStatus.Done) return status;
        offset = operands[0];
        diagnostic = default;
        return ReadStatus.Done;
    }
}
