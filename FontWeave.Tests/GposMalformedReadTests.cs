// Malformed-read faces for the shared coverage readers: a coverage offset that
// lands outside its readable region must answer the range diagnostic, never an
// exception out of the Try* contract (found by the in-process fuzzer over real
// table seeds).
using System.Buffers.Binary;
using FontWeave.Core;
using FontWeave.Tables;
using FontWeave.Tables.Layout;
using Xunit;

namespace FontWeave.Tests;

public sealed class GposMalformedReadTests
{
    // header(10) | empty script list | empty feature list | lookup list(4) |
    // lookup header(8) | one subtable(10).
    private static byte[] Gpos(ushort lookupType, ReadOnlySpan<byte> subtable)
    {
        var bytes = new byte[26 + subtable.Length];
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(0), 0x00010000);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(4), 10);   // scriptList
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(6), 12);   // featureList
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(8), 14);   // lookupList
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(10), 0);   // no scripts
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(12), 0);   // no features
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(14), 1);   // one lookup
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(16), 4);   // at lookup+4
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(18), lookupType);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(20), 0);   // flags
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(22), 1);   // one subtable
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(24), 8);   // at lookup+8
        subtable.CopyTo(bytes.AsSpan(26));
        return bytes;
    }

    // PairPos format 1 whose coverage offset names a position beyond the table:
    // the shared coverage reader must refuse the range instead of slicing past
    // its view.
    [Fact]
    public void PairPosCoverageBeyondTheTableRefuses()
    {
        byte[] gpos = Gpos(2, [0x00, 0x01, 0xFF, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00]);
        Assert.Equal(ReadStatus.Done, LayoutListsView.TryOpen(gpos, out var view, out _));

        var status = view.TryReadTable(8, LayoutLookupKind.Gpos, null,
            out var model, out var diagnostic);

        Assert.Equal(ReadStatus.InvalidOffset, status);
        Assert.Equal(DiagnosticCode.RangeOutsideSource, diagnostic.Code);
        Assert.Null(model);
    }

    // The same shape through CursivePos: its shared coverage reader carries the
    // identical slice.
    [Fact]
    public void CursiveCoverageBeyondTheTableRefuses()
    {
        byte[] gpos = Gpos(3, [0x00, 0x01, 0xFF, 0x00, 0x00, 0x00]);
        Assert.Equal(ReadStatus.Done, LayoutListsView.TryOpen(gpos, out var view, out _));

        var status = view.TryReadTable(8, LayoutLookupKind.Gpos, null,
            out var model, out var diagnostic);

        Assert.Equal(ReadStatus.InvalidOffset, status);
        Assert.Equal(DiagnosticCode.RangeOutsideSource, diagnostic.Code);
        Assert.Null(model);
    }
}
