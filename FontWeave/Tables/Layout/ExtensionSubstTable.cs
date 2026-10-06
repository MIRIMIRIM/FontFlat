using FontWeave.Core;

namespace FontWeave.Tables.Layout;

/// <summary>One extension-wrapped GSUB subtable read through the type-7 branch: the
/// declared wrapped lookup type plus exactly one typed direct leaf (the finite
/// one-level unwrap; wrapped types 5/6/8 and nested 7 never produce this model through
/// a typed read, they refuse, and whole-table reads degrade those lookups to
/// metadata-only, OQ-T4-03-4).</summary>
public sealed class ExtensionSubstTable
{
    internal ExtensionSubstTable(ushort extensionLookupType, SingleSubstTable? single,
        MultipleSubstTable? multiple, AlternateSubstTable? alternate, LigatureSubstTable? ligature,
        DataLocation location, int consumedLength)
    {
        ExtensionLookupType = extensionLookupType;
        Single = single;
        Multiple = multiple;
        Alternate = alternate;
        Ligature = ligature;
        Location = location;
        ConsumedLength = consumedLength;
    }

    public ushort ExtensionLookupType { get; }
    public SingleSubstTable? Single { get; }
    public MultipleSubstTable? Multiple { get; }
    public AlternateSubstTable? Alternate { get; }
    public LigatureSubstTable? Ligature { get; }
    /// <summary>The extension header's own location (not the wrapped body's).</summary>
    public DataLocation Location { get; }
    /// <summary>Header length plus the wrapped leaf's consumed length.</summary>
    public int ConsumedLength { get; }

    /// <summary>Explicit wrapper factory for the extension-wrapped rule edit: the
    /// wrapped lookup type must be one of the finite direct leaves and exactly that
    /// leaf's model must be supplied (the others stay null).</summary>
    public static ReadStatus TryCreate(ushort extensionLookupType, SingleSubstTable? single,
        MultipleSubstTable? multiple, AlternateSubstTable? alternate, LigatureSubstTable? ligature,
        out ExtensionSubstTable? model, out FontDiagnostic diagnostic, DataLocation location = default)
    {
        model = null;
        if (extensionLookupType is not (1 or 2 or 3 or 4))
            return ReadWire.Fail(ReadStatus.UnsupportedFormat, DiagnosticCode.UnsupportedOperation,
                location.AtLocal(DataField.Tag, ExtensionSubstLayout.TypeAt), out diagnostic,
                extensionLookupType);
        int leaves = (single is null ? 0 : 1) + (multiple is null ? 0 : 1) +
            (alternate is null ? 0 : 1) + (ligature is null ? 0 : 1);
        if (leaves != 1)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                location.AtLocal(DataField.Tag, ExtensionSubstLayout.TypeAt), out diagnostic,
                (ulong)leaves, 1UL);
        // The one supplied leaf must be the one the declared type names.
        bool matches = extensionLookupType switch
        {
            1 => single is not null,
            2 => multiple is not null,
            3 => alternate is not null,
            _ => ligature is not null,
        };
        if (!matches)
            return ReadWire.Fail(ReadStatus.InvalidFormat, DiagnosticCode.InvalidValue,
                location.AtLocal(DataField.Tag, ExtensionSubstLayout.TypeAt), out diagnostic,
                extensionLookupType);
        int consumed = ExtensionSubstLayout.HeaderLength + extensionLookupType switch
        {
            1 => single!.ConsumedLength,
            2 => multiple!.ConsumedLength,
            3 => alternate!.ConsumedLength,
            _ => ligature!.ConsumedLength,
        };
        model = new(extensionLookupType, single, multiple, alternate, ligature, location, consumed);
        diagnostic = default;
        return ReadStatus.Done;
    }
}
