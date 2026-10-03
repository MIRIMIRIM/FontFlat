using System.Buffers.Binary;
using FontWeave.Core;
using FontWeave.Tables;
using Xunit;

namespace FontWeave.Tests;

public sealed class NestedLocationTests
{
    private const uint CmapTag = 0x636D6170;
    private static readonly DataLocation SourceLocation = new(17, 3, CmapTag, -1,
        DataField.None, OffsetBasis.Table, 0, 7) { TableFileOffset = 1000 };

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NestedSemanticFieldRetainsTableCoordinatesAndSource(bool anonymous)
    {
        var bytes = Format12Cmap(16);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(14), 1);
        var location = anonymous ? DataLocation.Anonymous(CmapTag) : SourceLocation;
        Assert.Equal(ReadStatus.Done, CmapView.TryOpen(bytes, out var cmap, out _, location));
        Assert.Equal(ReadStatus.Done, cmap.TryOpenFormat12(0, out var view, out _));

        Assert.Equal(ReadStatus.InvalidFormat, view.Validate(4, out var diagnostic));

        Assert.Equal(DiagnosticCode.InvalidValue, diagnostic.Code);
        AssertLocation(location, diagnostic.Location, DataField.Payload, 14,
            anonymous ? null : 1014UL);
    }

    [Fact]
    public void NestedRawRecordFailureRetainsWindowOrigin()
    {
        var bytes = Format12Cmap(16);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(24), 1);
        Assert.Equal(ReadStatus.Done, CmapView.TryOpen(bytes, out var cmap, out _, SourceLocation));
        Assert.Equal(ReadStatus.Done, cmap.TryOpenFormat12(0, out var view, out _));

        Assert.Equal(ReadStatus.Truncated, view.TryGetGroup(0, out _, out var diagnostic));

        Assert.Equal(DiagnosticCode.TruncatedData, diagnostic.Code);
        Assert.Equal(0, diagnostic.Location.RecordOrdinal);
        AssertLocation(SourceLocation, diagnostic.Location, DataField.CharCode, 28, 1028);
    }

    [Fact]
    public void NestedIncompleteScalarFailureRetainsWindowOrigin()
    {
        var bytes = Format12Cmap(12);
        Assert.Equal(ReadStatus.Done, CmapView.TryOpen(bytes, out var cmap, out _, SourceLocation));

        Assert.Equal(ReadStatus.Truncated, cmap.TryOpenFormat12(0, out _, out var diagnostic));

        Assert.Equal(DiagnosticCode.TruncatedData, diagnostic.Code);
        AssertLocation(SourceLocation, diagnostic.Location, DataField.Count, 24, 1024);
    }

    [Theory]
    [InlineData(8UL)]
    [InlineData(13UL)]
    public void UnrepresentableNestedCoordinateHasNoInventedAbsoluteOffset(ulong remaining)
    {
        var bytes = Format12Cmap(16);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(14), 1);
        var location = SourceLocation with { Offset = ulong.MaxValue - remaining };
        Assert.Equal(ReadStatus.Done, CmapView.TryOpen(bytes, out var cmap, out _, location));
        Assert.Equal(ReadStatus.Done, cmap.TryOpenFormat12(0, out var view, out _));

        Assert.Equal(ReadStatus.InvalidFormat, view.Validate(4, out var diagnostic));

        Assert.Equal(OffsetBasis.None, diagnostic.Location.Basis);
        Assert.Equal(2UL, diagnostic.Location.Offset);
        Assert.Null(diagnostic.Location.AbsoluteOffset);
        AssertIdentity(location, diagnostic.Location);
    }

    [Fact]
    public void NearMaxRawReadReportsTruncationWithoutOverflowingItsDiagnostic()
    {
        byte[] bytes = [0, 2, 0, 6, 0, 0];
        var location = SourceLocation with { Offset = 12 };
        Assert.Equal(ReadStatus.Done, CmapFormat2View.TryOpen(bytes, out var view, out _, location));

        Assert.Equal(ReadStatus.Truncated,
            view.TryReadGlyphAt(ulong.MaxValue, out _, out var diagnostic));

        Assert.Equal(DiagnosticCode.TruncatedData, diagnostic.Code);
        Assert.Equal(OffsetBasis.None, diagnostic.Location.Basis);
        Assert.Equal(ulong.MaxValue, diagnostic.Location.Offset);
        Assert.Null(diagnostic.Location.AbsoluteOffset);
        AssertIdentity(location, diagnostic.Location);
    }

    [Fact]
    public void ExistingAtStillSetsTheCoordinateRatherThanAddingTheOrigin()
    {
        var location = SourceLocation with { Offset = 12 };

        var field = location.At(DataField.Length, 2, 5);

        Assert.Equal(2UL, field.Offset);
        Assert.Equal(1002UL, field.AbsoluteOffset);
        Assert.Equal(5, field.RecordOrdinal);
        AssertIdentity(location, field);
    }

    [Fact]
    public void NestedWindowDoesNotRelaxTopTableSourceIdentity()
    {
        var bytes = Format12Cmap(16);
        Assert.Equal(ReadStatus.Done, CmapView.TryOpen(bytes, out var view, out _, SourceLocation));
        byte[] maxpBytes = [0, 0, 0x50, 0, 0, 4];
        var maxpLocation = SourceLocation with { TableTag = 0x6D617870, TableOrdinal = 2 };
        Assert.Equal(ReadStatus.Done, MaxpView.TryOpen(maxpBytes, out var maxpView, out _, maxpLocation));
        Assert.Equal(ReadStatus.Done, maxpView.TryRead(out var maxp, out _));
        Assert.Equal(ReadStatus.Done, CmapContext.TryCreate(maxp!,
            SourceLocation with { TableOrdinal = 8 }, out var wrongContext, out _));

        Assert.Equal(ReadStatus.InvalidSource, view.Validate(wrongContext!, out var diagnostic));

        Assert.Equal(DiagnosticCode.WrongSourceFace, diagnostic.Code);
        Assert.Equal(SourceLocation, diagnostic.Location);
    }

    [Fact]
    public void MaterializedSubtableReportsItsSourceOffsetOnlyOnce()
    {
        var bytes = new byte[22];
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(2), 1);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(4), 0);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(6), 5);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(8), 12);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(12), 14);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(14), 10);
        Assert.Equal(ReadStatus.Done, CmapView.TryOpen(bytes, out var view, out _));
        Assert.Equal(ReadStatus.Done, CmapContext.TryCreate(4, out var context, out _));
        Assert.Equal(ReadStatus.Done, view.TryRead(context!, out var model, out _));
        Assert.Equal(ReadStatus.Done, model!.TryGetSubtable(0, out var subtable, out _));

        Assert.Equal(ReadStatus.UnsupportedFormat,
            subtable!.TryMapCharacter(0x41, out _, out _, out var diagnostic));

        Assert.Equal(12U, subtable.SourceOffset);
        Assert.Equal(12UL, subtable.Location.Offset);
        AssertLocation(DataLocation.Anonymous(CmapTag), diagnostic.Location,
            DataField.Version, 12, null);
    }

    private static byte[] Format12Cmap(int subtableLength)
    {
        var bytes = new byte[12 + subtableLength];
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(2), 1);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(4), 3);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(6), 10);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(8), 12);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(12), 12);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(16), (uint)subtableLength);
        return bytes;
    }

    private static void AssertLocation(DataLocation source, DataLocation actual,
        DataField field, ulong offset, ulong? absolute)
    {
        Assert.Equal(OffsetBasis.Table, actual.Basis);
        Assert.Equal(field, actual.Field);
        Assert.Equal(offset, actual.Offset);
        Assert.Equal(absolute, actual.AbsoluteOffset);
        AssertIdentity(source, actual);
    }

    private static void AssertIdentity(DataLocation source, DataLocation actual)
    {
        Assert.Equal(source.SourceId, actual.SourceId);
        Assert.Equal(source.FaceIndex, actual.FaceIndex);
        Assert.Equal(source.TableTag, actual.TableTag);
        Assert.Equal(source.TableOrdinal, actual.TableOrdinal);
        Assert.Equal(source.TableFileOffset, actual.TableFileOffset);
    }
}
