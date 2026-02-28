namespace OTFontFile2.Tables;

/// <summary>
/// Mutable builder for GPOS ExtensionPos subtables (lookup type 9), format 1.
/// </summary>
public sealed class GposExtensionPosSubtableBuilder
{
    private ushort _extensionLookupType;
    private ReadOnlyMemory<byte> _subtableData;

    private bool _dirty = true;
    private byte[]? _built;

    public ushort ExtensionLookupType
    {
        get => _extensionLookupType;
        set
        {
            if (value == _extensionLookupType)
                return;

            _extensionLookupType = value;
            MarkDirty();
        }
    }

    public ReadOnlyMemory<byte> SubtableData => _subtableData;

    public void Clear()
    {
        _extensionLookupType = 0;
        _subtableData = ReadOnlyMemory<byte>.Empty;
        MarkDirty();
    }

    public void SetSubtableData(ushort extensionLookupType, ReadOnlyMemory<byte> subtableData)
    {
        if (subtableData.IsEmpty)
            throw new ArgumentException("Extension subtable data must be non-empty.", nameof(subtableData));

        _extensionLookupType = extensionLookupType;
        _subtableData = subtableData;
        MarkDirty();
    }

    public byte[] ToArray()
    {
        EnsureBuilt();
        return _built!;
    }

    public ReadOnlyMemory<byte> ToMemory() => EnsureBuilt();

    private void MarkDirty()
    {
        _dirty = true;
        _built = null;
    }

    private ReadOnlyMemory<byte> EnsureBuilt()
    {
        if (!_dirty && _built is not null)
            return _built;

        _built = BuildBytes();
        _dirty = false;
        return _built;
    }

    private byte[] BuildBytes()
    {
        if (_subtableData.IsEmpty)
            throw new InvalidOperationException("ExtensionPos subtable is not configured. Call SetSubtableData(...).");

        var w = new OTFontFile2.OffsetWriter();
        var subtableLabel = w.CreateLabel();

        w.WriteUInt16(1);
        w.WriteUInt16(_extensionLookupType);
        w.WriteOffset32(subtableLabel, baseOffset: 0);

        w.Align2();
        w.DefineLabelHere(subtableLabel);
        w.WriteBytes(_subtableData);

        return w.ToArray();
    }
}

