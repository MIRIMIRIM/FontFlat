using System.Buffers;

namespace OTFontFile;

public static class BufferPool
{
    private static readonly ArrayPool<byte> s_pool = ArrayPool<byte>.Create();

    public static MBOBuffer Rent(int size)
    {
        byte[] buffer = s_pool.Rent(size);
        return new MBOBuffer(buffer, size, true); // 标记为池化缓冲区
    }

    public static MBOBuffer Rent(int size, long filepos)
    {
        byte[] buffer = s_pool.Rent(size);
        return new MBOBuffer(buffer, size, filepos, true); // 标记为池化缓冲区
    }

    internal static void Return(byte[] buffer)
    {
        if (buffer != null && buffer.Length != 0)
        {
            s_pool.Return(buffer);
        }
    }
}
