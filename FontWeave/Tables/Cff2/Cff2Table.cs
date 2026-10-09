using FontWeave.Core;
using FontWeave.Tables.Variation;

namespace FontWeave.Tables;

/// <summary>One CFF2 DICT operand: an integer value, or a real number kept as its
/// verbatim wire bytes (RealBytes non-null; BCD reals are never decoded to a number
/// on the product path - the write side passes them through byte-for-byte, the
/// Cff1TableCompiler real-operand policy).</summary>
public readonly record struct Cff2Operand(int Value, byte[]? RealBytes)
{
    public bool IsReal => RealBytes is not null;
}

/// <summary>One decoded CFF2 DICT entry: operator, operand list, the entry's raw wire
/// bytes and the number of PrivateDICT blend operators in the operand region (the
/// blend-modified shape; blend itself creates no key-value pair, spec L247-256).</summary>
public sealed record Cff2DictEntry(int Operator, IReadOnlyList<Cff2Operand> Operands,
    byte[] RawBytes, int BlendCount)
{
    /// <summary>The entry's byte position inside the CFF2 table (diagnostic face).</summary>
    public int EntryStart { get; internal init; }

    public bool BlendOperands => BlendCount > 0;
}

/// <summary>One decoded PrivateDICT: the key entries (16-operator whitelist), the
/// LocalSubrINDEX items when key 0x13 is present, and the lexical variation
/// bookkeeping (blend count, vsindex operand when key 0x16 is present). Blendable
/// pairing and value semantics stay observation-only (OQ-T5-05-6).</summary>
public sealed record Cff2PrivateDict(
    IReadOnlyList<Cff2DictEntry> Entries,
    byte[][] LocalSubrs,
    int BlendCount,
    int? VsindexValue)
{
    public bool HasLocalSubrs => LocalSubrs.Length > 0;
}

/// <summary>One FontDICT: the single 0x12 entry and its resolved PrivateDICT (NULL for
/// the empty `0 0` form).</summary>
public sealed record Cff2FontDict(Cff2DictEntry Entry, Cff2PrivateDict? Private)
{
    public int PrivateSize => (int)Entry.Operands[0].Value;
    public int PrivateOffset => (int)Entry.Operands[1].Value;
}

/// <summary>Lexical charstring aggregates over the whole CharStringINDEX (OQ-T5-05-11:
/// single token pass, no call-graph following, no Type2 interpretation). Mask and
/// subr-index observations are artifacts-of-lexing flags, never refusals; only the
/// 65535-byte per-item bound and the no-store blend/vsindex gate are hard faces.</summary>
public sealed record Cff2CharstringStats(
    int BlendCount,
    int VsindexCount,
    int? LastVsindexValue,
    int CallSubrCount,
    int CallGsubrCount,
    int HintmaskCount,
    int CntrmaskCount,
    int MaskWithoutStems,
    int MaskBytesTruncated,
    int TruncatedTokens,
    int MaxStackPeak,
    int MaxItemLength);

/// <summary>Owned CFF2 2.0 table: the TopDICT entries (BCD operand bytes preserved),
/// the four INDEX families fully materialized, the per-GID FontDICT index, the
/// PrivateDICTs with their subroutines and variation bookkeeping, and the reused
/// T5-01 variation store (the +2 length-prefixed slice, materialized through
/// ItemVariationStoreView - never a parallel reader). Charstring, subroutine and
/// private bytes are preserved verbatim and never interpreted: this is not a Type2
/// interpreter, a path builder or a blend evaluator (card T5-05 §11). Produced by
/// Cff2View.TryReadTable and edited through the Cff2TableCompiler API family.</summary>
public sealed class Cff2Table
{
    private readonly byte[][] _globalSubrs;
    private readonly byte[][] _charStrings;
    private readonly Cff2FontDict[] _fontDicts;
    private readonly int[] _fdIndex;

    internal Cff2Table(byte minorVersion, byte headerSize, Cff2DictEntry[] topDict,
        bool hasVariationStore, byte[][] globalSubrs, byte[][] charStrings,
        Cff2FontDict[] fontDicts, int[] fdIndex, VariationStoreTable? store,
        Cff2CharstringStats charstringStats, DataLocation location, int consumedLength)
    {
        MinorVersion = minorVersion;
        HeaderSize = headerSize;
        TopDictInternal = topDict;
        HasVariationStore = hasVariationStore;
        _globalSubrs = globalSubrs;
        _charStrings = charStrings;
        _fontDicts = fontDicts;
        _fdIndex = fdIndex;
        Store = store;
        CharstringStats = charstringStats;
        Location = location;
        ConsumedLength = consumedLength;
    }

    /// <summary>Wide-read fact: always 0 (the strict 2.0 face refuses nonzero minor
    /// versions at validation, OQ-T5-05-2).</summary>
    public byte MinorVersion { get; }

    /// <summary>The declared header size, preserved verbatim - the write side always
    /// re-emits 5, the 2.0 corpus constant (OQ-T5-05-3 lean).</summary>
    public byte HeaderSize { get; }

    public IReadOnlyList<Cff2DictEntry> TopDict => TopDictInternal;
    internal Cff2DictEntry[] TopDictInternal { get; }

    /// <summary>Whether the TopDICT carries the VariationStoreOffset key (0x18). The
    /// static face (false) forbids blend/vsindex everywhere (OQ-T5-05-6).</summary>
    public bool HasVariationStore { get; }

    /// <summary>The FontMatrix entry (0x0c07) when present; its BCD scale operands
    /// stay verbatim. Cross-table unitsPerEm coupling is never checked here.</summary>
    public Cff2DictEntry? FontMatrix => FindEntry(TopDictInternal, Cff2Operators.TopFontMatrix);

    public int NumGlyphs => _charStrings.Length;

    public IReadOnlyList<byte[]> GlobalSubrs => Array.AsReadOnly(_globalSubrs);

    /// <summary>Per-GID charstring bytes; zero-length items are the spec's
    /// non-printing-glyph form and are legal for any glyph including .notdef.</summary>
    public IReadOnlyList<byte[]> CharStrings => Array.AsReadOnly(_charStrings);

    public IReadOnlyList<Cff2FontDict> FontDicts => Array.AsReadOnly(_fontDicts);

    public int FontDictCount => _fontDicts.Length;

    /// <summary>Per-GID FontDICT index (glyph 0 first); a table without FDSelect maps
    /// every glyph to FontDICT 0 (the single-FD face).</summary>
    public IReadOnlyList<int> FdIndex => Array.AsReadOnly(_fdIndex);

    /// <summary>The reused item variation store; NULL exactly when the TopDICT omits
    /// key 0x18 (the static face).</summary>
    public VariationStoreTable? Store { get; }

    /// <summary>The store's declared axis count; 0 on the static face. The caller's
    /// fvar axis count is the validation-time authority (OQ-T5-05-7 signature coupling).</summary>
    public int AxisCount => Store?.AxisCount ?? 0;

    public Cff2CharstringStats CharstringStats { get; }

    public DataLocation Location { get; }

    /// <summary>The declared table extent: the highest end of every walked block.</summary>
    public int ConsumedLength { get; }

    internal Cff2FontDict[] FontDictsInternal => _fontDicts;
    internal int[] FdIndexInternal => _fdIndex;
    internal byte[][] GlobalSubrsInternal => _globalSubrs;
    internal byte[][] CharStringsInternal => _charStrings;

    internal static Cff2DictEntry? FindEntry(IReadOnlyList<Cff2DictEntry> entries, int op)
    {
        foreach (var entry in entries)
            if (entry.Operator == op)
                return entry;
        return null;
    }
}
