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
}
