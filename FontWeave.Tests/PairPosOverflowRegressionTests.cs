using System.Buffers.Binary;
using FontWeave.Core;
using FontWeave.Tables.Layout;
using Xunit;

namespace FontWeave.Tests;

public sealed class PairPosOverflowRegressionTests
{
    [Fact]
    public void Format2OffsetFallbackKeepsInlineMatrixAndDeviceReferences()
    {
        const ushort class1Count = 160, class2Count = 100;
        const int glyphCount = 500;
        const int matrixEnd = 16 + class1Count * class2Count * 4;
        var location = DataLocation.Anonymous(0x47504F53);
        byte[] variationWire = [0, 215, 3, 232, 128, 0];
        byte[] staticWire = [0, 9, 0, 12, 0, 2, 208, 71];
        Assert.Equal(ReadStatus.Done, StaticDeviceView.TryOpen(variationWire,
            out var variationView, out var diagnostic, location));
        Assert.Equal(ReadStatus.Done, variationView.TryRead(out var variationDevice,
            out diagnostic));
        Assert.Equal(ReadStatus.Done, StaticDeviceTableCompiler.TryCreate(9, 12, 2,
            [-3, 0, 4, 7], out var staticDevice, out diagnostic, location: location));

        var values1 = new StaticValueRecord[class1Count * class2Count];
        var values2 = new StaticValueRecord[values1.Length];
        for (int i = 0; i < values1.Length; i++)
        {
            StaticDeviceTable? device = (i % 3) switch
            {
                0 => variationDevice,
                1 => staticDevice,
                _ => null
            };
            values1[i] = new(XAdvance: (short)(i % 257 - 128), XAdvanceDevice: device);
        }
        ClassDefRange[] classes = [new(0, glyphCount - 1, 1)];
        Assert.Equal(ReadStatus.Done, PairPosTableCompiler.TryCreate([1], classes,
            classes, class1Count, class2Count, values1, values2, glyphCount, 0x0044, 0,
            out var pair, out diagnostic, location: location));
        Assert.Equal(ReadStatus.Done, PairPosTableCompiler.TryCompile(pair!,
            out var compiled, out diagnostic));
        Assert.Equal(DiagnosticCode.None, diagnostic.Code);
        byte[] bytes = Assert.IsType<byte[]>(compiled);

        Assert.True(bytes.Length > ushort.MaxValue);
        Assert.Equal(2, BinaryPrimitives.ReadUInt16BigEndian(bytes));
        Assert.Equal(0x0044, BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(4)));
        Assert.Equal(0, BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(6)));
        Assert.Equal(class1Count, BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(12)));
        Assert.Equal(class2Count, BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(14)));
        foreach (int field in new[] { 2, 8, 10 })
        {
            int target = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(field));
            Assert.InRange(target, matrixEnd, ushort.MaxValue);
        }
        for (int i = 0; i < values1.Length; i++)
        {
            int cellAt = 16 + i * 4;
            Assert.Equal(values1[i].XAdvance,
                BinaryPrimitives.ReadInt16BigEndian(bytes.AsSpan(cellAt)));
            int deviceAt = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(cellAt + 2));
            byte[]? expectedDevice = (i % 3) switch
            {
                0 => variationWire,
                1 => staticWire,
                _ => null
            };
            if (expectedDevice is null)
            {
                Assert.Equal(0, deviceAt);
            }
            else
            {
                Assert.InRange(deviceAt, matrixEnd, ushort.MaxValue);
                Assert.True(expectedDevice.AsSpan().SequenceEqual(
                    bytes.AsSpan(deviceAt, expectedDevice.Length)), $"Device changed at cell {i}.");
            }
        }

        Assert.Equal(ReadStatus.Done, PairPosView.TryOpen(bytes, out var view,
            out diagnostic, location));
        Assert.Equal(ReadStatus.Done, view.TryRead(glyphCount, out var reread,
            out diagnostic));
        Assert.Equal(DiagnosticCode.None, diagnostic.Code);
        Assert.Equal(new ushort[] { 1 }, reread!.Coverage.Glyphs.ToArray());
        Assert.Equal(classes, reread.ClassDef1!.Ranges.ToArray());
        Assert.Equal(classes, reread.ClassDef2!.Ranges.ToArray());
        foreach (int i in new[] { 0, 1, 2, 4021, values1.Length - 1 })
        {
            Assert.Equal(ReadStatus.Done, reread.TryGetMatrixValue(i / class2Count,
                i % class2Count, out var value1, out var value2, out diagnostic));
            Assert.Equal(values1[i].XAdvance, value1.XAdvance);
            Assert.Equal(default, value2);
            var expected = values1[i].XAdvanceDevice;
            if (expected is null)
            {
                Assert.Null(value1.XAdvanceDevice);
            }
            else
            {
                var device = Assert.IsType<StaticDeviceTable>(value1.XAdvanceDevice);
                Assert.Equal(expected.StartSize, device.StartSize);
                Assert.Equal(expected.EndSize, device.EndSize);
                Assert.Equal(expected.DeltaFormat, device.DeltaFormat);
                Assert.Equal(expected.Deltas.ToArray(), device.Deltas.ToArray());
            }
        }
    }
}
