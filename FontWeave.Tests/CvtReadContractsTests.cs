using FontWeave.Core;
using FontWeave.Tables;
using Xunit;

namespace FontWeave.Tests;

public sealed class CvtReadContractsTests
{
    private const uint CvtTag = 0x63767420;
    private static readonly DataLocation Source = new(17, 2, CvtTag, -1,
        DataField.None, OffsetBasis.Table, 12, 3) { TableFileOffset = 1000 };

    [Fact]
    public void DefaultViewCannotPublishAnEmptyModel()
    {
        CvtView view = default;

        Assert.Equal(ReadStatus.Uninitialized, view.TryRead(out var table, out var diagnostic));
        Assert.Null(table);
        Assert.False(view.IsEmpty);
        Assert.Equal(DiagnosticCode.UnboundView, diagnostic.Code);
        Assert.Equal(ReadStatus.Uninitialized, view.TryGetValue(int.MinValue, out var amount,
            out diagnostic));
        Assert.Equal((short)0, amount);
        Assert.Equal(DiagnosticCode.UnboundView, diagnostic.Code);
    }

    [Fact]
    public void BothOpenPathsBindTheLegalEmptyTable()
    {
        Assert.Equal(ReadStatus.Done, CvtView.TryOpen([], out var rawView, out _, Source));
        Assert.True(rawView.IsBound);
        Assert.Equal(0, rawView.RawBytes.Length);
        Assert.Equal(ReadStatus.Done, rawView.TryRead(out var rawTable, out _,
            new BuildLimits { MaxInputBytes = 0, MaxWorkUnits = 0, MaxTemporaryBytes = 0 }));
        Assert.NotNull(rawTable);
        Assert.Equal(0, rawTable.Count);
        Assert.Equal(Source, rawTable.Location);

        Assert.Equal(ReadStatus.Done, CvtView.TryOpenCvt([], out var strictView, out _, Source));
        Assert.True(strictView.IsBound);
        Assert.Equal(0, strictView.RawBytes.Length);
        Assert.Equal(ReadStatus.InvalidIndex, strictView.TryGetValue(0, out var amount,
            out var diagnostic));
        Assert.Equal((short)0, amount);
        Assert.Equal(DiagnosticCode.IndexOutOfRange, diagnostic.Code);
        Assert.Equal(Source.Offset, diagnostic.Location.Offset);
        Assert.Equal(Source.AbsoluteOffset, diagnostic.Location.AbsoluteOffset);
    }

    [Fact]
    public void RawOpenDoesNotLetSemanticReadTruncateAnOddPayload()
    {
        byte[] bytes = [0, 1, 0x7F];
        Assert.Equal(ReadStatus.Done, CvtView.TryOpen(bytes, out var view, out _, Source));

        Assert.Equal(ReadStatus.InvalidFormat, view.TryRead(out var table, out var diagnostic));
        Assert.Null(table);
        Assert.Equal(DiagnosticCode.InvalidValue, diagnostic.Code);
        Assert.Equal(DataField.Length, diagnostic.Location.Field);
        Assert.Equal(Source.Offset, diagnostic.Location.Offset);
        Assert.Equal(Source.AbsoluteOffset, diagnostic.Location.AbsoluteOffset);
        Assert.Equal(ReadStatus.InvalidFormat, CvtView.TryOpenCvt(bytes, out var rejected,
            out var openDiagnostic, Source));
        Assert.False(rejected.IsBound);
        Assert.Equal(diagnostic, openDiagnostic);
    }

    [Fact]
    public void RawOpenDoesNotBypassTheDeclaredValueLimit()
    {
        byte[] bytes = new byte[2 * 65537];
        Assert.Equal(ReadStatus.Done, CvtView.TryOpen(bytes, out var view, out _, Source));

        Assert.Equal(ReadStatus.ResourceLimit, view.TryRead(out var table, out var diagnostic));
        Assert.Null(table);
        Assert.Equal(DiagnosticCode.BudgetExceeded, diagnostic.Code);
        Assert.Equal(DataField.Count, diagnostic.Location.Field);
        Assert.Equal((ulong)65537, diagnostic.Detail);
        Assert.Equal((ulong)65536, diagnostic.Expected);
        Assert.Equal(Source.Offset, diagnostic.Location.Offset);
        Assert.Equal(ReadStatus.ResourceLimit, CvtView.TryOpenCvt(bytes, out var rejected,
            out var openDiagnostic, Source));
        Assert.False(rejected.IsBound);
        Assert.Equal(diagnostic, openDiagnostic);

        byte[] maximum = new byte[2 * 65536];
        Assert.Equal(ReadStatus.Done, CvtView.TryOpenCvt(maximum, out var maximumView,
            out _, Source));
        Assert.Equal(ReadStatus.Done, maximumView.TryRead(out var maximumTable, out _));
        Assert.NotNull(maximumTable);
        Assert.Equal(65536, maximumTable.Count);
    }

    [Fact]
    public void OwnedValuesAreAdmittedBeforePublishingAModel()
    {
        byte[] bytes = [0, 1, 0xFF, 0xFE, 0x7F, 0xFF, 0x80, 0];
        Assert.Equal(ReadStatus.Done, CvtView.TryOpen(bytes, out var view, out _, Source));

        Assert.Equal(ReadStatus.ResourceLimit, view.TryRead(out var refused, out var diagnostic,
            new BuildLimits { MaxTemporaryBytes = 7 }));
        Assert.Null(refused);
        Assert.Equal(DiagnosticCode.BudgetExceeded, diagnostic.Code);
        Assert.Equal(DataField.TemporaryBytes, diagnostic.Location.Field);
        Assert.Equal(Source.SourceId, diagnostic.Location.SourceId);
        Assert.Equal(Source.Offset, diagnostic.Location.Offset);
        Assert.Equal(Source.AbsoluteOffset, diagnostic.Location.AbsoluteOffset);
        Assert.Equal((ulong)8, diagnostic.Detail);
        Assert.Equal((ulong)7, diagnostic.Expected);

        Assert.Equal(ReadStatus.Done, view.TryRead(out var table, out _,
            new BuildLimits { MaxTemporaryBytes = 8 }));
        Assert.NotNull(table);
        Assert.Equal(new short[] { 1, -2, short.MaxValue, short.MinValue }, table.Values.ToArray());
        Assert.Equal(Source, table.Location);
        Assert.Equal(new byte[] { 0, 1, 0xFF, 0xFE, 0x7F, 0xFF, 0x80, 0 }, bytes);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    [InlineData(int.MaxValue)]
    public void InvalidIndicesProduceDiagnosticsWithoutCheckedArithmeticExceptions(int index)
    {
        Assert.Equal(ReadStatus.Done, CvtView.TryOpenCvt(new byte[] { 0, 1 }, out var view,
            out _, Source));

        Assert.Equal(ReadStatus.InvalidIndex, view.TryGetValue(index, out var amount,
            out var diagnostic));
        Assert.Equal((short)0, amount);
        Assert.Equal(DiagnosticCode.IndexOutOfRange, diagnostic.Code);
        Assert.Equal(index, diagnostic.Location.RecordOrdinal);
        Assert.Equal(unchecked((ulong)(long)index), diagnostic.Detail);
        Assert.Equal(ReadStatus.Done, view.TryRead(out var table, out _));
        Assert.NotNull(table);
        Assert.Equal(ReadStatus.InvalidIndex, table.TryGetValue(index, out amount, out diagnostic));
        Assert.Equal((short)0, amount);
        Assert.Equal(index, diagnostic.Location.RecordOrdinal);
    }
}
