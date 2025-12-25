using System.Buffers;

namespace OTFontFile
{
    public static class BufferPool
    {
        /// <summary>
        /// 默认的 ArrayPool 实例
        /// </summary>
        private static readonly ArrayPool<byte> s_pool = ArrayPool<byte>.Create();

        /// <summary>
        /// 从池中租用指定大小的缓冲区
        /// </summary>
        /// <param name="size">需要的大小</param>
        /// <returns>池化的 MBOBuffer</returns>
        public static MBOBuffer Rent(int size)
        {
            byte[] buffer = s_pool.Rent(size);
            return new MBOBuffer(buffer, size, true); // 标记为池化缓冲区
        }

        /// <summary>
        /// 从池中租用指定大小的缓冲区（带文件位置）
        /// </summary>
        /// <param name="size">需要的大小</param>
        /// <param name="filepos">文件位置</param>
        /// <returns>池化的 MBOBuffer</returns>
        public static MBOBuffer Rent(int size, long filepos)
        {
            byte[] buffer = s_pool.Rent(size);
            return new MBOBuffer(buffer, size, filepos, true); // 标记为池化缓冲区
        }

        /// <summary>
        /// 返回缓冲区到池中
        /// </summary>
        /// <param name="buffer">要返回的缓冲区</param>
        internal static void Return(byte[] buffer)
        {
            if (buffer != null && buffer.Length != 0)
            {
                s_pool.Return(buffer);
            }
        }
    }
}
