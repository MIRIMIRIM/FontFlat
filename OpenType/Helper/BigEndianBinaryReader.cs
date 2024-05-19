﻿using System.Buffers;
using System.Buffers.Binary;
using FontFlat.OpenType.DataTypes;
using FontFlat.OpenType.FontTables;

namespace FontFlat.OpenType.Helper;

public class BigEndianBinaryReader(Stream input) : BinaryReader(input)
{
    private byte[] buffer = ArrayPool<byte>.Shared.Rent(8);

    private Span<byte> Read(int count)
    {
        var span = buffer.AsSpan(0, count);
        var readCount = BaseStream.Read(span);
        if (readCount != count) { throw new EndOfStreamException(); }
        return span;
    }

    protected override void Dispose(bool disposing)
    {
        ArrayPool<byte>.Shared.Return(buffer);
        base.Dispose(disposing);
    }

    // uint8
    // int8
    public override ushort ReadUInt16() => BinaryPrimitives.ReadUInt16BigEndian(Read(sizeof(ushort)));
    public override short ReadInt16() => BinaryPrimitives.ReadInt16BigEndian(Read(sizeof(short)));
    // uint24
    public override uint ReadUInt32() => BinaryPrimitives.ReadUInt32BigEndian(Read(sizeof(uint)));
    public override int ReadInt32() => BinaryPrimitives.ReadInt32BigEndian(Read(sizeof(int)));
    public Fixed ReadFixed() => new()
    {
        High = ReadUInt16(),
        Low = ReadUInt16(),
    };
    public LONGDATETIME ReadLongDateTime() => new LONGDATETIME(BinaryPrimitives.ReadInt64BigEndian(Read(sizeof(long))));
    public Offset16 ReadOffset16() => new Offset16(ReadUInt16());
    public Offset32 ReadOffset32() => new Offset32(ReadUInt32());
    public Offset32[] ReadOffset32Array(int count)
    {
        var arr = new Offset32[count];
        for (var i = 0; i < count; i++)
        {
            arr[i] = ReadOffset32();
        }
        return arr;
    }
    public NameRecord[] ReadNameRecordArray(int count)
    {
        var nrs = new NameRecord[count];
        for (var i = 0; i < count; i++)
        {
            nrs[i] = FontTables.Read.ReadNameRecord(this);
        }
        return nrs;
    }
    public LangTagRecord[] ReadLangTagRecordArray(int count)
    {
        var arr = new LangTagRecord[count];
        for (var i = 0; i < count; i++)
        {
            arr[i] = FontTables.Read.ReadLangTagRecord(this);
        }
        return arr;
    }
    public Tag ReadTag() => new Tag(ReadBytes(4));
}