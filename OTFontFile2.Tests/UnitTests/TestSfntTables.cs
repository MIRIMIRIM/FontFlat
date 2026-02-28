using System.Buffers.Binary;

namespace OTFontFile2.Tests.UnitTests;

internal static class TestSfntTables
{
    public static byte[] BuildValidHeadTable(ushort unitsPerEm)
    {
        // head table is 54 bytes.
        byte[] head = new byte[54];
        var span = head.AsSpan();

        BinaryPrimitives.WriteUInt32BigEndian(span.Slice(0, 4), 0x00010000u); // tableVersion
        BinaryPrimitives.WriteUInt32BigEndian(span.Slice(4, 4), 0x00010000u); // fontRevision
        // checkSumAdjustment left as 0; SfntWriter will patch it.
        BinaryPrimitives.WriteUInt32BigEndian(span.Slice(12, 4), 0x5F0F3CF5u); // magicNumber
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(16, 2), 0); // flags
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(18, 2), unitsPerEm);
        // created/modified left as 0
        // bbox left as 0
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(44, 2), 0); // macStyle
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(46, 2), 0); // lowestRecPPEM
        BinaryPrimitives.WriteInt16BigEndian(span.Slice(48, 2), 0); // fontDirectionHint
        BinaryPrimitives.WriteInt16BigEndian(span.Slice(50, 2), 0); // indexToLocFormat
        BinaryPrimitives.WriteInt16BigEndian(span.Slice(52, 2), 0); // glyphDataFormat

        return head;
    }
}

