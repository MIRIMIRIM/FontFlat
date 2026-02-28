using System.IO.MemoryMappedFiles;

namespace OTFontFile2;

public sealed class FontBuffer : IDisposable
{
    private readonly ReadOnlyMemory<byte> _memory;
    private readonly MemoryMappedFile? _mmf;
    private readonly MemoryMappedViewAccessor? _viewAccessor;
    private readonly int _length;
    private unsafe byte* _pointer;
    private bool _disposed;

    private FontBuffer(ReadOnlyMemory<byte> memory)
    {
        _memory = memory;
        _length = memory.Length;
    }

    private unsafe FontBuffer(MemoryMappedFile mmf, MemoryMappedViewAccessor viewAccessor, byte* pointer, int length)
    {
        _mmf = mmf;
        _viewAccessor = viewAccessor;
        _pointer = pointer;
        _length = length;
    }

    public int Length => _length;

    public ReadOnlySpan<byte> Span
    {
        get
        {
#if DEBUG
            ThrowIfDisposed();
#endif
            if (_viewAccessor is null)
                return _memory.Span;

            unsafe
            {
                return new ReadOnlySpan<byte>(_pointer, _length);
            }
        }
    }

    public ReadOnlySpan<byte> Slice(int offset, int length)
    {
        if (!TrySlice(offset, length, out var slice))
            throw new ArgumentOutOfRangeException(nameof(offset), "Slice out of bounds.");

        return slice;
    }

    public bool TrySlice(int offset, int length, out ReadOnlySpan<byte> slice)
    {
        ThrowIfDisposed();

        slice = default;

        // Note: int bounds, because Span length is int.
        if ((uint)offset > (uint)_length)
            return false;
        if ((uint)length > (uint)(_length - offset))
            return false;

        slice = Span.Slice(offset, length);
        return true;
    }

    public static FontBuffer FromMemory(ReadOnlyMemory<byte> memory)
        => new(memory);

    public static FontBuffer MapReadOnlyFile(string path)
    {
        var fileInfo = new FileInfo(path);
        if (fileInfo.Length > int.MaxValue)
            throw new NotSupportedException("Fonts larger than 2GB are not supported by span-based APIs.");

        int length = checked((int)fileInfo.Length);
        var mmf = MemoryMappedFile.CreateFromFile(path, FileMode.Open, mapName: null, capacity: 0, access: MemoryMappedFileAccess.Read);
        var view = mmf.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);

        unsafe
        {
            byte* pointer = null;
            view.SafeMemoryMappedViewHandle.AcquirePointer(ref pointer);
            pointer += view.PointerOffset;
            return new FontBuffer(mmf, view, pointer, length);
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        if (_viewAccessor is not null)
        {
            unsafe
            {
                _pointer = null;
            }

            _viewAccessor.SafeMemoryMappedViewHandle.ReleasePointer();
            _viewAccessor.Dispose();
            _mmf!.Dispose();
        }

        _disposed = true;
        GC.SuppressFinalize(this);
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(FontBuffer));
    }
}
