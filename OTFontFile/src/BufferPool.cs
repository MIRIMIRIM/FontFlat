using System.Buffers;

namespace OTFontFile;

public static class BufferPool
{
    private static readonly ArrayPool<byte> s_pool = ArrayPool<byte>.Create();

    public static MBOBuffer Rent(int size)
    {
        int pad = MBOBuffer.CalcPadBytes(size, 4);
        byte[] buffer = s_pool.Rent(size + pad);
        return new MBOBuffer(buffer, size, true); // 标记为池化缓冲区
    }

    public static MBOBuffer Rent(int size, long filepos)
    {
        int pad = MBOBuffer.CalcPadBytes(size, 4);
        byte[] buffer = s_pool.Rent(size + pad);
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
