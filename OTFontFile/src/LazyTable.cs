using System;
using System.Runtime.CompilerServices;

namespace OTFontFile
{
    /// <summary>
    /// 延迟加载表的基类。
    /// 只在需要时才加载表内容，减少内存占用。
    /// </summary>
    public abstract class LazyTable : OTTable, IDisposable
    {
        /// <summary>
        /// 是否已加载表数据
        /// </summary>
        protected bool _contentLoaded;

        /// <summary>
        /// 是否正在加载数据（防止并发）
        /// </summary>
        protected bool _isLoading;

        /// <summary>
        /// 目录条目，包含表的元信息
        /// </summary>
        protected readonly DirectoryEntry? _directoryEntry;

        /// <summary>
        /// 关联的 OTFile，用于延迟加载数据
        /// </summary>
        protected readonly OTFile? _file;

        /// <summary>
        /// 延迟加载构造函数（子类表需要调用）
        /// </summary>
        protected LazyTable(DirectoryEntry? de, OTFile? file)
            : base(de!.tag, new MBOBuffer())
        {
            _directoryEntry = de;
            _file = file;
            _contentLoaded = false;
            _isLoading = false;
        }

        /// <summary>
        /// 立即加载构造函数（子类表需要调用）
        /// </summary>
        protected LazyTable(OTTag tag, MBOBuffer buf, DirectoryEntry? de, OTFile? file)
            : base(tag, buf)
        {
            _directoryEntry = de;
            _file = file;
            _contentLoaded = true;  // 数据已经加载
            _isLoading = false;
        }

        /// <summary>
        /// 确保表数据已加载。如果未加载，则从文件读取。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected void EnsureContentLoaded()
        {
            if (_isLoading)
            {
                // 防止递归加载
                return;
            }

            if (!_contentLoaded)
            {
                _isLoading = true;
                try
                {
                    // 从文件读取表数据
                    var buf = _file!.ReadPaddedBuffer(_directoryEntry!.offset, _directoryEntry!.length);

                    if (buf != null)
                    {
                        // 更新缓冲区
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

        /// <summary>
        /// 确保表数据已加载（使用对象池）。
        /// 对于大表（如 glyf），应该使用此方法以减少 GC 压力。
        /// </summary>
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
                    // 从对象池读取表数据
                    pooledBuf = _file!.ReadPooledBuffer(_directoryEntry!.offset, _directoryEntry!.length);

                    if (pooledBuf != null)
                    {
                        // 更新缓冲区（注意：pooledBuf 使用完后需要 Dispose）
                        UpdateBuffer(pooledBuf);
                    }

                    _contentLoaded = true;
                }
                finally
                {
                    _isLoading = false;
                    // 注意：UpdateBuffer 会复制数据，所以 pooledBuf 可以被返回到池中
                    pooledBuf?.Dispose();
                }
            }
        }

        /// <summary>
        /// 更新缓冲区内容（由子类实现实际的数据更新逻辑）
        /// </summary>
        protected virtual void UpdateBuffer(MBOBuffer buf)
        {
            // 子类可以重写此方法以实现特定的缓冲区更新逻辑
            // 默认行为：替换缓冲区引用
            // 注意：如果 buf 是池化的，子类需要小心处理 Disposable
        }

        /// <summary>
        /// 检查表数据是否已加载（用于测试和调试）
        /// </summary>
        public bool IsContentLoaded => _contentLoaded;

        /// <summary>
        /// 获取目录条目信息
        /// </summary>
        public DirectoryEntry? DirectoryEntry => _directoryEntry;

        /// <summary>
        /// 释放资源
        /// </summary>
        public virtual void Dispose()
        {
            // 子类可以重写以释放特定资源
            GC.SuppressFinalize(this);
        }
    }
}
