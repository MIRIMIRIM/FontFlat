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

    public override uint CalcChecksum()
    {
        if (!_contentLoaded)
        {
            EnsureContentLoadedPooled();
        }
        return base.CalcChecksum();
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
            bool success = false;
            try
            {
                pooledBuf = _file!.ReadPooledBuffer(_directoryEntry!.offset, _directoryEntry!.length);

                if (pooledBuf != null)
                {
                    UpdateBuffer(pooledBuf);
                    success = true;
                }

                _contentLoaded = true;
            }
            finally
            {
                _isLoading = false;
                // Only dispose if we failed to assign/keep the buffer
                if (!success)
                {
                    pooledBuf?.Dispose(); 
                }
            }
        }
    }

    protected virtual void UpdateBuffer(MBOBuffer buf)
    {
        m_bufTable = buf;
    }

    public bool IsContentLoaded => _contentLoaded;

    public DirectoryEntry? DirectoryEntry => _directoryEntry;

    public virtual void Dispose()
    {
        // Dispose the buffer if it exists (crucial for pooled buffers)
        m_bufTable?.Dispose();
        GC.SuppressFinalize(this);
    }
}
