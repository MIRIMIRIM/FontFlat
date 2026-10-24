// CFF2 reader robustness faces: the FontDICT walk must answer a missing required
// key with a diagnostic (not an index past the end of the entry list), and the
// payload materialization must charge the budget before it copies, so a refusing
// request never allocates the table first.
using FontWeave.Core;
using FontWeave.Tables;
using Xunit;

namespace FontWeave.Tests;

public sealed class Cff2ReadRobustnessTests
{
    // A minimal CFF2 whose FDArray holds one zero-length FontDICT item: the entry
    // list parses empty, so the required 0x12 pair is absent.
    private static readonly byte[] EmptyFontDict = Convert.FromHexString(
        "02000500059911A00C24000000000000000101010100000001010101");

    // The same table with the required `0 0 Private` pair: the legal empty form.
    private static readonly byte[] ExplicitEmptyPrivate = Convert.FromHexString(
        "02000500059911A00C240000000000000001010101000000010101048B8B12");

    [Fact]
    public void EmptyFontDictAnswersADiagnosticInsteadOfThrowing()
    {
        Assert.Equal(ReadStatus.Done, Cff2View.TryOpen(EmptyFontDict, out var view,
            out _, default));
        var status = view.ValidateTable(1, null, out var diagnostic);
        Assert.Equal(ReadStatus.InvalidFormat, status);
        Assert.Equal(DiagnosticCode.InvalidValue, diagnostic.Code);
        // The missing key is the FontDICT's required PrivateDICTOffset operator.
        Assert.Equal(0x12UL, diagnostic.Detail);
    }

    [Fact]
    public void ExplicitEmptyPrivateStaysLegal()
    {
        Assert.Equal(ReadStatus.Done, Cff2View.TryOpen(ExplicitEmptyPrivate, out var view,
            out _, default));
        Assert.Equal(ReadStatus.Done, view.ValidateTable(1, null, out _));
    }

    // The 60,000-byte charstring from the review probe: with no temporary budget the
    // read must refuse before copying the payload, so the observed allocation stays
    // far below the table size.
    [Fact]
    public void TemporaryBudgetRefusesBeforeThePayloadCopy()
    {
        const int charStringLength = 60000;
        const int charStringsAt = 22;
        int fontDictsAt = charStringsAt + 9 + charStringLength;
        var source = new byte[fontDictsAt + 10];
        // Header: major 2, minor 0, headerSize 5, TopDICT length 13; the TopDICT's
        // CharStrings (op 17) and FDArray (op 12 36) offsets are int32 operands.
        new byte[] { 2, 0, 5, 0, 13, 29 }.CopyTo(source, 0);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(source.AsSpan(6),
            charStringsAt);
        source[10] = 17;
        source[11] = 29;
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(source.AsSpan(12),
            fontDictsAt);
        source[16] = 12;
        source[17] = 36;
        // One 60,000-byte charstring: a two-byte offset array around one item.
        new byte[] { 0, 0, 0, 1, 2, 0, 1 }.CopyTo(source, charStringsAt);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16BigEndian(
            source.AsSpan(charStringsAt + 7), charStringLength + 1);
        for (int i = 0; i < charStringLength; i += 3)
        {
            source[charStringsAt + 9 + i] = 139;
            source[charStringsAt + 10 + i] = 139;
            source[charStringsAt + 11 + i] = i == 0 ? (byte)21 : (byte)5;
        }
        // One FontDICT holding the legal `0 0 Private` pair.
        new byte[] { 0, 0, 0, 1, 1, 1, 4, 139, 139, 18 }.CopyTo(source, fontDictsAt);

        Assert.Equal(ReadStatus.Done, Cff2View.TryOpen(source, out var view, out _,
            default));
        Assert.Equal(ReadStatus.Done, view.ValidateTable(1, null, out _));

        long before = GC.GetAllocatedBytesForCurrentThread();
        var status = view.TryReadTable(1, null, out var model, out var diagnostic,
            new BuildLimits { MaxTemporaryBytes = 0 });
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(ReadStatus.ResourceLimit, status);
        Assert.Equal(DiagnosticCode.BudgetExceeded, diagnostic.Code);
        Assert.Null(model);
        // The refusal is an admission gate: the 60 KB payload must not have been
        // copied before the budget answered.
        Assert.True(allocated < charStringLength / 4,
            $"the refused read still allocated {allocated} bytes");
    }
}
