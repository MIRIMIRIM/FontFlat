using FontWeave.Core;

namespace FontWeave.Tables.Variation;

/// <summary>A decoded delta-set index: the (outer, inner) row key into the item
/// variation store. 0xFFFF/0xFFFF is the spec's no-variation-data sentinel and is
/// carried as a normal value - the dereference layer interprets it (card 3.2).</summary>
public readonly record struct DeltaSetIndex(ushort Outer, ushort Inner)
{
    /// <summary>The spec sentinel: "a delta-set index 0xFFFF/0xFFFF ... is used to
    /// indicate that there is no variation data for a given item".</summary>
    public bool IsNoVariationData => Outer == 0xFFFF && Inner == 0xFFFF;
}

/// <summary>Owned DeltaSetIndexMap (format 0): every logical entry decoded to its
/// (outer, inner) pair. Entries beyond the declared count never exist here - the
/// last-entry fallback is a dereference-time rule keyed on the caller's index
/// (spec: "greater than or equal to mapCount ... the last logical entry").</summary>
public sealed class DeltaSetIndexMapTable
{
    private readonly DeltaSetIndex[] _entries;

    internal DeltaSetIndexMapTable(byte entryFormat, DeltaSetIndex[] entries,
        DataLocation location, int consumedLength)
    {
        EntryFormat = entryFormat;
        _entries = entries;
        Location = location;
        ConsumedLength = consumedLength;
    }

    public byte EntryFormat { get; }
    public int MapCount => _entries.Length;
    public DataLocation Location { get; }
    public int ConsumedLength { get; }

    /// <summary>The logical entry for a base-zero index; an index greater or equal to
    /// the map count resolves to the last logical entry (spec otvarcommonformats.md).
    /// An empty map has no entry to fall back to - the HVAR-domain validation refuses
    /// that shape before dereference (OQ-T5-01-3).</summary>
    public bool TryGetMapping(int index, out DeltaSetIndex entry)
    {
        if (_entries.Length == 0)
        {
            entry = default;
            return false;
        }
        entry = _entries[index >= _entries.Length ? _entries.Length - 1 : index];
        return true;
    }
}
