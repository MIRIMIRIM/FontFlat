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
        Cff2FontDict[] fontDicts, int fdSelectFormat, int[] fdIndex, VariationStoreTable? store,
        Cff2CharstringStats charstringStats, DataLocation location, int consumedLength)
    {
        MinorVersion = minorVersion;
        HeaderSize = headerSize;
        TopDictInternal = topDict;
        HasVariationStore = hasVariationStore;
        _globalSubrs = globalSubrs;
        _charStrings = charStrings;
        _fontDicts = fontDicts;
        FdSelectFormat = fdSelectFormat;
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

    /// <summary>The FDSelect wire format the table carries: 0, 3 or 4; -1 when the
    /// table has no FDSelect (the single-FD face). Preserved verbatim so the
    /// compiler's re-emission keeps the source's format family.</summary>
    public int FdSelectFormat { get; }

    /// <summary>Whether the table carries an FDSelect block (exactly the more-than-
    /// one-FontDICT face, spec L366/L390).</summary>
    public bool HasFdSelect => FdSelectFormat != -1;

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

    // ---- the edit API family (card T5-05 §4.1, OQ-T5-05-9) ----
    // Three supported edits, each returning a new owned model and never mutating
    // the source; every other change - FontDICT or charstring set changes, FDSelect
    // changes, TopDICT key add/remove/reorder, FontMatrix rewrites, version
    // upgrades - has no edit surface at all, which is the family's finite refusal.

    /// <summary>Rewrites one PrivateDICT single-integer hint-domain scalar (StdHW,
    /// StdVW, BlueScale, BlueShift, BlueFuzz, LanguageGroup, ExpansionFactor). The
    /// key must already exist, carry exactly one integer operand and stay
    /// blend-free: a blend-interleaved operand region is the evaluation domain and
    /// refuses (OQ-T5-05-9), as do the structural keys (the 0x13 offset and the
    /// 0x16 variation selector), the blend modifier itself, array keys and no-op
    /// rewrites (the Cff1PrivateValueEdit family shape).</summary>
    public ReadStatus TryWithPrivateScalar(int fdIndex, int op, int value,
        out Cff2Table? updated, out FontDiagnostic diagnostic)
    {
        updated = null;
        if (fdIndex < 0 || fdIndex >= _fontDicts.Length)
            return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                Location.AtLocal(DataField.FdIndex, 0, fdIndex), out diagnostic,
                unchecked((ulong)(long)fdIndex), (uint)_fontDicts.Length);
        var privateDict = _fontDicts[fdIndex].Private;
        if (privateDict is null)
            return UnsupportedOperation((uint)op, out diagnostic);
        if (op is not (Cff2Operators.StdHW or Cff2Operators.StdVW or Cff2Operators.BlueScale
            or Cff2Operators.BlueShift or Cff2Operators.BlueFuzz or Cff2Operators.LanguageGroup
            or Cff2Operators.ExpansionFactor))
            return UnsupportedOperation((uint)op, out diagnostic);
        Cff2DictEntry? target = null;
        foreach (var entry in privateDict.Entries)
        {
            if (entry.Operator == op)
            {
                target = entry;
                break;
            }
        }
        if (target is null || target.BlendOperands
            || target.Operands.Count != 1 || target.Operands[0].IsReal
            || target.Operands[0].Value == value)
            return UnsupportedOperation((uint)op, out diagnostic);
        var operand = new Cff2Operand(value, null);
        var rewritten = new Cff2DictEntry(op, [operand],
            Cff2TableCompiler.EncodeEntryRaw(op, [operand]), 0);
        var entries = new List<Cff2DictEntry>(privateDict.Entries.Count);
        foreach (var entry in privateDict.Entries)
            entries.Add(ReferenceEquals(entry, target) ? rewritten : entry);
        var updatedPrivate = new Cff2PrivateDict(entries, privateDict.LocalSubrs,
            entries.Sum(static entry => entry.BlendCount), privateDict.VsindexValue);
        var fontDicts = (Cff2FontDict[])_fontDicts.Clone();
        fontDicts[fdIndex] = new Cff2FontDict(_fontDicts[fdIndex].Entry, updatedPrivate);
        updated = new Cff2Table(MinorVersion, HeaderSize, TopDictInternal, HasVariationStore,
            _globalSubrs, _charStrings, fontDicts, FdSelectFormat, _fdIndex, Store,
            CharstringStats, Location, ConsumedLength);
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>Replaces one glyph's charstring bytes (byte-level substitution, no
    /// interpretation): the zero-length non-printing form stays legal and the
    /// 65535-byte per-item bound (spec L795) is re-enforced on the incoming bytes.</summary>
    public ReadStatus TryWithCharString(int glyphId, byte[] charstring,
        out Cff2Table? updated, out FontDiagnostic diagnostic)
    {
        updated = null;
        ArgumentNullException.ThrowIfNull(charstring);
        if (glyphId < 0 || glyphId >= _charStrings.Length)
            return ReadWire.Fail(ReadStatus.InvalidIndex, DiagnosticCode.IndexOutOfRange,
                Location.AtLocal(DataField.GlyphId, 0, glyphId), out diagnostic,
                unchecked((ulong)(long)glyphId), (uint)_charStrings.Length);
        if (charstring.Length > Cff2Operators.MaxCharstringLength)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Location.AtLocal(DataField.Length, 0, glyphId), out diagnostic,
                (uint)charstring.Length, (uint)Cff2Operators.MaxCharstringLength);
        var charStrings = (byte[][])_charStrings.Clone();
        charStrings[glyphId] = (byte[])charstring.Clone();
        updated = new Cff2Table(MinorVersion, HeaderSize, TopDictInternal, HasVariationStore,
            _globalSubrs, charStrings, _fontDicts, FdSelectFormat, _fdIndex, Store,
            CharstringStats, Location, ConsumedLength);
        diagnostic = default;
        return ReadStatus.Done;
    }

    /// <summary>Re-roots one variation region's axis triples through the T5-01
    /// compiler (the store edit family); the static face has no store and refuses.
    /// The triple count is the store's declared axis count, per the region layout.</summary>
    public ReadStatus TryWithRegionAxes(int region, ReadOnlySpan<VariationRegionAxis> axes,
        out Cff2Table? updated, out FontDiagnostic diagnostic)
    {
        updated = null;
        if (Store is null)
            return UnsupportedOperation((uint)region, out diagnostic);
        if (axes.Length != Store.AxisCount)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                Store.Location.AtLocal(DataField.Count, 0), out diagnostic,
                (uint)axes.Length, (uint)Store.AxisCount);
        ReadStatus status = VariationStoreTableCompiler.TryWithRegionAxes(Store, region, axes,
            out var updatedStore, out diagnostic);
        if (status != ReadStatus.Done) return status;
        updated = new Cff2Table(MinorVersion, HeaderSize, TopDictInternal, HasVariationStore,
            _globalSubrs, _charStrings, _fontDicts, FdSelectFormat, _fdIndex, updatedStore,
            CharstringStats, Location, ConsumedLength);
        diagnostic = default;
        return ReadStatus.Done;
    }

    private ReadStatus UnsupportedOperation(ulong detail, out FontDiagnostic diagnostic) =>
        ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
            Location.AtLocal(DataField.Payload, detail), out diagnostic, detail);
}
