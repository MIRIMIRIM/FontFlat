using System;
using System.Runtime.CompilerServices;

namespace OTFontFile;

public abstract class LazyTable : OTTable, IDisposable
{
    protected bool _contentLoaded;
    protected bool _isLoading;
    protected readonly DirectoryEntry? _directoryEntry;
    protected readonly OTFile? _file;

    protected LazyTable(DirectoryEntry? de, OTFile? file)
        : base(de!.tag, new MBOBuffer())
    {
        _directoryEntry = de;
        _file = file;
        _contentLoaded = false;
        _isLoading = false;
    }

    protected LazyTable(OTTag tag, MBOBuffer buf, DirectoryEntry? de, OTFile? file)
        : base(tag, buf)
    {
        _directoryEntry = de;
        _file = file;
        _contentLoaded = true;
        _isLoading = false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected void EnsureContentLoaded()
    {
        if (_isLoading)
        {
            return;
        }

        if (!_contentLoaded)
        {
            _isLoading = true;
            try
            {
                var buf = _file!.ReadPaddedBuffer(_directoryEntry!.offset, _directoryEntry!.length);

                if (buf != null)
                {
                    UpdateBuffer(buf);
                }

                _contentLoaded = true;
            }
            finally
            {
                _isLoading = false;
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected void EnsureContentLoadedPooled()
    {
        if (_isLoading)
        {
            // 防止递归加载
            return;
        }

        if (!_contentLoaded)
        {
            _isLoading = true;
            MBOBuffer? pooledBuf = null;
            try
            {
                pooledBuf = _file!.ReadPooledBuffer(_directoryEntry!.offset, _directoryEntry!.length);

                if (pooledBuf != null)
                {
                    UpdateBuffer(pooledBuf);
                }

                _contentLoaded = true;
            }
            finally
            {
                _isLoading = false;
                pooledBuf?.Dispose();
            }
        }
    }

    protected virtual void UpdateBuffer(MBOBuffer buf)
    {
    }

    public bool IsContentLoaded => _contentLoaded;

    public DirectoryEntry? DirectoryEntry => _directoryEntry;

    public virtual void Dispose()
    {
        GC.SuppressFinalize(this);
    }
}
