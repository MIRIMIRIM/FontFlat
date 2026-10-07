using FontWeave.Core;

namespace FontWeave.Tables.Layout;

/// <summary>One extension-wrapped GPOS subtable read through the type-9 branch: the
/// declared wrapped lookup type plus exactly one typed direct leaf (the finite
/// one-level unwrap; wrapped=9 and unknown values never produce this model through a
/// typed read, they refuse, and whole-table reads degrade those lookups to
/// metadata-only, the OQ-T4-03-4 discipline). The direct leaves are types 1/2/3
/// (T4-05), the mark family 4/5/6 and the GSUB-common contextual pair 7/8 (T4-06;
/// spec forbids only the wrapped=9 self-reference).</summary>
public sealed class ExtensionPosTable
{
    internal ExtensionPosTable(ushort extensionLookupType, SinglePosTable? single,
        PairPosTable? pair, CursivePosTable? cursive, DataLocation location, int consumedLength,
        MarkBasePosTable? markBase = null, MarkLigPosTable? markLig = null,
        MarkMarkPosTable? markMark = null, ContextualSubstTable? contextual = null,
        ChainingSubstTable? chaining = null)
    {
        ExtensionLookupType = extensionLookupType;
        Single = single;
        Pair = pair;
        Cursive = cursive;
        MarkBase = markBase;
        MarkLig = markLig;
        MarkMark = markMark;
        Contextual = contextual;
        Chaining = chaining;
        Location = location;
        ConsumedLength = consumedLength;
    }

    public ushort ExtensionLookupType { get; }
    public SinglePosTable? Single { get; }
    public PairPosTable? Pair { get; }
    public CursivePosTable? Cursive { get; }
    public MarkBasePosTable? MarkBase { get; }
    public MarkLigPosTable? MarkLig { get; }
    public MarkMarkPosTable? MarkMark { get; }
    public ContextualSubstTable? Contextual { get; }
    public ChainingSubstTable? Chaining { get; }
    /// <summary>The extension header's own location (not the wrapped body's).</summary>
    public DataLocation Location { get; }
    /// <summary>Header length plus the wrapped leaf's consumed length.</summary>
    public int ConsumedLength { get; }

    /// <summary>Explicit wrapper factory: the wrapped lookup type must be one of the
    /// finite direct leaves (wrapped=9's spec self-reference ban included) and exactly
    /// that leaf's model must be supplied (the others stay null).</summary>
    public static ReadStatus TryCreate(ushort extensionLookupType, SinglePosTable? single,
        PairPosTable? pair, CursivePosTable? cursive, out ExtensionPosTable? model,
        out FontDiagnostic diagnostic, DataLocation location = default,
        MarkBasePosTable? markBase = null, MarkLigPosTable? markLig = null,
        MarkMarkPosTable? markMark = null, ContextualSubstTable? contextual = null,
        ChainingSubstTable? chaining = null)
    {
        model = null;
        if (extensionLookupType is not (1 or 2 or 3 or 4 or 5 or 6 or 7 or 8))
            return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
                location.AtLocal(DataField.Tag, ExtensionPosLayout.TypeAt), out diagnostic,
                extensionLookupType);
        int leaves = (single is null ? 0 : 1) + (pair is null ? 0 : 1) + (cursive is null ? 0 : 1) +
            (markBase is null ? 0 : 1) + (markLig is null ? 0 : 1) + (markMark is null ? 0 : 1) +
            (contextual is null ? 0 : 1) + (chaining is null ? 0 : 1);
        if (leaves != 1)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                location.AtLocal(DataField.Tag, ExtensionPosLayout.TypeAt), out diagnostic,
                (ulong)leaves, 1UL);
        // The one supplied leaf must be the one the declared type names.
        bool matches = extensionLookupType switch
        {
            1 => single is not null,
            2 => pair is not null,
            3 => cursive is not null,
            4 => markBase is not null,
            5 => markLig is not null,
            6 => markMark is not null,
            7 => contextual is not null,
            _ => chaining is not null,
        };
        if (!matches)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                location.AtLocal(DataField.Tag, ExtensionPosLayout.TypeAt), out diagnostic,
                extensionLookupType);
        int consumed = ExtensionPosLayout.HeaderLength + extensionLookupType switch
        {
            1 => single!.ConsumedLength,
            2 => pair!.ConsumedLength,
            3 => cursive!.ConsumedLength,
            4 => markBase!.ConsumedLength,
            5 => markLig!.ConsumedLength,
            6 => markMark!.ConsumedLength,
            7 => contextual!.ConsumedLength,
            _ => chaining!.ConsumedLength,
        };
        model = new(extensionLookupType, single, pair, cursive, location, consumed,
            markBase, markLig, markMark, contextual, chaining);
        diagnostic = default;
        return ReadStatus.Done;
    }
}
