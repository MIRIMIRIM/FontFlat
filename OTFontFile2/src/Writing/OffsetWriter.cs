namespace OTFontFile2;

/// <summary>
/// Simple byte writer with label-based offset patching (Offset16/Offset32).
/// Intended for building OpenType subtables where offsets are relative to a base.
/// </summary>
internal sealed class OffsetWriter
{
    public readonly struct Label : IEquatable<Label>
    {
        internal Label(int id) => Id = id;
        internal int Id { get; }
        public bool Equals(Label other) => Id == other.Id;
        public override bool Equals(object? obj) => obj is Label other && Equals(other);
        public override int GetHashCode() => Id;
        public static bool operator ==(Label left, Label right) => left.Equals(right);
        public static bool operator !=(Label left, Label right) => !left.Equals(right);
    }

    private byte[] _buffer;
    private int _length;

    private int[] _labelOffsets;
    private int _labelCount;

    private Patch16[] _patch16;
    private int _patch16Count;

    private Patch32[] _patch32;
    private int _patch32Count;

    public OffsetWriter(int initialCapacity = 256)
    {
        if (initialCapacity < 0) throw new ArgumentOutOfRangeException(nameof(initialCapacity));
        _buffer = initialCapacity == 0 ? Array.Empty<byte>() : new byte[initialCapacity];
        _labelOffsets = Array.Empty<int>();
        _patch16 = Array.Empty<Patch16>();
        _patch32 = Array.Empty<Patch32>();
    }

    public int Position => _length;

    public Label CreateLabel()
    {
        int id = _labelCount;
        _labelCount++;

        if (_labelOffsets.Length < _labelCount)
        {
            int newSize = _labelOffsets.Length == 0 ? 8 : _labelOffsets.Length * 2;
            if (newSize < _labelCount) newSize = _labelCount;
            Array.Resize(ref _labelOffsets, newSize);
        }

        _labelOffsets[id] = -1;
        return new Label(id);
    }

    public void DefineLabelHere(Label label)
        => DefineLabel(label, Position);

    public void DefineLabel(Label label, int offset)
    {
        if ((uint)label.Id >= (uint)_labelCount)
            throw new ArgumentOutOfRangeException(nameof(label));
        if (offset < 0)
            throw new ArgumentOutOfRangeException(nameof(offset));

        int existing = _labelOffsets[label.Id];
        if (existing != -1 && existing != offset)
            throw new InvalidOperationException("Label already defined.");

        _labelOffsets[label.Id] = offset;
    }

    public void Align2()
    {
        if ((_length & 1) == 0)
            return;

        WriteUInt8(0);
    }

    public void Align4()
    {
        int mod = _length & 3;
        if (mod == 0)
            return;

        int pad = 4 - mod;
        ReserveZeros(pad);
    }

    public void WriteUInt8(byte value)
    {
        int o = Reserve(1);
        _buffer[o] = value;
    }

    public void WriteUInt16(ushort value)
    {
        int o = Reserve(2);
        BigEndian.WriteUInt16(_buffer, o, value);
    }

    public void WriteInt16(short value)
    {
        int o = Reserve(2);
        BigEndian.WriteInt16(_buffer, o, value);
    }

    public void WriteUInt32(uint value)
    {
        int o = Reserve(4);
        BigEndian.WriteUInt32(_buffer, o, value);
    }

    public void WriteBytes(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length == 0)
            return;

        int o = Reserve(bytes.Length);
        bytes.CopyTo(_buffer.AsSpan(o, bytes.Length));
    }

    public void WriteBytes(ReadOnlyMemory<byte> bytes) => WriteBytes(bytes.Span);

    public void WriteOffset16(Label target, int baseOffset)
    {
        int patchOffset = Reserve(2);
        BigEndian.WriteUInt16(_buffer, patchOffset, 0);
        AddPatch16(patchOffset, target, baseOffset);
    }

    public void WriteOffset32(Label target, int baseOffset)
    {
        int patchOffset = Reserve(4);
        BigEndian.WriteUInt32(_buffer, patchOffset, 0);
        AddPatch32(patchOffset, target, baseOffset);
    }

    public byte[] ToArray()
    {
        ApplyPatches();
        return _buffer.AsSpan(0, _length).ToArray();
    }

    private int Reserve(int count)
    {
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
        int newLen = checked(_length + count);
        EnsureCapacity(newLen);
        int o = _length;
        _length = newLen;
        return o;
    }

    private void ReserveZeros(int count)
    {
        if (count == 0)
            return;

        int o = Reserve(count);
        _buffer.AsSpan(o, count).Clear();
    }

    private void EnsureCapacity(int required)
    {
        if (_buffer.Length >= required)
            return;

        int newSize = _buffer.Length == 0 ? 256 : _buffer.Length * 2;
        if (newSize < required) newSize = required;
        Array.Resize(ref _buffer, newSize);
    }

    private void AddPatch16(int patchOffset, Label target, int baseOffset)
    {
        if ((uint)target.Id >= (uint)_labelCount)
            throw new ArgumentOutOfRangeException(nameof(target));
        if (baseOffset < 0)
            throw new ArgumentOutOfRangeException(nameof(baseOffset));

        int idx = _patch16Count;
        _patch16Count++;
        if (_patch16.Length < _patch16Count)
        {
            int newSize = _patch16.Length == 0 ? 8 : _patch16.Length * 2;
            if (newSize < _patch16Count) newSize = _patch16Count;
            Array.Resize(ref _patch16, newSize);
        }

        _patch16[idx] = new Patch16(patchOffset, target.Id, baseOffset);
    }

    private void AddPatch32(int patchOffset, Label target, int baseOffset)
    {
        if ((uint)target.Id >= (uint)_labelCount)
            throw new ArgumentOutOfRangeException(nameof(target));
        if (baseOffset < 0)
            throw new ArgumentOutOfRangeException(nameof(baseOffset));

        int idx = _patch32Count;
        _patch32Count++;
        if (_patch32.Length < _patch32Count)
        {
            int newSize = _patch32.Length == 0 ? 8 : _patch32.Length * 2;
            if (newSize < _patch32Count) newSize = _patch32Count;
            Array.Resize(ref _patch32, newSize);
        }

        _patch32[idx] = new Patch32(patchOffset, target.Id, baseOffset);
    }

    private void ApplyPatches()
    {
        for (int i = 0; i < _labelCount; i++)
        {
            if (_labelOffsets[i] == -1)
                throw new InvalidOperationException("Unresolved label.");
        }

        for (int i = 0; i < _patch16Count; i++)
        {
            var p = _patch16[i];
            int target = _labelOffsets[p.TargetLabelId];
            int rel = target - p.BaseOffset;
            if ((uint)rel > ushort.MaxValue)
                throw new InvalidOperationException("Offset16 overflow.");

            BigEndian.WriteUInt16(_buffer, p.PatchOffset, checked((ushort)rel));
        }

        for (int i = 0; i < _patch32Count; i++)
        {
            var p = _patch32[i];
            int target = _labelOffsets[p.TargetLabelId];
            long rel = (long)target - p.BaseOffset;
            if (rel < 0 || rel > uint.MaxValue)
                throw new InvalidOperationException("Offset32 overflow.");

            BigEndian.WriteUInt32(_buffer, p.PatchOffset, checked((uint)rel));
        }
    }

    private readonly struct Patch16
    {
        public int PatchOffset { get; }
        public int TargetLabelId { get; }
        public int BaseOffset { get; }

        public Patch16(int patchOffset, int targetLabelId, int baseOffset)
        {
            PatchOffset = patchOffset;
            TargetLabelId = targetLabelId;
            BaseOffset = baseOffset;
        }
    }

    private readonly struct Patch32
    {
        public int PatchOffset { get; }
        public int TargetLabelId { get; }
        public int BaseOffset { get; }

        public Patch32(int patchOffset, int targetLabelId, int baseOffset)
        {
            PatchOffset = patchOffset;
            TargetLabelId = targetLabelId;
            BaseOffset = baseOffset;
        }
    }
}
