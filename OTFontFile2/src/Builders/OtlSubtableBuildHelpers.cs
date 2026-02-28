namespace OTFontFile2.Tables;

/// <summary>
/// Shared helpers for building OTL subtables that reference Device/VariationIndex tables.
/// </summary>
internal sealed class DeviceTablePool
{
    private readonly Dictionary<DeviceTableBuilder, OTFontFile2.OffsetWriter.Label> _labelByDevice
        = new(ReferenceEqualityComparer.Instance);

    private readonly List<(DeviceTableBuilder device, OTFontFile2.OffsetWriter.Label label)> _devicesInOrder = new();

    public bool HasAny => _devicesInOrder.Count != 0;

    public void WriteOffset16(OTFontFile2.OffsetWriter writer, DeviceTableBuilder device, int baseOffset)
    {
        if (writer is null) throw new ArgumentNullException(nameof(writer));
        if (device is null) throw new ArgumentNullException(nameof(device));

        var label = GetOrAddLabel(writer, device);
        writer.WriteOffset16(label, baseOffset);
    }

    public void EmitAllAligned2(OTFontFile2.OffsetWriter writer)
    {
        if (writer is null) throw new ArgumentNullException(nameof(writer));

        for (int i = 0; i < _devicesInOrder.Count; i++)
        {
            var (device, label) = _devicesInOrder[i];
            writer.Align2();
            writer.DefineLabelHere(label);
            writer.WriteBytes(device.ToArray());
        }
    }

    private OTFontFile2.OffsetWriter.Label GetOrAddLabel(OTFontFile2.OffsetWriter writer, DeviceTableBuilder device)
    {
        if (_labelByDevice.TryGetValue(device, out var existing))
            return existing;

        var label = writer.CreateLabel();
        _labelByDevice.Add(device, label);
        _devicesInOrder.Add((device, label));
        return label;
    }
}

internal sealed class AnchorTablePool
{
    private readonly Dictionary<AnchorTableBuilder, OTFontFile2.OffsetWriter.Label> _labelByAnchor
        = new(ReferenceEqualityComparer.Instance);

    private readonly List<(AnchorTableBuilder anchor, OTFontFile2.OffsetWriter.Label label)> _anchorsInOrder = new();

    public void WriteOffset16(OTFontFile2.OffsetWriter writer, AnchorTableBuilder anchor, int baseOffset)
    {
        if (writer is null) throw new ArgumentNullException(nameof(writer));
        if (anchor is null) throw new ArgumentNullException(nameof(anchor));

        var label = GetOrAddLabel(writer, anchor);
        writer.WriteOffset16(label, baseOffset);
    }

    public void EmitAllAligned2(OTFontFile2.OffsetWriter writer, DeviceTablePool devices)
    {
        if (writer is null) throw new ArgumentNullException(nameof(writer));
        if (devices is null) throw new ArgumentNullException(nameof(devices));

        for (int i = 0; i < _anchorsInOrder.Count; i++)
        {
            var (anchor, label) = _anchorsInOrder[i];
            writer.Align2();
            writer.DefineLabelHere(label);
            int anchorStart = writer.Position;
            anchor.WriteTo(writer, anchorStartOffset: anchorStart, devices);
        }
    }

    private OTFontFile2.OffsetWriter.Label GetOrAddLabel(OTFontFile2.OffsetWriter writer, AnchorTableBuilder anchor)
    {
        if (_labelByAnchor.TryGetValue(anchor, out var existing))
            return existing;

        var label = writer.CreateLabel();
        _labelByAnchor.Add(anchor, label);
        _anchorsInOrder.Add((anchor, label));
        return label;
    }
}
